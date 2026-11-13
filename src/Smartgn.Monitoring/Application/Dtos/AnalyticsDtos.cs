namespace Com.Ventrax.Monitoring.Application.Dtos;

// GET /api/v1/analytics/live — estado actual de un medidor (US11, US12, US19, US24)
public record LiveReadingDto(
    Guid MeterId,
    DateTimeOffset? MeasuredAt,
    decimal? TotalVolumeM3,
    double? PressureKpa,
    bool? GasDetected,
    bool? ValveOpen,
    string Freshness); // current | delayed | no_data

// Consumo de un medidor en el periodo (US13, US14, US17)
public record MeterConsumptionDto(
    Guid MeterId,
    decimal VolumeM3,
    decimal EstimatedCost,
    int ReadingsCount,
    DateTimeOffset? FirstReadingAt,
    DateTimeOffset? LastReadingAt,
    bool Stale,
    decimal? MonthlyEstimateM3,
    decimal? MonthlyEstimateCost);

// GET /api/v1/analytics/consumption — vista consolidada multi-medidor (US10)
public record ConsumptionResponseDto(
    Guid AccountId,
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset CalculatedAt,
    string Currency,
    decimal PricePerM3,
    decimal TotalVolumeM3,
    decimal TotalEstimatedCost,
    List<MeterConsumptionDto> Meters);

// Resultado con indicador de caché (para el header X-Cache)
public record CachedResult<T>(T Value, bool FromCache);
