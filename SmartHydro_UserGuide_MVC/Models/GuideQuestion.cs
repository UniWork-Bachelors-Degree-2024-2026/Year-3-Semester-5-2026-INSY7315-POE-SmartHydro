namespace SmartHydro_UserGuide_MVC.Models
{
    public class GuideQuestion
    {
        public string Id { get; set; } = "";

        public string Text { get; set; } = "";

        public List<GuideAnswer> Answers { get; set; } = new();

        // Returns the element id used for this question's panel, so answers can link to it.
        public string GetPanelId()
        {
            return "ts-" + Id;
        }
    }
}
