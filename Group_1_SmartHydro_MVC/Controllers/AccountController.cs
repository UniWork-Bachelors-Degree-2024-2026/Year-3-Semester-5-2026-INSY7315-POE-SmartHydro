using Group_1_SmartHydro_MVC.Filters;
using Group_1_SmartHydro_MVC.Models;
using Group_1_SmartHydro_MVC.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace Group_1_SmartHydro_MVC.Controllers
{
    public class AccountController : Controller
    {
        private const string ClaimTokenKey = "ClaimInvite.Token";
        private const string ClaimEmailKey = "ClaimInvite.Email";
        private const string RegisterEmailKey = "Register.Email";
        private const string RegisterTokenKey = "Register.VerificationToken";
        private const string LoginEmailKey = "Login.Email";
        private const string LoginChallengeKey = "Login.ChallengeToken";
        private const string SignInExpiredMessage = "Your sign-in session has expired. Please sign in again.";

        private readonly AuthenticationApiClient _authentication;
        private readonly ISiteContext _siteContext;
        private readonly LoginFailureLimiter _loginFailures;

        public AccountController(AuthenticationApiClient authentication, ISiteContext siteContext, LoginFailureLimiter loginFailures)
        {
            _authentication = authentication;
            _siteContext = siteContext;
            _loginFailures = loginFailures;
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string? returnUrl, bool otp = false)
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToLocal(returnUrl);

            ViewData["Title"] = "Sign In";
            ViewData["ReturnUrl"] = returnUrl;
            ViewData["OtpStep"] = otp;

            if (TempData[ApiSessionFilter.SignedOutReasonKey] is string signedOutReason)
                ModelState.AddModelError(string.Empty, signedOutReason);

            ShowRateLimitMessage();

            return View(new LoginViewModel());
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(
            LoginViewModel model,
            string? returnUrl,
            CancellationToken ct)
        {
            ViewData["Title"] = "Sign In";
            ViewData["ReturnUrl"] = returnUrl;

            // Only failed sign-ins count towards the limit, so signing in correctly, however often, never uses it up
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var attemptEmail = (model.EmailOrUsername ?? string.Empty).Trim().ToLowerInvariant();
            var attempt = _loginFailures.Check(clientIp, attemptEmail);

            if (!attempt.Allowed)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Too many failed sign-in attempts. Please try again in " + WaitTime.Describe(attempt.RetryAfter) + ", or reset your password.");

                model.Password = null;
                model.OtpCode = null;
                return View(model);
            }

            if (string.IsNullOrWhiteSpace(model.OtpCode))
            {
                if (!ModelState.IsValid)
                    return View(model);

                var email = model.EmailOrUsername!
                    .Trim()
                    .ToLowerInvariant();

                var requestedOutcome = await _authentication.RequestOtpAsync(
                    email,
                    model.Password!,
                    ct);

                var requested = requestedOutcome.Result;

                if (requested == OtpRequestResult.InvalidCredentials)
                {
                    _loginFailures.RecordFailure(clientIp, email);

                    ModelState.AddModelError(
                        string.Empty,
                        "Invalid email or password.");

                    model.Password = null;
                    return View(model);
                }

                if (requested == OtpRequestResult.Disabled)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        "Your account has been disabled. Contact your administrator.");

                    model.Password = null;
                    return View(model);
                }

                if (requested == OtpRequestResult.PendingApproval)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        "Your Operator account is pending administrator review. You'll receive an email once it has been approved.");

                    model.Password = null;
                    return View(model);
                }

                if (requested == OtpRequestResult.LockedOut)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        "Too many sign-in attempts. Please try again in " + WaitTime.Describe(requestedOutcome.RetryAfter) + ", or reset your password.");

                    model.Password = null;
                    return View(model);
                }

                if (requested == OtpRequestResult.Failed)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        "Sign-in is temporarily unavailable. Please try again.");

                    return View(model);
                }

                TempData[LoginEmailKey] = email;
                TempData[LoginChallengeKey] = requestedOutcome.ChallengeToken;

                ViewData["OtpStep"] = true;
                ViewData["OtpMessage"] =
                    "A one-time code has been sent to your email address.";

                model.EmailOrUsername = email;
                model.Password = null;

                return View(model);
            }

            ModelState.Remove(nameof(model.Password));

            var otpEmail = (model.EmailOrUsername ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

            var challengeToken = ReadLoginChallenge(otpEmail);

            if (string.IsNullOrWhiteSpace(challengeToken))
            {
                ModelState.AddModelError(string.Empty, SignInExpiredMessage);

                model.OtpCode = null;
                model.Password = null;
                return View(model);
            }

            var verifiedOutcome = await _authentication.VerifyOtpAsync(
                otpEmail,
                challengeToken,
                model.OtpCode.Trim(),
                model.RememberMe,
                ct);

            if (verifiedOutcome.Result == ApiAttemptResult.RateLimited)
            {
                ModelState.AddModelError(
                    string.Empty,
                    WaitTime.TooManyAttemptsMessage(verifiedOutcome.RetryAfter));

                model.OtpCode = null;
                return View(model);
            }

            var verified = verifiedOutcome.Auth;

            if (verified is null)
            {
                _loginFailures.RecordFailure(clientIp, otpEmail);

                ViewData["OtpStep"] = true;

                ModelState.AddModelError(
                    string.Empty,
                    "Invalid or expired code.");

                return View(model);
            }

            TempData.Remove(LoginEmailKey);
            TempData.Remove(LoginChallengeKey);

            _loginFailures.Reset(clientIp, otpEmail);

            await SignInWithApiTokenAsync(verified, model.RememberMe);

            return RedirectToLocal(returnUrl, verified.Role);
        }

        // Returns the challenge token kept for the sign-in in progress when it belongs to the given email, otherwise an empty string
        private string ReadLoginChallenge(string email)
        {
            var storedEmail = TempData.Peek(LoginEmailKey) as string;
            var storedToken = TempData.Peek(LoginChallengeKey) as string;

            if (string.IsNullOrWhiteSpace(storedEmail) || string.IsNullOrWhiteSpace(storedToken))
                return string.Empty;

            if (!string.Equals(storedEmail, email, StringComparison.Ordinal))
                return string.Empty;

            return storedToken;
        }

        // Path A landing page: moves the emailed token and email off the URL into TempData, then shows the Create Your SmartHydro Account form
        [HttpGet]
        [AllowAnonymous]
        public IActionResult ClaimInvite(string? token, string? email)
        {
            Response.Headers["Referrer-Policy"] = "no-referrer";

            if (!string.IsNullOrWhiteSpace(token))
            {
                TempData[ClaimTokenKey] = token.Trim();
                TempData[ClaimEmailKey] = (email ?? string.Empty).Trim().ToLowerInvariant();
                return RedirectToAction(nameof(ClaimInvite));
            }

            ViewData["Title"] = "Create Your SmartHydro Account";

            var storedToken = TempData.Peek(ClaimTokenKey) as string;
            var storedEmail = TempData.Peek(ClaimEmailKey) as string;

            if (string.IsNullOrWhiteSpace(storedToken) || string.IsNullOrWhiteSpace(storedEmail))
                return View(new ClaimInviteViewModel { LinkMissing = true });

            ShowRateLimitMessage();
            return View(new ClaimInviteViewModel { Email = storedEmail, Token = storedToken });
        }

        // Shows the wait-time message left by the rate limiter when a form post was turned away and sent back to this page
        private void ShowRateLimitMessage()
        {
            if (TempData[WaitTime.TempDataKey] is string rateLimitMessage)
                ModelState.AddModelError(string.Empty, rateLimitMessage);
        }

        // Path A submit: sends the token and new password to the API, which forces the Worker role, then signs the worker in and opens the web dashboard
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("ClaimInviteRateLimit")]
        public async Task<IActionResult> ClaimInvite(ClaimInviteViewModel model, CancellationToken ct)
        {
            Response.Headers["Referrer-Policy"] = "no-referrer";
            ViewData["Title"] = "Create Your SmartHydro Account";

            if (string.IsNullOrWhiteSpace(model.Token) || string.IsNullOrWhiteSpace(model.Email))
                return View(new ClaimInviteViewModel { LinkMissing = true });

            if (!ModelState.IsValid)
                return View(model);

            var outcome = await _authentication.ClaimInviteWebAsync(
                model.Email.Trim().ToLowerInvariant(),
                model.Token.Trim(),
                model.Password!,
                model.FullName!.Trim(),
                ct);

            if (outcome.Result == ClaimInviteResult.Claimed && outcome.Auth is not null)
            {
                TempData.Remove(ClaimTokenKey);
                TempData.Remove(ClaimEmailKey);
                await SignInWithApiTokenAsync(outcome.Auth, false);
                return RedirectToAction("Index", "Dashboard");
            }

            ModelState.AddModelError(string.Empty, ClaimInviteErrorMessage(outcome));
            model.Password = null;
            model.ConfirmPassword = null;
            return View(model);
        }

        // Path B: a worker who'd rather type their invite in by hand than open the email on
        // this device - same account creation as ClaimInvite, just keyed by the access code
        [HttpGet]
        [AllowAnonymous]
        public IActionResult ClaimInviteCode()
        {
            ViewData["Title"] = "Join Your Team";
            ShowRateLimitMessage();
            return View(new ClaimInviteCodeViewModel());
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("ClaimInviteRateLimit")]
        public async Task<IActionResult> ClaimInviteCode(ClaimInviteCodeViewModel model, CancellationToken ct)
        {
            ViewData["Title"] = "Join Your Team";

            if (!ModelState.IsValid)
                return View(model);

            var outcome = await _authentication.ClaimInviteCodeAsync(
                model.Email!.Trim().ToLowerInvariant(),
                model.Code!.Trim(),
                model.Password!,
                model.FullName!.Trim(),
                ct);

            if (outcome.Result == ClaimInviteResult.Claimed && outcome.Auth is not null)
            {
                await SignInWithApiTokenAsync(outcome.Auth, false);
                return RedirectToAction("Index", "Dashboard");
            }

            ModelState.AddModelError(string.Empty, ClaimInviteErrorMessage(outcome));
            model.Password = null;
            model.ConfirmPassword = null;
            return View(model);
        }

        // Chooses the message shown when an invitation cannot be claimed
        private static string ClaimInviteErrorMessage(ClaimInviteOutcome outcome)
        {
            var result = outcome.Result;

            if (result == ClaimInviteResult.InvalidOrExpired)
                return "Invalid or expired invitation. Contact your operator.";

            if (result == ClaimInviteResult.AccountExists)
                return "An account already exists for this email. Sign in instead, or enter your current password to join this site.";

            if (result == ClaimInviteResult.RateLimited)
                return WaitTime.TooManyAttemptsMessage(outcome.RetryAfter);

            return "Could not create your account. Please try again.";
        }

        // Stores the API's JWT, email and role on the auth cookie so later requests can call the API on the user's behalf
        private async Task SignInWithApiTokenAsync(AuthResult auth, bool persistent)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, auth.Email),
                new(ClaimTypes.Email, auth.Email),
                new(ClaimTypes.Role, auth.Role),
                new("access_token", auth.Token),
                new("token_expires_at", auth.ExpiresAt.ToString("O"))
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties
                {
                    IsPersistent = persistent,
                    ExpiresUtc = auth.ExpiresAt
                });
        }

        // Shows the farm operator sign-up form
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Register()
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToLocal(null);

            ViewData["Title"] = "Create Account";
            ShowRateLimitMessage();
            return View(new RegisterViewModel());
        }

        // Submits a farm operator sign-up, which the API holds until the emailed verification code is entered, then opens the verify email step
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("RegisterRateLimit")]
        public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken ct)
        {
            ViewData["Title"] = "Create Account";

            if (!ModelState.IsValid)
                return View(model);

            var email = model.Email!.Trim().ToLowerInvariant();
            var fullName = model.FullName!.Trim();

            if (fullName.Length == 0)
            {
                ModelState.AddModelError(nameof(RegisterViewModel.FullName), "Enter your full name.");
                return View(model);
            }

            var outcome = await _authentication.RegisterOperatorAsync(email, model.Password!, model.FarmName!, fullName, ct);

            if (outcome.Result == RegisterResult.RateLimited)
            {
                ModelState.AddModelError(string.Empty, WaitTime.TooManyAttemptsMessage(outcome.RetryAfter));
                return View(model);
            }

            if (outcome.Result != RegisterResult.CodeSent)
            {
                ModelState.AddModelError(string.Empty, "Could not submit your registration. Please try again.");
                return View(model);
            }

            TempData[RegisterEmailKey] = email;
            TempData[RegisterTokenKey] = outcome.VerificationToken;
            return RedirectToAction(nameof(VerifyEmail));
        }

        // Shows the verify email step for the sign-up started in this browser, sending anyone without one back to the registration form
        [HttpGet]
        [AllowAnonymous]
        public IActionResult VerifyEmail()
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToLocal(null);

            var email = TempData.Peek(RegisterEmailKey) as string;
            var token = TempData.Peek(RegisterTokenKey) as string;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
                return RedirectToAction(nameof(Register));

            ViewData["Title"] = "Verify Your Email";
            ShowRateLimitMessage();
            return View(new VerifyEmailViewModel { Email = email });
        }

        // Checks the emailed code for the sign-up started in this browser and, when it matches, shows that the account is pending administrator review
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("RegistrationCodeRateLimit")]
        public async Task<IActionResult> VerifyEmail(VerifyEmailViewModel model, CancellationToken ct)
        {
            ViewData["Title"] = "Verify Your Email";

            var email = TempData.Peek(RegisterEmailKey) as string;
            var token = TempData.Peek(RegisterTokenKey) as string;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
                return RedirectToAction(nameof(Register));

            model.Email = email;

            if (!ModelState.IsValid)
                return View(model);

            var result = await _authentication.VerifyRegistrationAsync(email, token, model.Code!.Trim(), ct);

            if (result.Result == VerifyRegistrationResult.PendingApproval)
            {
                TempData.Remove(RegisterEmailKey);
                TempData.Remove(RegisterTokenKey);
                ViewData["Email"] = email;
                return View("RegisterPending");
            }

            ModelState.AddModelError(string.Empty, VerifyEmailErrorMessage(result));
            model.Code = null;
            return View(model);
        }

        // Ends the sign-up started in this browser and returns to the sign-in page, for someone who already has an account or changed their mind
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public IActionResult CancelRegistration()
        {
            TempData.Remove(RegisterEmailKey);
            TempData.Remove(RegisterTokenKey);
            return RedirectToAction(nameof(Login));
        }

        // Chooses the message shown when a verification code is not accepted
        private static string VerifyEmailErrorMessage(VerifyRegistrationOutcome outcome)
        {
            if (outcome.Result == VerifyRegistrationResult.InvalidCode)
                return "Invalid or expired code. Check the code and try again, or request a new one.";

            if (outcome.Result == VerifyRegistrationResult.RateLimited)
                return WaitTime.TooManyAttemptsMessage(outcome.RetryAfter);

            return "Could not verify your email right now. Please try again.";
        }

        // Emails a new verification code for the sign-up started in this browser, returning JSON for the verify email page
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("RegistrationCodeRateLimit")]
        public async Task<IActionResult> ResendRegistrationCode(CancellationToken ct)
        {
            var email = TempData.Peek(RegisterEmailKey) as string;
            var token = TempData.Peek(RegisterTokenKey) as string;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
                return Json(new { success = false, expired = true, message = "Your registration session has expired. Please register again." });

            var result = await _authentication.ResendRegistrationCodeAsync(email, token, ct);

            if (result.Result == ApiAttemptResult.RateLimited)
                return Json(new { success = false, message = WaitTime.TooManyAttemptsMessage(result.RetryAfter) });

            if (result.Result == ApiAttemptResult.Failed)
                return Json(new { success = false, message = "Could not resend the code. Please try again." });

            return Json(new { success = true, message = "A new code has been sent to your email. Check your spam folder if you don't see it." });
        }

        // Sends a new one-time code for the sign-in already in progress, returning JSON for the sign-in page
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("CodeVerifyRateLimit")]
        public async Task<IActionResult> ResendOtp(string email, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(email))
                return Json(new { success = false, message = "Missing email address." });

            var normalizedEmail = email.Trim().ToLowerInvariant();
            var challengeToken = ReadLoginChallenge(normalizedEmail);

            if (string.IsNullOrWhiteSpace(challengeToken))
                return Json(new { success = false, reason = "expired", message = SignInExpiredMessage });

            var resent = await _authentication.ResendOtpAsync(normalizedEmail, challengeToken, ct);

            if (resent.Result == ApiAttemptResult.RateLimited)
                return Json(new { success = false, message = WaitTime.TooManyAttemptsMessage(resent.RetryAfter) });

            if (resent.Result == ApiAttemptResult.Failed)
                return Json(new { success = false, reason = "unavailable", message = "Sign-in is temporarily unavailable. Please try again." });

            return Json(new { success = true, message = "A new code has been sent to your email. Check your spam folder if you don't see it." });
        }

        // Sends a password reset code when the email belongs to an approved account, returning JSON for the sign-in page
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("ForgotPasswordRateLimit")]
        public async Task<IActionResult> ForgotPassword(string email, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(email))
                return Json(new { success = false, message = "Enter your account email." });

            var normalizedEmail = email.Trim().ToLowerInvariant();

            var sent = await _authentication.RequestPasswordResetAsync(normalizedEmail, ct);

            if (sent.Result == ApiAttemptResult.RateLimited)
                return Json(new { success = false, message = WaitTime.TooManyAttemptsMessage(sent.RetryAfter) });

            if (sent.Result == ApiAttemptResult.Failed)
                return Json(new { success = false, reason = "unavailable", message = "Sign-in is temporarily unavailable. Please try again." });

            return Json(new { success = true, message = "If this email is linked to a SmartHydro account, a reset code is on its way. Check your spam folder if you don't see it." });
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("CodeVerifyRateLimit")]
        public async Task<IActionResult> VerifyResetCode(string email, string code, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code))
                return Json(new { success = false, message = "Enter the 6-digit code from your email." });

            var normalizedEmail = email.Trim().ToLowerInvariant();

            var resetOutcome = await _authentication.VerifyResetCodeAsync(normalizedEmail, code.Trim(), ct);

            if (resetOutcome.Result == ApiAttemptResult.RateLimited)
                return Json(new { success = false, message = WaitTime.TooManyAttemptsMessage(resetOutcome.RetryAfter) });

            var resetToken = resetOutcome.ResetToken;

            if (resetToken is null)
                return Json(new { success = false, message = "Invalid or expired code." });

            return Json(new { success = true, resetToken, message = "Code verified. Choose a new password." });
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("CodeVerifyRateLimit")]
        public async Task<IActionResult> ResetPassword(string email, string resetToken, string newPassword, string confirmPassword, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(resetToken))
                return Json(new { success = false, expired = true, message = "Your reset session has expired. Please request a new code." });

            if (string.IsNullOrWhiteSpace(newPassword))
                return Json(new { success = false, message = "Enter a new password." });

            if (newPassword.Length < 8)
                return Json(new { success = false, message = "Password must be at least 8 characters." });

            if (newPassword.Length > 128)
                return Json(new { success = false, message = "Password must be 128 characters or fewer." });

            if (newPassword != confirmPassword)
                return Json(new { success = false, message = "Passwords do not match." });

            var normalizedEmail = email.Trim().ToLowerInvariant();

            var resetResult = await _authentication.ResetPasswordAsync(normalizedEmail, resetToken.Trim(), newPassword, ct);
            var result = resetResult.Result;

            if (result == PasswordResetResult.PasswordReused)
                return Json(new { success = false, message = "You can't reuse any of your last 5 passwords. Choose a different password." });

            if (result == PasswordResetResult.InvalidToken)
                return Json(new { success = false, expired = true, message = "Your reset session has expired. Please request a new code." });

            if (result == PasswordResetResult.RateLimited)
                return Json(new { success = false, message = WaitTime.TooManyAttemptsMessage(resetResult.RetryAfter) });

            if (result == PasswordResetResult.Failed)
                return Json(new { success = false, message = "Could not reset the password. Please try again." });

            return Json(new { success = true, message = "Your password has been reset. You can now sign in with your new password." });
        }

        // Tells a worker who has no active site that they have no access yet. Anyone who does have a site, and anyone who is not a worker, is sent on to the structure list, so this page is also where a worker lands when they retry after being added to a site again
        [HttpGet]
        public async Task<IActionResult> NoAccess()
        {
            if (!User.IsInRole("Worker"))
            {
                return RedirectToAction("Index", "Structure");
            }

            var sites = await _siteContext.GetSitesAsync();

            if (_siteContext.SitesLoadFailed || sites.Count > 0)
            {
                return RedirectToAction("Index", "Structure");
            }

            return View();
        }

        // Signs the user out and returns them to the sign-in screen
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);

            return RedirectToAction(nameof(Login));
        }

        // Returns to a local page the user was heading to, otherwise opens the admin dashboard for admins and the structure list for everyone else
        private IActionResult RedirectToLocal(string? returnUrl, string? role = null)
        {
            if (!string.IsNullOrWhiteSpace(returnUrl) &&
                Url.IsLocalUrl(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }

            var isAdmin = User.IsInRole("Admin");
            if (role != null)
            {
                isAdmin = string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);
            }

            if (isAdmin)
            {
                return RedirectToAction("Index", "Admin");
            }

            return RedirectToAction("Index", "Structure");
        }
    }
}