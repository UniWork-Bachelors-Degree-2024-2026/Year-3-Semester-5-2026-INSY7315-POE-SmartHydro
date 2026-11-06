using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartHydroAPI.Models.Entities
{
    [Table("site_invites")]
    public class SiteInvite
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Required]
        [Column("site_id")]
        public Guid SiteId { get; set; }

        [Required, MaxLength(255)]
        [Column("email")]
        public string Email { get; set; } = string.Empty;

        [Required, MaxLength(512)]
        [Column("token_hash")]
        public string TokenHash { get; set; } = string.Empty;

        [Required, MaxLength(512)]
        [Column("code_hash")]
        public string CodeHash { get; set; } = string.Empty;

        [Required]
        [Column("expires_at")]
        public DateTime ExpiresAt { get; set; }

        [Column("attempts")]
        public int Attempts { get; set; }

        [Required, MaxLength(255)]
        [Column("created_by_email")]
        public string CreatedByEmail { get; set; } = string.Empty;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
