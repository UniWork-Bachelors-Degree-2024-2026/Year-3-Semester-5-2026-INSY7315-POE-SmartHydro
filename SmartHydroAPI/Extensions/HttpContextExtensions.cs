namespace SmartHydroAPI.Extensions
{
    public static class HttpContextExtensions
    {
        private const string UnknownClient = "unknown";

        // Resolves the caller's IP, trusting X-Forwarded-For only when the MVC gateway proves itself with the shared secret
        public static string ResolveClientIp(this HttpContext httpContext, IConfiguration configuration)
        {
            var configuredSecret = configuration["Gateway:SharedSecret"];
            var providedSecret = httpContext.Request.Headers["X-Gateway-Secret"].ToString();

            if (!string.IsNullOrEmpty(configuredSecret) &&
                string.Equals(providedSecret, configuredSecret, StringComparison.Ordinal))
            {
                var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].ToString();
                var forwardedIp = forwardedFor.Split(',')[0].Trim();

                if (!string.IsNullOrWhiteSpace(forwardedIp))
                    return forwardedIp;
            }

            var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();

            if (string.IsNullOrWhiteSpace(remoteIp))
                return UnknownClient;

            return remoteIp;
        }
    }
}
