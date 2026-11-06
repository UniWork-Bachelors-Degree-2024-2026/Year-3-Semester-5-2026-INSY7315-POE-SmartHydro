using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartHydroAPI.Models.Entities
{
    [Table("login_attempts")]
    public class LoginAttempt
    {
        [MaxLength(255)]
        [Column("email")]
        public string Email { get; set; } = string.Empty;

        [MaxLength(64)]
        [Column("client_ip")]
        public string ClientIp { get; set; } = string.Empty;

        [Column("failed_attempts")]
        public int FailedAttempts { get; set; } = 0;

        [Column("last_failed_at")]
        public DateTime? LastFailedAt { get; set; }

        [Column("locked_until")]
        public DateTime? LockedUntil { get; set; }
    }
}