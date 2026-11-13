using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Com.Ventrax.Monitoring.Domain.Aggregates;

namespace Com.Ventrax.Monitoring.Application.Interfaces;

// Puerto para guardar y leer reportes en PostgreSQL
public interface IMonitoringRepository
{
    Task SaveReportAsync(ConsumptionReport report);
    Task<ConsumptionReport?> GetReportAsync(Guid reportId);
}

// Puerto para conectarse al microservicio Telemetry & Data Ingestion vía gRPC
public interface ITelemetryGrpcClient
{
    // Lecturas validadas de un periodo (consulta en bloque, sin llamadas por medidor)
    Task<IReadOnlyList<TelemetryReadingDto>> GetReadingsBatchAsync(
        Guid accountId, IReadOnlyCollection<Guid> meterIds, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    // Última lectura de cada medidor
    Task<IReadOnlyList<TelemetryReadingDto>> GetLatestReadingsAsync(
        Guid accountId, IReadOnlyCollection<Guid> meterIds, CancellationToken ct = default);
}

// Puerto para conectarse al microservicio Payments & Billing vía gRPC
public interface IBillingGrpcClient
{
    Task<bool> HasProEntitlementAsync(Guid accountId, CancellationToken ct = default);
}

// Puerto hacia IAM: permiso vigente por medidor y función (titularidad o delegación)
public interface IMeterAuthorizationClient
{
    Task<bool> IsAllowedAsync(Guid accountId, Guid meterId, string function, CancellationToken ct = default);
}

// Puerto de caché (Cache-Aside). La implementación usa Redis; nunca reemplaza la autorización ni PostgreSQL.
public interface IAnalyticsCache
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) where T : class;
}

// Lectura de Telemetry ya traducida al lenguaje de Monitoring.
// Las métricas son opcionales: un dispositivo puede no reportar todas.
public record TelemetryReadingDto(
    Guid MeterId,
    DateTimeOffset MeasuredAt,
    decimal? TotalVolumeM3,
    double? PressureKpa,
    bool? GasDetected,
    bool? ValveOpen,
    bool Delayed);
