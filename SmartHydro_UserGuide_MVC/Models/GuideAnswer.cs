namespace SmartHydro_UserGuide_MVC.Models
{
    public class GuideAnswer
    {
        public string Icon { get; set; } = "";

        public string Label { get; set; } = "";

        public string Next { get; set; } = "";

        // Returns the element id of the question or fix this answer leads to.
        public string GetTargetPanelId()
        {
            return "ts-" + Next;
        }
    }
}
