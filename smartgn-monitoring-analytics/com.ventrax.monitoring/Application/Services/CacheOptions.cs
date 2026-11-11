namespace Com.Ventrax.Monitoring.Application.Services;

// Sección "Cache" de appsettings.json
public class CacheOptions
{
    public const string SectionName = "Cache";

    // TTL de reportes históricos (el periodo ya terminó, los datos casi no cambian)
    public int HistoricalReportTtlSeconds { get; set; } = 600;

    // TTL corto si el periodo incluye datos recientes (siguen llegando lecturas)
    public int RecentReportTtlSeconds { get; set; } = 30;

    // Un periodo cuyo fin está dentro de esta ventana se considera "reciente"
    public int RecentWindowMinutes { get; set; } = 15;
}
