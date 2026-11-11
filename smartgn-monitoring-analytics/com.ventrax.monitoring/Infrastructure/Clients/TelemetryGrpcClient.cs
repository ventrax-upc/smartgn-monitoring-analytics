using Com.Ventrax.Monitoring.Application.Exceptions;
using Com.Ventrax.Monitoring.Application.Interfaces;
using Com.Ventrax.Monitoring.Infrastructure.Clients.Telemetry;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace Com.Ventrax.Monitoring.Infrastructure.Clients;

// Telemetry gRPC Client: consulta privada en bloque a Telemetry & Data Ingestion.
// Telemetry devuelve las lecturas más nuevas primero y como máximo "limit" por dispositivo,
// así que si un medidor llena la página se pide la página anterior (hasta MaxPages).
public class TelemetryGrpcClient : ITelemetryGrpcClient
{
    private const string Dependency = "Telemetry & Data Ingestion";

    private readonly TelemetryQueryService.TelemetryQueryServiceClient _client;
    private readonly IntegrationOptions _integration;
    private readonly TelemetryMetricsOptions _metrics;
    private readonly ILogger<TelemetryGrpcClient> _logger;

    public TelemetryGrpcClient(
        TelemetryQueryService.TelemetryQueryServiceClient client,
        IOptions<IntegrationOptions> integration,
        IOptions<TelemetryMetricsOptions> metrics,
        ILogger<TelemetryGrpcClient> logger)
    {
        _client = client;
        _integration = integration.Value;
        _metrics = metrics.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TelemetryReadingDto>> GetReadingsBatchAsync(
        IReadOnlyCollection<Guid> meterIds, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var result = new List<TelemetryReadingDto>();
        var pages = new Queue<(IReadOnlyCollection<Guid> Meters, DateTimeOffset To)>();
        pages.Enqueue((meterIds, to));
        var requested = 0;

        while (pages.Count > 0)
        {
            if (requested >= _metrics.MaxPages)
            {
                _logger.LogWarning("Telemetry paging stopped after {Pages} pages; the period may be incomplete", requested);
                break;
            }

            var (meters, upper) = pages.Dequeue();
            requested++;

            var request = new ReadingsBatchRequest
            {
                FromUnixMs = from.ToUnixTimeMilliseconds(),
                ToUnixMs = upper.ToUnixTimeMilliseconds(),
                Limit = _metrics.PageSize,
            };
            request.DeviceIds.AddRange(meters.Select(id => id.ToString()));

            var response = await CallAsync(() => _client.GetReadingsBatchAsync(request, Options(ct)).ResponseAsync, ct);
            var page = response.Readings.Select(Map).OfType<TelemetryReadingDto>().ToList();
            result.AddRange(page);

            // Medidores que llenaron la página: pedir lo anterior a su lectura más antigua
            foreach (var full in page.GroupBy(r => r.MeterId).Where(g => g.Count() >= _metrics.PageSize))
            {
                var oldest = full.Min(r => r.MeasuredAt);
                if (oldest > from)
                {
                    pages.Enqueue(([full.Key], oldest));
                }
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<TelemetryReadingDto>> GetLatestReadingsAsync(
        IReadOnlyCollection<Guid> meterIds, CancellationToken ct = default)
    {
        var request = new LatestReadingsRequest { Limit = meterIds.Count };
        request.DeviceIds.AddRange(meterIds.Select(id => id.ToString()));

        var response = await CallAsync(() => _client.GetLatestReadingsAsync(request, Options(ct)).ResponseAsync, ct);
        return response.Readings.Select(Map).OfType<TelemetryReadingDto>().ToList();
    }

    private CallOptions Options(CancellationToken ct)
    {
        var headers = new Metadata();
        if (!string.IsNullOrEmpty(_integration.InternalServiceToken))
        {
            headers.Add("authorization", $"Bearer {_integration.InternalServiceToken}");
        }

        return new CallOptions(
            headers,
            deadline: DateTime.UtcNow.AddSeconds(_integration.RequestTimeoutSeconds),
            cancellationToken: ct);
    }

    private static async Task<T> CallAsync<T>(Func<Task<T>> call, CancellationToken ct)
    {
        try
        {
            return await call();
        }
        catch (RpcException ex) when (!ct.IsCancellationRequested)
        {
            throw new DependencyUnavailableException(Dependency, ex);
        }
    }

    private TelemetryReadingDto? Map(Reading reading)
    {
        if (!Guid.TryParse(reading.DeviceId, out var meterId))
        {
            _logger.LogWarning("Ignoring reading with non-GUID device id {DeviceId}", reading.DeviceId);
            return null;
        }

        return new TelemetryReadingDto(
            meterId,
            DateTimeOffset.FromUnixTimeMilliseconds(reading.MeasuredAtUnixMs),
            Metric(reading, _metrics.TotalVolumeM3) is { } volume ? (decimal)volume : null,
            Metric(reading, _metrics.PressureKpa),
            Metric(reading, _metrics.GasDetected) is { } gas ? gas != 0 : null,
            Metric(reading, _metrics.ValveOpen) is { } valve ? valve != 0 : null,
            Delayed: string.Equals(reading.Freshness, "delayed", StringComparison.OrdinalIgnoreCase));
    }

    private static double? Metric(Reading reading, string name) =>
        reading.Metrics.TryGetValue(name, out var value) ? value : null;
}
