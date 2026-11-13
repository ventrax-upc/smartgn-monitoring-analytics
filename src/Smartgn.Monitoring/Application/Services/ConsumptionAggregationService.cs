using Com.Ventrax.Monitoring.Application.Dtos;
using Com.Ventrax.Monitoring.Application.Interfaces;
using Com.Ventrax.Monitoring.Domain.Services;
using Microsoft.Extensions.Options;

namespace Com.Ventrax.Monitoring.Application.Services;

// Consumption Aggregation Service + Expense Estimation (componentes del informe).
// Convierte lecturas acumuladas en consumo por medidor y gasto estimado.
public class ConsumptionAggregationService
{
    private readonly TariffOptions _tariff;

    public ConsumptionAggregationService(IOptions<TariffOptions> tariff)
    {
        _tariff = tariff.Value;
    }

    public TariffOptions Tariff => _tariff;

    public List<MeterConsumptionDto> Aggregate(
        IReadOnlyCollection<Guid> meterIds,
        IReadOnlyList<TelemetryReadingDto> readings,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var byMeter = readings
            .Where(r => r.TotalVolumeM3.HasValue)
            .GroupBy(r => r.MeterId)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.MeasuredAt).ToList());

        return meterIds
            .Select(meterId => ForMeter(meterId, byMeter.GetValueOrDefault(meterId) ?? [], from, to))
            .ToList();
    }

    private MeterConsumptionDto ForMeter(
        Guid meterId, List<TelemetryReadingDto> ordered, DateTimeOffset from, DateTimeOffset to)
    {
        var volume = ConsumptionCalculator.FromCumulativeReadings(
            ordered.Select(r => new CumulativeReading(r.MeasuredAt, r.TotalVolumeM3!.Value)));

        var monthlyM3 = ExpenseEstimator.MonthlyProjectionM3(volume, from, to);
        var last = ordered.Count > 0 ? ordered[^1] : null;

        return new MeterConsumptionDto(
            meterId,
            volume,
            ExpenseEstimator.Cost(volume, _tariff.PricePerM3),
            ordered.Count,
            ordered.Count > 0 ? ordered[0].MeasuredAt : null,
            last?.MeasuredAt,
            Stale: last is null || last.Delayed,
            monthlyM3,
            monthlyM3 is null ? null : ExpenseEstimator.Cost(monthlyM3.Value, _tariff.PricePerM3));
    }
}
