using SmartHydroAPI.Models.Dtos;

namespace SmartHydroAPI.Services
{
    // Contract for Data-Mule synchronisation pipeline
    public interface ISyncService
    {
        // Retrieves the newest stored timestamp for a device to prevent redundant mobile uploads
        Task<HighWaterMarkResponseDto> GetLatestTimestampAsync(string deviceMac, CancellationToken ct = default);

        // Ingests a batch of historical telemetry readings
        Task<TelemetryBatchSyncResponseDto> SyncTelemetryBatchAsync(TelemetryBatchSyncRequestDto dto, CancellationToken ct = default);

        // Ingests a batch of offline-generated alerts with deduplication
        Task<AlertsBatchSyncResponseDto> SyncAlertsBatchAsync(AlertsBatchSyncRequestDto dto, CancellationToken ct = default);

        // Ingests a batch of manual offline relay overrides for audit logging
        Task<OverridesBatchSyncResponseDto> SyncOverridesBatchAsync(OverridesBatchSyncRequestDto dto, CancellationToken ct = default);
    }
}
