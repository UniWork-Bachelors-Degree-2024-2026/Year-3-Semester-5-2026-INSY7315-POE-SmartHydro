namespace Group_1_SmartHydro_MVC.Models
{
    /// <summary>
    /// Represents a single sensor card on a structure's dashboard. Holds the sensor's
    /// reading, status and icon, the equipment it controls (such as a pump, lights or fans)
    /// with its on/off state and schedule caption, and the controller and action to link to
    /// the sensor's detail page.
    /// </summary>
    public class DashboardCardViewModel
    {
        public string Label { get; set; } = "";
        public string Value { get; set; } = "No data yet";
        public string Unit { get; set; } = "";

        public bool ValueIsText { get; set; }
        public string ValueCaption { get; set; } = "";
        public PhDisplayFallback? PhDisplayFallback { get; set; }
        public string StatusText { get; set; } = "";
        public string StatusLevel { get; set; } = "good";

        public string IconName { get; set; } = "fa-solid fa-gauge";
        public string IconText { get; set; } = "";
        public bool IconIsMaterialSymbol { get; set; }
        public string IconColorClass { get; set; } = "blue";
        public bool IconFilled { get; set; }

        public string ControlLabel { get; set; } = "";
        public string ControlTag { get; set; } = "";
        public string ControlCaption { get; set; } = "No schedule set";
        public bool ControlOn { get; set; }
        public string ControlKey { get; set; } = "";

        public string? LinkController { get; set; }
        public string? LinkAction { get; set; }
    }
}
