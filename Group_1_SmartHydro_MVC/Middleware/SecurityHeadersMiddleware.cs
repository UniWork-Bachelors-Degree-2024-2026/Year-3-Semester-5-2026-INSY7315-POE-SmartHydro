namespace Group_1_SmartHydro_MVC.Middleware
{
    public class SecurityHeadersMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string _cspHeaderName;

        // Only same-origin scripts, plus Google Fonts and the Font Awesome CDN for styles and fonts
        // Images and connections allow http: so the camera can reach the board on the local network
        private const string ContentSecurityPolicy =
            "default-src 'self'; " +
            "script-src 'self'; " +
            "style-src 'self' https://fonts.googleapis.com https://cdnjs.cloudflare.com; " +
            "style-src-attr 'unsafe-inline'; " +
            "font-src 'self' https://fonts.gstatic.com https://cdnjs.cloudflare.com; " +
            "img-src 'self' data: blob: http: https:; " +
            "media-src 'self' blob:; " +
            "connect-src 'self' http: https: ws: wss:; " +
            "object-src 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self'; " +
            "frame-ancestors 'none'";

        public SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment environment)
        {
            _next = next;

            // Report-only in Development so the policy is logged but nothing is blocked
            _cspHeaderName = environment.IsDevelopment()
                ? "Content-Security-Policy-Report-Only"
                : "Content-Security-Policy";
        }

        public Task InvokeAsync(HttpContext context)
        {
            // Headers are added just before the response starts, and any header already set is kept
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;

                headers.TryAdd(_cspHeaderName, ContentSecurityPolicy);
                headers.TryAdd("X-Content-Type-Options", "nosniff");
                headers.TryAdd("X-Frame-Options", "DENY");
                headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
                headers.TryAdd("Permissions-Policy", "camera=(self), microphone=(), geolocation=()");

                return Task.CompletedTask;
            });

            return _next(context);
        }
    }
}
