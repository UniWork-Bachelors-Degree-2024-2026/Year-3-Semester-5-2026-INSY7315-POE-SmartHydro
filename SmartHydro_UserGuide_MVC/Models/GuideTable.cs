namespace SmartHydro_UserGuide_MVC.Models
{
    public class GuideTable
    {
        public List<string> Headers { get; set; } = new();

        public List<List<string>> Rows { get; set; } = new();
    }
}
