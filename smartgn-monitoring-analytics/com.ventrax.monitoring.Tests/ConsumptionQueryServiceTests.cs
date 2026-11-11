using Com.Ventrax.Monitoring.Application.Exceptions;
using Com.Ventrax.Monitoring.Application.Services;
using Xunit;

namespace Com.Ventrax.Monitoring.Tests;

public class ConsumptionQueryServiceTests
{
    private static readonly Guid Account = Guid.NewGuid();
    private static readonly Guid MeterA = Guid.NewGuid();
    private static readonly Guid MeterB = Guid.NewGuid();
    private static readonly DateTimeOffset October = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    // ---------- GET /live ----------

    [Fact]
    public async Task Live_ReturnsLatestReadingWithSafetyMetrics()
    {
        var host = new TestHost();
        host.Telemetry.AddCumulative(MeterA, TestHost.Now.AddMinutes(-5), 100m);
        host.Telemetry.AddCumulative(MeterA, TestHost.Now.AddSeconds(-2), 101.5m);

        var live = await host.Queries().GetLiveAsync(Account, MeterA);

        Assert.Equal(101.5m, live.TotalVolumeM3);
        Assert.Equal(2.1, live.PressureKpa);
        Assert.False(live.GasDetected);
        Assert.True(live.ValveOpen);
        Assert.Equal("current", live.Freshness);
        Assert.Contains(host.Authorization.Checks, c => c.Meter == MeterA && c.Function == MeterFunctions.ViewConsumption);
    }

    [Fact]
    public async Task Live_WithoutReadings_ReportsNoData()
    {
        var host = new TestHost();

        var live = await host.Queries().GetLiveAsync(Account, MeterA);

        Assert.Equal("no_data", live.Freshness);
        Assert.Null(live.MeasuredAt);
    }

    [Fact]
    public async Task Live_DelayedReading_IsFlagged()
    {
        var host = new TestHost();
        host.Telemetry.AddCumulative(MeterA, TestHost.Now.AddMinutes(-3), 100m, delayed: true);

        var live = await host.Queries().GetLiveAsync(Account, MeterA);

        Assert.Equal("delayed", live.Freshness);
    }

    [Fact]
    public async Task Live_MeterWithoutGrant_IsDenied_AndTelemetryIsNotQueried()
    {
        var host = new TestHost();
        host.Authorization.Denied.Add(MeterA);

        await Assert.ThrowsAsync<MeterAccessDeniedException>(() => host.Queries().GetLiveAsync(Account, MeterA));
        Assert.Equal(0, host.Telemetry.LatestCalls);
    }

    // ---------- GET /consumption ----------

    [Fact]
    public async Task Consumption_PerMeter_UsesCumulativeDifference_CostAndMonthlyEstimate()
    {
        var host = new TestHost();
        host.Telemetry.AddCumulative(MeterA, October.AddDays(1), 100m);
        host.Telemetry.AddCumulative(MeterA, October.AddDays(5), 106m);
        host.Telemetry.AddCumulative(MeterA, October.AddDays(9), 110m);
        host.Telemetry.AddCumulative(MeterB, October.AddDays(2), 50m);
        host.Telemetry.AddCumulative(MeterB, October.AddDays(9), 54m);

        var result = await host.Queries().GetConsumptionAsync(Account, [MeterA, MeterB], October, October.AddDays(10));

        var a = result.Value.Meters.Single(m => m.MeterId == MeterA);
        var b = result.Value.Meters.Single(m => m.MeterId == MeterB);
        Assert.Equal(10m, a.VolumeM3);
        Assert.Equal(25.00m, a.EstimatedCost);
        Assert.Equal(31m, a.MonthlyEstimateM3);   // 10 m3 / 10 días x 31 días de octubre
        Assert.Equal(77.50m, a.MonthlyEstimateCost);
        Assert.Equal(4m, b.VolumeM3);
        Assert.Equal(14m, result.Value.TotalVolumeM3);
        Assert.Equal(35.00m, result.Value.TotalEstimatedCost);
        Assert.Equal("PEN", result.Value.Currency);
    }

    [Fact]
    public async Task Consumption_MeterWithoutReadings_IsZeroAndStale()
    {
        var host = new TestHost();

        var result = await host.Queries().GetConsumptionAsync(Account, [MeterA], October, October.AddDays(3));

        var row = Assert.Single(result.Value.Meters);
        Assert.Equal(0m, row.VolumeM3);
        Assert.True(row.Stale);
        Assert.Equal(0, row.ReadingsCount);
    }

    [Fact]
    public async Task Consumption_DefaultsToCurrentMonth()
    {
        var host = new TestHost();

        var result = await host.Queries().GetConsumptionAsync(Account, [MeterA], null, null);

        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), result.Value.From);
        Assert.Equal(TestHost.Now, result.Value.To);
    }

    [Fact]
    public async Task Consumption_SecondIdenticalQuery_IsServedFromCache()
    {
        var host = new TestHost();
        host.Telemetry.AddCumulative(MeterA, October.AddDays(1), 1m);
        var service = host.Queries();

        var first = await service.GetConsumptionAsync(Account, [MeterA, MeterB], October, October.AddDays(5));
        var second = await service.GetConsumptionAsync(Account, [MeterB, MeterA], October, October.AddDays(5));

        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.Equal(1, host.Telemetry.BatchCalls);
        Assert.Equal(1, host.Metrics.Hits);
        Assert.Equal(1, host.Metrics.Misses);
    }

    [Fact]
    public async Task Consumption_AuthorizationIsCheckedEvenWhenCached()
    {
        var host = new TestHost();
        var service = host.Queries();
        await service.GetConsumptionAsync(Account, [MeterA], October, October.AddDays(5));

        host.Authorization.Denied.Add(MeterA); // se revocó el permiso

        await Assert.ThrowsAsync<MeterAccessDeniedException>(() =>
            service.GetConsumptionAsync(Account, [MeterA], October, October.AddDays(5)));
    }

    [Fact]
    public async Task Consumption_HistoricalPeriod_RequiresViewHistory()
    {
        var host = new TestHost();

        await host.Queries().GetConsumptionAsync(Account, [MeterA], October, October.AddDays(5));

        Assert.All(host.Authorization.Checks, c => Assert.Equal(MeterFunctions.ViewHistory, c.Function));
    }

    [Fact]
    public async Task Consumption_IamUnavailable_IsNotTreatedAsAllowed()
    {
        var host = new TestHost();
        host.Authorization.Unavailable = true;

        await Assert.ThrowsAsync<DependencyUnavailableException>(() =>
            host.Queries().GetConsumptionAsync(Account, [MeterA], October, October.AddDays(5)));
        Assert.Equal(0, host.Telemetry.BatchCalls);
    }

    [Fact]
    public async Task Consumption_RedisDown_StillAnswersFromSource()
    {
        var host = new TestHost(new BrokenCache());
        host.Telemetry.AddCumulative(MeterA, October.AddDays(1), 1m);
        host.Telemetry.AddCumulative(MeterA, October.AddDays(2), 3m);

        var result = await host.Queries().GetConsumptionAsync(Account, [MeterA], October, October.AddDays(5));

        Assert.False(result.FromCache);
        Assert.Equal(2m, result.Value.TotalVolumeM3);
        Assert.Equal(2, host.Metrics.Errors); // falló la lectura y la escritura en Redis
    }
}
