namespace Group_1_SmartHydro_MVC.Models
{
    /// <summary>
    /// Represents a single alert shown in the alerts section of a structure's dashboard.
    /// Holds the alert's title, the time it happened, and the icon and colour used to display it.
    /// </summary>
    public class DashboardAlertViewModel
    {
        public string Title { get; set; } = "";
        public string Time { get; set; } = "";
        public string IconName { get; set; } = "notifications";
        public string IconColorClass { get; set; } = "blue";
    }
}