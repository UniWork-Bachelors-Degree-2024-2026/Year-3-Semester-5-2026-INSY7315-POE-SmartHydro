using System.ComponentModel.DataAnnotations.Schema;
namespace SmartHydroAPI.Models.Entities;
[Table("activity_logs")]
public class ActivityLog
{
    [Column("id")] 
    public long Id { get; set; }

    [Column("device_mac")] 
    public string DeviceMac { get; set; } = "";

    [Column("event_key")] 
    public string EventKey { get; set; } = "";

    [Column("source")] 
    public string Source { get; set; } = "";

    [Column("action")] 
    public string Action { get; set; } = "";

    [Column("status")] 
    public string Status { get; set; } = "";

    [Column("details")] 
    public string Details { get; set; } = "";

    [Column("trigger_value")] 
    public decimal? TriggerValue { get; set; }

    [Column("minimum")] 
    public decimal? Minimum { get; set; }

    [Column("maximum")] 
    public decimal? Maximum { get; set; }

    [Column("estimated_ml")] 
    public decimal? EstimatedMl { get; set; }

    [Column("runtime_ms")] 
    public int? RuntimeMs { get; set; }

    [Column("command_id")] 
    public int? CommandId { get; set; }

    [Column("recorded_at")] 
    public DateTime? RecordedAt { get; set; }

    [Column("received_at")] 
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    [Column("capture_time_uncertain")] 
    public bool CaptureTimeUncertain { get; set; }
}
