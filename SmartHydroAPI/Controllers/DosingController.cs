using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using SmartHydroAPI.Hubs;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Services;
using Microsoft.AspNetCore.Authorization;
using SmartHydroAPI.Data;
using SmartHydroAPI.Extensions;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Controllers
{
    // Controller managing one-shot volumetric peristaltic dosing commands and hardware callbacks
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class DosingController : ControllerBase
    {
        private const string ControlRoles = UserRoles.EquipmentControl;
        private const int IssuedByMaxLength = 100;

        private readonly IDosingService _dosingService;
        private readonly IHubContext<ActuatorHub> _hubContext;
        private readonly ApplicationDbContext _database;

        // Injects dosing service, SignalR hub context for real-time edge dispatch, and database context
        public DosingController(
            IDosingService dosingService,
            IHubContext<ActuatorHub> hubContext,
            ApplicationDbContext database)
        {
            _dosingService = dosingService;
            _hubContext = hubContext;
            _database = database;
        }

        // Enqueues a one-shot volumetric dose for nutrients or pH balancing and dispatches to edge via SignalR
        [Authorize(Roles = ControlRoles)]
        [HttpPost("command")]
        [ProducesResponseType(typeof(DosingCommandResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<DosingCommandResponseDto>> EnqueueCommand(
            [FromBody] DosingCommandRequestDto dto,
            CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            if (!await DeviceAccess.CanOperateDeviceAsync(_database, User, dto.DeviceMac, ct))
                return NotFound(new { message = $"Device {dto.DeviceMac} was not found." });

            dto.IssuedBy = CallerName();

            try
            {
                var command = await _dosingService.EnqueueCommandAsync(dto, ct);

                var queued = await _database.DeviceCommands.SingleAsync(x => x.DosingCommandId == command.Id, ct);
                await new DeviceCommandQueue(_database).RecordPushAttemptAsync(queued.Id, ct);
                await _hubContext.Clients.Group(command.DeviceMac.Replace(":", ""))
                    .SendAsync("ReceiveCommand", DeviceCommandQueue.WirePayload(queued), ct);
                return Ok(command with { CommandId = queued.CommandId, DeliveryStatus = "QUEUED", ExpiresAt = queued.ExpiresAt });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid Dosing Target",
                    Detail = ex.Message,
                    Status = StatusCodes.Status400BadRequest
                });
            }
        }

        // Callback endpoint for microcontroller reporting completion of a dosing cycle
        public sealed record DoseReviewRequest(bool PumpsCheckedOff, bool ReservoirChecked, string Notes);

        [Authorize(Roles = ControlRoles)]
        [HttpPost("{mac}/{doseId:guid}/review")]
        public async Task<IActionResult> ReviewUnknownDose(string mac, Guid doseId, [FromBody] DoseReviewRequest dto, CancellationToken ct)
        {
            if (!await DeviceAccess.CanControlDeviceAsync(_database, User, mac, ct)) return NotFound();
            try
            {
                await new DeviceCommandQueue(_database).ReviewUnknownDoseAsync(mac, doseId, CallerName(),
                    dto.Notes, dto.PumpsCheckedOff, dto.ReservoirChecked, ct);
                return Ok(new { message = "The earlier request was reviewed. Its delivered amount remains unknown. Sensor checks and dosing limits still apply." });
            }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        }

        // Callback endpoint for microcontroller reporting completion of a dosing cycle
        [AllowAnonymous]
        [HttpPost("callback")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DosingCallback(
            [FromBody] DosingCallbackRequestDto dto,
            CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var updated = await _dosingService.CompleteCommandAsync(dto, ct);
            return updated ? Ok(new { message = "Dose status updated.", commandId = dto.CommandId, status = dto.Status })
                           : NotFound(new ProblemDetails
                           {
                               Title = "Command Not Found",
                               Detail = $"Dosing command '{dto.CommandId}' was not found.",
                               Status = StatusCodes.Status404NotFound
                           });
        }

        // Hardware poll endpoint for ESP32 nodes to retrieve pending unexecuted dosing commands
        [HttpGet("pending/{mac}")]
        [ProducesResponseType(typeof(IEnumerable<DosingCommandResponseDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<DosingCommandResponseDto>>> GetPendingCommands(
            string mac,
            CancellationToken ct)
        {
            if (!await DeviceAccess.CanReadDeviceAsync(_database, User, mac, ct))
                return NotFound(new { message = $"Device {mac} was not found." });
            var commands = await _dosingService.GetPendingCommandsAsync(mac, ct);
            return Ok(commands);
        }

        // Historical query retrieving recent dosing events for a device
        [HttpGet("history/{mac}")]
        [ProducesResponseType(typeof(IEnumerable<DosingCommandResponseDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<DosingCommandResponseDto>>> GetHistory(
            string mac,
            [FromQuery] int limit = 50,
            CancellationToken ct = default)
        {
            if (!await DeviceAccess.CanReadDeviceAsync(_database, User, mac, ct))
                return NotFound(new { message = $"Device {mac} was not found." });

            var history = await _dosingService.GetHistoryAsync(mac, limit, ct);
            return Ok(history);
        }

        // Maps DosingType to standard firmware actuator names matching handleReceiveTimedCommand
        private static string ResolveActuatorName(string dosingType) =>
            dosingType.Trim().ToUpperInvariant() switch
            {
                "PH_UP" or "PHUP" or "PHUPPUMP" => "PhUpPump",
                "PH_DOWN" or "PHDOWN" or "PHDOWNPUMP" => "PhDownPump",
                "NUTRIENT_UP" or "NUTRIENTUP" or "EC_UP" or "ECUP" or "NUTRIENTUPPUMP" => "NutrientUpPump",
                "DILUTION_DOWN" or "DILUTIONDOWN" or "EC_DOWN" or "ECDOWN" or "DILUTIONDOWNPUMP" => "DilutionDownPump",
                _ => dosingType
            };

        // Names the caller in the dosing history from their token rather than trusting the request body, cut to the length the column allows
        private string CallerName()
        {
            var name = User.GetEmail();

            if (string.IsNullOrWhiteSpace(name))
                return "User";

            name = name.Trim();

            if (name.Length > IssuedByMaxLength)
                name = name.Substring(0, IssuedByMaxLength);

            return name;
        }
    }
}
