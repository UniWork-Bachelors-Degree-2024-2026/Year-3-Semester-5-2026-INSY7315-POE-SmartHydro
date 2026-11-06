using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;
using SmartHydroAPI.Hubs;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Patterns.Observer;
using SmartHydroAPI.Patterns.Repository;
using SmartHydroAPI.Services;
using SmartHydroAPI.Tests.TestSupport;
using SmartHydroAPI.Data;
using SmartHydroAPI.Patterns.Decorator;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SmartHydroAPI.Patterns.Strategy;

namespace SmartHydroAPI_Tests.Services;

public class TowerReceiptTests
{
    private const string Mac = "AA:BB:CC:DD:EE:D1";
    [Fact]
    public async Task StoredRelayOutcome_IsPublishedBeforeModeStatus()
    {
        using var db = TestData.CreateDatabase();
        db.Devices.Add(new Device { MacAddress = Mac }); await db.SaveChangesAsync();
        var service = new ActuatorService(new ActuatorRepository(db), Mock.Of<IEventPublisher>());
        var command = await new DeviceCommandQueue(db).EnqueueAsync(Mac, "gl", new() { ["gl"] = 1 }, "test");
        var events = new List<string>();
        var proxy = new Mock<IClientProxy>();
        proxy.Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((name, args, ct) => {
                Assert.False(db.ChangeTracker.HasChanges());
                Assert.True(db.ActuatorStates.Single().GrowLight);
                events.Add(name);
            }).Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>(); clients.Setup(x => x.Group(It.IsAny<string>())).Returns(proxy.Object);
        var hub = new Mock<IHubContext<ActuatorHub>>(); hub.SetupGet(x => x.Clients).Returns(clients.Object);
        var reply = Reply(command.CommandId, 2); reply.ReportedSequence = 1;
        Assert.True(await CommandAcknowledgements.AcceptAsync(db, service, hub.Object, reply));
        Assert.Equal(new[] { "ReceiveCommandAck", "OnActuatorStateChanged", "ReceiveModeState" }, events);
    }
    private static IHubContext<ActuatorHub> Hub()
    {
        var proxy = new Mock<IClientProxy>();
        proxy.Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>(); clients.Setup(x => x.Group(It.IsAny<string>())).Returns(proxy.Object);
        var hub = new Mock<IHubContext<ActuatorHub>>(); hub.SetupGet(x => x.Clients).Returns(clients.Object); return hub.Object;
    }
    private static LoraCompactTelemetryDto Reply(int cid, int mask, string phase = "accepted", string reason = "executed") =>
        new() { DeviceMac = Mac, CommandAck = 1, CommandReceiptRequested = 1, CommandId = cid,
            CommandOk = 1, RelayBitmask = mask, CommandPhase = phase, CommandReason = reason };

    private static TelemetryService Telemetry(ApplicationDbContext db)
    {
        var factory = new Mock<ITelemetryStrategyFactory>();
        factory.Setup(x => x.GetStrategy(It.IsAny<string?>())).Returns(new FireBeetleC6TelemetryStrategy());
        return new(new TelemetryRepository(db), new ActuatorRepository(db), factory.Object, Mock.Of<IEventPublisher>(), db);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(4)] [InlineData(8)]
    [InlineData(16)] [InlineData(32)] [InlineData(64)] [InlineData(128)]
    public async Task EveryReportedRelayBit_IsPersistedWithItsSequence(int mask)
    {
        using var db = TestData.CreateDatabase(); db.Devices.Add(new Device { MacAddress = Mac }); await db.SaveChangesAsync();
        var service = new ActuatorService(new ActuatorRepository(db), Mock.Of<IEventPublisher>());
        var command = await new DeviceCommandQueue(db).EnqueueAsync(Mac, "gl", new() { ["gl"] = 1 }, "test");
        var reply = Reply(command.CommandId, mask); reply.ReportedSequence = 10;
        Assert.True(await CommandAcknowledgements.AcceptAsync(db, service, Hub(), reply));
        var state = await db.ActuatorStates.SingleAsync();
        var values = new[] { state.MainWaterPump, state.GrowLight, state.CirculationFan, state.ExtractorFan,
            state.PhUpPump, state.PhDownPump, state.NutrientUpPump, state.DilutionDownPump };
        for (int bit = 0; bit < 8; ++bit) Assert.Equal((mask & (1 << bit)) != 0, values[bit]);
        Assert.Equal(10L, state.ReportedSequence);
        var off = Reply(command.CommandId, 0); off.ReportedSequence = 11;
        Assert.True(await CommandAcknowledgements.AcceptAsync(db, service, Hub(), off));
        state = await db.ActuatorStates.SingleAsync();
        Assert.False(state.MainWaterPump || state.GrowLight || state.CirculationFan || state.ExtractorFan ||
            state.PhUpPump || state.PhDownPump || state.NutrientUpPump || state.DilutionDownPump);
        Assert.Equal(11L, state.ReportedSequence);
        Assert.True(await CommandAcknowledgements.AcceptAsync(db, service, Hub(), reply));
        Assert.Equal(11L, (await db.ActuatorStates.SingleAsync()).ReportedSequence);
    }

