using Group_1_SmartHydro_MVC.Patterns.Singleton;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Group_1_SmartHydro_MVC.Filters
{
    public sealed class ApiSessionFilter : IAsyncActionFilter
    {
        public const string SignedOutReasonKey = "SignedOutReason";

        private const string SignedOutReason = "Your session has ended. Your account may have been disabled, or your sign-in expired. Please sign in again.";

        private readonly ITempDataDictionaryFactory _tempDataFactory;

        public ApiSessionFilter(ITempDataDictionaryFactory tempDataFactory)
        {
            _tempDataFactory = tempDataFactory;
        }

        // Signs the user out and sends them to sign-in when the API rejected their token during this request, for example because an admin disabled the account
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var executed = await next();
            var httpContext = context.HttpContext;

            if (!httpContext.Items.ContainsKey(SmartHydroApiHttpClient.SessionRejectedKey)
                || httpContext.User.Identity?.IsAuthenticated != true
                || httpContext.Response.HasStarted)
            {
                return;
            }

            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            var tempData = _tempDataFactory.GetTempData(httpContext);
            tempData[SignedOutReasonKey] = SignedOutReason;

            executed.Result = new RedirectToActionResult("Login", "Account", null);
        }
    }
}