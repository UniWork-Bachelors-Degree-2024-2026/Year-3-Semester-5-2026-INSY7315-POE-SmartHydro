using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Dtos;

namespace SmartHydroAPI.Services;

public static class PhDisplayFallback
{
    public static async Task<bool> IsTowerAsync(ApplicationDbContext db, string mac, CancellationToken ct = default)
    {
        var canonical = LoraDownlinkQueue.NormalizeMac(mac);
        var compact = canonical.Replace(":", "");
        var profile = await db.Devices.AsNoTracking().Where(x => x.MacAddress == canonical || x.MacAddress == compact)
            .Select(x => x.Structure != null ? x.Structure.StructureType : x.StructureType).FirstOrDefaultAsync(ct);
        profile = profile?.Replace("-", "").Replace("_", "").ToUpperInvariant();
        return profile is "VERTICALTOWER" or "HYDROPONICS" or "HYDROPONIC";
    }

    public static async Task<PhDisplayFallbackDto?> GetAsync(ApplicationDbContext db, string mac,
        decimal currentPh, DateTime currentCapture, CancellationToken ct = default)
    {
        if (currentPh is >= 0m and <= 14m) return null;
        var canonical = LoraDownlinkQueue.NormalizeMac(mac);
        var compact = canonical.Replace(":", "");
        if (!await IsTowerAsync(db, canonical, ct)) return null;
        var now = DateTime.UtcNow;
        var cutoff = currentCapture < now ? currentCapture : now;
        // Match the tower's accepted cache range (PH_PLAUSIBLE_MIN/MAX).
        // A numeric historical value alone does not establish a usable reading.
        // Each accepted tower reading is already derived from a filtered voltage batch.
        // Capture time, not upload time, determines the retained reading's age.
        var previous = await db.TelemetryRealtime.AsNoTracking()
            .Where(x => (x.DeviceMac == canonical || x.DeviceMac == compact) && !x.CaptureTimeUncertain
                && x.PhLevel >= 2m && x.PhLevel <= 12.5m && x.RecordedAt <= cutoff)
            .OrderByDescending(x => x.RecordedAt).ThenByDescending(x => x.Id)
            .Select(x => new { x.PhLevel, x.RecordedAt }).FirstOrDefaultAsync(ct);
        return previous == null ? null : new(previous.PhLevel, previous.RecordedAt,
            Math.Max(0L, (long)(now - previous.RecordedAt).TotalSeconds));
    }
}
