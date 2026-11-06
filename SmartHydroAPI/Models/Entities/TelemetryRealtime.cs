using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartHydroAPI.Models.Entities
{
    [Table("telemetry_realtime")]
    public class TelemetryRealtime
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Required, MaxLength(17)]
        [Column("device_mac")]
        public string DeviceMac { get; set; } = string.Empty;

        [Column("ph_level")]
        public decimal PhLevel { get; set; }

        [NotMapped]
        public SmartHydroAPI.Models.Dtos.PhDisplayFallbackDto? PhDisplayFallback { get; set; }

        [Column("ec_ms")]
        public decimal EcMs { get; set; }

        [Column("light_lux")]
        public decimal LightLux { get; set; }

        [Column("water_level_raw")]
        public int WaterLevelRaw { get; set; }

        [Column("water_level_detected")]
        public bool WaterLevelDetected { get; set; }

        [Column("temperature_c")]
        public decimal TemperatureC { get; set; }

        [Column("humidity_percent")]
        public decimal HumidityPercent { get; set; }

        // Edge ML Predictions
        [Column("ml_ph_prediction")]
        public int MlPhPrediction { get; set; }

        [Column("ml_ec_prediction")]
        public int MlEcPrediction { get; set; }

        [Column("ml_temp_prediction")]
        public int MlTempPrediction { get; set; }

        [Column("ml_humidity_prediction")]
        public int MlHumidityPrediction { get; set; }

        [Column("recorded_at")]
        public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

        [Column("sample_sequence")]
        public long? SampleSequence { get; set; }

        [Column("received_at")]
        public DateTime? ReceivedAt { get; set; }

        [Column("capture_time_uncertain")]
        public bool CaptureTimeUncertain { get; set; }

        [Column("relay_bitmask")]
        public int? RelayBitmask { get; set; }

        [Column("operating_mode")]
        public int? OperatingMode { get; set; }

        // Navigation
        [ForeignKey(nameof(DeviceMac))]
        public Device? Device { get; set; }
    }
}
