using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Group_1_SmartHydro_MVC.Services;

namespace Group_1_SmartHydro_MVC.Patterns.Singleton
{
    public sealed class SmartHydroApiHttpClient : IDisposable
    {
        public const string SessionRejectedKey = "SmartHydro.ApiSessionRejected";

        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan ConnectionLifetime = TimeSpan.FromMinutes(2);

        private readonly IHttpContextAccessor _httpContextAccessor;

        public SmartHydroApiHttpClient(IConfiguration configuration, IHttpContextAccessor httpContextAccessor, ILogger<SmartHydroApiHttpClient> logger)
        {
            _httpContextAccessor = httpContextAccessor;

            var socketsHandler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = ConnectionLifetime
            };

            var forwardingHandler = new ForwardClientIpHandler(httpContextAccessor, configuration)
            {
                InnerHandler = socketsHandler
            };

            var resilienceHandler = new ApiResilienceHandler(RequestTimeout, logger)
            {
                InnerHandler = forwardingHandler
            };

            Client = new HttpClient(resilienceHandler)
            {
                BaseAddress = BuildBaseUri(configuration["Api:BaseUrl"]),
                Timeout = Timeout.InfiniteTimeSpan
            };
        }

        public HttpClient Client { get; }

        // Sends one request to the SmartHydro API on behalf of the signed-in user, attaching their bearer token and an optional JSON body
        public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string accessToken, object? body, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(method, path);

            if (!string.IsNullOrWhiteSpace(accessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            if (body is not null)
                request.Content = JsonContent.Create(body);

            var response = await Client.SendAsync(request, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && !string.IsNullOrWhiteSpace(accessToken))
                MarkSessionRejected();

            return response;
        }

        // Sends a request the caller has built (for example one with a file upload body) on behalf of the signed-in user, attaching their bearer token
        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string accessToken, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(accessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await Client.SendAsync(request, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && !string.IsNullOrWhiteSpace(accessToken))
                MarkSessionRejected();

            return response;
        }

        // Records on the current request that the API refused the user's token, so the request can end by signing them out
        private void MarkSessionRejected()
        {
            var httpContext = _httpContextAccessor.HttpContext;

            if (httpContext != null)
                httpContext.Items[SessionRejectedKey] = true;
        }

        // Reads a JSON response body into the requested type, returning the fallback when the body is empty, not valid JSON, or cut off part way
        public static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, T fallback, CancellationToken ct)
        {
            try
            {
                var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);

                if (value is null)
                    return fallback;

                return value;
            }
            catch (JsonException)
            {
                return fallback;
            }
            catch (NotSupportedException)
            {
                return fallback;
            }
            catch (OperationCanceledException)
            {
                return fallback;
            }
            catch (HttpRequestException)
            {
                return fallback;
            }
        }

        // Preserve custom errors and ASP.NET ProblemDetails without losing the refusal reason.
        public static async Task<ApiProblem> ReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
        {
            var problem = await ReadJsonAsync(response, new ApiProblem(), ct);
            if (!string.IsNullOrWhiteSpace(problem.Message)) return problem;
            if (!string.IsNullOrWhiteSpace(problem.Detail)) problem.Message = problem.Detail;
            else if (problem.Errors.ValueKind == JsonValueKind.Object)
            {
                var messages = new List<string>();
                foreach (var field in problem.Errors.EnumerateObject())
                {
                    if (field.Value.ValueKind == JsonValueKind.String)
                        messages.Add(field.Value.GetString() ?? "");
                    else if (field.Value.ValueKind == JsonValueKind.Array)
                        messages.AddRange(field.Value.EnumerateArray()
                            .Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? ""));
                }
                problem.Message = string.Join(" ", messages.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Take(3));
            }
            if (string.IsNullOrWhiteSpace(problem.Message)) problem.Message = problem.Title;
            return problem;
        }

        // Releases the shared connection pool when the application shuts down
        public void Dispose()
        {
            Client.Dispose();
        }

        // Builds the API base address from configuration, making sure it ends with a slash so relative paths resolve correctly
        private static Uri BuildBaseUri(string? configuredBaseUrl)
        {
            var baseUrl = configuredBaseUrl;

            if (string.IsNullOrWhiteSpace(baseUrl))
                baseUrl = "http://localhost:5043/";

            if (!baseUrl.EndsWith('/'))
                baseUrl += "/";

            return new Uri(baseUrl);
        }
    }

    public sealed class ApiProblem
    {
        public string? Message { get; set; }
        public string? Field { get; set; }
        public string? Detail { get; set; }
        public string? Title { get; set; }
        public JsonElement Errors { get; set; }
    }
}
