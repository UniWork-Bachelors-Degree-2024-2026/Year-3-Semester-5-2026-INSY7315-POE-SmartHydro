namespace SmartHydro_UserGuide_MVC.Models
{
    public class GuideTask
    {
        public string Id { get; set; } = "";

        public string Icon { get; set; } = "";

        public string Title { get; set; } = "";

        public string Why { get; set; } = "";

        public GuideWhere? Where { get; set; }

        public string ExtraHtml { get; set; } = "";

        public GuideTable? Table { get; set; }

        public List<string> Steps { get; set; } = new();

        public bool ShowLights { get; set; }

        public bool ShowHotspotFinder { get; set; }

        public List<GuideNote> Notes { get; set; } = new();

        // Returns true when the task has enough steps to be worth walking through one at a time.
        public bool HasWalkthrough()
        {
            return Steps.Count > 1;
        }
    }
}
