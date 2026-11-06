namespace Group_1_SmartHydro_MVC.Models
{
    /// <summary>
    /// Holds the allowed values for the alert status filter: all alerts, active alerts only,
    /// or resolved alerts only. Using constants keeps the values consistent across the
    /// controller and views.
    /// </summary>
    public static class AlertStatusFilter
    {
        public const string All = "all";
        public const string Active = "active";
        public const string Resolved = "resolved";
    }

    /// <summary>
    /// The view model for the Alerts page. Holds the connection and sync status, the
    /// currently selected filters, summary counts, the list of locations for the filter
    /// dropdown, and the alerts to display. Also provides helpers for "show more" paging
    /// and for counting active filters.
    /// </summary>
    public class AlertsPageViewModel
    {
        public const int PageSize = 5;

        public bool IsOnline { get; set; }
        public bool LoadFailed { get; set; }
        public string LastSyncText { get; set; } = "";

        public string Status { get; set; } = AlertStatusFilter.All;
        public string Severity { get; set; } = "";
        public string Location { get; set; } = "";
        public int Take { get; set; } = PageSize;

        public int TotalCount { get; set; }
        public int ActiveCount { get; set; }
        public int ResolvedCount { get; set; }
        public int AlertCount { get; set; }
        public int CautionCount { get; set; }
        public int MatchCount { get; set; }

        public List<string> Locations { get; set; } = new();
        public List<AlertItemViewModel> Alerts { get; set; } = new();

        /// <summary>
        /// Gets whether there are more matching alerts than are currently shown.
        /// Used to decide whether to display the "show more" button.
        /// </summary>
        public bool HasMore
        {
            get { return Alerts.Count < MatchCount; }
        }

        /// <summary>
        /// Gets how many alerts to request when the user clicks "show more",
        /// which is the current amount plus one more page.
        /// </summary>
        public int NextTake
        {
            get { return Take + PageSize; }
        }

        /// <summary>
        /// Gets how many optional filters are currently applied (severity and/or location).
        /// The status filter is not counted. Used to show a filter count badge in the view.
        /// </summary>
        public int ActiveFilterCount
        {
            get
            {
                var count = 0;

                if (Severity.Length > 0)
                {
                    count++;
                }

                if (Location.Length > 0)
                {
                    count++;
                }

                return count;
            }
        }
    }
}