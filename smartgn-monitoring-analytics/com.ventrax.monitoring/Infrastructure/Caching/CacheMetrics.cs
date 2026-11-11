using System.Diagnostics.Metrics;

namespace Com.Ventrax.Monitoring.Infrastructure.Caching;

// Métricas de la caché (Monitoring Cache): aciertos, fallos y errores de Redis.
// Se ven con: dotnet-counters monitor -n com.ventrax.monitoring --counters SmartGN.Monitoring.Cache
public sealed class CacheMetrics : IDisposable
{
    public const string MeterName = "SmartGN.Monitoring.Cache";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _hits;
    private readonly Counter<long> _misses;
    private readonly Counter<long> _errors;
    private long _hitCount;
    private long _missCount;
    private long _errorCount;

    public CacheMetrics()
    {
        _hits = _meter.CreateCounter<long>("monitoring_cache_hits", description: "Reportes servidos desde Redis");
        _misses = _meter.CreateCounter<long>("monitoring_cache_misses", description: "Reportes calculados porque no estaban en Redis");
        _errors = _meter.CreateCounter<long>("monitoring_cache_errors", description: "Fallos al leer o escribir en Redis");
    }

    public long Hits => Interlocked.Read(ref _hitCount);
    public long Misses => Interlocked.Read(ref _missCount);
    public long Errors => Interlocked.Read(ref _errorCount);

    public void Hit() { Interlocked.Increment(ref _hitCount); _hits.Add(1); }
    public void Miss() { Interlocked.Increment(ref _missCount); _misses.Add(1); }
    public void Error() { Interlocked.Increment(ref _errorCount); _errors.Add(1); }

    public void Dispose() => _meter.Dispose();
}
