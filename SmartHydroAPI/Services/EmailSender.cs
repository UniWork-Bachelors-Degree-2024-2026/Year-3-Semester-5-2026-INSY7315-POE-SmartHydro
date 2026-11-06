using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Services
{
    public interface IEmailSender
    {
        Task SendOtpEmailAsync(string toEmail, string code);
        Task SendRegistrationOtpEmailAsync(string toEmail, string code);
        Task SendPasswordResetEmailAsync(string toEmail, string code);
        Task SendAccountApprovedEmailAsync(string toEmail);
        Task SendAccountDeniedEmailAsync(string toEmail);
        Task SendAccountAlreadyRegisteredEmailAsync(string toEmail, string status);
        Task SendRegistrationReceivedEmailAsync(string toEmail);
        Task SendWorkerInvitationEmailAsync(string toEmail, string siteName, string accessCode, string claimUrl);
    }

    public class EmailSender : IEmailSender
    {
        private const int DefaultSmtpPort = 587;

        private readonly IConfiguration _configuration;

        public EmailSender(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // Sends the six-digit sign-in code
        public Task SendOtpEmailAsync(string toEmail, string code)
        {
            return SendAsync(
                toEmail,
                "Your SmartHydro sign-in code",
                BuildOtpEmailHtml(code),
                $"Your SmartHydro sign-in code is: {code}\n\nThis code expires in 10 minutes. If you didn't request this, you can ignore this email.");
        }

        // Sends the six-digit code that proves a new operator owns the email address they are registering with
        public Task SendRegistrationOtpEmailAsync(string toEmail, string code)
        {
            return SendAsync(
                toEmail,
                "Verify your SmartHydro email address",
                RegistrationOtpEmailTemplate.Replace("{{SPACED_CODE}}", code),
                $"Your SmartHydro email verification code is: {code}\n\nEnter it to finish registering. This code expires in 10 minutes. If you didn't try to register, you can ignore this email.");
        }

        // Sends the six-digit password reset code
        public Task SendPasswordResetEmailAsync(string toEmail, string code)
        {
            return SendAsync(
                toEmail,
                "Your SmartHydro password reset code",
                BuildPasswordResetEmailHtml(code),
                $"Your SmartHydro password reset code is: {code}\n\nThis code expires in 10 minutes. If you didn't request this, you can ignore this email.");
        }

        // Tells a farm operator that an administrator approved their account and they can now log in
        public Task SendAccountApprovedEmailAsync(string toEmail)
        {
            return SendAsync(
                toEmail,
                "Your SmartHydro Operator account has been approved",
                AccountApprovedEmailTemplate,
                "Your Operator account has been approved! You can now log in with the email and password you registered with.");
        }

        // Tells a farm operator that an administrator denied their account request
        public Task SendAccountDeniedEmailAsync(string toEmail)
        {
            return SendAsync(
                toEmail,
                "Your SmartHydro account request",
                AccountDeniedEmailTemplate,
                "Your SmartHydro account request was not approved. If you believe this is a mistake, please contact SmartHydro support.");
        }

        // Tells the owner of an already-registered address that someone tried to register with it, worded for the account's real status
        public Task SendAccountAlreadyRegisteredEmailAsync(string toEmail, string status)
        {
            string subject;
            string bodyText;
            string bodyHtml;

            if (status == UserStatus.PendingApproval)
            {
                subject = "You already have a pending registration";
                bodyText = "Someone (possibly you) tried to register again using this email address, but you " +
                    "already have an account pending approval. You'll receive an email once it's been reviewed. " +
                    "If this wasn't you, you can safely ignore this email.";
                bodyHtml = bodyText;
            }
            else if (status == UserStatus.Denied)
            {
                subject = "Registration attempt for a previously reviewed email";
                bodyText = "Someone tried to register using this email address. If you believe this is an error " +
                    "or want to reapply, please contact SmartHydro support. If this wasn't you, you can safely " +
                    "ignore this email.";
                bodyHtml = bodyText;
            }
            else
            {
                subject = "Account already registered";
                bodyText = "Someone tried to register a new account using this email address. If this was you, " +
                    "you can sign in instead. If you forgot your password, use the 'Forgot password' link. If " +
                    "you didn't request this, you can safely ignore this email.";
                bodyHtml = "Someone tried to register a new account using this email address. If this was you, " +
                    "you can sign in instead. If you forgot your password, use the \"Forgot password\" link on " +
                    "the sign-in page. If you didn't request this, you can safely ignore this email.";
            }

            return SendAsync(
                toEmail,
                subject,
                BuildAccountAlreadyRegisteredEmailHtml(subject, bodyHtml),
                bodyText);
        }

        // Confirms to a new farm operator that their registration was received and is pending administrator review
        public Task SendRegistrationReceivedEmailAsync(string toEmail)
        {
            return SendAsync(
                toEmail,
                "Your SmartHydro registration is pending review",
                RegistrationReceivedEmailTemplate,
                "Registration received. Your SmartHydro Operator account is pending administrator review. " +
                "We'll email you once it has been approved or denied.");
        }

        // Sends the worker invitation containing the secure website setup link and an invite code that works on both the website and the Android app
        public Task SendWorkerInvitationEmailAsync(
            string toEmail,
            string siteName,
            string accessCode,
            string claimUrl)
        {
            var safeSiteName = System.Net.WebUtility.HtmlEncode(siteName);
            var safeCode = System.Net.WebUtility.HtmlEncode(accessCode);
            var safeClaimUrl = System.Net.WebUtility.HtmlEncode(claimUrl);

            var html = WorkerInvitationEmailTemplate
                .Replace("{{SITE_NAME}}", safeSiteName)
                .Replace("{{CLAIM_URL}}", safeClaimUrl)
                .Replace("{{ACCESS_CODE}}", safeCode);

            var text =
                $"You've been invited to join {siteName} on SmartHydro as a Worker.\n\n" +
                $"Set up your account on the SmartHydro website: {claimUrl}\n\n" +
                $"Or use your invite code: {accessCode}\n\n" +
                "Your invite code works on both the SmartHydro website and the SmartHydro Android app:\n" +
                "- Website: on the sign-in page, click \"Have an invite code? Join your team\".\n" +
                "- Android app: on the sign-in screen, tap \"Have an invite code?\".\n\n" +
                "This invitation expires in 24 hours. If you did not expect it, you can ignore this email.";

            return SendAsync(toEmail, $"You're invited to SmartHydro - {siteName}", html, text);
        }

        // Builds the message and delivers it through the configured SMTP relay (Brevo) over STARTTLS
        private async Task SendAsync(string toEmail, string subject, string htmlBody, string textBody)
        {
            var smtp = _configuration.GetSection("Smtp");
            var fromEmail = smtp["FromEmail"] ?? "noreply@smarthydro.io";
            var fromName = smtp["FromName"] ?? "SmartHydro";
            var host = smtp["Host"] ?? "localhost";
            var username = smtp["Username"] ?? string.Empty;
            var password = smtp["Password"] ?? string.Empty;

            var port = DefaultSmtpPort;
            if (int.TryParse(smtp["Port"], out var configuredPort))
                port = configuredPort;

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(fromName, fromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;
            message.Body = new BodyBuilder
            {
                HtmlBody = htmlBody,
                TextBody = textBody
            }.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, SecureSocketOptions.StartTls);

            if (!string.IsNullOrEmpty(username))
                await client.AuthenticateAsync(username, password);

            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }

        // Fills the sign-in code template
        private static string BuildOtpEmailHtml(string code)
        {
            return OtpEmailTemplate
                .Replace("{{CODE}}", code)
                .Replace("{{SPACED_CODE}}", code);
        }

        // Fills the password reset code template
        private static string BuildPasswordResetEmailHtml(string code)
        {
            return PasswordResetEmailTemplate
                .Replace("{{CODE}}", code)
                .Replace("{{SPACED_CODE}}", code);
        }

        // Fills the already-registered template with the heading and body chosen for the account's status
        private static string BuildAccountAlreadyRegisteredEmailHtml(string heading, string body)
        {
            return AccountAlreadyRegisteredEmailTemplate
                .Replace("{{HEADING}}", System.Net.WebUtility.HtmlEncode(heading))
                .Replace("{{BODY}}", System.Net.WebUtility.HtmlEncode(body));
        }

        private const string WorkerInvitationEmailTemplate = """
            <!DOCTYPE html>
            <html>
            <body style="margin:0;padding:40px 0;background:#f4f6f5;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Arial,sans-serif;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                <tr><td align="center">
                  <table role="presentation" width="480" cellpadding="0" cellspacing="0" style="background:#fff;border-radius:12px;overflow:hidden;">
                    <tr><td style="background:#1f6b3f;padding:28px 40px;color:#fff;font-size:20px;font-weight:700;">SmartHydro</td></tr>
                    <tr><td style="padding:40px 40px 20px;">
                      <h1 style="margin:0 0 10px;color:#14251a;font-size:22px;">You're invited to SmartHydro</h1>
                      <p style="margin:0;color:#5b6b62;line-height:1.5;">
                        You have been invited to join <strong>{{SITE_NAME}}</strong> as a Worker. Set up your account using either option below.
                      </p>
                    </td></tr>
                    <tr><td style="padding:0 40px 24px;">
                      <p style="margin:0 0 10px;color:#5b6b62;">Set up your account on the SmartHydro website:</p>
                      <a href="{{CLAIM_URL}}" style="display:inline-block;background:#1f6b3f;color:#fff;text-decoration:none;padding:12px 20px;border-radius:8px;font-weight:600;">
                        Set Up on the Website
                      </a>
                    </td></tr>
                    <tr><td style="padding:0 40px 28px;">
                      <p style="margin:0 0 8px;color:#5b6b62;">Or use your invite code:</p>
                      <div style="background:#f0f7f2;border:1px solid #d9ebe0;border-radius:10px;padding:18px;text-align:center;">
                        <span style="font-size:30px;font-weight:700;letter-spacing:5px;color:#1f6b3f;font-family:'Courier New',monospace;">{{ACCESS_CODE}}</span>
                      </div>
                      <p style="margin:14px 0 6px;color:#5b6b62;">Your invite code works on both the SmartHydro website and the SmartHydro Android app:</p>
                      <ul style="margin:0;padding-left:20px;color:#5b6b62;line-height:1.6;">
                        <li><strong>Website:</strong> on the sign-in page, click "Have an invite code? Join your team".</li>
                        <li><strong>Android app:</strong> on the sign-in screen, tap "Have an invite code?".</li>
                      </ul>
                      <p style="margin:16px 0 0;font-size:13px;color:#8a968f;">
                        This invitation expires in 24 hours. If you did not expect this invitation, you can safely ignore this email.
                      </p>
                    </td></tr>
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;

        private const string OtpEmailTemplate = """
            <!DOCTYPE html>
            <html>
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            </head>
            <body style="margin:0; padding:0; background-color:#f4f6f5; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#f4f6f5; padding:40px 0;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="480" cellpadding="0" cellspacing="0" style="background-color:#ffffff; border-radius:12px; overflow:hidden; box-shadow:0 2px 8px rgba(0,0,0,0.06);">

                      <tr>
                        <td style="background-color:#1f6b3f; padding:28px 40px;">
                          <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                            <tr>
                              <td style="font-size:20px; font-weight:700; color:#ffffff; letter-spacing:0.5px;">
                                SmartHydro
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>

                      <tr>
                        <td style="padding:40px 40px 8px 40px;">
                          <h1 style="margin:0 0 8px 0; font-size:22px; color:#14251a; font-weight:600;">
                            Your sign-in code
                          </h1>
                          <p style="margin:0; font-size:15px; line-height:1.5; color:#5b6b62;">
                            Enter this code to finish signing into SmartHydro.
                          </p>
                        </td>
                      </tr>

                      <tr>
                        <td style="padding:28px 40px;">
                          <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                            <tr>
                              <td align="center" style="background-color:#f0f7f2; border:1px solid #d9ebe0; border-radius:10px; padding:20px;">
                                <span style="font-size:34px; font-weight:700; letter-spacing:8px; color:#1f6b3f; font-family:'Courier New',monospace;">
                                  {{SPACED_CODE}}
                                </span>
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>

                      <tr>
                        <td style="padding:0 40px 8px 40px;">
                          <p style="margin:0; font-size:13px; line-height:1.6; color:#8a968f;">
                            This code expires in <strong>10 minutes</strong>. If you didn't request this, you can safely ignore this email.
                          </p>
                        </td>
                      </tr>

                      <tr>
                        <td style="padding:32px 40px 28px 40px; border-top:1px solid #eef1ef; margin-top:24px;">
                          <p style="margin:0; font-size:12px; color:#a9b3ac;">
                            SmartHydro - Hydroponics monitoring
                          </p>
                        </td>
                      </tr>

                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        private const string RegistrationOtpEmailTemplate = """
            <!DOCTYPE html>
            <html>
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            </head>
            <body style="margin:0; padding:0; background-color:#f4f6f5; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#f4f6f5; padding:40px 0;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="480" cellpadding="0" cellspacing="0" style="background-color:#ffffff; border-radius:12px; overflow:hidden; box-shadow:0 2px 8px rgba(0,0,0,0.06);">

                      <tr>
                        <td style="background-color:#1f6b3f; padding:28px 40px;">
                          <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                            <tr>
                              <td style="font-size:20px; font-weight:700; color:#ffffff; letter-spacing:0.5px;">
                                SmartHydro
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>

                      <tr>
                        <td style="padding:40px 40px 8px 40px;">
                          <h1 style="margin:0 0 8px 0; font-size:22px; color:#14251a; font-weight:600;">
                            Verify your email address
                          </h1>
                          <p style="margin:0; font-size:15px; line-height:1.5; color:#5b6b62;">
                            Enter this code to finish registering your SmartHydro Operator account. Once verified, your request goes to an administrator for review.
                          </p>
                        </td>
                      </tr>

                      <tr>
                        <td style="padding:28px 40px;">
                          <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                            <tr>
                              <td align="center" style="background-color:#f0f7f2; border:1px solid #d9ebe0; border-radius:10px; padding:20px;">
                                <span style="font-size:34px; font-weight:700; letter-spacing:8px; color:#1f6b3f; font-family:'Courier New',monospace;">
                                  {{SPACED_CODE}}
                                </span>
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>

                      <tr>
                        <td style="padding:0 40px 8px 40px;">
                          <p style="margin:0; font-size:13px; line-height:1.6; color:#8a968f;">
                            This code expires in <strong>10 minutes</strong>. If you didn't try to register, you can safely ignore this email.
                          </p>
                        </td>
                      </tr>

                      <tr>
                        <td style="padding:32px 40px 28px 40px; border-top:1px solid #eef1ef; margin-top:24px;">
                          <p style="margin:0; font-size:12px; color:#a9b3ac;">
                            SmartHydro - Hydroponics monitoring
                          </p>
                        </td>
                      </tr>

                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        private const string PasswordResetEmailTemplate = """
            <!DOCTYPE html>
            <html>
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            </head>
            <body style="margin:0; padding:0; background-color:#f4f6f5; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#f4f6f5; padding:40px 0;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="480" cellpadding="0" cellspacing="0" style="background-color:#ffffff; border-radius:12px; overflow:hidden; box-shadow:0 2px 8px rgba(0,0,0,0.06);">

                      <tr>
                        <td style="background-color:#1f6b3f; padding:28px 40px;">
                          <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                            <tr>
                              <td style="font-size:20px; font-weight:700; color:#ffffff; letter-spacing:0.5px;">
                                SmartHydro
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>

                      <tr>
                        <td style="padding:40px 40px 8px 40px;">
                          <h1 style="margin:0 0 8px 0; font-size:22px; color:#14251a; font-weight:600;">
                            Reset your password
                          </h1>
                          <p style="margin:0; font-size:15px; line-height:1.5; color:#5b6b62;">
                            Enter this code to choose a new SmartHydro password.
                          </p>
                        </td>
                      </tr>

                      <tr>
                        <td style="padding:28px 40px;">
                          <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                            <tr>
                              <td align="center" style="background-color:#f0f7f2; border:1px solid #d9ebe0; border-radius:10px; padding:20px;">
                                <span style="font-size:34px; font-weight:700; letter-spacing:8px; color:#1f6b3f; font-family:'Courier New',monospace;">
                                  {{SPACED_CODE}}
                                </span>
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>

                      <tr>
                        <td style="padding:0 40px 8px 40px;">
                          <p style="margin:0; font-size:13px; line-height:1.6; color:#8a968f;">
                            This code expires in <strong>10 minutes</strong>. If you didn't request this, you can safely ignore this email and your password will stay the same.
                          </p>
                        </td>
                      </tr>

                      <tr>
                        <td style="padding:32px 40px 28px 40px; border-top:1px solid #eef1ef; margin-top:24px;">
                          <p style="margin:0; font-size:12px; color:#a9b3ac;">
                            SmartHydro - Hydroponics monitoring
                          </p>
                        </td>
                      </tr>

                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        private const string AccountApprovedEmailTemplate = """
            <!DOCTYPE html>
            <html>
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            </head>
            <body style="margin:0; padding:0; background-color:#f4f6f5; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#f4f6f5; padding:40px 0;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="480" cellpadding="0" cellspacing="0" style="background-color:#ffffff; border-radius:12px; overflow:hidden; box-shadow:0 2px 8px rgba(0,0,0,0.06);">

                      <tr>
                        <td style="background-color:#1f6b3f; padding:28px 40px;">
                          <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                            <tr>
                              <td style="font-size:20px; font-weight:700; color:#ffffff; letter-spacing:0.5px;">
                                SmartHydro
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>

                      <tr>
                        <td style="padding:40px 40px 8px 40px;">
                          <h1 style="margin:0 0 8px 0; font-size:22px; color:#14251a; font-weight:600;">
                            Your Operator account has been approved!
                          </h1>
                          <p style="margin:0; font-size:15px; line-height:1.5; color:#5b6b62;">
                            An administrator has reviewed your Operator account and approved it. You can now log in using the email and password you registered with. Your starting site has been set up, and you can create more sites and invite workers once you are logged in.
                          </p>
                        </td>
                      </tr>

                      <tr>
                        <td style="padding:28px 40px;">
                          <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                            <tr>
                              <td align="center" style="background-color:#f0f7f2; border:1px solid #d9ebe0; border-radius:10px; padding:20px;">
                                <span style="font-size:16px; font-weight:700; color:#1f6b3f;">
                                  You can now log in.
                                </span>
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>

                      <tr>
                        <td style="padding:32px 40px 28px 40px; border-top:1px solid #eef1ef; margin-top:24px;">
                          <p style="margin:0; font-size:12px; color:#a9b3ac;">
                            SmartHydro - Hydroponics monitoring
                          </p>
                        </td>
                      </tr>

                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        private const string AccountDeniedEmailTemplate = """
            <!DOCTYPE html>
            <html>
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            </head>
            <body style="margin:0; padding:0; background-color:#f4f6f5; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#f4f6f5; padding:40px 0;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="480" cellpadding="0" cellspacing="0" style="background-color:#ffffff; border-radius:12px; overflow:hidden; box-shadow:0 2px 8px rgba(0,0,0,0.06);">

                      <tr>
                        <td style="background-color:#1f6b3f; padding:28px 40px;">
                          <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                            <tr>
                              <td style="font-size:20px; font-weight:700; color:#ffffff; letter-spacing:0.5px;">
                                SmartHydro
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>

                      <tr>
                        <td style="padding:40px 40px 8px 40px;">
                          <h1 style="margin:0 0 8px 0; font-size:22px; color:#14251a; font-weight:600;">
                            Your account request was not approved
                          </h1>
                          <p style="margin:0; font-size:15px; line-height:1.5; color:#5b6b62;">
                            SmartHydro has reviewed your account request and was unable to approve it. If you believe this is a mistake, please contact SmartHydro support.
                          </p>
                        </td>
                      </tr>

                      <tr>
                        <td style="padding:32px 40px 28px 40px; border-top:1px solid #eef1ef; margin-top:24px;">
                          <p style="margin:0; font-size:12px; color:#a9b3ac;">
                            SmartHydro - Hydroponics monitoring
                          </p>
                        </td>
                      </tr>

                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;


        private const string AccountAlreadyRegisteredEmailTemplate = """
            <!DOCTYPE html>
            <html>
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            </head>
            <body style="margin:0; padding:0; background-color:#f4f6f5; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#f4f6f5; padding:40px 0;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="480" cellpadding="0" cellspacing="0" style="background-color:#ffffff; border-radius:12px; overflow:hidden; box-shadow:0 2px 8px rgba(0,0,0,0.06);">

                      <tr>
                        <td style="background-color:#1f6b3f; padding:28px 40px;">
                          <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                            <tr>
                              <td style="font-size:20px; font-weight:700; color:#ffffff; letter-spacing:0.5px;">
                                SmartHydro
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>

                      <tr>
                        <td style="padding:40px 40px 8px 40px;">
                          <h1 style="margin:0 0 8px 0; font-size:22px; color:#14251a; font-weight:600;">
                            {{HEADING}}
                          </h1>
                        </td>
                      </tr>

                      <tr>
                        <td style="padding:0 40px 8px 40px;">
                          <p style="margin:0; font-size:14px; line-height:1.6; color:#5b6b62;">
                            {{BODY}}
                          </p>
                        </td>
                      </tr>

                      <tr>
                        <td style="padding:32px 40px 28px 40px; border-top:1px solid #eef1ef; margin-top:24px;">
                          <p style="margin:0; font-size:12px; color:#a9b3ac;">
                            SmartHydro - Hydroponics monitoring
                          </p>
                        </td>
                      </tr>

                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        private const string RegistrationReceivedEmailTemplate = """
            <!DOCTYPE html>
            <html>
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            </head>
            <body style="margin:0; padding:0; background-color:#f4f6f5; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#f4f6f5; padding:40px 0;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="480" cellpadding="0" cellspacing="0" style="background-color:#ffffff; border-radius:12px; overflow:hidden; box-shadow:0 2px 8px rgba(0,0,0,0.06);">

                      <tr>
                        <td style="background-color:#1f6b3f; padding:28px 40px;">
                          <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                            <tr>
                              <td style="font-size:20px; font-weight:700; color:#ffffff; letter-spacing:0.5px;">
                                SmartHydro
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>

                      <tr>
                        <td style="padding:40px 40px 8px 40px;">
                          <h1 style="margin:0 0 8px 0; font-size:22px; color:#14251a; font-weight:600;">
                            Your registration is pending review
                          </h1>
                          <p style="margin:0; font-size:15px; line-height:1.5; color:#5b6b62;">
                            Thanks for registering with SmartHydro. Your account request has been received and
                            is pending review.
                          </p>
                        </td>
                      </tr>

                      <tr>
                        <td style="padding:20px 40px 8px 40px;">
                          <p style="margin:0; font-size:14px; line-height:1.6; color:#5b6b62;">
                            We'll email you again once your account has been approved or denied. If you didn't
                            request this, you can safely ignore this email.
                          </p>
                        </td>
                      </tr>

                      <tr>
                        <td style="padding:32px 40px 28px 40px; border-top:1px solid #eef1ef; margin-top:24px;">
                          <p style="margin:0; font-size:12px; color:#a9b3ac;">
                            SmartHydro - Hydroponics monitoring
                          </p>
                        </td>
                      </tr>

                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }
}