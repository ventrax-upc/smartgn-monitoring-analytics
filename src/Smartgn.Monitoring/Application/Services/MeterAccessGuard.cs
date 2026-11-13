using Com.Ventrax.Monitoring.Application.Exceptions;
using Com.Ventrax.Monitoring.Application.Interfaces;

namespace Com.Ventrax.Monitoring.Application.Services;

// Verifica en IAM el permiso vigente de CADA medidor pedido.
// La lista que manda el cliente no implica autorización automática.
// Se ejecuta siempre ANTES de leer la caché.
public class MeterAccessGuard
{
    private const int MaxParallelChecks = 10;

    private readonly IMeterAuthorizationClient _authorization;

    public MeterAccessGuard(IMeterAuthorizationClient authorization)
    {
        _authorization = authorization;
    }

    public async Task EnsureAllowedAsync(
        Guid accountId, IReadOnlyCollection<Guid> meterIds, string function, CancellationToken ct = default)
    {
        using var throttle = new SemaphoreSlim(MaxParallelChecks);

        var checks = meterIds.Select(async meterId =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                var allowed = await _authorization.IsAllowedAsync(accountId, meterId, function, ct);
                return (MeterId: meterId, Allowed: allowed);
            }
            finally
            {
                throttle.Release();
            }
        });

        var results = await Task.WhenAll(checks);
        foreach (var result in results)
        {
            if (!result.Allowed)
            {
                throw new MeterAccessDeniedException(result.MeterId, function);
            }
        }
    }
}
