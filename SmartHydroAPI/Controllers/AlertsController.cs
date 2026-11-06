using Microsoft.AspNetCore.Mvc;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Services;
using Microsoft.AspNetCore.Authorization;

namespace SmartHydroAPI.Controllers
{
    // Controller responsible for querying, triggering, badgin, and resolving system alerts
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class AlertsController : ControllerBase
    {
        private readonly AlertService _service;

        // Injects the AlertService dependency
        public AlertsController(AlertService service) => _service = service;

        // Retrieves paginated alerts filtered by structure and resolution status
        [HttpGet]
        public async Task<ActionResult<PagedResultDto<AlertResponseDto>>> GetAll(
            [FromQuery] int? structureId,
            [FromQuery] bool unresolvedOnly = true,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] Guid? siteId = null,
            CancellationToken ct = default)
        {
            return Ok(await _service.GetPagedAlertsAsync(structureId, unresolvedOnly, page, pageSize, User, ct, siteId));
        }

        // Lists the soft-deleted alerts the caller can restore, optionally narrowed to one site
        [Authorize(Roles = "Admin,Operator")]
        [HttpGet("deleted")]
        public async Task<ActionResult<IEnumerable<AlertResponseDto>>> GetDeleted([FromQuery] Guid? siteId = null, CancellationToken ct = default)
        {
            return Ok(await _service.GetDeletedAlertsAsync(siteId, User, ct));
        }

        // Returns count of unresolved/open alerts for dashboard notification badging
        [HttpGet("unresolved-count")]
        public async Task<ActionResult<object>> GetUnresolvedCount([FromQuery] int? structureId, [FromQuery] Guid? siteId = null, CancellationToken ct = default)
        {
            var count = await _service.GetUnresolvedCountAsync(structureId, User, ct, siteId);
            return Ok(new { structureId, siteId, unresolvedCount = count });
        }

        // Fetches a single alert by ID
        [HttpGet("{id:long}")]
        public async Task<ActionResult<AlertResponseDto>> GetById(long id, CancellationToken ct)
        {
            var alert = await _service.GetByIdAsync(id, User, ct);
            return alert is null ? NotFound(new ProblemDetails
            {
                Title = "Alert Not Found",
                Detail = $"Alert #{id} was not found.",
                Status = StatusCodes.Status404NotFound
            }) : Ok(alert);
        }

        // Creates a new telemetry threshold violation or hardware anomaly alert
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateAlertDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var alert = await _service.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(GetById), new { id = alert.Id }, alert);
        }

        // Marks an alert as resolved with operator notes
        [HttpPut("{id:long}/resolve")]
        public async Task<IActionResult> Resolve(long id, [FromBody] ResolveAlertDto dto, CancellationToken ct)
        {
            var resolved = await _service.ResolveAsync(id, dto, User, ct);
            return resolved ? NoContent() : NotFound(new ProblemDetails
            {
                Title = "Alert Not Found",
                Detail = $"Alert #{id} was not found.",
                Status = StatusCodes.Status404NotFound
            });
        }
        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long id, CancellationToken ct)
        {
            var deleted = await _service.DeleteAsync(id, User, ct);
            return deleted ? NoContent() : NotFound(new ProblemDetails
            {
                Title = "Alert Not Found",
                Detail = $"Alert #{id} was not found.",
                Status = StatusCodes.Status404NotFound
            });
        }

        [HttpPost("{id:long}/restore")]
        public async Task<IActionResult> Restore(long id, CancellationToken ct)
        {
            var restored = await _service.RestoreAsync(id, User, ct);
            return restored ? NoContent() : NotFound(new ProblemDetails
            {
                Title = "Alert Not Found",
                Detail = $"Alert #{id} was not found in the deleted items you can restore.",
                Status = StatusCodes.Status404NotFound
            });
        }

        [Authorize(Roles = "Admin")]
        [HttpDelete("{id:long}/purge")]
        public async Task<IActionResult> Purge(long id, CancellationToken ct)
        {
            var purged = await _service.PurgeAsync(id, ct);
            return purged ? NoContent() : NotFound(new ProblemDetails
            {
                Title = "Deleted Alert Not Found",
                Detail = $"Alert #{id} is not available for permanent purge.",
                Status = StatusCodes.Status404NotFound
            });
        }
    }
}
