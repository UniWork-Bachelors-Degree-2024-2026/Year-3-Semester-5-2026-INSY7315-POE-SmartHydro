using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartHydroAPI.Models.Entities
{
    [Table("camera_commands")]
    public class CameraCommand
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Required]
        [Column("device_mac")]
        [MaxLength(17)]
        public string DeviceMac { get; set; } = string.Empty;

        [Column("is_pending")]
        public bool IsPending { get; set; } = true;

        [Column("requested_at")]
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

        [Column("completed_at")]
        public DateTime? CompletedAt { get; set; }

        // Navigation
        [ForeignKey(nameof(DeviceMac))]
        public Device? Device { get; set; }
    }
}