    [Fact]
    public async Task NewerIdenticalAck_AdvancesWatermark_AndOldTelemetryIsStoredWithoutRevertingRelays()
    {
        using var db = TestData.CreateDatabase(); db.Devices.Add(new Device { MacAddress = Mac }); await db.SaveChangesAsync();
        var service = new ActuatorService(new ActuatorRepository(db), Mock.Of<IEventPublisher>());
        var command = await new DeviceCommandQueue(db).EnqueueAsync(Mac, "gl", new() { ["gl"] = 1 }, "test");
        var reply = Reply(command.CommandId, 2); reply.ReportedSequence = 10;
        Assert.True(await CommandAcknowledgements.AcceptAsync(db, service, Hub(), reply));
        reply.ReportedSequence = 30;
        Assert.True(await CommandAcknowledgements.AcceptAsync(db, service, Hub(), reply));
        await Telemetry(db).IngestAndSyncAsync(new() { DeviceMac = Mac, Sequence = 20, GrowLight = false });
        db.ChangeTracker.Clear();
        Assert.True((await db.ActuatorStates.SingleAsync()).GrowLight);
        Assert.Equal(30L, (await db.ActuatorStates.SingleAsync()).ReportedSequence);
        Assert.Equal(20L, (await db.TelemetryRealtime.SingleAsync()).SampleSequence);
        await Telemetry(db).IngestAndSyncAsync(new() { DeviceMac = Mac, Sequence = 40, GrowLight = false });
        Assert.False((await service.GetStateAsync(Mac)).GrowLight);
        Assert.Equal(40L, (await db.ActuatorStates.SingleAsync()).ReportedSequence);
    }

    [Fact]
    public async Task LateAckCannotReplaceNewerSequence_EvenWhenItBelongsToANewerCommand()
    {
        using var db = TestData.CreateDatabase(); db.Devices.Add(new Device { MacAddress = Mac }); await db.SaveChangesAsync();
        var service = new ActuatorService(new ActuatorRepository(db), Mock.Of<IEventPublisher>()); var queue = new DeviceCommandQueue(db);
        var light = await queue.EnqueueAsync(Mac, "gl", new() { ["gl"] = 1 }, "test");
        var fan = await queue.EnqueueAsync(Mac, "fc", new() { ["fc"] = 1 }, "test");
        var latest = Reply(light.CommandId, 6); latest.ReportedSequence = 30;
        var delayed = Reply(fan.CommandId, 2); delayed.ReportedSequence = 20;
        Assert.True(await CommandAcknowledgements.AcceptAsync(db, service, Hub(), latest));
        Assert.True(await CommandAcknowledgements.AcceptAsync(db, service, Hub(), delayed));
        Assert.True((await service.GetStateAsync(Mac)).CirculationFan);
        Assert.Equal("ACKED", fan.Status);
    }

    [Fact]
    public async Task LegacyFodderTelemetryDoesNotOptIntoVersionedAcks()
    {
        using var db = TestData.CreateDatabase(); db.Devices.Add(new Device { MacAddress = Mac, StructureType = "FODDER" }); await db.SaveChangesAsync();
        var service = new ActuatorService(new ActuatorRepository(db), Mock.Of<IEventPublisher>());
        await Telemetry(db).IngestAndSyncAsync(new() { DeviceMac = Mac, SystemType = "fodder", Sequence = 1, GrowLight = false });
        Assert.Null((await db.ActuatorStates.SingleAsync()).ReportedSequence);
        var command = await new DeviceCommandQueue(db).EnqueueAsync(Mac, "gl", new() { ["gl"] = 1 }, "test");
        Assert.True(await CommandAcknowledgements.AcceptAsync(db, service, Hub(), Reply(command.CommandId, 2)));
        Assert.True((await service.GetStateAsync(Mac)).GrowLight);
    }

