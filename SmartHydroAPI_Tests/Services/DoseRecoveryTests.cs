using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Services;

namespace SmartHydroAPI_Tests.Services;

public class DoseRecoveryTests
{
    private const string Mac = "AA:BB:CC:DD:EE:95";
    private static ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<DosingCommand> Seed(ApplicationDbContext db, int minutes = 30)
    {
        var dose = new DosingCommand { DeviceMac = Mac, DosingType = "PH_UP", Status = "UNCONFIRMED",
            CreatedAt = DateTime.UtcNow.AddMinutes(-minutes), CalculatedDurationMs = 1600, TargetVolumeMl = 2 };
        db.Devices.Add(new Device { MacAddress = Mac });
        db.DosingCommands.Add(dose);
        db.DeviceCommands.Add(new DeviceCommand { DeviceMac = Mac, DosingCommandId = dose.Id,
            CommandId = 31, RequestId = Guid.NewGuid(), Status = "EXPIRED", Target = "ph",
            CreatedAt = dose.CreatedAt, ExpiresAt = dose.CreatedAt.AddSeconds(60), SentAt = dose.CreatedAt });
        await db.SaveChangesAsync();
        return dose;
    }

    [Fact]
    public async Task ReviewIsAuditedIdempotentAndNeverCreatesOrConfirmsADose()
    {
        using var db = Database(); var dose = await Seed(db);
        var queue = new DeviceCommandQueue(db);
        await queue.ReviewUnknownDoseAsync(Mac, dose.Id, "operator", "Pumps off and reservoir inspected", true, true);
        await queue.ReviewUnknownDoseAsync(Mac, dose.Id, "operator", "Pumps off and reservoir inspected", true, true);
        Assert.Equal("FAILED", dose.Status);
        Assert.Null(dose.ExecutedAt);
        var command = await db.DeviceCommands.SingleAsync();
        Assert.Equal("EXPIRED", command.Status); Assert.Null(command.AckedAt); Assert.Null(command.Succeeded);
        Assert.Empty(await queue.ClaimAsync());
        Assert.Single(await db.DosingCommands.ToListAsync());
        var audit = await db.ActivityLogs.SingleAsync();
        Assert.Equal("REVIEWED", audit.Status); Assert.Contains("operator", audit.Details);
        var history = await new DosingService(db).GetHistoryAsync(Mac);
        Assert.Equal("REVIEWED", Assert.Single(history).DeliveryStatus);
        // A later request can be queued, never dispatched by the review itself.
        await new DosingService(db).EnqueueCommandAsync(new() { DeviceMac = Mac, DosingType = "PH_DOWN", TargetVolumeMl = 1 });
        Assert.Equal(2, await db.DosingCommands.CountAsync());
    }

    [Theory]
    [InlineData(false, true, "Checked pumps and reservoir", 30)]
    [InlineData(true, false, "Checked pumps and reservoir", 30)]
    [InlineData(true, true, "short", 30)]
    [InlineData(true, true, "Checked pumps and reservoir", 1)]
    public async Task MissingChecksOrActiveWaitingPeriodCannotClearBlock(bool pumps, bool reservoir, string note, int minutes)
    {
        using var db = Database(); var dose = await Seed(db, minutes);
        await Assert.ThrowsAsync<ArgumentException>(() => new DeviceCommandQueue(db)
            .ReviewUnknownDoseAsync(Mac, dose.Id, "operator", note, pumps, reservoir));
        Assert.Equal("UNCONFIRMED", dose.Status); Assert.Empty(db.ActivityLogs);
    }

    [Fact]
    public async Task WrongControllerCannotReviewDose()
    {
        using var db = Database(); var dose = await Seed(db);
        await Assert.ThrowsAsync<ArgumentException>(() => new DeviceCommandQueue(db)
            .ReviewUnknownDoseAsync("AA:BB:CC:DD:EE:96", dose.Id, "operator", "Equipment checked locally", true, true));
        Assert.Equal("UNCONFIRMED", dose.Status);
    }

    [Fact]
    public async Task MatchingLateRefusalResolvesUnconfirmedDoseWithoutManualReview()
    {
        using var db = Database(); var dose = await Seed(db);
        var receipt = await new DeviceCommandQueue(db).AcknowledgeAsync(Mac, 31, 0, 0, "sensor_fault", "accepted");
        Assert.NotNull(receipt); Assert.Equal("FAILED", dose.Status);
        Assert.Equal(false, receipt.Succeeded);
        Assert.Equal("REJECTED", Assert.Single(await new DosingService(db).GetHistoryAsync(Mac)).DeliveryStatus);
    }
}
