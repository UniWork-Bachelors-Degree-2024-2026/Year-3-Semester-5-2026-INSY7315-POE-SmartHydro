using Group_1_SmartHydro_MVC.Models;
using Group_1_SmartHydro_MVC.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using System.Globalization;
using System.Security.Claims;

namespace Group_1_SmartHydro_MVC.Controllers
{
    public class AlertsController : Controller
    {
        private const int MaxTake = 100;
        private const int ClearBatchSize = 10;
        private const int MaxClearRounds = 1000;

        private readonly AlertApiClient _alerts;
        private readonly ISiteContext _siteContext;

        /// <summary>
        /// Creates the controller and stores the injected API client and site context for use by all actions.
        /// </summary>
        /// <param name="alerts">The client used to read, acknowledge and clear alerts through the API.</param>
        /// <param name="siteContext">Tells the controller which site the user has open.</param>
        public AlertsController(AlertApiClient alerts, ISiteContext siteContext)
        {
            _alerts = alerts;
            _siteContext = siteContext;
        }

        /// <summary>
        /// Displays the alerts page, applying any status, severity and location filters
        /// and limiting the number of alerts shown to the requested amount.
        /// </summary>
        /// <param name="status">Optional status filter (all, active or resolved).</param>
        /// <param name="severity">Optional severity filter (alert or caution).</param>
        /// <param name="location">Optional location filter.</param>
        /// <param name="take">How many matching alerts to display.</param>
        /// <returns>The alerts view populated with the filtered page model.</returns>
        public async Task<IActionResult> Index(string? status, string? severity, string? location, int take = AlertsPageViewModel.PageSize, CancellationToken ct = default)
        {
            return View(await BuildModelAsync(status, severity, location, take, ct));
        }

        /// <summary>
        /// Handles a filter submission by redirecting back to Index with the chosen filters
        /// as clean query-string values. The page size is reset so results start from the top.
        /// </summary>
        /// <param name="status">Selected status filter.</param>
        /// <param name="severity">Selected severity filter.</param>
        /// <param name="location">Selected location filter.</param>
        /// <returns>A redirect to the Index action.</returns>
        public IActionResult Filter(string? status, string? severity, string? location)
        {
            return RedirectToAction(nameof(Index), BuildRoute(status, severity, location, 0));
        }

        /// <summary>
        /// Acknowledges a single alert (marking it as resolved), sets a confirmation message
        /// if successful, and redirects back to Index while keeping the current filters.
        /// </summary>
        /// <param name="id">The ID of the alert to acknowledge.</param>
        /// <param name="status">Current status filter to preserve.</param>
        /// <param name="severity">Current severity filter to preserve.</param>
        /// <param name="location">Current location filter to preserve.</param>
        /// <param name="take">Current number of alerts shown, to preserve.</param>
        /// <returns>A redirect to the Index action.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Acknowledge(string? id, string? status, string? severity, string? location, int take = AlertsPageViewModel.PageSize, CancellationToken ct = default)
        {
            if (!string.IsNullOrWhiteSpace(id) && await _alerts.ResolveAsync(id, ClearedBy(), User.GetAccessToken(), ct))
            {
                TempData["AlertsMessage"] = "Alert acknowledged and moved to Resolved.";
            }

            return RedirectToAction(nameof(Index), BuildRoute(status, severity, location, take));
        }

        /// <summary>
        /// Clears (resolves) all active alerts, stores a message describing how many were cleared,
        /// and redirects back to Index while keeping the current filters.
        /// </summary>
        /// <param name="status">Current status filter to preserve.</param>
        /// <param name="severity">Current severity filter to preserve.</param>
        /// <param name="location">Current location filter to preserve.</param>
        /// <param name="take">Current number of alerts shown, to preserve.</param>
        /// <returns>A redirect to the Index action.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClearAll(string? status, string? severity, string? location, int take = AlertsPageViewModel.PageSize, CancellationToken ct = default)
        {
            var result = await ClearActiveAsync(ct);
            TempData["AlertsMessage"] = DescribeCleared(result.Cleared);

            if (result.Failed > 0)
            {
                TempData["AlertsMessage"] += " " + result.Failed.ToString(CultureInfo.InvariantCulture) + " could not be cleared. Please try again.";
            }

            return RedirectToAction(nameof(Index), BuildRoute(status, severity, location, take));
        }

