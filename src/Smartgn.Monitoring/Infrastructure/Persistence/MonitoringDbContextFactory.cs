using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Com.Ventrax.Monitoring.Infrastructure.Persistence;

// Usado solo por "dotnet ef migrations add ..." para crear el DbContext sin arrancar la API.
// En ejecución se usa la cadena de appsettings / ConnectionStrings__DefaultConnection.
public class MonitoringDbContextFactory : IDesignTimeDbContextFactory<MonitoringDbContext>
{
    public MonitoringDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Database=smartgn_monitoring;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<MonitoringDbContext>()
            .UseNpgsql(connection)
            .Options;

        return new MonitoringDbContext(options);
    }
}
