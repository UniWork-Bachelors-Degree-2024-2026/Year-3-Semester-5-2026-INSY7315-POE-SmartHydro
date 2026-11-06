using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Services;
using Microsoft.Extensions.Configuration;

namespace SmartHydroAPI_Tests.Services;

public class ManualPhOverrideTests
{
    private const string Mac = "FC:01:2C:EE:29:F0";
    private static ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static DosingService Service(ApplicationDbContext db) => new(db,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Security:DeviceKey"] = new string('0', 64) }).Build());
    private static async Task Seed(ApplicationDbContext db, int minutes)
    {
        db.Devices.Add(new Device { MacAddress = Mac, StructureType = "VERTICAL-TOWER" });
        db.TelemetryRealtime.Add(new() { DeviceMac = Mac, PhLevel = 6.2m, RecordedAt = DateTime.UtcNow.AddMinutes(-minutes) });
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("PH_UP", 2)]
    [InlineData("PH_DOWN", 2)]
    [InlineData("NUTRIENT_UP", 20)]
    [InlineData("DILUTION_DOWN", 25)]
    public async Task ExplicitConfirmationIsQueuedOnceAndAuditedWithoutClaimingExecution(string type, int volume)
    {
        using var db = Database(); await Seed(db, 5);
        await Service(db).EnqueueCommandAsync(new() { DeviceMac = Mac, DosingType = type,
            TargetVolumeMl = volume, ManualPhOverride = true, IssuedBy = "Operator" });
        var wire = Assert.Single(await new DeviceCommandQueue(db).ClaimAsync());
        Assert.True(((System.Text.Json.JsonElement)wire["po"]).GetBoolean());
        var proof = ((System.Text.Json.JsonElement)wire["ps"]).GetString();
        Assert.Equal(22, proof!.Length);
        var command = await db.DeviceCommands.SingleAsync();
        var dose = await db.DosingCommands.SingleAsync();
        var text = FormattableString.Invariant($"po1|FC012CEE29F0|{command.CommandId}|{new DateTimeOffset(command.ExpiresAt).ToUnixTimeSeconds()}|{type}|{dose.CalculatedDurationMs}|{dose.Id}");
        var hash = System.Security.Cryptography.HMACSHA256.HashData(new byte[32], System.Text.Encoding.UTF8.GetBytes(text));
        Assert.Equal(Convert.ToBase64String(hash, 0, 16).TrimEnd('=').Replace('+', '-').Replace('/', '_'), proof);
        wire["cid"] = 65535; wire["ttl"] = 60000;
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(System.Text.Json.JsonSerializer.Serialize(wire)) <= 255);
        Assert.Null((await db.DeviceCommands.SingleAsync()).AckedAt);
        Assert.Contains(await db.ActivityLogs.ToListAsync(), x => x.Details.Contains("Manual pH fault override confirmed"));
    }

    [Theory]
    [InlineData(180)]
    [InlineData(240)]
    public async Task ExplicitManualConfirmationDoesNotRequireRecentPhHistory(int minutes)
    {
        using var db = Database(); await Seed(db, minutes);
        await Service(db).EnqueueCommandAsync(new() {
            DeviceMac = Mac, DosingType = "PH_DOWN", TargetVolumeMl = 2, ManualPhOverride = true });
        Assert.Single(db.DeviceCommands); Assert.Single(db.DosingCommands);
    }

    [Theory]
    [InlineData("PH_UP", 2)]
    [InlineData("PH_DOWN", 2)]
    [InlineData("NUTRIENT_UP", 20)]
    [InlineData("DILUTION_DOWN", 25)]
    public async Task ExplicitOverrideWithoutAnyPhHistoryIsSignedForAllDoseDirections(string type, int volume)
    {
        using var db = Database();
        db.Devices.Add(new Device { MacAddress = Mac, StructureType = "VERTICAL-TOWER" });
        await db.SaveChangesAsync();
        await Service(db).EnqueueCommandAsync(new() { DeviceMac = Mac, DosingType = type,
            TargetVolumeMl = volume, ManualPhOverride = true });
        var command = Assert.Single(await db.DeviceCommands.ToListAsync());
        Assert.Contains("\"po\":true", command.Payload);
        Assert.Null(command.AckedAt);
        Assert.Contains(await db.ActivityLogs.ToListAsync(), x => x.Details.Contains("current pH is unavailable"));
    }

    [Fact]
    public async Task OverrideDoesNotExtendToFodder()
    {
        using var db = Database();
        db.Devices.Add(new Device { MacAddress = Mac, StructureType = "FODDER" });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => Service(db).EnqueueCommandAsync(new() {
            DeviceMac = Mac, DosingType = "PH_DOWN", TargetVolumeMl = 2, ManualPhOverride = true }));
        Assert.Empty(db.DeviceCommands);
    }

    [Fact]
    public async Task ConfirmationNeverBypassesAnUnresolvedDose()
    {
        using var db = Database(); await Seed(db, 5);
        db.DosingCommands.Add(new() { DeviceMac = Mac, DosingType = "PH_UP", Status = "UNCONFIRMED" });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => Service(db).EnqueueCommandAsync(new() {
            DeviceMac = Mac, DosingType = "DILUTION_DOWN", TargetVolumeMl = 25, ManualPhOverride = true }));
        Assert.Empty(db.DeviceCommands);
    }

    [Fact]
    public async Task MissingSigningKeyCannotQueueAnOverride()
    {
        using var db = Database(); await Seed(db, 5);
        await Assert.ThrowsAsync<ArgumentException>(() => new DosingService(db).EnqueueCommandAsync(new() {
            DeviceMac = Mac, DosingType = "PH_UP", TargetVolumeMl = 2, ManualPhOverride = true }));
        Assert.Empty(db.DeviceCommands);
    }
}
