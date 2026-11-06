using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Services
{
    public enum SiteInviteOutcome
    {
        Sent,
        NotAuthorized,
        EmailFailed
    }

    public sealed record SiteInviteResult(SiteInviteOutcome Outcome, DateTime? ExpiresAt)
    {
        // Builds the result returned once the invitation has been stored and emailed
        public static SiteInviteResult Sent(DateTime expiresAt) => new(SiteInviteOutcome.Sent, expiresAt);

        // Builds the result returned when the caller is not the verified operator who owns the site
        public static SiteInviteResult NotAuthorized() => new(SiteInviteOutcome.NotAuthorized, null);

        // Builds the result returned when the invitation email could not be delivered
        public static SiteInviteResult EmailFailed() => new(SiteInviteOutcome.EmailFailed, null);
    }

    public enum TeamActionOutcome
    {
        Done,
        NotAuthorized,
        NotFound
    }

    public sealed record TeamActionResult(TeamActionOutcome Outcome, string Email, string SiteName)
    {
        // Builds the result returned once the worker or invitation has been taken off the site
        public static TeamActionResult Done(string email, string siteName) => new(TeamActionOutcome.Done, email, siteName);

        // Builds the result returned when the caller is not the approved operator who owns the site
        public static TeamActionResult NotAuthorized() => new(TeamActionOutcome.NotAuthorized, string.Empty, string.Empty);

        // Builds the result returned when there is no such worker or invitation on the site
        public static TeamActionResult NotFound() => new(TeamActionOutcome.NotFound, string.Empty, string.Empty);
    }

    public static class TeamStatus
    {
        public const string Joined = "Joined";
        public const string Invited = "Invited";
        public const string Expired = "Expired";
    }

    public sealed class SiteInvitationService
    {
        public const string RateLimitPolicy = "WorkerInviteRateLimit";
        public const int MaxInvitesPerWindow = 5;
        public static readonly TimeSpan InviteWindow = TimeSpan.FromMinutes(10);

        public const int MaxAttempts = 3;
        public static readonly TimeSpan InvitationLifetime = TimeSpan.FromHours(24);
        public static readonly TimeSpan ExpiredInviteShownFor = TimeSpan.FromDays(14);

        private const string HashPrefix = "PBKDF2-SHA256";
        private const string CodeAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        private const int TokenBytes = 32;
        private const int CodeLength = 8;
        private const int HashIterations = 100_000;
        private const int HashSize = 32;
        private const int SaltSize = 16;

        private readonly ApplicationDbContext _database;
        private readonly IEmailSender _emailSender;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SiteInvitationService> _logger;

        public SiteInvitationService(
            ApplicationDbContext database,
            IEmailSender emailSender,
            IConfiguration configuration,
            ILogger<SiteInvitationService> logger)
        {
            _database = database;
            _emailSender = emailSender;
            _configuration = configuration;
            _logger = logger;
        }

        // Verifies the caller owns the site, upserts a hashed 24-hour invitation and emails the worker a secure link and an access code
        public async Task<SiteInviteResult> CreateAsync(
            Guid siteId,
            string workerEmail,
            string operatorEmail,
            CancellationToken ct = default)
        {
            workerEmail = workerEmail.Trim().ToLowerInvariant();
            operatorEmail = operatorEmail.Trim().ToLowerInvariant();

            var site = await FindOwnedSiteAsync(siteId, operatorEmail, ct);

            if (site is null)
                return SiteInviteResult.NotAuthorized();

            var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(TokenBytes));
            var rawCode = GenerateAccessCode();
            var tokenHash = HashSecret(token);
            var codeHash = HashSecret(rawCode);
            var now = DateTime.UtcNow;
            var expiresAt = now.Add(InvitationLifetime);

            await _database.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO site_invites (site_id, email, token_hash, code_hash, expires_at, attempts, created_by_email, created_at)
                VALUES ({siteId}, {workerEmail}, {tokenHash}, {codeHash}, {expiresAt}, 0, {operatorEmail}, {now})
                ON CONFLICT (site_id, email) DO UPDATE
                SET token_hash = EXCLUDED.token_hash,
                    code_hash = EXCLUDED.code_hash,
                    expires_at = EXCLUDED.expires_at,
                    attempts = 0,
                    created_by_email = EXCLUDED.created_by_email,
                    created_at = EXCLUDED.created_at
                """, ct);

            try
            {
                await _emailSender.SendWorkerInvitationEmailAsync(
                    workerEmail,
                    site.Name,
                    FormatCode(rawCode),
                    BuildClaimUrl(token, workerEmail));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The invitation for {WorkerEmail} to site {SiteId} could not be emailed.", workerEmail, siteId);

                await _database.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM site_invites
                    WHERE site_id = {siteId} AND email = {workerEmail} AND token_hash = {tokenHash}
                    """, ct);

                return SiteInviteResult.EmailFailed();
            }

            return SiteInviteResult.Sent(expiresAt);
        }

        // Returns the site only when it exists, is active, and is owned by an approved Operator matching the caller
        // Lists the workers who have joined the operator's sites and the invitations still open or recently expired, newest first
        public async Task<List<TeamMemberDto>> GetTeamAsync(string operatorEmail, CancellationToken ct = default)
        {
            operatorEmail = operatorEmail.Trim().ToLowerInvariant();

            var sites = await _database.Sites
                .AsNoTracking()
                .Where(s => !s.IsDeleted && s.OwnerEmail != null && s.OwnerEmail.ToLower() == operatorEmail)
                .Select(s => new { s.Id, s.Name })
                .ToListAsync(ct);

            return await BuildTeamAsync(sites.ToDictionary(s => s.Id, s => s.Name), ct);
        }

        // Lists the workers and open invitations on every active site, or on one active site, so an Admin can see who has access
        public async Task<List<TeamMemberDto>> GetAdminTeamAsync(Guid? siteId, CancellationToken ct = default)
        {
            var query = _database.Sites.AsNoTracking().Where(s => !s.IsDeleted);

            if (siteId.HasValue)
                query = query.Where(s => s.Id == siteId.Value);

            var sites = await query
                .Select(s => new { s.Id, s.Name })
                .ToListAsync(ct);

            return await BuildTeamAsync(sites.ToDictionary(s => s.Id, s => s.Name), ct);
        }

        // Builds the team list for the given sites: the workers who joined, then the invitations still open or recently expired, newest first
        private async Task<List<TeamMemberDto>> BuildTeamAsync(Dictionary<Guid, string> siteNames, CancellationToken ct)
        {
            var siteIds = siteNames.Keys.ToList();

            var joined = await _database.SiteUsers
                .AsNoTracking()
                .Where(su => siteIds.Contains(su.SiteId))
                .Join(_database.Users, su => su.UserId, u => u.Id, (su, u) => new { su.SiteId, su.UserId, u.Email, u.FullName, su.CreatedAt })
                .ToListAsync(ct);

            var shownSince = DateTime.UtcNow - ExpiredInviteShownFor;

            var invites = await _database.SiteInvites
                .AsNoTracking()
                .Where(i => siteIds.Contains(i.SiteId) && i.CreatedAt >= shownSince)
                .Select(i => new { i.SiteId, i.Email, i.CreatedAt, i.ExpiresAt, i.Attempts })
                .ToListAsync(ct);

            var team = joined
                .Select(j => new TeamMemberDto
                {
                    SiteId = j.SiteId,
                    UserId = j.UserId,
                    SiteName = siteNames[j.SiteId],
                    Email = j.Email,
                    FullName = j.FullName,
                    Status = TeamStatus.Joined,
                    Since = j.CreatedAt
                })
                .ToList();

            var now = DateTime.UtcNow;

            foreach (var invite in invites)
            {
                var alreadyJoined = team.Any(m => m.SiteId == invite.SiteId && string.Equals(m.Email, invite.Email, StringComparison.OrdinalIgnoreCase));
                if (alreadyJoined)
                    continue;

                var status = TeamStatus.Invited;
                if (invite.ExpiresAt <= now || invite.Attempts >= MaxAttempts)
                    status = TeamStatus.Expired;

                team.Add(new TeamMemberDto
                {
                    SiteId = invite.SiteId,
                    SiteName = siteNames[invite.SiteId],
                    Email = invite.Email,
                    Status = status,
                    Since = invite.CreatedAt,
                    ExpiresAt = invite.ExpiresAt
                });
            }

            return team.OrderByDescending(m => m.Since).ToList();
        }

        // Takes a worker off one of the operator's sites; their account stays, and access to any other sites they belong to is untouched
        public async Task<TeamActionResult> RemoveMemberAsync(Guid siteId, int userId, string operatorEmail, CancellationToken ct = default)
        {
            var site = await FindOwnedSiteAsync(siteId, operatorEmail.Trim().ToLowerInvariant(), ct);

            if (site is null)
                return TeamActionResult.NotAuthorized();

            return await RemoveMemberFromSiteAsync(site, userId, ct);
        }

        // Takes a worker off any active site on an Admin's say-so; their account stays, and access to any other sites they belong to is untouched
        public async Task<TeamActionResult> AdminRemoveMemberAsync(Guid siteId, int userId, CancellationToken ct = default)
        {
            var site = await FindActiveSiteAsync(siteId, ct);

            if (site is null)
                return TeamActionResult.NotFound();

            return await RemoveMemberFromSiteAsync(site, userId, ct);
        }

        // Deletes a worker's membership of a site and reports which worker and site it was
        private async Task<TeamActionResult> RemoveMemberFromSiteAsync(Site site, int userId, CancellationToken ct)
        {
            var siteId = site.Id;
            var membership = await _database.SiteUsers.FirstOrDefaultAsync(su => su.SiteId == siteId && su.UserId == userId, ct);

            if (membership is null)
                return TeamActionResult.NotFound();

            var email = await _database.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(ct);

            _database.SiteUsers.Remove(membership);
            await _database.SaveChangesAsync(ct);

            return TeamActionResult.Done(email ?? string.Empty, site.Name);
        }

        // Withdraws an invitation that has not been used yet, so its link and access code stop working
        public async Task<TeamActionResult> CancelInviteAsync(Guid siteId, string email, string operatorEmail, CancellationToken ct = default)
        {
            var site = await FindOwnedSiteAsync(siteId, operatorEmail.Trim().ToLowerInvariant(), ct);

            if (site is null)
                return TeamActionResult.NotAuthorized();

            return await CancelInviteOnSiteAsync(site, email, ct);
        }

        // Withdraws an unused invitation on any active site on an Admin's say-so, so its link and access code stop working
        public async Task<TeamActionResult> AdminCancelInviteAsync(Guid siteId, string email, CancellationToken ct = default)
        {
            var site = await FindActiveSiteAsync(siteId, ct);

            if (site is null)
                return TeamActionResult.NotFound();

            return await CancelInviteOnSiteAsync(site, email, ct);
        }

        // Deletes the open invitation for an email on a site and reports which invitation and site it was
        private async Task<TeamActionResult> CancelInviteOnSiteAsync(Site site, string email, CancellationToken ct)
        {
            var siteId = site.Id;
            var workerEmail = email.Trim().ToLowerInvariant();

            var removed = await _database.SiteInvites
                .Where(i => i.SiteId == siteId && i.Email == workerEmail)
                .ExecuteDeleteAsync(ct);

            if (removed == 0)
                return TeamActionResult.NotFound();

            return TeamActionResult.Done(workerEmail, site.Name);
        }

        // Returns the site when it exists and has not been deleted, whoever owns it
        private async Task<Site?> FindActiveSiteAsync(Guid siteId, CancellationToken ct)
        {
            return await _database.Sites
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == siteId && !s.IsDeleted, ct);
        }

        private async Task<Site?> FindOwnedSiteAsync(Guid siteId, string operatorEmail, CancellationToken ct)
        {
            var isVerifiedOperator = await _database.Users
                .AsNoTracking()
                .AnyAsync(u => u.Email == operatorEmail
                    && u.Role == UserRoles.Operator
                    && u.Status == UserStatus.Approved, ct);

            if (!isVerifiedOperator)
                return null;

            return await _database.Sites
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == siteId
                    && !s.IsDeleted
                    && s.OwnerEmail != null
                    && s.OwnerEmail.ToLower() == operatorEmail, ct);
        }

        // Builds the web claim link the worker opens to complete setup in a browser
        private string BuildClaimUrl(string token, string email)
        {
            var configuredBaseUrl = _configuration["Invitation:WebBaseUrl"];

            if (string.IsNullOrWhiteSpace(configuredBaseUrl))
                configuredBaseUrl = "https://localhost:7043";

            return $"{configuredBaseUrl.TrimEnd('/')}/Account/ClaimInvite?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(email)}";
        }

        // Generates an 8-character access code from 36 symbols using a CSPRNG, giving 36^8 (about 2.8 trillion) combinations
        private static string GenerateAccessCode()
        {
            var chars = new char[CodeLength];

            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
            }

            return new string(chars);
        }

        // Formats a raw access code for display, for example HYDR9X2W becomes HYDR-9X2W
        public static string FormatCode(string rawCode)
        {
            return $"{rawCode[..4]}-{rawCode[4..]}";
        }

        // Strips separators and upper-cases a typed access code so HYDR-9X2W and hydr9x2w compare equal
        public static string NormaliseCode(string code)
        {
            return new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        }

        // Checks a supplied token or access code against its stored hash in constant time
        public bool VerifySecret(string suppliedSecret, string storedHash)
        {
            try
            {
                var parts = storedHash.Split('$', StringSplitOptions.None);

                if (parts.Length != 4 || parts[0] != HashPrefix)
                    return false;

                if (!int.TryParse(parts[1], out var iterations))
                    return false;

                var salt = Convert.FromBase64String(parts[2]);
                var expected = Convert.FromBase64String(parts[3]);

                var actual = Rfc2898DeriveBytes.Pbkdf2(
                    Pepper(suppliedSecret),
                    salt,
                    iterations,
                    HashAlgorithmName.SHA256,
                    expected.Length);

                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch (Exception ex) when (ex is FormatException || ex is InvalidOperationException)
            {
                return false;
            }
        }

        // Hashes a secret with the HMAC pepper and PBKDF2, storing the iteration count and salt inside the PBKDF2-SHA256$iter$salt$hash string
        private string HashSecret(string secret)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                Pepper(secret),
                salt,
                HashIterations,
                HashAlgorithmName.SHA256,
                HashSize);

            return $"{HashPrefix}${HashIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        // Applies the HMAC-SHA256 pepper read from the Security__HmacPepper environment variable
        private byte[] Pepper(string secret)
        {
            var pepper = _configuration["Security:HmacPepper"];

            if (string.IsNullOrWhiteSpace(pepper))
                throw new InvalidOperationException("Security:HmacPepper is not configured.");

            return HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(pepper),
                Encoding.UTF8.GetBytes(secret));
        }
    }
}