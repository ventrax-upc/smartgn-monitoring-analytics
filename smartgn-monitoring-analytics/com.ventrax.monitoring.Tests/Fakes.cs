using Com.Ventrax.Monitoring.Application.Exceptions;
using Com.Ventrax.Monitoring.Application.Interfaces;
using Com.Ventrax.Monitoring.Application.Services;
using Com.Ventrax.Monitoring.Domain.Aggregates;
using Com.Ventrax.Monitoring.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Com.Ventrax.Monitoring.Tests;

// Dobles de prueba en memoria (sin Redis, PostgreSQL, IAM, Billing ni Telemetry reales)

public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

public sealed class FakeTelemetry : ITelemetryGrpcClient
{
    public List<TelemetryReadingDto> Readings { get; } = [];
    public int BatchCalls { get; private set; }
    public int LatestCalls { get; private set; }

    public void AddCumulative(Guid meterId, DateTimeOffset at, decimal totalM3, bool delayed = false) =>
        Readings.Add(new TelemetryReadingDto(meterId, at, totalM3, 2.1, false, true, delayed));

    public Task<IReadOnlyList<TelemetryReadingDto>> GetReadingsBatchAsync(
        IReadOnlyCollection<Guid> meterIds, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        BatchCalls++;
        IReadOnlyList<TelemetryReadingDto> result = Readings
            .Where(r => meterIds.Contains(r.MeterId) && r.MeasuredAt >= from && r.MeasuredAt < to)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<TelemetryReadingDto>> GetLatestReadingsAsync(
        IReadOnlyCollection<Guid> meterIds, CancellationToken ct = default)
    {
        LatestCalls++;
        IReadOnlyList<TelemetryReadingDto> result = Readings
            .Where(r => meterIds.Contains(r.MeterId))
            .GroupBy(r => r.MeterId)
            .Select(g => g.OrderByDescending(r => r.MeasuredAt).First())
            .ToList();
        return Task.FromResult(result);
    }
}

public sealed class FakeBilling : IBillingGrpcClient
{
    public bool IsPro { get; set; } = true;
    public bool Unavailable { get; set; }

    public Task<bool> HasProEntitlementAsync(Guid accountId, CancellationToken ct = default) =>
        Unavailable
            ? throw new DependencyUnavailableException("Payments & Billing")
            : Task.FromResult(IsPro);
}

public sealed class FakeAuthorization : IMeterAuthorizationClient
{
    public HashSet<Guid> Denied { get; } = [];
    public bool Unavailable { get; set; }
    public List<(Guid Account, Guid Meter, string Function)> Checks { get; } = [];

    public Task<bool> IsAllowedAsync(Guid accountId, Guid meterId, string function, CancellationToken ct = default)
    {
        lock (Checks)
        {
            Checks.Add((accountId, meterId, function));
        }

        return Unavailable
            ? throw new DependencyUnavailableException("IAM")
            : Task.FromResult(!Denied.Contains(meterId));
    }
}

public sealed class FakeRepository : IMonitoringRepository
{
    public List<ConsumptionReport> Saved { get; } = [];

    public Task SaveReportAsync(ConsumptionReport report)
    {
        Saved.Add(report);
        return Task.CompletedTask;
    }

    public Task<ConsumptionReport?> GetReportAsync(Guid reportId) =>
        Task.FromResult(Saved.FirstOrDefault(r => r.ReportId == reportId));
}

// Simula Redis caído: toda operación lanza excepción
public sealed class BrokenCache : IDistributedCache
{
    private static Exception Down() => new InvalidOperationException("redis down");

    public byte[]? Get(string key) => throw Down();
    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw Down();
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw Down();
    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw Down();
    public void Refresh(string key) => throw Down();
    public Task RefreshAsync(string key, CancellationToken token = default) => throw Down();
    public void Remove(string key) => throw Down();
    public Task RemoveAsync(string key, CancellationToken token = default) => throw Down();
}

// Arma los servicios con dobles; comparte la caché si se pasa la misma instancia
public sealed class TestHost
{
    public static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    public FakeTelemetry Telemetry { get; } = new();
    public FakeBilling Billing { get; } = new();
    public FakeAuthorization Authorization { get; } = new();
    public FakeRepository Repository { get; } = new();
    public CacheMetrics Metrics { get; } = new();
    public IDistributedCache Cache { get; }

    public TestHost(IDistributedCache? cache = null)
    {
        Cache = cache ?? new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
    }

    private RedisAnalyticsCache AnalyticsCache() =>
        new(Cache, Metrics, NullLogger<RedisAnalyticsCache>.Instance);

    private ConsumptionAggregationService Aggregation() =>
        new(Options.Create(new TariffOptions { PricePerM3 = 2.50m, Currency = "PEN" }));

    public ConsumptionQueryService Queries() => new(
        new MeterAccessGuard(Authorization),
        Telemetry,
        Aggregation(),
        AnalyticsCache(),
        Options.Create(new CacheOptions()),
        new FixedClock(Now));

    public ReportService Reports() => new(
        Repository,
        Telemetry,
        Billing,
        new MeterAccessGuard(Authorization),
        Aggregation(),
        AnalyticsCache(),
        Options.Create(new CacheOptions()),
        new FixedClock(Now));
}
