using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartHydroAPI.Extensions;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Services;

namespace SmartHydroAPI.Controllers
{
    [ApiController]
    [Route("api/admin")]
    [Authorize(Roles = UserRoles.Admin)]
    public class AdminController : ControllerBase
    {
        private readonly AuthenticationService _service;
        private readonly AdminDirectoryService _directory;
        private readonly SiteInvitationService _invitations;

        public AdminController(AuthenticationService service, AdminDirectoryService directory, SiteInvitationService invitations)
        {
            _service = service;
            _directory = directory;
            _invitations = invitations;
        }

        // Returns every account in the system with its role, status and site links for the admin dashboard
        [HttpGet("users")]
        public async Task<ActionResult<List<AdminUserDto>>> GetUsers(CancellationToken ct = default)
        {
            return Ok(await _directory.GetUsersAsync(AdminEmail(), ct));
        }

        // Disables an approved account: the user is signed out straight away and cannot sign in, but all of their data is kept
        [HttpPost("users/{id:int}/deactivate")]
        public async Task<ActionResult<AccountActionResponseDto>> Deactivate(int id, CancellationToken ct = default)
        {
            return AccountResponse(id, await _directory.DeactivateAsync(id, AdminEmail(), ct));
        }

        // Re-enables a disabled account so the user can sign in again
        [HttpPost("users/{id:int}/reactivate")]
        public async Task<ActionResult<AccountActionResponseDto>> Reactivate(int id, CancellationToken ct = default)
        {
            return AccountResponse(id, await _directory.ReactivateAsync(id, AdminEmail(), ct));
        }

        // Moves a denied account back to pending approval so it shows up for review again
        [HttpPost("users/{id:int}/reopen")]
        public async Task<ActionResult<AccountActionResponseDto>> Reopen(int id, CancellationToken ct = default)
        {
            return AccountResponse(id, await _directory.ReopenAsync(id, AdminEmail(), ct));
        }

        // Updates an account's name, starting site name and role; the email cannot be changed, and built-in administrators may only change their name
        [HttpPut("users/{id:int}")]
        public async Task<ActionResult<AccountActionResponseDto>> UpdateUser(int id, [FromBody] UpdateUserDto dto, CancellationToken ct = default)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { message = "Check the details and try again." });

            var result = await _directory.UpdateUserAsync(id, dto, AdminEmail(), ct);

            if (result.Outcome == AccountActionOutcome.Conflict)
                return Conflict(new { message = result.Message, field = result.Field });

            if (result.Outcome == AccountActionOutcome.Invalid)
                return BadRequest(new { message = result.Message, field = result.Field });