        /// <summary>
        /// Builds the view model for the alerts page. Loads all alerts, validates the filter
        /// values, applies the filters, sorts the results by rank then newest first, calculates
        /// the summary counts, and limits the returned list to the requested page size.
        /// </summary>
        /// <param name="status">Raw status filter from the request.</param>
        /// <param name="severity">Raw severity filter from the request.</param>
        /// <param name="location">Raw location filter from the request.</param>
        /// <param name="take">Requested number of alerts to show.</param>
        /// <returns>A fully populated AlertsPageViewModel.</returns>
        private async Task<AlertsPageViewModel> BuildModelAsync(string? status, string? severity, string? location, int take, CancellationToken ct)
        {
            var loadFailed = false;
            var all = new List<AlertItemViewModel>();
            var site = await _siteContext.GetCurrentAsync();

            if (site != null)
            {
                var loaded = await _alerts.GetAlertsAsync(site.Id, User.GetAccessToken(), ct);

                if (loaded == null)
                {
                    loadFailed = true;
                }
                else
                {
                    all = loaded;
                }
            }


            var locations = all.Select(a => a.Location).Distinct().OrderBy(l => l).ToList();

            var cleanStatus = CleanStatus(status);
            var cleanSeverity = CleanSeverity(severity);
            var cleanLocation = CleanLocation(location, locations);
            var cleanTake = Math.Clamp(take, AlertsPageViewModel.PageSize, MaxTake);

            IEnumerable<AlertItemViewModel> query = all;

            if (cleanStatus == AlertStatusFilter.Active)
            {
                query = query.Where(a => !a.IsResolved);
            }

            if (cleanStatus == AlertStatusFilter.Resolved)
            {
                query = query.Where(a => a.IsResolved);
            }

            if (cleanSeverity.Length > 0)
            {
                query = query.Where(a => string.Equals(a.Severity.ToString(), cleanSeverity, StringComparison.OrdinalIgnoreCase));
            }

            if (cleanLocation.Length > 0)
            {
                query = query.Where(a => a.Location == cleanLocation);
            }

            var matches = query
                .OrderBy(a => a.SortRank)
                .ThenByDescending(a => a.OccurredAt)
                .ToList();

            return new AlertsPageViewModel
            {
                IsOnline = !loadFailed,
                LoadFailed = loadFailed,
                LastSyncText = DescribeSync(loadFailed ? null : DateTime.Now),
                Status = cleanStatus,
                Severity = cleanSeverity,
                Location = cleanLocation,
                Take = cleanTake,
                TotalCount = all.Count,
                ActiveCount = all.Count(a => !a.IsResolved),
                ResolvedCount = all.Count(a => a.IsResolved),
                AlertCount = all.Count(a => !a.IsResolved && a.Severity == AlertSeverity.Alert),
                CautionCount = all.Count(a => !a.IsResolved && a.Severity == AlertSeverity.Caution),
                MatchCount = matches.Count,
                Locations = locations,
                Alerts = matches.Take(cleanTake).ToList()
            };
        }

        // Resolves every active alert in the current site through the API, however many there are, and reports how many were cleared and how many could not be
        private async Task<(int Cleared, int Failed)> ClearActiveAsync(CancellationToken ct)
        {
            var site = await _siteContext.GetCurrentAsync();
            if (site == null)
            {
                return (0, 0);
            }

            var accessToken = User.GetAccessToken();
            var clearedBy = ClearedBy();
            var cleared = 0;
            var failed = 0;

            // Resolved alerts leave the active list, so each round loads whatever is still active until none are left
            for (var round = 0; round < MaxClearRounds; round++)
            {
                var batch = await _alerts.GetActiveAlertsPageAsync(site.Id, accessToken, ct);
                if (batch == null || batch.Count == 0)
                {
                    break;
                }

                var clearedThisRound = 0;

                foreach (var group in batch.Chunk(ClearBatchSize))
                {
                    var outcomes = await Task.WhenAll(group.Select(a => _alerts.ResolveAsync(a.Id, clearedBy, accessToken, ct)));
                    clearedThisRound += outcomes.Count(ok => ok);
                    failed += outcomes.Count(ok => !ok);
                }

                cleared += clearedThisRound;

                // Nothing in this round could be cleared, so asking again would only return the same alerts
                if (clearedThisRound == 0)
                {
                    break;
                }
            }

            return (cleared, failed);
        }

        // Names who cleared an alert: the signed-in person's email, or the role if the sign-in has no email
        private string ClearedBy()
        {
            var email = User.GetEmail();

            if (!string.IsNullOrWhiteSpace(email))
            {
                return email;
            }

            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            if (string.IsNullOrWhiteSpace(role))
            {
                return "Operator";
            }

            return role;
        }

