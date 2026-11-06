using Group_1_SmartHydro_MVC.Filters;
using Group_1_SmartHydro_MVC.Hubs;
using Group_1_SmartHydro_MVC.Middleware;
using Group_1_SmartHydro_MVC.Patterns.Factory;
using Group_1_SmartHydro_MVC.Patterns.Observer;
using Group_1_SmartHydro_MVC.Patterns.Singleton;
using Group_1_SmartHydro_MVC.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace Group_1_SmartHydro_MVC
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddHttpContextAccessor();
            builder.Services.AddControllersWithViews(options =>
            {
                options.Filters.Add(new AuthorizeFilter());
                options.Filters.Add<ApiSessionFilter>();
                options.Filters.Add<NoSiteAccessFilter>();
            });

            builder.Services.AddAuthentication(
                    CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.LoginPath = "/Account/Login";
                    options.AccessDeniedPath = "/Account/Login";
                    options.SlidingExpiration = true;
                    options.Events.OnRedirectToLogin = RedirectToPlainLogin;
                    options.Events.OnRedirectToAccessDenied = RedirectToNoAccess;
                });

            builder.Services.AddAuthorization();

            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                options.OnRejected = RejectWithWaitTimeAsync;

                options.AddPolicy("RegisterRateLimit", httpContext => ClientIpLimiter(httpContext));
                options.AddPolicy("ForgotPasswordRateLimit", httpContext => ClientIpLimiter(httpContext));
                options.AddPolicy("ClaimInviteRateLimit", httpContext => ClientIpLimiter(httpContext));
                options.AddPolicy("RegistrationCodeRateLimit", httpContext => CodeAttemptLimiter(httpContext, "registration-code:"));
                options.AddPolicy("CodeVerifyRateLimit", httpContext => CodeAttemptLimiter(httpContext, "code-verify:"));
            });

            builder.Services.AddSignalR();

            builder.Services.AddSingleton<SmartHydroApiHttpClient>();
            builder.Services.AddSingleton<LoginFailureLimiter>();
            builder.Services.AddSingleton<AuthenticationApiClient>();
            builder.Services.AddSingleton<AdminApiClient>();
            builder.Services.AddSingleton<SiteInvitationApiClient>();
            builder.Services.AddSingleton<SiteApiClient>();
            builder.Services.AddSingleton<StructureApiClient>();
            builder.Services.AddSingleton<TelemetryApiClient>();
            builder.Services.AddSingleton<ThresholdApiClient>();
            builder.Services.AddSingleton<ICameraDeviceService, CameraDeviceService>();
            builder.Services.AddSingleton<CameraApiClient>();
            builder.Services.AddScoped<ISiteContext, SiteContext>();

            builder.Services.AddSingleton<IDashboardBuilder, HydroponicDashboardBuilder>();
            builder.Services.AddSingleton<IDashboardBuilder, FodderDashboardBuilder>();
            builder.Services.AddSingleton<IDashboardFactory, DashboardFactory>();

            builder.Services.AddSingleton<ISiteChangeObserver, SignalRSiteChangeObserver>();
            builder.Services.AddSingleton<ISiteChangeObserver, LoggingSiteChangeObserver>();
            builder.Services.AddSingleton<ISiteChangePublisher, SiteChangePublisher>();

            builder.Services.AddSingleton<AlertApiClient>();

            var app = builder.Build();

            app.UseMiddleware<SecurityHeadersMiddleware>();

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Dashboard/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseRouting();

            app.UseRateLimiter();

            app.UseAuthentication();

            app.UseMiddleware<UnknownUrlRedirectMiddleware>();

            app.UseAuthorization();

            app.MapStaticAssets();

            app.MapHub<SiteUpdatesHub>(SiteUpdatesHub.Path);

            app.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Account}/{action=Login}/{id?}")
                .WithStaticAssets();

            app.Run();
        }

        // Limits a form post to 5 attempts per 15 minutes per client IP
        private static RateLimitPartition<string> ClientIpLimiter(HttpContext httpContext)
        {
            var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: clientIp,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(15),
                    QueueLimit = 0
                });
        }

        // Tells a rate-limited visitor exactly how long to wait: a page form post goes back to the same page with the message shown in its error box, and a background request gets the message as JSON
        private static async ValueTask RejectWithWaitTimeAsync(OnRejectedContext context, CancellationToken cancellationToken)
        {
            var httpContext = context.HttpContext;
            var retryAfter = TimeSpan.FromMinutes(15);

            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var leaseRetryAfter))
                retryAfter = leaseRetryAfter;

            var retryAfterSeconds = (int)Math.Ceiling(retryAfter.TotalSeconds);

            if (retryAfterSeconds < 1)
                retryAfterSeconds = 1;

            var message = WaitTime.TooManyAttemptsMessage(retryAfter);
            httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();

            if (IsPageFormPost(httpContext.Request))
            {
                var tempData = httpContext.RequestServices
                    .GetRequiredService<ITempDataDictionaryFactory>()
                    .GetTempData(httpContext);

                tempData[WaitTime.TempDataKey] = message;
                tempData.Save();

                httpContext.Response.Redirect(httpContext.Request.PathBase + httpContext.Request.Path + httpContext.Request.QueryString);
                return;
            }

            await httpContext.Response.WriteAsJsonAsync(new
            {
                success = false,
                message,
                retryAfterSeconds
            }, cancellationToken: cancellationToken);
        }

        // Tells a normal page form submit apart from a background fetch request, which the page scripts read as JSON
        private static bool IsPageFormPost(HttpRequest request)
        {
            if (!HttpMethods.IsPost(request.Method))
                return false;

            if (string.Equals(request.Headers["Sec-Fetch-Mode"], "navigate", StringComparison.OrdinalIgnoreCase))
                return true;

            return request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);
        }

        // Limits code entry and resend posts to 10 attempts per 15 minutes per client IP, keeping a separate count for each group of actions
        private static RateLimitPartition<string> CodeAttemptLimiter(HttpContext httpContext, string partitionPrefix)
        {
            var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: partitionPrefix + clientIp,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(15),
                    QueueLimit = 0
                });
        }

        // Sends a signed-in user who lacks the role for a page back to their home page with a message saying so, and treats background requests as before
        private static Task RedirectToNoAccess(RedirectContext<CookieAuthenticationOptions> context)
        {
            if (string.Equals(context.Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToPlainLogin(context);
            }

            // Goes straight to the structure list, and the flag tells the layout to say why
            context.Response.Redirect(context.Request.PathBase + "/Structure/Index?denied=1");
            return Task.CompletedTask;
        }

        // Sends signed-out users to the login page without a ReturnUrl, returning 401 to background requests instead
        private static Task RedirectToPlainLogin(RedirectContext<CookieAuthenticationOptions> context)
        {
            if (string.Equals(context.Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.Request.PathBase + context.Options.LoginPath);
            return Task.CompletedTask;
        }
    }
}