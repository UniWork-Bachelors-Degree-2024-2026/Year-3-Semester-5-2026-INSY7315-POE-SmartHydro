using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SmartHydroAPI.Models.Dtos;

public class TelemetryCaptureMetadata
{
    [JsonPropertyName("seq"), Range(1, long.MaxValue)]
    public long? Sequence { get; set; }

    [JsonPropertyName("age"), Range(0, 315360000)]
    public long? Age { get; set; }

    [JsonPropertyName("ts"), Range(1704067200L, 4102444800L)]
    public long? CapturedAtUnix { get; set; }

    [JsonPropertyName("tu")]
    public bool CaptureTimeUncertain { get; set; }

    [JsonPropertyName("sys"), MaxLength(20)]
    public string? SystemType { get; set; }

    [JsonIgnore] public string Transport { get; set; } = "wifi";
    [JsonIgnore] public bool IsOutOfOrder { get; set; }
    [JsonIgnore] public bool WasDuplicate { get; set; }
    [JsonIgnore] public DateTime? ReceivedAtUtc { get; set; }
    [JsonIgnore] public DateTime RecordedAtUtc => (ReceivedAtUtc ?? DateTime.UtcNow).AddSeconds(-(Age ?? 0));
    [JsonIgnore] public bool IsLive => !WasDuplicate && !IsOutOfOrder && !CaptureTimeUncertain && (Age ?? 0) < 30;

    public void PrepareCapture()
    {
        ReceivedAtUtc ??= DateTime.UtcNow;
        if (CapturedAtUnix.HasValue && !CaptureTimeUncertain)
        {
            var elapsed = (long)(ReceivedAtUtc.Value - DateTimeOffset.FromUnixTimeSeconds(CapturedAtUnix.Value).UtcDateTime).TotalSeconds;
            if (elapsed < -5) CaptureTimeUncertain = true;
            else Age = Math.Max(0, elapsed);
        }
    }
}
