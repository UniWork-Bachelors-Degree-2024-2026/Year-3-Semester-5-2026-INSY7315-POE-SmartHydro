using Group_1_SmartHydro_MVC.Services;
using Group_1_SmartHydro_MVC.Tests.TestSupport;

namespace Group_1_SmartHydro_MVC.Tests.Services
{
    public class ThresholdApiClientTests
    {
        private const string Token = "test-token";

        private const string SiteJson = """
            [
              {
                "structureId": 1,
                "structureName": "Tower A",
                "structureType": "VERTICAL_TOWER",
                "thresholds": [
                  { "sensorType": "PH", "minValue": 5.5, "maxValue": 6.5 },
                  { "sensorType": "ph", "minValue": 1.0, "maxValue": 2.0 },
                  { "sensorType": "EC", "minValue": 1.0, "maxValue": 2.5 }
                ]
              }
            ]
            """;

        // A successful site request should be turned into structures with their ranges keyed by reading
        [Fact]
        public async Task GetSiteAsync_Success_MapsStructuresAndRanges()
        {
            await using var server = await FakeApiServer.StartAsync();
            server.ResponseBody = SiteJson;
            var client = new ThresholdApiClient(server.CreateClient());

            var result = await client.GetSiteAsync("site-1", Token);

            Assert.NotNull(result);
            var structure = Assert.Single(result);
            Assert.Equal(1, structure.Id);
            Assert.Equal("Tower A", structure.Name);
            Assert.Equal("VERTICAL_TOWER", structure.Type);
            Assert.Equal(2, structure.Values.Count);
            Assert.Equal(5.5m, structure.Values["ph"].Min);
            Assert.Equal(2.5m, structure.Values["EC"].Max);
        }

        // The site request should be a GET to the site thresholds route carrying the user's bearer token
        [Fact]
        public async Task GetSiteAsync_SendsGetWithBearerToken()
        {
            await using var server = await FakeApiServer.StartAsync();
            server.ResponseBody = "[]";
            var client = new ThresholdApiClient(server.CreateClient());

            await client.GetSiteAsync("site-1", Token);

            Assert.Equal("GET", server.LastMethod);
            Assert.Equal("/api/Thresholds/site/site-1", server.LastPath);
            Assert.Equal("Bearer test-token", server.LastAuthorization);
        }

        // A refused request should give null so the page can show a load error
        [Theory]
        [InlineData(401)]
        [InlineData(403)]
        [InlineData(404)]
        [InlineData(500)]
        public async Task GetSiteAsync_ErrorStatus_ReturnsNull(int status)
        {
            await using var server = await FakeApiServer.StartAsync();
            server.StatusCode = status;
            var client = new ThresholdApiClient(server.CreateClient());

            var result = await client.GetSiteAsync("site-1", Token);

            Assert.Null(result);
        }

        // An API that is not running should give null instead of crashing the page
        [Fact]
        public async Task GetSiteAsync_ApiNotRunning_ReturnsNull()
        {
            var client = new ThresholdApiClient(FakeApiServer.CreateClientFor(FakeApiServer.UnusedAddress()));

            var result = await client.GetSiteAsync("site-1", Token);

            Assert.Null(result);
        }

        // A successful reply with a broken body should give an empty list rather than throw
        [Fact]
        public async Task GetSiteAsync_InvalidJson_ReturnsEmptyList()
        {
            await using var server = await FakeApiServer.StartAsync();
            server.ResponseBody = "{ this is not json";
            var client = new ThresholdApiClient(server.CreateClient());

            var result = await client.GetSiteAsync("site-1", Token);

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        // A successful structure request should give its ranges keyed by reading, ignoring letter case
        [Fact]
        public async Task GetStructureAsync_Success_ReturnsRanges()
        {
            await using var server = await FakeApiServer.StartAsync();
            server.ResponseBody = """
                { "structureId": 2, "thresholds": [ { "sensorType": "TEMPERATURE", "minValue": 18, "maxValue": 28 } ] }
                """;
            var client = new ThresholdApiClient(server.CreateClient());

            var result = await client.GetStructureAsync(2, Token);

            Assert.NotNull(result);
            Assert.Equal(18m, result["temperature"].Min);
            Assert.Equal("/api/Thresholds/structure/2", server.LastPath);
        }

        // A refused structure request should give null
        [Fact]
        public async Task GetStructureAsync_NotFound_ReturnsNull()
        {
            await using var server = await FakeApiServer.StartAsync();
            server.StatusCode = 404;
            var client = new ThresholdApiClient(server.CreateClient());

            var result = await client.GetStructureAsync(99, Token);

            Assert.Null(result);
        }

        // Saving should PUT the structure, reading and range as JSON and report success
        [Fact]
        public async Task SaveAsync_Success_SendsPutAndReportsOk()
        {
            await using var server = await FakeApiServer.StartAsync();
            server.ResponseBody = "{}";
            var client = new ThresholdApiClient(server.CreateClient());

            var outcome = await client.SaveAsync(1, "PH", 5.8m, 6.2m, Token);

            Assert.True(outcome.Ok);
            Assert.Equal(200, outcome.StatusCode);
            Assert.Equal("PUT", server.LastMethod);
            Assert.Equal("/api/Thresholds", server.LastPath);
            Assert.Contains("\"structureId\":1", server.LastBody);
            Assert.Contains("\"sensorType\":\"PH\"", server.LastBody);
            Assert.Contains("\"minValue\":5.8", server.LastBody);
            Assert.Contains("\"maxValue\":6.2", server.LastBody);
        }

        // Each refusal from the API should be turned into the right message for the user
        [Theory]
        [InlineData(403, "", "Only operators can change thresholds.")]
        [InlineData(404, "", "This structure was not found, or you cannot change its thresholds.")]
        [InlineData(400, "{\"message\":\"Minimum must be below maximum.\"}", "Minimum must be below maximum.")]
        [InlineData(400, "", "The thresholds could not be saved. Please try again.")]
        [InlineData(500, "", "The thresholds could not be saved. Please try again.")]
        public async Task SaveAsync_Refused_GivesMatchingMessage(int status, string body, string expectedMessage)
        {
            await using var server = await FakeApiServer.StartAsync();
            server.StatusCode = status;
            server.ResponseBody = body;
            var client = new ThresholdApiClient(server.CreateClient());

            var outcome = await client.SaveAsync(1, "PH", 5.8m, 6.2m, Token);

            Assert.False(outcome.Ok);
            Assert.Equal(status, outcome.StatusCode);
            Assert.Equal(expectedMessage, outcome.Message);
        }

        // Saving while the API is not running should fail with the general message instead of crashing
        [Fact]
        public async Task SaveAsync_ApiNotRunning_ReportsFailure()
        {
            var client = new ThresholdApiClient(FakeApiServer.CreateClientFor(FakeApiServer.UnusedAddress()));

            var outcome = await client.SaveAsync(1, "PH", 5.8m, 6.2m, Token);

            Assert.False(outcome.Ok);
            Assert.Equal(503, outcome.StatusCode);
            Assert.Equal("The thresholds could not be saved. Please try again.", outcome.Message);
        }
    }
}