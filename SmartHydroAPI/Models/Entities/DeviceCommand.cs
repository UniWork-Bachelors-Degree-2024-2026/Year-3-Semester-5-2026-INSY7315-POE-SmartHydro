using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartHydroAPI.Models.Entities;

[Table("device_commands")]
public class DeviceCommand
{
    [Column("id")] 
    public long Id { get; set; }

    [Column("command_id")]
    public int CommandId { get; set; }

    [Column("request_id")]
    public Guid RequestId { get; set; }

    [Column("device_mac"), MaxLength(17)]
    public string DeviceMac { get; set; } = "";

    [Column("system_type"), MaxLength(20)] 
    public string SystemType { get; set; } = "";
    
    [Column("target"), MaxLength(100)] 
    public string Target { get; set; } = "";
    
    [Column("command_text")] 
    public string CommandText { get; set; } = "";
    
    [Column("payload")] 
    public string Payload { get; set; } = "{}";
    
    [Column("status"), MaxLength(20)] 
    public string Status { get; set; } = "QUEUED";
    
    [Column("source"), MaxLength(100)] 
    public string Source { get; set; } = "User-MVC";
    
    [Column("retry_count")] 
    public int RetryCount { get; set; }
    
    [Column("created_at")] 
    public DateTime CreatedAt { get; set; }

    [Column("sent_at")] 
    public DateTime? SentAt { get; set; }
    [Column("acked_at")] 
    public DateTime? AckedAt { get; set; }
    [Column("expires_at")] 
    public DateTime ExpiresAt { get; set; }
    [Column("relay_bitmask")] 
    public int? RelayBitmask { get; set; }
    [Column("succeeded")] 
    public bool? Succeeded { get; set; }
    [Column("reason"), MaxLength(100)] 
    public string? Reason { get; set; }
    [Column("dosing_command_id")] 
    public Guid? DosingCommandId { get; set; }
}
