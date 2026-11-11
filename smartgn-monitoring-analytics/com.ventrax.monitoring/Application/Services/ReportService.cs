using Com.Ventrax.Monitoring.Application.Dtos;
using Com.Ventrax.Monitoring.Application.Exceptions;
using Com.Ventrax.Monitoring.Application.Interfaces;
using Com.Ventrax.Monitoring.Domain.Aggregates;
using Com.Ventrax.Monitoring.Domain.Entities;
using Microsoft.Extensions.Options;

namespace Com.Ventrax.Monitoring.Application.Services;

// Report Service: reportes consolidados de consumo y gasto (US18, función Pro).
public class ReportService
{
    private const string ReportCacheKind = "report";

    private readonly IMonitoringRepository _repository;
    private readonly ITelemetryGrpcClient _telemetryClient;
    private readonly IBillingGrpcClient _billingClient;
    private readonly MeterAccessGuard _guard;
    private readonly ConsumptionAggregationService _aggregation;
    private readonly IAnalyticsCache _cache;
    private readonly CacheOptions _cacheOptions;
    private readonly TimeProvider _clock;

    // Inyección de dependencias
    public ReportService(
        IMonitoringRepository repository,
        ITelemetryGrpcClient telemetryClient,
        IBillingGrpcClient billingClient,
        MeterAccessGuard guard,
        ConsumptionAggregationService aggregation,
        IAnalyticsCache cache,
        IOptions<CacheOptions> cacheOptions,
        TimeProvider clock)
    {
        _repository = repository;
        _telemetryClient = telemetryClient;
        _billingClient = billingClient;
        _guard = guard;
        _aggregation = aggregation;
        _cache = cache;
        _cacheOptions = cacheOptions.Value;
        _clock = clock;
    }

    public async Task<CachedResult<ConsumptionReportDto>> GenerateReportAsync(
        Guid accountId,
        IEnumerable<Guid> meterIds,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        var meters = AnalyticsQueryRules.ValidateMeters(meterIds);
        AnalyticsQueryRules.ValidatePeriod(from, to, now);

        // 1. Permisos por medidor (IAM) y beneficio Pro (Billing gRPC).
        //    Van ANTES de la caché: la autorización nunca depende solo de Redis.
        //    Si Billing no responde, el cliente lanza DependencyUnavailableException (503): no se asume Free.
        await _guard.EnsureAllowedAsync(accountId, meters, MeterFunctions.UseReports, ct);
        if (!await _billingClient.HasProEntitlementAsync(accountId, ct))
        {
            throw new ProSubscriptionRequiredException();
        }

        // 2. Cache-Aside: buscar primero en Redis
        var cacheKey = AnalyticsCacheKey.For(ReportCacheKind, accountId, meters, from, to);
        var cached = await _cache.GetAsync<ConsumptionReportDto>(cacheKey, ct);
        if (cached is not null)
        {
            return new CachedResult<ConsumptionReportDto>(cached, FromCache: true);
        }

        // 3. Lecturas validadas desde Telemetry (una consulta en bloque) y agregación
        var readings = await _telemetryClient.GetReadingsBatchAsync(meters, from, to, ct);
        var rows = _aggregation.Aggregate(meters, readings, from, to);

        var report = new ConsumptionReport(accountId);
        foreach (var row in rows)
        {
            report.AddRow(new ReportLine(row.MeterId, row.VolumeM3, row.EstimatedCost));
        }
        report.MarkAsGenerated();

        // 4. Persistir el reporte en PostgreSQL (fuente de verdad)
        await _repository.SaveReportAsync(report);

        // 5. Guardar en Redis con TTL según el periodo
        var dto = ConsumptionReportDto.FromDomain(report, from, to, _aggregation.Tariff.Currency);
        await _cache.SetAsync(cacheKey, dto, CacheTtl.For(to, now, _cacheOptions), ct);

        return new CachedResult<ConsumptionReportDto>(dto, FromCache: false);
    }
}
