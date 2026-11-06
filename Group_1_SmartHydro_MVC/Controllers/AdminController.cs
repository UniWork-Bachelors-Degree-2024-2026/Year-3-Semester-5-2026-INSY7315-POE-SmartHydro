using Group_1_SmartHydro_MVC.Models;
using Group_1_SmartHydro_MVC.Patterns.Observer;
using Group_1_SmartHydro_MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Group_1_SmartHydro_MVC.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private const string OperatorRole = "Operator";
        private const string ApprovedStatus = "Approved";
        private const string PendingStatus = "PendingApproval";
        private const string DisabledStatus = "Disabled";
        private const int RecentSignUpCount = 5;
        private const int MaxSearchLength = 100;

        private static readonly TimeSpan ActiveWindow = TimeSpan.FromDays(7);

        private readonly AdminApiClient _admin;
        private readonly SiteApiClient _siteApi;
        private readonly ISiteChangePublisher _changes;

        public AdminController(AdminApiClient admin, SiteApiClient siteApi, ISiteChangePublisher changes)
        {
            _admin = admin;
            _siteApi = siteApi;
            _changes = changes;
        }

        // Shows the admin dashboard: headline counts, the newest sign-ups and a filterable list of every user
        [HttpGet]
        public async Task<IActionResult> Index(string? role, string? status, string? q, CancellationToken ct)
        {
            ViewData["Title"] = "Admin Dashboard";
            ViewData["LiveRefresh"] = true;

            var model = new AdminDashboardViewModel
            {
                Role = CleanFilter(role, AdminUserFilters.Roles),
                Status = CleanFilter(status, AdminUserFilters.Statuses),
                Search = CleanSearch(q)
            };

            var users = await _admin.GetUsersAsync(User.GetAccessToken(), ct);
            var sites = await _admin.GetSitesAsync(User.GetAccessToken(), ct);

            if (users == null || sites == null)
            {
                model.LoadFailed = true;
                return View(model);
            }

            var activeSince = DateTime.UtcNow - ActiveWindow;

            model.TotalUsers = users.Count;
            model.OperatorCount = users.Count(u => u.Role == OperatorRole && u.Status == ApprovedStatus);
            model.WorkerCount = users.Count(u => u.Role == "Worker");
            model.AdminCount = users.Count(u => u.Role == "Admin");
            model.PendingCount = users.Count(u => u.Status == PendingStatus);
            model.DisabledCount = users.Count(u => u.Status == DisabledStatus);
            model.SiteCount = sites.Count;
            model.StructureCount = sites.Sum(s => s.StructureCount);
            model.ActiveThisWeek = users.Count(u => u.LastLoginAt.HasValue && u.LastLoginAt.Value >= activeSince);
            model.OperatorsNotApproved = users.Count(u => u.Role == OperatorRole && u.Status != ApprovedStatus);
            model.RecentSignUps = users.OrderByDescending(u => u.CreatedAt).Take(RecentSignUpCount).ToList();
            model.Users = users
                .OrderBy(u => u.Status != PendingStatus)
                .ThenBy(u => u.Role)
                .ThenBy(u => u.Email)
                .ToList();

            return View(model);
        }

        // Shows every site grouped under the operator who owns it, with ways to give an operator a site or move a site to another operator
        [HttpGet]
        public async Task<IActionResult> Sites(CancellationToken ct)
        {
            ViewData["Title"] = "Sites by Operator";
            ViewData["LiveRefresh"] = true;

            var model = new AdminSitesViewModel();

            var users = await _admin.GetUsersAsync(User.GetAccessToken(), ct);
            var sites = await _admin.GetSitesAsync(User.GetAccessToken(), ct);

            if (users == null || sites == null)
            {
                model.LoadFailed = true;
                return View(model);
            }

            var usersByEmail = users
                .GroupBy(u => u.Email.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First());

            model.SiteCount = sites.Count;
            model.Operators = users
                .Where(u => u.Role == OperatorRole && u.Status == ApprovedStatus)
                .OrderBy(u => u.Email)
                .ToList();

            var groups = new Dictionary<string, OperatorSitesGroupViewModel>();

            foreach (var site in sites)
            {
                var ownerKey = (site.OwnerEmail ?? "").Trim().ToLowerInvariant();

                if (ownerKey.Length == 0)
                {
                    continue;
                }

                if (!groups.TryGetValue(ownerKey, out var group))
                {
                    group = BuildGroup(FindOwner(usersByEmail, ownerKey));
                    groups[ownerKey] = group;
                }

                group.Sites.Add(site);
            }

            foreach (var op in model.Operators)
            {
                var key = op.Email.ToLowerInvariant();
                if (!groups.ContainsKey(key))
                {
                    groups[key] = BuildGroup(op);
                }
            }

            model.Groups = groups.Values
                .OrderBy(g => g.OwnerRole != OperatorRole)
                .ThenBy(g => g.OwnerName)
                .ToList();

            var workers = await _admin.GetWorkersAsync(User.GetAccessToken(), ct);
            if (workers == null)
            {
                model.WorkersLoadFailed = true;
            }
            else
            {
                model.Workers = workers
                    .OrderBy(w => w.SiteName)
                    .ThenByDescending(w => w.Since)
                    .ToList();
            }

            var deletedSites = await _admin.GetDeletedSitesAsync(User.GetAccessToken(), ct);
            if (deletedSites == null)
            {
                model.DeletedSitesLoadFailed = true;
            }
            else
            {
                model.DeletedSites = deletedSites;
            }

            return View(model);
        }

        // Disables an approved account, then returns to the same filtered user list
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeactivateUser(int id, string? email, string? returnUrl, CancellationToken ct)
        {
            var outcome = await _admin.DeactivateUserAsync(id, User.GetAccessToken(), ct);
            return await AfterAccountActionAsync(outcome, email, returnUrl, ct);
        }

        // Re-enables a disabled account, then returns to the same filtered user list
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReactivateUser(int id, string? email, string? returnUrl, CancellationToken ct)
        {
            var outcome = await _admin.ReactivateUserAsync(id, User.GetAccessToken(), ct);
            return await AfterAccountActionAsync(outcome, email, returnUrl, ct);
        }

        // Saves the admin's edits to an account, then applies the chosen status change, and returns to the same filtered user list
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditUser(int id, string? email, string? fullName, string? farmName, string? role, string? statusAction, string? returnUrl, CancellationToken ct)
        {
            var cleanEmail = (email ?? "").Trim();
            var cleanRole = (role ?? "").Trim();

            if (cleanRole.Length == 0)
            {
                return await AfterAccountActionAsync(new OperatorReviewOutcome(false, "Choose a role for this account."), cleanEmail, returnUrl, ct);
            }

            var update = new AdminUserUpdate(fullName, farmName, cleanRole);
            var saved = await _admin.UpdateUserAsync(id, update, User.GetAccessToken(), ct);

            if (!saved.Succeeded || string.IsNullOrWhiteSpace(statusAction))
            {
                return await AfterAccountActionAsync(saved, cleanEmail, returnUrl, ct);
            }

            var changed = await ApplyStatusActionAsync(id, statusAction.Trim(), ct);

            if (!changed.Succeeded)
            {
                changed = new OperatorReviewOutcome(false, "Details saved, but the status was not changed. " + changed.Message);
            }

            return await AfterAccountActionAsync(changed, cleanEmail, returnUrl, ct);
        }

        // Sends the status change the admin picked in the edit form to the matching API endpoint
        private Task<OperatorReviewOutcome> ApplyStatusActionAsync(int id, string action, CancellationToken ct)
        {
            var token = User.GetAccessToken();

            if (action == "approve")
            {
                return _admin.ApproveOperatorAsync(id, token, ct);
            }

            if (action == "deny")
            {
                return _admin.DenyOperatorAsync(id, token, ct);
            }

            if (action == "deactivate")
            {
                return _admin.DeactivateUserAsync(id, token, ct);
            }

            if (action == "reactivate")
            {
                return _admin.ReactivateUserAsync(id, token, ct);
            }

            if (action == "reopen")
            {
                return _admin.ReopenUserAsync(id, token, ct);
            }

            return Task.FromResult(new OperatorReviewOutcome(false, "That status change isn't available."));
        }

        // Deletes an account after the admin confirms, then returns to the same filtered user list
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(int id, string? email, string? returnUrl, CancellationToken ct)
        {
            var outcome = await _admin.DeleteUserAsync(id, User.GetAccessToken(), ct);
            return await AfterAccountActionAsync(outcome, email, returnUrl, ct);
        }

        // Shows the API's message for an account action, tells other open admin pages about it, and goes back to the user list
        private async Task<IActionResult> AfterAccountActionAsync(OperatorReviewOutcome outcome, string? email, string? returnUrl, CancellationToken ct)
        {
            if (outcome.Succeeded)
            {
                TempData["AdminUsersMessage"] = outcome.Message;
                await _changes.PublishAsync(new SiteChange("", SiteChangeKind.AccountUpdated, (email ?? "").Trim(), User.GetEmail()), ct);
            }
            else
            {
                TempData["AdminUsersError"] = outcome.Message;
            }

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }

            return RedirectToAction(nameof(Index));
        }

        // Creates a new site owned by the chosen operator
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GiveSite(string? ownerEmail, string? name, CancellationToken ct)
        {
            var cleanName = (name ?? "").Trim();

            if (string.IsNullOrWhiteSpace(ownerEmail) || cleanName.Length == 0 || cleanName.Length > SiteController.MaxNameLength)
            {
                TempData["AdminSitesError"] = "Enter a site name of up to " + SiteController.MaxNameLength + " characters and choose an operator.";
                return RedirectToAction(nameof(Sites));
            }

            var outcome = await _siteApi.CreateSiteAsync(cleanName, ownerEmail.Trim(), User.GetAccessToken(), ct);

            if (outcome.Result != SiteSaveResult.Saved || outcome.Site == null)
            {
                TempData["AdminSitesError"] = DescribeSiteFailure(outcome);
                return RedirectToAction(nameof(Sites));
            }

            TempData["AdminSitesMessage"] = "\"" + outcome.Site.Name + "\" was created for " + ownerEmail.Trim() + ".";
            await _changes.PublishAsync(new SiteChange(outcome.Site.Id, SiteChangeKind.SiteCreated, outcome.Site.Name, User.GetEmail()), ct);

            return RedirectToAction(nameof(Sites));
        }

        // Moves a site, and its structures, to a different approved operator
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignOwner(Guid siteId, string? siteName, string? ownerEmail, CancellationToken ct)
        {
            if (siteId == Guid.Empty || string.IsNullOrWhiteSpace(ownerEmail))
            {
                TempData["AdminSitesError"] = "Choose an operator to give this site to.";
                return RedirectToAction(nameof(Sites));
            }

            var outcome = await _admin.AssignSiteOwnerAsync(siteId, ownerEmail.Trim(), User.GetAccessToken(), ct);

            if (!outcome.Succeeded)
            {
                TempData["AdminSitesError"] = outcome.Message;
                return RedirectToAction(nameof(Sites));
            }

            var label = (siteName ?? "").Trim();
            if (label.Length == 0)
            {
                label = "The site";
            }

            TempData["AdminSitesMessage"] = label + " now belongs to " + ownerEmail.Trim() + ".";
            await _changes.PublishAsync(new SiteChange(siteId.ToString(), SiteChangeKind.SiteOwnerChanged, label, User.GetEmail()), ct);

            return RedirectToAction(nameof(Sites));
        }

        // Deletes a site after the admin confirms; its structures move to deleted items and its open invitations are cancelled
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteSite(Guid siteId, string? siteName, CancellationToken ct)
        {
            if (siteId == Guid.Empty)
            {
                TempData["AdminSitesError"] = "Choose a site to delete.";
                return RedirectToAction(nameof(Sites));
            }

            var outcome = await _admin.DeleteSiteAsync(siteId, User.GetAccessToken(), ct);

            if (!outcome.Succeeded)
            {
                TempData["AdminSitesError"] = outcome.Message;
                return RedirectToAction(nameof(Sites));
            }

            var label = (siteName ?? "").Trim();
            if (label.Length == 0)
            {
                label = "The site";
            }

            if (string.Equals(SiteCookies.ReadSiteId(HttpContext), siteId.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                SiteCookies.ClearSiteId(HttpContext);
                SiteCookies.ClearStructureId(HttpContext);
            }

            TempData["AdminSitesMessage"] = outcome.Message;
            await _changes.PublishAsync(new SiteChange(siteId.ToString(), SiteChangeKind.SiteDeleted, label, User.GetEmail()), ct);

            return RedirectToAction(nameof(Sites));
        }

        // Restores a deleted site, and the structures deleted with it, back to its owner
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreSite(Guid siteId, string? siteName, CancellationToken ct)
        {
            if (siteId == Guid.Empty)
            {
                TempData["AdminSitesError"] = "Choose a site to restore.";
                return RedirectToAction(nameof(Sites));
            }

            var outcome = await _admin.RestoreSiteAsync(siteId, User.GetAccessToken(), ct);

            if (!outcome.Succeeded)
            {
                TempData["AdminSitesError"] = outcome.Message;
                return RedirectToAction(nameof(Sites));
            }

            var label = (siteName ?? "").Trim();
            if (label.Length == 0)
            {
                label = "The site";
            }

            TempData["AdminSitesMessage"] = "\"" + label + "\" was restored, along with the structures deleted with it.";
            await _changes.PublishAsync(new SiteChange(siteId.ToString(), SiteChangeKind.SiteRestored, label, User.GetEmail()), ct);

            return RedirectToAction(nameof(Sites));
        }

        // Permanently deletes a deleted site, and its structures, after the admin confirms; this cannot be undone
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PurgeSite(Guid siteId, string? siteName, CancellationToken ct)
        {
            if (siteId == Guid.Empty)
            {
                TempData["AdminSitesError"] = "Choose a site to permanently delete.";
                return RedirectToAction(nameof(Sites));
            }

            var outcome = await _admin.PurgeSiteAsync(siteId, User.GetAccessToken(), ct);

            if (!outcome.Succeeded)
            {
                TempData["AdminSitesError"] = outcome.Message;
                return RedirectToAction(nameof(Sites));
            }

            var label = (siteName ?? "").Trim();
            if (label.Length == 0)
            {
                label = "The site";
            }

            TempData["AdminSitesMessage"] = "\"" + label + "\" was permanently deleted, along with its structures.";
            await _changes.PublishAsync(new SiteChange(siteId.ToString(), SiteChangeKind.SitePurged, label, User.GetEmail()), ct);

            return RedirectToAction(nameof(Sites));
        }

        // Takes a worker off a site after the admin confirms, and tells the worker's open pages straight away
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveWorker(Guid siteId, int userId, string? email, CancellationToken ct)
        {
            var outcome = await _admin.RemoveWorkerAsync(siteId, userId, User.GetAccessToken(), ct);

            if (outcome.Succeeded)
            {
                TempData["AdminSitesMessage"] = outcome.Message;
                await _changes.PublishAsync(new SiteChange(siteId.ToString(), SiteChangeKind.WorkerRemoved, (email ?? "").Trim(), User.GetEmail()), ct);
            }
            else
            {
                TempData["AdminSitesError"] = outcome.Message;
            }

            return RedirectToAction(nameof(Sites));
        }

        // Cancels an unused worker invitation after the admin confirms
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelInvite(Guid siteId, string? email, CancellationToken ct)
        {
            var cleaned = (email ?? "").Trim();

            if (cleaned.Length == 0)
            {
                TempData["AdminSitesError"] = "Choose an invitation to cancel.";
                return RedirectToAction(nameof(Sites));
            }

            var outcome = await _admin.CancelInvitationAsync(siteId, cleaned, User.GetAccessToken(), ct);

            if (outcome.Succeeded)
            {
                TempData["AdminSitesMessage"] = outcome.Message;
            }
            else
            {
                TempData["AdminSitesError"] = outcome.Message;
            }

            return RedirectToAction(nameof(Sites));
        }

        // Shows the farm operators awaiting review
        [HttpGet]
        public async Task<IActionResult> PendingApprovals(CancellationToken ct)
        {
            ViewData["Title"] = "Pending Approvals";
            ViewData["LiveRefresh"] = true;

            var pending = await _admin.GetPendingOperatorsAsync(User.GetAccessToken(), ct);

            if (pending is null)
            {
                ViewData["PendingApprovalsError"] =
                    "Could not load pending operators. Your session may have expired - try signing out and back in, or try again.";

                return View(new List<PendingOperator>());
            }

            return View(pending);
        }

        // Approves the selected farm operator, which also creates their starting site and emails them
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(int id, string? email, CancellationToken ct)
        {
            var outcome = await _admin.ApproveOperatorAsync(id, User.GetAccessToken(), ct);
            TempData["PendingApprovalsMessage"] = outcome.Message;

            if (outcome.Succeeded)
            {
                await _changes.PublishAsync(new SiteChange("", SiteChangeKind.OperatorReviewed, (email ?? "").Trim(), User.GetEmail()), ct);
            }

            return RedirectToAction(nameof(PendingApprovals));
        }

        // Denies the selected farm operator and emails them the outcome
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Deny(int id, string? email, CancellationToken ct)
        {
            var outcome = await _admin.DenyOperatorAsync(id, User.GetAccessToken(), ct);
            TempData["PendingApprovalsMessage"] = outcome.Message;

            if (outcome.Succeeded)
            {
                await _changes.PublishAsync(new SiteChange("", SiteChangeKind.OperatorReviewed, (email ?? "").Trim(), User.GetEmail()), ct);
            }

            return RedirectToAction(nameof(PendingApprovals));
        }

        // Finds the account that owns a site, describing an owner email with no account behind it as unknown
        private static AdminUser FindOwner(Dictionary<string, AdminUser> usersByEmail, string ownerEmail)
        {
            if (usersByEmail.TryGetValue(ownerEmail, out var owner))
            {
                return owner;
            }

            return new AdminUser { Email = ownerEmail, Role = "Unknown", Status = "No account" };
        }

        // Builds an empty site group headed by the given account
        private static OperatorSitesGroupViewModel BuildGroup(AdminUser owner)
        {
            var name = owner.Email;
            if (!string.IsNullOrWhiteSpace(owner.FullName))
            {
                name = owner.FullName.Trim();
            }

            return new OperatorSitesGroupViewModel
            {
                OwnerEmail = owner.Email,
                OwnerName = name,
                OwnerRole = owner.Role,
                OwnerStatus = owner.Status,
                CanReceiveSites = owner.Role == OperatorRole && owner.Status == ApprovedStatus
            };
        }

        // Keeps a filter value only when it is one of the known options
        private static string CleanFilter(string? value, IReadOnlyList<(string Value, string Label)> options)
        {
            var match = options.FirstOrDefault(o => string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase));

            if (match.Value == null)
            {
                return AdminUserFilters.All;
            }

            return match.Value;
        }

        // Trims the search box and caps its length
        private static string CleanSearch(string? value)
        {
            var cleaned = (value ?? "").Trim();

            if (cleaned.Length > MaxSearchLength)
            {
                cleaned = cleaned.Substring(0, MaxSearchLength);
            }

            return cleaned;
        }

        // Picks the message shown when a site could not be created for an operator
        private static string DescribeSiteFailure(SiteSaveOutcome outcome)
        {
            if (outcome.Result == SiteSaveResult.DuplicateName)
            {
                return "That operator already has a site with this name.";
            }

            if (!string.IsNullOrWhiteSpace(outcome.Message))
            {
                return outcome.Message;
            }

            return "The site could not be created. Please try again.";
        }
    }
}