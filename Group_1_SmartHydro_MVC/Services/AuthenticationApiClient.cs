using Group_1_SmartHydro_MVC.Patterns.Singleton;
using System.Net;
using System.Net.Http.Json;

namespace Group_1_SmartHydro_MVC.Services
{
    public sealed class AuthenticationApiClient
    {
        private const string OperatorRole = "Operator";

        private readonly HttpClient _http;

        public AuthenticationApiClient(SmartHydroApiHttpClient api)
        {
            _http = api.Client;
        }

        // Asks the API to check the email and password and send a one-time code, returning the challenge token that ties the code to this sign-in
        public async Task<OtpRequestOutcome> RequestOtpAsync(string email, string password, CancellationToken ct = default)
        {
            using var response = await _http.PostAsJsonAsync("api/auth/request-otp", new { email, password }, ct);

            if (response.IsSuccessStatusCode)
            {
                var started = await SmartHydroApiHttpClient.ReadJsonAsync(response, new OtpChallenge(), ct);

                if (string.IsNullOrWhiteSpace(started.ChallengeToken))
                    return new OtpRequestOutcome(OtpRequestResult.Failed, TimeSpan.Zero, string.Empty);

                return new OtpRequestOutcome(OtpRequestResult.Sent, TimeSpan.Zero, started.ChallengeToken);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return new OtpRequestOutcome(OtpRequestResult.LockedOut, WaitTime.Read(response), string.Empty);

            if (response.StatusCode == HttpStatusCode.Forbidden)
                return new OtpRequestOutcome(await ReadForbiddenReasonAsync(response, ct), TimeSpan.Zero, string.Empty);

            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.BadRequest)
                return new OtpRequestOutcome(OtpRequestResult.InvalidCredentials, TimeSpan.Zero, string.Empty);

            return new OtpRequestOutcome(OtpRequestResult.Failed, TimeSpan.Zero, string.Empty);
        }

        // Tells a disabled account apart from one still awaiting approval, using the code the API adds for disabled accounts
        private static async Task<OtpRequestResult> ReadForbiddenReasonAsync(HttpResponseMessage response, CancellationToken ct)
        {
            var problem = await SmartHydroApiHttpClient.ReadJsonAsync(response, new ForbiddenReason(), ct);

            if (string.Equals(problem.Code, "AccountDisabled", StringComparison.Ordinal))
                return OtpRequestResult.Disabled;

            return OtpRequestResult.PendingApproval;
        }

        // Asks the API to resend the one-time code for the sign-in identified by the challenge token
        public async Task<AttemptOutcome> ResendOtpAsync(string email, string challengeToken, CancellationToken ct = default)
        {
            using var response = await _http.PostAsJsonAsync("api/auth/resend-otp", new { email, challengeToken }, ct);
            return ToAttemptOutcome(response);
        }

        // Asks the API to email a password reset code
        public async Task<AttemptOutcome> RequestPasswordResetAsync(string email, CancellationToken ct = default)
        {
            using var response = await _http.PostAsJsonAsync("api/auth/forgot-password", new { email }, ct);
            return ToAttemptOutcome(response);
        }

        // Maps a response that carries no data onto succeeded, rate limited with the wait the API asked for, or failed
        private static AttemptOutcome ToAttemptOutcome(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
                return new AttemptOutcome(ApiAttemptResult.Succeeded, TimeSpan.Zero);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return new AttemptOutcome(ApiAttemptResult.RateLimited, WaitTime.Read(response));

            return new AttemptOutcome(ApiAttemptResult.Failed, TimeSpan.Zero);
        }

        // Exchanges a password reset code for a reset token, reporting a wrong or expired code and a rate limit separately
        public async Task<ResetCodeOutcome> VerifyResetCodeAsync(string email, string code, CancellationToken ct = default)
        {
            using var response = await _http.PostAsJsonAsync("api/auth/verify-reset-code", new { email, code }, ct);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return new ResetCodeOutcome(ApiAttemptResult.RateLimited, null, WaitTime.Read(response));

            if (!response.IsSuccessStatusCode)
                return new ResetCodeOutcome(ApiAttemptResult.Failed, null, TimeSpan.Zero);

            var result = await SmartHydroApiHttpClient.ReadJsonAsync(response, new ResetCodeResult(), ct);

            if (string.IsNullOrWhiteSpace(result.ResetToken))
                return new ResetCodeOutcome(ApiAttemptResult.Failed, null, TimeSpan.Zero);

            return new ResetCodeOutcome(ApiAttemptResult.Succeeded, result.ResetToken, TimeSpan.Zero);
        }

        // Sets a new password using a verified reset token
        public async Task<PasswordResetOutcome> ResetPasswordAsync(string email, string resetToken, string newPassword, CancellationToken ct = default)
        {
            using var response = await _http.PostAsJsonAsync("api/auth/reset-password", new { email, resetToken, newPassword }, ct);

            if (response.IsSuccessStatusCode)
                return new PasswordResetOutcome(PasswordResetResult.Succeeded, TimeSpan.Zero);

            if (response.StatusCode == HttpStatusCode.Conflict)
                return new PasswordResetOutcome(PasswordResetResult.PasswordReused, TimeSpan.Zero);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return new PasswordResetOutcome(PasswordResetResult.RateLimited, WaitTime.Read(response));

            if (response.StatusCode == HttpStatusCode.BadRequest)
                return new PasswordResetOutcome(PasswordResetResult.InvalidToken, TimeSpan.Zero);

            return new PasswordResetOutcome(PasswordResetResult.Failed, TimeSpan.Zero);
        }

