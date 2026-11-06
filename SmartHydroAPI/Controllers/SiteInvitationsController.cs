using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartHydroAPI.Extensions;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Services;

namespace SmartHydroAPI.Controllers
{
    [ApiController]
    [Route("api/sites/{siteId:guid}/invitations")]
    [Authorize(Roles = UserRoles.Operator)]
    public class SiteInvitationsController : ControllerBase
    {
        private const string NotAuthorizedMessage = "You are not authorized to manage workers on this site";

        private readonly SiteInvitationService _service;

        public SiteInvitationsController(SiteInvitationService service)
        {
            _service = service;
        }

        // Invites a worker to a site after the rate limit and ownership checks pass, emailing them a secure link and an access code
        [HttpPost]
        [EnableRateLimiting(SiteInvitationService.RateLimitPolicy)]
        public async Task<ActionResult<SiteInviteResponseDto>> Create(
            Guid siteId,
            [FromBody] CreateSiteInviteDto dto,
            CancellationToken ct = default)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { message = "Enter a valid worker email address." });

            var operatorEmail = User.GetEmail();

            if (string.IsNullOrWhiteSpace(operatorEmail))
                return Unauthorized(new { message = "Your session is missing an email identity." });

            var result = await _service.CreateAsync(siteId, dto.Email, operatorEmail, ct);

            if (result.Outcome == SiteInviteOutcome.NotAuthorized)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = NotAuthorizedMessage });

            if (result.Outcome == SiteInviteOutcome.EmailFailed)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "The invitation email could not be sent. Please try again." });

            return Ok(new SiteInviteResponseDto
            {
                Message = "Invitation sent",
                ExpiresAt = result.ExpiresAt.GetValueOrDefault()
            });
        }
    }
}