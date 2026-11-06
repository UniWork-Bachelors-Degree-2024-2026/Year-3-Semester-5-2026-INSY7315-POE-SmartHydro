using Microsoft.EntityFrameworkCore;
using Moq;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Patterns.Observer;
using SmartHydroAPI.Patterns.Repository;
using SmartHydroAPI.Patterns.Strategy;
using SmartHydroAPI.Services;

namespace SmartHydroAPI_Tests.Services;

public class ModeStateTests
{
    const string Mac = "AA:BB:CC:DD:EE:71";
    static ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    static TelemetryService Service(ApplicationDbContext db) {
        var factory = new Mock<ITelemetryStrategyFactory>();
        factory.Setup(x => x.GetStrategy(It.IsAny<string?>())).Returns(new FireBeetleC6TelemetryStrategy());
        return new(new TelemetryRepository(db), new ActuatorRepository(db), factory.Object, Mock.Of<IEventPublisher>(), db);
    }
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task OnlineOrSoftAp_CanRequestLora_WithoutInventingConfirmation(int active) {
        using var db = Database(); db.Devices.Add(new Device { MacAddress = Mac, ReportedMode = active }); await db.SaveChangesAsync();
        var queue = new DeviceCommandQueue(db);
        await queue.EnqueueAsync(Mac, "mode", new() { ["operatingMode"] = 0, ["mode"] = "lora" }, "test");
        var waiting = await ModeStateService.GetAsync(db, Mac);
        Assert.Equal(active, waiting!.ReportedMode); Assert.Equal(0, waiting.RequestedMode); Assert.Equal("waiting", waiting.Status);
        var packet = Assert.Single(await queue.ClaimAsync(deviceMacs: new[] { Mac }));
        Assert.Equal(0, System.Text.Json.JsonSerializer.SerializeToElement(packet).GetProperty("operatingMode").GetInt32());
        await Service(db).IngestAndSyncAsync(new() { DeviceMac = Mac, Sequence = 5, OperatingMode = 0, PreferredMode = 0, ModeReason = 0 });
        var reported = await ModeStateService.GetAsync(db, Mac);
        Assert.Equal(0, reported!.ReportedMode); Assert.Equal("confirmed", reported.Status);
    }
    [Fact]
    public async Task ModeEndpoint_QueuesOneCidAndBroadcastsRequestWithoutReportingSuccess() {
        using var db = Database(); db.Devices.Add(new Device { MacAddress = Mac, ReportedMode = 0 }); await db.SaveChangesAsync();
        var proxy = new Mock<Microsoft.AspNetCore.SignalR.IClientProxy>();
        proxy.Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var clients = new Mock<Microsoft.AspNetCore.SignalR.IHubClients>();
        clients.SetupGet(x => x.All).Returns(proxy.Object); clients.Setup(x => x.Group(It.IsAny<string>())).Returns(proxy.Object);
        var hub = new Mock<Microsoft.AspNetCore.SignalR.IHubContext<SmartHydroAPI.Hubs.ActuatorHub>>(); hub.SetupGet(x => x.Clients).Returns(clients.Object);
        var controller = new SmartHydroAPI.Controllers.DevicesController(new DeviceService(db), hub.Object, db) {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext {
                User = SmartHydroAPI.Tests.TestSupport.TestData.Admin() } } };
        Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(await controller.RequestOnlineMode(Mac, default));
        var command = await db.DeviceCommands.SingleAsync(); Assert.Equal("mode", command.Target); Assert.True(command.CommandId > 0);
        Assert.Equal(0, (await db.Devices.SingleAsync()).ReportedMode);
        proxy.Verify(x => x.SendCoreAsync("ReceiveCommand", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
        proxy.Verify(x => x.SendCoreAsync("SetOperatingMode", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    [Fact]
    public async Task ModeRequest_HasCidPriorityAndDoesNotChangeReportedMode() {
        using var db = Database(); db.Devices.Add(new Device { MacAddress = Mac, ReportedMode = 0 }); await db.SaveChangesAsync();
        var queue = new DeviceCommandQueue(db);
        await queue.EnqueueAsync(Mac, "growLight", new() { ["gl"] = 1 }, "test");
        var mode = await queue.EnqueueAsync(Mac, "mode", new() { ["operatingMode"] = 1, ["mode"] = "online" }, "test");
        Assert.InRange((mode.ExpiresAt - mode.CreatedAt).TotalSeconds, 179, 181);
        var state = await ModeStateService.GetAsync(db, Mac);
        Assert.Equal("waiting", state!.Status); Assert.Equal(0, state.ReportedMode); Assert.Equal(1, state.RequestedMode);
        var wire = Assert.Single(await queue.ClaimAsync()); Assert.Equal(mode.CommandId, wire["cid"]);
    }
    [Theory]
    [InlineData(0, false, "waiting")]
    [InlineData(300, false, "waiting")]
    [InlineData(0, true, "waiting")]
    public async Task OldOrDifferentReports_DoNotConfirmMode(long age, bool uncertain, string expected) {
        using var db = Database(); db.Devices.Add(new Device { MacAddress = Mac, ReportedMode = 0 }); await db.SaveChangesAsync();
        await new DeviceCommandQueue(db).EnqueueAsync(Mac, "mode", new() { ["operatingMode"] = 1 }, "test");
        await Service(db).IngestAndSyncAsync(new() { DeviceMac = Mac, Sequence = 1, Age = age, CaptureTimeUncertain = uncertain,
            OperatingMode = age > 0 || uncertain ? 1 : 0 });
        Assert.Equal(expected, (await ModeStateService.GetAsync(db, Mac))!.Status);
    }
    [Fact]
    public async Task RecentCaptureFromBeforeTheRequest_CannotConfirmIt() {
        using var db = Database(); db.Devices.Add(new Device { MacAddress = Mac }); await db.SaveChangesAsync();
        await new DeviceCommandQueue(db).EnqueueAsync(Mac, "mode", new() { ["operatingMode"] = 1 }, "test");
        await Service(db).IngestAndSyncAsync(new() { DeviceMac = Mac, Age = 5, OperatingMode = 1 });
        Assert.Equal("waiting", (await ModeStateService.GetAsync(db, Mac))!.Status);
    }
    [Fact]
    public async Task ConfirmedMode_SurvivesRefreshAndLaterAutomaticFallback() {
        using var db = Database(); db.Devices.Add(new Device { MacAddress = Mac }); await db.SaveChangesAsync();
        await new DeviceCommandQueue(db).EnqueueAsync(Mac, "mode", new() { ["operatingMode"] = 0 }, "test");
        await Service(db).IngestAndSyncAsync(new() { DeviceMac = Mac, Sequence = 1, OperatingMode = 0, PreferredMode = 0, ModeReason = 0 });
        db.ChangeTracker.Clear();
        Assert.Equal("confirmed", (await ModeStateService.GetAsync(db, Mac))!.Status);
        await Service(db).IngestAndSyncAsync(new() { DeviceMac = Mac, Sequence = 2, OperatingMode = 1, PreferredMode = 0, ModeReason = 1 });
        var state = await ModeStateService.GetAsync(db, Mac);
        Assert.Equal("confirmed", state!.Status); Assert.Equal(1, state.ReportedMode); Assert.Equal(1, state.ReportedModeReason);
    }
    [Fact]
    public async Task AcceptedAck_StillWaitsForReportedMode_AndTimeoutIsNotSuccess() {
        using var db = Database(); db.Devices.Add(new Device { MacAddress = Mac, ReportedMode = 0 }); await db.SaveChangesAsync();
        var queue = new DeviceCommandQueue(db); var command = await queue.EnqueueAsync(Mac, "mode", new() { ["operatingMode"] = 2 }, "test");
        await queue.AcknowledgeAsync(Mac, command.CommandId, 1, 0, "executed", "accepted");
        Assert.Equal("waiting", (await ModeStateService.GetAsync(db, Mac))!.Status);
        command.CreatedAt = DateTime.UtcNow.AddSeconds(-181); await db.SaveChangesAsync();
        Assert.Equal("unconfirmed", (await ModeStateService.GetAsync(db, Mac))!.Status);
    }
    [Fact]
    public async Task SupersededRequest_CannotConfirmTheNewTarget() {
        using var db = Database(); db.Devices.Add(new Device { MacAddress = Mac }); await db.SaveChangesAsync();
        var queue = new DeviceCommandQueue(db);
        var first = await queue.EnqueueAsync(Mac, "mode", new() { ["operatingMode"] = 1 }, "test");
        var second = await queue.EnqueueAsync(Mac, "mode", new() { ["operatingMode"] = 2 }, "test");
        Assert.Equal("SUPERSEDED", first.Status);
        await Service(db).IngestAndSyncAsync(new() { DeviceMac = Mac, OperatingMode = 1 });
        var state = await ModeStateService.GetAsync(db, Mac);
        Assert.Equal(second.CommandId, state!.CommandId); Assert.Equal("waiting", state.Status);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutOfOrderRecentCapture_IsHistoryWithoutRevertingLiveRelays(bool rawWasRolledUp) {
        using var db = Database(); db.Devices.Add(new Device { MacAddress = Mac });
        db.ActuatorStates.Add(new ActuatorState { DeviceMac = Mac, GrowLight = false }); await db.SaveChangesAsync();
        await Service(db).IngestAndSyncAsync(new() { DeviceMac = Mac, Sequence = 1, OperatingMode = 1, GrowLight = false });
        if (rawWasRolledUp) { db.TelemetryRealtime.RemoveRange(db.TelemetryRealtime); await db.SaveChangesAsync(); }
        var delayed = new TelemetryIngestDto { DeviceMac = Mac, Sequence = 2, Age = 10, OperatingMode = 0, GrowLight = true };
        await Service(db).IngestAndSyncAsync(delayed);
        Assert.True(delayed.IsOutOfOrder); Assert.False(delayed.IsLive);
        Assert.Equal(rawWasRolledUp ? 1 : 2, await db.TelemetryRealtime.CountAsync());
        db.ChangeTracker.Clear();
        Assert.False((await db.ActuatorStates.SingleAsync()).GrowLight);
        Assert.Equal(1, (await ModeStateService.GetAsync(db, Mac))!.ReportedMode);
    }
    [Fact]
    public async Task DirectWifiRelayReport_IsPersistedWithoutAnOptimisticCommand() {
        using var db = Database(); db.Devices.Add(new Device { MacAddress = Mac });
        db.ActuatorStates.Add(new ActuatorState { DeviceMac = Mac, GrowLight = false }); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await Service(db).IngestAndSyncAsync(new() { DeviceMac = Mac, OperatingMode = 1, GrowLight = true });
        db.ChangeTracker.Clear(); Assert.True((await db.ActuatorStates.SingleAsync()).GrowLight);
    }
}