        // Verifies the one-time code for the sign-in identified by the challenge token and returns the issued JWT, reporting a rejected code and a rate limit separately
        public async Task<VerifyOtpOutcome> VerifyOtpAsync(string email, string challengeToken, string code, bool rememberMe, CancellationToken ct = default)
        {
            using var response = await _http.PostAsJsonAsync("api/auth/verify-otp", new { email, challengeToken, code, rememberMe }, ct);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return new VerifyOtpOutcome(ApiAttemptResult.RateLimited, null, WaitTime.Read(response));

            if (!response.IsSuccessStatusCode)
                return new VerifyOtpOutcome(ApiAttemptResult.Failed, null, TimeSpan.Zero);

            var auth = await SmartHydroApiHttpClient.ReadJsonAsync(response, new AuthResult(), ct);

            if (string.IsNullOrWhiteSpace(auth.Token))
                return new VerifyOtpOutcome(ApiAttemptResult.Failed, null, TimeSpan.Zero);

            return new VerifyOtpOutcome(ApiAttemptResult.Succeeded, auth, TimeSpan.Zero);
        }

        // Starts a farm operator sign-up; the API emails a verification code and returns the token that ties the code to this sign-up
        public async Task<RegisterOutcome> RegisterOperatorAsync(string email, string password, string farmName, string fullName, CancellationToken ct = default)
        {
            using var response = await _http.PostAsJsonAsync(
                "api/auth/register",
                new { email, password, farmName, fullName, role = OperatorRole },
                ct);

            if (response.IsSuccessStatusCode)
            {
                var started = await SmartHydroApiHttpClient.ReadJsonAsync(response, new RegisterStarted(), ct);

                if (string.IsNullOrWhiteSpace(started.VerificationToken))
                    return new RegisterOutcome(RegisterResult.Failed, string.Empty, TimeSpan.Zero);

                return new RegisterOutcome(RegisterResult.CodeSent, started.VerificationToken, TimeSpan.Zero);
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
                return new RegisterOutcome(RegisterResult.InvalidInput, string.Empty, TimeSpan.Zero);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return new RegisterOutcome(RegisterResult.RateLimited, string.Empty, WaitTime.Read(response));

            return new RegisterOutcome(RegisterResult.Failed, string.Empty, TimeSpan.Zero);
        }

        // Sends the emailed verification code; when it matches, the API submits the operator account for administrator review
        public async Task<VerifyRegistrationOutcome> VerifyRegistrationAsync(string email, string verificationToken, string code, CancellationToken ct = default)
        {
            using var response = await _http.PostAsJsonAsync(
                "api/auth/verify-registration",
                new { email, verificationToken, code },
                ct);

            if (response.IsSuccessStatusCode)
                return new VerifyRegistrationOutcome(VerifyRegistrationResult.PendingApproval, TimeSpan.Zero);

            if (response.StatusCode == HttpStatusCode.BadRequest)
                return new VerifyRegistrationOutcome(VerifyRegistrationResult.InvalidCode, TimeSpan.Zero);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return new VerifyRegistrationOutcome(VerifyRegistrationResult.RateLimited, WaitTime.Read(response));

            return new VerifyRegistrationOutcome(VerifyRegistrationResult.Failed, TimeSpan.Zero);
        }

        // Asks the API to email a new verification code for the sign-up in progress
        public async Task<AttemptOutcome> ResendRegistrationCodeAsync(string email, string verificationToken, CancellationToken ct = default)
        {
            using var response = await _http.PostAsJsonAsync(
                "api/auth/resend-registration-code",
                new { email, verificationToken },
                ct);

            return ToAttemptOutcome(response);
        }

        // Sends POST /api/auth/claim-invite-web so the invited worker's account is created and a Worker JWT is returned
        public async Task<ClaimInviteOutcome> ClaimInviteWebAsync(string email, string token, string newPassword, string fullName, CancellationToken ct = default)
        {
            using var response = await _http.PostAsJsonAsync(
                "api/auth/claim-invite-web",
                new { email, token, newPassword, fullName },
                ct);

            if (response.IsSuccessStatusCode)
            {
                var auth = await response.Content.ReadFromJsonAsync<AuthResult>(cancellationToken: ct);

                if (auth is null || string.IsNullOrWhiteSpace(auth.Token))
                    return new ClaimInviteOutcome(ClaimInviteResult.Failed, null, TimeSpan.Zero);

                return new ClaimInviteOutcome(ClaimInviteResult.Claimed, auth, TimeSpan.Zero);
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
                return new ClaimInviteOutcome(ClaimInviteResult.InvalidOrExpired, null, TimeSpan.Zero);

            if (response.StatusCode == HttpStatusCode.Conflict)
                return new ClaimInviteOutcome(ClaimInviteResult.AccountExists, null, TimeSpan.Zero);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return new ClaimInviteOutcome(ClaimInviteResult.RateLimited, null, WaitTime.Read(response));

            return new ClaimInviteOutcome(ClaimInviteResult.Failed, null, TimeSpan.Zero);
        }

        // Claims a worker invitation with its 8-character access code instead of the emailed link's token,
        // for someone who wants to type the code in by hand rather than open the invite email on this device
        public async Task<ClaimInviteOutcome> ClaimInviteCodeAsync(string email, string code, string newPassword, string fullName, CancellationToken ct = default)
        {
            using var response = await _http.PostAsJsonAsync(
                "api/auth/claim-invite-code",
                new { email, code, newPassword, fullName },
                ct);

            if (response.IsSuccessStatusCode)
            {
                var auth = await response.Content.ReadFromJsonAsync<AuthResult>(cancellationToken: ct);

                if (auth is null || string.IsNullOrWhiteSpace(auth.Token))
                    return new ClaimInviteOutcome(ClaimInviteResult.Failed, null, TimeSpan.Zero);

                return new ClaimInviteOutcome(ClaimInviteResult.Claimed, auth, TimeSpan.Zero);
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
                return new ClaimInviteOutcome(ClaimInviteResult.InvalidOrExpired, null, TimeSpan.Zero);

            if (response.StatusCode == HttpStatusCode.Conflict)
                return new ClaimInviteOutcome(ClaimInviteResult.AccountExists, null, TimeSpan.Zero);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return new ClaimInviteOutcome(ClaimInviteResult.RateLimited, null, WaitTime.Read(response));

            return new ClaimInviteOutcome(ClaimInviteResult.Failed, null, TimeSpan.Zero);
        }
    }

    public static class WaitTime
    {
        public const string TempDataKey = "RateLimited.Message";

        private static readonly TimeSpan Fallback = TimeSpan.FromMinutes(15);

        // Reads how long the API asked the caller to wait from the Retry-After header, falling back to the longest window when it did not say
        public static TimeSpan Read(HttpResponseMessage response)
        {
            var retryAfter = response.Headers.RetryAfter;

            if (retryAfter is null || retryAfter.Delta is null || retryAfter.Delta.Value <= TimeSpan.Zero)
                return Fallback;

            return retryAfter.Delta.Value;
        }

        // Words a wait time for a message, in whole seconds under a minute and otherwise rounded up to whole minutes
        public static string Describe(TimeSpan wait)
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

        // Builds the message shown when a limit is reached, telling the user exactly how long to wait
        public static string TooManyAttemptsMessage(TimeSpan wait)
        {
            return "Too many attempts. Please try again in " + Describe(wait) + ".";
        }
    }

    public enum ClaimInviteResult
    {
        Claimed,
        InvalidOrExpired,
        AccountExists,
        RateLimited,
        Failed
    }

    public sealed record ClaimInviteOutcome(ClaimInviteResult Result, AuthResult? Auth, TimeSpan RetryAfter);

    public enum OtpRequestResult
    {
        Sent,
        InvalidCredentials,
        LockedOut,
        PendingApproval,
        Disabled,
        Failed
    }

    public sealed record OtpRequestOutcome(OtpRequestResult Result, TimeSpan RetryAfter, string ChallengeToken);

    public sealed class OtpChallenge
    {
        public string? Message { get; set; }
        public string? ChallengeToken { get; set; }
    }

    public sealed class ForbiddenReason
    {
        public string? Code { get; set; }
        public string? Message { get; set; }
    }

    public enum PasswordResetResult
    {
        Succeeded,
        InvalidToken,
        PasswordReused,
        RateLimited,
        Failed
    }

    public sealed record PasswordResetOutcome(PasswordResetResult Result, TimeSpan RetryAfter);

    public enum ApiAttemptResult
    {
        Succeeded,
        RateLimited,
        Failed
    }

    public sealed record AttemptOutcome(ApiAttemptResult Result, TimeSpan RetryAfter);

    public sealed record ResetCodeOutcome(ApiAttemptResult Result, string? ResetToken, TimeSpan RetryAfter);

    public sealed record VerifyOtpOutcome(ApiAttemptResult Result, AuthResult? Auth, TimeSpan RetryAfter);

    public enum RegisterResult
    {
        CodeSent,
        InvalidInput,
        RateLimited,
        Failed
    }

    public sealed record RegisterOutcome(RegisterResult Result, string VerificationToken, TimeSpan RetryAfter);

    public sealed class RegisterStarted
    {
        public string? Message { get; set; }
        public string? VerificationToken { get; set; }
    }

    public enum VerifyRegistrationResult
    {
        PendingApproval,
        InvalidCode,
        RateLimited,
        Failed
    }

    public sealed record VerifyRegistrationOutcome(VerifyRegistrationResult Result, TimeSpan RetryAfter);

    public sealed class ResetCodeResult
    {
        public string ResetToken { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }

    public sealed class AuthResult
    {
        public string Token { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }
}