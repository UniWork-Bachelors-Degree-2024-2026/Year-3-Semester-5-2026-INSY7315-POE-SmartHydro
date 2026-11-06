using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Services
{
    public class AccountStatusGate
    {
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

        private readonly ApplicationDbContext _database;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;

        public AccountStatusGate(ApplicationDbContext database, IConfiguration configuration, IMemoryCache cache)
        {
            _database = database;
            _configuration = configuration;
            _cache = cache;
        }

        // Builds the cache key that holds whether an email's account is still active
        public static string CacheKey(string email)
        {
            return "account-active:" + email.Trim().ToLowerInvariant();
        }

        // Checks whether an email belongs to an account provisioned in configuration, which can never be disabled or deleted
        public static bool IsConfigurationAccount(IConfiguration configuration, string email)
        {
            return configuration.GetSection("Authentication:AllowedUsers").GetChildren()
                .Any(section => string.Equals(section["Email"], email, StringComparison.OrdinalIgnoreCase));
        }

        // Decides whether a signed-in token still belongs to an active account, so a disabled or deleted user is locked out straight away
        public async Task<bool> IsActiveAsync(ClaimsPrincipal? principal, CancellationToken ct = default)
        {
            if (principal is null)
                return false;

            var email = SiteAccess.NormalizedEmail(principal);

            if (email.Length == 0)
                return false;

            if (IsConfigurationAccount(_configuration, email))
                return true;

            var key = CacheKey(email);

            if (_cache.TryGetValue(key, out bool cachedActive))
                return cachedActive;

            var status = await _database.Users
                .AsNoTracking()
                .Where(u => u.Email == email)
                .Select(u => u.Status)
                .FirstOrDefaultAsync(ct);

            var active = status is not null && UserStatusRules.CanSignIn(status);
            _cache.Set(key, active, CacheLifetime);
            return active;
        }
    }
}