namespace Group_1_SmartHydro_MVC.Models
{
    /// <summary>
    /// The view model for a structure's dashboard page. Holds the structure being viewed,
    /// the status heading and subtitle, whether the sensors are online, the sensor cards to
    /// display, and any recent alerts for that structure.
    /// </summary>
    public class DashboardViewModel
    {
        public StructureViewModel Structure { get; set; } = new();

        public string StatusTitle { get; set; } = "Hydroponic Status";
        public string StatusSubtitle { get; set; } = "Waiting for sensors to report in.";
        public bool IsOnline { get; set; }
        public bool CanControl { get; set; }
        public bool CanChangeMode { get; set; }

        public List<DashboardCardViewModel> Cards { get; set; } = new();
        public List<DashboardAlertViewModel> Alerts { get; set; } = new();
    }
}