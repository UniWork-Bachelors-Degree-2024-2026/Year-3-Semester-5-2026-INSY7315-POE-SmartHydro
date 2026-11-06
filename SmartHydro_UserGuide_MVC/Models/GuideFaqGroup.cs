namespace SmartHydro_UserGuide_MVC.Models
{
    public class GuideFaqGroup
    {
        public string Id { get; set; } = "";

        public string Icon { get; set; } = "";

        public string Title { get; set; } = "";

        public List<GuideFaq> Items { get; set; } = new();
    }
}
