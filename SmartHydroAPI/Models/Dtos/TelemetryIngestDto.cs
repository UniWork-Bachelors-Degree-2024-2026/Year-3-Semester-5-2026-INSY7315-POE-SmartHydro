using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SmartHydroAPI.Models.Dtos
{
    // Ingestion DTO for sensor nodes (DFRobot FireBeetle 2 ESP32-C6 and AI-Thinker ESP32-CAM)
    public class TelemetryIngestDto : TelemetryCaptureMetadata
    {
        [Required]
        [MaxLength(17)]
        public string DeviceMac { get; set; } = string.Empty;

        public string? LocalIp { get; set; }

        [Range(0, 2)]
        public int? OperatingMode { get; set; }

        [Range(0, 2)]
        public int? PreferredMode { get; set; }

        [Range(0, 1)]
        public int? ModeReason { get; set; }

        [Range(-1.0, 14.0)]
        public decimal Ph { get; set; } = -1m;

        // Negative sentinels denote unavailable readings; never use them for control.
        // Calibrated Electrical Conductivity (EC in mS/cm) from edge MCU ADC sampling
        [Range(-1.0, 15.0)]
        [JsonPropertyName("ec")]
        public decimal? Ec { get; set; }

        [Range(-1.0, 100000.0)]
        public decimal Light { get; set; } = -1m;

        public int WaterLevelRaw { get; set; }

        public bool WaterLevelDetected { get; set; }

        [Range(-99.0, 80.0)]
        public decimal Temperature { get; set; } = -99m;

        [Range(-1.0, 100.0)]
        public decimal Humidity { get; set; } = -1m;

        // Edge ML Random Forest Predictions
        [JsonPropertyName("ml_ph")]
        public int MlPh { get; set; }

        [JsonPropertyName("ml_ec")]
        public int MlEc { get; set; }

        [JsonPropertyName("ml_temp")]
        public int MlTemp { get; set; }

        [JsonPropertyName("ml_humidity")]
        public int MlHumidity { get; set; }

        // Edge Reported Actuator Relay States (for bi-directional real-time cloud sync)
        [JsonPropertyName("growLight")]
        public bool? GrowLight { get; set; }

        [JsonPropertyName("circulationFan")]
        public bool? CirculationFan { get; set; }

        [JsonPropertyName("mainWaterPump")]
        public bool? MainWaterPump { get; set; }

        [JsonPropertyName("extractorFan")]
        public bool? ExtractorFan { get; set; }

        [JsonPropertyName("spraySystem")]
        public bool? SpraySystem { get; set; }

        [JsonPropertyName("phUpPump")]
        public bool? PhUpPump { get; set; }

        [JsonPropertyName("phDownPump")]
        public bool? PhDownPump { get; set; }

        [JsonPropertyName("nutrientUpPump")]
        public bool? NutrientUpPump { get; set; }

        [JsonPropertyName("dilutionDownPump")]
        public bool? DilutionDownPump { get; set; }
    }

    // Aggregated historical daily telemetry metrics contract
    public record TelemetryDailyPointDto(
        DateOnly SummaryDate,
        decimal AvgPh,
        decimal AvgEc,
        decimal AvgLight,
        decimal AvgTemperatureC,
        decimal AvgHumidityPercent,
        int TotalSamples
    );
}
