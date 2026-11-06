using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Services
{
    // Service managing IoT device inventory, online/offline heartbeats, friendly renaming, and structure topology
    public class DeviceService
    {
        private readonly ApplicationDbContext _database;
        private static readonly TimeSpan OnlineThreshold = TimeSpan.FromMinutes(2); // Devices seen within 2 minutes considered online

        // Injects database context
        public DeviceService(ApplicationDbContext database) => _database = database;

        // Maps Device entity to DeviceDto calculating live online status from LastSeenAt timestamp
        private static DeviceDto ToDto(Device device)
        {
            var isOnline = (DateTime.UtcNow - device.LastSeenAt) <= OnlineThreshold && !device.IsOfflineMode;
            return new DeviceDto(
                device.MacAddress,
                device.StructureId,
                device.DeviceName,
                device.DeviceType,
                device.StructureType,
                device.LocalIp,
                isOnline,
                device.IsOfflineMode,
                device.PendingOfflineTransition,
                device.LastSeenAt,
                device.CreatedAt
            );
        }

        // Retrieves all registered IoT devices, optionally filtered by structure
        public async Task<IEnumerable<DeviceDto>> GetAllAsync(int? structureId = null, CancellationToken ct = default)
        {
            var query = _database.Devices.AsNoTracking().AsQueryable();
            if (structureId.HasValue)
            {
                query = query.Where(d => d.StructureId == structureId.Value);
            }

            var entities = await query.OrderBy(d => d.DeviceName).ToListAsync(ct);
            return entities.Select(ToDto);
        }

        // Retrieves a single device DTO by MAC address
        public async Task<DeviceDto?> GetByMacAsync(string mac, CancellationToken ct = default)
        {
            var normalizedMac = mac.Trim().ToUpperInvariant();
            var entity = await _database.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.MacAddress == normalizedMac, ct);
            return entity is null ? null : ToDto(entity);
        }

        // Registers a new device with unique MAC validation and initial baseline state
        public async Task<DeviceDto> RegisterAsync(CreateDeviceDto dto, CancellationToken ct = default)
        {
            var normalizedMac = dto.MacAddress.Trim().ToUpperInvariant();

            var exists = await _database.Devices.AnyAsync(d => d.MacAddress == normalizedMac, ct);
            if (exists)
            {
                throw new InvalidOperationException($"Device with MAC address '{normalizedMac}' already exists.");
            }

            if (dto.StructureId.HasValue)
            {
                var structureExists = await _database.Structures.AnyAsync(s => s.Id == dto.StructureId.Value, ct);
                if (!structureExists)
                {
                    throw new ArgumentException($"Structure #{dto.StructureId.Value} does not exist.");
                }
            }

            var device = new Device
            {
                MacAddress = normalizedMac,
                DeviceName = dto.DeviceName.Trim(),
                DeviceType = string.IsNullOrWhiteSpace(dto.DeviceType) ? "SENSOR-NODE" : dto.DeviceType.Trim().ToUpperInvariant(),
                StructureType = dto.StructureType?.Trim().ToUpperInvariant(),
                StructureId = dto.StructureId,
                LocalIp = dto.LocalIp?.Trim(),
                IsOfflineMode = false,
                PendingOfflineTransition = false,
                LastSeenAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            _database.Devices.Add(device);

            // Create default baseline actuator state if not camera node
            if (device.DeviceType != "CAMERA-NODE")
            {
                var hasActuatorState = await _database.ActuatorStates.AnyAsync(a => a.DeviceMac == normalizedMac, ct);
                if (!hasActuatorState)
                {
                    _database.ActuatorStates.Add(new ActuatorState
                    {
                        DeviceMac = normalizedMac,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
            }

            await _database.SaveChangesAsync(ct);
            return ToDto(device);
        }

        // Updates existing device configuration
        public async Task<DeviceDto?> UpdateAsync(string mac, UpdateDeviceDto dto, CancellationToken ct = default)
        {
            var normalizedMac = mac.Trim().ToUpperInvariant();
            var device = await _database.Devices.FindAsync([normalizedMac], ct);
            if (device is null) return null;

            if (dto.StructureId.HasValue)
            {
                var structureExists = await _database.Structures.AnyAsync(s => s.Id == dto.StructureId.Value, ct);
                if (!structureExists)
                {
                    throw new ArgumentException($"Structure #{dto.StructureId.Value} does not exist.");
                }
                device.StructureId = dto.StructureId.Value;
            }
            else if (dto.StructureId == null && dto.DeviceName == null && dto.DeviceType == null && dto.StructureType == null)
            {
                // Explicit structure unassignment if needed
                device.StructureId = null;
            }

            if (!string.IsNullOrWhiteSpace(dto.DeviceName)) device.DeviceName = dto.DeviceName.Trim();
            if (!string.IsNullOrWhiteSpace(dto.DeviceType)) device.DeviceType = dto.DeviceType.Trim().ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(dto.StructureType)) device.StructureType = dto.StructureType.Trim().ToUpperInvariant();
            if (dto.LocalIp != null) device.LocalIp = dto.LocalIp.Trim();

            await _database.SaveChangesAsync(ct);
            return ToDto(device);
        }

        // Deletes a device and cleans up associated child states
        public async Task<bool> DeleteAsync(string mac, CancellationToken ct = default)
        {
            var normalizedMac = mac.Trim().ToUpperInvariant();
            var device = await _database.Devices.FindAsync([normalizedMac], ct);
            if (device is null) return false;

            _database.Devices.Remove(device);
            await _database.SaveChangesAsync(ct);
            return true;
        }

        // Assigns structure deployment profile ('VERTICAL-TOWER' or 'FODDER-SYSTEM')
        public async Task<bool> SetStructureTypeAsync(string mac, string structureType, CancellationToken ct = default)
        {
            var normalizedMac = mac.Trim().ToUpperInvariant();
            var normalizedType = structureType.Trim().ToUpperInvariant();

            return await _database.Devices
                .Where(d => d.MacAddress == normalizedMac)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.StructureType, normalizedType), ct) > 0;
        }

        // Sets offline mode transition flag (triggers device to switch to local SoftAP mode, Online mode, or LoRa mode)
        public async Task<bool> SetOfflineModeTransitionAsync(string mac, bool enableOffline, CancellationToken ct = default)
        {
            var canonical = LoraDownlinkQueue.NormalizeMac(mac);
            if (string.IsNullOrWhiteSpace(canonical)) return false;

            var clean = canonical.Replace(":", "").Replace("-", "");
            var device = await _database.Devices
                .FirstOrDefaultAsync(d => d.MacAddress.ToUpper() == canonical || d.MacAddress.ToUpper().Replace(":", "").Replace("-", "") == clean, ct);

            if (device is null)
            {
                device = new Device
                {
                    MacAddress = canonical,
                    DeviceName = $"HydroNode-{clean[^Math.Min(4, clean.Length)..]}",
                    DeviceType = "SENSOR-NODE",
                    IsOfflineMode = enableOffline,
                    PendingOfflineTransition = enableOffline,
                    LastSeenAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                };
                _database.Devices.Add(device);
                await _database.SaveChangesAsync(ct);
                return true;
            }

            device.PendingOfflineTransition = enableOffline;
            device.IsOfflineMode = enableOffline;
            await _database.SaveChangesAsync(ct);
            return true;
        }

        // Confirms that the device has acknowledged and executed the offline SoftAP transition
        public async Task<bool> ConfirmOfflineTransitionAsync(string mac, CancellationToken ct = default)
        {
            var normalizedMac = mac.Trim().ToUpperInvariant();
            return await _database.Devices
                .Where(d => d.MacAddress == normalizedMac)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.IsOfflineMode, true)
                    .SetProperty(d => d.PendingOfflineTransition, false), ct) > 0;
        }

        // Renames a device in the database via atomic ExecuteUpdateAsync
        public async Task<bool> RenameAsync(string mac, RenameDeviceDto dto, CancellationToken ct = default)
        {
            var normalizedMac = mac.Trim().ToUpperInvariant();
            return await _database.Devices
                .Where(d => d.MacAddress == normalizedMac)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.DeviceName, dto.Name), ct) > 0;
        }

        // Assigns or reassigns a device to a structure, verifying target structure existence
        public async Task<bool> AssignStructureAsync(string mac, AssignStructureDto dto, CancellationToken ct = default)
        {
            var normalizedMac = mac.Trim().ToUpperInvariant();
            if (dto.StructureId.HasValue)
            {
                var exists = await _database.Structures.AnyAsync(s => s.Id == dto.StructureId.Value, ct);
                if (!exists) throw new ArgumentException($"Structure #{dto.StructureId.Value} does not exist.");
            }

            return await _database.Devices
                .Where(d => d.MacAddress == normalizedMac)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.StructureId, dto.StructureId), ct) > 0;
        }
    }
}
