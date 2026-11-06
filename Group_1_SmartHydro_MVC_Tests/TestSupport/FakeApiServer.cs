using System.Net;
using System.Net.Sockets;
using Group_1_SmartHydro_MVC.Patterns.Singleton;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Group_1_SmartHydro_MVC.Tests.TestSupport
{
    public sealed class FakeApiServer : IAsyncDisposable
    {
        private readonly WebApplication _app;

        public int StatusCode { get; set; } = 200;
        public string ResponseBody { get; set; } = "";
        public string? LastMethod { get; private set; }
        public string? LastPath { get; private set; }
        public string? LastAuthorization { get; private set; }
        public string? LastBody { get; private set; }
        public string BaseUrl { get; private set; } = "";

        // Keeps the running web application so it can be stopped when the test ends
        private FakeApiServer(WebApplication app)
        {
            _app = app;
        }

        // Starts a fake SmartHydro API on a free local port that answers every request with the configured status and body
        public static async Task<FakeApiServer> StartAsync()
        {
            var baseUrl = UnusedAddress();

            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls(baseUrl.TrimEnd('/'));
            builder.Logging.ClearProviders();

            var app = builder.Build();
            var server = new FakeApiServer(app);
            app.Run(server.HandleAsync);

            await app.StartAsync();
            server.BaseUrl = baseUrl;
            return server;
        }

        // Records what the MVC client sent and replies with the configured status and JSON body
        private async Task HandleAsync(HttpContext context)
        {
            LastMethod = context.Request.Method;
            LastPath = context.Request.Path.Value;
            LastAuthorization = context.Request.Headers.Authorization.ToString();

            using var reader = new StreamReader(context.Request.Body);
            LastBody = await reader.ReadToEndAsync();

            context.Response.StatusCode = StatusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(ResponseBody);
        }

        // Builds the real MVC HTTP client pointed at this fake API, with an optional current web request
        public SmartHydroApiHttpClient CreateClient(HttpContext? httpContext = null)
        {
            return CreateClientFor(BaseUrl, httpContext);
        }

        // Builds the real MVC HTTP client pointed at any address, used to simulate an API that is not running
        public static SmartHydroApiHttpClient CreateClientFor(string baseUrl, HttpContext? httpContext = null)
        {
            var settings = new Dictionary<string, string?> { ["Api:BaseUrl"] = baseUrl };
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            var accessor = new HttpContextAccessor { HttpContext = httpContext };

            return new SmartHydroApiHttpClient(configuration, accessor, NullLogger<SmartHydroApiHttpClient>.Instance);
        }

        // Returns the address of a local port that nothing is listening on
        public static string UnusedAddress()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            return $"http://127.0.0.1:{port}/";
        }

        // Stops the fake API when the test is finished
        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}