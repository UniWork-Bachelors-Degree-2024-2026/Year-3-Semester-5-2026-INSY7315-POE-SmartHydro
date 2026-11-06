using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartHydroAPI.Extensions;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Services;

namespace SmartHydroAPI.Controllers
{
    [ApiController]
    [Route("api/auth")]
    [Route("api/Authentication")]
    [AllowAnonymous]
    public class AuthenticationController : ControllerBase
    {
        private const string RegistrationReceivedMessage = "Registration received. Pending administrator review.";
        private const string VerificationCodeSentMessage = "We've sent a 6-digit verification code to your email. Enter it to finish registering.";
        private const string VerificationCodeResentMessage = "A new code has been sent to your email. Check your spam folder if you don't see it.";
        private const string InvalidCodeMessage = "Invalid or expired code.";
        private const string ResetCodeSentMessage = "If this email is linked to a SmartHydro account, a reset code is on its way. Check your spam folder if you don't see it.";
        private static readonly TimeSpan ResponseFloor = TimeSpan.FromMilliseconds(600);

        private readonly AuthenticationService _service;
        private readonly InviteClaimService _claims;
        private readonly EmailRateLimiter _emailLimits;
        private readonly IConfiguration _configuration;

        public AuthenticationController(AuthenticationService service, InviteClaimService claims, EmailRateLimiter emailLimits, IConfiguration configuration)
        {
            _service = service;
            _claims = claims;
            _emailLimits = emailLimits;
            _configuration = configuration;
        }

        // Resolves the caller's IP so sign-in and code limits are counted per client IP and email together
        private string ClientIp()
        {
            return HttpContext.ResolveClientIp(_configuration);
        }

        // Checks the email and password and emails a one-time code, returning one message for either a wrong email or a wrong password, and on success the challenge token that ties the code to this sign-in
        [HttpPost("request-otp")]
        [EnableRateLimiting("LoginRateLimit")]
        public async Task<IActionResult> RequestOtp(RequestOtpDto dto)
        {
            if (!ModelState.IsValid)
                return Unauthorized(new { message = "Invalid email or password." });

            var clientIp = ClientIp();
            var signInLimit = await _emailLimits.TryConsumeAsync(EmailRateLimiter.SignInAction, clientIp, dto.Email, EmailRateLimiter.SignInLimit);

            if (!signInLimit.Allowed)
                return EmailRateLimited(signInLimit.RetryAfter);

            var result = await _service.RequestOtpAsync(dto.Email, dto.Password, clientIp);

            if (result.Status == OtpRequestStatus.LockedOut)
            {
                var retryAfterSeconds = (int)Math.Ceiling(result.RetryAfter.TotalSeconds);
                Response.Headers.RetryAfter = retryAfterSeconds.ToString();

                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    message = $"Too many failed sign-in attempts. Try again in {EmailRateLimiter.DescribeWait(result.RetryAfter)}.",
                    retryAfterSeconds
                });
            }

            if (result.Status == OtpRequestStatus.InvalidCredentials)
                return Unauthorized(new { message = "Invalid email or password." });

            if (result.Status == OtpRequestStatus.PendingApproval)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Your Operator account is pending administrator review. You'll receive an email once it has been approved." });

            if (result.Status == OtpRequestStatus.Disabled)
                return StatusCode(StatusCodes.Status403Forbidden, new { code = "AccountDisabled", message = "Your account has been disabled. Contact your administrator." });

