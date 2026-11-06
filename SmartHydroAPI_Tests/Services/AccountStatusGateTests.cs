using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Services;
using SmartHydroAPI.Tests.TestSupport;

namespace SmartHydroAPI.Tests.Services
{
    public class AccountStatusGateTests
    {
        private const string ConfigAdminEmail = "config.admin@smarthydro.co.za";

        // Builds configuration holding one account provisioned in appsettings
        private static IConfiguration BuildConfiguration()
        {
            var settings = new Dictionary<string, string?>
            {
                ["Authentication:AllowedUsers:0:Email"] = ConfigAdminEmail,
                ["Authentication:AllowedUsers:0:Role"] = "Admin"
            };

            return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        }

        // Builds a gate over the given database with a fresh cache
        private static AccountStatusGate BuildGate(ApplicationDbContext database)
        {
            return new AccountStatusGate(database, BuildConfiguration(), new MemoryCache(new MemoryCacheOptions()));
        }

        // The cache key should ignore spaces and letter case so one account has one key
        [Fact]
        public void CacheKey_NormalisesEmail()
        {
            Assert.Equal("account-active:user@farm.co.za", AccountStatusGate.CacheKey("  User@Farm.co.za "));
        }

        // An account listed in configuration should be recognised regardless of letter case
        [Fact]
        public void IsConfigurationAccount_ListedEmail_ReturnsTrue()
        {
            var configuration = BuildConfiguration();

            Assert.True(AccountStatusGate.IsConfigurationAccount(configuration, "CONFIG.ADMIN@smarthydro.co.za"));
            Assert.False(AccountStatusGate.IsConfigurationAccount(configuration, "someone@else.co.za"));
        }

        // A missing user or a token with no email should never count as active
        [Fact]
        public async Task IsActiveAsync_NoPrincipalOrNoEmail_ReturnsFalse()
        {
            using var database = TestData.CreateDatabase();
            var gate = BuildGate(database);

            Assert.False(await gate.IsActiveAsync(null));
            Assert.False(await gate.IsActiveAsync(new ClaimsPrincipal(new ClaimsIdentity())));
        }

        // A configuration account should always be active even with no database row
        [Fact]
        public async Task IsActiveAsync_ConfigurationAccount_ReturnsTrue()
        {
            using var database = TestData.CreateDatabase();
            var gate = BuildGate(database);

            var active = await gate.IsActiveAsync(TestData.User(ConfigAdminEmail, "Admin"));

            Assert.True(active);
        }

        // Only an approved database account should be active
        [Theory]
        [InlineData(UserStatus.Approved, true)]
        [InlineData(UserStatus.Disabled, false)]
        [InlineData(UserStatus.Denied, false)]
        [InlineData(UserStatus.PendingApproval, false)]
        public async Task IsActiveAsync_DatabaseAccount_FollowsStatus(string status, bool expected)
        {
            using var database = TestData.CreateDatabase();
            database.Users.Add(new User { Email = "user@farm.co.za", Status = status });
            database.SaveChanges();
            var gate = BuildGate(database);

            var active = await gate.IsActiveAsync(TestData.User("user@farm.co.za", "Operator"));

            Assert.Equal(expected, active);
        }

        // A token for an account that was deleted from the database should not be active
        [Fact]
        public async Task IsActiveAsync_UnknownAccount_ReturnsFalse()
        {
            using var database = TestData.CreateDatabase();
            var gate = BuildGate(database);

            var active = await gate.IsActiveAsync(TestData.User("gone@farm.co.za", "Operator"));

            Assert.False(active);
        }

        // The answer should be cached, so a status change is only seen once the cache entry expires
        [Fact]
        public async Task IsActiveAsync_SecondCall_UsesCachedAnswer()
        {
            using var database = TestData.CreateDatabase();
            var user = new User { Email = "user@farm.co.za", Status = UserStatus.Approved };
            database.Users.Add(user);
            database.SaveChanges();
            var gate = BuildGate(database);
            var principal = TestData.User("user@farm.co.za", "Operator");

            var first = await gate.IsActiveAsync(principal);
            user.Status = UserStatus.Disabled;
            database.SaveChanges();
            var second = await gate.IsActiveAsync(principal);

            Assert.True(first);
            Assert.True(second);
        }
    }
}