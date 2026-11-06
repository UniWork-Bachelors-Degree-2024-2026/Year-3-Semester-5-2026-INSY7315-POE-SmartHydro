using System.Globalization;

namespace Group_1_SmartHydro_MVC.Models
{
    /// <summary>
    /// The severity level of an alert. Alert is the most serious, and Caution is a lower-priority
    /// warning. The order matters because it is used when sorting alerts.
    /// </summary>
    public enum AlertSeverity
    {
        Alert,
        Caution
    }

    /// <summary>
    /// Represents a single alert shown on the Alerts page. Holds the alert's details
    /// (the site it belongs to, component, location, severity, value and time) and provides read-only helpers that
    /// work out the CSS class, label, icon, sort order and display text for the view.
    /// </summary>
    public class AlertItemViewModel
    {
        public string Id { get; set; } = "";
        public string SiteId { get; set; } = "";
        public string Component { get; set; } = "";
        public string Location { get; set; } = "";

        public string IconName { get; set; } = "notifications";
        public string IconText { get; set; } = "";
        public bool IconIsMaterialSymbol { get; set; }
        public string IconColorClass { get; set; } = "blue";

        public AlertSeverity Severity { get; set; }
        public bool IsResolved { get; set; }
        public string Value { get; set; } = "";
        public string Detail { get; set; } = "";
        public DateTime OccurredAt { get; set; }
        public string ClearedBy { get; set; } = "";

        /// <summary>
        /// Gets the CSS class used to colour the alert based on its state:
        /// resolved, alert or caution.
        /// </summary>
        public string StatusClass
        {
            get
            {
                if (IsResolved)
                {
                    return "level-resolved";
                }

                if (Severity == AlertSeverity.Alert)
                {
                    return "level-alert";
                }

                return "level-caution";
            }
        }

        /// <summary>
        /// Gets the text label shown for the alert's state: "Resolved", "Alert" or "Caution".
        /// </summary>
        public string StatusLabel
        {
            get
            {
                if (IsResolved)
                {
                    return "Resolved";
                }

                if (Severity == AlertSeverity.Alert)
                {
                    return "Alert";
                }

                return "Caution";
            }
        }

        /// <summary>
        /// Gets the Material Symbol icon name for the alert's state: a check for resolved,
        /// a warning sign for alerts, and an info icon for cautions.
        /// </summary>
        public string StatusIcon
        {
            get
            {
                if (IsResolved)
                {
                    return "check_circle";
                }

                if (Severity == AlertSeverity.Alert)
                {
                    return "warning";
                }

                return "info";
            }
        }

        /// <summary>
        /// Gets a number used to sort alerts so the most urgent appear first:
        /// active alerts (0), then active cautions (1), then resolved alerts (2).
        /// </summary>
        public int SortRank
        {
            get
            {
                if (IsResolved)
                {
                    return 2;
                }

                return (int)Severity;
            }
        }

        /// <summary>
        /// Gets the time the alert occurred, formatted like "3:45 PM".
        /// </summary>
        public string TimeText
        {
            get { return OccurredAt.ToString("h:mm tt", CultureInfo.InvariantCulture); }
        }

        /// <summary>
        /// Gets a friendly description of the day the alert occurred: "Today", "Yesterday",
        /// or a short date such as "20 Sep" for anything older.
        /// </summary>
        public string DayText
        {
            get
            {
                var days = (DateTime.Today - OccurredAt.Date).Days;

                if (days <= 0)
                {
                    return "Today";
                }

                if (days == 1)
                {
                    return "Yesterday";
                }

                return OccurredAt.ToString("d MMM", CultureInfo.InvariantCulture);
            }
        }
    }
}