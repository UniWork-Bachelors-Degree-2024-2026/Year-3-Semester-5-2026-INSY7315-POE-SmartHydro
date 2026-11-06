using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartHydroAPI.Models.Entities
{
    // Kept separate from OtpCode so a pending sign-in code and a pending
    // password reset code never get mixed up for the same email address.
    [Table("password_reset_codes")]
    public class PasswordResetCode
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Required, MaxLength(255)]
        [Column("email")]
        public string Email { get; set; } = string.Empty;

        [Required, MaxLength(255)]
        [Column("code_hash")]
        public string CodeHash { get; set; } = string.Empty;

        [Column("expires_at")]
        public DateTime ExpiresAt { get; set; }

        [Column("is_used")]
        public bool IsUsed { get; set; } = false;

        [Column("attempts")]
        public int Attempts { get; set; } = 0;

        [Column("is_verified")]
        public bool IsVerified { get; set; } = false;

        [MaxLength(255)]
        [Column("reset_token_hash")]
        public string? ResetTokenHash { get; set; }

        [Column("reset_token_expires_at")]
        public DateTime? ResetTokenExpiresAt { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}