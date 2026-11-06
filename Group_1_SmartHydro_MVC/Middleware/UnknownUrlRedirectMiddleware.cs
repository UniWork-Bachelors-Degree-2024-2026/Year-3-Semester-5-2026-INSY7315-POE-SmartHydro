namespace Group_1_SmartHydro_MVC.Middleware
{
    public class UnknownUrlRedirectMiddleware
    {
        public const string LastPageCookie = "SmartHydro.LastPage";

        private readonly RequestDelegate _next;

        /// <summary>
        /// Creates the middleware and stores the next step in the request pipeline.
        /// </summary>
        public UnknownUrlRedirectMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        /// <summary>
        /// Redirects browser page requests for URLs that don't exist (404/405) back to the
        /// last page the user viewed, then the linking page, then the Dashboard or Login page.
        /// The last page is remembered in a small cookie.
        /// </summary>
        public async Task InvokeAsync(HttpContext context)
        {
            var isPageNavigation = IsPageNavigation(context.Request);

            if (isPageNavigation)
            {
                context.Response.OnStarting(() =>
                {
                    RememberPage(context);
                    return Task.CompletedTask;
                });
            }

            await _next(context);

            if (!isPageNavigation || context.Response.HasStarted)
                return;

            var status = context.Response.StatusCode;
            if (status != StatusCodes.Status404NotFound && status != StatusCodes.Status405MethodNotAllowed)
                return;

            context.Response.Clear();
            context.Response.Redirect(ResolveRedirectTarget(context));
        }

        /// <summary>
        /// Saves the current page's address in the last-page cookie, but only when the page
        /// loaded successfully as HTML.
        /// </summary>
        private static void RememberPage(HttpContext context)
        {
            var response = context.Response;

            if (response.StatusCode != StatusCodes.Status200OK || !IsHtmlResponse(response))
                return;

            response.Cookies.Append(
                LastPageCookie,
                CurrentPathAndQuery(context.Request),
                new CookieOptions
                {
                    HttpOnly = true,
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = context.Request.IsHttps,
                    Path = "/"
                });
        }

        /// <summary>
        /// Works out where to send the user: the last page in the cookie, otherwise the page that
        /// linked here, otherwise the Dashboard if signed in or the Login page if not.
        /// </summary>
        private static string ResolveRedirectTarget(HttpContext context)
        {
            var current = CurrentPathAndQuery(context.Request);

            if (context.Request.Cookies.TryGetValue(LastPageCookie, out var lastPage) &&
                IsSafeLocalUrl(lastPage) &&
                !string.Equals(lastPage, current, StringComparison.OrdinalIgnoreCase))
            {
                return lastPage!;
            }

            var referer = context.Request.GetTypedHeaders().Referer;
            if (referer != null &&
                string.Equals(referer.Host, context.Request.Host.Host, StringComparison.OrdinalIgnoreCase))
            {
                var refererPath = referer.PathAndQuery;
                if (IsSafeLocalUrl(refererPath) &&
                    !string.Equals(refererPath, current, StringComparison.OrdinalIgnoreCase))
                {
                    return refererPath;
                }
            }

            if (context.User.Identity?.IsAuthenticated == true)
                return "/Dashboard";

            return "/Account/Login";
        }

        /// <summary>
        /// Checks whether the request is the browser loading a full page (typed URL or clicked
        /// link) rather than a script, image, form post or other background request.
        /// </summary>
        private static bool IsPageNavigation(HttpRequest request)
        {
            if (!HttpMethods.IsGet(request.Method))
                return false;

            if (string.Equals(request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
                return false;

            var fetchMode = request.Headers["Sec-Fetch-Mode"].ToString();
            if (!string.IsNullOrEmpty(fetchMode))
            {
                if (!string.Equals(fetchMode, "navigate", StringComparison.OrdinalIgnoreCase))
                    return false;

                var fetchDest = request.Headers["Sec-Fetch-Dest"].ToString();
                return string.IsNullOrEmpty(fetchDest) ||
                       string.Equals(fetchDest, "document", StringComparison.OrdinalIgnoreCase);
            }

            return request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks whether the response being sent back is an HTML page.
        /// </summary>
        private static bool IsHtmlResponse(HttpResponse response)
        {
            if (response.ContentType == null)
                return false;

            return response.ContentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Builds the current request's address, including the path and any query string.
        /// </summary>
        private static string CurrentPathAndQuery(HttpRequest request)
        {
            return $"{request.PathBase}{request.Path}{request.QueryString}";
        }

        /// <summary>
        /// Checks that an address points to a page on this site, blocking addresses like
        /// //evil.com that would send the user to another website.
        /// </summary>
        private static bool IsSafeLocalUrl(string? url)
        {
            if (string.IsNullOrEmpty(url) || url[0] != '/')
                return false;

            if (url.Length == 1)
                return true;

            return url[1] != '/' && url[1] != '\\';
        }
    }
}