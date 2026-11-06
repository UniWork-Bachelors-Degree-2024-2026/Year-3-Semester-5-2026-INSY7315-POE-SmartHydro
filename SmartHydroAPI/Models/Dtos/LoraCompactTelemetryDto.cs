using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SmartHydroAPI.Models.Dtos
{
    // Standardized Compact Minified JSON contract for LoRa 868MHz, 4G LTE, BLE, and Scalar API testing
    public class LoraCompactTelemetryDto : TelemetryCaptureMetadata
    {
        [JsonPropertyName("rs"), Range(typeof(long), "1", "4611686018427387903")]
        public long? ReportedSequence { get; set; }
        [JsonPropertyName("via"), MaxLength(10)] public string? ActivityTransport { get; set; }
        [JsonPropertyName("ev"), Range(0, 6)] 
        public int? ActivityPhase { get; set; }
        [JsonPropertyName("as"), Range(0, 6)] 
        public int? ActivitySource { get; set; }
        [JsonPropertyName("aa"), Range(0, 7)] 
        public int? ActivityAction { get; set; }
        [JsonPropertyName("av")] 
        public decimal? ActivityValue { get; set; }
        [JsonPropertyName("an")] 
        public decimal? ActivityMin { get; set; }
        [JsonPropertyName("az")] 
        public decimal? ActivityMax { get; set; }
        [JsonPropertyName("ad"), Range(0, 20000)] 
        public int? ActivityDuration { get; set; }
        [JsonPropertyName("ac"), Range(0, 65535)] 
        public int? ActivityCid { get; set; }
        [JsonPropertyName("ap"), Range(0, 2)] 
        public int? ActivityPrediction { get; set; }
        [JsonPropertyName("aw"), Range(0, 11)]
        public int? ActivityReason { get; set; }

        [JsonPropertyName("cack")] 
        public int? CommandAck { get; set; }

        [JsonPropertyName("ar"), Range(0, 1)]
        public int? CommandReceiptRequested { get; set; }
        
        [JsonPropertyName("cid")] 
        public int? CommandId { get; set; }
        
        [JsonPropertyName("ok")] 
        public int? CommandOk { get; set; }
        
        [JsonPropertyName("reason")] 
        public string? CommandReason { get; set; }
        
        [JsonPropertyName("phase")] 
        public string? CommandPhase { get; set; }

        [Required]
        [JsonPropertyName("m")]
        public string DeviceMac { get; set; } = string.Empty;

        [JsonPropertyName("wr"), Range(0, 4095)]
        public int? WaterLevelRaw { get; set; }
        [JsonPropertyName("mp"), Range(0, 255)]
        public int? ModelPredictions { get; set; }

        [JsonPropertyName("p")]
        public decimal? Ph { get; set; }

        [JsonPropertyName("e")]
        public decimal? Ec { get; set; }

        [JsonPropertyName("t")]
        public decimal? Temperature { get; set; }

        [JsonPropertyName("h")]
        public decimal? Humidity { get; set; }

        [JsonPropertyName("l")]
        public decimal? LightLux { get; set; }

        [JsonPropertyName("w")]
        public int? WaterLevel { get; set; }

        [JsonPropertyName("s")]
        public int? SprayPump { get; set; }

        [JsonPropertyName("p1")]
        public int? MainWaterPump { get; set; }

        [JsonPropertyName("wp")]
        public int? WaterPumpAlt { get; set; }

        [JsonPropertyName("fc")]
        public int? CirculationFan { get; set; }

        [JsonPropertyName("fe")]
        public int? ExtractorFan { get; set; }

        [JsonPropertyName("gl")]
        public int? GrowLight { get; set; }

        [JsonPropertyName("pu")]
        public int? PhUpPump { get; set; }

        [JsonPropertyName("pd")]
        public int? PhDownPump { get; set; }

        [JsonPropertyName("nu")]
        public int? NutrientUpPump { get; set; }

        [JsonPropertyName("nd")]
        public int? DilutionDownPump { get; set; }

        [JsonPropertyName("r")]
        [Range(0, 255)]
        public int? RelayBitmask { get; set; }

        [JsonPropertyName("md")]
        [Range(0, 2)]
        public int? OperatingMode { get; set; }

        [JsonPropertyName("pm")]
        [Range(0, 2)]
        public int? PreferredMode { get; set; }

        [JsonPropertyName("mr")]
        [Range(0, 1)]
        public int? ModeReason { get; set; }

        [JsonPropertyName("al")]
        public string? SafetyAlert { get; set; }

        [JsonPropertyName("br")]
        public int? BridgeRssi { get; set; }

        [JsonPropertyName("gr")]
        public int? GatewayRssi { get; set; }

        [JsonPropertyName("rssi")]
        public int? Rssi { get; set; }

        [JsonPropertyName("snr")]
        public float? Snr { get; set; }
    }
}
