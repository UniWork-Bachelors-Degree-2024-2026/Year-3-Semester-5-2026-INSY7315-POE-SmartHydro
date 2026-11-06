using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Patterns.Repository
{
    // Repository interface for real-time telemetry recording, device registration, and historical lookups
    public interface ITelemetryRepository
    {
        // Upserts a device record with current timestamp and local IP
        Task<Device> UpsertDeviceHeartbeatAsync(string mac, string? localIp, DateTime now, CancellationToken ct = default);

        // Inserts a raw real-time telemetry reading
        Task InsertReadingAsync(TelemetryRealtime reading, CancellationToken ct = default);

        // Retrieves the latest telemetry reading for a device
        Task<TelemetryRealtime?> GetLatestByMacAsync(string mac, CancellationToken ct = default);

        // Retrieves the latest telemetry readings for all devices in a structure
        Task<IEnumerable<TelemetryRealtime>> GetLatestByStructureAsync(int structureId, CancellationToken ct = default);

        // Retrieves daily history telemetry aggregations over a specified number of days
        Task<IEnumerable<TelemetryDailyHistory>> GetDailyHistoryAsync(string mac, int days, CancellationToken ct = default);
    }
}
