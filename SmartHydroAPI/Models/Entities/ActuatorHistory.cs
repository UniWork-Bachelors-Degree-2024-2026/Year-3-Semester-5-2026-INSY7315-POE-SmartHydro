using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartHydroAPI.Models.Entities
{
    [Table("actuator_history")]
    public class ActuatorHistory
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Required]
        [Column("device_mac")]
        [MaxLength(17)]
        public string DeviceMac { get; set; } = string.Empty;

        [Required]
        [Column("actuator_name")]
        [MaxLength(50)]
        public string ActuatorName { get; set; } = string.Empty;

        [Column("new_state")]
        public bool NewState { get; set; }

        [Column("changed_at")]
        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

        [Required]
        [Column("changed_by")]
        [MaxLength(50)]
        public string ChangedBy { get; set; } = "System";

        // Navigation
        [ForeignKey(nameof(DeviceMac))]
        public Device? Device { get; set; }
    }
}
