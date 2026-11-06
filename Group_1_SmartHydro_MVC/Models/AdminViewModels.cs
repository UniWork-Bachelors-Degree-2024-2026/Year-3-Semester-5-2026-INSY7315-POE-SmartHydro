using Group_1_SmartHydro_MVC.Services;

namespace Group_1_SmartHydro_MVC.Models
{
    public static class AdminUserFilters
    {
        public const string All = "all";

        public static readonly IReadOnlyList<(string Value, string Label)> Roles = new List<(string, string)>
        {
            (All, "All roles"),
            ("Operator", "Operators"),
            ("Worker", "Workers"),
            ("Admin", "Admins")
        };

        public static readonly IReadOnlyList<(string Value, string Label)> Statuses = new List<(string, string)>
        {
            (All, "Any status"),
            ("Approved", "Approved"),
            ("PendingApproval", "Pending"),
            ("Disabled", "Disabled"),
            ("Denied", "Denied")
        };
    }

    public class AdminDashboardViewModel
    {
        public bool LoadFailed { get; set; }

        public int TotalUsers { get; set; }
        public int OperatorCount { get; set; }
        public int WorkerCount { get; set; }
        public int AdminCount { get; set; }
        public int PendingCount { get; set; }
        public int DisabledCount { get; set; }
        public int SiteCount { get; set; }
        public int StructureCount { get; set; }
        public int ActiveThisWeek { get; set; }
        public int OperatorsNotApproved { get; set; }

        public string Role { get; set; } = AdminUserFilters.All;
        public string Status { get; set; } = AdminUserFilters.All;
        public string Search { get; set; } = "";

        public List<AdminUser> Users { get; set; } = new();
        public List<AdminUser> RecentSignUps { get; set; } = new();
    }

    public class OperatorSitesGroupViewModel
    {
        public string OwnerEmail { get; set; } = "";
        public string OwnerName { get; set; } = "";
        public string OwnerRole { get; set; } = "";
        public string OwnerStatus { get; set; } = "";
        public bool CanReceiveSites { get; set; }
        public List<AdminSite> Sites { get; set; } = new();

        public int StructureCount
        {
            get { return Sites.Sum(s => s.StructureCount); }
        }

        public int WorkerCount
        {
            get { return Sites.Sum(s => s.WorkerCount); }
        }
    }

    public class AdminSitesViewModel
    {
        public bool LoadFailed { get; set; }
        public List<OperatorSitesGroupViewModel> Groups { get; set; } = new();
        public List<AdminUser> Operators { get; set; } = new();
        public int SiteCount { get; set; }
        public List<TeamMember> Workers { get; set; } = new();
        public bool WorkersLoadFailed { get; set; }
        public List<AdminDeletedSite> DeletedSites { get; set; } = new();
        public bool DeletedSitesLoadFailed { get; set; }
    }
}