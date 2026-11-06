using System.Security.Claims;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Services;
using SmartHydroAPI.Tests.TestSupport;

namespace SmartHydroAPI.Tests.Services
{
    public class SiteAccessTests
    {
        // The email should come back trimmed and lower-cased, or empty when the token has none
        [Fact]
        public void NormalizedEmail_TrimsAndLowerCases()
        {
            var user = TestData.User("  Operator@Farm.CO.ZA ", "Operator");
            var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

            Assert.Equal("operator@farm.co.za", SiteAccess.NormalizedEmail(user));
            Assert.Equal(string.Empty, SiteAccess.NormalizedEmail(anonymous));
        }

        // An admin should see every active site but not deleted ones
        [Fact]
        public void VisibleSiteIds_Admin_SeesAllActiveSites()
        {
            using var database = TestData.CreateDatabase();
            TestData.SeedSites(database);

            var ids = SiteAccess.VisibleSiteIds(database, TestData.Admin()).ToList();

            Assert.Contains(TestData.OwnedSiteId, ids);
            Assert.Contains(TestData.OtherSiteId, ids);
            Assert.DoesNotContain(TestData.DeletedSiteId, ids);
        }

        // An operator should only see the active sites they own
        [Fact]
        public void VisibleSiteIds_Operator_SeesOnlyOwnSites()
        {
            using var database = TestData.CreateDatabase();
            TestData.SeedSites(database);

            var ids = SiteAccess.VisibleSiteIds(database, TestData.Operator()).ToList();

            Assert.Equal(new[] { TestData.OwnedSiteId }, ids);
        }

        // Site ownership should match the email regardless of letter case
        [Fact]
        public void VisibleSiteIds_OperatorEmailInCapitals_StillMatches()
        {
            using var database = TestData.CreateDatabase();
            TestData.SeedSites(database);
            var user = TestData.User("OPERATOR@FARM.CO.ZA", "Operator");

            var ids = SiteAccess.VisibleSiteIds(database, user).ToList();

            Assert.Equal(new[] { TestData.OwnedSiteId }, ids);
        }

        // A worker should only see the sites they have been assigned to
        [Fact]
        public void VisibleSiteIds_Worker_SeesOnlyAssignedSites()
        {
            using var database = TestData.CreateDatabase();
            TestData.SeedSites(database);

            var ids = SiteAccess.VisibleSiteIds(database, TestData.Worker()).ToList();

            Assert.Equal(new[] { TestData.OwnedSiteId }, ids);
        }

        // A worker with no assignments should see nothing
        [Fact]
        public void VisibleSiteIds_UnassignedWorker_SeesNothing()
        {
            using var database = TestData.CreateDatabase();
            TestData.SeedSites(database);
            var user = TestData.User("new.worker@farm.co.za", "Worker");

            var ids = SiteAccess.VisibleSiteIds(database, user).ToList();

            Assert.Empty(ids);
        }

        // A worker can view a site but must never be able to change one
        [Fact]
        public void ManageableSiteIds_Worker_ReturnsNothing()
        {
            using var database = TestData.CreateDatabase();
            TestData.SeedSites(database);

            var ids = SiteAccess.ManageableSiteIds(database, TestData.Worker()).ToList();

            Assert.Empty(ids);
        }

        // An operator should only be able to change their own sites
        [Fact]
        public void ManageableSiteIds_Operator_ReturnsOwnSites()
        {
            using var database = TestData.CreateDatabase();
            TestData.SeedSites(database);

            var ids = SiteAccess.ManageableSiteIds(database, TestData.Operator()).ToList();

            Assert.Equal(new[] { TestData.OwnedSiteId }, ids);
        }

        // An operator should only be able to restore deleted sites they owned
        [Fact]
        public void ManageableDeletedSites_Operator_ReturnsOwnDeletedSites()
        {
            using var database = TestData.CreateDatabase();
            TestData.SeedSites(database);

            var sites = SiteAccess.ManageableDeletedSites(database, TestData.Operator()).ToList();

            var site = Assert.Single(sites);
            Assert.Equal(TestData.DeletedSiteId, site.Id);
        }

        // An operator should never see structures that belong to another operator's site
        [Fact]
        public void VisibleStructures_Operator_ExcludesOtherSites()
        {
            using var database = TestData.CreateDatabase();
            TestData.SeedSites(database);

            var ids = SiteAccess.VisibleStructures(database, TestData.Operator()).Select(s => s.Id).ToList();

            Assert.Contains(TestData.OwnedTowerId, ids);
            Assert.DoesNotContain(TestData.OtherTowerId, ids);
        }

        // Visible structure ids should leave out deleted structures
        [Fact]
        public void VisibleStructureIds_ExcludesDeletedStructures()
        {
            using var database = TestData.CreateDatabase();
            TestData.SeedSites(database);

            var ids = SiteAccess.VisibleStructureIds(database, TestData.Operator()).ToList();

            Assert.DoesNotContain(TestData.DeletedTowerId, ids);
        }

        // A worker should be able to see the structures of their site but change none of them
        [Fact]
        public void ManageableStructures_Worker_ReturnsNothing()
        {
            using var database = TestData.CreateDatabase();
            TestData.SeedSites(database);

            var visible = SiteAccess.VisibleStructures(database, TestData.Worker()).ToList();
            var manageable = SiteAccess.ManageableStructures(database, TestData.Worker()).ToList();

            Assert.NotEmpty(visible);
            Assert.Empty(manageable);
        }

        // A structure not yet placed on a site should be manageable by the operator whose email owns it
        [Fact]
        public void ManageableStructures_OperatorOwnsUnassignedStructure_IsIncluded()
        {
            using var database = TestData.CreateDatabase();
            TestData.SeedSites(database);
            database.Structures.Add(new Structure { Id = 50, Name = "Loose", OwnerEmail = "Operator@Farm.co.za" });
            database.SaveChanges();

            var ids = SiteAccess.ManageableStructureIds(database, TestData.Operator()).ToList();

            Assert.Contains(50, ids);
        }
    }
}