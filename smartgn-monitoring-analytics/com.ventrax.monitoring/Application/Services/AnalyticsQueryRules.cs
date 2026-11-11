namespace Com.Ventrax.Monitoring.Application.Services;

// Validaciones de entrada de las consultas (400 Bad Request si fallan).
public static class AnalyticsQueryRules
{
    // QAS8: consulta consolidada de hasta 100 medidores
    public const int MaxMetersPerQuery = 100;

    // Telemetry conserva el detalle 30 días (QAS16); se acepta hasta 31 para cubrir un mes completo
    public static readonly TimeSpan MaxPeriod = TimeSpan.FromDays(31);

    // Tolerancia de reloj para "to" en el futuro
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    public static IReadOnlyList<Guid> ValidateMeters(IEnumerable<Guid>? meterIds)
    {
        var distinct = (meterIds ?? Enumerable.Empty<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (distinct.Count == 0)
        {
            throw new ArgumentException("At least one meterId is required.");
        }

        if (distinct.Count > MaxMetersPerQuery)
        {
            throw new ArgumentException($"A maximum of {MaxMetersPerQuery} meters can be queried at once.");
        }

        return distinct;
    }

    public static void ValidatePeriod(DateTimeOffset from, DateTimeOffset to, DateTimeOffset now)
    {
        if (from >= to)
        {
            throw new ArgumentException("'from' must be earlier than 'to'.");
        }

        if (to > now + FutureTolerance)
        {
            throw new ArgumentException("'to' cannot be in the future.");
        }

        if (to - from > MaxPeriod)
        {
            throw new ArgumentException($"The period cannot exceed {MaxPeriod.TotalDays} days.");
        }
    }

    // Por defecto: desde el inicio del mes actual (UTC) hasta ahora
    public static (DateTimeOffset From, DateTimeOffset To) DefaultPeriod(DateTimeOffset now) =>
        (new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero), now);
}
