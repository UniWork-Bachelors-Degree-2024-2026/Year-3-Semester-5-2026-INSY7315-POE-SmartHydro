using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SmartHydroAPI.Hubs;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Patterns.Decorator;
using Microsoft.AspNetCore.Authorization;
using SmartHydroAPI.Data;
using SmartHydroAPI.Extensions;
using SmartHydroAPI.Services;
using Microsoft.EntityFrameworkCore;

namespace SmartHydroAPI.Controllers
{
    // Controller responsible for managing actuator states, continuous/timed commands, and state history
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ActuatorsController : ControllerBase
    {
        private const string ControlRoles = UserRoles.EquipmentControl;
        private const int ChangedByMaxLength = 50;

        private readonly IActuatorService _service;
        private readonly IHubContext<ActuatorHub> _hubContext;
        private readonly ApplicationDbContext _database;

        // Injects the IActuatorService, SignalR hub context for instant edge dispatch and the database used to check the caller may use a device
        public ActuatorsController(IActuatorService service, IHubContext<ActuatorHub> hubContext, ApplicationDbContext database)
        {
            _service = service;
            _hubContext = hubContext;
            _database = database;
        }

        // Retrieves the current state of all relays/actuators for a specific device MAC address
        [HttpGet("{mac}")]
        public async Task<ActionResult<ActuatorStateResponseDto>> GetState(string mac, CancellationToken ct)
        {
            if (!await DeviceAccess.CanReadDeviceAsync(_database, User, mac, ct))
                return NotFound(DeviceNotFound(mac));

            return Ok(await _service.GetStateAsync(mac, ct));
        }

        // Applies continuous manual actuator commands (GrowLight, Fans, Circulation Pump)
        [HttpGet("{mac}/commands/{cid:int}")]
        public async Task<ActionResult> CommandStatus(string mac, int cid, CancellationToken ct, Guid? requestId = null)
        {
            if (!await DeviceAccess.CanReadDeviceAsync(_database, User, mac, ct)) return NotFound(DeviceNotFound(mac));
            mac = LoraDownlinkQueue.NormalizeMac(mac);
            await new DeviceCommandQueue(_database).ReconcileDosesAsync(mac, ct);
            var command = await _database.DeviceCommands.AsNoTracking().Where(x => x.DeviceMac == mac && x.CommandId == cid
                && (!requestId.HasValue || x.RequestId == requestId))
                .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            if (command == null) return NotFound();
            var dose = command.DosingCommandId.HasValue
                ? await _database.DosingCommands.AsNoTracking().FirstOrDefaultAsync(x => x.Id == command.DosingCommandId, ct) : null;
            return Ok(new { commandId = cid, command.RequestId, status = command.Status == "QUEUED" && command.SentAt.HasValue ? "SENT" : command.Status, terminal = command.Status is "ACKED" or "EXPIRED" or "SUPERSEDED",
                ok = command.Status == "ACKED" && command.Succeeded == true, command.Reason, command.CreatedAt, command.ExpiresAt,
                phase = dose?.Status == "COMPLETED" ? "completed" : dose?.Status == "FAILED" && command.Succeeded == true ? "cancelled" : "accepted",
                state = await _service.GetStateAsync(mac, ct) });
        }

        [Authorize(Roles = ControlRoles)]
        [HttpPost("continuous")]
        [HttpPost("command")] // Preserves backwards compatibility
        public async Task<ActionResult<ActuatorStateResponseDto>> SendContinuousCommand([FromBody] ActuatorCommandDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            if (!await DeviceAccess.CanOperateDeviceAsync(_database, User, dto.DeviceMac, ct))
                return NotFound(DeviceNotFound(dto.DeviceMac));

            dto.ChangedBy = CallerName();

            dto.DeviceMac = LoraDownlinkQueue.NormalizeMac(dto.DeviceMac);
            var payload = new Dictionary<string, object>();
            void Put(string key, bool? value) { if (value.HasValue) payload[key] = value.Value ? 1 : 0; }
            Put("w", dto.MainWaterPump); Put("s", dto.SpraySystem);
            Put("fc", dto.CirculationFan); Put("fe", dto.ExtractorFan); Put("gl", dto.GrowLight);
            Put("pu", dto.PhUpPump); Put("pd", dto.PhDownPump);
            Put("nu", dto.NutrientUpPump); Put("nd", dto.DilutionDownPump);
            if (payload.Count == 0) return BadRequest(new { message = "Choose an actuator." });
            if ((dto.PhUpPump == true && dto.PhDownPump == true) ||
                (dto.NutrientUpPump == true && dto.DilutionDownPump == true))
                return BadRequest(new { message = "Choose one dosing direction at a time." });
            var commandIds = new List<int>();
            Guid? requestId = null;
            foreach (var group in payload.GroupBy(x => x.Key is "pu" or "pd" ? "ph" : x.Key is "nu" or "nd" ? "ec" : x.Key))
            {
                var command = await new DeviceCommandQueue(_database).EnqueueAsync(dto.DeviceMac,
                    group.Key, group.ToDictionary(x => x.Key, x => x.Value), dto.ChangedBy, ct: ct);
                commandIds.Add(command.CommandId);
                requestId = command.RequestId;
                await new DeviceCommandQueue(_database).RecordPushAttemptAsync(command.Id, ct);
                await _hubContext.Clients.Group(dto.DeviceMac.Replace(":", ""))
                    .SendAsync("ReceiveCommand", DeviceCommandQueue.WirePayload(command), ct);
            }
            var state = await _service.GetStateAsync(dto.DeviceMac, ct);
            return Ok(state with { CommandId = commandIds.Last(), CommandIds = commandIds.ToArray(), RequestId = requestId, Pending = true });
        }

        // Applies a one-shot timed actuator command (e.g. Fodder misting spray for 8 seconds)
        [Authorize(Roles = ControlRoles)]
        [HttpPost("timed")]
        public async Task<ActionResult<ActuatorStateResponseDto>> SendTimedCommand([FromBody] TimedActuatorCommandDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            if (!await DeviceAccess.CanOperateDeviceAsync(_database, User, dto.DeviceMac, ct))
                return NotFound(DeviceNotFound(dto.DeviceMac));

            dto.ChangedBy = CallerName();

            var key = dto.ActuatorName.ToLowerInvariant() switch
            {
                "spraysystem" => "s", "mainwaterpump" => "w", _ => ""
            };
            if (key.Length == 0) return BadRequest(new { message = "Timed commands support the water and spray pumps." });
            dto.DeviceMac = LoraDownlinkQueue.NormalizeMac(dto.DeviceMac);
            var payload = new Dictionary<string, object> { [key] = 1, [key + "t"] = dto.DurationSeconds };
            var command = await new DeviceCommandQueue(_database).EnqueueAsync(dto.DeviceMac, key, payload, dto.ChangedBy, ct: ct);
            var state = await _service.GetStateAsync(dto.DeviceMac, ct);
            await new DeviceCommandQueue(_database).RecordPushAttemptAsync(command.Id, ct);
            await _hubContext.Clients.Group(dto.DeviceMac.Replace(":", ""))
                .SendAsync("ReceiveCommand", DeviceCommandQueue.WirePayload(command), ct);
            return Ok(state with { CommandId = command.CommandId, RequestId = command.RequestId, Pending = true });
        }

        // Returns paginated historical actuator state changes
        [HttpGet("history/{mac}")]
        public async Task<ActionResult<PagedResultDto<ActuatorHistory>>> GetHistory(
            string mac,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken ct = default)
        {
            if (!await DeviceAccess.CanReadDeviceAsync(_database, User, mac, ct))
                return NotFound(DeviceNotFound(mac));

            return Ok(await _service.GetPagedHistoryAsync(mac, page, pageSize, ct));
        }

        // Names the caller in the actuator history from their token rather than trusting the request body, cut to the length the history column allows
        private string CallerName()
        {
            var name = User.GetEmail();

            if (string.IsNullOrWhiteSpace(name))
                return "User";

            name = name.Trim();

            if (name.Length > ChangedByMaxLength)
                name = name.Substring(0, ChangedByMaxLength);

            return name;
        }

        // Builds the not-found body used when a device is missing or the caller may not use it, so the two cases look the same
        private static object DeviceNotFound(string mac)
        {
            return new { message = $"Device {mac} was not found." };
        }
    }
}
