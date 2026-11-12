using Com.Ventrax.Monitoring.Domain.Aggregates;
using Com.Ventrax.Monitoring.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Com.Ventrax.Monitoring.Tests;

public class MonitoringDbContextTests
{
    [Fact]
    public void EfModel_Builds_AndGeneratesReportTables()
    {
        var options = new DbContextOptionsBuilder<MonitoringDbContext>()
            .UseNpgsql("Host=localhost;Database=model_check")
            .Options;
        using var context = new MonitoringDbContext(options);

        Assert.NotNull(context.Model.FindEntityType(typeof(ConsumptionReport)));
        Assert.Contains("CREATE TABLE", context.Database.GenerateCreateScript());
    }
}
