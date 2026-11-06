using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Services
{
    // Automated background service executing daily at 23:59:59 UTC to rollup telemetry metrics and purge raw samples
    public class TelemetryRollupService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TelemetryRollupService> _logger;

        // Injects scope factory for resolving scoped database context and logger
        public TelemetryRollupService(IServiceScopeFactory scopeFactory, ILogger<TelemetryRollupService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        // Main execution loop running on startup to catch up on any completed past days, then scheduling daily at 23:59:59 UTC
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Telemetry Rollup Background Service initialized.");

            // Initial startup catch-up: Aggregate any completed past days before today that were not yet rolled up
            try
            {
                await RollupCompletedDaysAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Initial telemetry rollup catch-up encountered an error.");
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var nowUtc = DateTime.UtcNow;
                    var nextRunUtc = nowUtc.Date.AddHours(23).AddMinutes(59).AddSeconds(59);

                    if (nowUtc >= nextRunUtc)
                    {
                        nextRunUtc = nextRunUtc.AddDays(1);
                    }

                    var delay = nextRunUtc - nowUtc;
                    _logger.LogInformation("Next telemetry rollup scheduled for {NextRunUtc} (delay: {Delay})", nextRunUtc, delay);

                    await Task.Delay(delay, stoppingToken);

                    // Execute rollup and purge for all completed days up to today
                    await RollupCompletedDaysAsync(stoppingToken);

                    // Also rollup today's window
                    var today = DateOnly.FromDateTime(DateTime.UtcNow);
                    await ExecuteDailyRollupAsync(today, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Telemetry Rollup Background Service is stopping.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred during telemetry daily rollup execution.");
                    // Backoff delay before retry
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
        }

        // Catches up on any completed calendar dates strictly prior to today that still have raw records
        public async Task RollupCompletedDaysAsync(CancellationToken ct = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var todayStartUtc = DateTime.UtcNow.Date;

            var unrolledDates = await db.TelemetryRealtime
                .AsNoTracking()
                .Where(t => !t.CaptureTimeUncertain && t.RecordedAt < todayStartUtc)
                .Select(t => t.RecordedAt.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToListAsync(ct);

            if (unrolledDates.Count == 0)
            {
                _logger.LogInformation("No un-rolled past telemetry dates found to aggregate.");
                return;
            }

            _logger.LogInformation("Found {Count} un-rolled past date(s) to aggregate into daily history.", unrolledDates.Count);

            foreach (var date in unrolledDates)
            {
                var targetDate = DateOnly.FromDateTime(date);
                await ExecuteDailyRollupAsync(targetDate, ct);
            }
        }

        // Aggregates raw telemetry readings for a date into TelemetryDailyHistory, commits to DB, and only then purges raw records
        public async Task ExecuteDailyRollupAsync(DateOnly targetDate, CancellationToken ct = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                var startOfDay = targetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                var endOfDay = targetDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

                _logger.LogInformation("Starting telemetry daily aggregation for date: {TargetDate} [{Start} - {End}]", targetDate, startOfDay, endOfDay);

                await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;

                // Fetch raw telemetry grouped by DeviceMac (case-insensitive)
                var dailyGroups = await db.TelemetryRealtime
                    .AsNoTracking()
                    .Where(t => !t.CaptureTimeUncertain && t.RecordedAt >= startOfDay && t.RecordedAt <= endOfDay)
                    .GroupBy(t => t.DeviceMac.ToUpper())
                    .Select(g => new
                    {
                        DeviceMac = g.Key,
                        SumPh = g.Where(t => t.PhLevel >= 0m).Sum(t => (decimal?)t.PhLevel) ?? 0m,
                        CountPh = g.Count(t => t.PhLevel >= 0m),
                        SumEc = g.Where(t => t.EcMs >= 0m).Sum(t => (decimal?)t.EcMs) ?? 0m,
                        CountEc = g.Count(t => t.EcMs >= 0m),
                        SumLight = g.Where(t => t.LightLux >= 0m).Sum(t => (decimal?)t.LightLux) ?? 0m,
                        CountLight = g.Count(t => t.LightLux >= 0m),
                        SumTemperature = g.Where(t => t.TemperatureC >= -40m && t.TemperatureC <= 80m).Sum(t => (decimal?)t.TemperatureC) ?? 0m,
                        CountTemperature = g.Count(t => t.TemperatureC >= -40m && t.TemperatureC <= 80m),
                        SumHumidity = g.Where(t => t.HumidityPercent >= 0m).Sum(t => (decimal?)t.HumidityPercent) ?? 0m,
                        CountHumidity = g.Count(t => t.HumidityPercent >= 0m),
                        TotalSamples = g.Count()
                    })
                    .ToListAsync(ct);

                if (dailyGroups.Count == 0)
                {
                    _logger.LogInformation("No telemetry readings found to rollup for date {TargetDate}.", targetDate);
                    return;
                }

                // Save the summary and remove exactly the rows it includes in one transaction.
                foreach (var group in dailyGroups)
                {
                    var summary = await db.TelemetryDailyHistory
                        .FirstOrDefaultAsync(h => h.DeviceMac.ToUpper() == group.DeviceMac && h.SummaryDate == targetDate, ct);
                    if (summary != null && (summary.CountPh == null || summary.CountEc == null ||
                        summary.CountLight == null || summary.CountTemperature == null || summary.CountHumidity == null))
                    {
                        // Older summaries did not keep valid-sample counts. Keep the late readings instead of guessing.
                        _logger.LogWarning("Keeping late readings for {Mac} on {Date}: the existing summary has no sample counts.", group.DeviceMac, targetDate);
                        continue;
                    }
                    if (summary == null)
                    {
                        summary = new TelemetryDailyHistory { DeviceMac = group.DeviceMac, SummaryDate = targetDate };
                        db.TelemetryDailyHistory.Add(summary);
                    }
                    summary.SumPh = (summary.SumPh ?? 0m) + group.SumPh;
                    summary.CountPh = (summary.CountPh ?? 0) + group.CountPh;
                    summary.AvgPh = summary.CountPh > 0 ? Math.Round(summary.SumPh!.Value / summary.CountPh!.Value, 2) : -1m;
                    summary.SumEc = (summary.SumEc ?? 0m) + group.SumEc;
                    summary.CountEc = (summary.CountEc ?? 0) + group.CountEc;
                    summary.AvgEc = summary.CountEc > 0 ? Math.Round(summary.SumEc!.Value / summary.CountEc!.Value, 2) : -1m;
                    summary.SumLight = (summary.SumLight ?? 0m) + group.SumLight;
                    summary.CountLight = (summary.CountLight ?? 0) + group.CountLight;
                    summary.AvgLight = summary.CountLight > 0 ? Math.Round(summary.SumLight!.Value / summary.CountLight!.Value, 1) : -1m;
                    summary.SumTemperature = (summary.SumTemperature ?? 0m) + group.SumTemperature;
                    summary.CountTemperature = (summary.CountTemperature ?? 0) + group.CountTemperature;
                    summary.AvgTemperatureC = summary.CountTemperature > 0 ? Math.Round(summary.SumTemperature!.Value / summary.CountTemperature!.Value, 2) : -99m;
                    summary.SumHumidity = (summary.SumHumidity ?? 0m) + group.SumHumidity;
                    summary.CountHumidity = (summary.CountHumidity ?? 0) + group.CountHumidity;
                    summary.AvgHumidityPercent = summary.CountHumidity > 0 ? Math.Round(summary.SumHumidity!.Value / summary.CountHumidity!.Value, 2) : -1m;
                    summary.TotalSamples += group.TotalSamples;
                    summary.CalculatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    var rows = await db.TelemetryRealtime.Where(t => !t.CaptureTimeUncertain &&
                        t.RecordedAt >= startOfDay && t.RecordedAt <= endOfDay && t.DeviceMac.ToUpper() == group.DeviceMac).ToListAsync(ct);
                    db.TelemetryRealtime.RemoveRange(rows);
                    await db.SaveChangesAsync(ct);
                }
                if (transaction != null) await transaction.CommitAsync(ct);
            });
        }
    }
}
