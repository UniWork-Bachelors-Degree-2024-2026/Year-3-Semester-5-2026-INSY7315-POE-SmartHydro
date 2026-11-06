using System.ComponentModel.DataAnnotations;

namespace SmartHydroAPI.Models.Dtos
{
    // DTO for sending continuous manual relay toggle commands (fans, grow light, circulation pump)
    public class ActuatorCommandDto
    {
        [Required]
        [MaxLength(17)]
        public string DeviceMac { get; set; } = string.Empty;

        public bool? GrowLight { get; set; }
        public bool? CirculationFan { get; set; }
        public bool? MainWaterPump { get; set; }
        public bool? ExtractorFan { get; set; }
        public bool? PhUpPump { get; set; }
        public bool? PhDownPump { get; set; }
        public bool? NutrientUpPump { get; set; }
        public bool? DilutionDownPump { get; set; }
        public bool? SpraySystem { get; set; } // Fodder trays

        public string ChangedBy { get; set; } = "User";

        [Range(typeof(long), "1", "4611686018427387903")]
        public long? ReportedSequence { get; set; }
    }

    // DTO for one-shot timed actuator commands (e.g., Fodder misting spray for 8 seconds)
    public class TimedActuatorCommandDto
    {
        [Required]
        [MaxLength(17)]
        public string DeviceMac { get; set; } = string.Empty;

        // Target actuator: 'SpraySystem' or 'MainWaterPump'
        [Required]
        [MaxLength(50)]
        public string ActuatorName { get; set; } = "SpraySystem";

        // Duration in seconds (e.g. 5 to 60 seconds)
        [Range(1, 3600, ErrorMessage = "Duration must be between 1 and 3600 seconds.")]
        public int DurationSeconds { get; set; } = 8;

        public string ChangedBy { get; set; } = "User";
    }

    // Response DTO containing all 9 relay channels and update timestamp
    public record ActuatorStateResponseDto(
        string DeviceMac,
        bool GrowLight,
        bool CirculationFan,
        bool MainWaterPump,
        bool ExtractorFan,
        bool PhUpPump,
        bool PhDownPump,
        bool NutrientUpPump,
        bool DilutionDownPump,
        bool SpraySystem,
        DateTime UpdatedAt,
        decimal? MinPh = null,
        decimal? MaxPh = null,
        decimal? MinEc = null,
        decimal? MaxEc = null,
        string? PendingDosingType = null,
        int? PendingDosingDurationMs = null,
        string? PendingDosingCommandId = null,
        bool PendingOfflineTransition = false,
        string? ChangedBy = null,
        int? CommandId = null,
        bool Pending = false,
        Dictionary<string, object>? Command = null,
        int[]? CommandIds = null,
        [property: System.Text.Json.Serialization.JsonPropertyName("utc")] long? ServerUtc = null,
        long? ReportedSequence = null,
        Guid? RequestId = null
    );
}
