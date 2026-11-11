using Com.Ventrax.Monitoring.Application.Dtos;
using Com.Ventrax.Monitoring.Application.Exceptions;
using Com.Ventrax.Monitoring.Application.Services;
using Com.Ventrax.Monitoring.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Com.Ventrax.Monitoring.Interfaces;

[ApiController]
[Authorize]
[Route("api/v1/analytics")]
[Produces("application/json")]
[SwaggerTag("Analytics: authorized live readings, consumption and Pro reports. The account always comes from the IAM JWT.")]
public class MonitoringController : ControllerBase
{
    private readonly ConsumptionQueryService _consumption;
    private readonly ReportService _reportService;
    private readonly ILogger<MonitoringController> _logger;

    public MonitoringController(
        ConsumptionQueryService consumption,
        ReportService reportService,
        ILogger<MonitoringController> logger)
    {
        _consumption = consumption;
        _reportService = reportService;
        _logger = logger;
    }

    [HttpGet("live")]
    [SwaggerOperation(
        Summary = "Latest reading of an authorized meter",
        Description = "Returns the most recent reading (volume, pressure, gas presence, valve state) and its freshness. Requires VIEW_CONSUMPTION on the meter. Not cached.")]
    [SwaggerResponse(200, "Latest reading", typeof(LiveReadingDto))]
    [SwaggerResponse(400, "Invalid meterId")]
    [SwaggerResponse(401, "Missing or invalid IAM token")]
    [SwaggerResponse(403, "No current grant on the meter")]
    [SwaggerResponse(503, "IAM or Telemetry temporarily unavailable")]
    public Task<IActionResult> GetLive(
        [FromQuery, SwaggerParameter("Meter identifier", Required = true)] Guid meterId,
        CancellationToken ct) =>
        Execute(async account => Ok(await _consumption.GetLiveAsync(account, meterId, ct)));

    [HttpGet("consumption")]
    [SwaggerOperation(
        Summary = "Consolidated consumption of authorized meters",
        Description = "Consumption (difference of cumulative readings), estimated cost and simple monthly estimate per meter. Defaults to the current month. Cache-Aside with Redis (header X-Cache: HIT or MISS).")]
    [SwaggerResponse(200, "Consumption per meter", typeof(ConsumptionResponseDto))]
    [SwaggerResponse(400, "Invalid meters or period")]
    [SwaggerResponse(401, "Missing or invalid IAM token")]
    [SwaggerResponse(403, "No current grant on one of the meters")]
    [SwaggerResponse(503, "IAM or Telemetry temporarily unavailable")]
    public Task<IActionResult> GetConsumption(
        [FromQuery, SwaggerParameter("Meter identifiers (max 100)", Required = true)] List<Guid> meterIds,
        [FromQuery, SwaggerParameter("Period start (ISO 8601). Default: first day of the current month")] DateTimeOffset? from,
        [FromQuery, SwaggerParameter("Period end (ISO 8601). Default: now")] DateTimeOffset? to,
        CancellationToken ct) =>
        Execute(async account =>
        {
            var result = await _consumption.GetConsumptionAsync(account, meterIds, from, to, ct);
            return Cached(result);
        });

    [HttpGet("reports")]
    [SwaggerOperation(
        Summary = "Generate consolidated consumption report (Pro)",
        Description = "Requires USE_REPORTS on every meter and an active Pro subscription confirmed by Billing over gRPC. The report is stored in PostgreSQL and cached in Redis (header X-Cache).")]
    [SwaggerResponse(200, "Report generated", typeof(ConsumptionReportDto))]
    [SwaggerResponse(400, "Invalid meters or period")]
    [SwaggerResponse(401, "Missing or invalid IAM token")]
    [SwaggerResponse(403, "No grant on a meter or Pro subscription required")]
    [SwaggerResponse(503, "IAM, Billing or Telemetry temporarily unavailable")]
    public Task<IActionResult> GenerateReport(
        [FromQuery, SwaggerParameter("Meter identifiers (max 100)", Required = true)] List<Guid> meterIds,
        [FromQuery, SwaggerParameter("Period start (ISO 8601)", Required = true)] DateTimeOffset from,
        [FromQuery, SwaggerParameter("Period end (ISO 8601)", Required = true)] DateTimeOffset to,
        CancellationToken ct) =>
        Execute(async account =>
        {
            var result = await _reportService.GenerateReportAsync(account, meterIds, from, to, ct);
            return Cached(result);
        });

    private IActionResult Cached<T>(CachedResult<T> result)
    {
        // Permite ver en Swagger/Postman si la respuesta vino de Redis
        Response.Headers["X-Cache"] = result.FromCache ? "HIT" : "MISS";
        return Ok(result.Value);
    }

    // Traduce errores de aplicación a respuestas HTTP sin filtrar detalles internos
    private async Task<IActionResult> Execute(Func<Guid, Task<IActionResult>> action)
    {
        if (JwtAuthentication.TryGetAccountId(User) is not { } accountId)
        {
            return Unauthorized();
        }

        try
        {
            return await action(accountId);
        }
        catch (ArgumentException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (MeterAccessDeniedException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (ProSubscriptionRequiredException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (DependencyUnavailableException ex)
        {
            _logger.LogWarning(ex, "Dependency unavailable: {Dependency}", ex.Dependency);
            Response.Headers.RetryAfter = "5";
            return Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
