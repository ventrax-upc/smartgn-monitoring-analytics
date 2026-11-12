using Com.Ventrax.Monitoring.Application.Interfaces;
using Com.Ventrax.Monitoring.Application.Services;
using Com.Ventrax.Monitoring.Infrastructure.Caching;
using Com.Ventrax.Monitoring.Infrastructure.Clients;
using Com.Ventrax.Monitoring.Infrastructure.Clients.Billing;
using Com.Ventrax.Monitoring.Infrastructure.Clients.Telemetry;
using Com.Ventrax.Monitoring.Infrastructure.OpenApi;
using Com.Ventrax.Monitoring.Infrastructure.Persistence;
using Com.Ventrax.Monitoring.Infrastructure.Security;
using Com.Ventrax.Monitoring.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
await RemoteServiceBootstrap.LoadConfigServerAsync(builder.Configuration);

// Variables de entorno compartidas con el resto de la composición SmartGN
builder.Configuration.AddInMemoryCollection(EnvironmentAliases.From(builder.Configuration));

// 1. Configurar Base de Datos (PostgreSQL)
builder.Services.AddDbContext<MonitoringDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// 2. Configurar Caché (Redis) para agregaciones rápidas — Cache-Aside
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("RedisConnection");
    options.InstanceName = "Monitoring_";
});
builder.Services.Configure<CacheOptions>(builder.Configuration.GetSection(CacheOptions.SectionName));
builder.Services.AddSingleton<CacheMetrics>();
builder.Services.AddScoped<IAnalyticsCache, RedisAnalyticsCache>();

// 3. Opciones de negocio e integración
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<TariffOptions>(builder.Configuration.GetSection(TariffOptions.SectionName));
builder.Services.Configure<IntegrationOptions>(builder.Configuration.GetSection(IntegrationOptions.SectionName));
builder.Services.Configure<TelemetryMetricsOptions>(builder.Configuration.GetSection(TelemetryMetricsOptions.SectionName));
var integration = builder.Configuration.GetSection(IntegrationOptions.SectionName).Get<IntegrationOptions>() ?? new IntegrationOptions();

// 4. Clientes gRPC (Telemetry y Billing) y HTTP interno (IAM)
builder.Services.AddGrpcClient<TelemetryQueryService.TelemetryQueryServiceClient>(o =>
{
    o.Address = new Uri(builder.Configuration["GrpcServers:Telemetry"] ?? "http://localhost:9084");
});

builder.Services.AddGrpcClient<SubscriptionEntitlementService.SubscriptionEntitlementServiceClient>(o =>
{
    o.Address = new Uri(builder.Configuration["GrpcServers:Billing"] ?? "http://localhost:9083");
});

builder.Services.AddHttpClient<IMeterAuthorizationClient, IamMeterAuthorizationClient>(client =>
{
    client.BaseAddress = new Uri(integration.IamBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(integration.RequestTimeoutSeconds);
});

// 5. Inyección de Dependencias (Puertos y Adaptadores)
builder.Services.AddScoped<ITelemetryGrpcClient, TelemetryGrpcClient>();
builder.Services.AddScoped<IBillingGrpcClient, BillingGrpcClient>();
builder.Services.AddScoped<IMonitoringRepository, MonitoringRepository>();
builder.Services.AddScoped<MeterAccessGuard>();
builder.Services.AddScoped<ConsumptionAggregationService>();
builder.Services.AddScoped<ConsumptionQueryService>();
builder.Services.AddScoped<ReportService>();

// 6. Seguridad: JWT emitido por IAM (mismas reglas que los demás microservicios)
builder.Services.AddSmartGnJwt(builder.Configuration);

// 7. Controladores REST, errores estándar y Swagger
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddHttpClient(nameof(AdminRegistrationWorker), client =>
    client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddHostedService<AdminRegistrationWorker>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SmartGN - Monitoring & Analytics",
        Version = "v1",
        Description = "Live consumption metrics, consumption history, multi-meter analytics, simple expense estimates and Pro reports. Owns an independent PostgreSQL database, caches aggregates in Redis (Cache-Aside) and queries Telemetry and Billing over private gRPC.",
        License = new OpenApiLicense { Name = "MIT" }
    });

    c.AddSecurityDefinition("bearerAuth", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Current IAM JWT. The service verifies the active session and operation-specific meter/function permissions."
    });
    c.AddSecurityDefinition("serviceAuth", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        Description = "Private service credential, independent of user JWTs. Internal routes must remain outside public gateway routing."
    });

    c.TagActionsBy(api => new[] { api.GroupName ?? "Analytics" });
    c.DocumentFilter<MonitoringTagsDocumentFilter>();
    c.OperationFilter<BearerAuthOperationFilter>();
    c.EnableAnnotations();
});

