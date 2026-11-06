using System.Net;
using System.Text;
using Group_1_SmartHydro_MVC.Patterns.Singleton;
using Group_1_SmartHydro_MVC.Tests.TestSupport;
using Microsoft.AspNetCore.Http;

namespace Group_1_SmartHydro_MVC.Tests.Patterns
{
    public class SmartHydroApiHttpClientTests
    {
        [Theory]
        [InlineData("{\"detail\":\"Previous dose outcome unknown\",\"title\":\"Invalid Dosing Target\"}", "Previous dose outcome unknown")]
        [InlineData("{\"message\":\"Custom refusal\",\"detail\":\"Other detail\"}", "Custom refusal")]
        [InlineData("{\"errors\":{\"TargetVolumeMl\":[\"Volume outside limit\"],\"Type\":[\"Unknown pump\"]}}", "Volume outside limit Unknown pump")]
        [InlineData("{\"message\":\" \",\"errors\":{\"Type\":\"Unknown pump\"}}", "Unknown pump")]
        [InlineData("{\"title\":\"Validation failed\",\"errors\":[]}", "Validation failed")]
        [InlineData("{\"title\":\"Validation failed\",\"errors\":{\"Type\":[null,3]}}", "Validation failed")]
        [InlineData("invalid", null)]
        [InlineData("null", null)]
        public async Task ReadProblemAsync_PreservesUsefulReason(string body, string? expected)
        {
            using var response = ResponseWithBody(body);
            response.Content.Headers.ContentType = new("application/problem+json");
            var problem = await SmartHydroApiHttpClient.ReadProblemAsync(response, CancellationToken.None);
            Assert.Equal(expected, problem.Message);
        }

        // Builds a response whose body is the given text, for testing the JSON readers
        private static HttpResponseMessage ResponseWithBody(string body)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }

        // A configured address without a trailing slash should get one so relative paths resolve correctly
        [Fact]
        public void Constructor_AddsTrailingSlashToBaseUrl()
        {
            using var client = FakeApiServer.CreateClientFor("http://localhost:5043");

            Assert.Equal("http://localhost:5043/", client.Client.BaseAddress!.ToString());
        }

        // With no address configured the client should fall back to the local development API
        [Fact]
        public void Constructor_NoBaseUrl_UsesLocalDefault()
        {
            using var client = FakeApiServer.CreateClientFor("");

            Assert.Equal("http://localhost:5043/", client.Client.BaseAddress!.ToString());
        }

        // When a token is given it should be sent as a bearer header
        [Fact]
        public async Task SendAsync_WithToken_AttachesBearerHeader()
        {
            await using var server = await FakeApiServer.StartAsync();
            using var client = server.CreateClient();

            using var response = await client.SendAsync(HttpMethod.Get, "api/anything", "abc123", null, CancellationToken.None);

            Assert.Equal("Bearer abc123", server.LastAuthorization);
        }

        // When no token is given no authorization header should be sent
        [Fact]
        public async Task SendAsync_WithoutToken_SendsNoAuthorization()
        {
            await using var server = await FakeApiServer.StartAsync();
            using var client = server.CreateClient();

            using var response = await client.SendAsync(HttpMethod.Get, "api/anything", "", null, CancellationToken.None);

            Assert.Equal("", server.LastAuthorization);
        }

        // A 401 for a signed-in user should flag the current request so the user gets signed out
        [Fact]
        public async Task SendAsync_UnauthorizedWithToken_MarksSessionRejected()
        {
            await using var server = await FakeApiServer.StartAsync();
            server.StatusCode = 401;
            var httpContext = new DefaultHttpContext();
            using var client = server.CreateClient(httpContext);

            using var response = await client.SendAsync(HttpMethod.Get, "api/anything", "expired-token", null, CancellationToken.None);

            Assert.True(httpContext.Items.ContainsKey(SmartHydroApiHttpClient.SessionRejectedKey));
        }

        // A 401 when no one is signed in should not flag the request
        [Fact]
        public async Task SendAsync_UnauthorizedWithoutToken_DoesNotMarkSession()
        {
            await using var server = await FakeApiServer.StartAsync();
            server.StatusCode = 401;
            var httpContext = new DefaultHttpContext();
            using var client = server.CreateClient(httpContext);

            using var response = await client.SendAsync(HttpMethod.Get, "api/anything", "", null, CancellationToken.None);

            Assert.False(httpContext.Items.ContainsKey(SmartHydroApiHttpClient.SessionRejectedKey));
        }

        // An API that is not running should come back as a 503 reply instead of an exception
        [Fact]
        public async Task SendAsync_ApiNotRunning_Returns503()
        {
            using var client = FakeApiServer.CreateClientFor(FakeApiServer.UnusedAddress());

            using var response = await client.SendAsync(HttpMethod.Get, "api/anything", "token", null, CancellationToken.None);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }

        // Valid JSON should be read into the requested type
        [Fact]
        public async Task ReadJsonAsync_ValidJson_ReturnsValue()
        {
            using var response = ResponseWithBody("{\"message\":\"hello\",\"field\":\"email\"}");

            var problem = await SmartHydroApiHttpClient.ReadJsonAsync(response, new ApiProblem(), CancellationToken.None);

            Assert.Equal("hello", problem.Message);
            Assert.Equal("email", problem.Field);
        }

        // A broken, empty or null body should give back the fallback value
        [Theory]
        [InlineData("{ broken")]
        [InlineData("")]
        [InlineData("null")]
        public async Task ReadJsonAsync_BadBody_ReturnsFallback(string body)
        {
            using var response = ResponseWithBody(body);
            var fallback = new ApiProblem { Message = "fallback" };

            var result = await SmartHydroApiHttpClient.ReadJsonAsync(response, fallback, CancellationToken.None);

            Assert.Same(fallback, result);
        }
    }
}
