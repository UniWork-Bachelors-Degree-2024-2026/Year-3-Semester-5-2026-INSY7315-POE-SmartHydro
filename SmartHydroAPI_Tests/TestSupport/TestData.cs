using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Tests.TestSupport
{
    public static class TestData
    {
        public static readonly Guid OwnedSiteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public static readonly Guid OtherSiteId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        public static readonly Guid DeletedSiteId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        public const string OperatorEmail = "operator@farm.co.za";
        public const string OtherOperatorEmail = "other@farm.co.za";
        public const string WorkerEmail = "worker@farm.co.za";
        public const string AdminEmail = "admin@smarthydro.co.za";

        public const int OwnedTowerId = 1;
        public const int OwnedFodderId = 2;
        public const int OtherTowerId = 3;
        public const int DeletedTowerId = 4;

        // Creates a fresh in-memory database per test so tests never share data
        public static ApplicationDbContext CreateDatabase()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            return new ApplicationDbContext(options);
        }

        // Builds a signed-in user with the given email and role, the same way the JWT middleware would
        public static ClaimsPrincipal User(string email, string role)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Role, role)
            };

            return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        }

        // Builds a signed-in operator who owns the main test site
        public static ClaimsPrincipal Operator()
        {
            return User(OperatorEmail, UserRoles.Operator);
        }

        // Builds a signed-in worker who is assigned to the main test site
        public static ClaimsPrincipal Worker()
        {
            return User(WorkerEmail, UserRoles.Worker);
        }

        // Builds a signed-in admin
        public static ClaimsPrincipal Admin()
        {
            return User(AdminEmail, UserRoles.Admin);
        }

        // Adds two operators' sites, a deleted site, their structures and a worker assigned to the first operator's site
        public static void SeedSites(ApplicationDbContext database)
        {
            database.Sites.Add(new Site { Id = OwnedSiteId, Name = "Main Farm", OwnerEmail = OperatorEmail });
            database.Sites.Add(new Site { Id = OtherSiteId, Name = "Other Farm", OwnerEmail = OtherOperatorEmail });
            database.Sites.Add(new Site { Id = DeletedSiteId, Name = "Old Farm", OwnerEmail = OperatorEmail, IsDeleted = true });

            database.Structures.Add(new Structure { Id = OwnedTowerId, SiteId = OwnedSiteId, Name = "Tower A", StructureType = "VERTICAL_TOWER" });
            database.Structures.Add(new Structure { Id = OwnedFodderId, SiteId = OwnedSiteId, Name = "Fodder A", StructureType = "FODDER_TRAY" });
            database.Structures.Add(new Structure { Id = OtherTowerId, SiteId = OtherSiteId, Name = "Tower B", StructureType = "VERTICAL_TOWER" });
            database.Structures.Add(new Structure { Id = DeletedTowerId, SiteId = OwnedSiteId, Name = "Removed Tower", StructureType = "VERTICAL_TOWER", IsDeleted = true });

            database.Users.Add(new User { Id = 10, Email = WorkerEmail, Role = UserRoles.Worker, Status = UserStatus.Approved });
            database.SiteUsers.Add(new SiteUser { SiteId = OwnedSiteId, UserId = 10 });

            database.SaveChanges();
        }
    }
}