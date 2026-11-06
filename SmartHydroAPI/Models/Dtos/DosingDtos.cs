using System.ComponentModel.DataAnnotations;

namespace SmartHydroAPI.Models.Dtos
{
    // DTO for sending a one-shot volumetric peristaltic dosing command
    public class DosingCommandRequestDto
    {
        [Required]
        [MaxLength(17)]
        public string DeviceMac { get; set; } = string.Empty;

        // Dosing type: 'PH_UP', 'PH_DOWN', 'NUTRIENT_UP', 'DILUTION_DOWN'
        [Required]
        [MaxLength(30)]
        public string DosingType { get; set; } = string.Empty;

        // Target volumetric requirement in milliliters (e.g. 10.0 mL or 20.0 mL)
        [Range(0.5, 25.0, ErrorMessage = "Dosing volume must be between 0.5 mL and 25 mL.")]
        public decimal TargetVolumeMl { get; set; }
        public bool ManualPhOverride { get; set; }

        [MaxLength(100)]
        public string? IssuedBy { get; set; } = "User";
    }

    // Callback DTO sent by microcontroller when dosing pulse finishes
    public class DosingCallbackRequestDto
    {
        [Required]
        public Guid CommandId { get; set; }

        // Execution status: 'COMPLETED' or 'FAILED'
        [Required]
        [MaxLength(20)]
        [RegularExpression("^(COMPLETED|FAILED)$")]
        public string Status { get; set; } = "COMPLETED";

        public DateTime? ExecutedAt { get; set; }
    }

    // Response DTO representing queued or historical dosing command
    public record DosingCommandResponseDto(
        Guid Id,
        string DeviceMac,
        string DosingType,
        decimal TargetVolumeMl,
        int CalculatedDurationMs,
        string Status,
        DateTime CreatedAt,
        DateTime? ExecutedAt,
        string? IssuedBy,
        int? CommandId = null,
        string? DeliveryStatus = null,
        string? Reason = null,
        DateTime? ExpiresAt = null,
        DateTime? AcknowledgedAt = null,
        bool VolumeEstimated = true
    );
}
