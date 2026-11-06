using System.ComponentModel.DataAnnotations;

namespace SmartHydroAPI.Models.Dtos
{
    public record StructureDto(
        int Id,
        Guid? SiteId,
        string Name,
        string StructureType,
        string LabelColour,
        string? LocationText,
        decimal? Latitude,
        decimal? Longitude,
        DateTime CreatedAt,
        bool IsOnline,
        DateTime? LastSeenAt,
        string? CameraMac = null,
        string? ControllerMac = null,
        decimal? Temperature = null,
        decimal? Humidity = null,
        decimal? Ec = null,
        decimal? Ph = null
    );

    public record CreateStructureDto(
        string Name,
        string StructureType,
        string LabelColour,
        string? LocationText,
        decimal? Latitude,
        decimal? Longitude,
        Guid? SiteId = null,
        string? CameraMac = null,
        string? ControllerMac = null
    );

    public record UpdateStructureDto(
        string Name,
        string LabelColour,
        string? LocationText,
        decimal? Latitude,
        decimal? Longitude,
        Guid? SiteId = null,
        string? CameraMac = null,
        string? ControllerMac = null,
        string? StructureType = null
    );

    // Aggregated dashboard summary contract for single-call loading
    public class StructureDashboardSummaryDto
    {
        public int StructureId { get; set; }
        public Guid? SiteId { get; set; }
        public string StructureName { get; set; } = string.Empty;
        public string StructureType { get; set; } = string.Empty;
        public string LabelColour { get; set; } = string.Empty;
        public string? LocationText { get; set; }
        public bool IsOnline { get; set; }

        // Latest real-time telemetry snapshot
        public TelemetrySnapshotDto? LatestTelemetry { get; set; }

        // Current active relay states
        public ActuatorStateResponseDto? ActuatorState { get; set; }

        // Open alerts summary
        public int UnresolvedAlertsCount { get; set; }
        public List<AlertResponseDto> RecentAlerts { get; set; } = new();

        // Pending dosing queue
        public int PendingDosingCommandsCount { get; set; }

        // Registered devices
        public List<DeviceDto> Devices { get; set; } = new();

        // Optional Camera: null if no ESP32-CAM is assigned to this structure
        public string? CameraMac { get; set; }
        public DeviceDto? CameraDevice { get; set; }
    }

    public record TelemetrySnapshotDto(
        decimal PhLevel,
        decimal EcMs,
        decimal LightLux,
        int WaterLevelRaw,
        bool WaterLevelDetected,
        decimal TemperatureC,
        decimal HumidityPercent,
        DateTime RecordedAt
    );
}