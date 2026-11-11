namespace Com.Ventrax.Monitoring.Domain.Services;

// Estimación simple de gasto (US13) y proyección mensual (US17).
public static class ExpenseEstimator
{
    // Se necesita al menos un día de datos para proyectar el mes.
    public static readonly TimeSpan MinimumPeriodForProjection = TimeSpan.FromDays(1);

    public static decimal Cost(decimal volumeM3, decimal pricePerM3) =>
        Math.Round(volumeM3 * pricePerM3, 2, MidpointRounding.AwayFromZero);

    // Consumo acumulado / días transcurridos x días del mes de "to".
    public static decimal? MonthlyProjectionM3(decimal volumeM3, DateTimeOffset from, DateTimeOffset to)
    {
        var elapsed = to - from;
        if (elapsed < MinimumPeriodForProjection)
        {
            return null;
        }

        var daysInMonth = DateTime.DaysInMonth(to.Year, to.Month);
        var perDay = volumeM3 / (decimal)elapsed.TotalDays;
        return Math.Round(perDay * daysInMonth, 3, MidpointRounding.AwayFromZero);
    }
}
