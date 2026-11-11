using Microsoft.EntityFrameworkCore;
using Com.Ventrax.Monitoring.Domain.Aggregates;
using Com.Ventrax.Monitoring.Domain.Entities;

namespace Com.Ventrax.Monitoring.Infrastructure.Persistence;

public class MonitoringDbContext : DbContext
{
    public DbSet<ConsumptionReport> ConsumptionReports { get; set; }

    public MonitoringDbContext(DbContextOptions<MonitoringDbContext> options) : base(options) 
    { 
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Mapeo del Agregado Principal
        modelBuilder.Entity<ConsumptionReport>(entity =>
        {
            entity.HasKey(e => e.ReportId);
            entity.Property(e => e.AccountId).IsRequired();
            entity.Property(e => e.Status).HasConversion<string>();

            // Configurar ReportLine como una entidad poseída (Owned Entity)
            // Esto asegura que las líneas del reporte no existan sin su reporte padre
            entity.OwnsMany(e => e.MeterRows, row =>
            {
                row.WithOwner().HasForeignKey("ConsumptionReportId");
                row.Property<int>("Id").ValueGeneratedOnAdd();
                row.HasKey("Id");
            });

            // OptionalComparison no se persiste por ahora: sus DateRange anidados no se pueden mapear
            // como owned con el constructor del record y EF falla al construir el modelo.
            entity.Ignore(e => e.OptionalComparison);
        });
    }
}