using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Services;

namespace SmartHydroAPI.Patterns.Repository
{
    // Concrete repository implementation for telemetry persistence and queries
    public class TelemetryRepository : ITelemetryRepository
    {
        private readonly ApplicationDbContext _database;

        // Injects database context
        public TelemetryRepository(ApplicationDbContext database) => _database = database;

        // Performs thread-safe device registration and heartbeat update
        public async Task<Device> UpsertDeviceHeartbeatAsync(string mac, string? localIp, DateTime now, CancellationToken ct = default)
        {
            var upperMac = LoraDownlinkQueue.NormalizeMac(mac);
            var compactMac = upperMac.Replace(":", "");
            var device = await _database.Devices.FindAsync([upperMac], ct)
                      ?? await _database.Devices.FindAsync([compactMac], ct)
                      ?? await _database.Devices.FindAsync([mac], ct);
            if (device is null)
            {
                var suffix = compactMac.Length >= 4 ? compactMac[^4..] : compactMac;
                device = new Device
                {
                    MacAddress = upperMac,
                    DeviceName = $"Node-{suffix}",
                    DeviceType = "SENSOR_NODE",
                    LocalIp = localIp,
                    LastSeenAt = now,
                    CreatedAt = now
                };
                _database.Devices.Add(device);
                _database.ActuatorStates.Add(new ActuatorState { DeviceMac = upperMac, UpdatedAt = now });
            }
            else
            {
                device.LastSeenAt = now;
                if (!string.IsNullOrWhiteSpace(localIp)) device.LocalIp = localIp;
            }

            return device;
        }

        // Inserts a new telemetry entity and flushes to PostgreSQL
        public async Task InsertReadingAsync(TelemetryRealtime reading, CancellationToken ct = default)
        {
            reading.DeviceMac = LoraDownlinkQueue.NormalizeMac(reading.DeviceMac);
            _database.TelemetryRealtime.Add(reading);
            await _database.SaveChangesAsync(ct);
        }

        // Reads the latest telemetry row for a device with AsNoTracking
        public async Task<TelemetryRealtime?> GetLatestByMacAsync(string mac, CancellationToken ct = default)
        {
            var upperMac = LoraDownlinkQueue.NormalizeMac(mac);
            var compactMac = upperMac.Replace(":", "");
            return await _database.TelemetryRealtime
                .AsNoTracking()
                .Where(t => !t.CaptureTimeUncertain)
                .Where(t => t.DeviceMac.ToUpper() == upperMac || t.DeviceMac.ToUpper() == compactMac)
                .OrderByDescending(t => t.RecordedAt)
                .FirstOrDefaultAsync(ct);
        }

        // Returns latest reading for each active node in a structure
        public async Task<IEnumerable<TelemetryRealtime>> GetLatestByStructureAsync(int structureId, CancellationToken ct = default)
        {
            var rawMacs = await _database.Devices
                .AsNoTracking()
                .Where(d => d.StructureId == structureId)
                .Select(d => d.MacAddress)
                .ToListAsync(ct);

            var matchMacs = rawMacs
                .SelectMany(m =>
                {
                    var norm = LoraDownlinkQueue.NormalizeMac(m);
                    return new[] { norm, norm.Replace(":", ""), m.Trim().ToUpperInvariant() };
                })
                .Distinct()
                .ToList();

            return await _database.TelemetryRealtime
                .AsNoTracking()
                .Where(t => !t.CaptureTimeUncertain)
                .Where(t => matchMacs.Contains(t.DeviceMac.ToUpper()))
                .GroupBy(t => t.DeviceMac.ToUpper())
                .Select(g => g.OrderByDescending(t => t.RecordedAt).First())
                .ToListAsync(ct);
        }

        // Queries daily rollup aggregations
        public async Task<IEnumerable<TelemetryDailyHistory>> GetDailyHistoryAsync(string mac, int days, CancellationToken ct = default)
        {
            var upperMac = LoraDownlinkQueue.NormalizeMac(mac);
            var compactMac = upperMac.Replace(":", "");
            var fromDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(2).AddDays(-days));

            return await _database.TelemetryDailyHistory
                .AsNoTracking()
                .Where(h => (h.DeviceMac.ToUpper() == upperMac || h.DeviceMac.ToUpper() == compactMac) && h.SummaryDate >= fromDate)
                .OrderBy(h => h.SummaryDate)
                .ToListAsync(ct);
        }
    }
}