            return AccountResponse(id, result);
        }

        // Deletes an account in any status, moving the sites and structures it owned to deleted items
        [HttpDelete("users/{id:int}")]
        public async Task<ActionResult<AccountActionResponseDto>> DeleteUser(int id, CancellationToken ct = default)
        {
            return AccountResponse(id, await _directory.DeleteAsync(id, AdminEmail(), ct));
        }

        // Returns every active site with its owning operator so the admin dashboard can group them
        [HttpGet("sites")]
        public async Task<ActionResult<List<AdminSiteDto>>> GetSites(CancellationToken ct = default)
        {
            return Ok(await _directory.GetSitesAsync(ct));
        }

        // Gives a site to an approved operator, moving its structures to them as well
        [HttpPut("sites/{id:guid}/owner")]
        public async Task<IActionResult> AssignSiteOwner(Guid id, [FromBody] AssignSiteOwnerDto dto, CancellationToken ct = default)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { message = "Choose an operator to own this site." });

            var outcome = await _directory.AssignOwnerAsync(id, dto.OwnerEmail, ct);

            if (outcome == AssignOwnerOutcome.SiteNotFound)
                return NotFound(new { message = "That site could not be found." });

            if (outcome == AssignOwnerOutcome.OwnerNotEligible)
                return BadRequest(new { message = "Sites can only be given to approved operators." });

            return Ok(new { message = "Site owner updated." });
        }

        // Lists the workers and open invitations on every active site, or on one site, so an admin can see who has access
        [HttpGet("workers")]
        public async Task<ActionResult<List<TeamMemberDto>>> GetWorkers([FromQuery] Guid? siteId, CancellationToken ct = default)
        {
            return Ok(await _invitations.GetAdminTeamAsync(siteId, ct));
        }

        // Takes a worker off a site; they keep their account and any other sites they belong to
        [HttpDelete("sites/{siteId:guid}/workers/{userId:int}")]
        public async Task<IActionResult> RemoveWorker(Guid siteId, int userId, CancellationToken ct = default)
        {
            var result = await _invitations.AdminRemoveMemberAsync(siteId, userId, ct);
            return TeamResponse(result, $"{result.Email} was removed from {result.SiteName}.", "That worker is no longer on this site.");
        }

        // Cancels an unused invitation so its link and access code stop working
        [HttpDelete("sites/{siteId:guid}/invitations")]
        public async Task<IActionResult> CancelInvitation(Guid siteId, [FromQuery] string? email, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(email))
                return BadRequest(new { message = "Say which invitation to cancel." });

            var result = await _invitations.AdminCancelInviteAsync(siteId, email, ct);
            return TeamResponse(result, $"The invitation to {result.Email} was cancelled.", "That invitation has already been used or cancelled.");
        }

        // Returns every self-registered farm operator whose account is still awaiting administrator review
        [HttpGet("pending-operators")]
        public async Task<ActionResult<List<PendingOperatorDto>>> GetPendingOperators(CancellationToken ct = default)
        {
            var pending = await _service.GetPendingOperatorsAsync(ct);

            var result = pending
                .Select(u => new PendingOperatorDto
                {
                    Id = u.Id,
                    Email = u.Email,
                    FullName = u.FullName,
                    FarmName = u.FarmName ?? string.Empty,
                    Role = u.Role,
                    Status = u.Status,
                    CreatedAt = u.CreatedAt
                })
                .ToList();

            return Ok(result);
        }

        // Approves a pending farm operator, creates their farm site and emails them that they can now log in
        [HttpPost("users/{id:int}/approve")]
        public async Task<ActionResult<OperatorReviewResponseDto>> Approve(int id, CancellationToken ct = default)
        {
            var result = await _service.ApproveOperatorAsync(id, ct);

            if (result.Outcome == OperatorReviewOutcome.NotFound)
                return NotFound(new { message = "No pending or denied operator was found for this account." });

            var message = "Operator approved. An approval email has been sent.";

            if (!result.NotificationSent)
                message = "Operator approved, but the approval email could not be sent.";

            return Ok(BuildResponse(result, message));
        }

        // Denies a pending farm operator so the account can never sign in, and emails them the outcome
        [HttpPost("users/{id:int}/deny")]
        public async Task<ActionResult<OperatorReviewResponseDto>> Deny(int id, CancellationToken ct = default)
        {
            var result = await _service.DenyOperatorAsync(id, ct);

            if (result.Outcome == OperatorReviewOutcome.NotFound)
                return NotFound(new { message = "No operator awaiting approval was found for this account." });

            var message = "Operator denied. A notification email has been sent.";

            if (!result.NotificationSent)
                message = "Operator denied, but the notification email could not be sent.";

            return Ok(BuildResponse(result, message));
        }

        // Maps an account action result onto its HTTP response: 404 when missing, 403 when protected, 409 when the current status does not allow it
        private ActionResult<AccountActionResponseDto> AccountResponse(int id, AccountActionResult result)
        {
            var body = new AccountActionResponseDto
            {
                Message = result.Message,
                Id = id,
                Status = result.Status
            };

            if (result.Outcome == AccountActionOutcome.NotFound)
                return NotFound(body);

            if (result.Outcome == AccountActionOutcome.NotAllowed)
                return StatusCode(StatusCodes.Status403Forbidden, body);

            if (result.Outcome == AccountActionOutcome.WrongState)
                return Conflict(body);

            return Ok(body);
        }

        // Maps a team action onto its HTTP response: 404 when the site, worker or invitation was not there
        private IActionResult TeamResponse(TeamActionResult result, string doneMessage, string notFoundMessage)
        {
            if (result.Outcome == TeamActionOutcome.NotFound)
                return NotFound(new { message = notFoundMessage });

            return Ok(new { message = doneMessage });
        }

        // Reads the signed-in admin's email from their token
        private string AdminEmail()
        {
            return SiteAccess.NormalizedEmail(User);
        }

        // Maps a review result onto the response body returned to the web portal
        private static OperatorReviewResponseDto BuildResponse(OperatorReviewResult result, string message)
        {
            return new OperatorReviewResponseDto
            {
                Message = message,
                Id = result.Id,
                Email = result.Email,
                Status = result.Status,
                NotificationSent = result.NotificationSent
            };
        }
    }
}