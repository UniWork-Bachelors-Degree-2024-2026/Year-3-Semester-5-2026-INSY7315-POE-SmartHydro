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
    public class SitesController : ControllerBase
    {
        private const string ManagerRoles = UserRoles.Admin + "," + UserRoles.Operator;

        private readonly SiteService _service;

        public SitesController(SiteService service)
        {
            _service = service;
        }

        // Lists the sites the caller can see: owned sites for an Operator, assigned sites for a Worker, every site for an Admin
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SiteDto>>> GetAll(CancellationToken ct = default)
        {
            return Ok(await _service.GetAllAsync(User, ct));
        }

        // Returns one site the caller is allowed to see
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<SiteDto>> GetById(Guid id, CancellationToken ct = default)
        {
            var site = await _service.GetByIdAsync(id, User, ct);

            if (site is null)
                return NotFound(SiteNotFound(id));

            return Ok(site);
        }

        // Creates a site owned by the calling Operator, or by a chosen operator when an Admin gives them a site
        [Authorize(Roles = ManagerRoles)]
        [HttpPost]
        public async Task<ActionResult<SiteDto>> Create([FromBody] CreateSiteDto dto, CancellationToken ct = default)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var created = await _service.CreateAsync(dto, User, ct);
                return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(DuplicateName(ex.Message));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(BuildProblem("Invalid Site Owner", ex.Message, StatusCodes.Status400BadRequest));
            }
        }

        // Renames a site the caller manages
        [Authorize(Roles = ManagerRoles)]
        [HttpPut("{id:guid}")]
        public async Task<ActionResult<SiteDto>> Update(Guid id, [FromBody] UpdateSiteDto dto, CancellationToken ct = default)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var updated = await _service.UpdateAsync(id, dto, User, ct);

                if (updated is null)
                    return NotFound(SiteNotFound(id));

                return Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(DuplicateName(ex.Message));
            }
        }

        // Lists the deleted sites the caller can restore: every deleted site for an Admin, their own for an Operator
        [Authorize(Roles = ManagerRoles)]
        [HttpGet("deleted")]
        public async Task<ActionResult<IEnumerable<DeletedSiteDto>>> GetDeleted(CancellationToken ct = default)
        {
            return Ok(await _service.GetDeletedAsync(User, ct));
        }

        // Restores a deleted site the caller manages, together with the structures that were deleted along with it
        [Authorize(Roles = ManagerRoles)]
        [HttpPost("{id:guid}/restore")]
        public async Task<ActionResult<SiteDto>> Restore(Guid id, CancellationToken ct = default)
        {
            try
            {
                var restored = await _service.RestoreAsync(id, User, ct);

                if (restored is null)
                    return NotFound(BuildProblem("Deleted Site Not Found", $"Site '{id}' was not found in the deleted items you can restore.", StatusCodes.Status404NotFound));

                return Ok(restored);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(DuplicateName(ex.Message));
            }
        }

        // Permanently removes a deleted site and everything in it; only an Admin can do this and it cannot be undone
        [Authorize(Roles = UserRoles.Admin)]
        [HttpDelete("{id:guid}/purge")]
        public async Task<IActionResult> Purge(Guid id, CancellationToken ct = default)
        {
            var structureCount = await _service.PurgeAsync(id, User, ct);

            if (structureCount is null)
                return NotFound(BuildProblem("Deleted Site Not Found", $"Site '{id}' is not available for permanent purge.", StatusCodes.Status404NotFound));

            return Ok(new { success = true, message = "The site was permanently deleted.", structureCount = structureCount.Value });
        }

        // Soft-deletes a site the caller manages, refusing while it still has active structures unless force is set; forcing moves those structures to deleted items and cancels open invitations
        [Authorize(Roles = ManagerRoles)]
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id, [FromQuery] bool force = false, CancellationToken ct = default)
        {
            var result = await _service.DeleteAsync(id, force, User, ct);

            if (result.Success)
                return Ok(new { success = true, message = result.Message, structureCount = result.LinkedStructuresCount });

            if (result.LinkedStructuresCount > 0)
                return Conflict(BuildProblem("Cannot Delete Site", result.Message, StatusCodes.Status409Conflict, result.LinkedStructuresCount));

            return NotFound(BuildProblem("Site Not Found", result.Message, StatusCodes.Status404NotFound));
        }

        // Builds the problem details returned when a site cannot be found
        private static ProblemDetails SiteNotFound(Guid id)
        {
            return BuildProblem("Site Not Found", $"Site '{id}' was not found.", StatusCodes.Status404NotFound);
        }

        // Builds the problem details returned when a site name is already in use
        private static ProblemDetails DuplicateName(string detail)
        {
            return BuildProblem("Duplicate Site Name", detail, StatusCodes.Status409Conflict);
        }

        // Builds problem details that also carry a "message" field, matching every other API error, and the structure count when the refusal is about structures
        private static ProblemDetails BuildProblem(string title, string detail, int status, int? structureCount = null)
        {
            var problem = new ProblemDetails
            {
                Title = title,
                Detail = detail,
                Status = status
            };

            problem.Extensions["message"] = detail;

            if (structureCount.HasValue)
                problem.Extensions["structureCount"] = structureCount.Value;

            return problem;
        }
    }
}