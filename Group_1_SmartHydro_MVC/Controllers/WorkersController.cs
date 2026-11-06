using Group_1_SmartHydro_MVC.Models;
using Group_1_SmartHydro_MVC.Patterns.Observer;
using Group_1_SmartHydro_MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Group_1_SmartHydro_MVC.Controllers
{
    [Authorize(Roles = "Operator")]
    public class WorkersController : Controller
    {
        private const string RateLimitedMessage = "Invite quota reached. Please wait before inviting more workers.";
        private const string ForbiddenMessage = "You are not authorized to manage workers on this site";

        private readonly SiteInvitationApiClient _siteInvitations;
        private readonly ISiteContext _siteContext;
        private readonly ISiteChangePublisher _changes;

        public WorkersController(SiteInvitationApiClient siteInvitations, ISiteContext siteContext, ISiteChangePublisher changes)
        {
            _siteInvitations = siteInvitations;
            _siteContext = siteContext;
            _changes = changes;
        }

        // Shows the Invite Worker form with the Operator's own sites, preselecting the site the Operator came from
        [HttpGet]
        public async Task<IActionResult> Invite(Guid? siteId, string? email, CancellationToken ct)
        {
            ViewData["Title"] = "Invite Worker";

            var sites = await _siteInvitations.GetMySitesAsync(AccessToken(), ct);

            if (sites is null)
            {
                ViewData["InviteWorkerError"] =
                    "Could not load your sites. Your session may have expired - try signing out and back in, or try again.";
                sites = new List<OperatorSite>();
            }
            else if (sites.Count == 0)
            {
                ViewData["InviteWorkerError"] =
                    "You don't have a site to invite a worker to yet. Create one from the site picker in the sidebar first.";
            }

            var model = new InviteWorkerViewModel { Sites = sites, Email = CleanEmail(email) };
            await LoadTeamAsync(model, ct);

            if (!siteId.HasValue)
                siteId = await CurrentSiteIdAsync();

            if (siteId.HasValue && sites.Any(s => s.Id == siteId.Value))
                model.SiteId = siteId.Value;
            else if (sites.Count == 1)
                model.SiteId = sites[0].Id;

            return View(model);
        }

        // Takes a worker off a site after the Operator confirms, and tells the worker's open pages straight away
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveMember(Guid siteId, int userId, string? email, CancellationToken ct)
        {
            var outcome = await _siteInvitations.RemoveMemberAsync(siteId, userId, AccessToken(), ct);

            if (outcome.Succeeded)
            {
                TempData["InviteWorkerMessage"] = outcome.Message;
                await _changes.PublishAsync(new SiteChange(siteId.ToString(), SiteChangeKind.WorkerRemoved, (email ?? "").Trim(), User.GetEmail()), ct);
            }
            else
            {
                TempData["InviteWorkerError"] = outcome.Message;
            }

            return RedirectToAction(nameof(Invite), new { siteId });
        }

        // Cancels an unused invitation after the Operator confirms
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelInvite(Guid siteId, string? email, CancellationToken ct)
        {
            var cleaned = (email ?? "").Trim();

            if (cleaned.Length == 0)
            {
                TempData["InviteWorkerError"] = "Choose an invitation to cancel.";
                return RedirectToAction(nameof(Invite), new { siteId });
            }

            var outcome = await _siteInvitations.CancelInviteAsync(siteId, cleaned, AccessToken(), ct);

            if (outcome.Succeeded)
            {
                TempData["InviteWorkerMessage"] = outcome.Message;
            }
            else
            {
                TempData["InviteWorkerError"] = outcome.Message;
            }

            return RedirectToAction(nameof(Invite), new { siteId });
        }

        // Loads the workers and invitations for the team list under the form
        private async Task LoadTeamAsync(InviteWorkerViewModel model, CancellationToken ct)
        {
            var team = await _siteInvitations.GetTeamAsync(AccessToken(), ct);

            if (team is null)
            {
                model.TeamLoadFailed = true;
                return;
            }

            model.Team = team;
        }

        // Keeps an email passed in the address (from "Invite again") only when it is a sensible length
        private static string? CleanEmail(string? email)
        {
            var cleaned = (email ?? "").Trim();

            if (cleaned.Length == 0 || cleaned.Length > 255)
                return null;

            return cleaned;
        }

        // Returns the site the Operator is currently working in, so the form starts on that site
        private async Task<Guid?> CurrentSiteIdAsync()
        {
            var current = await _siteContext.GetCurrentAsync();

            if (current != null && Guid.TryParse(current.Id, out var id))
                return id;

            return null;
        }

        // Relays the invitation to the API, which enforces the rate limit and site ownership, and shows the outcome
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Invite(InviteWorkerViewModel model, CancellationToken ct)
        {
            ViewData["Title"] = "Invite Worker";

            var sites = await _siteInvitations.GetMySitesAsync(AccessToken(), ct);

            if (sites is null)
                sites = new List<OperatorSite>();

            model.Sites = sites;
            await LoadTeamAsync(model, ct);

            if (!ModelState.IsValid)
                return View(model);

            var workerEmail = model.Email!.Trim().ToLowerInvariant();
            var outcome = await _siteInvitations.InviteWorkerAsync(model.SiteId!.Value, workerEmail, AccessToken(), ct);

            if (outcome.Result == SiteInviteResult.Sent)
            {
                TempData["InviteWorkerMessage"] = $"Invitation sent to {workerEmail}.";
                return RedirectToAction(nameof(Invite), new { siteId = model.SiteId });
            }

            ModelState.AddModelError(string.Empty, ErrorMessageFor(outcome));
            return View(model);
        }

        // Chooses the message shown for a failed invitation, preferring the API's own wording when it sent one
        private static string ErrorMessageFor(SiteInviteOutcome outcome)
        {
            if (outcome.Result == SiteInviteResult.RateLimited)
                return RateLimitedMessage;

            if (outcome.Result == SiteInviteResult.Forbidden)
                return ForbiddenMessage;

            if (outcome.Result == SiteInviteResult.SessionExpired)
                return "Your session has expired. Sign out and back in, then try again.";

            if (!string.IsNullOrWhiteSpace(outcome.Message))
                return outcome.Message;

            if (outcome.Result == SiteInviteResult.InvalidInput)
                return "Enter a valid worker email address.";

            return "Could not send the invitation. Please try again.";
        }

        // Reads the API bearer token stored on the signed-in Operator's cookie
        private string AccessToken()
        {
            return User.FindFirst("access_token")?.Value ?? string.Empty;
        }
    }
}