var app = builder.Build();

// Aplica las migraciones de EF Core al iniciar (configurable con Database:MigrateOnStartup)
if (app.Configuration.GetValue("Database:MigrateOnStartup", app.Environment.IsDevelopment()))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<MonitoringDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();

// Keep the same Swagger URLs as the Spring Boot services.
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/v3/api-docs")
    {
        context.Request.Path = "/swagger/v1/swagger.json";
    }

    await next();
});
app.UseStaticFiles();
app.UseSwagger(options => options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1);
app.UseSwaggerUI(options =>
{
    options.InjectStylesheet("./smartgn.css");
    options.DocumentTitle = "SmartGN | Monitoring & Analytics";
    options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
    options.DefaultModelsExpandDepth(0);
    options.DisplayRequestDuration();
    options.SwaggerEndpoint("/v3/api-docs", "SmartGN - Monitoring & Analytics v1");
});

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/swagger-ui.html", () => Results.Redirect("/swagger/index.html"))
    .AllowAnonymous()
    .ExcludeFromDescription();

// Health check para el API Gateway / orquestador
app.MapGet("/health", () => Results.Text("UP"))
    .AllowAnonymous()
    .ExcludeFromDescription();
app.MapGet("/ready", async (MonitoringDbContext database, CancellationToken cancellationToken) =>
{
    var postgresUp = false;
    try
    {
        postgresUp = await database.Database.CanConnectAsync(cancellationToken);
    }
    catch (Exception exception)
    {
        app.Logger.LogWarning(exception, "PostgreSQL readiness check failed.");
    }

    return Results.Json(
        new { status = postgresUp ? "UP" : "DOWN", postgres = postgresUp ? "UP" : "DOWN" },
        statusCode: postgresUp ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
})
    .AllowAnonymous()
    .ExcludeFromDescription();

app.Run();

// Permite usar las mismas variables de entorno que los demás servicios
// (JWT_SECRET, INTERNAL_SERVICE_TOKEN, IAM_BASE_URL, TELEMETRY_GRPC_URL, BILLING_GRPC_URL).
// Las cadenas de conexión usan las variables estándar de .NET:
// ConnectionStrings__DefaultConnection y ConnectionStrings__RedisConnection.
internal static class EnvironmentAliases
{
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["JWT_SECRET"] = "Jwt:Secret",
        ["INTERNAL_SERVICE_TOKEN"] = "Integration:InternalServiceToken",
        ["IAM_BASE_URL"] = "Integration:IamBaseUrl",
        ["TELEMETRY_GRPC_URL"] = "GrpcServers:Telemetry",
        ["BILLING_GRPC_URL"] = "GrpcServers:Billing",
    };

    public static IEnumerable<KeyValuePair<string, string?>> From(IConfiguration configuration) =>
        Aliases
            .Where(alias => !string.IsNullOrEmpty(configuration[alias.Key]))
            .Select(alias => new KeyValuePair<string, string?>(alias.Value, configuration[alias.Key]));
}
