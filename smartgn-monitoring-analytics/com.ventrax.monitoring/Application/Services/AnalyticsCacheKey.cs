namespace Com.Ventrax.Monitoring.Application.Services;

// Clave de caché separada por servicio, tipo de consulta, versión, cuenta, medidores y periodo
// (Cache-Aside del informe). Los medidores se ordenan para que el mismo conjunto use la misma clave.
public static class AnalyticsCacheKey
{
    private const string Version = "v2";

    public static string For(string kind, Guid accountId, IEnumerable<Guid> meterIds, DateTimeOffset from, DateTimeOffset to)
    {
        var meters = string.Join(",", meterIds.Distinct().OrderBy(id => id));
        return $"analytics:{kind}:{Version}:{accountId}:{meters}:{from.UtcDateTime:O}:{to.UtcDateTime:O}";
    }
}
