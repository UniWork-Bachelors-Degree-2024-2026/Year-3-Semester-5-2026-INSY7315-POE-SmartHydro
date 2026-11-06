using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SmartHydroAPI.Models.Dtos
{
    // DTO returned when querying the highest recorded timestamp for a device
    public record HighWaterMarkResponseDto(
        string DeviceMac,
        DateTime? LatestTimestamp,
        int TotalStoredReadings
    );

    // Request payload for batch telemetry ingestion from Android RoomDB
    public class TelemetryBatchSyncRequestDto
    {
        [Required]
        [MaxLength(17)]
        public string DeviceMac { get; set; } = string.Empty;

        [Required]
        public List<TelemetryBatchItemDto> Readings { get; set; } = new();
    }

    // Individual sensor reading cached offline with original timestamp
    public class TelemetryBatchItemDto
    {
        [Range(-1.0, 14.0)]
        public decimal Ph { get; set; } = -1m;

        [Range(-1.0, 15.0)]
        [JsonPropertyName("ec")]
        public decimal? Ec { get; set; }

        [Range(-1.0, 100000.0)]
        public decimal Light { get; set; } = -1m;

        public int WaterLevelRaw { get; set; }
        public bool WaterLevelDetected { get; set; }

        [Range(-99.0, 80.0)]
        public decimal Temperature { get; set; } = -99m;

        [Range(-1.0, 100.0)]
        public decimal Humidity { get; set; } = -1m;

        // Edge ML Predictions
        [JsonPropertyName("ml_ph")]
        public int MlPh { get; set; }

        [JsonPropertyName("ml_ec")]
        public int MlEc { get; set; }

        [JsonPropertyName("ml_temp")]
        public int MlTemp { get; set; }

        [JsonPropertyName("ml_humidity")]
        public int MlHumidity { get; set; }

        [Required]
        public DateTime RecordedAt { get; set; }
    }

    // Response after batch telemetry deduplication and ingestion
    public record TelemetryBatchSyncResponseDto(
        string DeviceMac,
        int TotalReceived,
        int Inserted,
        int Skipped,
        DateTime? LatestStoredTimestamp
    );

    // Request payload for batch alerts raised while offline
    public class AlertsBatchSyncRequestDto
    {
        [Required]
        [MaxLength(17)]
        public string DeviceMac { get; set; } = string.Empty;

        public int? StructureId { get; set; }

        [Required]
        public List<AlertBatchItemDto> Alerts { get; set; } = new();
    }

    // Individual offline alert item
    public class AlertBatchItemDto
    {
        [Required, MaxLength(50)]
        public string Component { get; set; } = string.Empty;

        [Required, MaxLength(20)]
        public string Severity { get; set; } = "WARNING";

        [MaxLength(50)]
        public string? Value { get; set; }

        [MaxLength(300)]
        public string? Detail { get; set; }

        [Required]
        public DateTime OccurredAt { get; set; }
    }

    // Response after batch alert ingestion
    public record AlertsBatchSyncResponseDto(
        string DeviceMac,
        int TotalReceived,
        int Inserted,
        int Skipped
    );

    // Request payload for batch manual relay overrides executed while offline
    public class OverridesBatchSyncRequestDto
    {
        [Required]
        [MaxLength(17)]
        public string DeviceMac { get; set; } = string.Empty;

        [Required]
        public List<OverrideBatchItemDto> Overrides { get; set; } = new();
    }

    // Individual manual override record
    public class OverrideBatchItemDto
    {
        [Required, MaxLength(50)]
        public string ActuatorName { get; set; } = string.Empty;

        public bool NewState { get; set; }

        [MaxLength(50)]
        public string ChangedBy { get; set; } = "Android_Mule_Worker";

        [Required]
        public DateTime ExecutedAt { get; set; }
    }

    // Response after batch overrides ingestion
    public record OverridesBatchSyncResponseDto(
        string DeviceMac,
        int TotalReceived,
        int Inserted,
        int Skipped
    );
}
