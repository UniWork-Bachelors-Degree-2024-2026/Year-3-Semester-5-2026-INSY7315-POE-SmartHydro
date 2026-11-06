using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Services;

namespace SmartHydroAPI_Tests.Services;

public class DoseDeliveryTests
{
    [Fact]
    public async Task History_KeepsOlderUnresolvedBlockerWithinLimit()
    {
        using var db = await Database();
        var old = new DosingCommand { DeviceMac = Mac, DosingType = "PH_DOWN", Status = "UNCONFIRMED", CreatedAt = DateTime.UtcNow.AddDays(-2) };
        db.DosingCommands.Add(old);
        for (var i = 0; i < 5; i++)
            db.DosingCommands.Add(new DosingCommand { DeviceMac = Mac, DosingType = "NUTRIENT_UP", Status = "FAILED", CreatedAt = DateTime.UtcNow.AddMinutes(-i) });
        await db.SaveChangesAsync();
        var history = (await new DosingService(db).GetHistoryAsync(Mac, 2)).ToList();
        Assert.Equal(2, history.Count);
        Assert.Contains(history, x => x.Id == old.Id && x.Status == "UNCONFIRMED");
        old.Status = "COMPLETED";
        await db.SaveChangesAsync();
        Assert.DoesNotContain(await new DosingService(db).GetHistoryAsync(Mac, 2), x => x.Id == old.Id);
    }
    private const string Mac = "AA:BB:CC:DD:EE:C1";
    private static async Task<ApplicationDbContext> Database()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Devices.Add(new Device { MacAddress = Mac, StructureType = "VERTICAL-TOWER" });
        await db.SaveChangesAsync();
        return db;
    }
    private static Task<DosingCommandResponseDto> Create(DosingService service, string type = "PH_UP") =>
        service.EnqueueCommandAsync(new() { DeviceMac = Mac, DosingType = type, TargetVolumeMl = 1m });

    [Theory]
    [InlineData("PH_UP", 16)]
    [InlineData("PH_DOWN", 32)]
    [InlineData("NUTRIENT_UP", 64)]
    [InlineData("DILUTION_DOWN", 128)]
    public async Task AllFourPumps_ExposeQueuedSentRunningAndCompleted(string type, int mask)
    {
        using var db = await Database(); var service = new DosingService(db); var queue = new DeviceCommandQueue(db);
        await Create(service, type);
        Assert.Equal("QUEUED", Assert.Single(await service.GetHistoryAsync(Mac)).DeliveryStatus);
        await queue.ClaimAsync();
        Assert.Equal("SENT", Assert.Single(await service.GetHistoryAsync(Mac)).DeliveryStatus);
        var command = await db.DeviceCommands.SingleAsync();
        await queue.AcknowledgeAsync(Mac, command.CommandId, 1, mask, "executed", "accepted");
        var running = Assert.Single(await service.GetHistoryAsync(Mac));
        Assert.Equal("RUNNING", running.DeliveryStatus); Assert.Null(running.ExecutedAt);
        await queue.AcknowledgeAsync(Mac, command.CommandId, 1, 0, "timer_finished", "completed");
        var completed = Assert.Single(await service.GetHistoryAsync(Mac));
        Assert.Equal("COMPLETED", completed.DeliveryStatus); Assert.NotNull(completed.ExecutedAt);
    }

    [Theory]
    [InlineData(false, "FAILED")]
    [InlineData(true, "UNCONFIRMED")]
    public async Task HistoryExpiresUnacknowledgedRequests_EvenWithoutGatewayPolling(bool dispatched, string status)
    {
        using var db = await Database(); var service = new DosingService(db); var queue = new DeviceCommandQueue(db);
        await Create(service);
        if (dispatched) await queue.ClaimAsync();
        var command = await db.DeviceCommands.SingleAsync();
        command.ExpiresAt = DateTime.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
        var result = Assert.Single(await service.GetHistoryAsync(Mac));
        Assert.Equal(dispatched ? "UNCONFIRMED" : "TIMED_OUT", result.DeliveryStatus); Assert.Equal("delivery_timeout", result.Reason);
        Assert.Equal(status, result.Status); Assert.Null(result.ExecutedAt);
        Assert.Empty(await queue.ClaimAsync());
    }

    [Fact]
    public async Task MissingCompletion_StopsBeingPending_AndIsNeverAutomaticallyResent()
    {
        using var db = await Database(); var service = new DosingService(db); var queue = new DeviceCommandQueue(db);
        await Create(service);
        var command = await db.DeviceCommands.SingleAsync();
        await queue.AcknowledgeAsync(Mac, command.CommandId, 1, 16, "executed", "accepted");
        command.AckedAt = DateTime.UtcNow.AddMinutes(-3); await db.SaveChangesAsync();
        var result = Assert.Single(await service.GetHistoryAsync(Mac));
        Assert.Equal("UNCONFIRMED", result.DeliveryStatus); Assert.Null(result.ExecutedAt);
        Assert.Equal("completion_unconfirmed", result.Reason);
        Assert.Empty(await queue.ClaimAsync());
        await queue.AcknowledgeAsync(Mac, command.CommandId, 1, 0, "executed", "accepted");
        Assert.Equal("UNCONFIRMED", (await db.DosingCommands.SingleAsync()).Status);
        await queue.AcknowledgeAsync(Mac, command.CommandId, 1, 0, "timer_finished", "completed");
        Assert.Equal("COMPLETED", Assert.Single(await service.GetHistoryAsync(Mac)).DeliveryStatus);
    }

    [Theory]
    [InlineData(0, "safety_blocked", "accepted", "REJECTED")]
    [InlineData(1, "cancelled", "cancelled", "CANCELLED")]
    public async Task RefusedOrCancelledDose_HasAnExplicitOutcome(int ok, string reason, string phase, string expected)
    {
        using var db = await Database(); var service = new DosingService(db); await Create(service);
        var command = await db.DeviceCommands.SingleAsync();
        await new DeviceCommandQueue(db).AcknowledgeAsync(Mac, command.CommandId, ok, 0, reason, phase);
        var result = Assert.Single(await service.GetHistoryAsync(Mac));
        Assert.Equal(expected, result.DeliveryStatus); Assert.Equal(reason, result.Reason);
    }

    [Fact]
    public async Task CompletionCallback_AlsoClosesDeliveryQueue_AndCannotBeReversed()
    {
        using var db = await Database(); var service = new DosingService(db); var dose = await Create(service);
        Assert.Empty(await service.GetPendingCommandsAsync(Mac));
        Assert.True(await service.CompleteCommandAsync(new() { CommandId = dose.Id, Status = "COMPLETED" }));
        Assert.Empty(await new DeviceCommandQueue(db).ClaimAsync());
        Assert.Equal("ACKED", (await db.DeviceCommands.SingleAsync()).Status);
        Assert.False(await service.CompleteCommandAsync(new() { CommandId = dose.Id, Status = "FAILED" }));
        Assert.Equal("COMPLETED", (await db.DosingCommands.SingleAsync()).Status);
    }

    [Fact]
    public async Task OldPendingRows_AreMarkedUnconfirmed_WithoutInventingCompletion()
    {
        using var db = await Database();
        db.DosingCommands.Add(new DosingCommand { DeviceMac = Mac, DosingType = "PH_DOWN", CreatedAt = DateTime.UtcNow.AddDays(-1) });
        await db.SaveChangesAsync();
        var service = new DosingService(db);
        var result = Assert.Single(await service.GetHistoryAsync(Mac));
        Assert.Equal("UNCONFIRMED", result.Status); Assert.Null(result.ExecutedAt);
        Assert.Empty(await service.GetPendingCommandsAsync(Mac));
        Assert.Empty(await db.DeviceCommands.ToListAsync());
    }
}
