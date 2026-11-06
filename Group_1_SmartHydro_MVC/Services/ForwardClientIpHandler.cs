namespace Group_1_SmartHydro_MVC.Services
{
    /// <summary>
    /// Browser requests go through MVC before reaching the API, so without this, the API would
    /// see every visitor as the same IP (this server's) - meaning the rate limits on
    /// login/register/forgot-password would apply to everyone combined, not per person.
    ///
    /// This attaches the real visitor's IP as X-Forwarded-For, plus a shared secret so the API
    /// knows it genuinely came from this server.
    /// </summary>
    public sealed class ForwardClientIpHandler : DelegatingHandler
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IConfiguration _configuration;

        public ForwardClientIpHandler(IHttpContextAccessor httpContextAccessor, IConfiguration configuration)
        {
            _httpContextAccessor = httpContextAccessor;
            _configuration = configuration;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var clientIp = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
            var sharedSecret = _configuration["Gateway:SharedSecret"];

            if (!string.IsNullOrWhiteSpace(clientIp) && !string.IsNullOrWhiteSpace(sharedSecret))
            {
                request.Headers.Remove("X-Forwarded-For");
                request.Headers.Add("X-Forwarded-For", clientIp);

                request.Headers.Remove("X-Gateway-Secret");
                request.Headers.Add("X-Gateway-Secret", sharedSecret);
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
