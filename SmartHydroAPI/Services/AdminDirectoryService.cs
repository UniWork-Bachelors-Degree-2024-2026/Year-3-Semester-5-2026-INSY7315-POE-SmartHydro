using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Services
{
    public enum AssignOwnerOutcome
    {
        Assigned,
        SiteNotFound,
        OwnerNotEligible
    }

    public enum AccountActionOutcome
    {
        Done,
        NotFound,
        NotAllowed,
        WrongState,
        Conflict,
        Invalid
    }

    public sealed record AccountActionResult(AccountActionOutcome Outcome, string Message, string? Status, string? Field = null);

    public class AdminDirectoryService
    {
        private readonly ApplicationDbContext _database;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;

        public AdminDirectoryService(ApplicationDbContext database, IConfiguration configuration, IMemoryCache cache)
        {
            _database = database;
            _configuration = configuration;
            _cache = cache;
        }

        // Lists every account with its role, status, owned site count and the sites a worker has been added to
        public async Task<List<AdminUserDto>> GetUsersAsync(string adminEmail, CancellationToken ct = default)
        {
            var users = await _database.Users
                .AsNoTracking()
                .OrderBy(u => u.Role)
                .ThenBy(u => u.Email)
                .ToListAsync(ct);

            var ownedCounts = await _database.Sites
                .AsNoTracking()
                .Where(s => !s.IsDeleted && s.OwnerEmail != null)
                .GroupBy(s => s.OwnerEmail!.ToLower())
                .Select(g => new { Email = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Email, x => x.Count, ct);

            var assignments = await _database.SiteUsers
                .AsNoTracking()
                .Join(
                    _database.Sites.Where(s => !s.IsDeleted),
                    su => su.SiteId,
                    s => s.Id,
                    (su, s) => new { su.UserId, s.Name })
                .ToListAsync(ct);

            var result = new List<AdminUserDto>();

            foreach (var user in users)
            {
                ownedCounts.TryGetValue(user.Email.ToLowerInvariant(), out var ownedCount);
                var isProtected = IsProtected(user, adminEmail);

                result.Add(new AdminUserDto
                {
                    Id = user.Id,
                    Email = user.Email,
                    FullName = user.FullName,
                    FarmName = user.FarmName,
                    Role = user.Role,
                    Status = user.Status,
                    IsVerified = user.IsVerified,
                    CreatedAt = user.CreatedAt,
                    LastLoginAt = user.LastLoginAt,
                    OwnedSiteCount = ownedCount,
                    IsProtected = isProtected,
                    IsCurrentUser = string.Equals(user.Email, adminEmail, StringComparison.OrdinalIgnoreCase),
                    AllowedActions = UserStatusRules.AllowedActions(user.Status, isProtected).ToList(),
                    StatusOptions = UserStatusRules.StatusOptions(user.Status, isProtected)
                        .Select(o => new StatusOptionDto { Status = o.Status, Action = o.Action })
                        .ToList(),
                    AssignedSites = assignments
                        .Where(a => a.UserId == user.Id)
                        .Select(a => a.Name)
                        .OrderBy(n => n)
                        .ToList()
                });
            }

            return result;
        }

        // Lists every active site with its owner, structure count and worker count
        public async Task<List<AdminSiteDto>> GetSitesAsync(CancellationToken ct = default)
        {
            return await _database.Sites
                .AsNoTracking()
                .Where(s => !s.IsDeleted)
                .OrderBy(s => s.OwnerEmail)
                .ThenBy(s => s.Name)
                .Select(s => new AdminSiteDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    OwnerEmail = s.OwnerEmail,
                    CreatedAt = s.CreatedAt,
                    StructureCount = s.Structures.Count(st => !st.IsDeleted),
                    WorkerCount = _database.SiteUsers.Count(su => su.SiteId == s.Id)
                })
                .ToListAsync(ct);
        }

        // Gives a site to an approved operator, moving its structures across so the new owner can manage them
        public async Task<AssignOwnerOutcome> AssignOwnerAsync(Guid siteId, string ownerEmail, CancellationToken ct = default)
        {
            var email = ownerEmail.Trim().ToLowerInvariant();

            var site = await _database.Sites.FirstOrDefaultAsync(s => s.Id == siteId && !s.IsDeleted, ct);

            if (site is null)
                return AssignOwnerOutcome.SiteNotFound;

            var eligible = await _database.Users.AnyAsync(
                u => u.Email == email && u.Role == UserRoles.Operator && u.Status == UserStatus.Approved,
                ct);

            if (!eligible)
                return AssignOwnerOutcome.OwnerNotEligible;

            site.OwnerEmail = email;

            await _database.Structures
                .Where(s => s.SiteId == siteId)
                .ExecuteUpdateAsync(s => s.SetProperty(st => st.OwnerEmail, email), ct);

            await _database.SaveChangesAsync(ct);
            return AssignOwnerOutcome.Assigned;
        }

        // Disables an approved account so it can no longer sign in or use an existing session; all of its data is kept
        public Task<AccountActionResult> DeactivateAsync(int id, string adminEmail, CancellationToken ct = default)
        {
            return ChangeStatusAsync(id, adminEmail, AccountActions.Deactivate, "Account disabled. The user is signed out and can no longer sign in.", ct);
        }

        // Re-enables a disabled account so it can sign in again
        public Task<AccountActionResult> ReactivateAsync(int id, string adminEmail, CancellationToken ct = default)
        {
            return ChangeStatusAsync(id, adminEmail, AccountActions.Reactivate, "Account re-enabled. The user can sign in again.", ct);
        }

        // Moves a denied account back to pending approval so it can be reviewed again
        public Task<AccountActionResult> ReopenAsync(int id, string adminEmail, CancellationToken ct = default)
        {
            return ChangeStatusAsync(id, adminEmail, AccountActions.Reopen, "Account moved back to pending approval.", ct);
        }

        // Updates an account's name, starting site name and role
        public async Task<AccountActionResult> UpdateUserAsync(int id, UpdateUserDto dto, string adminEmail, CancellationToken ct = default)
        {
            User? user = await _database.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

            if (user is null)
                return new AccountActionResult(AccountActionOutcome.NotFound, "That account could not be found.", null);

            var email = user.Email.Trim().ToLowerInvariant();
            var newRole = dto.Role.Trim();
            var roleChanged = !string.Equals(newRole, user.Role, StringComparison.OrdinalIgnoreCase);

            if (roleChanged && string.Equals(user.Email, adminEmail, StringComparison.OrdinalIgnoreCase))
                return new AccountActionResult(AccountActionOutcome.NotAllowed, "You can't change your own role. Ask another admin to do it.", user.Status);

            if (IsProtected(user, adminEmail) && roleChanged)
                return new AccountActionResult(AccountActionOutcome.NotAllowed, "Only the name can be changed on a built-in administrator account.", user.Status);

            var roleRefusal = await CheckRoleChangeAsync(user, newRole, email, ct);
            if (roleRefusal is not null)
                return roleRefusal;

            var resultingRole = user.Role;
            if (roleChanged)
                resultingRole = MatchRole(newRole);

            var awaitingReview = user.Status == UserStatus.PendingApproval || user.Status == UserStatus.Denied;
            if (awaitingReview && resultingRole == UserRoles.Operator && string.IsNullOrWhiteSpace(dto.FarmName))
                return new AccountActionResult(AccountActionOutcome.Invalid, "Enter a starting site name. It becomes their first site when they are approved.", user.Status, "farmName");

            user.FullName = CleanOptional(dto.FullName);
            user.FarmName = CleanOptional(dto.FarmName);
            user.Role = resultingRole;

            await _database.SaveChangesAsync(ct);

            if (!roleChanged)
                return new AccountActionResult(AccountActionOutcome.Done, "Account details saved.", user.Status);

            ForgetSession(email);
            return new AccountActionResult(AccountActionOutcome.Done, "Account details saved. The user will need to sign in again.", user.Status);
        }

        // Checks that a new role is one an admin may give, that only approved accounts are made admins and that an operator giving up the role owns no sites
        private async Task<AccountActionResult?> CheckRoleChangeAsync(User user, string newRole, string email, CancellationToken ct)
        {
            if (string.Equals(newRole, user.Role, StringComparison.OrdinalIgnoreCase))
                return null;

            if (!IsKnownRole(newRole))
                return new AccountActionResult(AccountActionOutcome.Invalid, "Choose Admin, Operator or Worker.", user.Status, "role");

            // An admin can see and change everything, so the account must already be approved and able to sign in
            if (MatchRole(newRole) == UserRoles.Admin && user.Status != UserStatus.Approved)
                return new AccountActionResult(AccountActionOutcome.Invalid, "Only approved accounts can be made admins. Approve or re-enable this account first.", user.Status, "role");

            if (user.Role != UserRoles.Operator)
                return null;

            var ownedSites = await _database.Sites.CountAsync(s => !s.IsDeleted && s.OwnerEmail != null && s.OwnerEmail.ToLower() == email, ct);

            if (ownedSites > 0)
                return new AccountActionResult(AccountActionOutcome.Conflict, $"This operator owns {ownedSites} site(s). Move them to another operator on Sites by Operator before changing their role.", user.Status, "role");

            return null;
        }

        // True for the three roles an admin can give
        private static bool IsKnownRole(string role)
        {
            return string.Equals(role, UserRoles.Admin, StringComparison.OrdinalIgnoreCase)
                || string.Equals(role, UserRoles.Operator, StringComparison.OrdinalIgnoreCase)
                || string.Equals(role, UserRoles.Worker, StringComparison.OrdinalIgnoreCase);
        }

        // Returns the stored spelling of an admin, operator or worker role
        private static string MatchRole(string role)
        {
            if (string.Equals(role, UserRoles.Admin, StringComparison.OrdinalIgnoreCase))
                return UserRoles.Admin;

            if (string.Equals(role, UserRoles.Worker, StringComparison.OrdinalIgnoreCase))
                return UserRoles.Worker;

            return UserRoles.Operator;
        }

        // Trims an optional text value, storing null when it is left blank
        private static string? CleanOptional(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return value.Trim();
        }

        // Applies a status change after checking the account exists, is not protected, and allows that action in its current status
        private async Task<AccountActionResult> ChangeStatusAsync(int id, string adminEmail, string action, string successMessage, CancellationToken ct)
        {
            User? user = await _database.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

            var refusal = CheckAction(user, adminEmail, action);
            if (refusal is not null)
                return refusal;

            user!.Status = UserStatusRules.NextStatus(action)!;
            await _database.SaveChangesAsync(ct);

            ForgetSession(user.Email);
            return new AccountActionResult(AccountActionOutcome.Done, successMessage, user.Status);
        }

        // The database retries on transient failures, so the whole transaction runs as one unit the retry can repeat
        public Task<AccountActionResult> DeleteAsync(int id, string adminEmail, CancellationToken ct = default)
        {
            var strategy = _database.Database.CreateExecutionStrategy();
            return strategy.ExecuteAsync(() => DeleteInTransactionAsync(id, adminEmail, ct));
        }

        // Removes an account and its sign-in records, soft-deleting the sites and structures it owns so their data can still be recovered
        private async Task<AccountActionResult> DeleteInTransactionAsync(int id, string adminEmail, CancellationToken ct)
        {
            User? user = await _database.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

            var refusal = CheckAction(user, adminEmail, AccountActions.Delete);
            if (refusal is not null)
                return refusal;

            var email = user!.Email.Trim().ToLowerInvariant();
            var now = DateTime.UtcNow;

            await using var transaction = await _database.Database.BeginTransactionAsync(ct);

            var ownedSiteIds = await _database.Sites
                .Where(s => s.OwnerEmail != null && s.OwnerEmail.ToLower() == email)
                .Select(s => s.Id)
                .ToListAsync(ct);

            // The thresholds of the structures being deleted go with them
            await _database.Thresholds
                .Where(t => _database.Structures.Any(s => s.Id == t.StructureId && !s.IsDeleted
                    && ((s.SiteId != null && ownedSiteIds.Contains(s.SiteId.Value))
                        || (s.OwnerEmail != null && s.OwnerEmail.ToLower() == email))))
                .ExecuteDeleteAsync(ct);

            var structuresRemoved = await _database.Structures
                .Where(s => !s.IsDeleted
                    && ((s.SiteId != null && ownedSiteIds.Contains(s.SiteId.Value))
                        || (s.OwnerEmail != null && s.OwnerEmail.ToLower() == email)))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(st => st.IsDeleted, true)
                    .SetProperty(st => st.DeletedAt, now)
                    .SetProperty(st => st.DeletedByEmail, adminEmail), ct);

            var sitesRemoved = await _database.Sites
                .Where(s => ownedSiteIds.Contains(s.Id) && !s.IsDeleted)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(site => site.IsDeleted, true)
                    .SetProperty(site => site.DeletedAt, now)
                    .SetProperty(site => site.DeletedByEmail, adminEmail), ct);

            await _database.SiteInvites
                .Where(i => i.CreatedByEmail.ToLower() == email || ownedSiteIds.Contains(i.SiteId))
                .ExecuteDeleteAsync(ct);
            await _database.OtpCodes.Where(o => o.Email == email).ExecuteDeleteAsync(ct);
            await _database.PasswordResetCodes.Where(r => r.Email == email).ExecuteDeleteAsync(ct);
            await _database.PasswordHistory.Where(h => h.Email == email).ExecuteDeleteAsync(ct);
            await _database.LoginAttempts.Where(l => l.Email == email).ExecuteDeleteAsync(ct);
            await _database.SiteUsers.Where(su => su.UserId == user.Id).ExecuteDeleteAsync(ct);

            _database.Users.Remove(user);
            await _database.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            ForgetSession(email);

            var message = "Account deleted.";
            if (sitesRemoved > 0 || structuresRemoved > 0)
                message = $"Account deleted. {sitesRemoved} site(s) and {structuresRemoved} structure(s) it owned were moved to deleted items.";

            return new AccountActionResult(AccountActionOutcome.Done, message, null);
        }

        // Returns why an action cannot be applied to an account, or null when it can
        private AccountActionResult? CheckAction(User? user, string adminEmail, string action)
        {
            if (user is null)
                return new AccountActionResult(AccountActionOutcome.NotFound, "That account could not be found.", null);

            if (IsProtected(user, adminEmail))
                return new AccountActionResult(AccountActionOutcome.NotAllowed, "You can't change your own account or a built-in administrator account.", user.Status);

            if (!UserStatusRules.IsAllowed(user.Status, action))
                return new AccountActionResult(AccountActionOutcome.WrongState, UserStatusRules.DescribeRefusal(user.Status, action), user.Status);

            return null;
        }

        // Stops an admin from locking themselves out and keeps configuration-provisioned accounts permanent
        private bool IsProtected(User user, string adminEmail)
        {
            if (string.Equals(user.Email, adminEmail, StringComparison.OrdinalIgnoreCase))
                return true;

            return AccountStatusGate.IsConfigurationAccount(_configuration, user.Email);
        }

        // Clears the cached account status so the change applies to the user's next request
        private void ForgetSession(string email)
        {
            _cache.Remove(AccountStatusGate.CacheKey(email));
        }
    }
}