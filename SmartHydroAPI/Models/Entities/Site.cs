using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartHydroAPI.Models.Entities
{
    // Entity representing a physical cultivation farm or research site location
    [Table("sites")]
    public class Site
    {
        [Key]
        [Column("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [Column("name")]
        [MaxLength(60)]
        public string Name { get; set; } = string.Empty;

        [Column("owner_email")]
        [MaxLength(255)]
        public string? OwnerEmail { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("is_deleted")]
        public bool IsDeleted { get; set; } = false;

        [Column("deleted_at")]
        public DateTime? DeletedAt { get; set; }

        [Column("deleted_by_email")]
        [MaxLength(255)]
        public string? DeletedByEmail { get; set; }

        // Navigation collection of cultivation structures situated at this site
        public ICollection<Structure> Structures { get; set; } = new List<Structure>();
    }
}
