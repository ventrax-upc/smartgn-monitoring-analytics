namespace Com.Ventrax.Monitoring.Infrastructure.Clients;

// Sección "Integration" de appsettings.json (o variables de entorno).
public class IntegrationOptions
{
    public const string SectionName = "Integration";

    // Token de servicio a servicio compartido por la composición (INTERNAL_SERVICE_TOKEN).
    // Vacío = desarrollo local sin token, igual que Telemetry.
    public string InternalServiceToken { get; set; } = string.Empty;

    // IAM: POST /internal/v1/authorizations/check
    public string IamBaseUrl { get; set; } = "http://localhost:8080";

    // Plazo máximo para llamadas internas (QAS18: sin esperas indefinidas)
    public int RequestTimeoutSeconds { get; set; } = 3;
}

// Nombres de las métricas que publican los dispositivos (el contrato de Telemetry las deja libres).
public class TelemetryMetricsOptions
{
    public const string SectionName = "TelemetryMetrics";

    public string TotalVolumeM3 { get; set; } = "totalVolumeM3";
    public string PressureKpa { get; set; } = "pressureKpa";
    public string GasDetected { get; set; } = "gasDetected";
    public string ValveOpen { get; set; } = "valveOpen";

    // Límite por dispositivo en cada página de Telemetry (máximo del contrato: 1000)
    public int PageSize { get; set; } = 1000;

    // Páginas máximas por consulta, para no traer datos sin límite
    public int MaxPages { get; set; } = 20;
}
