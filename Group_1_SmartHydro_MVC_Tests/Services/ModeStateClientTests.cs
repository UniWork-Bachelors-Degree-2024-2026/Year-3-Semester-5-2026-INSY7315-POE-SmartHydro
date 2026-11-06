using Group_1_SmartHydro_MVC.Services;
using Group_1_SmartHydro_MVC.Tests.TestSupport;

namespace Group_1_SmartHydro_MVC_Tests.Services;

public class ModeStateClientTests
{
    [Fact]
    public async Task CommandStatus_UsesCanonicalMacAndAuthenticatedReadRoute() {
        await using var api = await FakeApiServer.StartAsync();
        api.ResponseBody = """{"commandId":42,"terminal":true,"status":"ACKED","ok":true}""";
        var result = await new TelemetryApiClient(api.CreateClient()).ReadCommandStatusAsync("aabbccddee71", 42, "test-token", default);
        Assert.True(result!.Value.GetProperty("terminal").GetBoolean());
        Assert.EndsWith("AA:BB:CC:DD:EE:71/commands/42", Uri.UnescapeDataString(api.LastPath!));
        Assert.Equal("Bearer test-token", api.LastAuthorization); Assert.Equal("GET", api.LastMethod);
    }
    [Fact]
    public async Task ModeState_UsesCanonicalMacAndUserToken() {
        await using var api = await FakeApiServer.StartAsync();
        api.ResponseBody = """{"requestedMode":1,"reportedMode":0,"status":"waiting"}""";
        var state = await new TelemetryApiClient(api.CreateClient()).GetModeStateAsync("aabbccddee71", "mode-token");
        Assert.NotNull(state); Assert.Equal("waiting", state.Value.GetProperty("status").GetString());
        Assert.Contains("AA:BB:CC:DD:EE:71", Uri.UnescapeDataString(api.LastPath!));
        Assert.Equal("Bearer mode-token", api.LastAuthorization);
    }
    [Theory]
    [InlineData(404)]
    [InlineData(503)]
    public async Task UnavailableModeState_DoesNotInventAnActiveMode(int status) {
        await using var api = await FakeApiServer.StartAsync(); api.StatusCode = status;
        Assert.Null(await new TelemetryApiClient(api.CreateClient()).GetModeStateAsync("AABBCCDDEE71", "mode-token"));
    }
}
