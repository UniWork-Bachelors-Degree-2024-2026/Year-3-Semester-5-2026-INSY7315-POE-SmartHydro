using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using SmartHydroAPI.Hubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Patterns.Decorator;
using SmartHydroAPI.Patterns.Observer;
using SmartHydroAPI.Patterns.Repository;
using SmartHydroAPI.Patterns.Strategy;
using SmartHydroAPI.Services;

namespace SmartHydroAPI_Tests.Services;

public class HomeGatewayTests
{
    private const string Mac = "10:51:DB:29:3F:0C";
    private const string Key = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    [Theory]
    [InlineData("/hubs/home-gateway")]
    [InlineData("/api/home-gateway/packet")]
    [InlineData("/api/home-gateway/commands")]
    public async Task PilotRoutes_RequireOptInAndSeparateKey(string path)
    {
        async Task<DefaultHttpContext> Request(bool enabled, string? supplied, string pilotKey = Key)
        {
            var context = new DefaultHttpContext();
            context.Request.Path = path;
            if (supplied != null) context.Request.Headers[DeviceKeySecurity.Header] = supplied;
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HomeGateway:Enabled"] = enabled.ToString(), ["HomeGateway:NodeMac"] = Mac,
                ["Security:HomeGatewayKey"] = pilotKey, ["Security:DeviceKey"] = new string('1', 64)
            }).Build();
            await DeviceKeySecurity.InvokeAsync(context, config, _ => { context.Response.StatusCode = 202; return Task.CompletedTask; });
            return context;
        }
        Assert.Equal(503, (await Request(false, Key)).Response.StatusCode);
        Assert.Equal(401, (await Request(true, null)).Response.StatusCode);
        Assert.Equal(401, (await Request(true, new string('1', 64))).Response.StatusCode);
        Assert.Equal(503, (await Request(true, new string('1', 64), new string('1', 64))).Response.StatusCode);
        var accepted = await Request(true, Key);
        Assert.Equal(202, accepted.Response.StatusCode);
        Assert.Equal(Mac, accepted.User.FindFirst("homeGatewayMac")?.Value);
    }

    private static ServiceProvider Services(ApplicationDbContext db, ITelemetryRepository? repository = null)
    {
        var strategy = new TelemetryStrategyFactory([new FireBeetleC6TelemetryStrategy()]);
        var service = new TelemetryService(repository ?? new TelemetryRepository(db), new ActuatorRepository(db),
            strategy, Mock.Of<IEventPublisher>(), db);
        var proxy = new Mock<IClientProxy>();
        proxy.Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>();
        clients.Setup(x => x.Group(It.IsAny<string>())).Returns(proxy.Object);
        var hub = new Mock<IHubContext<ActuatorHub>>();
        hub.SetupGet(x => x.Clients).Returns(clients.Object);
        return new ServiceCollection().AddSingleton(db).AddSingleton<ITelemetryService>(service)
            .AddSingleton<IActuatorService>(new ActuatorService(new ActuatorRepository(db), Mock.Of<IEventPublisher>()))
            .AddSingleton(hub.Object).BuildServiceProvider();
    }

    private static LoraCompactTelemetryDto Reading() => new()
    {
        DeviceMac = Mac, Sequence = 1001, Ph = 6.2m, Ec = 0.31m, Temperature = 25m,
        Humidity = 60m, LightLux = 76m, RelayBitmask = 12
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptedPilotReading_IsStoredInTelemetryRealtime_AndRetryIsDeduplicated(bool staleDeviceProfile)
    {
        using var db = SmartHydroAPI.Tests.TestSupport.TestData.CreateDatabase();
        db.Devices.Add(new Device {
            MacAddress = Mac, DeviceName = "Home Heltec",
            StructureType = staleDeviceProfile ? "VERTICAL-TOWER" : "FODDER-SYSTEM",
            Structure = staleDeviceProfile ? new Structure { Name = "Home fodder", StructureType = "FODDER_TRAY" } : null
        });
        await db.SaveChangesAsync();
        using var services = Services(db);
        var processor = new HomeGatewayPackets(services, db);

        var result = JsonSerializer.SerializeToElement(await processor.ProcessAsync(Reading(), Mac, default));
        Assert.True(result.GetProperty("success").GetBoolean());
        // Inspect the PostgreSQL model without opening a database connection.
        using var postgres = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_check;Username=model_check;Password=unused").Options);
        Assert.Equal("telemetry_realtime", postgres.Model.FindEntityType(typeof(TelemetryRealtime))!.GetTableName());
        foreach (var (entity, table) in new (Type, string)[]
        {
            (typeof(DeviceCommand), "device_commands"), (typeof(DosingCommand), "dosing_commands"),
            (typeof(ActuatorState), "actuator_states"), (typeof(ActuatorHistory), "actuator_history"),
            (typeof(ActivityLog), "activity_logs"), (typeof(TelemetryReceipt), "telemetry_receipts")
        }) Assert.Equal(table, postgres.Model.FindEntityType(entity)!.GetTableName());
        var row = Assert.Single(await db.TelemetryRealtime.ToListAsync());
        Assert.Equal(Mac, row.DeviceMac);
        Assert.Equal(6.2m, row.PhLevel);
        Assert.Equal(0.31m, row.EcMs);
        Assert.Equal(12, row.RelayBitmask);
        Assert.Equal(1001, row.SampleSequence);

        await processor.ProcessAsync(Reading(), Mac, default);
        Assert.Single(await db.TelemetryRealtime.ToListAsync());
    }

    [Fact]
    public async Task FailedStorage_DoesNotReturnSuccessReceipt()
    {
        using var db = SmartHydroAPI.Tests.TestSupport.TestData.CreateDatabase();
        db.Devices.Add(new Device { MacAddress = Mac, StructureType = "FODDER-SYSTEM" });
        await db.SaveChangesAsync();
        var repository = new Mock<ITelemetryRepository>();
        repository.Setup(x => x.UpsertDeviceHeartbeatAsync(Mac, It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(db.Devices.Local.Single());
        repository.Setup(x => x.InsertReadingAsync(It.IsAny<TelemetryRealtime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Storage unavailable"));
        using var services = Services(db, repository.Object);
        await Assert.ThrowsAsync<DbUpdateException>(() => new HomeGatewayPackets(services, db).ProcessAsync(Reading(), Mac, default));
        Assert.Empty(await db.TelemetryRealtime.ToListAsync());
    }

    [Fact]
    public async Task TowerOrWrongMac_IsRejectedBeforeIngestion()
    {
        using var db = SmartHydroAPI.Tests.TestSupport.TestData.CreateDatabase();
        db.Devices.Add(new Device { MacAddress = Mac, StructureType = "VERTICAL-TOWER" });
        await db.SaveChangesAsync();
        using var services = Services(db);
        var processor = new HomeGatewayPackets(services, db);
        await Assert.ThrowsAsync<ArgumentException>(() => processor.ProcessAsync(Reading(), Mac, default));
        await Assert.ThrowsAsync<ArgumentException>(() => processor.ProcessAsync(Reading(), "FC:01:2C:EE:29:F0", default));
        Assert.Empty(await db.TelemetryRealtime.ToListAsync());
    }

    [Fact]
    public void ReplacedConnection_CannotRemoveCurrentGateway()
    {
        HomeGatewayConnections.Attach(Mac, "old");
        HomeGatewayConnections.Attach(Mac, "new");
        try
        {
            HomeGatewayConnections.Remove(Mac, "old");
            Assert.True(HomeGatewayConnections.IsCurrent(Mac, "new"));
            Assert.False(HomeGatewayConnections.IsCurrent(Mac, "old"));
        }
        finally { HomeGatewayConnections.Remove(Mac, "new"); }
    }

    [Fact]
    public async Task PilotCommand_IsReservedForItsGateway_WhileTowerRemainsOnLegacyRoute()
    {
        using var db = SmartHydroAPI.Tests.TestSupport.TestData.CreateDatabase();
        const string tower = "FC:01:2C:EE:29:F0";
        db.Devices.AddRange(new Device { MacAddress = Mac, StructureType = "FODDER-SYSTEM" },
            new Device { MacAddress = tower, StructureType = "VERTICAL-TOWER" });
        await db.SaveChangesAsync();
        var queue = new DeviceCommandQueue(db);
        await queue.EnqueueAsync(Mac, "ec", new() { ["dt"] = "NUTRIENT_UP", ["dm"] = 1600 }, "Test");
        await queue.EnqueueAsync(tower, "light", new() { ["gl"] = 1 }, "Test");
        HomeGatewayConnections.Configure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["HomeGateway:Enabled"] = "true", ["HomeGateway:NodeMac"] = Mac
        }).Build());
        try
        {
            var legacy = Assert.Single(await queue.ClaimAsync());
            Assert.Equal(tower.Replace(":", ""), legacy["m"]);
            var pilot = Assert.Single(await queue.ClaimAsync(Mac));
            Assert.Equal(Mac.Replace(":", ""), pilot["m"]);
            Assert.True(Convert.ToInt32(pilot["ttl"]) > 0);
        }
        finally { HomeGatewayConnections.Configure(new ConfigurationBuilder().Build()); }
    }

    [Theory]
    [InlineData("PH_UP", 16)]
    [InlineData("PH_DOWN", 32)]
    [InlineData("NUTRIENT_UP", 64)]
    [InlineData("DILUTION_DOWN", 128)]
    public async Task PilotDose_PersistsRequestOutcomeRelayStateAndHistory(string type, int mask)
    {
        using var db = SmartHydroAPI.Tests.TestSupport.TestData.CreateDatabase();
        db.Devices.Add(new Device { MacAddress = Mac, StructureType = "FODDER-SYSTEM" });
        await db.SaveChangesAsync();
        await new DosingService(db).EnqueueCommandAsync(new() { DeviceMac = Mac, DosingType = type, TargetVolumeMl = 1m });
        var command = await db.DeviceCommands.SingleAsync();
        Assert.Equal("QUEUED", command.Status);
        Assert.Single(await db.DosingCommands.ToListAsync());
        await new DeviceCommandQueue(db).ClaimAsync(Mac);
        Assert.Equal("SENT", command.Status);
        using var services = Services(db);
        var processor = new HomeGatewayPackets(services, db);
        LoraCompactTelemetryDto Ack(int relays, string phase) => new()
        {
            DeviceMac = Mac, CommandAck = 1, CommandReceiptRequested = 1, CommandId = command.CommandId,
            CommandOk = 1, RelayBitmask = relays, CommandPhase = phase,
            CommandReason = phase == "completed" ? "timer_finished" : "executed"
        };
        var accepted = JsonSerializer.SerializeToElement(await processor.ProcessAsync(Ack(mask, "accepted"), Mac, default));
        Assert.True(accepted.GetProperty("success").GetBoolean());
        Assert.Equal("DISPATCHED", (await db.DosingCommands.SingleAsync()).Status);
        Assert.NotEmpty(await db.ActuatorHistory.ToListAsync());
        await processor.ProcessAsync(Ack(0, "completed"), Mac, default);
        Assert.Equal("ACKED", command.Status);
        Assert.Equal("COMPLETED", (await db.DosingCommands.SingleAsync()).Status);
        var state = await db.ActuatorStates.SingleAsync();
        Assert.False(state.PhUpPump || state.PhDownPump || state.NutrientUpPump || state.DilutionDownPump);
        Assert.Contains(await db.ActivityLogs.ToListAsync(), x => x.CommandId == command.CommandId && x.Status == "completed");
        Assert.Empty(await db.TelemetryRealtime.ToListAsync());
    }

    [Fact]
    public async Task PilotEdgeActivity_IsPersistedOnceInActivityLogs()
    {
        using var db = SmartHydroAPI.Tests.TestSupport.TestData.CreateDatabase();
        db.Devices.Add(new Device { MacAddress = Mac, StructureType = "FODDER-SYSTEM" });
        await db.SaveChangesAsync();
        using var services = Services(db);
        var processor = new HomeGatewayPackets(services, db);
        var activity = new LoraCompactTelemetryDto
        {
            DeviceMac = Mac, Sequence = 2001, ActivityPhase = 4, ActivitySource = 6,
            ActivityAction = 6, ActivityReason = 1, SystemType = "fodder"
        };
        await processor.ProcessAsync(activity, Mac, default);
        await processor.ProcessAsync(activity, Mac, default);
        var row = Assert.Single(await db.ActivityLogs.ToListAsync());
        Assert.Equal("REFUSED", row.Status);
        Assert.Equal("NUTRIENT_UP", row.Action);
        Assert.Empty(await db.TelemetryRealtime.ToListAsync());
    }

    [Theory]
    [InlineData("FODDER_TRAY", "VERTICAL-TOWER", true)]
    [InlineData("VERTICAL_TOWER", "FODDER_TRAY", false)]
    [InlineData("FODDER_SYSTEM", "VERTICAL_TOWER", true)]
    [InlineData("VERTICAL_TOWER", "FODDER_SYSTEM", false)]
    public async Task LinkedStructure_DeterminesPilotAccess_WhenDeviceProfileIsStale(string linkedType, string deviceType, bool allowed)
    {
        using var db = SmartHydroAPI.Tests.TestSupport.TestData.CreateDatabase();
        db.Devices.Add(new Device
        {
            MacAddress = Mac, StructureType = deviceType,
            Structure = new Structure { Name = "Pilot structure", StructureType = linkedType }
        });
        await db.SaveChangesAsync();
        using var services = Services(db);
        var processor = new HomeGatewayPackets(services, db);
        if (allowed)
        {
            await processor.RequireFodderAsync(Mac, default);
            var command = await new DeviceCommandQueue(db).EnqueueAsync(Mac, "w", new() { ["w"] = 1 }, "Test");
            Assert.Equal("fodder", command.SystemType);
        }
        else {
            var error = await Assert.ThrowsAsync<ArgumentException>(() => processor.RequireFodderAsync(Mac, default));
            Assert.Contains(Mac, error.Message);
            Assert.Contains($"linked structure {db.Devices.Local.Single().StructureId}", error.Message);
            Assert.Contains(linkedType, error.Message);
        }
    }

    [Fact]
    public async Task UnregisteredPilot_ReportsTheApiBoundMac_WithoutStoringData()
    {
        using var db = SmartHydroAPI.Tests.TestSupport.TestData.CreateDatabase();
        using var services = Services(db);
        var processor = new HomeGatewayPackets(services, db);
        var error = await Assert.ThrowsAsync<ArgumentException>(() => processor.ProcessAsync(Reading(), Mac, default));
        Assert.Contains(Mac, error.Message);
        Assert.Contains("HomeGateway__NodeMac", error.Message);
        Assert.Empty(await db.TelemetryRealtime.ToListAsync());
        Assert.Empty(await db.TelemetryReceipts.ToListAsync());
    }
}
