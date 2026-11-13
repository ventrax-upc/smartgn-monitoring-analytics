using Com.Ventrax.Monitoring.Domain.Aggregates;

namespace Com.Ventrax.Monitoring.Application.Dtos;

// Respuesta del reporte. Es lo que se guarda en Redis (no la entidad de EF).
public record ReportLineDto(Guid MeterId, decimal VolumeM3, decimal EstimatedCost);

public record ConsumptionReportDto(
    Guid ReportId,
    Guid AccountId,
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset GeneratedAt,
    string Status,
    string Currency,
    decimal TotalVolumeM3,
    decimal TotalEstimatedCost,
    List<ReportLineDto> MeterRows)
{
    public static ConsumptionReportDto FromDomain(
        ConsumptionReport report, DateTimeOffset from, DateTimeOffset to, string currency)
    {
        var rows = report.MeterRows
            .Select(r => new ReportLineDto(r.MeterId, r.VolumeM3, r.EstimatedCost))
            .ToList();

        return new ConsumptionReportDto(
            report.ReportId,
            report.AccountId,
            from,
            to,
            report.GeneratedAt,
            report.Status.ToString(),
            currency,
            rows.Sum(r => r.VolumeM3),
            rows.Sum(r => r.EstimatedCost),
            rows);
    }
}
