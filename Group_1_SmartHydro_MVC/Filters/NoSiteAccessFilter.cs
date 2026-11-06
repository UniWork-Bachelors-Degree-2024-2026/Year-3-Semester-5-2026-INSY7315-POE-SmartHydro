using Group_1_SmartHydro_MVC.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Group_1_SmartHydro_MVC.Filters
{
    public sealed class NoSiteAccessFilter : IAsyncActionFilter
    {
        private const string NoAccessMessage = "You do not have access to any site right now.";

        private readonly ISiteContext _siteContext;

        public NoSiteAccessFilter(ISiteContext siteContext)
        {
            _siteContext = siteContext;
        }

        // Stops a signed-in worker who belongs to no active site and shows them the no-access page instead. Workers can belong to several operators' sites, so a worker is only stopped when none are left, and never when the site list could not be loaded
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var user = context.HttpContext.User;

            if (user.Identity?.IsAuthenticated != true || !user.IsInRole("Worker") || IsAccountPage(context))
            {
                await next();
                return;
            }

            var sites = await _siteContext.GetSitesAsync();

            if (_siteContext.SitesLoadFailed || sites.Count > 0)
            {
                await next();
                return;
            }

            if (IsBackgroundRequest(context.HttpContext.Request))
            {
                context.Result = new JsonResult(new { ok = false, message = NoAccessMessage }) { StatusCode = StatusCodes.Status403Forbidden };
                return;
            }

            context.Result = new RedirectToActionResult("NoAccess", "Account", null);
        }

        // Lets the sign-in, sign-out and no-access pages through so the worker can always leave or try again
        private static bool IsAccountPage(ActionExecutingContext context)
        {
            var controller = context.RouteData.Values["controller"] as string;
            return string.Equals(controller, "Account", StringComparison.OrdinalIgnoreCase);
        }

        // Tells a background request from the page scripts apart from a normal page visit
        private static bool IsBackgroundRequest(HttpRequest request)
        {
            return string.Equals(request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
        }
    }
}
