using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartHydroAPI.Models.Entities
{
    [Table("camera_captures")]
    public class CameraCapture
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Column("device_mac")]
        [MaxLength(17)]
        public string? DeviceMac { get; set; }

        [Column("structure_id")]
        public int? StructureId { get; set; }

        [Required]
        [Column("image_data")]
        public byte[] ImageData { get; set; } = Array.Empty<byte>();

        [Required]
        [Column("content_type")]
        [MaxLength(50)]
        public string ContentType { get; set; } = "image/jpeg";

        [Column("image_size_bytes")]
        public int ImageSizeBytes { get; set; }

        [Column("plant")]
        [MaxLength(100)]
        public string? Plant { get; set; }

        [Column("label")]
        [MaxLength(100)]
        public string? Label { get; set; }

        [Column("camera_label")]
        [MaxLength(50)]
        public string? CameraLabel { get; set; }

        [Column("is_favourite")]
        public bool IsFavourite { get; set; }

        [Column("is_deleted")]
        public bool IsDeleted { get; set; }

        [Column("deleted_at")]
        public DateTime? DeletedAt { get; set; }

        [Column("deleted_by_email")]
        [MaxLength(255)]
        public string? DeletedByEmail { get; set; }

        [Column("captured_at")]
        public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(DeviceMac))]
        public Device? Device { get; set; }

        [ForeignKey(nameof(StructureId))]
        public Structure? Structure { get; set; }
    }
}
