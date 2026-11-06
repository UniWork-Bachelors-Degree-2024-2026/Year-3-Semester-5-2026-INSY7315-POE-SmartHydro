using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;
namespace SmartHydroAPI.Services;

// The controller runs the loop locally in all three modes. The API records the matching recommendation.
public sealed class AutomationService(ApplicationDbContext db)
{
    public async Task ObserveAsync(TelemetryIngestDto dto, decimal minPh, decimal maxPh, decimal minEc, decimal maxEc, CancellationToken ct)
    {
        if (!dto.IsLive || !dto.Sequence.HasValue) return;
        if (dto.Ec >= 2m && !await db.ActivityLogs.AnyAsync(x => x.DeviceMac == dto.DeviceMac && x.Action == "EC_SENSOR" && x.ReceivedAt > DateTime.UtcNow.AddMinutes(-15), ct))
        {
            db.ActivityLogs.Add(new ActivityLog { DeviceMac = dto.DeviceMac, EventKey = $"ec-limit:{dto.Sequence}", Source = "Safety Interlock", Action = "EC_SENSOR",
                Status = "BLOCKED", TriggerValue = dto.Ec, RecordedAt = dto.RecordedAtUtc,
                Details = "Automatic EC dosing paused: the SEN0244 TDS probe is at or beyond its approximate 2.0 mS/cm rated EC range. Plant thresholds are preserved." });
            await db.SaveChangesAsync(ct);
        }
        var plan = DosingPolicy.Plan(dto.Ph, minPh, maxPh, dto.MlPh, true)
            ?? (dto.Ec.HasValue ? DosingPolicy.Plan(dto.Ec.Value, minEc, maxEc, dto.MlEc, false) : null);
        if (plan == null) return;
        var cutoff = DateTime.UtcNow.AddMinutes(-15);
        if (await db.ActivityLogs.AnyAsync(x => x.DeviceMac == dto.DeviceMac && x.Action == plan.Pump &&
            x.Status == "RECOMMENDATION" && x.ReceivedAt > cutoff, ct)) return;
        db.ActivityLogs.Add(new ActivityLog { DeviceMac = dto.DeviceMac, EventKey = $"recommendation:{dto.Sequence}",
            Source = plan.Source, Action = plan.Pump, Status = "RECOMMENDATION", RecordedAt = dto.RecordedAtUtc,
            TriggerValue = plan.Value, Minimum = plan.Low, Maximum = plan.High, EstimatedMl = plan.EstimatedMl,
            RuntimeMs = plan.RuntimeMs, Details = ActivityLogService.DoseDetails(plan.Pump, plan.Value, plan.Low, plan.High, plan.RuntimeMs, true)
                + ActivityLogService.ReservoirDetails(plan.Pump, plan.Value, plan.Low, plan.High, dto.SystemType)
                + $" | ML class {plan.Prediction} | Recommendation only; the edge controller owns dosing and safety checks." });
        await db.SaveChangesAsync(ct);
    }
}
