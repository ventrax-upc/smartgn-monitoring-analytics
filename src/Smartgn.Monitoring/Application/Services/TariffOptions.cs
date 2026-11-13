namespace Com.Ventrax.Monitoring.Application.Services;

// Sección "Tariff" de appsettings.json: tarifa configurada para el gasto estimado (US13, US17)
public class TariffOptions
{
    public const string SectionName = "Tariff";

    public decimal PricePerM3 { get; set; } = 2.50m;
    public string Currency { get; set; } = "PEN";
}
