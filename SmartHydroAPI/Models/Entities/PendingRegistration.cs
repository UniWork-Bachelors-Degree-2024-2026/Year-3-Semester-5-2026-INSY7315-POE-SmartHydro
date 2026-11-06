using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartHydroAPI.Models.Entities
{
    [Table("pending_registrations")]
    public class PendingRegistration
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Required, MaxLength(255)]
        [Column("email")]
        public string Email { get; set; } = string.Empty;

        [Required, MaxLength(255)]
        [Column("password_hash")]
        public string PasswordHash { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        [Column("full_name")]
        public string FullName { get; set; } = string.Empty;

        [Required, MaxLength(60)]
        [Column("farm_name")]
        public string FarmName { get; set; } = string.Empty;

        [Required, MaxLength(255)]
        [Column("verification_token_hash")]
        public string VerificationTokenHash { get; set; } = string.Empty;

        [Required, MaxLength(255)]
        [Column("code_hash")]
        public string CodeHash { get; set; } = string.Empty;

        [Column("expires_at")]
        public DateTime ExpiresAt { get; set; }

        [Column("attempts")]
        public int Attempts { get; set; } = 0;

        [Column("send_count")]
        public int SendCount { get; set; } = 0;

        [Column("last_sent_at")]
        public DateTime? LastSentAt { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}