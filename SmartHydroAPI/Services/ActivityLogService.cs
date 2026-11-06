using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;
namespace SmartHydroAPI.Services;

public static class ActivityLogService
{
    static readonly string[] Sources = ["User - MVC", "User - BLE", "User - SoftAP", "Threshold Auto-Dosing", "ML Recommendation", "Schedule Timer", "Safety Interlock"];
    static readonly string[] Actions = ["main_water_pump", "grow_light", "circulation_fan", "extractor_fan", "PH_UP", "PH_DOWN", "NUTRIENT_UP", "DILUTION_DOWN"];
    static readonly string[] Phases = ["GPIO_ON", "GPIO_OFF", "DOSE_STARTED", "DOSE_COMPLETED", "REFUSED", "CANCELLED", "SAFETY_CUTOFF"];
    public static string DoseDetails(string action, decimal? value, decimal? low, decimal? high, int duration, bool proportional = false)
    {
        var context = value.HasValue && low.HasValue && high.HasValue
            ? string.Create(CultureInfo.InvariantCulture, $"reading {value:0.00}, range {low:0.00}–{high:0.00}, midpoint {(low + high) / 2:0.00}; ")
            : "manual request; reading and thresholds are recorded separately by the controller; ";
        var text = context + string.Create(CultureInfo.InvariantCulture, $"{action}: requested estimate {duration / 800m:0.00} mL = {duration} ms / 800 ms/mL.");
        if (proportional && value.HasValue && low.HasValue && high.HasValue) {
            var error = Math.Abs(value.Value - (low.Value + high.Value) / 2m);
            text += string.Create(CultureInfo.InvariantCulture, $" | absolute midpoint error {error:0.00}; ") +
                (action.StartsWith("PH_") ? "pulse estimate = 0.5 + 1.5 × clamp((error - 0.10) / 0.90, 0, 1) mL."
                    : "pulse estimate = 1 + 4 × clamp(error / max(range width, 0.10), 0, 1) mL.");
        }
        text += action.StartsWith("PH_") ? " pH pulse 0.5–2 mL; mixing lockout 15m; pH cap 10 mL/hour. The 0.25-pH shift limit is unverified without calibration."
            : " EC pulse uses a bounded proportional estimate; mixing lockout 10m. Nutrient strength-to-EC response is uncalibrated.";
        if (action == "DILUTION_DOWN") text += " EC Down adds clean water. A small pulse does not establish that the target EC has been reached.";
        return text + " Tubes must be primed; estimated volume is not measured delivery.";
    }
    public static string ReservoirDetails(string action, decimal? value, decimal? low, decimal? high, string? system)
    {
        if (system is not ("hydroponics" or "fodder")) return " Reservoir volume was not recorded for this event.";
        decimal minLitres = system == "fodder" ? 20 : 25, maxLitres = system == "fodder" ? 25 : 30;
        var text = $" Reservoir operating volume is approximately {minLitres}–{maxLitres} L.";
        if (action == "NUTRIENT_UP") text += $" The supplied CFT label rate of 1 mL/L would be {minLitres}–{maxLitres} mL for this water volume; it does not calibrate the EC change from an extra dose.";
        if (action == "DILUTION_DOWN" && value.HasValue && low.HasValue && high.HasValue && low + high > 0)
        {
            var target = (low.Value + high.Value) / 2m;
            if (value > target) {
                var ratio = value.Value / target - 1m;
                text += string.Create(CultureInfo.InvariantCulture, $" Ideal zero-EC dilution water: added mL = reservoir L × 1000 × (current EC / midpoint EC - 1), approximately {minLitres * 1000 * ratio:0}–{maxLitres * 1000 * ratio:0} mL. Incoming water EC and reservoir headroom are unmeasured, so this is context, not a pump command.");
            }
        }
        return text;
    }
    public static async Task<bool> IngestAsync(ApplicationDbContext db, LoraCompactTelemetryDto dto, CancellationToken ct)
    {
        if (!dto.Sequence.HasValue || dto.ActivityPhase is not (>= 0 and <= 6) || dto.ActivityAction is not (>= 0 and <= 7) || dto.ActivitySource is not (>= 0 and <= 6)) throw new ArgumentException("Activity sequence, action and source are required.");
        dto.PrepareCapture(); var mac = LoraDownlinkQueue.NormalizeMac(dto.DeviceMac); var key = "edge:" + dto.Sequence;
        var action = Actions[dto.ActivityAction.Value]; var phase = Phases[dto.ActivityPhase!.Value]; var duration = dto.ActivityDuration ?? 0;
        var details = dto.ActivityAction >= 4 ? DoseDetails(action, dto.ActivityValue, dto.ActivityMin, dto.ActivityMax, duration, dto.ActivitySource is 3 or 4)
            : action + (dto.ActivityPhase == 0 ? " reported ON" : " reported OFF");
        if (dto.ActivityAction >= 4) details += ReservoirDetails(action, dto.ActivityValue, dto.ActivityMin, dto.ActivityMax, dto.SystemType);
        details += $" | ML class {dto.ActivityPrediction ?? 2} | {phase} | " + (dto.ActivityCid > 0 ? $"cid #{dto.ActivityCid}" : "local action; no cloud CID");
        string[] reasons = ["accepted", "sensor fault", "pump busy", "pulse limit", "mixing lockout", "pH hourly cap", "safety storage unavailable", "cutoff timer unavailable", "outer safety limit", "dose count limit", "sensor_stale", "sensor_recovering"];
        if (dto.ActivityReason > 0) details += " | " + reasons[Math.Min(dto.ActivityReason.Value, reasons.Length - 1)];
        if (dto.ActivityPhase == 4) details += " | Refused; no dose was started.";
        // Retransmissions share an event key; the database handles simultaneous receipts too.
        if (await db.ActivityLogs.AnyAsync(x => x.DeviceMac == mac && x.EventKey == key, ct)) return true;
        db.ActivityLogs.Add(new() { DeviceMac = mac, EventKey = key, Source = Sources[dto.ActivitySource.Value], Action = action,
            Status = phase, Details = details, TriggerValue = dto.ActivityValue, Minimum = dto.ActivityMin, Maximum = dto.ActivityMax,
            RuntimeMs = duration, EstimatedMl = dto.ActivityAction >= 4 ? duration / 800m : null, CommandId = dto.ActivityCid,
            CaptureTimeUncertain = dto.CaptureTimeUncertain, RecordedAt = dto.CaptureTimeUncertain ? null : dto.RecordedAtUtc });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { db.ChangeTracker.Clear(); }
        return true;
    }
    public static async Task CommandAsync(ApplicationDbContext db, DeviceCommand command, string phase, CancellationToken ct)
    {
        var key = $"command:{command.RequestId}:{phase}";
        if (await db.ActivityLogs.AnyAsync(x => x.DeviceMac == command.DeviceMac && x.EventKey == key, ct)) return;
        var dose = command.DosingCommandId.HasValue ? await db.DosingCommands.FindAsync([command.DosingCommandId.Value], ct) : null;
        using var payload = JsonDocument.Parse(command.Payload);
        bool phOverride = payload.RootElement.TryGetProperty("po", out var flag) && flag.ValueKind == JsonValueKind.True;
        db.ActivityLogs.Add(new() { DeviceMac = command.DeviceMac, EventKey = key, Source = "User - MVC", Action = dose?.DosingType ?? command.Target,
            Status = phase, CommandId = command.CommandId, RuntimeMs = dose?.CalculatedDurationMs, EstimatedMl = dose?.TargetVolumeMl,
            RecordedAt = DateTime.UtcNow, Details = $"[{phase}] {dose?.DosingType ?? command.Target} | cid #{command.CommandId} | " +
                (dose == null ? "Controller confirmation required." : DoseDetails(dose.DosingType, null, null, null, dose.CalculatedDurationMs) + ReservoirDetails(dose.DosingType, null, null, null, command.SystemType)) +
                (phOverride ? " | Manual pH fault override confirmed; current pH is unavailable and historical pH is not required. Automatic dosing remains sensor-guarded." : "") + " | " + command.Reason + " | requested by " + command.Source });
    }
}
