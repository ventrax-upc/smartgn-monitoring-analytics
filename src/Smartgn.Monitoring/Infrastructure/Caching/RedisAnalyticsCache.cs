using System.Text.Json;
using Com.Ventrax.Monitoring.Application.Interfaces;
using Microsoft.Extensions.Caching.Distributed;

namespace Com.Ventrax.Monitoring.Infrastructure.Caching;

// Adaptador de caché sobre IDistributedCache (Redis en ejecución, memoria en pruebas).
// Si Redis no responde, no rompe la consulta: se registra el error y se calcula desde la fuente.
public class RedisAnalyticsCache : IAnalyticsCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDistributedCache _cache;
    private readonly CacheMetrics _metrics;
    private readonly ILogger<RedisAnalyticsCache> _logger;

    public RedisAnalyticsCache(IDistributedCache cache, CacheMetrics metrics, ILogger<RedisAnalyticsCache> logger)
    {
        _cache = cache;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        try
        {
            var json = await _cache.GetStringAsync(key, ct);
            if (json is null)
            {
                _metrics.Miss();
                _logger.LogInformation("Cache MISS {CacheKey}", key);
                return null;
            }

            _metrics.Hit();
            _logger.LogInformation("Cache HIT {CacheKey}", key);
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _metrics.Error();
            _metrics.Miss();
            _logger.LogWarning(ex, "Redis no disponible al leer {CacheKey}; se consulta la fuente", key);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) where T : class
    {
        try
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            await _cache.SetStringAsync(key, json,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _metrics.Error();
            _logger.LogWarning(ex, "Redis no disponible al guardar {CacheKey}", key);
        }
    }
}
