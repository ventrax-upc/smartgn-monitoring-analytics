using System;
using System.Collections.Generic;

namespace Com.Ventrax.Monitoring.Domain.ValueObjects;

// Representa el estado de actualización de los datos del medidor
public record DataFreshness(
    DateTimeOffset? LastValidReadingAt, 
    bool Stale, 
    decimal CoveragePercent
);

// Instantánea de la tarifa aplicada en un momento dado
public record TariffsSnapshot(
    decimal PricePerM3, 
    string Currency, 
    DateTimeOffset EffectiveAt
);

// Estimación monetaria calculada a partir del volumen y la tarifa
public record ExpenseEstimate(
    decimal EstimatedAmount, 
    string Currency, 
    DateTimeOffset CalculatedAt
);

// Rango de fechas simple para comparaciones
public record DateRange(DateTimeOffset Start, DateTimeOffset End);

// Comparación de consumo entre dos periodos o medidores
public record ConsumptionComparison(
    List<Guid> MeterIds, 
    DateRange PeriodA, 
    DateRange PeriodB, 
    decimal DeltaM3
);