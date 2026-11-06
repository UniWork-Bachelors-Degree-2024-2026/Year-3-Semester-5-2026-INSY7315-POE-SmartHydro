using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Services;

namespace SmartHydroAPI_Tests.Services;

public class PhDisplayFallbackTests
{
    private const string Mac = "FC:01:2C:EE:29:F0";
    private static ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task FaultRetainsMostRecentAcceptedCaptureWithoutRefreshingItFromUploadTime()
    {
        using var db = Database(); var now = DateTime.UtcNow;
        db.Devices.Add(new Device { MacAddress = Mac, StructureType = "VERTICAL-TOWER" });
        db.TelemetryRealtime.AddRange(
            new() { DeviceMac = Mac, PhLevel = 6.1m, RecordedAt = now.AddMinutes(-5), ReceivedAt = now },
            new() { DeviceMac = Mac, PhLevel = 5.8m, RecordedAt = now.AddMinutes(-10) },
            new() { DeviceMac = Mac, PhLevel = 7m, RecordedAt = now.AddMinutes(-1), CaptureTimeUncertain = true },
            new() { DeviceMac = Mac, PhLevel = 8m, RecordedAt = now.AddMinutes(1) },
            new() { DeviceMac = "AA:BB:CC:DD:EE:FF", PhLevel = 9m, RecordedAt = now },
            new() { DeviceMac = Mac, PhLevel = -1m, RecordedAt = now });
        await db.SaveChangesAsync();
        var previous = await PhDisplayFallback.GetAsync(db, Mac, -1, now);
        Assert.NotNull(previous); Assert.Equal(6.1m, previous.Value);
        Assert.Equal(now.AddMinutes(-5), previous.RecordedAt); Assert.True(previous.AgeSeconds >= 300);
        Assert.Equal(-1m, (await db.TelemetryRealtime.OrderByDescending(x => x.Id).FirstAsync()).PhLevel);
        Assert.Null(db.Model.FindEntityType(typeof(TelemetryRealtime))!.FindProperty("PhDisplayFallback"));
    }

    [Fact]
    public async Task OldValueRemainsAvailableForDisplayAndCurrentValidReadingNeedsNoFallback()
    {
        using var db = Database(); var now = DateTime.UtcNow;
        db.Devices.Add(new Device { MacAddress = Mac, StructureType = "VERTICAL_TOWER" });
        db.TelemetryRealtime.Add(new() { DeviceMac = Mac, PhLevel = 6.4m, RecordedAt = now.AddHours(-4) });
        await db.SaveChangesAsync();
        Assert.True((await PhDisplayFallback.GetAsync(db, Mac, -1, now))!.AgeSeconds >= 14400);
        Assert.Null(await PhDisplayFallback.GetAsync(db, Mac, 6.5m, now));
    }

    [Fact]
    public async Task NoStoredAcceptedReadingDoesNotInventStartupPh()
    {
        using var db = Database();
        db.Devices.Add(new Device { MacAddress = Mac, StructureType = "VERTICAL_TOWER" });
        await db.SaveChangesAsync();
        Assert.Null(await PhDisplayFallback.GetAsync(db, Mac, -1, DateTime.UtcNow));
    }

    [Fact]
    public async Task FodderProfileIsUnaffected()
    {
        using var db = Database();
        db.Devices.Add(new Device { MacAddress = Mac, StructureType = "FODDER-SYSTEM" });
        db.TelemetryRealtime.Add(new() { DeviceMac = Mac, PhLevel = 6.4m, RecordedAt = DateTime.UtcNow.AddMinutes(-1) });
        await db.SaveChangesAsync();
        Assert.Null(await PhDisplayFallback.GetAsync(db, Mac, -1, DateTime.UtcNow));
    }

    [Theory]
    [InlineData("0.1")]
    [InlineData("12.6")]
    public async Task HistoricalValueOutsideTowerCacheRangeCannotReplaceAcceptedReading(string value)
    {
        using var db = Database(); var now = DateTime.UtcNow;
        db.Devices.Add(new Device { MacAddress = Mac, StructureType = "VERTICAL_TOWER" });
        db.TelemetryRealtime.AddRange(
            new() { DeviceMac = Mac, PhLevel = 6.2m, RecordedAt = now.AddMinutes(-5) },
            new() { DeviceMac = Mac, PhLevel = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
                RecordedAt = now.AddMinutes(-1) });
        await db.SaveChangesAsync();
        var previous = await PhDisplayFallback.GetAsync(db, Mac, -1, now);
        Assert.NotNull(previous);
        Assert.Equal(6.2m, previous.Value);
        Assert.Equal(now.AddMinutes(-5), previous.RecordedAt);
    }

    [Fact]
    public async Task OnlyOutOfRangeHistoryLeavesRetainedReadingUnavailable()
    {
        using var db = Database();
        db.Devices.Add(new Device { MacAddress = Mac, StructureType = "VERTICAL_TOWER" });
        db.TelemetryRealtime.Add(new() { DeviceMac = Mac, PhLevel = 0.1m, RecordedAt = DateTime.UtcNow.AddMinutes(-1) });
        await db.SaveChangesAsync();
        Assert.Null(await PhDisplayFallback.GetAsync(db, Mac, -1, DateTime.UtcNow));
    }
}
