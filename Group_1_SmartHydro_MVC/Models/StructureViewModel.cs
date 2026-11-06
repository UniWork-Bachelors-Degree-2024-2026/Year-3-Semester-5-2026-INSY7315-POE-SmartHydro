namespace Group_1_SmartHydro_MVC.Models
{
    public class StructureViewModel
    {
        public string Id { get; set; } = "";
        public string SiteId { get; set; } = "";
        public string Name { get; set; } = "";
        public StructureType Type { get; set; }
        public string StatusText { get; set; } = "No data yet";
        public string StatusLevel { get; set; } = "none";
        public string StatusNote { get; set; } = "";
        public string IconName { get; set; } = "fa-solid fa-seedling";
        public bool IconIsMaterialSymbol { get; set; }
        public bool IsOnline { get; set; }
        public DateTime? LastSeenAt { get; set; }
        public string LabelColour { get; set; } = "";
        public string MacAddress { get; set; } = "";
        public string CameraMac { get; set; } = "";
        public DateTime? CreatedAt { get; set; }

        public string Temperature { get; set; } = "No Data";
        public string Humidity { get; set; } = "No Data";
        public string Ec { get; set; } = "No Data";
        public string Ph { get; set; } = "No Data";
        public string UpdatedAt { get; set; } = "No Data";
    }

    public class StructureListViewModel
    {
        public List<StructureViewModel> Structures { get; set; } = new();
        public List<StructureViewModel> DeletedStructures { get; set; } = new();
        public bool CanManage { get; set; }
        public bool LoadFailed { get; set; }
    }
}