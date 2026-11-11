using Com.Ventrax.Monitoring.Application.Services;
using Com.Ventrax.Monitoring.Domain.Services;
using Xunit;

namespace Com.Ventrax.Monitoring.Tests;

public class ConsumptionCalculatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private static CumulativeReading At(int hours, decimal total) => new(T0.AddHours(hours), total);

    [Fact]
    public void Consumption_IsDifferenceOfCumulativeReadings_NotTheirSum()
    {
        var consumed = ConsumptionCalculator.FromCumulativeReadings([At(0, 100m), At(1, 103m), At(2, 110m)]);

        Assert.Equal(10m, consumed); // 110 - 100, no 313
    }

    [Fact]
    public void OutOfOrderReadings_AreSortedByMeasuredAt()
    {
        var consumed = ConsumptionCalculator.FromCumulativeReadings([At(2, 110m), At(0, 100m), At(1, 103m)]);

        Assert.Equal(10m, consumed);
    }

    [Fact]
    public void CounterReset_StartsANewBaseline()
    {
        // 100 -> 105 (+5), reinicio a 0, 0 -> 4 (+4)
        var consumed = ConsumptionCalculator.FromCumulativeReadings([At(0, 100m), At(1, 105m), At(2, 0m), At(3, 4m)]);

        Assert.Equal(9m, consumed);
    }

    [Fact]
    public void OneOrNoReading_MeansNoMeasurableConsumption()
    {
        Assert.Equal(0m, ConsumptionCalculator.FromCumulativeReadings([]));
        Assert.Equal(0m, ConsumptionCalculator.FromCumulativeReadings([At(0, 50m)]));
    }
}

public class ExpenseEstimatorTests
{
    [Fact]
    public void Cost_IsVolumeTimesTariff_RoundedToCents()
    {
        Assert.Equal(25.83m, ExpenseEstimator.Cost(10.333m, 2.50m));
    }

    [Fact]
    public void MonthlyProjection_UsesDaysElapsedAndDaysInMonth()
    {
        var from = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(10);

        // 10 m3 en 10 días -> 1 m3/día x 30 días de noviembre
        Assert.Equal(30m, ExpenseEstimator.MonthlyProjectionM3(10m, from, to));
    }

    [Fact]
    public void MonthlyProjection_NeedsAtLeastOneDayOfData()
    {
        var from = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.Null(ExpenseEstimator.MonthlyProjectionM3(1m, from, from.AddHours(5)));
    }
}

public class AnalyticsQueryRulesTests
{
    private static readonly DateTimeOffset Now = TestHost.Now;

    [Fact]
    public void Meters_AreRequired_Deduplicated_AndLimitedTo100()
    {
        var id = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => AnalyticsQueryRules.ValidateMeters([]));
        Assert.Single(AnalyticsQueryRules.ValidateMeters([id, id]));
        Assert.Throws<ArgumentException>(() =>
            AnalyticsQueryRules.ValidateMeters(Enumerable.Range(0, 101).Select(_ => Guid.NewGuid())));
    }

    [Fact]
    public void Period_MustBeOrdered_NotInFuture_AndAtMost31Days()
    {
        Assert.Throws<ArgumentException>(() => AnalyticsQueryRules.ValidatePeriod(Now, Now.AddDays(-1), Now));
        Assert.Throws<ArgumentException>(() => AnalyticsQueryRules.ValidatePeriod(Now.AddDays(-1), Now.AddHours(1), Now));
        Assert.Throws<ArgumentException>(() => AnalyticsQueryRules.ValidatePeriod(Now.AddDays(-40), Now, Now));

        AnalyticsQueryRules.ValidatePeriod(Now.AddDays(-30), Now, Now);
    }
}
