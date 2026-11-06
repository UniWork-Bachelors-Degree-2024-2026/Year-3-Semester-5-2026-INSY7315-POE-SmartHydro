using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Services
{
    // Concrete implementation of Data-Mule synchronisation
    public class SyncService : ISyncService
    {
        private readonly ApplicationDbContext _database;

        // Injects database context
        public SyncService(ApplicationDbContext database)
        {
            _database = database;
        }

        // Retrieves highest recorded timestamp currently held by cloud database for given device
        public async Task<HighWaterMarkResponseDto> GetLatestTimestampAsync(string deviceMac, CancellationToken ct = default)
        {
            var mac = deviceMac.Trim().ToUpperInvariant();

            var latest = await _database.TelemetryRealtime
                .AsNoTracking()
                .Where(t => t.DeviceMac == mac)
                .OrderByDescending(t => t.RecordedAt)
                .Select(t => (DateTime?)t.RecordedAt)
                .FirstOrDefaultAsync(ct);

            var totalCount = await _database.TelemetryRealtime
                .AsNoTracking()
                .CountAsync(t => t.DeviceMac == mac, ct);

            return new HighWaterMarkResponseDto(mac, latest, totalCount);
        }

        // Ingests batch of sensor telemetry
        public async Task<TelemetryBatchSyncResponseDto> SyncTelemetryBatchAsync(TelemetryBatchSyncRequestDto dto, CancellationToken ct = default)
        {
            var mac = dto.DeviceMac.Trim().ToUpperInvariant();
            if (dto.Readings.Count == 0)
            {
                return new TelemetryBatchSyncResponseDto(mac, 0, 0, 0, null);
            }

            // Extract distinct timestamps from incoming payload
            var incomingTimes = dto.Readings.Select(r => r.RecordedAt.ToUniversalTime()).Distinct().ToList();

            // Find existing timestamps in database for this MAC address
            var existingTimes = await _database.TelemetryRealtime
                .AsNoTracking()
                .Where(t => t.DeviceMac == mac && incomingTimes.Contains(t.RecordedAt))
                .Select(t => t.RecordedAt)
                .ToListAsync(ct);

            var existingSet = new HashSet<DateTime>(existingTimes);

            // Filter out existing duplicates and deduplicate within batch
            var novelReadings = dto.Readings
                .Where(r => !existingSet.Contains(r.RecordedAt.ToUniversalTime()))
                .GroupBy(r => r.RecordedAt.ToUniversalTime())
                .Select(g => g.First())
                .ToList();

            var insertedCount = 0;
            if (novelReadings.Count > 0)
            {
                var entities = novelReadings.Select(r => new TelemetryRealtime
                {
                    DeviceMac = mac,
                    PhLevel = r.Ph >= 0m && r.Ph <= 14m ? r.Ph : -1m,
                    EcMs = r.Ec >= 0m && r.Ec <= 15m ? r.Ec.Value : -1m,
                    LightLux = r.Light >= 0m ? r.Light : -1m,
                    WaterLevelRaw = r.WaterLevelRaw,
                    WaterLevelDetected = r.WaterLevelDetected,
                    TemperatureC = r.Temperature >= -40m && r.Temperature <= 80m ? r.Temperature : -99m,
                    HumidityPercent = r.Humidity >= 0m && r.Humidity <= 100m ? r.Humidity : -1m,
                    MlPhPrediction = r.MlPh,
                    MlEcPrediction = r.MlEc,
                    MlTempPrediction = r.MlTemp,
                    MlHumidityPrediction = r.MlHumidity,
                    RecordedAt = r.RecordedAt.ToUniversalTime()
                }).ToList();

                await _database.TelemetryRealtime.AddRangeAsync(entities, ct);

                // Update device last seen timestamp
                var maxRecordedAt = novelReadings.Max(r => r.RecordedAt.ToUniversalTime());
                var device = await _database.Devices.FindAsync([mac], ct);
                if (device != null && maxRecordedAt > device.LastSeenAt)
                {
                    device.LastSeenAt = maxRecordedAt;
                }

                await _database.SaveChangesAsync(ct);
                insertedCount = entities.Count;
            }

            var skippedCount = dto.Readings.Count - insertedCount;

            // Query newest timestamp after insertion
            var latestStored = await _database.TelemetryRealtime
                .AsNoTracking()
                .Where(t => t.DeviceMac == mac)
                .OrderByDescending(t => t.RecordedAt)
                .Select(t => (DateTime?)t.RecordedAt)
                .FirstOrDefaultAsync(ct);

            return new TelemetryBatchSyncResponseDto(mac, dto.Readings.Count, insertedCount, skippedCount, latestStored);
        }

        // Ingests batch of offline alerts raised by the microcontroller
        public async Task<AlertsBatchSyncResponseDto> SyncAlertsBatchAsync(AlertsBatchSyncRequestDto dto, CancellationToken ct = default)
        {
            var mac = dto.DeviceMac.Trim().ToUpperInvariant();
            if (dto.Alerts.Count == 0)
            {
                return new AlertsBatchSyncResponseDto(mac, 0, 0, 0);
            }

            // Resolve structure ID if not explicitly provided
            var structureId = dto.StructureId;
            if (!structureId.HasValue)
            {
                var device = await _database.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.MacAddress == mac, ct);
                structureId = device?.StructureId ?? 1; // Fallback to structure 1 if unassigned
            }

            var incomingTimes = dto.Alerts.Select(a => a.OccurredAt.ToUniversalTime()).Distinct().ToList();

            var existingAlerts = await _database.Alerts
                .AsNoTracking()
                .Where(a => a.DeviceMac == mac && incomingTimes.Contains(a.OccurredAt))
                .Select(a => new { a.Component, a.OccurredAt })
                .ToListAsync(ct);

            var existingSet = new HashSet<string>(existingAlerts.Select(a => $"{a.Component}_{a.OccurredAt:O}"));

            var novelAlerts = dto.Alerts
                .Where(a => !existingSet.Contains($"{a.Component}_{a.OccurredAt.ToUniversalTime():O}"))
                .GroupBy(a => $"{a.Component}_{a.OccurredAt.ToUniversalTime():O}")
                .Select(g => g.First())
                .ToList();

            var insertedCount = 0;
            if (novelAlerts.Count > 0)
            {
                var entities = novelAlerts.Select(a => new Alert
                {
                    StructureId = structureId.Value,
                    DeviceMac = mac,
                    Component = a.Component,
                    Severity = a.Severity.ToUpperInvariant(),
                    Value = a.Value,
                    Detail = a.Detail,
                    OccurredAt = a.OccurredAt.ToUniversalTime(),
                    IsResolved = false
                }).ToList();

                await _database.Alerts.AddRangeAsync(entities, ct);
                await _database.SaveChangesAsync(ct);
                insertedCount = entities.Count;
            }

            var skippedCount = dto.Alerts.Count - insertedCount;
            return new AlertsBatchSyncResponseDto(mac, dto.Alerts.Count, insertedCount, skippedCount);
        }

        // Ingests batch of offline manual relay toggles into actuator history audit log
        public async Task<OverridesBatchSyncResponseDto> SyncOverridesBatchAsync(OverridesBatchSyncRequestDto dto, CancellationToken ct = default)
        {
            var mac = dto.DeviceMac.Trim().ToUpperInvariant();
            if (dto.Overrides.Count == 0)
            {
                return new OverridesBatchSyncResponseDto(mac, 0, 0, 0);
            }

            var incomingTimes = dto.Overrides.Select(o => o.ExecutedAt.ToUniversalTime()).Distinct().ToList();

            var existingHistory = await _database.ActuatorHistory
                .AsNoTracking()
                .Where(h => h.DeviceMac == mac && incomingTimes.Contains(h.ChangedAt))
                .Select(h => new { h.ActuatorName, h.ChangedAt })
                .ToListAsync(ct);

            var existingSet = new HashSet<string>(existingHistory.Select(h => $"{h.ActuatorName}_{h.ChangedAt:O}"));

            var novelOverrides = dto.Overrides
                .Where(o => !existingSet.Contains($"{o.ActuatorName}_{o.ExecutedAt.ToUniversalTime():O}"))
                .GroupBy(o => $"{o.ActuatorName}_{o.ExecutedAt.ToUniversalTime():O}")
                .Select(g => g.First())
                .ToList();

            var insertedCount = 0;
            if (novelOverrides.Count > 0)
            {
                var entities = novelOverrides.Select(o => new ActuatorHistory
                {
                    DeviceMac = mac,
                    ActuatorName = o.ActuatorName,
                    NewState = o.NewState,
                    ChangedBy = string.IsNullOrWhiteSpace(o.ChangedBy) ? "Android_Mule_Worker" : o.ChangedBy,
                    ChangedAt = o.ExecutedAt.ToUniversalTime()
                }).ToList();

                await _database.ActuatorHistory.AddRangeAsync(entities, ct);
                await _database.SaveChangesAsync(ct);
                insertedCount = entities.Count;
            }

            var skippedCount = dto.Overrides.Count - insertedCount;
            return new OverridesBatchSyncResponseDto(mac, dto.Overrides.Count, insertedCount, skippedCount);
        }
    }
}
