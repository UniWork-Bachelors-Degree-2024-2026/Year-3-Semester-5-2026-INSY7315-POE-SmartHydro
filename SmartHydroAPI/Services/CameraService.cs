using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Data;
using SmartHydroAPI.Extensions;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Patterns.Strategy;

namespace SmartHydroAPI.Services
{
    // Service managing ESP32-CAM command synchronization, image storage strategies, and gallery management
    public class CameraService
    {
        private static readonly ConcurrentDictionary<string, bool> PendingFlashByMac = new(StringComparer.OrdinalIgnoreCase);

        private readonly ApplicationDbContext _database;
        private readonly IImageStorageStrategy _storageStrategy;

        // Injects database context and pluggable image storage strategy
        public CameraService(ApplicationDbContext database, IImageStorageStrategy storageStrategy)
        {
            _database = database;
            _storageStrategy = storageStrategy;
        }

        // Normalizes a 12-hex-character MAC into canonical colon-separated AA:BB:CC:DD:EE:FF format
        private static string CanonicalizeMac(string mac)
        {
            var trimmed = (mac ?? string.Empty).Trim().ToUpperInvariant();
            var clean = trimmed.Replace(":", "").Replace("-", "");
            if (clean.Length == 12 && clean.All(Uri.IsHexDigit))
            {
                return string.Join(":", Enumerable.Range(0, 6).Select(i => clean.Substring(i * 2, 2)));
            }
            return trimmed;
        }

        // Ensures the camera MAC exists in the Devices table so CameraCommands foreign key constraints always succeed
        private async Task<Device> EnsureCameraDeviceRowAsync(string mac, CancellationToken ct)
        {
            var canonical = CanonicalizeMac(mac);
            var clean = canonical.Replace(":", "").Replace("-", "");

            var device = await _database.Devices
                .FirstOrDefaultAsync(d => d.MacAddress.ToUpper() == canonical || d.MacAddress.ToUpper().Replace(":", "").Replace("-", "") == clean, ct);

            if (device is null)
            {
                device = new Device
                {
                    MacAddress = canonical,
                    DeviceName = $"ESP32-CAM-{clean[^Math.Min(4, clean.Length)..]}",
                    DeviceType = "CAMERA-NODE",
                    LastSeenAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                };
                _database.Devices.Add(device);
                await _database.SaveChangesAsync(ct);
            }
            else
            {
                device.LastSeenAt = DateTime.UtcNow;
                await _database.SaveChangesAsync(ct);
            }

            return device;
        }

        // Checks if a pending photo capture exists for the specified camera MAC address
        public async Task<CameraPendingResponseDto> PollPendingCommandAsync(string mac, CancellationToken ct = default)
        {
            var device = await EnsureCameraDeviceRowAsync(mac, ct);
            var canonical = device.MacAddress;
            var clean = canonical.Replace(":", "").Replace("-", "");

            var command = await _database.CameraCommands
                .Where(c => (c.DeviceMac.ToUpper() == canonical || c.DeviceMac.ToUpper().Replace(":", "").Replace("-", "") == clean) && c.IsPending)
                .OrderByDescending(c => c.RequestedAt)
                .FirstOrDefaultAsync(ct);

            if (command is null)
            {
                return new CameraPendingResponseDto
                {
                    DeviceMac = canonical,
                    CapturePending = false,
                    Flash = false,
                    StructureId = device.StructureId,
                    StreamPending = false,
                    TimeoutSeconds = 0,
                    RequestedAt = DateTime.UtcNow
                };
            }

            // Acknowledge and clear the pending flag so it is only delivered once and won't spam the API or re-trigger indefinitely
            command.IsPending = false;
            command.CompletedAt = DateTime.UtcNow;
            await _database.SaveChangesAsync(ct);

            PendingFlashByMac.TryRemove(clean, out var useFlash);

            return new CameraPendingResponseDto
            {
                DeviceMac = canonical,
                CapturePending = true,
                Flash = useFlash,
                StructureId = device.StructureId,
                StreamPending = false,
                TimeoutSeconds = 0,
                RequestedAt = command.RequestedAt
            };
        }

