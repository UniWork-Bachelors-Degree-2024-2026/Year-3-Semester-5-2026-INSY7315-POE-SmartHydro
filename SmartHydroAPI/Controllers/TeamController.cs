using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartHydroAPI.Extensions;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Services;

namespace SmartHydroAPI.Controllers
{
    [ApiController]
    [Route("api/team")]
    [Authorize(Roles = UserRoles.Operator)]
    public class TeamController : ControllerBase
    {
        private readonly SiteInvitationService _invitations;

        public TeamController(SiteInvitationService invitations)
        {
            _invitations = invitations;
        }

        // Returns the workers on the signed-in operator's sites and the invitations they have sent, so the Invite Worker page can show their team
        [HttpGet]
        public async Task<ActionResult<List<TeamMemberDto>>> Get(CancellationToken ct = default)
        {
            var operatorEmail = User.GetEmail();

            if (string.IsNullOrWhiteSpace(operatorEmail))
                return Unauthorized(new { message = "Your session is missing an email identity." });

            return Ok(await _invitations.GetTeamAsync(operatorEmail, ct));
        }

        // Takes a worker off one of the operator's sites; they keep their account and any other sites they belong to
        [HttpDelete("sites/{siteId:guid}/members/{userId:int}")]
        public async Task<IActionResult> RemoveMember(Guid siteId, int userId, CancellationToken ct = default)
        {
            var operatorEmail = User.GetEmail();

            if (string.IsNullOrWhiteSpace(operatorEmail))
                return Unauthorized(new { message = "Your session is missing an email identity." });

            var result = await _invitations.RemoveMemberAsync(siteId, userId, operatorEmail, ct);
            return TeamResponse(result, $"{result.Email} was removed from {result.SiteName}.", "That worker is no longer on this site.");
        }

        // Cancels an unused invitation so its link and access code stop working
        [HttpDelete("sites/{siteId:guid}/invitations")]
        public async Task<IActionResult> CancelInvite(Guid siteId, [FromQuery] string? email, CancellationToken ct = default)
        {
            var operatorEmail = User.GetEmail();

            if (string.IsNullOrWhiteSpace(operatorEmail))
                return Unauthorized(new { message = "Your session is missing an email identity." });

            if (string.IsNullOrWhiteSpace(email))
                return BadRequest(new { message = "Say which invitation to cancel." });

            var result = await _invitations.CancelInviteAsync(siteId, email, operatorEmail, ct);
            return TeamResponse(result, $"The invitation to {result.Email} was cancelled.", "That invitation has already been used or cancelled.");
        }

        // Maps a team action onto its HTTP response: 403 when the caller does not own the site, 404 when there was nothing to remove
        private IActionResult TeamResponse(TeamActionResult result, string doneMessage, string notFoundMessage)
        {
            if (result.Outcome == TeamActionOutcome.NotAuthorized)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "You are not authorized to manage workers on this site" });

            if (result.Outcome == TeamActionOutcome.NotFound)
                return NotFound(new { message = notFoundMessage });

            return Ok(new { message = doneMessage });
        }
    }
}