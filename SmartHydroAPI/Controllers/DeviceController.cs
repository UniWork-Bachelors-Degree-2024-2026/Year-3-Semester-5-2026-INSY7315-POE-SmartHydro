using Microsoft.AspNetCore.Mvc;
using SmartHydroAPI.Data;
using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SmartHydroAPI.Hubs;

namespace SmartHydroAPI.Controllers
{
    // Controller handling IoT device inventory, CRUD, offline provisioning, and structure assignment
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class DevicesController : ControllerBase
    {
        private readonly ApplicationDbContext _database;
        private readonly DeviceService _service;
        private readonly IHubContext<ActuatorHub> _hubContext;

        // Injects the DeviceService and ActuatorHub dependencies
        public DevicesController(DeviceService service, IHubContext<ActuatorHub> hubContext, ApplicationDbContext database)
        {
            _database = database;
            _service = service;
            _hubContext = hubContext;
        }

        // Retrieves all registered IoT devices, optionally filtered by structure ID
        [HttpGet]
        public async Task<ActionResult<IEnumerable<DeviceDto>>> GetAll([FromQuery] int? structureId, CancellationToken ct = default) =>
            Ok(await _service.GetAllAsync(structureId, ct));

        // Retrieves device telemetry state and connection status by hardware MAC address
        [HttpGet("{mac}")]
        public async Task<ActionResult<DeviceDto>> GetByMac(string mac, CancellationToken ct)
        {
            var device = await _service.GetByMacAsync(mac, ct);
            return device is null ? NotFound(new ProblemDetails
            {
                Title = "Device Not Found",
                Detail = $"Device with MAC address '{mac}' was not found.",
                Status = StatusCodes.Status404NotFound
            }) : Ok(device);
        }

        // Registers a new device node by MAC address
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<ActionResult<DeviceDto>> Register([FromBody] CreateDeviceDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var device = await _service.RegisterAsync(dto, ct);
                return CreatedAtAction(nameof(GetByMac), new { mac = device.MacAddress }, device);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new ProblemDetails
                {
                    Title = "Device Already Registered",
                    Detail = ex.Message,
                    Status = StatusCodes.Status409Conflict
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid Configuration",
                    Detail = ex.Message,
                    Status = StatusCodes.Status400BadRequest
                });
            }
        }

        // Updates existing device configuration and assignment
        [Authorize(Roles = "Admin")]
        [HttpPut("{mac}")]
        public async Task<ActionResult<DeviceDto>> Update(string mac, [FromBody] UpdateDeviceDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var updated = await _service.UpdateAsync(mac, dto, ct);
                return updated is null ? NotFound(new ProblemDetails
                {
                    Title = "Device Not Found",
                    Detail = $"Device with MAC address '{mac}' was not found.",
                    Status = StatusCodes.Status404NotFound
                }) : Ok(updated);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid Assignment",
                    Detail = ex.Message,
                    Status = StatusCodes.Status400BadRequest
                });
            }
        }

        // Deletes a device from the system
        [Authorize(Roles = "Admin")]
        [HttpDelete("{mac}")]
        public async Task<IActionResult> Delete(string mac, CancellationToken ct)
        {
            var deleted = await _service.DeleteAsync(mac, ct);
            return deleted ? NoContent() : NotFound(new ProblemDetails
            {
                Title = "Device Not Found",
                Detail = $"Device with MAC address '{mac}' was not found.",
                Status = StatusCodes.Status404NotFound
            });
        }

        // Sets the device structure type profile ('VERTICAL-TOWER' or 'FODDER-SYSTEM')
        [Authorize(Roles = "Admin")]
        [HttpPut("{mac}/structure-type")]
        public async Task<IActionResult> SetStructureType(string mac, [FromBody] SetStructureTypeDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var updated = await _service.SetStructureTypeAsync(mac, dto.StructureType, ct);
            return updated ? Ok(new { message = "Structure type updated successfully.", structureType = dto.StructureType })
                           : NotFound(new ProblemDetails
                           {
                               Title = "Device Not Found",
                               Detail = $"Device with MAC address '{mac}' was not found.",
                               Status = StatusCodes.Status404NotFound
                           });
        }

        [Authorize(Roles = "Admin,Operator")]
        [HttpPost("{mac}/offline-mode")]
        public Task<IActionResult> RequestOfflineMode(string mac, CancellationToken ct) => RequestMode(mac, 2, ct);

        [Authorize(Roles = "Admin,Operator")]
        [HttpPost("{mac}/online-mode")]
        public Task<IActionResult> RequestOnlineMode(string mac, CancellationToken ct) => RequestMode(mac, 1, ct);

        [Authorize(Roles = "Admin,Operator")]
        [HttpPost("{mac}/lora-mode")]
        public Task<IActionResult> RequestLoraMode(string mac, CancellationToken ct) => RequestMode(mac, 0, ct);

        private async Task<IActionResult> RequestMode(string mac, int mode, CancellationToken ct)
        {
            mac = LoraDownlinkQueue.NormalizeMac(mac);
            if (!await DeviceAccess.CanControlDeviceAsync(_database, User, mac, ct)) return NotFound();
            var command = await new DeviceCommandQueue(_database).EnqueueAsync(mac, "mode", new()
            {
                ["mode"] = LoraDownlinkQueue.CanonicalModeName(mode), ["operatingMode"] = mode
            }, "User-MVC", ct: ct);
            var state = await ModeStateService.GetAsync(_database, mac, ct);
            await _hubContext.Clients.Group(LoraDownlinkQueue.NormalizeMac(mac).Replace(":", "")).SendAsync("ReceiveModeState", state, ct);
            // Both transports carry the same CID, so the node can discard a duplicate delivery.
            await _hubContext.Clients.Group(mac.Replace(":", "")).SendAsync("ReceiveCommand", DeviceCommandQueue.WirePayload(command), ct);
            return Ok(new { pending = true, commandId = command.CommandId, modeIndex = mode, requestedAt = command.CreatedAt });
        }

        // Renames a friendly display name for a sensor or camera node
        [Authorize(Roles = "Admin")]
        [HttpPut("{mac}/rename")]
        public async Task<IActionResult> Rename(string mac, [FromBody] RenameDeviceDto dto, CancellationToken ct)
        {
            var renamed = await _service.RenameAsync(mac, dto, ct);
            return renamed ? Ok(new { message = "Device renamed successfully.", name = dto.Name }) : NotFound(new { message = $"Device {mac} not found." });
        }

        // Associates or unassigns a device to/from a greenhouse tower or fodder tray
        [Authorize(Roles = "Admin")]
        [HttpPut("{mac}/assign-structure")]
        public async Task<IActionResult> AssignStructure(string mac, [FromBody] AssignStructureDto dto, CancellationToken ct)
        {
            try
            {
                var assigned = await _service.AssignStructureAsync(mac, dto, ct);
                return assigned ? Ok(new { message = "Structure assigned successfully." }) : NotFound(new { message = $"Device {mac} not found." });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