        // Narrows photos to those on structures the caller can see, which for a Worker means the structures in their assigned sites
        private IQueryable<CameraCapture> VisibleCaptures(ClaimsPrincipal user)
        {
            if (user.IsAdmin())
                return _database.CameraCaptures;

            var structureIds = SiteAccess.VisibleStructureIds(_database, user);
            return _database.CameraCaptures.Where(c => c.StructureId != null && structureIds.Contains(c.StructureId.Value));
        }

        // Narrows photos to those the caller may delete or restore; Workers can view and favourite photos but not remove them
        private IQueryable<CameraCapture> ManageableCaptures(ClaimsPrincipal user)
        {
            if (user.IsAdmin())
                return _database.CameraCaptures;

            var structureIds = SiteAccess.ManageableStructureIds(_database, user);
            return _database.CameraCaptures.Where(c => c.StructureId != null && structureIds.Contains(c.StructureId.Value));
        }

        // Stores image payload through the configured IImageStorageStrategy and clears pending camera commands
        public async Task<long> UploadPhotoAsync(IFormFile file, string? deviceMac, int? structureId, string? plant, string? label, string? cameraLabel, DateTimeOffset? capturedAt, bool isFavourite, ClaimsPrincipal user, CancellationToken ct = default)
        {
            string? normalizedMac = null;
            if (!string.IsNullOrWhiteSpace(deviceMac))
            {
                var device = await EnsureCameraDeviceRowAsync(deviceMac, ct);
                normalizedMac = device.MacAddress;
                if (!structureId.HasValue || structureId.Value <= 0)
                {
                    structureId = device.StructureId;
                }
            }

            var captureLabel = "Manual Upload";
            if (normalizedMac != null)
                captureLabel = "Camera 1";

            var isAuthenticatedUser = user.Identity?.IsAuthenticated == true;

            if (structureId.HasValue && structureId.Value > 0 && (isAuthenticatedUser || normalizedMac == null))
            {
                var visibleIds = SiteAccess.VisibleStructureIds(_database, user);
                var allowed = await visibleIds.AnyAsync(id => id == structureId.Value, ct);
                if (!allowed) throw new UnauthorizedAccessException("You do not have access to this structure.");
            }
            else if (normalizedMac != null && (!structureId.HasValue || structureId.Value <= 0))
            {
                // A photo sent by a board without a structure belongs to the structure the board is fitted to, so it shows up in that structure's gallery
                var cleanMac = normalizedMac.Replace(":", "").Replace("-", "");
                structureId = await _database.Devices
                    .Where(d => d.MacAddress.ToUpper() == normalizedMac || d.MacAddress.ToUpper().Replace(":", "").Replace("-", "") == cleanMac)
                    .Select(d => d.StructureId)
                    .FirstOrDefaultAsync(ct);

                if (!structureId.HasValue)
                {
                    var recentCommand = await _database.CameraCommands
                        .Where(c => c.RequestedAt > DateTime.UtcNow.AddMinutes(-2))
                        .OrderByDescending(c => c.RequestedAt)
                        .FirstOrDefaultAsync(ct);

                    if (recentCommand != null)
                    {
                        structureId = await _database.Devices
                            .Where(d => d.MacAddress.ToUpper() == recentCommand.DeviceMac)
                            .Select(d => d.StructureId)
                            .FirstOrDefaultAsync(ct);
                    }
                }
            }

            if (structureId.HasValue && structureId.Value <= 0)
            {
                structureId = null;
            }

            var takenAt = ClampCapturedAt(capturedAt);

            // Persist image stream via Strategy Pattern (Database BLOB or Local Disk Volume)
            var storageResult = await _storageStrategy.StoreImageAsync(file, normalizedMac, ct);

            var capture = new CameraCapture
            {
                DeviceMac = normalizedMac,
                StructureId = structureId,
                ImageData = storageResult.ImageData ?? Array.Empty<byte>(),
                ContentType = storageResult.ContentType,
                ImageSizeBytes = storageResult.ImageSizeBytes,
                Plant = plant,
                Label = label,
                CameraLabel = cameraLabel ?? captureLabel,
                IsFavourite = isFavourite,
                CapturedAt = takenAt
            };

            _database.CameraCaptures.Add(capture);
            await _database.SaveChangesAsync(ct);

            // Atomic Bulk Update: Clear pending commands for this camera (if MAC provided)
            if (normalizedMac != null)
            {
                var cleanMac = normalizedMac.Replace(":", "").Replace("-", "");
                PendingFlashByMac.TryRemove(cleanMac, out _);

                await _database.CameraCommands
                    .Where(c => (c.DeviceMac.ToUpper() == normalizedMac || c.DeviceMac.ToUpper().Replace(":", "").Replace("-", "") == cleanMac || (structureId.HasValue && c.Device != null && c.Device.StructureId == structureId.Value)) && c.IsPending)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.IsPending, false)
                        .SetProperty(c => c.CompletedAt, DateTime.UtcNow), ct);
            }

