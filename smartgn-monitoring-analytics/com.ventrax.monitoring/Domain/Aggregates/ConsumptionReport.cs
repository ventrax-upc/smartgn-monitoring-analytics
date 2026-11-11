using System;
using System.Collections.Generic;
using Com.Ventrax.Monitoring.Domain.Entities;
using Com.Ventrax.Monitoring.Domain.ValueObjects;

namespace Com.Ventrax.Monitoring.Domain.Aggregates;

public enum ReportStatus { Pending, Generated, Failed }

public class ConsumptionReport
{
    public Guid ReportId { get; init; } = Guid.NewGuid();
    public Guid AccountId { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
    public ReportStatus Status { get; private set; }
    
    public List<ReportLine> MeterRows { get; private set; } = new();
    public ConsumptionComparison? OptionalComparison { get; private set; }

    public ConsumptionReport(Guid accountId)
    {
        AccountId = accountId;
        GeneratedAt = DateTimeOffset.UtcNow;
        Status = ReportStatus.Pending;
    }

    public void AddRow(ReportLine line)
    {
        MeterRows.Add(line);
    }

    public void SetComparison(ConsumptionComparison comparison)
    {
        OptionalComparison = comparison;
    }

    public void MarkAsGenerated()
    {
        Status = ReportStatus.Generated;
    }
}