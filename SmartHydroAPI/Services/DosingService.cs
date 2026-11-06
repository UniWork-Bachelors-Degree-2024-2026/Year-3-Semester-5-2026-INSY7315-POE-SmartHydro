using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Services
{
    // Concrete service managing volumetric dosing calculations and hardware queue lifecycle
    public class DosingService : IDosingService
    {
        private readonly ApplicationDbContext _database;
        private readonly IConfiguration? _configuration;

        private const decimal MillisecondsPerMl = 800.0m;

        // Injects database context
        public DosingService(ApplicationDbContext database, IConfiguration? configuration = null)
        {
            _database = database;
            _configuration = configuration;
        }

        // Maps DosingCommand entity to DTO
        private static DosingCommandResponseDto ToDto(DosingCommand entity, DeviceCommand? delivery = null) =>
            new(
                entity.Id,
                entity.DeviceMac,
                entity.DosingType,
                entity.TargetVolumeMl,
                entity.CalculatedDurationMs,
                entity.Status,
                entity.CreatedAt,
                entity.ExecutedAt,
                entity.IssuedBy,
                delivery?.CommandId,
                DeliveryStatus(entity, delivery),
                delivery?.Reason,
                delivery?.ExpiresAt,
                delivery?.AckedAt
            );

        private static string DeliveryStatus(DosingCommand dose, DeviceCommand? command)
        {
            if (dose.Status == "FAILED" && command?.Reason == "operator_reviewed_unknown") return "REVIEWED";
            if (dose.Status == "COMPLETED") return "COMPLETED";
            if (command?.Status == "SUPERSEDED" || command?.Reason == "cancelled") return "CANCELLED";
            if (dose.Status == "UNCONFIRMED") return "UNCONFIRMED";
            if (command?.Status == "EXPIRED") return "TIMED_OUT";
            if (command?.Succeeded == false) return "REJECTED";
            if (dose.Status == "FAILED") return command?.Succeeded == true ? "CANCELLED" : "FAILED";
            if (command?.Status == "ACKED")
            {
                int bit = dose.DosingType switch { "PH_UP" => 16, "PH_DOWN" => 32, "NUTRIENT_UP" => 64, _ => 128 };
                bool withinRun = command.AckedAt.HasValue && DateTime.UtcNow < command.AckedAt.Value.AddMilliseconds(dose.CalculatedDurationMs);
                return withinRun && (command.RelayBitmask.GetValueOrDefault() & bit) != 0 ? "RUNNING" : "AWAITING_COMPLETION";
            }
            return command?.Status == "SENT" || command?.SentAt != null || dose.Status == "DISPATCHED" ? "SENT" : "QUEUED";
        }

        // Enqueues a requested volume using the estimated pump runtime in milliseconds
        public async Task<DosingCommandResponseDto> EnqueueCommandAsync(DosingCommandRequestDto dto, CancellationToken ct = default)
        {
            var mac = LoraDownlinkQueue.NormalizeMac(dto.DeviceMac);
            var cleanMac = mac.Replace(":", "");
            var dosingType = dto.DosingType.Trim().ToUpperInvariant();
            if (dto.TargetVolumeMl is < 0.5m or > 25m)
                throw new ArgumentException("Dosing volume must be between 0.5 mL and 25 mL.");
            if (dosingType is not ("PH_UP" or "PH_DOWN" or "NUTRIENT_UP" or "DILUTION_DOWN"))
                throw new ArgumentException("Unknown dosing pump.");

            if (dosingType.StartsWith("PH_") && dto.TargetVolumeMl > 2m)
                throw new ArgumentException("pH pulses are limited to 2 mL, followed by a 15-minute mixing lockout.");

            // Validate device existence across canonical or uncolonized formats
            var deviceExists = await _database.Devices.AnyAsync(d => d.MacAddress == mac || d.MacAddress == cleanMac, ct);
            if (!deviceExists)
            {
                throw new ArgumentException($"Device with MAC '{mac}' does not exist.");
            }

            // Calculate run duration: e.g. 10 mL * 800 ms/mL = 8000 ms
            if (dto.ManualPhOverride)
            {
                var key = _configuration?["Security:DeviceKey"];
                if (key?.Length != 64 || !key.All(Uri.IsHexDigit))
                    throw new ArgumentException("Manual override is unavailable until the API device key is configured.");
                if (!await PhDisplayFallback.IsTowerAsync(_database, mac, ct))
                    throw new ArgumentException("Manual pH fault override is available only for the vertical tower.");
                // Explicit manual confirmation does not assert a current pH or
                // require historical pH. The controller still checks fresh EC,
                // runtime limits, waiting periods and signed command expiry.
            }
            var calculatedDurationMs = (int)Math.Round(dto.TargetVolumeMl * MillisecondsPerMl);

            var command = new DosingCommand
            {
                DeviceMac = mac,
                DosingType = dosingType,
                TargetVolumeMl = dto.TargetVolumeMl,
                CalculatedDurationMs = calculatedDurationMs,
                Status = "PENDING",
                CreatedAt = DateTime.UtcNow,
                IssuedBy = string.IsNullOrWhiteSpace(dto.IssuedBy) ? "User" : dto.IssuedBy.Trim()
            };

            await new DeviceCommandQueue(_database).EnqueueDoseAsync(command, ct, dto.ManualPhOverride,
                dto.ManualPhOverride ? _configuration?["Security:DeviceKey"] : null);

            return ToDto(command);
        }

        // Microcontroller callback acknowledging that dosing cycle has executed
        public Task<bool> CompleteCommandAsync(DosingCallbackRequestDto dto, CancellationToken ct = default) =>
            new DeviceCommandQueue(_database).CompleteDoseAsync(dto.CommandId, dto.Status, dto.ExecutedAt, ct);

        // Hardware poll endpoint retrieving unexecuted dosing commands
        public async Task<IEnumerable<DosingCommandResponseDto>> GetPendingCommandsAsync(string deviceMac, CancellationToken ct = default)
        {
            var mac = LoraDownlinkQueue.NormalizeMac(deviceMac);
            var cleanMac = mac.Replace(":", "");
            await new DeviceCommandQueue(_database).ReconcileDosesAsync(mac, ct);
            var commands = await _database.DosingCommands
                .AsNoTracking()
                .Where(c => (c.DeviceMac == mac || c.DeviceMac == cleanMac) && c.Status == "PENDING"
                    && !_database.DeviceCommands.Any(d => d.DosingCommandId == c.Id))
                .OrderBy(c => c.CreatedAt)
                .ToListAsync(ct);

            return commands.Select(c => ToDto(c));
        }

        // Retrieves historical dosing audit trail for a device
        public async Task<IEnumerable<DosingCommandResponseDto>> GetHistoryAsync(string deviceMac, int limit = 50, CancellationToken ct = default)
        {
            var mac = LoraDownlinkQueue.NormalizeMac(deviceMac);
            var cleanMac = mac.Replace(":", "");
            await new DeviceCommandQueue(_database).ReconcileDosesAsync(mac, ct);
            var commands = await _database.DosingCommands
                .AsNoTracking()
                .Where(c => c.DeviceMac == mac || c.DeviceMac == cleanMac)
                .OrderByDescending(c => c.CreatedAt)
                .Take(Math.Clamp(limit, 1, 200))
                .ToListAsync(ct);

            // Keep the device-wide safety blocker visible even when it predates this page.
            var unresolved = await _database.DosingCommands.AsNoTracking()
                .Where(c => (c.DeviceMac == mac || c.DeviceMac == cleanMac) && c.Status == "UNCONFIRMED")
                .OrderBy(c => c.CreatedAt).FirstOrDefaultAsync(ct);
            if (unresolved != null && commands.All(c => c.Id != unresolved.Id))
            {
                if (commands.Count >= Math.Clamp(limit, 1, 200)) commands.RemoveAt(commands.Count - 1);
                commands.Add(unresolved);
                commands = commands.OrderByDescending(c => c.CreatedAt).ToList();
            }

            var ids = commands.Select(x => x.Id).ToArray();
            var deliveries = await _database.DeviceCommands.AsNoTracking()
                .Where(x => x.DosingCommandId != null && ids.Contains(x.DosingCommandId.Value)).OrderBy(x => x.Id).ToListAsync(ct);
            return commands.Select(c => ToDto(c, deliveries.LastOrDefault(x => x.DosingCommandId == c.Id)));
        }
    }
}
