namespace SmartHydro_UserGuide_MVC.Models
{
    public class GuideFix
    {
        public string Id { get; set; } = "";

        public string Title { get; set; } = "";

        public List<string> Steps { get; set; } = new();

        public string LinkHref { get; set; } = "";

        public string LinkLabel { get; set; } = "";

        // Returns the element id used for this fix's panel, so answers can link to it.
        public string GetPanelId()
        {
            return "ts-" + Id;
        }

        // Returns true when the fix points to a guide section that shows how to do it.
        public bool HasLink()
        {
            return !string.IsNullOrEmpty(LinkHref);
        }
    }
}
