namespace Group_1_SmartHydro_MVC.Models
{
    /// <summary>
    /// The style of the main (hero) section on a sensor page. Reading shows a numeric
    /// value with a unit, and Status shows a status message instead (for example when
    /// a pump last ran).
    /// </summary>
    public enum SensorHeroKind
    {
        Reading,
        Status
    }

    /// <summary>
    /// The view model for a sensor detail page. Holds the structure the sensor belongs to,
    /// the sensor's name, icon and last sync time, the hero section (either a reading with its unit, optimal range and action
    /// buttons, or a status message), optional stat tiles, the equipment control and its
    /// state, the activity log, and the data for the trend chart.
    /// </summary>
    public class SensorDetailViewModel
    {
        public string StructureId { get; set; } = "";
        public string StructureName { get; set; } = "";
        public string MacAddress { get; set; } = "";
        public string SensorName { get; set; } = "";
        public string LastSyncLabel { get; set; } = "";

        public string IconName { get; set; } = "water_drop";
        public string IconText { get; set; } = "";
        public bool IconIsMaterialSymbol { get; set; }
        public string IconColorClass { get; set; } = "blue";

        public SensorHeroKind HeroKind { get; set; } = SensorHeroKind.Reading;

        public string HeroTitle { get; set; } = "";
        public string HeroLead { get; set; } = "";
        public string HeroDetail { get; set; } = "";
        public string ScheduleText { get; set; } = "";

        public string HeroValue { get; set; } = "";
        public string HeroUnit { get; set; } = "";
        public string HeroSubtitle { get; set; } = "";
        public bool ShowOptimalRange { get; set; }
        public string OptimalRange { get; set; } = "";
        public string HeroCaption { get; set; } = "";
        public string StatusText { get; set; } = "";
        public string StatusLevel { get; set; } = "good";
        public List<SensorHeroActionViewModel> HeroActions { get; set; } = new();

        public bool ShowDosing { get; set; }
        public string DosePendingText { get; set; } = "";
        public string MeasurementNotice { get; set; } = "";
        public PhDisplayFallback? PhDisplayFallback { get; set; }
        public string PhReadingLabel { get; set; } = "";
        public bool ManualPhOverrideAllowed { get; set; } = true;
        public bool ManualPhOverrideRequired { get; set; }
        public string DoseLastText { get; set; } = "";
        public bool DoseHasPending { get; set; }
        public Guid? DoseReviewId { get; set; }

        public List<SensorStatTileViewModel> StatTiles { get; set; } = new();

        public string ControlLabel { get; set; } = "";
        public string ControlCaption { get; set; } = "";
        public string ControlHint { get; set; } = "";
        public bool ControlOn { get; set; }
        public string ControlKey { get; set; } = "";
        public bool CanControl { get; set; }

        public List<ActivityLogEntry> ActivityLog { get; set; } = new();

        public bool ShowTrend { get; set; } = true;
        public List<string> TrendLabels { get; set; } = new();
        public List<double> TrendValues { get; set; } = new();
        public string TrendRangeLabel { get; set; } = "";
    }

    /// <summary>
    /// Represents an action button in a sensor page's hero section, such as "Raise pH"
    /// or "Dose nutrients". Holds the button's icon, its label and whether it uses the
    /// filled button style, plus the dose type it sends when it doses the structure (empty when it does nothing else).
    /// </summary>
    public class SensorHeroActionViewModel
    {
        public string Icon { get; set; } = "add";
        public string Label { get; set; } = "";
        public bool Filled { get; set; }
        public string DoseType { get; set; } = "";
    }

    /// <summary>
    /// Represents a small stat tile on a sensor page, such as "Next Spray Cycle" or
    /// "Reservoir Level". Holds the tile's label, value and unit, plus an optional
    /// status pill with its text and level.
    /// </summary>
    public class SensorStatTileViewModel
    {
        public string Label { get; set; } = "";
        public string Value { get; set; } = "";
        public string Unit { get; set; } = "";
        public string PillText { get; set; } = "";
        public string PillLevel { get; set; } = "good";
    }

    /// <summary>
    /// Represents a single entry in a sensor page's activity log, such as a pump turning
    /// on or a reading going out of range. Holds the title, description and time, and
    /// whether the entry should be shown as a warning.
    /// </summary>
    public class ActivityLogEntry
    {
        public string Source { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Summary { get; set; } = "";
        public string Time { get; set; } = "";
        public bool IsWarning { get; set; }
    }
}
