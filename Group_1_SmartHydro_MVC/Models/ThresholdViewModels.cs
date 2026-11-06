namespace Group_1_SmartHydro_MVC.Models
{
    /// <summary>
    /// The thresholds page: one section for each structure in the current site, each listing the readings
    /// that structure has a safe range for.
    /// </summary>
    public class ThresholdsPageViewModel
    {
        public List<ThresholdSectionViewModel> Sections { get; set; } = new();
        public bool LoadFailed { get; set; }
        public bool HasSite { get; set; }
    }

    /// <summary>
    /// The safe ranges for one structure, plus a message when the last save of this
    /// section did not go through. Key is the structure's id.
    /// </summary>
    public class ThresholdSectionViewModel
    {
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Message { get; set; } = "";
        public List<ThresholdRowViewModel> Rows { get; set; } = new();
    }

    /// <summary>
    /// One reading's safe range as shown on the page. Min and Max are kept as text so a value the
    /// operator typed is shown back exactly as typed when it is refused.
    /// </summary>
    public class ThresholdRowViewModel
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string Unit { get; set; } = "";
        public string Lowest { get; set; } = "";
        public string Highest { get; set; } = "";
        public string Min { get; set; } = "";
        public string Max { get; set; } = "";
        public string Error { get; set; } = "";
    }

    /// <summary>
    /// The form posted when an operator saves one structure's ranges. Numbers arrive as text
    /// and are read with the invariant culture, so the site's language cannot change what they mean.
    /// </summary>
    public class ThresholdSectionForm
    {
        public string? StructureId { get; set; }
        public List<ThresholdRowForm> Rows { get; set; } = new();
    }

    /// <summary>
    /// One reading's minimum and maximum as typed into the form.
    /// </summary>
    public class ThresholdRowForm
    {
        public string? Sensor { get; set; }
        public string? Min { get; set; }
        public string? Max { get; set; }
    }
}
