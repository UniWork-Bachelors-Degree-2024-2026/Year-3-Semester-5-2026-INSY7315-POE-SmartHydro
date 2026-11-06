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
using SmartHydroAPI.Patterns.Repository;
using SmartHydroAPI.Patterns.Strategy;
using SmartHydroAPI.Services;
using SmartHydroAPI.Tests.TestSupport;

namespace SmartHydroAPI_Tests.Services;

public class DosingConfirmationTests
{
    [Theory]
    [InlineData("AA:BB:CC:DD:EE:95")]
    [InlineData("AABBCCDDEE95")]
    public async Task DoseDispatch_SendsOnceToSharedMacGroup(string requestedMac)
    {
        const string mac = "AA:BB:CC:DD:EE:95";
        using var database = CreateDatabase();
        database.Devices.Add(new Device { MacAddress = mac });
        await database.SaveChangesAsync();
        var proxy = new Mock<IClientProxy>();
        proxy.Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>();
        clients.Setup(x => x.Group(It.IsAny<string>())).Returns(proxy.Object);
        var hub = new Mock<IHubContext<ActuatorHub>>();
        hub.SetupGet(x => x.Clients).Returns(clients.Object);
        var controller = new DosingController(new DosingService(database), hub.Object, database)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = TestData.Admin() }
            }
        };

        try
        {
            var result = await controller.EnqueueCommand(new DosingCommandRequestDto
            {
                DeviceMac = requestedMac, DosingType = "PH_UP", TargetVolumeMl = 2m
            }, default);

            Assert.IsType<OkObjectResult>(result.Result);
            clients.Verify(x => x.Group("AABBCCDDEE95"), Times.Once);
            clients.Verify(x => x.Group(mac), Times.Never);
            proxy.Verify(x => x.SendCoreAsync("ReceiveCommand", It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            LoraDownlinkQueue.TryTakeForMac(mac, out _);
            LoraDownlinkQueue.ClearCommandGraceWindow(mac);
        }
    }

    [Theory]
    [InlineData("PENDING", false, false)]
    [InlineData("PENDING", null, false)]
    [InlineData("DISPATCHED", false, false)]
    [InlineData("DISPATCHED", null, false)]
    [InlineData("PENDING", false, true)]
    public async Task IdleOrMissingPumpTelemetry_DoesNotConfirmDoseCompletion(string status, bool? pumpState, bool stale)
    {
        const string mac = "AA:BB:CC:DD:EE:93";
        using var database = CreateDatabase();
        var command = new DosingCommand
        {
            DeviceMac = mac, DosingType = "PH_DOWN", TargetVolumeMl = 5m,
            CalculatedDurationMs = 4000, Status = status,
            CreatedAt = DateTime.UtcNow.AddSeconds(stale ? -120 : -5)
        };
        database.DosingCommands.Add(command);
        await database.SaveChangesAsync();
        var telemetry = new Mock<ITelemetryRepository>();
        telemetry.Setup(x => x.UpsertDeviceHeartbeatAsync(mac, It.IsAny<string?>(),
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Device { MacAddress = mac });
        var actuators = new Mock<IActuatorRepository>();
        actuators.Setup(x => x.GetOrCreateStateAsync(mac, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActuatorState { DeviceMac = mac });
        var factory = new Mock<ITelemetryStrategyFactory>();
        factory.Setup(x => x.GetStrategy(It.IsAny<string?>()))
            .Returns(new FireBeetleC6TelemetryStrategy());
        var service = new TelemetryService(telemetry.Object, actuators.Object,
            factory.Object, Mock.Of<IEventPublisher>(), database);

        var response = await service.IngestAndSyncAsync(new TelemetryIngestDto
        {
            DeviceMac = mac, PhUpPump = pumpState, PhDownPump = pumpState,
            NutrientUpPump = pumpState, DilutionDownPump = pumpState
        });

        Assert.Equal(stale ? "PENDING" : "DISPATCHED", command.Status);
        Assert.Null(command.ExecutedAt);
        if (status == "PENDING" && !stale)
            Assert.Equal(command.Id.ToString(), response.PendingDosingCommandId);
        if (stale) Assert.Null(response.PendingDosingCommandId);

        // Completion remains possible through the explicit command-specific callback.
        Assert.True(await new DosingService(database).CompleteCommandAsync(new DosingCallbackRequestDto
        {
            CommandId = command.Id, Status = "COMPLETED"
        }));
        Assert.Equal("COMPLETED", command.Status);
        Assert.NotNull(command.ExecutedAt);
    }

    [Theory]
    [InlineData("PH_UP", 0.5, 400)]
    [InlineData("PH_DOWN", 2, 1600)]
    [InlineData("NUTRIENT_UP", 10, 8000)]
    [InlineData("DILUTION_DOWN", 25, 20000)]
    public async Task RequestedDose_UsesAuthorised800MillisecondsPerMlEstimate(
        string type, double volume, int expectedDuration)
    {
        const string mac = "AA:BB:CC:DD:EE:94";
        using var database = CreateDatabase();
        database.Devices.Add(new Device { MacAddress = mac });
        await database.SaveChangesAsync();

        var response = await new DosingService(database).EnqueueCommandAsync(new DosingCommandRequestDto
        {
            DeviceMac = mac, DosingType = type, TargetVolumeMl = (decimal)volume
        });

        Assert.Equal(expectedDuration, response.CalculatedDurationMs);
        Assert.Equal("PENDING", response.Status);
        Assert.Null(response.ExecutedAt);
    }

    private static ApplicationDbContext CreateDatabase() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
