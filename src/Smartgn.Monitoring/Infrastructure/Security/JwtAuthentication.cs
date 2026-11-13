using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Com.Ventrax.Monitoring.Infrastructure.Security;

// Valida el JWT emitido por IAM, con las mismas reglas que los demás servicios
// (issuer smartgn-iam-service, audience smartgn-api, HS256 con JWT_SECRET, sub == accountId).
// El accountId SIEMPRE sale del token: no se confía en un id enviado por el cliente.
public static class JwtAuthentication
{
    public const string Issuer = "smartgn-iam-service";
    public const string Audience = "smartgn-api";
    public const string AccountIdClaim = "accountId";

    // HS256 exige una clave de al menos 256 bits
    private const int MinimumSecretBytes = 32;

    public static IServiceCollection AddSmartGnJwt(this IServiceCollection services, IConfiguration configuration)
    {
        var secret = configuration["Jwt:Secret"] ?? configuration["JWT_SECRET"];
        if (string.IsNullOrEmpty(secret) || Encoding.UTF8.GetByteCount(secret) < MinimumSecretBytes)
        {
            throw new InvalidOperationException(
                "Jwt:Secret (or JWT_SECRET) must be configured with at least 32 bytes, the same value used by IAM.");
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = Issuer,
                    ValidateAudience = true,
                    ValidAudience = Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(60),
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (TryGetAccountId(context.Principal) is null)
                        {
                            context.Fail("Token subject and accountId claim must match.");
                        }
                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorization();
        return services;
    }

    // Devuelve el accountId solo si "sub" y "accountId" coinciden y son GUID válidos.
    public static Guid? TryGetAccountId(ClaimsPrincipal? principal)
    {
        var subject = principal?.FindFirst("sub")?.Value;
        var accountId = principal?.FindFirst(AccountIdClaim)?.Value;
        if (subject is null || !string.Equals(subject, accountId, StringComparison.Ordinal))
        {
            return null;
        }

        return Guid.TryParse(accountId, out var id) ? id : null;
    }
}
