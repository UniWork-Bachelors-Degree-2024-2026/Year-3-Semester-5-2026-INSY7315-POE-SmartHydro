using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Data;
using SmartHydroAPI.Extensions;

namespace SmartHydroAPI.Services
{
    // Decides whether the caller may read or control the readings and relays of a device, following the same site rules as structures
    public static class DeviceAccess
    {
        // Checks whether the caller may read a device: any device for an Admin, otherwise only boards fitted to a structure they can see
        public static async Task<bool> CanReadDeviceAsync(ApplicationDbContext database, ClaimsPrincipal user, string mac, CancellationToken ct)
        {
            var normalizedMac = NormalizeMac(mac);

            if (normalizedMac.Length == 0)
                return false;

            var compactMac = normalizedMac.Replace(":", "");

            if (user.IsAdmin())
                return await AdminCanUseDeviceAsync(database, normalizedMac, compactMac, ct);

            var structureIds = SiteAccess.VisibleStructureIds(database, user);

            return await database.Devices.AnyAsync(d =>
                (d.MacAddress.ToUpper() == normalizedMac || d.MacAddress.ToUpper() == compactMac)
                && d.StructureId != null
                && structureIds.Contains(d.StructureId.Value), ct);
        }

        // Checks whether the caller may switch a device's relays: any device for an Admin, otherwise only boards fitted to a structure they manage
        public static async Task<bool> CanControlDeviceAsync(ApplicationDbContext database, ClaimsPrincipal user, string mac, CancellationToken ct)
        {
            var normalizedMac = NormalizeMac(mac);

            if (normalizedMac.Length == 0)
                return false;

            var compactMac = normalizedMac.Replace(":", "");

            if (user.IsAdmin())
                return await AdminCanUseDeviceAsync(database, normalizedMac, compactMac, ct);

            var structureIds = SiteAccess.ManageableStructures(database, user)
                .Where(s => !s.IsDeleted)
                .Select(s => s.Id);

            return await database.Devices.AnyAsync(d =>
                (d.MacAddress.ToUpper() == normalizedMac || d.MacAddress.ToUpper() == compactMac)
                && d.StructureId != null
                && structureIds.Contains(d.StructureId.Value), ct);
        }

        // Checks whether the caller may switch relays or add doses on a device: any device for an Admin, otherwise only boards fitted to a structure they can see, so a Worker is limited to their assigned sites
        public static async Task<bool> CanOperateDeviceAsync(ApplicationDbContext database, ClaimsPrincipal user, string mac, CancellationToken ct)
        {
            var normalizedMac = NormalizeMac(mac);

            if (normalizedMac.Length == 0)
                return false;

            var compactMac = normalizedMac.Replace(":", "");

            if (user.IsAdmin())
                return await AdminCanUseDeviceAsync(database, normalizedMac, compactMac, ct);

            var structureIds = SiteAccess.VisibleStructures(database, user)
                .Where(s => !s.IsDeleted)
                .Select(s => s.Id);

            return await database.Devices.AnyAsync(d =>
                (d.MacAddress.ToUpper() == normalizedMac || d.MacAddress.ToUpper() == compactMac)
                && d.StructureId != null
                && structureIds.Contains(d.StructureId.Value), ct);
        }

        // Checks whether the caller may read the readings of a structure: any active structure for an Admin, otherwise only structures they can see
        public static async Task<bool> CanReadStructureAsync(ApplicationDbContext database, ClaimsPrincipal user, int structureId, CancellationToken ct)
        {
            if (user.IsAdmin())
                return await database.Structures.AnyAsync(s => s.Id == structureId && !s.IsDeleted, ct);

            return await SiteAccess.VisibleStructureIds(database, user).AnyAsync(id => id == structureId, ct);
        }

        // Checks the device exists and is not fitted to a deleted structure, the same rule CanReadStructureAsync applies to Admins for structures
        private static async Task<bool> AdminCanUseDeviceAsync(ApplicationDbContext database, string normalizedMac, string compactMac, CancellationToken ct)
        {
            return await database.Devices.AnyAsync(d =>
                (d.MacAddress.ToUpper() == normalizedMac || d.MacAddress.ToUpper() == compactMac)
                && (d.StructureId == null || !d.Structure!.IsDeleted), ct);
        }

        // Trims, upper-cases, and formats a 12-hex MAC address as AA:BB:CC:DD:EE:FF so it matches LoRa and cloud device records
        private static string NormalizeMac(string? mac)
        {
            if (string.IsNullOrWhiteSpace(mac))
                return string.Empty;

            return LoraDownlinkQueue.NormalizeMac(mac);
        }
    }
}
