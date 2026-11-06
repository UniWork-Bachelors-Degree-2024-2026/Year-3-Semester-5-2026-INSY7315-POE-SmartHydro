using System.ComponentModel.DataAnnotations.Schema;

namespace SmartHydroAPI.Models.Entities;

[Table("telemetry_receipts")]
public class TelemetryReceipt
{
    [Column("device_mac")] 
    public string DeviceMac { get; set; } = "";

    [Column("sample_sequence")] 
    public long Sequence { get; set; }

    [Column("received_at")] 
    public DateTime ReceivedAt { get; set; }
}
