using Com.Ventrax.Monitoring.Application.Dtos;
using Com.Ventrax.Monitoring.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace Com.Ventrax.Monitoring.Application.Services;

// Consultas de consumo: lectura en vivo y consumo consolidado de varios medidores.
public class ConsumptionQueryService
{
    private const string ConsumptionCacheKind = "consumption";

    private readonly MeterAccessGuard _guard;
    private readonly ITelemetryGrpcClient _telemetry;
    private readonly ConsumptionAggregationService _aggregation;
    private readonly IAnalyticsCache _cache;
    private readonly CacheOptions _cacheOptions;
    private readonly TimeProvider _clock;

    public ConsumptionQueryService(
        MeterAccessGuard guard,
        ITelemetryGrpcClient telemetry,
        ConsumptionAggregationService aggregation,
        IAnalyticsCache cache,
        IOptions<CacheOptions> cacheOptions,
        TimeProvider clock)
    {
        _guard = guard;
        _telemetry = telemetry;
        _aggregation = aggregation;
        _cache = cache;
        _cacheOptions = cacheOptions.Value;
        _clock = clock;
    }

    // GET /api/v1/analytics/live?meterId= — sin caché: debe reflejar la lectura más reciente (≤5 s, US12)
    public async Task<LiveReadingDto> GetLiveAsync(Guid accountId, Guid meterId, CancellationToken ct = default)
    {
        var meters = AnalyticsQueryRules.ValidateMeters([meterId]);
        await _guard.EnsureAllowedAsync(accountId, meters, MeterFunctions.ViewConsumption, ct);

        var latest = (await _telemetry.GetLatestReadingsAsync(accountId, meters, ct))
            .Where(r => r.MeterId == meterId)
            .OrderByDescending(r => r.MeasuredAt)
            .FirstOrDefault();

        if (latest is null)
        {
            return new LiveReadingDto(meterId, null, null, null, null, null, "no_data");
        }

        return new LiveReadingDto(
            meterId,
            latest.MeasuredAt,
            latest.TotalVolumeM3,
            latest.PressureKpa,
            latest.GasDetected,
            latest.ValveOpen,
            latest.Delayed ? "delayed" : "current");
    }

    // GET /api/v1/analytics/consumption?meterIds=&from=&to= — Cache-Aside
    public async Task<CachedResult<ConsumptionResponseDto>> GetConsumptionAsync(
        Guid accountId,
        IEnumerable<Guid> meterIds,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        var meters = AnalyticsQueryRules.ValidateMeters(meterIds);
        var (defaultFrom, defaultTo) = AnalyticsQueryRules.DefaultPeriod(now);
        var periodFrom = from ?? defaultFrom;
        var periodTo = to ?? defaultTo;
        AnalyticsQueryRules.ValidatePeriod(periodFrom, periodTo, now);

        // Permisos primero: la caché nunca reemplaza la autorización
        var function = periodTo < now - TimeSpan.FromMinutes(_cacheOptions.RecentWindowMinutes)
            ? MeterFunctions.ViewHistory
            : MeterFunctions.ViewConsumption;
        await _guard.EnsureAllowedAsync(accountId, meters, function, ct);

        var key = AnalyticsCacheKey.For(ConsumptionCacheKind, accountId, meters, periodFrom, periodTo);
        var cached = await _cache.GetAsync<ConsumptionResponseDto>(key, ct);
        if (cached is not null)
        {
            return new CachedResult<ConsumptionResponseDto>(cached, FromCache: true);
        }

        var readings = await _telemetry.GetReadingsBatchAsync(accountId, meters, periodFrom, periodTo, ct);
        var rows = _aggregation.Aggregate(meters, readings, periodFrom, periodTo);

        var response = new ConsumptionResponseDto(
            accountId,
            periodFrom,
            periodTo,
            now,
            _aggregation.Tariff.Currency,
            _aggregation.Tariff.PricePerM3,
            rows.Sum(r => r.VolumeM3),
            rows.Sum(r => r.EstimatedCost),
            rows);

        await _cache.SetAsync(key, response, CacheTtl.For(periodTo, now, _cacheOptions), ct);
        return new CachedResult<ConsumptionResponseDto>(response, FromCache: false);
    }
}

// TTL corto si el periodo incluye datos recientes; más largo si es histórico.
public static class CacheTtl
{
    public static TimeSpan For(DateTimeOffset periodEnd, DateTimeOffset now, CacheOptions options) =>
        periodEnd >= now.AddMinutes(-options.RecentWindowMinutes)
            ? TimeSpan.FromSeconds(options.RecentReportTtlSeconds)
            : TimeSpan.FromSeconds(options.HistoricalReportTtlSeconds);
}
