namespace SmartHydro_UserGuide_MVC.Models
{
    public class HelpPageViewModel
    {
        public string Title { get; set; } = "";

        public string Icon { get; set; } = "";

        public string Intro { get; set; } = "";

        public List<string> CanDo { get; set; } = new();

        public List<string> CannotDo { get; set; } = new();

        public List<GuideTask> Tasks { get; set; } = new();

        public List<GuideNote> QuickChecks { get; set; } = new();

        public List<GuideQuestion> Questions { get; set; } = new();

        public List<GuideFix> Fixes { get; set; } = new();

        public GuideTable? Messages { get; set; }

        public List<GuideFaqGroup> FaqGroups { get; set; } = new();

        public List<GuideTermGroup> TermGroups { get; set; } = new();

        public GuideTable? EaseOfUse { get; set; }
    }
}