            return Ok(new RequestOtpResponseDto
            {
                Message = "A one-time code has been sent to your email.",
                ChallengeToken = result.ChallengeToken
            });
        }

        // Starts a farm operator sign-up, giving the same response, token and response time whether or not the email was already registered
        [HttpPost("register")]
        [EnableRateLimiting("RegisterRateLimit")]
        public async Task<IActionResult> Register(RegisterDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { message = "Enter your full name, a valid email address, a password of at least 8 characters, and your farm name." });

            if (!string.IsNullOrWhiteSpace(dto.Role) &&
                !string.Equals(dto.Role, UserRoles.Operator, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Only farm operators can register. Workers join through an invitation from their operator." });
            }

            var stopwatch = Stopwatch.StartNew();
            var limit = await _emailLimits.TryConsumeAsync(EmailRateLimiter.RegisterAction, dto.Email, EmailRateLimiter.RegisterLimit);
            var result = RegistrationStartResult.Failed();

            if (limit.Allowed)
                result = _service.RegisterOperator(dto.Email, dto.Password, dto.FarmName, dto.FullName);

            await WaitForResponseFloorAsync(stopwatch);

            if (!limit.Allowed)
                return EmailRateLimited(limit.RetryAfter);

            if (result.Status == RegisterStatus.Failed)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Registration is busy right now. Please try again in a minute." });

            return Ok(new { message = VerificationCodeSentMessage, verificationToken = result.VerificationToken });
        }

        // Checks the emailed verification code and, when it matches, submits the operator account for administrator review
        [HttpPost("verify-registration")]
        [EnableRateLimiting("RegistrationCodeRateLimit")]
        public async Task<IActionResult> VerifyRegistration(VerifyRegistrationDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { message = InvalidCodeMessage });

            var stopwatch = Stopwatch.StartNew();
            var limit = await _emailLimits.TryConsumeAsync(EmailRateLimiter.VerifyRegistrationAction, ClientIp(), dto.Email, EmailRateLimiter.VerifyRegistrationLimit);
            var status = RegistrationVerifyStatus.InvalidCode;

            if (limit.Allowed)
                status = await _service.VerifyRegistrationAsync(dto.Email, dto.VerificationToken, dto.Code);

            await WaitForResponseFloorAsync(stopwatch);

            if (!limit.Allowed)
                return EmailRateLimited(limit.RetryAfter);

            if (status == RegistrationVerifyStatus.Failed)
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Could not create the account. Please try again." });

            if (status == RegistrationVerifyStatus.InvalidCode)
                return BadRequest(new { message = InvalidCodeMessage });

            return Ok(new { message = RegistrationReceivedMessage, status = UserStatus.PendingApproval });
        }

        // Queues a new verification code for a sign-up in progress, giving the same response whether or not the sign-up exists
        [HttpPost("resend-registration-code")]
        [EnableRateLimiting("RegistrationCodeRateLimit")]
        public async Task<IActionResult> ResendRegistrationCode(ResendRegistrationCodeDto dto)
        {
            if (!ModelState.IsValid)
                return Ok(new { message = VerificationCodeResentMessage });

            var stopwatch = Stopwatch.StartNew();
            var limit = await _emailLimits.TryConsumeAsync(EmailRateLimiter.ResendRegistrationAction, dto.Email, EmailRateLimiter.ResendRegistrationLimit);

            if (limit.Allowed)
                _service.ResendRegistrationCode(dto.Email, dto.VerificationToken);

            await WaitForResponseFloorAsync(stopwatch);

            if (!limit.Allowed)
                return EmailRateLimited(limit.RetryAfter);

            return Ok(new { message = VerificationCodeResentMessage });
        }

        // Waits until the minimum response time has passed so every outcome takes the same time to answer
        private static async Task WaitForResponseFloorAsync(Stopwatch stopwatch)
        {
            var remaining = ResponseFloor - stopwatch.Elapsed;

            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining);
        }

        // Builds the response for an email that has hit its limit for an action, telling the user exactly how long to wait, which is the same whether or not the email is registered
        private ObjectResult EmailRateLimited(TimeSpan retryAfter)
        {
            var retryAfterSeconds = (int)Math.Ceiling(retryAfter.TotalSeconds);

            if (retryAfterSeconds < 1)
                retryAfterSeconds = 1;

            Response.Headers.RetryAfter = retryAfterSeconds.ToString();

            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                message = $"Too many attempts. Please try again in {EmailRateLimiter.DescribeWait(retryAfter)}.",
                retryAfterSeconds
            });
        }

        // Resends a one-time code for the sign-in identified by the challenge token, giving the same response whether or not one was sent
        [HttpPost("resend-otp")]
        [EnableRateLimiting("CodeVerifyRateLimit")]
        public async Task<IActionResult> ResendOtp(ResendOtpDto dto)
        {
            if (!ModelState.IsValid)
                return Ok(new { message = VerificationCodeResentMessage });

            var stopwatch = Stopwatch.StartNew();
            var limit = await _emailLimits.TryConsumeAsync(EmailRateLimiter.ResendOtpAction, ClientIp(), dto.Email, EmailRateLimiter.ResendOtpLimit);

            if (limit.Allowed)
                await _service.ResendOtpAsync(dto.Email, dto.ChallengeToken);

            await WaitForResponseFloorAsync(stopwatch);

            if (!limit.Allowed)
                return EmailRateLimited(limit.RetryAfter);

            return Ok(new { message = VerificationCodeResentMessage });
        }

        // Queues a password reset code for the given address, giving the same response and response time whether or not it belongs to an account
        [HttpPost("forgot-password")]
        [EnableRateLimiting("ForgotPasswordRateLimit")]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordDto dto)
        {
            if (!ModelState.IsValid)
                return Ok(new { message = ResetCodeSentMessage });

            var stopwatch = Stopwatch.StartNew();
            var limit = await _emailLimits.TryConsumeAsync(EmailRateLimiter.ForgotPasswordAction, dto.Email, EmailRateLimiter.ForgotPasswordLimit);
            var queued = false;

            if (limit.Allowed)
                queued = _service.RequestPasswordReset(dto.Email);

            await WaitForResponseFloorAsync(stopwatch);

            if (!limit.Allowed)
                return EmailRateLimited(limit.RetryAfter);

            if (!queued)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Password reset is busy right now. Please try again in a minute." });

            return Ok(new { message = ResetCodeSentMessage });
        }

        // Exchanges a valid reset code for a short-lived reset token
        [HttpPost("verify-reset-code")]
        [EnableRateLimiting("CodeVerifyRateLimit")]
        public async Task<ActionResult<VerifyResetCodeResponseDto>> VerifyResetCode(VerifyResetCodeDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { message = InvalidCodeMessage });

            var stopwatch = Stopwatch.StartNew();
            var limit = await _emailLimits.TryConsumeAsync(EmailRateLimiter.VerifyResetCodeAction, ClientIp(), dto.Email, EmailRateLimiter.VerifyResetCodeLimit);
            VerifyResetCodeResponseDto? result = null;

            if (limit.Allowed)
                result = await _service.VerifyResetCodeAsync(dto.Email, dto.Code);

            await WaitForResponseFloorAsync(stopwatch);

            if (!limit.Allowed)
                return EmailRateLimited(limit.RetryAfter);

            if (result is null)
                return BadRequest(new { message = InvalidCodeMessage });

            return Ok(result);
        }

        // Sets a new password using either the verified reset token from the web flow or the emailed 6-digit code the Android app sends
        [HttpPost("reset-password")]
        [EnableRateLimiting("CodeVerifyRateLimit")]
        public async Task<IActionResult> ResetPassword(ResetPasswordDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { message = "Your reset session has expired. Please request a new code." });

            var stopwatch = Stopwatch.StartNew();
            var limit = await _emailLimits.TryConsumeAsync(EmailRateLimiter.ResetPasswordAction, ClientIp(), dto.Email, EmailRateLimiter.ResetPasswordLimit);
            var status = PasswordResetStatus.InvalidToken;
            var usesCode = string.IsNullOrWhiteSpace(dto.ResetToken);

            if (limit.Allowed && usesCode)
                status = await _service.ResetPasswordWithCodeAsync(dto.Email, dto.Code, dto.NewPassword);

            if (limit.Allowed && !usesCode)
                status = await _service.ResetPasswordAsync(dto.Email, dto.ResetToken, dto.NewPassword);

            await WaitForResponseFloorAsync(stopwatch);

            if (!limit.Allowed)
                return EmailRateLimited(limit.RetryAfter);

            if (status == PasswordResetStatus.PasswordReused)
                return Conflict(new { message = "You can't reuse any of your last 5 passwords. Choose a different password." });

            if (status == PasswordResetStatus.InvalidToken && usesCode)
                return BadRequest(new { message = InvalidCodeMessage });

            if (status == PasswordResetStatus.InvalidToken)
                return BadRequest(new { message = "Your reset session has expired. Please request a new code." });

            return Ok(new { message = "Password reset." });
        }

        // Path A: claims a worker invitation with the token from the emailed link, creating the Worker account and returning a JWT
        [HttpPost("claim-invite-web")]
        [EnableRateLimiting(InviteClaimService.RateLimitPolicy)]
        public async Task<ActionResult<AuthResponseDto>> ClaimInviteWeb(ClaimInviteWebDto dto, CancellationToken ct = default)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { message = "Enter your email, the invitation link token and a password of at least 8 characters." });

            var limit = await _emailLimits.TryConsumeAsync(EmailRateLimiter.ClaimInviteAction, dto.Email, EmailRateLimiter.ClaimInviteLimit);

            if (!limit.Allowed)
                return EmailRateLimited(limit.RetryAfter);

            var result = await _claims.ClaimWithTokenAsync(dto.Email, dto.Token, dto.NewPassword, dto.FullName, ct);
            return ClaimResponse(result, "Invalid or expired invitation");
        }

        // Path B: claims a worker invitation with the access code typed into the Android app, creating the Worker account and returning a JWT
        [HttpPost("claim-invite-code")]
        [EnableRateLimiting(InviteClaimService.RateLimitPolicy)]
        public async Task<ActionResult<AuthResponseDto>> ClaimInviteCode(ClaimInviteCodeDto dto, CancellationToken ct = default)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { message = "Enter your email, the access code and a password of at least 8 characters." });

            var limit = await _emailLimits.TryConsumeAsync(EmailRateLimiter.ClaimInviteAction, dto.Email, EmailRateLimiter.ClaimInviteLimit);

            if (!limit.Allowed)
                return EmailRateLimited(limit.RetryAfter);

            var result = await _claims.ClaimWithCodeAsync(dto.Email, dto.Code, dto.NewPassword, dto.FullName, ct);
            return ClaimResponse(result, "Invalid or expired access code");
        }

        // Maps an invitation claim result onto the response shared by both claim paths
        private ActionResult<AuthResponseDto> ClaimResponse(InviteClaimResult result, string invalidMessage)
        {
            if (result.Outcome == InviteClaimOutcome.AccountExists)
                return Conflict(new { message = "An account already exists for this email. Sign in instead, or enter your current password to join this site." });

            if (result.Outcome == InviteClaimOutcome.Invalid || result.Auth is null)
                return BadRequest(new { message = invalidMessage });

            return Ok(result.Auth);
        }

        // Verifies the emailed one-time code together with the challenge token from request-otp and returns a signed JWT
        [HttpPost("verify-otp")]
        [EnableRateLimiting("VerifyOtpRateLimit")]
        public async Task<ActionResult<AuthResponseDto>> VerifyOtp(VerifyOtpDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { message = InvalidCodeMessage });

            var stopwatch = Stopwatch.StartNew();
            var clientIp = ClientIp();
            var limit = await _emailLimits.TryConsumeAsync(EmailRateLimiter.VerifyOtpAction, clientIp, dto.Email, EmailRateLimiter.VerifyOtpLimit);
            AuthResponseDto? result = null;

            if (limit.Allowed)
            {
                result = await _service.VerifyOtpAsync(dto.Email, dto.ChallengeToken, dto.Code, dto.RememberMe);

                // A correct code is not a failed attempt, so it gives its count back and only wrong codes use up the allowance
                if (result is not null)
                    await _emailLimits.RefundAsync(EmailRateLimiter.VerifyOtpAction, clientIp, dto.Email);
            }

            await WaitForResponseFloorAsync(stopwatch);

            if (!limit.Allowed)
                return EmailRateLimited(limit.RetryAfter);

            if (result is null)
                return BadRequest(new { message = InvalidCodeMessage });

            return Ok(result);
        }
    }
}