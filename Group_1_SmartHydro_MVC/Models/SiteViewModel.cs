namespace Group_1_SmartHydro_MVC.Models
{
    public class SiteViewModel
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? OwnerEmail { get; set; }
        public DateTime? CreatedAt { get; set; }
        public int StructureCount { get; set; }
    }

    public class SiteOwnerGroupViewModel
    {
        public string OwnerEmail { get; set; } = "";
        public string OwnerLabel { get; set; } = "";
        public List<SiteViewModel> Sites { get; set; } = new();
    }
}