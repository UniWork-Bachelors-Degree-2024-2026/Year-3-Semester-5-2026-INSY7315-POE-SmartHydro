using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartHydroAPI.Models.Entities
{
    [Table("alerts")]
    public class Alert
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Column("structure_id")]
        public int StructureId { get; set; }

        [Column("device_mac")]
        [MaxLength(17)]
        public string? DeviceMac { get; set; }

        [Required]
        [Column("component")]
        [MaxLength(50)]
        public string Component { get; set; } = string.Empty;

        [Required]
        [Column("severity")]
        [MaxLength(20)]
        public string Severity { get; set; } = "INFO";

        [Column("value")]
        [MaxLength(50)]
        public string? Value { get; set; }

        [Column("detail")]
        [MaxLength(300)]
        public string? Detail { get; set; }

        [Column("occurred_at")]
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

        [Column("is_resolved")]
        public bool IsResolved { get; set; }

        [Column("cleared_by")]
        [MaxLength(255)]
        public string? ClearedBy { get; set; }

        [Column("resolved_at")]
        public DateTime? ResolvedAt { get; set; }

        [Column("is_deleted")]
        public bool IsDeleted { get; set; }

        [Column("deleted_at")]
        public DateTime? DeletedAt { get; set; }

        [Column("deleted_by_email")]
        [MaxLength(255)]
        public string? DeletedByEmail { get; set; }

        [ForeignKey(nameof(StructureId))]
        public Structure? Structure { get; set; }

        [ForeignKey(nameof(DeviceMac))]
        public Device? Device { get; set; }
    }
}
