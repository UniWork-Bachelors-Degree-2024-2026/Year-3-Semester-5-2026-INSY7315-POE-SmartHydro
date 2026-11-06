using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Data;
using SmartHydroAPI.Hubs;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Services;
using Microsoft.AspNetCore.Authorization;

namespace SmartHydroAPI.Controllers
{
    // Controller managing threshold configurations and boundary limits, one set per structure
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ThresholdsController : ControllerBase
    {
        private const string EditRoles = UserRoles.Admin + "," + UserRoles.Operator;
        private const decimal LargestStoredValue = 99999m;

        private readonly ThresholdService _service;
        private readonly ApplicationDbContext? _database;
        private readonly IHubContext<ActuatorHub>? _hubContext;

        // Dependency injection for threshold service, database, and SignalR hub
        public ThresholdsController(
            ThresholdService service,
            ApplicationDbContext? database = null,
            IHubContext<ActuatorHub>? hubContext = null)
        {
            _service = service;
            _database = database;
            _hubContext = hubContext;
        }

        // Gets the ranges of every structure in one site. A reading nobody has changed shows its healthy default
        [HttpGet("site/{siteId:guid}")]
        public async Task<ActionResult<IEnumerable<StructureThresholdsDto>>> GetBySite(Guid siteId, CancellationToken ct)
        {
            var result = await _service.GetForSiteAsync(siteId, User, ct);

            if (result is null)
                return NotFound(new ProblemDetails
                {
                    Title = "Site Not Found",
                    Detail = "The site was not found.",
                    Status = StatusCodes.Status404NotFound
                });

            return Ok(result);
        }

        // Gets the ranges of one structure
        [HttpGet("structure/{structureId:int}")]
        public async Task<ActionResult<StructureThresholdsDto>> GetByStructure(int structureId, CancellationToken ct)
        {
            var result = await _service.GetForStructureAsync(structureId, User, ct);

            if (result is null)
                return NotFound(StructureNotFound(structureId));

            return Ok(result);
        }

        // Upserts the range of one reading on one structure. An admin may change any structure, an operator only structures in the sites they own
        [Authorize(Roles = EditRoles)]
        [HttpPut]
        public async Task<ActionResult<ThresholdResponseDto>> UpsertThreshold([FromBody] UpdateThresholdDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            if (dto.MinValue >= dto.MaxValue)
                return BadRequest(new { message = "The minimum must be lower than the maximum.", field = nameof(dto.MinValue) });

            if (Math.Abs(dto.MinValue) > LargestStoredValue || Math.Abs(dto.MaxValue) > LargestStoredValue)
                return BadRequest(new { message = $"Values must be between -{LargestStoredValue:0} and {LargestStoredValue:0}.", field = nameof(dto.MaxValue) });

            dto.SensorType = dto.SensorType.Trim();

            try
            {
                var saved = await _service.UpsertAsync(dto, User, ct);

                if (saved is null)
                    return NotFound(StructureNotFound(dto.StructureId));

                await BroadcastStructureThresholdsToEdgeAsync(saved.StructureId, ct);

                return Ok(new ThresholdResponseDto
                {
                    Id = saved.Id,
                    StructureId = saved.StructureId,
                    StructureType = saved.StructureType,
                    SensorType = saved.SensorType,
                    MinValue = saved.MinValue,
                    MaxValue = saved.MaxValue,
                    IsDefault = false
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // Puts a saved range back to the healthy default
        [Authorize(Roles = EditRoles)]
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            int? structureId = null;
            if (_database != null)
            {
                structureId = await _database.Thresholds
                    .AsNoTracking()
                    .Where(t => t.Id == id)
                    .Select(t => (int?)t.StructureId)
                    .FirstOrDefaultAsync(ct);
            }

            var deleted = await _service.DeleteAsync(id, User, ct);
            if (deleted && structureId.HasValue)
            {
                await BroadcastStructureThresholdsToEdgeAsync(structureId.Value, ct);
            }

            return deleted ? NoContent() : NotFound(new ProblemDetails
            {
                Title = "Threshold Not Found",
                Detail = $"Threshold #{id} was not found.",
                Status = StatusCodes.Status404NotFound
            });
        }

        private async Task BroadcastStructureThresholdsToEdgeAsync(int structureId, CancellationToken ct)
        {
            if (_database == null) return;

            try
            {
                var resolved = await _service.GetForStructureAsync(structureId, User, ct);
                if (resolved == null) return;

                decimal phMin = 5.5m, phMax = 6.5m;
                decimal ecMin = 1.2m, ecMax = 2.2m;
                decimal tempMin = 18.0m, tempMax = 26.0m;
                decimal humMin = 50.0m, humMax = 75.0m;

                foreach (var r in resolved.Thresholds)
                {
                    if (r.SensorType.Equals("PH", StringComparison.OrdinalIgnoreCase))
                    {
                        phMin = r.MinValue;
                        phMax = r.MaxValue;
                    }
                    else if (r.SensorType.Equals("EC", StringComparison.OrdinalIgnoreCase))
                    {
                        ecMin = r.MinValue;
                        ecMax = r.MaxValue;
                    }
                    else if (r.SensorType.Equals("TEMPERATURE", StringComparison.OrdinalIgnoreCase))
                    {
                        tempMin = r.MinValue;
                        tempMax = r.MaxValue;
                    }
                    else if (r.SensorType.Equals("HUMIDITY", StringComparison.OrdinalIgnoreCase))
                    {
                        humMin = r.MinValue;
                        humMax = r.MaxValue;
                    }
                }

                var deviceMacs = await _database.Devices
                    .AsNoTracking()
                    .Where(d => d.StructureId == structureId && d.DeviceType != "CAMERA-NODE")
                    .Select(d => d.MacAddress)
                    .ToListAsync(ct);

                foreach (var mac in deviceMacs)
                {
                    var canonical = LoraDownlinkQueue.NormalizeMac(mac);
                    var command = await new DeviceCommandQueue(_database).EnqueueAsync(canonical, "thresholds", new()
                    {
                        ["p0"] = phMin, ["p1"] = phMax, ["p2"] = ecMin, ["p3"] = ecMax,
                        ["p5"] = tempMax, ["p7"] = humMax
                    }, "User-MVC", ct: ct);
                    if (_hubContext != null)
                        await _hubContext.Clients.Group(canonical.Replace(":", "")).SendAsync("ReceiveCommand", DeviceCommandQueue.WirePayload(command), ct);
                }
            }
            catch { }
        }

        private static ProblemDetails StructureNotFound(int id) => new()
        {
            Title = "Structure Not Found",
            Detail = $"Structure #{id} was not found.",
            Status = StatusCodes.Status404NotFound
        };
    }
}
