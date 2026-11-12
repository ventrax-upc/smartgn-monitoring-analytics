using Com.Ventrax.Monitoring.Application.Exceptions;
using Com.Ventrax.Monitoring.Application.Services;
using Xunit;

namespace Com.Ventrax.Monitoring.Tests;

public class ReportServiceTests
{
    private static readonly Guid Account = Guid.NewGuid();
    private static readonly Guid MeterA = Guid.NewGuid();
    private static readonly Guid MeterB = Guid.NewGuid();
    private static readonly DateTimeOffset From = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Report_UsesCumulativeDifference_AndIsPersisted()
    {
        var host = new TestHost();
        host.Telemetry.AddCumulative(MeterA, From.AddDays(1), 200m);
        host.Telemetry.AddCumulative(MeterA, From.AddDays(20), 212m);

        var result = await host.Reports().GenerateReportAsync(Account, [MeterA], From, To);

        var row = Assert.Single(result.Value.MeterRows);
        Assert.Equal(12m, row.VolumeM3);
        Assert.Equal(30.00m, row.EstimatedCost);
        Assert.Equal("Generated", result.Value.Status);
        Assert.Single(host.Repository.Saved);
        Assert.Contains(host.Authorization.Checks, c => c.Function == MeterFunctions.UseReports);
    }

    [Fact]
    public async Task SecondIdenticalReport_IsServedFromCache_WithoutSavingAgain()
    {
        var host = new TestHost();
        var service = host.Reports();

        var first = await service.GenerateReportAsync(Account, [MeterA, MeterB], From, To);
        var second = await service.GenerateReportAsync(Account, [MeterB, MeterA], From, To);

        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.Equal(first.Value.ReportId, second.Value.ReportId);
        Assert.Equal(1, host.Telemetry.BatchCalls);
        Assert.Single(host.Repository.Saved);
    }

    [Fact]
    public async Task DifferentAccount_DoesNotReuseAnotherAccountsCache()
    {
        var host = new TestHost();
        var service = host.Reports();

        await service.GenerateReportAsync(Account, [MeterA], From, To);
        var other = await service.GenerateReportAsync(Guid.NewGuid(), [MeterA], From, To);

        Assert.False(other.FromCache);
        Assert.Equal(2, host.Telemetry.BatchCalls);
    }

    [Fact]
    public async Task Report_RequiresPro_EvenIfAlreadyCached()
    {
        var host = new TestHost();
        var service = host.Reports();
        await service.GenerateReportAsync(Account, [MeterA], From, To);

        host.Billing.IsPro = false; // venció la suscripción

        await Assert.ThrowsAsync<ProSubscriptionRequiredException>(() =>
            service.GenerateReportAsync(Account, [MeterA], From, To));
    }

    [Fact]
    public async Task BillingUnavailable_IsNotTreatedAsFree_Or_Pro()
    {
        var host = new TestHost();
        host.Billing.Unavailable = true;

        await Assert.ThrowsAsync<DependencyUnavailableException>(() =>
            host.Reports().GenerateReportAsync(Account, [MeterA], From, To));
        Assert.Equal(0, host.Telemetry.BatchCalls);
        Assert.Empty(host.Repository.Saved);
    }

    [Fact]
    public async Task MeterWithoutGrant_IsDenied()
    {
        var host = new TestHost();
        host.Authorization.Denied.Add(MeterB);

        await Assert.ThrowsAsync<MeterAccessDeniedException>(() =>
            host.Reports().GenerateReportAsync(Account, [MeterA, MeterB], From, To));
        Assert.Equal(0, host.Telemetry.BatchCalls);
    }

    [Fact]
    public async Task InvalidPeriod_IsRejected()
    {
        var host = new TestHost();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            host.Reports().GenerateReportAsync(Account, [MeterA], To, From));
    }

    [Fact]
    public async Task RedisDown_ReportIsStillCalculatedFromSource()
    {
        var host = new TestHost(new BrokenCache());

        var result = await host.Reports().GenerateReportAsync(Account, [MeterA], From, To);

        Assert.False(result.FromCache);
        Assert.Single(host.Repository.Saved);
        Assert.Equal(2, host.Metrics.Errors);
    }
}
