using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartHydroAPI.Models.Entities
{
    [Table("email_rate_limits")]
    public class EmailRateLimit
    {
        [Key, MaxLength(512)]
        [Column("limit_key")]
        public string LimitKey { get; set; } = string.Empty;

        [Column("hits")]
        public int Hits { get; set; }

        [Column("window_start")]
        public DateTime WindowStart { get; set; }
    }
}