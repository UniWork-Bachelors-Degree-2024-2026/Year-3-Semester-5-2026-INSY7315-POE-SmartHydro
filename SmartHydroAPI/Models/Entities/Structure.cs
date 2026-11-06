using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartHydroAPI.Models.Entities
{
    [Table("structures")]
    public class Structure
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        // Foreign key referencing the parent physical farm or installation site
        [Column("site_id")]
        public Guid? SiteId { get; set; }

        [Required]
        [Column("name")]
        [MaxLength(60)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [Column("structure_type")]
        [MaxLength(20)]
        public string StructureType { get; set; } = "VERTICAL_TOWER";

        [Required]
        [Column("label_colour")]
        [MaxLength(20)]
        public string LabelColour { get; set; } = "#2ECC71";

        [Column("location_text")]
        [MaxLength(200)]
        public string? LocationText { get; set; }

        [Column("latitude")]
        public decimal? Latitude { get; set; }

        [Column("longitude")]
        public decimal? Longitude { get; set; }

        [Column("owner_email")]
        [MaxLength(255)]
        public string? OwnerEmail { get; set; }

        [Column("is_deleted")]
        public bool IsDeleted { get; set; }

        [Column("deleted_at")]
        public DateTime? DeletedAt { get; set; }

        [Column("deleted_by_email")]
        [MaxLength(255)]
        public string? DeletedByEmail { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey(nameof(SiteId))]
        public Site? Site { get; set; }

        public ICollection<Device> Devices { get; set; } = new List<Device>();
        public ICollection<Alert> Alerts { get; set; } = new List<Alert>();
        public ICollection<CameraCapture> CameraCaptures { get; set; } = new List<CameraCapture>();
    }
}
