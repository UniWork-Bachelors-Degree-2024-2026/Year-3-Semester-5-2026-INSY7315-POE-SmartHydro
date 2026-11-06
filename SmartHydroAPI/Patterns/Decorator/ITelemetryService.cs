using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Patterns.Decorator
{
    // Core service interface for telemetry operations
    public interface ITelemetryService
    {
        // Ingests live telemetry packet and returns latest actuator state directives
        Task<ActuatorStateResponseDto> IngestAndSyncAsync(TelemetryIngestDto dto, CancellationToken ct = default);

        // Retrieves the most recent real-time telemetry reading for a device
        Task<TelemetryRealtime?> GetLatestByMacAsync(string mac, CancellationToken ct = default);

        // Retrieves latest readings for all nodes in a structure
        Task<IEnumerable<TelemetryRealtime>> GetLatestByStructureAsync(int structureId, CancellationToken ct = default);

        // Queries historical daily summary records
        Task<IEnumerable<TelemetryDailyHistory>> GetHistoryAsync(string mac, int days = 7, CancellationToken ct = default);
    }
}