            return capture.Id;
        }

        // Queues a new capture trigger command for the target camera
        public async Task<CameraCommand> TriggerCaptureAsync(string deviceMac, bool flash = false, CancellationToken ct = default)
        {
            var device = await EnsureCameraDeviceRowAsync(deviceMac, ct);
            var normalizedMac = device.MacAddress;
            var cleanMac = normalizedMac.Replace(":", "").Replace("-", "");

            PendingFlashByMac[cleanMac] = flash;

            // Throttle: avoid queueing duplicate pending commands within 5 seconds for the same device
            var cutoff = DateTime.UtcNow.AddSeconds(-5);
            var existing = await _database.CameraCommands
                .Where(c => (c.DeviceMac.ToUpper() == normalizedMac || c.DeviceMac.ToUpper().Replace(":", "").Replace("-", "") == cleanMac) && c.IsPending && c.RequestedAt > cutoff)
                .OrderByDescending(c => c.RequestedAt)
                .FirstOrDefaultAsync(ct);

            if (existing != null)
            {
                return existing;
            }

            var command = new CameraCommand
            {
                DeviceMac = normalizedMac,
                IsPending = true,
                RequestedAt = DateTime.UtcNow
            };

            _database.CameraCommands.Add(command);
            await _database.SaveChangesAsync(ct);
            return command;
        }

        // Returns binary data and content type retrieved via storage strategy for the latest image of a device
        public async Task<(byte[] Data, string ContentType)?> GetLatestImageAsync(string mac, ClaimsPrincipal user, CancellationToken ct = default)
        {
            var normalizedMac = CanonicalizeMac(mac);
            var cleanMac = normalizedMac.Replace(":", "").Replace("-", "");
            var capture = await VisibleCaptures(user)
                .AsNoTracking()
                .Where(c => c.DeviceMac != null && (c.DeviceMac.ToUpper() == normalizedMac || c.DeviceMac.ToUpper().Replace(":", "").Replace("-", "") == cleanMac) && !c.IsDeleted)
                .OrderByDescending(c => c.CapturedAt)
                .FirstOrDefaultAsync(ct);

            if (capture is null) return null;

            return await _storageStrategy.RetrieveImageAsync(capture, ct);
        }

        // Retrieves photo binary by database ID
        public async Task<(byte[] Data, string ContentType)?> GetPhotoByIdAsync(long id, ClaimsPrincipal user, CancellationToken ct = default)
        {
            var capture = await VisibleCaptures(user)
                .Include(c => c.Structure)
                .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct);
            if (capture is null) return null;

            return await _storageStrategy.RetrieveImageAsync(capture, ct);
        }

        // Retrieves photo metadata by ID
        public async Task<object?> GetPhotoMetadataByIdAsync(long id, ClaimsPrincipal user, CancellationToken ct = default)
        {
            return await VisibleCaptures(user)
                .AsNoTracking()
                .Where(c => c.Id == id && !c.IsDeleted)
                .Select(c => new
                {
                    c.Id,
                    c.DeviceMac,
                    c.StructureId,
                    c.Plant,
                    c.Label,
                    c.CameraLabel,
                    c.IsFavourite,
                    c.CapturedAt,
                    c.ImageSizeBytes,
                    c.ContentType
                })
                .FirstOrDefaultAsync(ct);
        }

        // Soft-deletes a single photo record
        public async Task<bool> DeletePhotoAsync(long id, ClaimsPrincipal user, CancellationToken ct = default)
        {
            var capture = await ManageableCaptures(user)
                .Include(c => c.Structure)
                .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct);
            if (capture is null) return false;

            capture.IsDeleted = true;
            capture.DeletedAt = DateTime.UtcNow;
            capture.DeletedByEmail = user.GetEmail();
            await _database.SaveChangesAsync(ct);
            return true;
        }

        // Restores a soft-deleted photo on a structure the caller manages
        public async Task<bool> RestorePhotoAsync(long id, ClaimsPrincipal user, CancellationToken ct = default)
        {
            var capture = await ManageableCaptures(user)
                .Include(c => c.Structure)
                .FirstOrDefaultAsync(c => c.Id == id && c.IsDeleted, ct);
            if (capture is null) return false;

            capture.IsDeleted = false;
            capture.DeletedAt = null;
            capture.DeletedByEmail = null;
            await _database.SaveChangesAsync(ct);
            return true;
        }

        // Permanently removes a soft-deleted photo (Admin only)
        public async Task<bool> PurgePhotoAsync(long id, CancellationToken ct = default)
        {
            var capture = await _database.CameraCaptures.FirstOrDefaultAsync(c => c.Id == id && c.IsDeleted, ct);
            if (capture is null) return false;

            _database.CameraCaptures.Remove(capture);
            await _database.SaveChangesAsync(ct);
            return true;
        }

        // Paginated gallery query with structure and device filtering
        public async Task<PagedResultDto<object>> GetGalleryAsync(int? structureId, string? deviceMac, string? date, ClaimsPrincipal user, int page = 1, int pageSize = 12, CancellationToken ct = default)
        {
            var query = VisibleCaptures(user).AsNoTracking().Include(c => c.Structure).Where(c => !c.IsDeleted).AsQueryable();

            if (structureId.HasValue) query = query.Where(c => c.StructureId == structureId.Value);
            if (!string.IsNullOrWhiteSpace(deviceMac))
            {
                var normalizedMac = CanonicalizeMac(deviceMac);
                var cleanMac = normalizedMac.Replace(":", "").Replace("-", "");
                query = query.Where(c => c.DeviceMac != null && (c.DeviceMac.ToUpper() == normalizedMac || c.DeviceMac.ToUpper().Replace(":", "").Replace("-", "") == cleanMac));
            }

            if (!string.IsNullOrWhiteSpace(date) && DateOnly.TryParse(date, out var filterDate))
            {
                var startUtc = filterDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                var endUtc = filterDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
                query = query.Where(c => c.CapturedAt >= startUtc && c.CapturedAt <= endUtc);
            }

            var totalCount = await query.CountAsync(ct);
            var safePage = Math.Max(1, page);
            var safePageSize = Math.Clamp(pageSize, 1, 100);

            var items = await query
                .OrderByDescending(c => c.CapturedAt)
                .Skip((safePage - 1) * safePageSize)
                .Take(safePageSize)
                .Select(c => (object)new
                {
                    c.Id,
                    c.DeviceMac,
                    c.StructureId,
                    c.Plant,
                    c.Label,
                    c.CameraLabel,
                    c.IsFavourite,
                    c.CapturedAt,
                    c.ImageSizeBytes
                })
                .ToListAsync(ct);

            var totalPages = (int)Math.Ceiling(totalCount / (double)safePageSize);
            return new PagedResultDto<object>(items, totalCount, safePage, safePageSize, totalPages);
        }

        // Retrieves photo metadata for structure gallery display (legacy non-paginated)
        public async Task<IEnumerable<object>> GetStructureGalleryAsync(int structureId, ClaimsPrincipal user, CancellationToken ct = default)
        {
            return await VisibleCaptures(user)
                .AsNoTracking()
                .Where(c => c.StructureId == structureId && !c.IsDeleted)
                .OrderByDescending(c => c.CapturedAt)
                .Select(c => (object)new
                {
                    c.Id,
                    c.DeviceMac,
                    c.Plant,
                    c.Label,
                    c.CameraLabel,
                    c.IsFavourite,
                    c.CapturedAt,
                    c.ImageSizeBytes
                })
                .ToListAsync(ct);
        }

        // Edits the details of a photo the caller can see, optionally moving it to another visible structure or replacing the image itself
        public async Task<bool> UpdatePhotoAsync(long id, IFormFile? file, int? structureId, string? plant, string? label, string? cameraLabel, DateTimeOffset? capturedAt, bool? isFavourite, ClaimsPrincipal user, CancellationToken ct = default)
        {
            var capture = await VisibleCaptures(user).FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct);
            if (capture is null) return false;

            if (structureId.HasValue && structureId != capture.StructureId)
            {
                var visibleIds = SiteAccess.VisibleStructureIds(_database, user);
                var allowed = await visibleIds.AnyAsync(sid => sid == structureId.Value, ct);
                if (!allowed) throw new UnauthorizedAccessException("You do not have access to this structure.");
                capture.StructureId = structureId;
            }

            if (plant is not null) capture.Plant = TrimToLength(plant, 100);
            if (label is not null) capture.Label = TrimToLength(label, 100);
            if (cameraLabel is not null) capture.CameraLabel = TrimToLength(cameraLabel, 50);
            if (isFavourite.HasValue) capture.IsFavourite = isFavourite.Value;
            if (capturedAt.HasValue) capture.CapturedAt = ClampCapturedAt(capturedAt);

            if (file is not null && file.Length > 0)
            {
                var storageResult = await _storageStrategy.StoreImageAsync(file, capture.DeviceMac, ct);
                capture.ImageData = storageResult.ImageData ?? Array.Empty<byte>();
                capture.ContentType = storageResult.ContentType;
                capture.ImageSizeBytes = storageResult.ImageSizeBytes;
            }

            await _database.SaveChangesAsync(ct);
            return true;
        }

        // Checks the caller may use a camera board, which means it is fitted to a structure they can see (an Admin may use any board)
        public Task<bool> CanUseCameraAsync(string mac, ClaimsPrincipal user, CancellationToken ct = default)
        {
            return DeviceAccess.CanReadDeviceAsync(_database, user, mac, ct);
        }

        // Looks up the structure id a camera device belongs to
        public async Task<int?> GetStructureIdForDeviceAsync(string deviceMac, CancellationToken ct = default)
        {
            var normalizedMac = deviceMac.Trim().ToUpperInvariant();
            var cleanMac = normalizedMac.Replace(":", "").Replace("-", "");
            return await _database.Devices
                .Where(d => d.MacAddress.ToUpper() == normalizedMac || d.MacAddress.ToUpper().Replace(":", "").Replace("-", "") == cleanMac)
                .Select(d => d.StructureId)
                .FirstOrDefaultAsync(ct);
        }

        // Turns a supplied capture time into UTC, never later than now, defaulting to now
        private static DateTime ClampCapturedAt(DateTimeOffset? capturedAt)
        {
            var now = DateTime.UtcNow;
            if (!capturedAt.HasValue) return now;

            var utc = capturedAt.Value.UtcDateTime;
            return utc > now ? now : utc;
        }

        // Trims text and shortens it to the column length, storing null when nothing is left
        private static string? TrimToLength(string value, int maxLength)
        {
            var trimmed = value.Trim();
            if (trimmed.Length == 0) return null;

            return trimmed.Length > maxLength ? trimmed.Substring(0, maxLength) : trimmed;
        }

        // Sets or toggles the favourite flag on a capture record; when a value is supplied it is set to that value, otherwise it is flipped
        public async Task<bool?> ToggleFavouriteAsync(long id, ClaimsPrincipal user, bool? value = null, CancellationToken ct = default)
        {
            var capture = await VisibleCaptures(user)
                .Include(c => c.Structure)
                .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct);
            if (capture is null) return null;

            capture.IsFavourite = value ?? !capture.IsFavourite;
            await _database.SaveChangesAsync(ct);
            return capture.IsFavourite;
        }
    }
}
