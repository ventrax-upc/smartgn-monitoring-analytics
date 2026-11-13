using System;
using Com.Ventrax.Monitoring.Domain.ValueObjects;

namespace Com.Ventrax.Monitoring.Domain.Aggregates;

public class ConsumptionSummary
{
    public Guid SummaryId { get; init; } = Guid.NewGuid();
    public Guid MeterId { get; init; }
    public DateTimeOffset PeriodStart { get; init; }
    public DateTimeOffset PeriodEnd { get; init; }
    public decimal VolumeM3 { get; private set; }
    
    // Value Objects asociados
    public DataFreshness Freshness { get; private set; }
    public ExpenseEstimate? Expense { get; private set; }

    public ConsumptionSummary(Guid meterId, DateTimeOffset periodStart, DateTimeOffset periodEnd, decimal volumeM3, DataFreshness freshness)
    {
        MeterId = meterId;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        VolumeM3 = volumeM3;
        Freshness = freshness;
    }

    public void ApplyExpenseEstimate(ExpenseEstimate estimate)
    {
        Expense = estimate;
    }
}