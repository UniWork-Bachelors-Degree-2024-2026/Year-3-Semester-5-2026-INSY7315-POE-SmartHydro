using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartHydroAPI.Models.Entities
{
    // Entity representing registered IoT microcontroller nodes
    [Table("devices")]
    public class Device
    {
        [Key]
        [Column("mac_address")]
        [MaxLength(17)]
        public string MacAddress { get; set; } = string.Empty;

        [Column("structure_id")]
        public int? StructureId { get; set; }

        [Required]
        [Column("device_name")]
        [MaxLength(100)]
        public string DeviceName { get; set; } = string.Empty;

        [Required]
        [Column("device_type")]
        [MaxLength(50)]
        public string DeviceType { get; set; } = "SENSOR-NODE"; // 'SENSOR-NODE', 'CAMERA-NODE', 'CONTROLLER-NODE'

        // Structure deployment profile: 'VERTICAL-TOWER' or 'FODDER-SYSTEM'
        [Column("structure_type")]
        [MaxLength(50)]
        public string? StructureType { get; set; }

        // Flag indicating whether device is currently in offline SoftAP mode
        [Column("is_offline_mode")]
        public bool IsOfflineMode { get; set; } = false;

        // Pending flag indicating that the user requested transition to offline SoftAP mode
        [Column("pending_offline_transition")]
        public bool PendingOfflineTransition { get; set; } = false;

        [Column("local_ip")]
        [MaxLength(45)]
        public string? LocalIp { get; set; }

        [Column("last_seen_at")]
        public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("reported_mode")]
        public int? ReportedMode { get; set; }
        [Column("reported_preferred_mode")]
        public int? ReportedPreferredMode { get; set; }
        [Column("reported_mode_reason")]
        public int? ReportedModeReason { get; set; }
        [Column("mode_reported_at")]
        public DateTime? ModeReportedAt { get; set; }
        [Column("mode_request_confirmed_at")]
        public DateTime? ModeRequestConfirmedAt { get; set; }
        [Column("last_telemetry_transport")]
        public string? LastTelemetryTransport { get; set; }

        // Navigation
        [ForeignKey(nameof(StructureId))]
        public Structure? Structure { get; set; }
        public ActuatorState? ActuatorState { get; set; }
        public ICollection<DosingCommand> DosingCommands { get; set; } = new List<DosingCommand>();
    }
}
