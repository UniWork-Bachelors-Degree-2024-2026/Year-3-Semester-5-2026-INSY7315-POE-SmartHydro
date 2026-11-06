using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Services;

namespace SmartHydroAPI.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class StructuresController : ControllerBase
    {
        private const string ManagerRoles = UserRoles.Admin + "," + UserRoles.Operator;

        private readonly StructureService _service;

        public StructuresController(StructureService service)
        {
            _service = service;
        }

        // Lists the active structures the caller can see, optionally narrowed to one site
        [HttpGet]
        public async Task<ActionResult<IEnumerable<StructureDto>>> GetAll([FromQuery] Guid? siteId = null, CancellationToken ct = default)
        {
            return Ok(await _service.GetAllAsync(User, siteId, ct));
        }

        // Lists the soft-deleted structures the caller can restore, optionally narrowed to one site
        [Authorize(Roles = ManagerRoles)]
        [HttpGet("deleted")]
        public async Task<ActionResult<IEnumerable<StructureDto>>> GetDeleted([FromQuery] Guid? siteId = null, CancellationToken ct = default)
        {
            return Ok(await _service.GetDeletedAsync(User, siteId, ct));
        }

        // Returns one active structure the caller can see
        [HttpGet("{id:int}")]
        public async Task<ActionResult<StructureDto>> GetById(int id, CancellationToken ct)
        {
            var structure = await _service.GetByIdAsync(id, User, ct);

            if (structure is null)
                return NotFound(StructureNotFound(id));

            return Ok(structure);
        }

        // Returns the single-call dashboard summary for a structure
        [HttpGet("{id:int}/dashboard-summary")]
        public async Task<ActionResult<StructureDashboardSummaryDto>> GetDashboardSummary(int id, CancellationToken ct)
        {
            var summary = await _service.GetDashboardSummaryAsync(id, User, ct);

            if (summary is null)
                return NotFound(StructureNotFound(id));

            return Ok(summary);
        }

        // Creates a structure in a site the caller manages
        [Authorize(Roles = ManagerRoles)]
        [HttpPost]
        public async Task<ActionResult<StructureDto>> Create([FromBody] CreateStructureDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var created = await _service.CreateAsync(dto, User, ct);
                return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
            }
            catch (StructureConflictException ex)
            {
                return Conflict(Conflicted(ex));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(BuildProblem("Invalid Structure", ex.Message, StatusCodes.Status400BadRequest));
            }
        }

        // Updates a structure the caller manages
        [Authorize(Roles = ManagerRoles)]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateStructureDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var updated = await _service.UpdateAsync(id, dto, User, ct);

                if (!updated)
                    return NotFound(StructureNotFound(id));

                return Ok(new { message = "Structure updated successfully." });
            }
            catch (StructureConflictException ex)
            {
                return Conflict(Conflicted(ex));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(BuildProblem("Invalid Structure", ex.Message, StatusCodes.Status400BadRequest));
            }
        }

        // Soft-deletes a structure; the caller can restore it and only an Admin can purge it
        [Authorize(Roles = ManagerRoles)]
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var deleted = await _service.DeleteAsync(id, User, ct);

            if (!deleted)
                return NotFound(StructureNotFound(id));

            return NoContent();
        }

        // Restores a soft-deleted structure the caller manages
        [Authorize(Roles = ManagerRoles)]
        [HttpPost("{id:int}/restore")]
        public async Task<IActionResult> Restore(int id, CancellationToken ct)
        {
            try
            {
                var restored = await _service.RestoreAsync(id, User, ct);

                if (!restored)
                    return NotFound(BuildProblem("Structure Not Found", $"Structure #{id} was not found in the deleted items you can restore.", StatusCodes.Status404NotFound));

                return NoContent();
            }
            catch (StructureConflictException ex)
            {
                return Conflict(Conflicted(ex));
            }
        }

        // Permanently removes a soft-deleted structure
        [Authorize(Roles = UserRoles.Admin)]
        [HttpDelete("{id:int}/purge")]
        public async Task<IActionResult> Purge(int id, CancellationToken ct)
        {
            var purged = await _service.PurgeAsync(id, ct);

            if (!purged)
                return NotFound(BuildProblem("Deleted Structure Not Found", $"Structure #{id} is not available for permanent purge.", StatusCodes.Status404NotFound));

            return NoContent();
        }

        // Builds the problem details returned when a structure cannot be found
        private static ProblemDetails StructureNotFound(int id)
        {
            return BuildProblem("Structure Not Found", $"Structure #{id} was not found.", StatusCodes.Status404NotFound);
        }

        // Builds the conflict problem details, naming the field that clashed so a form can show the error beside it
        private static ProblemDetails Conflicted(StructureConflictException ex)
        {
            var problem = BuildProblem("Structure Conflict", ex.Message, StatusCodes.Status409Conflict);
            problem.Extensions["field"] = ex.Field;
            return problem;
        }

        // Builds problem details that also carry a "message" field, matching every other API error
        private static ProblemDetails BuildProblem(string title, string detail, int status)
        {
            var problem = new ProblemDetails
            {
                Title = title,
                Detail = detail,
                Status = status
            };

            problem.Extensions["message"] = detail;
            return problem;
        }
    }
}