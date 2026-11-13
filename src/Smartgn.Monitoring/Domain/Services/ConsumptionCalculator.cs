namespace Com.Ventrax.Monitoring.Domain.Services;

// Lectura acumulada de un medidor (el contador solo sube, como el de un medidor de gas).
public readonly record struct CumulativeReading(DateTimeOffset MeasuredAt, decimal TotalVolumeM3);

// Calcula el consumo de un periodo a partir de lecturas ACUMULADAS.
// El consumo NO es la suma de lecturas: es cuánto subió el contador.
// - Se ordenan por fecha (pueden llegar fuera de orden).
// - Se suman solo los incrementos positivos.
// - Si el contador baja (reinicio o cambio de medidor), esa lectura pasa a ser la nueva base.
public static class ConsumptionCalculator
{
    public static decimal FromCumulativeReadings(IEnumerable<CumulativeReading> readings)
    {
        var ordered = readings
            .OrderBy(r => r.MeasuredAt)
            .ToList();

        decimal consumed = 0m;
        for (var i = 1; i < ordered.Count; i++)
        {
            var delta = ordered[i].TotalVolumeM3 - ordered[i - 1].TotalVolumeM3;
            if (delta > 0)
            {
                consumed += delta;
            }
        }

        return consumed;
    }
}
