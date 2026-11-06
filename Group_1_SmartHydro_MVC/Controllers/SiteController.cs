using Group_1_SmartHydro_MVC.Patterns.Observer;
using Group_1_SmartHydro_MVC.Services;
using Microsoft.AspNetCore.Mvc;

namespace Group_1_SmartHydro_MVC.Controllers
{
    public class SiteController : Controller
    {
        public const int MaxNameLength = 60;

        private readonly SiteApiClient _siteApi;
        private readonly ISiteContext _siteContext;
        private readonly ISiteChangePublisher _changes;

        public SiteController(SiteApiClient siteApi, ISiteContext siteContext, ISiteChangePublisher changes)
        {
            _siteApi = siteApi;
            _siteContext = siteContext;
            _changes = changes;
        }

        // Switches to the chosen site and opens its structure list, ignoring sites the user cannot see
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Select(string? id, string? returnUrl)
        {
            var sites = await _siteContext.GetSitesAsync();
            var site = sites.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

            if (site == null)
            {
                return RedirectBack(returnUrl);
            }

            _siteContext.Select(site.Id);
            return RedirectToAction("Index", "Structure");
        }

        // Creates a site from the pop-up and switches to it; an Admin can give the new site straight to an operator
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string? name, string? ownerEmail, CancellationToken ct)
        {
            if (!User.CanManageSites())
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { ok = false, message = "Only operators and admins can create sites." });
            }

            var cleanName = Clean(name, MaxNameLength + 1);
            var errors = ValidateSite(cleanName);

            if (errors.Count > 0)
            {
                return BadRequest(new { ok = false, errors });
            }

            string? owner = null;
            if (User.IsInRole("Admin") && !string.IsNullOrWhiteSpace(ownerEmail))
            {
                owner = ownerEmail.Trim();
            }

            var outcome = await _siteApi.CreateSiteAsync(cleanName, owner, User.GetAccessToken(), ct);

            if (outcome.Result != SiteSaveResult.Saved || outcome.Site == null)
            {
                return SaveFailed(outcome);
            }

            _siteContext.Select(outcome.Site.Id);
            TempData["SiteCreated"] = outcome.Site.Name;

            await _changes.PublishAsync(new SiteChange(outcome.Site.Id, SiteChangeKind.SiteCreated, outcome.Site.Name, User.GetEmail()), ct);

            return Json(new { ok = true, redirect = Url.Action("Index", "Structure") });
        }

        // Saves changes to a site from the pop-up, returning JSON so errors show without a reload
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string? id, string? name, CancellationToken ct)
        {
            if (!User.CanManageSites() || string.IsNullOrWhiteSpace(id))
            {
                return NotFound(new { ok = false, message = "That site could not be found. It may have been removed." });
            }

            var cleanName = Clean(name, MaxNameLength + 1);
            var errors = ValidateSite(cleanName);

            if (errors.Count > 0)
            {
                return BadRequest(new { ok = false, errors });
            }

            var outcome = await _siteApi.UpdateSiteAsync(id, cleanName, User.GetAccessToken(), ct);

            if (outcome.Result != SiteSaveResult.Saved)
            {
                return SaveFailed(outcome);
            }

            await _changes.PublishAsync(new SiteChange(id, SiteChangeKind.SiteUpdated, cleanName, User.GetEmail()), ct);

            return Json(new { ok = true });
        }

        // Deletes a site after the pop-up confirms; its structures move to deleted items and its workers lose access
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string? id, CancellationToken ct)
        {
            if (!User.CanManageSites() || string.IsNullOrWhiteSpace(id))
            {
                return NotFound(new { ok = false, message = "That site could not be found. It may have been removed." });
            }

            var sites = await _siteContext.GetSitesAsync();
            var site = sites.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            var current = await _siteContext.GetCurrentAsync();
            var wasCurrent = current != null && string.Equals(current.Id, id, StringComparison.OrdinalIgnoreCase);

            var outcome = await _siteApi.DeleteSiteAsync(id, true, User.GetAccessToken(), ct);

            if (outcome.Result != SiteDeleteResult.Deleted)
            {
                return DeleteFailed(outcome);
            }

            var label = site?.Name ?? "The site";

            if (wasCurrent)
            {
                SiteCookies.ClearSiteId(HttpContext);
                SiteCookies.ClearStructureId(HttpContext);
            }

            TempData["SiteDeleted"] = label;
            TempData["SiteDeletedStructures"] = outcome.StructureCount;

            await _changes.PublishAsync(new SiteChange(site?.Id ?? id, SiteChangeKind.SiteDeleted, label, User.GetEmail()), ct);

            return Json(new { ok = true, redirect = Url.Action("Index", "Structure") });
        }

        // Turns a failed API delete into the JSON error the delete pop-up understands
        private IActionResult DeleteFailed(SiteDeleteOutcome outcome)
        {
            if (outcome.Result == SiteDeleteResult.NotFound)
            {
                return NotFound(new { ok = false, message = "That site could not be found. It may have been removed." });
            }

            var message = outcome.Message;
            if (string.IsNullOrWhiteSpace(message))
            {
                message = "The site could not be deleted. Please try again.";
            }

            return BadRequest(new { ok = false, message });
        }

        // Turns a failed API save into the JSON error the site pop-up understands
        private IActionResult SaveFailed(SiteSaveOutcome outcome)
        {
            if (outcome.Result == SiteSaveResult.DuplicateName)
            {
                return BadRequest(new { ok = false, errors = new Dictionary<string, string> { ["name"] = "You already have a site with this name." } });
            }

            if (outcome.Result == SiteSaveResult.NotFound)
            {
                return NotFound(new { ok = false, message = "That site could not be found. It may have been removed." });
            }

            var message = outcome.Message;
            if (string.IsNullOrWhiteSpace(message))
            {
                message = "The site could not be saved. Please try again.";
            }

            return BadRequest(new { ok = false, message });
        }

        // Checks the name is present and short enough
        private static Dictionary<string, string> ValidateSite(string name)
        {
            var errors = new Dictionary<string, string>();

            if (name.Length == 0)
            {
                errors["name"] = "Please enter a site name.";
            }
            else if (name.Length > MaxNameLength)
            {
                errors["name"] = "Site name must be " + MaxNameLength + " characters or fewer.";
            }

            return errors;
        }

        // Removes control characters and surrounding spaces, and caps the length so very long input is not processed further
        private static string Clean(string? value, int cap)
        {
            var cleaned = new string((value ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (cleaned.Length > cap)
            {
                cleaned = cleaned.Substring(0, cap);
            }

            return cleaned;
        }

        // Redirects to the given local URL, or to the structure list when it is missing or points to another website
        private IActionResult RedirectBack(string? returnUrl)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }

            return RedirectToAction("Index", "Structure");
        }
    }
}