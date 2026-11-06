using Group_1_SmartHydro_MVC.Services;
using Group_1_SmartHydro_MVC.Tests.TestSupport;

namespace Group_1_SmartHydro_MVC_Tests.Services;

public class CommandConfirmationTests
{
    [Fact]
    public async Task QueuedCommand_PreservesConfirmedOffStateAndCommandId()
    {
        await using var api = await FakeApiServer.StartAsync();
        api.ResponseBody = """{"growLight":false,"commandId":42,"pending":true}""";
        var result = await new TelemetryApiClient(api.CreateClient())
            .SendCommandAsync("AA:BB:CC:DD:EE:A1", "growLight", true, "test-token");
        Assert.True(result.Ok); Assert.True(result.Pending);
        Assert.False(result.On); Assert.Equal(42, result.CommandId);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"growLight\":true}")]
    public async Task UnconfirmedResponse_IsNotShownAsSuccessfulExecution(string body)
    {
        await using var api = await FakeApiServer.StartAsync(); api.ResponseBody = body;
        var result = await new TelemetryApiClient(api.CreateClient())
            .SendCommandAsync("AA:BB:CC:DD:EE:A1", "growLight", true, "test-token");
        Assert.False(result.Ok); Assert.False(result.On);
    }
}
