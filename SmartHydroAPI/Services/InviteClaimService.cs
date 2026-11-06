using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Services
{
    public enum InviteSecretKind
    {
        WebToken,
        AccessCode
    }

    public enum InviteClaimOutcome
    {
        Claimed,
        Invalid,
        AccountExists
    }

    public sealed record InviteClaimResult(InviteClaimOutcome Outcome, AuthResponseDto? Auth)
    {
        // Builds the result returned once the worker account exists and is linked to the site
        public static InviteClaimResult Claimed(AuthResponseDto auth) => new(InviteClaimOutcome.Claimed, auth);

        // Builds the result returned for a wrong, expired or exhausted invitation
        public static InviteClaimResult Invalid() => new(InviteClaimOutcome.Invalid, null);

        // Builds the result returned when the email already belongs to an account the invitation cannot take over
        public static InviteClaimResult AccountExists() => new(InviteClaimOutcome.AccountExists, null);
    }

    public sealed class InviteClaimService
    {
        public const string RateLimitPolicy = "ClaimInviteRateLimit";

        private const string DummyHash =
            "PBKDF2-SHA256$100000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

        private readonly ApplicationDbContext _database;
        private readonly SiteInvitationService _invitations;
        private readonly AuthenticationService _authentication;
        private readonly IConfiguration _configuration;
        private readonly ILogger<InviteClaimService> _logger;

        public InviteClaimService(
            ApplicationDbContext database,
            SiteInvitationService invitations,
            AuthenticationService authentication,
            IConfiguration configuration,
            ILogger<InviteClaimService> logger)
        {
            _database = database;
            _invitations = invitations;
            _authentication = authentication;
            _configuration = configuration;
            _logger = logger;
        }

        // Claims an invitation with the token from the emailed link (Path A, web browser)
        public Task<InviteClaimResult> ClaimWithTokenAsync(string email, string token, string newPassword, string? fullName, CancellationToken ct = default)
        {
            return ClaimAsync(email, token.Trim(), InviteSecretKind.WebToken, newPassword, fullName, ct);
        }

        // Claims an invitation with the 8-character access code typed into the Android app (Path B)
        public Task<InviteClaimResult> ClaimWithCodeAsync(string email, string code, string newPassword, string? fullName, CancellationToken ct = default)
        {
            return ClaimAsync(email, SiteInvitationService.NormaliseCode(code), InviteSecretKind.AccessCode, newPassword, fullName, ct);
        }

        // The database retries on transient failures, so the whole transaction runs as one unit the retry can repeat
        private Task<InviteClaimResult> ClaimAsync(
            string email,
            string secret,
            InviteSecretKind kind,
            string newPassword,
            string? fullName,
            CancellationToken ct)
        {
            var strategy = _database.Database.CreateExecutionStrategy();
            return strategy.ExecuteAsync(() => ClaimInTransactionAsync(email, secret, kind, newPassword, fullName, ct));
        }

        // Verifies the secret against the caller's live invitations in constant time, then either creates the Worker account or records a failed attempt
        private async Task<InviteClaimResult> ClaimInTransactionAsync(
            string email,
            string secret,
            InviteSecretKind kind,
            string newPassword,
            string? fullName,
            CancellationToken ct)
        {
            email = email.Trim().ToLowerInvariant();
            var now = DateTime.UtcNow;

            await using var transaction = await _database.Database.BeginTransactionAsync(ct);

            var invites = await _database.SiteInvites
                .FromSqlInterpolated($"SELECT * FROM site_invites WHERE email = {email} FOR UPDATE")
                .ToListAsync(ct);

            var liveInvites = new List<SiteInvite>();

            foreach (var invite in invites)
            {
                if (invite.ExpiresAt <= now || invite.Attempts >= SiteInvitationService.MaxAttempts)
                {
                    _database.SiteInvites.Remove(invite);
                }
                else
                {
                    liveInvites.Add(invite);
                }
            }

            if (liveInvites.Count == 0)
            {
                _invitations.VerifySecret(secret, DummyHash);
                await _database.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return InviteClaimResult.Invalid();
            }

            var matched = FindMatchingInvite(liveInvites, secret, kind);

            if (matched is null)
            {
                RecordFailedAttempt(liveInvites);
                await _database.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return InviteClaimResult.Invalid();
            }

            var siteActive = await _database.Sites.AnyAsync(s => s.Id == matched.SiteId && !s.IsDeleted, ct);

            if (!siteActive)
            {
                _database.SiteInvites.Remove(matched);
                await _database.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return InviteClaimResult.Invalid();
            }

            var worker = await ResolveWorkerAsync(email, newPassword, fullName, now, ct);

            if (worker is null)
            {
                await transaction.RollbackAsync(ct);
                return InviteClaimResult.AccountExists();
            }

            try
            {
                await _database.SaveChangesAsync(ct);

                var alreadyLinked = await _database.SiteUsers
                    .AnyAsync(su => su.SiteId == matched.SiteId && su.UserId == worker.Id, ct);

                if (!alreadyLinked)
                {
                    _database.SiteUsers.Add(new SiteUser
                    {
                        SiteId = matched.SiteId,
                        UserId = worker.Id,
                        CreatedAt = now
                    });
                }

                _database.SiteInvites.Remove(matched);
                await _database.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogWarning(ex, "Claiming the invitation for {Email} conflicted with an existing account.", email);
                await transaction.RollbackAsync(ct);
                return InviteClaimResult.AccountExists();
            }

            return InviteClaimResult.Claimed(_authentication.IssueToken(worker));
        }

        // Returns the invitation whose token or code hash matches the supplied secret, checking every candidate so timing does not reveal which one matched
        private SiteInvite? FindMatchingInvite(List<SiteInvite> invites, string secret, InviteSecretKind kind)
        {
            SiteInvite? matched = null;

            foreach (var invite in invites)
            {
                var storedHash = invite.CodeHash;

                if (kind == InviteSecretKind.WebToken)
                    storedHash = invite.TokenHash;

                if (_invitations.VerifySecret(secret, storedHash) && matched is null)
                    matched = invite;
            }

            return matched;
        }

        // Counts a wrong token or code against every live invitation for the email and deletes any that reach the attempt limit
        private void RecordFailedAttempt(List<SiteInvite> invites)
        {
            foreach (var invite in invites)
            {
                invite.Attempts++;

                if (invite.Attempts >= SiteInvitationService.MaxAttempts)
                    _database.SiteInvites.Remove(invite);
            }
        }

        // Creates the Worker account with the role forced server side, or reuses an existing Worker whose current password was supplied; returns null when the email cannot be claimed
        private async Task<User?> ResolveWorkerAsync(string email, string newPassword, string? fullName, DateTime now, CancellationToken ct)
        {
            if (IsConfigurationAccount(email))
                return null;

            var existing = await _database.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
            var trimmedName = (fullName ?? string.Empty).Trim();

            if (existing is not null)
            {
                if (existing.Role != UserRoles.Worker || existing.Status != UserStatus.Approved)
                    return null;

                if (string.IsNullOrWhiteSpace(existing.PasswordHash) ||
                    !AuthenticationService.VerifyPassword(newPassword, existing.PasswordHash))
                {
                    return null;
                }

                if (string.IsNullOrWhiteSpace(existing.FullName) && trimmedName.Length > 0)
                    existing.FullName = trimmedName;

                existing.IsVerified = true;
                existing.LastLoginAt = now;
                return existing;
            }

            var worker = new User
            {
                Email = email,
                Role = UserRoles.Worker,
                PasswordHash = AuthenticationService.HashPassword(newPassword),
                Status = UserStatus.Approved,
                IsVerified = true,
                CreatedAt = now,
                LastLoginAt = now
            };

            if (trimmedName.Length > 0)
                worker.FullName = trimmedName;

            _database.Users.Add(worker);
            return worker;
        }

        // Checks whether the email is one of the accounts provisioned in configuration, which an invitation must never take over
        private bool IsConfigurationAccount(string email)
        {
            return _configuration.GetSection("Authentication:AllowedUsers").GetChildren()
                .Any(section => string.Equals(section["Email"], email, StringComparison.OrdinalIgnoreCase));
        }
    }
}