using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Data;
using SmartHydroAPI.Extensions;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Services
{
    public class StructureConflictException : InvalidOperationException
    {
        public StructureConflictException(string field, string message) : base(message)
        {
            Field = field;
        }

        public string Field { get; }
    }

    public class StructureService
    {
        private const string CameraNodeType = "CAMERA-NODE";
        private const string DefaultLabelColour = "#2ECC71";

        private static readonly TimeSpan OnlineThreshold = TimeSpan.FromMinutes(2);

        private static readonly Dictionary<string, string> DeviceProfiles = new(StringComparer.OrdinalIgnoreCase)
        {
            ["VERTICAL_TOWER"] = "VERTICAL-TOWER",
            ["FODDER_TRAY"] = "FODDER-SYSTEM"
        };

        private readonly ApplicationDbContext _database;

        public StructureService(ApplicationDbContext database)
        {
            _database = database;
        }

        // Maps a structure onto its DTO, working out live online status and the linked controller and camera boards
        private static StructureDto ToDto(Structure structure, TelemetryRealtime? latest = null)
        {
            var isOnline = structure.Devices.Any(d => (DateTime.UtcNow - d.LastSeenAt) <= OnlineThreshold && !d.IsOfflineMode);

            DateTime? lastSeen = null;
            if (structure.Devices.Count > 0)
                lastSeen = structure.Devices.Max(d => d.LastSeenAt);

            string? cameraMac = null;
            var camera = structure.Devices.FirstOrDefault(IsCamera);
            if (camera is not null)
                cameraMac = camera.MacAddress;

            string? controllerMac = null;
            var controller = structure.Devices
                .Where(d => !IsCamera(d))
                .OrderBy(d => d.CreatedAt)
                .FirstOrDefault();
            if (controller is not null)
                controllerMac = controller.MacAddress;

            return new StructureDto(
                structure.Id,
                structure.SiteId,
                structure.Name,
                structure.StructureType,
                structure.LabelColour,
                structure.LocationText,
                structure.Latitude,
                structure.Longitude,
                structure.CreatedAt,
                isOnline,
                lastSeen,
                cameraMac,
                controllerMac,
                latest?.TemperatureC,
                latest?.HumidityPercent,
                latest?.EcMs,
                latest?.PhLevel);
        }

        // Builds the structures the caller may read, following the visibility of the sites they belong to
        private IQueryable<Structure> VisibleStructures(ClaimsPrincipal user)
        {
            return SiteAccess.VisibleStructures(_database, user);
        }

        // Builds the structures the caller may change: every structure for an Admin, the structures in owned sites for an Operator, none for a Worker
        private IQueryable<Structure> ManageableStructures(ClaimsPrincipal user)
        {
            return SiteAccess.ManageableStructures(_database, user);
        }

        // Lists the active structures the caller can see, optionally narrowed to one site
        public async Task<IEnumerable<StructureDto>> GetAllAsync(ClaimsPrincipal user, Guid? siteId = null, CancellationToken ct = default)
        {
            var query = VisibleStructures(user)
                .AsNoTracking()
                .Include(s => s.Devices)
                .Where(s => !s.IsDeleted);

            if (siteId.HasValue && siteId.Value != Guid.Empty)
                query = query.Where(s => s.SiteId == siteId.Value);

            var structures = await query.OrderBy(s => s.Name).ToListAsync(ct);

            var controllerMacs = structures
                .SelectMany(s => s.Devices)
                .Where(d => !IsCamera(d))
                .Select(d => d.MacAddress)
                .Where(m => !string.IsNullOrEmpty(m))
                .Distinct()
                .ToList();

            var latestTelemetryMap = new Dictionary<string, TelemetryRealtime>(StringComparer.OrdinalIgnoreCase);
            foreach (var mac in controllerMacs)
            {
                var latest = await _database.TelemetryRealtime
                    .AsNoTracking()
                    .Where(t => t.DeviceMac == mac)
                    .OrderByDescending(t => t.RecordedAt)
                    .FirstOrDefaultAsync(ct);
                if (latest != null)
                {
                    latestTelemetryMap[mac] = latest;
                }
            }

            return structures.Select(s =>
            {
                var controller = s.Devices.Where(d => !IsCamera(d)).OrderBy(d => d.CreatedAt).FirstOrDefault();
                TelemetryRealtime? latest = null;
                if (controller != null && !string.IsNullOrEmpty(controller.MacAddress))
                {
                    latestTelemetryMap.TryGetValue(controller.MacAddress, out latest);
                }
                return ToDto(s, latest);
            });
        }

        // Lists the soft-deleted structures the caller can restore, newest deletion first, optionally narrowed to one site
        public async Task<IEnumerable<StructureDto>> GetDeletedAsync(ClaimsPrincipal user, Guid? siteId = null, CancellationToken ct = default)
        {
            var query = ManageableStructures(user)
                .AsNoTracking()
                .Include(s => s.Devices)
                .Where(s => s.IsDeleted);

            if (siteId.HasValue && siteId.Value != Guid.Empty)
                query = query.Where(s => s.SiteId == siteId.Value);

            var structures = await query.OrderByDescending(s => s.DeletedAt).ToListAsync(ct);
            return structures.Select(s => ToDto(s, null));
        }

        // Finds one active structure the caller can see
        public async Task<StructureDto?> GetByIdAsync(int id, ClaimsPrincipal user, CancellationToken ct = default)
        {
            var structure = await VisibleStructures(user)
                .AsNoTracking()
                .Include(s => s.Devices)
                .FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted, ct);

            if (structure is null)
                return null;

            var controller = structure.Devices.Where(d => !IsCamera(d)).OrderBy(d => d.CreatedAt).FirstOrDefault();
            TelemetryRealtime? latest = null;
            if (controller != null && !string.IsNullOrEmpty(controller.MacAddress))
            {
                latest = await _database.TelemetryRealtime
                    .AsNoTracking()
                    .Where(t => t.DeviceMac == controller.MacAddress)
                    .OrderByDescending(t => t.RecordedAt)
                    .FirstOrDefaultAsync(ct);
            }

            return ToDto(structure, latest);
        }

        // Creates a structure in a site the caller manages, linking its controller board and optional camera by MAC address
        public async Task<StructureDto> CreateAsync(CreateStructureDto dto, ClaimsPrincipal user, CancellationToken ct = default)
        {
            var normalizedType = NormalizeType(dto.StructureType);
            var trimmedName = dto.Name.Trim();
            var ownerEmail = SiteAccess.NormalizedEmail(user);

            if (dto.SiteId.HasValue)
                ownerEmail = await RequireManageableSiteOwnerAsync(dto.SiteId.Value, user, ct);

            await EnsureNameFreeAsync(dto.SiteId, trimmedName, null, ct);

            var labelColour = DefaultLabelColour;
            if (!string.IsNullOrWhiteSpace(dto.LabelColour))
                labelColour = dto.LabelColour.Trim();

            var structure = new Structure
            {
                SiteId = dto.SiteId,
                Name = trimmedName,
                StructureType = normalizedType,
                LabelColour = labelColour,
                LocationText = dto.LocationText?.Trim(),
                Latitude = dto.Latitude,
                Longitude = dto.Longitude,
                OwnerEmail = ownerEmail,
                CreatedAt = DateTime.UtcNow
            };

            EnsureDifferentMacs(dto.ControllerMac, dto.CameraMac);

            _database.Structures.Add(structure);
            await LinkControllerAsync(structure, dto.ControllerMac, dto.CameraMac, ct);
            await LinkCameraAsync(structure, dto.CameraMac, ct);
            await _database.SaveChangesAsync(ct);

            await _database.Entry(structure).Collection(s => s.Devices).LoadAsync(ct);
            return ToDto(structure);
        }

        // Updates a structure the caller manages, including its site, controller board and optional camera
        public async Task<bool> UpdateAsync(int id, UpdateStructureDto dto, ClaimsPrincipal user, CancellationToken ct = default)
        {
            var structure = await ManageableStructures(user)
                .Include(s => s.Devices)
                .FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted, ct);

            if (structure is null)
                return false;

            if (dto.SiteId.HasValue && dto.SiteId.Value != structure.SiteId)
            {
                structure.OwnerEmail = await RequireManageableSiteOwnerAsync(dto.SiteId.Value, user, ct);
                structure.SiteId = dto.SiteId.Value;
            }

            var trimmedName = dto.Name.Trim();
            await EnsureNameFreeAsync(structure.SiteId, trimmedName, structure.Id, ct);

            structure.Name = trimmedName;
            structure.LocationText = dto.LocationText?.Trim();
            structure.Latitude = dto.Latitude;
            structure.Longitude = dto.Longitude;

            if (!string.IsNullOrWhiteSpace(dto.LabelColour))
                structure.LabelColour = dto.LabelColour.Trim();

            if (!string.IsNullOrWhiteSpace(dto.StructureType))
                structure.StructureType = NormalizeType(dto.StructureType);

            EnsureDifferentMacs(dto.ControllerMac, dto.CameraMac);

            await LinkControllerAsync(structure, dto.ControllerMac, dto.CameraMac, ct);

            // Null leaves the camera as it is, an empty value unlinks it
            await LinkCameraAsync(structure, dto.CameraMac, ct);

            await _database.SaveChangesAsync(ct);
            return true;
        }

        // Soft-deletes a structure the caller manages so it can still be restored
        public async Task<bool> DeleteAsync(int id, ClaimsPrincipal user, CancellationToken ct = default)
        {
            var structure = await ManageableStructures(user).FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted, ct);

            if (structure is null)
                return false;

            structure.IsDeleted = true;
            structure.DeletedAt = DateTime.UtcNow;
            structure.DeletedByEmail = user.GetEmail();
            await _database.SaveChangesAsync(ct);
            return true;
        }

        // Restores a soft-deleted structure the caller manages
        public async Task<bool> RestoreAsync(int id, ClaimsPrincipal user, CancellationToken ct = default)
        {
            var structure = await ManageableStructures(user).FirstOrDefaultAsync(s => s.Id == id && s.IsDeleted, ct);

            if (structure is null)
                return false;

            // A structure cannot come back inside a site that is itself deleted, or it would be restored but still invisible
            if (structure.SiteId.HasValue)
            {
                var siteIsActive = await _database.Sites.AnyAsync(s => s.Id == structure.SiteId.Value && !s.IsDeleted, ct);

                if (!siteIsActive)
                    throw new StructureConflictException("site", "This structure's site has been deleted. Restore the site first, then restore the structure.");
            }

            // Another active structure may have taken the name while this one was deleted
            await EnsureNameFreeAsync(structure.SiteId, structure.Name, structure.Id, ct);

            structure.IsDeleted = false;
            structure.DeletedAt = null;
            structure.DeletedByEmail = null;
            await _database.SaveChangesAsync(ct);
            return true;
        }

        // Permanently removes a soft-deleted structure along with the history of its devices
        public async Task<bool> PurgeAsync(int id, CancellationToken ct = default)
        {
            var structure = await _database.Structures.FirstOrDefaultAsync(s => s.Id == id && s.IsDeleted, ct);

            if (structure is null)
                return false;

            var macs = await _database.Devices.Where(d => d.StructureId == id).Select(d => d.MacAddress).ToListAsync(ct);
            if (macs.Count > 0)
            {
                await _database.TelemetryRealtime.Where(t => macs.Contains(t.DeviceMac)).ExecuteDeleteAsync(ct);
                await _database.TelemetryDailyHistory.Where(t => macs.Contains(t.DeviceMac)).ExecuteDeleteAsync(ct);
                await _database.ActuatorHistory.Where(a => macs.Contains(a.DeviceMac)).ExecuteDeleteAsync(ct);
                await _database.ActuatorStates.Where(a => macs.Contains(a.DeviceMac)).ExecuteDeleteAsync(ct);
                await _database.DosingCommands.Where(d => macs.Contains(d.DeviceMac)).ExecuteDeleteAsync(ct);
                await _database.CameraCommands.Where(c => macs.Contains(c.DeviceMac)).ExecuteDeleteAsync(ct);
                await _database.CameraCaptures.Where(c => c.StructureId == id).ExecuteDeleteAsync(ct);
                await _database.Alerts.Where(a => a.StructureId == id).ExecuteDeleteAsync(ct);
                await _database.Devices.Where(d => d.StructureId == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.StructureId, (int?)null), ct);
            }

            // Thresholds are only removed on a permanent purge; a soft delete keeps them so Undo brings them back
            await _database.Thresholds.Where(t => t.StructureId == id).ExecuteDeleteAsync(ct);

            _database.Structures.Remove(structure);
            await _database.SaveChangesAsync(ct);
            return true;
        }

        // Builds the single-call dashboard summary for a structure the caller can see
        public async Task<StructureDashboardSummaryDto?> GetDashboardSummaryAsync(int structureId, ClaimsPrincipal user, CancellationToken ct = default)
        {
            var structure = await VisibleStructures(user)
                .AsNoTracking()
                .Include(s => s.Devices)
                .FirstOrDefaultAsync(s => s.Id == structureId && !s.IsDeleted, ct);

            if (structure is null)
                return null;

            var isOnline = structure.Devices.Any(d => (DateTime.UtcNow - d.LastSeenAt) <= OnlineThreshold && !d.IsOfflineMode);
            var deviceMacs = structure.Devices.Select(d => d.MacAddress).ToList();

            var latestTelemetry = await _database.TelemetryRealtime
                .AsNoTracking()
                .Where(t => deviceMacs.Contains(t.DeviceMac))
                .OrderByDescending(t => t.RecordedAt)
                .Select(t => new TelemetrySnapshotDto(
                    t.PhLevel,
                    t.EcMs,
                    t.LightLux,
                    t.WaterLevelRaw,
                    t.WaterLevelDetected,
                    t.TemperatureC,
                    t.HumidityPercent,
                    t.RecordedAt))
                .FirstOrDefaultAsync(ct);

            ActuatorStateResponseDto? actuatorDto = null;
            // The controller board holds the actuator state, so the camera is skipped here
            var primaryDeviceMac = structure.Devices
                .Where(d => !IsCamera(d))
                .OrderBy(d => d.CreatedAt)
                .Select(d => d.MacAddress)
                .FirstOrDefault();
            if (primaryDeviceMac is not null)
            {
                var state = await _database.ActuatorStates
                    .AsNoTracking()
                    .FirstOrDefaultAsync(a => a.DeviceMac == primaryDeviceMac, ct);

                if (state is not null)
                {
                    actuatorDto = new ActuatorStateResponseDto(
                        state.DeviceMac,
                        state.GrowLight,
                        state.CirculationFan,
                        state.MainWaterPump,
                        state.ExtractorFan,
                        state.PhUpPump,
                        state.PhDownPump,
                        state.NutrientUpPump,
                        state.DilutionDownPump,
                        state.SpraySystem,
                        state.UpdatedAt);
                }
            }

            var unresolvedCount = await _database.Alerts
                .AsNoTracking()
                .CountAsync(a => a.StructureId == structureId && !a.IsResolved && !a.IsDeleted, ct);

            var recentAlerts = await _database.Alerts
                .AsNoTracking()
                .Where(a => a.StructureId == structureId && !a.IsResolved && !a.IsDeleted)
                .OrderByDescending(a => a.OccurredAt)
                .Take(5)
                .Select(a => new AlertResponseDto
                {
                    Id = a.Id,
                    StructureId = a.StructureId,
                    DeviceMac = a.DeviceMac,
                    Component = a.Component,
                    Severity = a.Severity,
                    Value = a.Value,
                    Detail = a.Detail,
                    OccurredAt = a.OccurredAt,
                    IsResolved = a.IsResolved
                })
                .ToListAsync(ct);

            var pendingDosingCount = await _database.DosingCommands
                .AsNoTracking()
                .CountAsync(d => deviceMacs.Contains(d.DeviceMac) && (d.Status == "PENDING" || d.Status == "DISPATCHED"), ct);

            var devices = structure.Devices.Select(d => new DeviceDto(
                d.MacAddress,
                d.StructureId,
                d.DeviceName,
                d.DeviceType,
                d.StructureType,
                d.LocalIp,
                (DateTime.UtcNow - d.LastSeenAt) <= OnlineThreshold && !d.IsOfflineMode,
                d.IsOfflineMode,
                d.PendingOfflineTransition,
                d.LastSeenAt,
                d.CreatedAt)).ToList();

            string? cameraMac = null;
            DeviceDto? cameraDto = null;
            var cameraDevice = structure.Devices.FirstOrDefault(IsCamera);
            if (cameraDevice is not null)
            {
                cameraMac = cameraDevice.MacAddress;
                cameraDto = devices.FirstOrDefault(d => d.MacAddress == cameraDevice.MacAddress);
            }

            return new StructureDashboardSummaryDto
            {
                StructureId = structure.Id,
                SiteId = structure.SiteId,
                StructureName = structure.Name,
                StructureType = structure.StructureType,
                LabelColour = structure.LabelColour,
                LocationText = structure.LocationText,
                IsOnline = isOnline,
                LatestTelemetry = latestTelemetry,
                ActuatorState = actuatorDto,
                UnresolvedAlertsCount = unresolvedCount,
                RecentAlerts = recentAlerts,
                PendingDosingCommandsCount = pendingDosingCount,
                Devices = devices,
                CameraMac = cameraMac,
                CameraDevice = cameraDto
            };
        }

        // Accepts only the VERTICAL_TOWER and FODDER_TRAY structure types
        private static string NormalizeType(string structureType)
        {
            var normalizedType = (structureType ?? string.Empty).Trim().ToUpperInvariant();

            if (!DeviceProfiles.ContainsKey(normalizedType))
                throw new ArgumentException("Structure type must be strictly 'VERTICAL_TOWER' or 'FODDER_TRAY'.");

            return normalizedType;
        }

        // Returns the owner of a site the caller manages, refusing a site they cannot change
        private async Task<string> RequireManageableSiteOwnerAsync(Guid siteId, ClaimsPrincipal user, CancellationToken ct)
        {
            var manageableIds = SiteAccess.ManageableSiteIds(_database, user);

            var site = await _database.Sites
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == siteId && manageableIds.Contains(s.Id), ct);

            if (site is null)
                throw new ArgumentException("That site could not be found, or you cannot add structures to it.");

            if (string.IsNullOrWhiteSpace(site.OwnerEmail))
                return SiteAccess.NormalizedEmail(user);

            return site.OwnerEmail.Trim().ToLowerInvariant();
        }

        // Refuses a name another active structure in the same site already uses, ignoring case
        private async Task EnsureNameFreeAsync(Guid? siteId, string name, int? excludeId, CancellationToken ct)
        {
            if (!siteId.HasValue)
                return;

            var lowerName = name.ToLower();
            var query = _database.Structures.Where(s => s.SiteId == siteId.Value && !s.IsDeleted && s.Name.ToLower() == lowerName);

            if (excludeId.HasValue)
                query = query.Where(s => s.Id != excludeId.Value);

            if (await query.AnyAsync(ct))
                throw new StructureConflictException("name", "A structure with this name already exists in this site.");
        }

        // Links the controller board with the given MAC to the structure, registering the board ahead of its first reading when it is new
        private async Task LinkControllerAsync(Structure structure, string? controllerMac, string? cameraMac, CancellationToken ct)
        {
            if (controllerMac is null)
                return;

            var normalizedMac = LoraDownlinkQueue.NormalizeMac(controllerMac);
            var keepCamera = cameraMac is not null ? LoraDownlinkQueue.NormalizeMac(cameraMac) : null;

            // Unlinks old controller boards, but never the camera being saved in the same request
            foreach (var linked in structure.Devices.Where(d => !IsCamera(d) && d.MacAddress != normalizedMac && d.MacAddress != keepCamera).ToList())
                linked.StructureId = null;

            if (normalizedMac.Length == 0)
                return;

            var device = await _database.Devices.FindAsync([normalizedMac], ct);
            if (device is null)
            {
                var compactMac = normalizedMac.Replace(":", "");
                if (compactMac != normalizedMac)
                    device = await _database.Devices.FindAsync([compactMac], ct);
            }

            if (device is null)
            {
                device = new Device
                {
                    MacAddress = normalizedMac,
                    DeviceName = $"{structure.Name} Controller",
                    LastSeenAt = DateTime.UnixEpoch,
                    CreatedAt = DateTime.UtcNow
                };

                _database.Devices.Add(device);
                _database.ActuatorStates.Add(new ActuatorState { DeviceMac = normalizedMac, UpdatedAt = DateTime.UtcNow });
            }
            else
            {
                await EnsureDeviceFreeAsync(device, structure.Id, ct);
            }

            device.Structure = structure;

            if (structure.Id != 0)
                device.StructureId = structure.Id;

            if (DeviceProfiles.TryGetValue(structure.StructureType, out var profile))
                device.StructureType = profile;
        }

        // Refuses a board that is a camera, or that is already linked to a different active structure
        private async Task EnsureDeviceFreeAsync(Device device, int structureId, CancellationToken ct)
        {
            if (IsCamera(device))
                throw new StructureConflictException("controllerMac", "This MAC address belongs to a camera, not a controller board.");

            if (!device.StructureId.HasValue || device.StructureId.Value == structureId)
                return;

            var linkedToActive = await _database.Structures.AnyAsync(s => s.Id == device.StructureId.Value && !s.IsDeleted, ct);

            if (linkedToActive)
                throw new StructureConflictException("controllerMac", "This MAC address is already linked to another structure.");
        }

        // Links the camera with the given MAC to the structure, registering it ahead of its first photo when it is new
        private async Task LinkCameraAsync(Structure structure, string? cameraMac, CancellationToken ct)
        {
            if (cameraMac is null)
                return;

            var normalizedMac = LoraDownlinkQueue.NormalizeMac(cameraMac);

            // Only one camera per structure
            foreach (var linked in structure.Devices.Where(d => IsCamera(d) && d.MacAddress != normalizedMac).ToList())
                linked.StructureId = null;

            if (normalizedMac.Length == 0)
                return;

            var device = await _database.Devices.FindAsync([normalizedMac], ct);
            if (device is null)
            {
                var compactMac = normalizedMac.Replace(":", "");
                if (compactMac != normalizedMac)
                    device = await _database.Devices.FindAsync([compactMac], ct);
            }

            if (device is null)
            {
                device = new Device
                {
                    MacAddress = normalizedMac,
                    DeviceName = $"{structure.Name} Camera",
                    DeviceType = CameraNodeType,
                    LastSeenAt = DateTime.UnixEpoch,
                    CreatedAt = DateTime.UtcNow
                };

                _database.Devices.Add(device);
            }
            else
            {
                await EnsureCameraFreeAsync(device, structure.Id, ct);
                device.DeviceType = CameraNodeType;
            }

            device.Structure = structure;

            if (structure.Id != 0)
                device.StructureId = structure.Id;
        }

        // Refuses a MAC that is another structure's controller
        private async Task EnsureCameraFreeAsync(Device device, int structureId, CancellationToken ct)
        {
            if (!device.StructureId.HasValue || device.StructureId.Value == structureId)
            {
                // A controller board on this same structure can't also be its camera
                if (device.StructureId == structureId && !IsCamera(device))
                    throw new StructureConflictException("cameraMac", "This MAC address is the structure's controller board, not a camera.");

                return;
            }

            var linkedToActive = await _database.Structures.AnyAsync(s => s.Id == device.StructureId.Value && !s.IsDeleted, ct);

            if (!linkedToActive)
                return;

            var message = IsCamera(device)
                ? "This camera is already linked to another structure."
                : "This MAC address belongs to another structure's controller board.";

            throw new StructureConflictException("cameraMac", message);
        }

        // True for a camera board, whichever spelling it was saved with
        private static bool IsCamera(Device device)
        {
            return string.Equals(device.DeviceType?.Replace('_', '-'), CameraNodeType, StringComparison.OrdinalIgnoreCase);
        }

        // The controller and the camera are two different boards
        private static void EnsureDifferentMacs(string? controllerMac, string? cameraMac)
        {
            var controller = controllerMac is not null ? LoraDownlinkQueue.NormalizeMac(controllerMac) : null;
            var camera = cameraMac is not null ? LoraDownlinkQueue.NormalizeMac(cameraMac) : null;

            if (!string.IsNullOrEmpty(controller) && controller == camera)
                throw new StructureConflictException("cameraMac", "The camera and the controller can't have the same MAC address.");
        }
    }
}