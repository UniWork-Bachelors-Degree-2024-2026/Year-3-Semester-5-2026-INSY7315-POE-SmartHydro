using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Dtos;

namespace SmartHydroAPI.Services;

public static class ModeStateService
{
    public static async Task<ModeStateDto?> GetAsync(ApplicationDbContext db, string mac, CancellationToken ct = default)
    {
        mac = LoraDownlinkQueue.NormalizeMac(mac);
        var compact = mac.Replace(":", "");
        var device = await db.Devices.AsNoTracking().FirstOrDefaultAsync(x => x.MacAddress == mac || x.MacAddress == compact, ct);
        if (device == null) return null;
        var request = await db.DeviceCommands.AsNoTracking().Where(x => x.DeviceMac == mac && x.Target == "mode")
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        int? target = null;
        if (request != null) {
            using var payload = JsonDocument.Parse(request.Payload);
            if (payload.RootElement.TryGetProperty("operatingMode", out var mode)) target = mode.GetInt32();
        }
        var status = request == null ? "reported" :
            request.Succeeded == false ? "rejected" :
            (device.ModeRequestConfirmedAt >= request.CreatedAt || (device.ReportedMode == target && device.ModeReportedAt >= request.CreatedAt)) ? "confirmed" :
            DateTime.UtcNow >= request.CreatedAt.AddSeconds(180) ? "unconfirmed" : "waiting";
        var profile = device.StructureType;
        if (profile == null && device.StructureId.HasValue)
            profile = await db.Structures.Where(x => x.Id == device.StructureId).Select(x => x.StructureType).FirstOrDefaultAsync(ct);
        return new(mac, profile?.Contains("FODDER", StringComparison.OrdinalIgnoreCase) == true ? "fodder" : "hydroponics",
            target, request?.CreatedAt, request?.CommandId, device.ReportedMode, device.ReportedPreferredMode,
            device.ReportedModeReason, device.ModeReportedAt, LoraDownlinkQueue.LastGatewaySeenAt,
            LoraDownlinkQueue.LastGatewayRssi, device.LastTelemetryTransport, status, DateTime.UtcNow);
    }
}
