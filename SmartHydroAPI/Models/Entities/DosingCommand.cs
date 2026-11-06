using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartHydroAPI.Models.Entities
{
    // Entity representing a non-latching volumetric dosing queue command
    [Table("dosing_commands")]
    public class DosingCommand
    {
        [Key]
        [Column("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(17)]
        [Column("device_mac")]
        public string DeviceMac { get; set; } = string.Empty;

        // Dosing type: 'PH_UP', 'PH_DOWN', 'NUTRIENT_UP', 'DILUTION_DOWN'
        [Required, MaxLength(30)]
        [Column("dosing_type")]
        public string DosingType { get; set; } = string.Empty;

        // Volumetric requirement in milliliters
        [Column("target_volume_ml")]
        public decimal TargetVolumeMl { get; set; }

        // Computed peristaltic pump run duration in milliseconds (800ms per 1 mL)
        [Column("calculated_duration_ms")]
        public int CalculatedDurationMs { get; set; }

        // State lifecycle: 'PENDING', 'DISPATCHED', 'COMPLETED', 'FAILED'
        [Required, MaxLength(20)]
        [Column("status")]
        public string Status { get; set; } = "PENDING";

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("executed_at")]
        public DateTime? ExecutedAt { get; set; }

        [MaxLength(100)]
        [Column("issued_by")]
        public string? IssuedBy { get; set; }

        // Navigation
        [ForeignKey(nameof(DeviceMac))]
        public Device? Device { get; set; }
    }
}
