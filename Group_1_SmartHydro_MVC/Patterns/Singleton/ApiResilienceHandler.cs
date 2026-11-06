using System.Net;
using System.Net.Http.Json;

namespace Group_1_SmartHydro_MVC.Patterns.Singleton
{
    public sealed class ApiResilienceHandler : DelegatingHandler
    {
        private readonly TimeSpan _timeout;
        private readonly ILogger _logger;

        public ApiResilienceHandler(TimeSpan timeout, ILogger logger)
        {
            _timeout = timeout;
            _logger = logger;
        }

        // Sends the request with a time limit and turns an unreachable, slow or abandoned call into a 503 reply, so pages show a load error instead of crashing
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);

            try
            {
                return await base.SendAsync(request, timeout.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Unavailable(request, "The request was cancelled because the page was closed or reloaded.");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("The SmartHydro API did not answer {Method} {Path} within {Seconds} seconds.", request.Method, request.RequestUri?.AbsolutePath, _timeout.TotalSeconds);
                return Unavailable(request, "The SmartHydro API took too long to answer. Please try again.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "The SmartHydro API could not be reached for {Method} {Path}.", request.Method, request.RequestUri?.AbsolutePath);
                return Unavailable(request, "The SmartHydro API could not be reached. Check that it is running and try again.");
            }
        }

        // Builds the 503 reply used when the API could not give a real answer
        private static HttpResponseMessage Unavailable(HttpRequestMessage request, string message)
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                RequestMessage = request,
                Content = JsonContent.Create(new { message })
            };
        }
    }
}