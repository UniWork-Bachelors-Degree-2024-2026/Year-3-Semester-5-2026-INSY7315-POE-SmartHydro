namespace SmartHydro_UserGuide_MVC.Models
{
    public class GuideTermGroup
    {
        public string Id { get; set; } = "";

        public string Icon { get; set; } = "";

        public string Title { get; set; } = "";

        public string Intro { get; set; } = "";

        public List<GuideTerm> Terms { get; set; } = new();
    }
}
