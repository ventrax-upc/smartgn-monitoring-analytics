using System;

namespace Com.Ventrax.Monitoring.Domain.Entities;

public class ReportLine
{
    public Guid MeterId { get; init; }
    public decimal VolumeM3 { get; init; }
    public decimal EstimatedCost { get; init; }

    public ReportLine(Guid meterId, decimal volumeM3, decimal estimatedCost)
    {
        MeterId = meterId;
        VolumeM3 = volumeM3;
        EstimatedCost = estimatedCost;
    }
}