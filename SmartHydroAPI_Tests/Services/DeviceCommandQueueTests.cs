using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Moq;
using SmartHydroAPI.Controllers;
using SmartHydroAPI.Data;
using SmartHydroAPI.Hubs;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Patterns.Observer;
using SmartHydroAPI.Patterns.Decorator;
using SmartHydroAPI.Patterns.Repository;
using SmartHydroAPI.Services;
using SmartHydroAPI.Tests.TestSupport;

namespace SmartHydroAPI_Tests.Services;

public class DeviceCommandQueueTests
{
    private const string Mac = "AA:BB:CC:DD:EE:A1";
    private static DbContextOptions<ApplicationDbContext> Options() =>
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    private static async Task Seed(ApplicationDbContext db)
    {
        db.Devices.Add(new Device { MacAddress = Mac, StructureType = "VERTICAL-TOWER" });
        await db.SaveChangesAsync();
    }
    private static Task<DeviceCommand> Queue(DeviceCommandQueue queue, string target = "gl", Guid? doseId = null) =>
        queue.EnqueueAsync(Mac, target, new() { [target] = 1 }, "User-MVC", doseId);

    [Theory]
    [InlineData(1, 2, true)]
    [InlineData(0, 0, false)]
    public async Task MvcRequestStaysPending_UntilNodeReportsItsOutput(int ok, int mask, bool expectedOn)
    {
        using var db = new ApplicationDbContext(Options()); await Seed(db);
        var service = new ActuatorService(new ActuatorRepository(db), Mock.Of<IEventPublisher>());
        var proxy = new Mock<IClientProxy>();
        proxy.Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>();
        clients.Setup(x => x.Group(It.IsAny<string>())).Returns(proxy.Object);
        clients.SetupGet(x => x.All).Returns(proxy.Object);
        var hub = new Mock<IHubContext<ActuatorHub>>(); hub.SetupGet(x => x.Clients).Returns(clients.Object);
        var controller = new ActuatorsController(service, hub.Object, db)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = TestData.Admin() } }
        };

        var result = await controller.SendContinuousCommand(new ActuatorCommandDto { DeviceMac = Mac, GrowLight = true }, default);
        var pending = Assert.IsType<ActuatorStateResponseDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.True(pending.Pending); Assert.False(pending.GrowLight);
        Assert.False((await db.ActuatorStates.SingleAsync()).GrowLight);
        Assert.Empty(await db.ActuatorHistory.ToListAsync());
        proxy.Verify(x => x.SendCoreAsync("OnActuatorStateChanged", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Never);

        var telemetry = new Mock<ITelemetryService>();
        await new TelemetryController(telemetry.Object, db, service, hub.Object).IngestLoraCompact(new()
        {
            DeviceMac = Mac, CommandAck = 1, CommandId = pending.CommandId, CommandOk = ok,
            RelayBitmask = mask, CommandReason = ok == 1 ? "executed" : "safety_blocked"
        }, default);

        Assert.Equal(expectedOn, (await db.ActuatorStates.SingleAsync()).GrowLight);
        Assert.Equal("ACKED", (await db.DeviceCommands.SingleAsync()).Status);
        Assert.Equal(ok == 1, (await db.DeviceCommands.SingleAsync()).Succeeded);
        telemetry.Verify(x => x.IngestAndSyncAsync(It.IsAny<TelemetryIngestDto>(), It.IsAny<CancellationToken>()), Times.Never);
        proxy.Verify(x => x.SendCoreAsync("ReceiveCommandAck", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CommandSurvivesNewContext_AndPollingDoesNotAcknowledgeIt()
    {
        var options = Options();
        int cid;
        using (var db = new ApplicationDbContext(options))
        {
            await Seed(db);
            cid = (await Queue(new DeviceCommandQueue(db))).CommandId;
        }
        using (var db = new ApplicationDbContext(options))
        {
            var payload = Assert.Single(await new DeviceCommandQueue(db).ClaimAsync());
            Assert.Equal(cid, payload["cid"]);
            var saved = await db.DeviceCommands.SingleAsync();
            Assert.Equal("SENT", saved.Status);
            Assert.Null(saved.AckedAt);
        }
    }

    [Fact]
    public async Task PollWaitsForFirstCommand_ThenRetriesSameId_ThenReleasesNextTarget()
    {
        using var db = new ApplicationDbContext(Options()); await Seed(db);
        var queue = new DeviceCommandQueue(db);
        var first = await Queue(queue);
        var second = await Queue(queue, "fc");
        Assert.Equal(first.CommandId, Assert.Single(await queue.ClaimAsync())["cid"]);
        Assert.Empty(await queue.ClaimAsync());
        first.SentAt = DateTime.UtcNow.AddSeconds(-13); await db.SaveChangesAsync();
        Assert.Equal(first.CommandId, Assert.Single(await queue.ClaimAsync())["cid"]);
        Assert.Equal(1, first.RetryCount);
        await queue.AcknowledgeAsync(Mac, first.CommandId, 1, 2, "executed", "accepted");
        Assert.Equal(second.CommandId, Assert.Single(await queue.ClaimAsync())["cid"]);
    }

    [Fact]
    public async Task SupersessionDoesNotReplaceAnotherTargetOrIndependentDose()
    {
        using var db = new ApplicationDbContext(Options()); await Seed(db);
        var queue = new DeviceCommandQueue(db);
        var old = await Queue(queue);
        var fan = await Queue(queue, "fc");
        await Queue(queue);
        Assert.Equal("SUPERSEDED", old.Status);
        Assert.Equal("QUEUED", fan.Status);
        var dose = await Queue(queue, "PH_UP", Guid.NewGuid());
        await Queue(queue, "PH_UP", Guid.NewGuid());
        Assert.Equal("QUEUED", dose.Status);
        Assert.Null(await queue.AcknowledgeAsync(Mac, old.CommandId, 1, 2, "executed", null));
    }

    [Theory]
    [InlineData(1, "accepted", "DISPATCHED")]
    [InlineData(1, "completed", "COMPLETED")]
    [InlineData(1, "cancelled", "FAILED")]
    [InlineData(0, "accepted", "FAILED")]
    public async Task DoseStatusRequiresCommandSpecificOutcome(int ok, string phase, string expected)
    {
        using var db = new ApplicationDbContext(Options()); await Seed(db);
        var dose = new DosingCommand { DeviceMac = Mac, DosingType = "PH_UP" };
        db.DosingCommands.Add(dose); await db.SaveChangesAsync();
        var queue = new DeviceCommandQueue(db);
        var command = await Queue(queue, "PH_UP", dose.Id);
        var result = await queue.AcknowledgeAsync(Mac, command.CommandId, ok, 16, "test", phase);
        Assert.Equal("ACKED", result!.Status);
        Assert.Equal(ok == 1, result.Succeeded);
        Assert.Equal(expected, dose.Status);
        Assert.Equal(expected != "DISPATCHED", dose.ExecutedAt.HasValue);
    }

    [Fact]
    public async Task ExpiredAndMismatchedAcksCannotConfirmExecution()
    {
        using var db = new ApplicationDbContext(Options()); await Seed(db);
        var queue = new DeviceCommandQueue(db);
        var command = await Queue(queue);
        Assert.Null(await queue.AcknowledgeAsync("AA:BB:CC:DD:EE:A2", command.CommandId, 1, 2, null, null));
        Assert.Null(await queue.AcknowledgeAsync(Mac, command.CommandId, 1, 256, null, null));
        command.ExpiresAt = DateTime.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
        Assert.Empty(await queue.ClaimAsync());
        Assert.Equal("EXPIRED", command.Status);
        Assert.Null(await queue.AcknowledgeAsync(Mac, command.CommandId, 1, 2, null, null));
    }

    [Fact]
    public async Task CommandIdsWrapWithoutUsingZero()
    {
        using var db = new ApplicationDbContext(Options()); await Seed(db);
        db.DeviceCommands.Add(new DeviceCommand { Id = 65535, DeviceMac = Mac, CommandId = 65535,
            RequestId = Guid.NewGuid(), Status = "ACKED" });
        await db.SaveChangesAsync();
        var command = await Queue(new DeviceCommandQueue(db));
        Assert.Equal(65536, command.Id);
        Assert.Equal(1, command.CommandId);
    }
}
