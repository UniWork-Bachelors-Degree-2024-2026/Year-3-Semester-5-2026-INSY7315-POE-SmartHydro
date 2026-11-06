using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartHydroAPI.Models.Entities
{
    [Table("telemetry_daily_history")]
    public class TelemetryDailyHistory
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Required]
        [Column("device_mac")]
        [MaxLength(17)]
        public string DeviceMac { get; set; } = string.Empty;

        [Column("summary_date")]
        public DateOnly SummaryDate { get; set; }

        [Column("avg_ph")]
        public decimal AvgPh { get; set; }

        [Column("avg_ec")]
        public decimal AvgEc { get; set; }

        [Column("avg_light")]
        public decimal AvgLight { get; set; }

        [Column("avg_temperature_c")]
        public decimal AvgTemperatureC { get; set; }

        [Column("avg_humidity_percent")]
        public decimal AvgHumidityPercent { get; set; }

        [Column("total_samples")]
        public int TotalSamples { get; set; }

        [Column("calculated_at")]
        public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;

        [Column("sum_ph")]
        public decimal? SumPh { get; set; }

        [Column("count_ph")]
        public int? CountPh { get; set; }

        [Column("sum_ec")]
        public decimal? SumEc { get; set; }

        [Column("count_ec")]
        public int? CountEc { get; set; }

        [Column("sum_light")]
        public decimal? SumLight { get; set; }

        [Column("count_light")]
        public int? CountLight { get; set; }

        [Column("sum_temperature")]
        public decimal? SumTemperature { get; set; }

        [Column("count_temperature")]
        public int? CountTemperature { get; set; }

        [Column("sum_humidity")]
        public decimal? SumHumidity { get; set; }

        [Column("count_humidity")]
        public int? CountHumidity { get; set; }

        // Navigation
        [ForeignKey(nameof(DeviceMac))]
        public Device? Device { get; set; }
    }
}
