using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Com.Ventrax.Monitoring.Domain.Aggregates;
using Com.Ventrax.Monitoring.Application.Interfaces;

namespace Com.Ventrax.Monitoring.Infrastructure.Persistence;

public class MonitoringRepository : IMonitoringRepository
{
    private readonly MonitoringDbContext _dbContext;

    public MonitoringRepository(MonitoringDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SaveReportAsync(ConsumptionReport report)
    {
        await _dbContext.ConsumptionReports.AddAsync(report);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<ConsumptionReport?> GetReportAsync(Guid reportId)
    {
        return await _dbContext.ConsumptionReports
            .Include(r => r.MeterRows)
            .FirstOrDefaultAsync(r => r.ReportId == reportId);
    }
}