    [Fact]
    public async Task TrackedStateIsRefreshedBeforeApplyingAReportFromAnotherContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var first = new ApplicationDbContext(options); using var second = new ApplicationDbContext(options);
        var repository = new ActuatorRepository(first);
        await repository.UpdateStateAndLogHistoryAsync(new() { DeviceMac = Mac, GrowLight = true, ReportedSequence = 10 });
        await new ActuatorRepository(second).UpdateStateAndLogHistoryAsync(new() { DeviceMac = Mac, GrowLight = false, ReportedSequence = 30 });
        await repository.UpdateStateAndLogHistoryAsync(new() { DeviceMac = Mac, GrowLight = true, ReportedSequence = 20 });
        first.ChangeTracker.Clear();
        Assert.False((await first.ActuatorStates.SingleAsync()).GrowLight);
        Assert.Equal(30L, (await first.ActuatorStates.SingleAsync()).ReportedSequence);
    }

    [Theory]
    [InlineData("PH_UP")] [InlineData("PH_DOWN")] [InlineData("NUTRIENT_UP")] [InlineData("DILUTION_DOWN")]
    public async Task UnknownDoseBlocksAnotherRequest_UntilALateCompletionArrives(string type)
    {
        using var db = TestData.CreateDatabase(); db.Devices.Add(new Device { MacAddress = Mac }); await db.SaveChangesAsync();
        var dosing = new DosingService(db); var queue = new DeviceCommandQueue(db);
        await dosing.EnqueueCommandAsync(new() { DeviceMac = Mac, DosingType = type, TargetVolumeMl = 1m });
        var command = await db.DeviceCommands.SingleAsync();
        await queue.RecordPushAttemptAsync(command.Id);
        command.ExpiresAt = DateTime.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
        Assert.Equal("UNCONFIRMED", Assert.Single(await dosing.GetHistoryAsync(Mac)).DeliveryStatus);
        await Assert.ThrowsAsync<ArgumentException>(() => dosing.EnqueueCommandAsync(new() { DeviceMac = Mac, DosingType = type, TargetVolumeMl = 1m }));
        Assert.Single(await db.DosingCommands.ToListAsync());
        var service = new ActuatorService(new ActuatorRepository(db), Mock.Of<IEventPublisher>());
        var completed = Reply(command.CommandId, 0, "completed", "timer_finished"); completed.ReportedSequence = 10;
        Assert.True(await CommandAcknowledgements.AcceptAsync(db, service, Hub(), completed));
        Assert.Equal("COMPLETED", Assert.Single(await dosing.GetHistoryAsync(Mac)).DeliveryStatus);
        await dosing.EnqueueCommandAsync(new() { DeviceMac = Mac, DosingType = type, TargetVolumeMl = 1m });
        Assert.Equal(2, await db.DosingCommands.CountAsync());
    }

    [Fact]
    public async Task UnknownDoseStaysBlockedOnLateStart_ThenResolvesOnCancellation()
    {
        using var db = TestData.CreateDatabase(); db.Devices.Add(new Device { MacAddress = Mac }); await db.SaveChangesAsync();
        var dosing = new DosingService(db); var queue = new DeviceCommandQueue(db);
        await dosing.EnqueueCommandAsync(new() { DeviceMac = Mac, DosingType = "PH_UP", TargetVolumeMl = 1m });
        var command = await db.DeviceCommands.SingleAsync();
        await queue.RecordPushAttemptAsync(command.Id);
        command.ExpiresAt = DateTime.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
        Assert.Equal("UNCONFIRMED", Assert.Single(await dosing.GetHistoryAsync(Mac)).DeliveryStatus);
        var service = new ActuatorService(new ActuatorRepository(db), Mock.Of<IEventPublisher>());
        var started = Reply(command.CommandId, 16); started.ReportedSequence = 10;
        Assert.True(await CommandAcknowledgements.AcceptAsync(db, service, Hub(), started));
        Assert.Equal("UNCONFIRMED", Assert.Single(await dosing.GetHistoryAsync(Mac)).DeliveryStatus);
        await Assert.ThrowsAsync<ArgumentException>(() => dosing.EnqueueCommandAsync(new() { DeviceMac = Mac, DosingType = "PH_DOWN", TargetVolumeMl = 1m }));
        var cancelled = Reply(command.CommandId, 0, "cancelled", "sensor_stale"); cancelled.ReportedSequence = 11;
        Assert.True(await CommandAcknowledgements.AcceptAsync(db, service, Hub(), cancelled));
        Assert.Equal("CANCELLED", Assert.Single(await dosing.GetHistoryAsync(Mac)).DeliveryStatus);
        Assert.False((await service.GetStateAsync(Mac)).PhUpPump);
    }

    [Fact]
    public async Task UnsequencedReportCannotReplaceAnOrderedRelaySnapshot()
    {
        using var db = TestData.CreateDatabase(); var repository = new ActuatorRepository(db);
        await repository.UpdateStateAndLogHistoryAsync(new() { DeviceMac = Mac, GrowLight = true, ReportedSequence = 30 });
        await repository.UpdateStateAndLogHistoryAsync(new() { DeviceMac = Mac, GrowLight = false });
        Assert.True((await db.ActuatorStates.SingleAsync()).GrowLight);
        Assert.Equal(30L, (await db.ActuatorStates.SingleAsync()).ReportedSequence);
    }

    [Fact]
    public async Task RetriedLegacyReceipt_RepairsAMissingRelayStateRow()
    {
        using var db = TestData.CreateDatabase(); db.Devices.Add(new Device { MacAddress = Mac }); await db.SaveChangesAsync();
        var queue = new DeviceCommandQueue(db);
        var command = await queue.EnqueueAsync(Mac, "gl", new() { ["gl"] = 1 }, "test");
        await queue.AcknowledgeAsync(Mac, command.CommandId, 1, 2, "executed", "accepted");
        Assert.Empty(await db.ActuatorStates.ToListAsync());
        var service = new ActuatorService(new ActuatorRepository(db), Mock.Of<IEventPublisher>());
        Assert.True(await CommandAcknowledgements.AcceptAsync(db, service, Hub(), Reply(command.CommandId, 2)));
        Assert.True((await service.GetStateAsync(Mac)).GrowLight);
    }

    [Fact]
    public async Task DelayedFirstAck_DoesNotReplaceANewerCommandsSnapshot()
    {
        using var db = TestData.CreateDatabase(); db.Devices.Add(new Device { MacAddress = Mac }); await db.SaveChangesAsync();
        var service = new ActuatorService(new ActuatorRepository(db), Mock.Of<IEventPublisher>());
        var queue = new DeviceCommandQueue(db);
        var light = await queue.EnqueueAsync(Mac, "gl", new() { ["gl"] = 1 }, "test");
        var fan = await queue.EnqueueAsync(Mac, "cf", new() { ["cf"] = 1 }, "test");
        Assert.True(await CommandAcknowledgements.AcceptAsync(db, service, Hub(), Reply(fan.CommandId, 6)));
        Assert.True(await CommandAcknowledgements.AcceptAsync(db, service, Hub(), Reply(light.CommandId, 2)));
        Assert.True((await service.GetStateAsync(Mac)).CirculationFan);
        Assert.Equal("ACKED", light.Status);
    }

    [Fact]
    public async Task TelemetryStateWrite_IsVisibleWithoutWaitingForCacheExpiry()
    {
        using var db = TestData.CreateDatabase();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var repository = new ActuatorRepository(db);
        var service = new CachedActuatorServiceDecorator(new ActuatorService(repository, Mock.Of<IEventPublisher>()), cache);
        Assert.False((await service.GetStateAsync(Mac)).GrowLight);
        await repository.UpdateStateAndLogHistoryAsync(new() { DeviceMac = Mac, GrowLight = true, ChangedBy = "EdgeSync" });
        Assert.True((await service.GetStateAsync(Mac)).GrowLight);
    }

    private sealed class FailReceiptSave : SaveChangesInterceptor
    {
        public bool Fail { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Fail && eventData.Context!.ChangeTracker.Entries<DeviceCommand>().Any(x => x.Entity.Status == "ACKED"))
            {
                Assert.Contains(eventData.Context.ChangeTracker.Entries<ActuatorState>(), x => x.Entity.GrowLight);
                throw new InvalidOperationException("Simulated database save failure");
            }
            return new(result);
        }
    }

    [Fact]
    public async Task FailedReceiptSave_LeavesCommandAndRelayStateUnconfirmed_ThenRetryRepairsBoth()
    {
        var failure = new FailReceiptSave();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(failure).Options;
        using var db = new ApplicationDbContext(options);
        db.Devices.Add(new Device { MacAddress = Mac }); await db.SaveChangesAsync();
        var service = new ActuatorService(new ActuatorRepository(db), Mock.Of<IEventPublisher>());
        var command = await new DeviceCommandQueue(db).EnqueueAsync(Mac, "gl", new() { ["gl"] = 1 }, "test");
        failure.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => CommandAcknowledgements.AcceptAsync(db, service, Hub(), Reply(command.CommandId, 2)));
        db.ChangeTracker.Clear();
        Assert.Equal("QUEUED", (await db.DeviceCommands.SingleAsync()).Status);
        Assert.Empty(await db.ActuatorStates.ToListAsync());
        failure.Fail = false;
        Assert.True(await CommandAcknowledgements.AcceptAsync(db, service, Hub(), Reply(command.CommandId, 2)));
        Assert.True((await service.GetStateAsync(Mac)).GrowLight);
        Assert.Equal("ACKED", (await db.DeviceCommands.SingleAsync()).Status);
    }

    [Fact]
    public async Task RetriedAck_DoesNotReplayOldStateOverANewerCommand()
    {
        using var db = TestData.CreateDatabase(); db.Devices.Add(new Device { MacAddress = Mac }); await db.SaveChangesAsync();
        var service = new ActuatorService(new ActuatorRepository(db), Mock.Of<IEventPublisher>()); var hub = Hub();
        var queue = new DeviceCommandQueue(db);
        var on = await queue.EnqueueAsync(Mac, "gl", new() { ["gl"] = 1 }, "test");
        Assert.True(await CommandAcknowledgements.AcceptAsync(db, service, hub, Reply(on.CommandId, 2)));
        var off = await queue.EnqueueAsync(Mac, "gl", new() { ["gl"] = 0 }, "test");
        Assert.True(await CommandAcknowledgements.AcceptAsync(db, service, hub, Reply(off.CommandId, 0)));
        var historyCount = await db.ActuatorHistory.CountAsync();
        Assert.True(await CommandAcknowledgements.AcceptAsync(db, service, hub, Reply(on.CommandId, 2)));
        Assert.False((await service.GetStateAsync(Mac)).GrowLight);
        Assert.Equal(historyCount, await db.ActuatorHistory.CountAsync());
    }

    [Theory]
    [InlineData("PH_UP", 16)] [InlineData("PH_DOWN", 32)]
    [InlineData("NUTRIENT_UP", 64)] [InlineData("DILUTION_DOWN", 128)]
    public async Task LateRunningAck_CannotUndoCompletedDose(string type, int mask)
    {
        using var db = TestData.CreateDatabase(); db.Devices.Add(new Device { MacAddress = Mac }); await db.SaveChangesAsync();
        var service = new ActuatorService(new ActuatorRepository(db), Mock.Of<IEventPublisher>()); var hub = Hub();
        await new DosingService(db).EnqueueCommandAsync(new() { DeviceMac = Mac, DosingType = type, TargetVolumeMl = 1m });
        var command = await db.DeviceCommands.SingleAsync();
        Assert.True(await CommandAcknowledgements.AcceptAsync(db, service, hub, Reply(command.CommandId, mask)));
        Assert.True(await CommandAcknowledgements.AcceptAsync(db, service, hub, Reply(command.CommandId, 0, "completed", "timer_finished")));
        Assert.True(await CommandAcknowledgements.AcceptAsync(db, service, hub, Reply(command.CommandId, mask)));
        Assert.Equal("COMPLETED", (await db.DosingCommands.SingleAsync()).Status);
        Assert.Equal(0, command.RelayBitmask);
        var state = await service.GetStateAsync(Mac);
        Assert.False(state.PhUpPump || state.PhDownPump || state.NutrientUpPump || state.DilutionDownPump);
    }
}
