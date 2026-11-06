using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Services
{
    public class AuthenticationService
    {
        private readonly ApplicationDbContext _database;
        private readonly IEmailSender _emailSender;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;
        private readonly ILogger<AuthenticationService> _logger;
        private readonly EmailRateLimiter _emailRateLimiter;
        private readonly AuthEmailQueue _emailQueue;
        private readonly SecretHasher _secretHasher;

        private const int CodeLength = 6;
        private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
        private const int MaxAttempts = 5;

        private const int MaxFailedLoginAttempts = 5;
        private static readonly TimeSpan FailedLoginWindow = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

        private const int PasswordHashIterations = 100_000;
        private const int PasswordHashSize = 32;
        private const int PasswordSaltSize = 16;

        private const int PasswordHistoryLimit = 5;
        private const int ResetTokenSize = 32;
        private static readonly TimeSpan ResetTokenLifetime = TimeSpan.FromMinutes(10);

        private static readonly TimeSpan RegisterNotifyCooldown = TimeSpan.FromSeconds(60);
        private const int RegistrationTokenSize = 32;
        private const int ChallengeTokenSize = 32;
        private const string UnknownClientIp = "unknown";
        private const int MaxClientIpLength = 64;
        private const int MaxRegistrationCodeSends = 3;
        private const string DummyPasswordHash = "PBKDF2-SHA256$100000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
        private const string UnmatchableCodeHash = "0000000000000000000000000000000000000000000000000000000000000000";

        public AuthenticationService(
            ApplicationDbContext database,
            IEmailSender emailSender,
            IConfiguration configuration,
            IMemoryCache cache,
            ILogger<AuthenticationService> logger,
            EmailRateLimiter emailRateLimiter,
            AuthEmailQueue emailQueue,
            SecretHasher secretHasher)
        {
            _database = database;
            _emailSender = emailSender;
            _configuration = configuration;
            _cache = cache;
            _logger = logger;
            _emailRateLimiter = emailRateLimiter;
            _emailQueue = emailQueue;
            _secretHasher = secretHasher;
        }

        private sealed record AllowedUser(string Email, string Role, string PasswordHash);

        // Reads the configuration-provisioned accounts that can always sign in
        private IReadOnlyList<AllowedUser> GetAllowedUsers()
        {
            return _configuration.GetSection("Authentication:AllowedUsers").GetChildren()
                .Select(section => new AllowedUser(
                    section["Email"] ?? string.Empty,
                    section["Role"] ?? UserRoles.Operator,
                    section["PasswordHash"] ?? string.Empty))
                .Where(x => !string.IsNullOrWhiteSpace(x.Email) && !string.IsNullOrWhiteSpace(x.PasswordHash))
                .ToList();
        }

        // Finds a configuration-provisioned account by email, ignoring case
        private AllowedUser? FindAllowedUser(string email) =>
            GetAllowedUsers().FirstOrDefault(x =>
                string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase));

        // Finds an account that may sign in: a configuration-provisioned user first, then an approved database user
        private async Task<AllowedUser?> FindAllowedUserAsync(string email)
        {
            var configUser = FindAllowedUser(email);
            if (configUser is not null)
                return configUser;

            var dbUser = await _database.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == email && u.Status == UserStatus.Approved);

            if (dbUser is null || string.IsNullOrWhiteSpace(dbUser.PasswordHash))
                return null;

            return new AllowedUser(dbUser.Email, dbUser.Role, dbUser.PasswordHash);
        }

        // Checks a password against a stored PBKDF2-SHA256 hash in constant time
        public static bool VerifyPassword(string password, string storedHash)
        {
            try
            {
                var parts = storedHash.Split('$', StringSplitOptions.None);
                if (parts.Length != 4 || parts[0] != "PBKDF2-SHA256")
                    return false;

                var iterations = int.Parse(parts[1]);
                var salt = Convert.FromBase64String(parts[2]);
                var expected = Convert.FromBase64String(parts[3]);
                var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch
            {
                return false;
            }
        }

        // Hashes a password into the self-describing PBKDF2-SHA256$iterations$salt$hash format
        public static string HashPassword(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(PasswordSaltSize);
            var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordHashIterations, HashAlgorithmName.SHA256, PasswordHashSize);
            return $"PBKDF2-SHA256${PasswordHashIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        // Returns the database password hash when one has been set, otherwise the configuration-provisioned hash
        private async Task<string> GetEffectivePasswordHashAsync(string email, AllowedUser allowed)
        {
            var user = await _database.Users.FirstOrDefaultAsync(x => x.Email == email);

            if (user is not null && !string.IsNullOrWhiteSpace(user.PasswordHash))
                return user.PasswordHash;

            return allowed.PasswordHash;
        }

        // Checks the email and password and emails a one-time code bound to a fresh challenge token, reporting pending operators and lockouts separately, with failures counted per client IP and email
        public async Task<OtpRequestResult> RequestOtpAsync(string email, string password, string clientIp)
        {
            email = email.Trim().ToLowerInvariant();
            clientIp = NormalizeClientIp(clientIp);

            var remainingLockout = await GetRemainingLockoutAsync(email, clientIp);
            if (remainingLockout > TimeSpan.Zero)
                return OtpRequestResult.LockedOut(remainingLockout);

            var allowed = await FindAllowedUserAsync(email);
            var passwordValid = false;

            if (allowed is not null)
            {
                var effectiveHash = await GetEffectivePasswordHashAsync(email, allowed);
                passwordValid = VerifyPassword(password, effectiveHash);
            }
            else
            {
                var blockedUser = await _database.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Email == email
                        && (u.Status == UserStatus.PendingApproval || u.Status == UserStatus.Disabled));

                var blockedHash = DummyPasswordHash;

                if (blockedUser is not null && !string.IsNullOrWhiteSpace(blockedUser.PasswordHash))
                    blockedHash = blockedUser.PasswordHash;

                var blockedPasswordValid = VerifyPassword(password, blockedHash);

                if (blockedUser is not null && blockedPasswordValid)
                {
                    if (blockedUser.Status == UserStatus.Disabled)
                        return OtpRequestResult.Disabled();

                    return OtpRequestResult.PendingApproval();
                }
            }

            if (!passwordValid)
                return await RecordFailedLoginAsync(email, clientIp);

            await ClearFailedLoginsAsync(email, clientIp);

            var challengeToken = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(ChallengeTokenSize));
            var challengeHash = _secretHasher.Hash(challengeToken);

            // Only a code that can still be used is reused within the cooldown. A code that was already used to sign in
            // (for example the user signed out and straight back in) must not count, or no new email would be sent
            var now = DateTime.UtcNow;

            var recentCode = await _database.OtpCodes
                .AsNoTracking()
                .Where(o => o.Email == email
                    && !o.IsUsed
                    && o.ExpiresAt > now
                    && o.Attempts < MaxAttempts)
                .OrderByDescending(o => o.CreatedAt)
                .FirstOrDefaultAsync();

            if (recentCode is not null && now - recentCode.CreatedAt < ResendCooldown)
            {
                var rebound = await RebindLiveOtpAsync(recentCode.Id, challengeHash);

                if (rebound)
                    return OtpRequestResult.Sent(challengeToken);
            }

            var code = GenerateNumericCode();

            var otpCode = new OtpCode
            {
                Email = email,
                CodeHash = _secretHasher.Hash(code),
                ChallengeHash = challengeHash,
                ExpiresAt = DateTime.UtcNow + CodeLifetime
            };

            _database.OtpCodes.Add(otpCode);
            await _database.SaveChangesAsync();
            await SendOtpOrDiscardAsync(otpCode, code);
            return OtpRequestResult.Sent(challengeToken);
        }

        // Moves a code sent within the cooldown onto the newest challenge token so a repeated sign-in can use it without sending another email
        // Returns false when the code was used or expired in the meantime, so the caller sends a fresh one instead
        private async Task<bool> RebindLiveOtpAsync(long otpId, string challengeHash)
        {
            var now = DateTime.UtcNow;

            var updated = await _database.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE otp_codes
                SET challenge_hash = {challengeHash}
                WHERE id = {otpId}
                  AND is_used = false
                  AND expires_at > {now}
                  AND attempts < {MaxAttempts}
                """);

            return updated > 0;
        }

        // Generates a cryptographically random numeric code of the configured length
        private static string GenerateNumericCode()
        {
            return RandomNumberGenerator
                .GetInt32(0, (int)Math.Pow(10, CodeLength))
                .ToString($"D{CodeLength}");
        }

        // Trims the full name from a sign-up, giving null when it is blank so older sign-ups without a name fall back to their email
        private static string? CleanFullName(string? fullName)
        {
            var trimmed = (fullName ?? string.Empty).Trim();

            if (trimmed.Length == 0)
            {
                return null;
            }

            return trimmed;
        }

        // Trims and lower-cases the client IP, falling back to a fixed value when none is known, so it can be used as part of a key
        private static string NormalizeClientIp(string clientIp)
        {
            var normalized = (clientIp ?? string.Empty).Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(normalized))
                return UnknownClientIp;

            if (normalized.Length > MaxClientIpLength)
                return normalized.Substring(0, MaxClientIpLength);

            return normalized;
        }

        // Returns how long this client IP is still locked out of this email for, or zero when it is not locked
        private async Task<TimeSpan> GetRemainingLockoutAsync(string email, string clientIp)
        {
            var lockedUntil = await _database.LoginAttempts
                .AsNoTracking()
                .Where(x => x.Email == email && x.ClientIp == clientIp)
                .Select(x => x.LockedUntil)
                .FirstOrDefaultAsync();

            if (lockedUntil is null)
                return TimeSpan.Zero;

            var remaining = lockedUntil.Value - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return TimeSpan.Zero;

            return remaining;
        }

        // Records a failed sign-in atomically against the pair of client IP and email, and locks only that pair once the failure limit is reached
        private async Task<OtpRequestResult> RecordFailedLoginAsync(string email, string clientIp)
        {
            var now = DateTime.UtcNow;
            var windowStart = now - FailedLoginWindow;
            var lockedUntil = now + LockoutDuration;

            await _database.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE login_attempts
                SET failed_attempts = 0, locked_until = NULL
                WHERE email = {email}
                  AND client_ip = {clientIp}
                  AND ((locked_until IS NOT NULL AND locked_until <= {now})
                    OR (locked_until IS NULL AND last_failed_at < {windowStart}))
                """);

            await _database.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO login_attempts (email, client_ip, failed_attempts, last_failed_at, locked_until)
                VALUES ({email}, {clientIp}, 1, {now}, NULL)
                ON CONFLICT (email, client_ip) DO UPDATE
                SET failed_attempts = login_attempts.failed_attempts + 1,
                    last_failed_at = EXCLUDED.last_failed_at
                """);

            await _database.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE login_attempts
                SET locked_until = {lockedUntil}
                WHERE email = {email}
                  AND client_ip = {clientIp}
                  AND locked_until IS NULL
                  AND failed_attempts >= {MaxFailedLoginAttempts}
                """);

            var remainingLockout = await GetRemainingLockoutAsync(email, clientIp);
            if (remainingLockout > TimeSpan.Zero)
                return OtpRequestResult.LockedOut(remainingLockout);

            return OtpRequestResult.InvalidCredentials();
        }

        // Clears the failed sign-in counter for this client IP and email after a successful password check
        private async Task ClearFailedLoginsAsync(string email, string clientIp)
        {
            await _database.LoginAttempts
                .Where(x => x.Email == email && x.ClientIp == clientIp)
                .ExecuteDeleteAsync();
        }

        // Clears the failed sign-in counters for every client IP of this email after the password is reset
        private async Task ClearAllFailedLoginsAsync(string email)
        {
            await _database.LoginAttempts
                .Where(x => x.Email == email)
                .ExecuteDeleteAsync();
        }

        // Resends a one-time code only when the challenge token belongs to a live code created by a password-checked sign-in, keeping the new code on the same challenge
        public async Task<bool> ResendOtpAsync(string email, string challengeToken)
        {
            email = email.Trim().ToLowerInvariant();
            var allowed = await FindAllowedUserAsync(email);

            if (allowed is null)
                return true;

            var challengeHash = _secretHasher.Hash((challengeToken ?? string.Empty).Trim());
            var now = DateTime.UtcNow;

            var recentCode = await _database.OtpCodes
                .AsNoTracking()
                .Where(o => o.Email == email && o.ChallengeHash == challengeHash)
                .OrderByDescending(o => o.CreatedAt)
                .FirstOrDefaultAsync();

            if (recentCode is null || recentCode.IsUsed || recentCode.ExpiresAt <= now || recentCode.Attempts >= MaxAttempts)
                return true;

            if (now - recentCode.CreatedAt < ResendCooldown)
                return true;

            var code = GenerateNumericCode();

            var otpCode = new OtpCode
            {
                Email = email,
                CodeHash = _secretHasher.Hash(code),
                ChallengeHash = challengeHash,
                ExpiresAt = DateTime.UtcNow + CodeLifetime
            };

            _database.OtpCodes.Add(otpCode);
            await _database.SaveChangesAsync();
            await SendOtpOrDiscardAsync(otpCode, code);
            return true;
        }

        // Emails the one-time code and deletes it again if sending fails so the cooldown does not block a retry
        private async Task SendOtpOrDiscardAsync(OtpCode otpCode, string code)
        {
            try
            {
                await _emailSender.SendOtpEmailAsync(otpCode.Email, code);
            }
            catch
            {
                _database.OtpCodes.Remove(otpCode);
                await _database.SaveChangesAsync();
                throw;
            }
        }

        // Verifies the one-time code for the sign-in identified by the challenge token, counting the attempt atomically and consuming the code at most once, then issues a JWT carrying the account's role
        public async Task<AuthResponseDto?> VerifyOtpAsync(string email, string challengeToken, string code, bool rememberMe = false)
        {
            email = email.Trim().ToLowerInvariant();
            var allowed = await FindAllowedUserAsync(email);
            if (allowed is null)
                return null;

            var challengeHash = _secretHasher.Hash((challengeToken ?? string.Empty).Trim());
            var now = DateTime.UtcNow;

            var claimed = await _database.OtpCodes
                .FromSqlInterpolated($"""
                    UPDATE otp_codes
                    SET attempts = attempts + 1
                    WHERE id = (
                        SELECT id
                        FROM otp_codes
                        WHERE email = {email}
                          AND challenge_hash = {challengeHash}
                          AND is_used = false
                          AND expires_at > {now}
                        ORDER BY created_at DESC
                        LIMIT 1)
                      AND is_used = false
                      AND expires_at > {now}
                      AND attempts < {MaxAttempts}
                    RETURNING *
                    """)
                .AsNoTracking()
                .ToListAsync();

            OtpCode? otp = null;
            var expectedHash = UnmatchableCodeHash;

            if (claimed.Count == 1)
            {
                otp = claimed[0];
                expectedHash = otp.CodeHash;
            }

            var codeMatches = _secretHasher.Matches((code ?? string.Empty).Trim(), expectedHash);

            if (otp is null || !codeMatches)
                return null;

            var consumed = await _database.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE otp_codes
                SET is_used = true
                WHERE id = {otp.Id}
                  AND is_used = false
                """);

            if (consumed == 0)
                return null;

            var user = await _database.Users.FirstOrDefaultAsync(x => x.Email == email);
            if (user is null)
            {
                user = new User { Email = email };
                _database.Users.Add(user);
            }

            user.Role = allowed.Role;

            if (string.IsNullOrWhiteSpace(user.PasswordHash))
                user.PasswordHash = allowed.PasswordHash;

            user.LastLoginAt = DateTime.UtcNow;

            await _database.SaveChangesAsync();
            return GenerateToken(user, rememberMe);
        }

        // Queues a password reset so the response does the same work and takes the same time whether or not the email belongs to an account
        public bool RequestPasswordReset(string email)
        {
            email = email.Trim().ToLowerInvariant();

            var job = new AuthEmailJob(AuthEmailJobKind.PasswordReset, email, string.Empty, string.Empty, string.Empty, string.Empty);

            if (_emailQueue.TryEnqueue(job))
                return true;

            _logger.LogError("The authentication email queue is full; the password reset for {Email} was not queued.", email);
            return false;
        }

        // Emails a password reset code when the email belongs to an approved account, doing nothing otherwise
        private async Task ProcessPasswordResetAsync(string email)
        {
            var allowed = await FindAllowedUserAsync(email);

            if (allowed is null)
                return;

            var recentCode = await _database.PasswordResetCodes
                .Where(o => o.Email == email)
                .OrderByDescending(o => o.CreatedAt)
                .FirstOrDefaultAsync();

            if (recentCode is not null && DateTime.UtcNow - recentCode.CreatedAt < ResendCooldown)
                return;

            var code = GenerateNumericCode();

            _database.PasswordResetCodes.Add(new PasswordResetCode
            {
                Email = email,
                CodeHash = _secretHasher.Hash(code),
                ExpiresAt = DateTime.UtcNow + CodeLifetime
            });

            await _database.SaveChangesAsync();
            await _emailSender.SendPasswordResetEmailAsync(email, code);
        }

        // Atomically counts one attempt against the newest live, unverified reset code for the email and returns it, or null when there is none or its attempts are used up
        private async Task<PasswordResetCode?> ClaimResetCodeAttemptAsync(string email)
        {
            var now = DateTime.UtcNow;

            var claimed = await _database.PasswordResetCodes
                .FromSqlInterpolated($"""
                    UPDATE password_reset_codes
                    SET attempts = attempts + 1
                    WHERE id = (
                        SELECT id
                        FROM password_reset_codes
                        WHERE email = {email}
                          AND is_used = false
                          AND is_verified = false
                          AND expires_at > {now}
                        ORDER BY created_at DESC
                        LIMIT 1)
                      AND is_used = false
                      AND is_verified = false
                      AND expires_at > {now}
                      AND attempts < {MaxAttempts}
                    RETURNING *
                    """)
                .AsNoTracking()
                .ToListAsync();

            if (claimed.Count != 1)
                return null;

            return claimed[0];
        }

        // Counts the attempt atomically and checks the reset code, returning the claimed reset only when the code matches
        private async Task<PasswordResetCode?> MatchResetCodeAsync(string email, string code)
        {
            var reset = await ClaimResetCodeAttemptAsync(email);
            var expectedHash = UnmatchableCodeHash;

            if (reset is not null)
                expectedHash = reset.CodeHash;

            var codeMatches = _secretHasher.Matches((code ?? string.Empty).Trim(), expectedHash);

            if (reset is null || !codeMatches)
                return null;

            return reset;
        }

        // Exchanges a valid reset code for a short-lived reset token, marking the code verified at most once even under parallel requests
        public async Task<VerifyResetCodeResponseDto?> VerifyResetCodeAsync(string email, string code)
        {
            email = email.Trim().ToLowerInvariant();
            var allowed = await FindAllowedUserAsync(email);
            if (allowed is null)
                return null;

            var reset = await MatchResetCodeAsync(email, code);
            if (reset is null)
                return null;

            var resetToken = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(ResetTokenSize));
            var resetTokenHash = _secretHasher.Hash(resetToken);
            var expiresAt = DateTime.UtcNow + ResetTokenLifetime;

            var verified = await _database.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE password_reset_codes
                SET is_verified = true,
                    attempts = 0,
                    reset_token_hash = {resetTokenHash},
                    reset_token_expires_at = {expiresAt}
                WHERE id = {reset.Id}
                  AND is_verified = false
                  AND is_used = false
                """);

            if (verified == 0)
                return null;

            return new VerifyResetCodeResponseDto
            {
                ResetToken = resetToken,
                ExpiresAt = expiresAt
            };
        }

        // Sets a new password using a verified reset token, refusing any of the last five passwords
        public async Task<PasswordResetStatus> ResetPasswordAsync(string email, string resetToken, string newPassword)
        {
            email = email.Trim().ToLowerInvariant();
            var allowed = await FindAllowedUserAsync(email);
            if (allowed is null)
                return PasswordResetStatus.InvalidToken;

            var tokenHash = _secretHasher.Hash(resetToken);
            var now = DateTime.UtcNow;

            var reset = await _database.PasswordResetCodes
                .AsNoTracking()
                .Where(o => o.Email == email
                    && o.IsVerified
                    && !o.IsUsed
                    && o.ResetTokenHash == tokenHash
                    && o.ResetTokenExpiresAt > now)
                .OrderByDescending(o => o.CreatedAt)
                .FirstOrDefaultAsync();

            if (reset is null || reset.Attempts >= MaxAttempts)
                return PasswordResetStatus.InvalidToken;

            return await ApplyNewPasswordAsync(reset.Id, email, allowed, newPassword);
        }

        // Sets a new password straight from the emailed reset code, as the Android app does, counting every try atomically against the same attempt limit as the two-step web flow
        public async Task<PasswordResetStatus> ResetPasswordWithCodeAsync(string email, string code, string newPassword)
        {
            email = email.Trim().ToLowerInvariant();
            var allowed = await FindAllowedUserAsync(email);
            if (allowed is null)
                return PasswordResetStatus.InvalidToken;

            var reset = await MatchResetCodeAsync(email, code);
            if (reset is null)
                return PasswordResetStatus.InvalidToken;

            return await ApplyNewPasswordAsync(reset.Id, email, allowed, newPassword);
        }

        // The database retries on transient failures, so the whole transaction runs as one unit the retry can repeat
        private Task<PasswordResetStatus> ApplyNewPasswordAsync(long resetId, string email, AllowedUser allowed, string newPassword)
        {
            var strategy = _database.Database.CreateExecutionStrategy();
            return strategy.ExecuteAsync(() => ApplyNewPasswordInTransactionAsync(resetId, email, allowed, newPassword));
        }

        // Refuses any of the recent passwords, otherwise uses up the reset exactly once, saves the new password hash, cancels pending sign-in codes and clears failed sign-ins in one transaction
        private async Task<PasswordResetStatus> ApplyNewPasswordInTransactionAsync(long resetId, string email, AllowedUser allowed, string newPassword)
        {
            var currentHash = await GetEffectivePasswordHashAsync(email, allowed);

            if (await IsRecentlyUsedPasswordAsync(email, newPassword, currentHash))
            {
                await _database.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE password_reset_codes
                    SET attempts = attempts + 1,
                        is_used = is_used OR attempts + 1 >= {MaxAttempts}
                    WHERE id = {resetId}
                    """);

                return PasswordResetStatus.PasswordReused;
            }

            await using var transaction = await _database.Database.BeginTransactionAsync();

            var consumed = await _database.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE password_reset_codes
                SET is_used = true
                WHERE id = {resetId}
                  AND is_used = false
                  AND attempts < {MaxAttempts}
                """);

            if (consumed == 0)
            {
                await transaction.RollbackAsync();
                return PasswordResetStatus.InvalidToken;
            }

            var user = await _database.Users.FirstOrDefaultAsync(x => x.Email == email);
            if (user is null)
            {
                user = new User { Email = email, Role = allowed.Role };
                _database.Users.Add(user);
            }

            var newHash = HashPassword(newPassword);
            user.PasswordHash = newHash;

            await RecordPasswordHistoryAsync(email, currentHash, newHash);

            await _database.SaveChangesAsync();
            await ExpirePendingSignInCodesAsync(email);
            await ClearAllFailedLoginsAsync(email);
            await transaction.CommitAsync();

            return PasswordResetStatus.Succeeded;
        }

        // Marks every unused sign-in code for the email as used so a code requested with the old password cannot finish a sign-in after the password changes
        private async Task ExpirePendingSignInCodesAsync(string email)
        {
            await _database.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE otp_codes
                SET is_used = true
                WHERE email = {email}
                  AND is_used = false
                """);
        }

        // Checks whether a password matches the current one or any of the recent password history
        private async Task<bool> IsRecentlyUsedPasswordAsync(string email, string password, string currentHash)
        {
            if (VerifyPassword(password, currentHash))
                return true;

            var previousHashes = await _database.PasswordHistory
                .AsNoTracking()
                .Where(x => x.Email == email)
                .OrderByDescending(x => x.CreatedAt)
                .Take(PasswordHistoryLimit)
                .Select(x => x.PasswordHash)
                .ToListAsync();

            foreach (var previousHash in previousHashes)
            {
                if (VerifyPassword(password, previousHash))
                    return true;
            }

            return false;
        }

        // Adds the old and new hashes to the password history and trims it to the history limit
        private async Task RecordPasswordHistoryAsync(string email, string currentHash, string newHash)
        {
            var history = await _database.PasswordHistory
                .Where(x => x.Email == email)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();

            var now = DateTime.UtcNow;
            var currentAlreadyRecorded = history.Any(x => x.PasswordHash == currentHash);

            if (!currentAlreadyRecorded && !string.IsNullOrWhiteSpace(currentHash))
            {
                var currentEntry = new PasswordHistory
                {
                    Email = email,
                    PasswordHash = currentHash,
                    CreatedAt = now.AddTicks(-1)
                };

                _database.PasswordHistory.Add(currentEntry);
                history.Insert(0, currentEntry);
            }

            var newEntry = new PasswordHistory
            {
                Email = email,
                PasswordHash = newHash,
                CreatedAt = now
            };

            _database.PasswordHistory.Add(newEntry);
            history.Insert(0, newEntry);

            var expired = history.Skip(PasswordHistoryLimit).ToList();
            _database.PasswordHistory.RemoveRange(expired);
        }

        // Accepts an operator sign-up and queues it, doing the same work and giving the same answer whether or not the email is already registered
        public RegistrationStartResult RegisterOperator(string email, string password, string farmName, string fullName)
        {
            email = email.Trim().ToLowerInvariant();
            farmName = (farmName ?? string.Empty).Trim();
            fullName = (fullName ?? string.Empty).Trim();

            var verificationToken = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(RegistrationTokenSize));
            var passwordHash = HashPassword(password);

            var job = new AuthEmailJob(
                AuthEmailJobKind.Register,
                email,
                _secretHasher.Hash(verificationToken),
                passwordHash,
                farmName,
                fullName);

            if (!_emailQueue.TryEnqueue(job))
            {
                _logger.LogError("The registration queue is full; the sign-up for {Email} was not queued.", email);
                return RegistrationStartResult.Failed();
            }

            return RegistrationStartResult.CodeSent(verificationToken);
        }

        // Accepts a request for a new verification code and queues it, giving the same answer whether or not the sign-up exists
        public void ResendRegistrationCode(string email, string verificationToken)
        {
            email = email.Trim().ToLowerInvariant();

            var job = new AuthEmailJob(
                AuthEmailJobKind.ResendRegistration,
                email,
                _secretHasher.Hash((verificationToken ?? string.Empty).Trim()),
                string.Empty,
                string.Empty,
                string.Empty);

            if (!_emailQueue.TryEnqueue(job))
                _logger.LogError("The registration queue is full; the resend for {Email} was not queued.", email);
        }

        // Runs a queued sign-up, registration resend or password reset in the background worker
        public async Task ProcessQueuedJobAsync(AuthEmailJob job)
        {
            if (job.Kind == AuthEmailJobKind.ResendRegistration)
            {
                await ProcessResendAsync(job);
                return;
            }

            if (job.Kind == AuthEmailJobKind.PasswordReset)
            {
                await ProcessPasswordResetAsync(job.Email);
                return;
            }

            await ProcessRegisterAsync(job);
        }

        // Emails a verification code for a new address, or only warns the real owner when the address is already registered
        private async Task ProcessRegisterAsync(AuthEmailJob job)
        {
            await DeleteExpiredPendingRegistrationsAsync();
            await _emailRateLimiter.DeleteStaleAsync();

            var configUser = FindAllowedUser(job.Email);
            var existingUser = await _database.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Email == job.Email);

            if (configUser is not null || existingUser is not null)
            {
                var status = UserStatus.Approved;

                if (existingUser is not null)
                    status = existingUser.Status;

                await NotifyAlreadyRegisteredAsync(job.Email, status);
                return;
            }

            var code = GenerateNumericCode();

            var pending = new PendingRegistration
            {
                Email = job.Email,
                PasswordHash = job.PasswordHash,
                FarmName = job.FarmName,
                FullName = job.FullName,
                VerificationTokenHash = job.VerificationTokenHash,
                CodeHash = _secretHasher.Hash(code),
                ExpiresAt = DateTime.UtcNow + CodeLifetime,
                CreatedAt = DateTime.UtcNow
            };

            _database.PendingRegistrations.Add(pending);
            await _database.SaveChangesAsync();

            if (await CanSendRegistrationCodeAsync(pending))
                await SendRegistrationCodeAsync(pending, code);
        }

        // Emails a fresh verification code when the token belongs to a live sign-up that still has sends left
        private async Task ProcessResendAsync(AuthEmailJob job)
        {
            var now = DateTime.UtcNow;

            var pending = await _database.PendingRegistrations
                .FirstOrDefaultAsync(p => p.Email == job.Email
                    && p.VerificationTokenHash == job.VerificationTokenHash
                    && p.ExpiresAt > now);

            if (pending is null)
                return;

            if (!await CanSendRegistrationCodeAsync(pending))
                return;

            var code = GenerateNumericCode();
            pending.CodeHash = _secretHasher.Hash(code);
            pending.Attempts = 0;
            pending.ExpiresAt = DateTime.UtcNow + CodeLifetime;

            await _database.SaveChangesAsync();
            await SendRegistrationCodeAsync(pending, code);
        }

        // Checks the emailed code with one database round trip that is the same whether or not the sign-up exists, and when it matches creates the operator account as PendingApproval for administrator review
        public async Task<RegistrationVerifyStatus> VerifyRegistrationAsync(string email, string verificationToken, string code)
        {
            email = email.Trim().ToLowerInvariant();

            var tokenHash = _secretHasher.Hash((verificationToken ?? string.Empty).Trim());
            var now = DateTime.UtcNow;

            var claimed = await _database.PendingRegistrations
                .FromSqlInterpolated($"""
                    UPDATE pending_registrations
                    SET attempts = attempts + 1
                    WHERE email = {email}
                      AND verification_token_hash = {tokenHash}
                      AND expires_at > {now}
                      AND attempts < {MaxAttempts}
                    RETURNING *
                    """)
                .AsNoTracking()
                .ToListAsync();

            PendingRegistration? pending = null;
            var expectedHash = UnmatchableCodeHash;

            if (claimed.Count == 1)
            {
                pending = claimed[0];
                expectedHash = pending.CodeHash;
            }

            var codeMatches = _secretHasher.Matches((code ?? string.Empty).Trim(), expectedHash);

            if (pending is null || !codeMatches)
                return RegistrationVerifyStatus.InvalidCode;

            var consumed = await _database.PendingRegistrations
                .Where(p => p.Id == pending.Id)
                .ExecuteDeleteAsync();

            if (consumed == 0)
                return RegistrationVerifyStatus.InvalidCode;

            await _database.PendingRegistrations
                .Where(p => p.Email == email)
                .ExecuteDeleteAsync();

            var alreadyRegistered = FindAllowedUser(email) is not null
                || await _database.Users.AnyAsync(x => x.Email == email);

            if (alreadyRegistered)
                return RegistrationVerifyStatus.InvalidCode;

            var user = new User
            {
                Email = email,
                Role = UserRoles.Operator,
                PasswordHash = pending.PasswordHash,
                Status = UserStatus.PendingApproval,
                FarmName = pending.FarmName,
                FullName = CleanFullName(pending.FullName),
                CreatedAt = DateTime.UtcNow
            };

            _database.Users.Add(user);

            try
            {
                await _database.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Could not save the verified operator registration for {Email}.", email);
                return RegistrationVerifyStatus.Failed;
            }

            await NotifyRegistrationReceivedAsync(email);
            return RegistrationVerifyStatus.PendingReview;
        }

        // Allows a verification email only when this sign-up has sends left and no code went to the same address within the cooldown
        private async Task<bool> CanSendRegistrationCodeAsync(PendingRegistration pending)
        {
            if (pending.SendCount >= MaxRegistrationCodeSends)
                return false;

            var cooldownStart = DateTime.UtcNow - ResendCooldown;

            var sentRecently = await _database.PendingRegistrations
                .AnyAsync(p => p.Email == pending.Email
                    && p.LastSentAt != null
                    && p.LastSentAt > cooldownStart);

            return !sentRecently;
        }

        // Emails the verification code and records the send, logging instead of failing so the worker keeps running
        private async Task SendRegistrationCodeAsync(PendingRegistration pending, string code)
        {
            try
            {
                await _emailSender.SendRegistrationOtpEmailAsync(pending.Email, code);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not send the registration verification code to {Email}.", pending.Email);
                return;
            }

            pending.SendCount++;
            pending.LastSentAt = DateTime.UtcNow;
            await _database.SaveChangesAsync();
        }

        // Removes sign-ups whose verification code has expired
        private async Task DeleteExpiredPendingRegistrationsAsync()
        {
            var now = DateTime.UtcNow;

            await _database.PendingRegistrations
                .Where(p => p.ExpiresAt <= now)
                .ExecuteDeleteAsync();
        }

        // Emails the real owner of an already-registered address, rate limited per email and never failing the request
        private async Task NotifyAlreadyRegisteredAsync(string email, string status)
        {
            var cacheKey = $"register-already-registered:{email}";
            if (_cache.TryGetValue(cacheKey, out _))
                return;

            try
            {
                await _emailSender.SendAccountAlreadyRegisteredEmailAsync(email, status);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not send the already-registered notice to {Email}.", email);
            }

            _cache.Set(cacheKey, true, RegisterNotifyCooldown);
        }

        // Emails a new operator that their registration was received and is pending review
        private async Task NotifyRegistrationReceivedAsync(string email)
        {
            var cacheKey = $"register-received:{email}";
            if (_cache.TryGetValue(cacheKey, out _))
                return;

            try
            {
                await _emailSender.SendRegistrationReceivedEmailAsync(email);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not send the registration-received email to {Email}.", email);
            }

            _cache.Set(cacheKey, true, RegisterNotifyCooldown);
        }

        // Lists every farm operator awaiting administrator approval, oldest first
        public async Task<List<User>> GetPendingOperatorsAsync(CancellationToken ct = default)
        {
            return await _database.Users
                .AsNoTracking()
                .Where(u => u.Status == UserStatus.PendingApproval && u.Role == UserRoles.Operator)
                .OrderBy(u => u.CreatedAt)
                .ToListAsync(ct);
        }

        // Approves a pending or previously denied farm operator, creates the site for their farm and emails them that they can now log in
        public async Task<OperatorReviewResult> ApproveOperatorAsync(int id, CancellationToken ct = default)
        {
            var user = await FindReviewableOperatorAsync(id, ct);

            if (user is null)
                return OperatorReviewResult.NotFound(id);

            user.Status = UserStatus.Approved;
            user.IsVerified = true;
            await EnsureFarmSiteAsync(user, ct);
            await _database.SaveChangesAsync(ct);

            var notificationSent = await TrySendReviewEmailAsync(user.Email, true);
            return OperatorReviewResult.Reviewed(user.Id, user.Email, user.Status, notificationSent);
        }

        // Denies a pending farm operator so the account can never sign in, and emails them the outcome
        public async Task<OperatorReviewResult> DenyOperatorAsync(int id, CancellationToken ct = default)
        {
            var user = await FindPendingOperatorAsync(id, ct);

            if (user is null)
                return OperatorReviewResult.NotFound(id);

            user.Status = UserStatus.Denied;
            await _database.SaveChangesAsync(ct);

            var notificationSent = await TrySendReviewEmailAsync(user.Email, false);
            return OperatorReviewResult.Reviewed(user.Id, user.Email, user.Status, notificationSent);
        }

        // Loads a tracked operator account that is awaiting approval or was denied, since an admin may change their mind about a denial
        private async Task<User?> FindReviewableOperatorAsync(int id, CancellationToken ct)
        {
            return await _database.Users
                .FirstOrDefaultAsync(
                    u => u.Id == id
                        && (u.Status == UserStatus.PendingApproval || u.Status == UserStatus.Denied)
                        && u.Role == UserRoles.Operator,
                    ct);
        }

        // Loads a tracked operator account that is still awaiting approval
        private async Task<User?> FindPendingOperatorAsync(int id, CancellationToken ct)
        {
            return await _database.Users
                .FirstOrDefaultAsync(
                    u => u.Id == id && u.Status == UserStatus.PendingApproval && u.Role == UserRoles.Operator,
                    ct);
        }

        // Sends the approval or denial email after the decision is saved, reporting whether it went out
        private async Task<bool> TrySendReviewEmailAsync(string email, bool approved)
        {
            try
            {
                if (approved)
                {
                    await _emailSender.SendAccountApprovedEmailAsync(email);
                }
                else
                {
                    await _emailSender.SendAccountDeniedEmailAsync(email);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The review decision for {Email} was saved but the notification email could not be sent.", email);
                return false;
            }
        }

        // Turns the starting site name given at registration into the operator's first site, unless they already own one
        private async Task EnsureFarmSiteAsync(User user, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(user.FarmName))
                return;

            var alreadyOwnsSite = await _database.Sites
                .AnyAsync(s => !s.IsDeleted && s.OwnerEmail == user.Email, ct);

            if (alreadyOwnsSite)
                return;

            _database.Sites.Add(new Site
            {
                Name = user.FarmName.Trim(),
                OwnerEmail = user.Email,
                CreatedAt = DateTime.UtcNow
            });
        }

        // Issues a signed JWT for a user whose identity has already been proven by another flow, such as claiming an invitation
        public AuthResponseDto IssueToken(User user)
        {
            return GenerateToken(user);
        }

        // Builds a signed JWT whose claims carry the user's id, email and role; a Remember me sign-in gets the longer lifetime (30 days unless Jwt:RememberMeExpiryMinutes says otherwise)
        private AuthResponseDto GenerateToken(User user, bool rememberMe = false)
        {
            var jwt = _configuration.GetSection("Jwt");
            var expiryMinutes = int.Parse(jwt["ExpiryMinutes"]!);

            if (rememberMe)
            {
                expiryMinutes = int.TryParse(jwt["RememberMeExpiryMinutes"], out var longer) && longer > 0 ? longer : 43200;
            }

            var expiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: jwt["Issuer"],
                audience: jwt["Audience"],
                claims: claims,
                expires: expiresAt,
                signingCredentials: creds);

            return new AuthResponseDto
            {
                Token = new JwtSecurityTokenHandler().WriteToken(token),
                ExpiresAt = expiresAt,
                Email = user.Email,
                Role = user.Role
            };
        }
    }
}