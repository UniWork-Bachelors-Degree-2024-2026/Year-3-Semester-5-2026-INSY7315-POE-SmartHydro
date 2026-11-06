namespace Group_1_SmartHydro_MVC.Models
{
    /// <summary>
    /// Everything the API knows about a structure's controller board right now: its latest sensor
    /// reading, the on/off state of its relays, recent relay changes and the daily averages used
    /// for the seven-day trend. Empty until the API has something to report.
    /// </summary>
    public class StructureReadings
    {
        public bool HasTelemetry { get; set; }
        public decimal Ph { get; set; }
        public PhDisplayFallback? PhDisplayFallback { get; set; }
        public decimal EcMs { get; set; }
        public decimal LightLux { get; set; }
        public decimal TemperatureC { get; set; }
        public decimal HumidityPercent { get; set; }
        public int WaterLevelRaw { get; set; }
        public bool WaterLevelDetected { get; set; }
        public DateTime? RecordedAt { get; set; }

        public bool HasActuators { get; set; }
        public Dictionary<string, bool> Actuators { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public DateTime? ActuatorsUpdatedAt { get; set; }

        public List<AuditRecord> Audit { get; set; } = new();
        public List<ActuatorEvent> Events { get; set; } = new();
        public List<DoseRecord> Doses { get; set; } = new();
        public List<TrendPoint> Trend { get; set; } = new();
    }

    /// <summary>
    /// One recorded change to a relay, such as the water pump being switched on by an operator.
    /// </summary>
    public sealed record ActuatorEvent(string Actuator, bool NewState, DateTime ChangedAt, string ChangedBy);

    /// <summary>
    /// One dosing command from the API's dosing history: what was dosed, how much, where it is in its
    /// lifecycle (PENDING, DISPATCHED, COMPLETED or FAILED), when it was queued and when it ran.
    /// </summary>
    public sealed record DoseRecord(string DosingType, decimal VolumeMl, string Status, DateTime CreatedAt,
        DateTime? ExecutedAt, string IssuedBy, Guid Id = default, int? CommandId = null,
        string? DeliveryStatus = null, string? Reason = null);

    /// <summary>
    /// The daily averages for one day of the seven-day trend.
    /// </summary>
    public sealed record TrendPoint(DateOnly Date, decimal AvgPh, decimal AvgEc, decimal AvgLight, decimal AvgTemperature, decimal AvgHumidity);
    public sealed record PhDisplayFallback(decimal Value, DateTime RecordedAt, long AgeSeconds);
}

namespace Group_1_SmartHydro_MVC.Models
{
    public class AuditRecord {
        public decimal? TriggerValue { get; set; }
        public decimal? Minimum { get; set; }
        public decimal? Maximum { get; set; }
        public int? CommandId { get; set; }
        public string Source { get; set; } = "";
        public string Action { get; set; } = "";
        public string Details { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime? RecordedAt { get; set; }
        public DateTime ReceivedAt { get; set; }
        public bool CaptureTimeUncertain { get; set; }
    }
}