        /// <summary>
        /// Builds the route values used when redirecting to Index. Only non-default filter
        /// values are included, so the URL stays short and clean. The take value is capped at MaxTake.
        /// </summary>
        /// <param name="status">Status filter to include.</param>
        /// <param name="severity">Severity filter to include.</param>
        /// <param name="location">Location filter to include.</param>
        /// <param name="take">Number of alerts to show.</param>
        /// <returns>A RouteValueDictionary containing only the relevant filter values.</returns>
        private static RouteValueDictionary BuildRoute(string? status, string? severity, string? location, int take)
        {
            var route = new RouteValueDictionary();
            var cleanStatus = CleanStatus(status);
            var cleanSeverity = CleanSeverity(severity);
            var cleanLocation = (location ?? "").Trim();

            if (cleanStatus != AlertStatusFilter.All)
            {
                route["status"] = cleanStatus;
            }

            if (cleanSeverity.Length > 0)
            {
                route["severity"] = cleanSeverity;
            }

            if (cleanLocation.Length > 0)
            {
                route["location"] = cleanLocation;
            }

            if (take > AlertsPageViewModel.PageSize)
            {
                route["take"] = Math.Min(take, MaxTake);
            }

            return route;
        }

        /// <summary>
        /// Normalises the status filter. Returns "active" or "resolved" if valid;
        /// otherwise falls back to the "all" filter.
        /// </summary>
        /// <param name="value">The raw status value.</param>
        /// <returns>A valid status filter value.</returns>
        private static string CleanStatus(string? value)
        {
            var cleaned = (value ?? "").Trim().ToLowerInvariant();

            if (cleaned == AlertStatusFilter.Active || cleaned == AlertStatusFilter.Resolved)
            {
                return cleaned;
            }

            return AlertStatusFilter.All;
        }

        /// <summary>
        /// Normalises the severity filter. Returns "alert" or "caution" if valid;
        /// otherwise returns an empty string, meaning no severity filter is applied.
        /// </summary>
        /// <param name="value">The raw severity value.</param>
        /// <returns>A valid severity value or an empty string.</returns>
        private static string CleanSeverity(string? value)
        {
            var cleaned = (value ?? "").Trim().ToLowerInvariant();

            if (cleaned == "alert" || cleaned == "caution")
            {
                return cleaned;
            }

            return "";
        }

        /// <summary>
        /// Validates the location filter against the list of known alert locations.
        /// Returns the location if it exists; otherwise returns an empty string (no filter).
        /// </summary>
        /// <param name="value">The raw location value.</param>
        /// <param name="knownLocations">All locations that currently have alerts.</param>
        /// <returns>A valid location or an empty string.</returns>
        private static string CleanLocation(string? value, List<string> knownLocations)
        {
            var cleaned = (value ?? "").Trim();

            if (knownLocations.Contains(cleaned))
            {
                return cleaned;
            }

            return "";
        }

        /// <summary>
        /// Creates a user-friendly message describing how many active alerts were cleared,
        /// using correct wording for zero, one, or multiple alerts.
        /// </summary>
        /// <param name="count">The number of alerts that were cleared.</param>
        /// <returns>A message to display to the user.</returns>
        private static string DescribeCleared(int count)
        {
            if (count == 0)
            {
                return "There were no active alerts to clear.";
            }

            if (count == 1)
            {
                return "1 active alert was cleared and moved to Resolved.";
            }

            return count.ToString(CultureInfo.InvariantCulture) + " active alerts were cleared and moved to Resolved.";
        }

        /// <summary>
        /// Converts the last sync time into a readable relative description, such as
        /// "Just now", "5 min ago" or "3 hr ago". Syncs older than a day show the full date and time.
        /// </summary>
        /// <param name="lastSynced">The time of the last sync, or null if never synced.</param>
        /// <returns>A human-readable description of when the last sync happened.</returns>
        private static string DescribeSync(DateTime? lastSynced)
        {
            if (lastSynced == null)
            {
                return "Not synced yet";
            }

            var syncedAt = lastSynced.Value;
            var elapsed = DateTime.Now - syncedAt;

            if (elapsed.TotalSeconds < 60)
            {
                return "Just now";
            }

            if (elapsed.TotalMinutes < 60)
            {
                /* Microsoft Learn (2026) documents Math.Floor() for rounding a value down to the nearest integer. */
                return Math.Floor(elapsed.TotalMinutes).ToString(CultureInfo.InvariantCulture) + " min ago";
            }

            if (elapsed.TotalHours < 24)
            {
                return Math.Floor(elapsed.TotalHours).ToString(CultureInfo.InvariantCulture) + " hr ago";
            }

            return syncedAt.ToString("d MMM, h:mm tt", CultureInfo.InvariantCulture);
        }
    }
}
/*
    Reference List:
    Microsoft Learn, 2026. Math.Floor Method. [online] Available at: <https://learn.microsoft.com/en-us/dotnet/api/system.math.floor?view=net-10.0> [Accessed 22 September 2026]. 

*/