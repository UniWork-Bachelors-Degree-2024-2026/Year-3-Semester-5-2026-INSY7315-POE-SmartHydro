using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Data;

namespace SmartHydroAPI.Services
{
    public sealed record EmailLimitResult(bool Allowed, TimeSpan RetryAfter);

    public class EmailRateLimiter
    {
        public const string RegisterAction = "register";
        public const string VerifyRegistrationAction = "verify-registration";
        public const string ResendRegistrationAction = "resend-registration";
        public const string SignInAction = "sign-in";
        public const string VerifyOtpAction = "verify-otp";
        public const string ResendOtpAction = "resend-otp";
        public const string ForgotPasswordAction = "forgot-password";
        public const string VerifyResetCodeAction = "verify-reset-code";
        public const string ResetPasswordAction = "reset-password";
        public const string ClaimInviteAction = "claim-invite";

        public const int RegisterLimit = 3;
        public const int VerifyRegistrationLimit = 5;
        public const int ResendRegistrationLimit = 3;
        public const int SignInLimit = 15;
        public const int VerifyOtpLimit = 5;
        public const int ResendOtpLimit = 3;
        public const int ForgotPasswordLimit = 3;
        public const int VerifyResetCodeLimit = 5;
        public const int ResetPasswordLimit = 5;
        public const int ClaimInviteLimit = 5;

        public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

        private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(1);
        private static readonly TimeSpan MinimumRetryAfter = TimeSpan.FromSeconds(1);

        private readonly ApplicationDbContext _database;

        public EmailRateLimiter(ApplicationDbContext database)
        {
            _database = database;
        }

        // Counts one attempt for this action and email in the database, and when the limit for the current 15-minute window is passed reports exactly how long until that window ends, so the limit holds across every IP address and every API instance
        public async Task<EmailLimitResult> TryConsumeAsync(string action, string email, int limit)
        {
            var key = action + ":" + email.Trim().ToLowerInvariant();
            return await ConsumeKeyAsync(key, limit);
        }

        // Counts one attempt for this action against the pair of client IP and email, so a caller on one IP cannot use up the allowance of the account owner on another IP
        public async Task<EmailLimitResult> TryConsumeAsync(string action, string clientIp, string email, int limit)
        {
            var key = action + ":" + clientIp.Trim().ToLowerInvariant() + ":" + email.Trim().ToLowerInvariant();
            return await ConsumeKeyAsync(key, limit);
        }

        // Gives back one counted attempt for the pair of client IP and email, used when the attempt succeeded so only failures use up the allowance
        public async Task RefundAsync(string action, string clientIp, string email)
        {
            var key = action + ":" + clientIp.Trim().ToLowerInvariant() + ":" + email.Trim().ToLowerInvariant();

            await _database.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE email_rate_limits
                SET hits = GREATEST(hits - 1, 0)
                WHERE limit_key = {key}
                """);
        }

        // Increments the counter for a limit key inside its 15-minute window and reports whether the attempt is allowed and, if not, how long until the window ends
        private async Task<EmailLimitResult> ConsumeKeyAsync(string key, int limit)
        {
            var now = DateTime.UtcNow;
            var expiredBefore = now - Window;

            await _database.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE email_rate_limits
                SET hits = 0, window_start = {now}
                WHERE limit_key = {key}
                  AND window_start <= {expiredBefore}
                """);

            await _database.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO email_rate_limits (limit_key, hits, window_start)
                VALUES ({key}, 1, {now})
                ON CONFLICT (limit_key) DO UPDATE
                SET hits = email_rate_limits.hits + 1
                """);

            var counter = await _database.EmailRateLimits
                .AsNoTracking()
                .Where(x => x.LimitKey == key)
                .Select(x => new { x.Hits, x.WindowStart })
                .FirstOrDefaultAsync();

            if (counter is null || counter.Hits <= limit)
                return new EmailLimitResult(true, TimeSpan.Zero);

            var retryAfter = counter.WindowStart + Window - now;

            if (retryAfter < MinimumRetryAfter)
                retryAfter = MinimumRetryAfter;

            return new EmailLimitResult(false, retryAfter);
        }

        // Words a wait time for a message, in whole seconds under a minute and otherwise rounded up to whole minutes
        public static string DescribeWait(TimeSpan wait)
        {
            var seconds = (int)Math.Ceiling(wait.TotalSeconds);

            if (seconds <= 1)
                return "1 second";

            if (seconds < 60)
                return seconds + " seconds";

            var minutes = (int)Math.Ceiling(wait.TotalMinutes);

            if (minutes == 1)
                return "1 minute";

            return minutes + " minutes";
        }

        // Removes counters that have not been touched for a day so the table stays small
        public async Task DeleteStaleAsync()
        {
            var cutoff = DateTime.UtcNow - StaleAfter;

            await _database.EmailRateLimits
                .Where(x => x.WindowStart < cutoff)
                .ExecuteDeleteAsync();
        }
    }
}