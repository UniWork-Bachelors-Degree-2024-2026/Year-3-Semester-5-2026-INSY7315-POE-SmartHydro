using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Patterns.Observer;
using SmartHydroAPI.Patterns.Decorator;
using SmartHydroAPI.Patterns.Observer.Events;
using SmartHydroAPI.Patterns.Repository;
using SmartHydroAPI.Patterns.Strategy;
using SmartHydroAPI.Services;

namespace SmartHydroAPI_Tests.Services;

public class TelemetryCaptureTests
{
    private static ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static TelemetryService Service(ApplicationDbContext db, Mock<IEventPublisher> events) {
        var factory = new Mock<ITelemetryStrategyFactory>();
        factory.Setup(x => x.GetStrategy(It.IsAny<string?>())).Returns(new FireBeetleC6TelemetryStrategy());
        return new(new TelemetryRepository(db), new ActuatorRepository(db), factory.Object, events.Object, db);
    }
    [Fact]
    public async Task SameCaptureAcrossTransports_IsStoredAndPublishedOnce() {
        using var db = Database(); var events = new Mock<IEventPublisher>(); var service = Service(db, events);
        await service.IngestAndSyncAsync(new() { DeviceMac = "AABBCCDDEE61", Sequence = 45, Ph = 6.1m });
        var retry = new TelemetryIngestDto { DeviceMac = "AA:BB:CC:DD:EE:61", Sequence = 45, Ph = 6.1m };
        await service.IngestAndSyncAsync(retry);
        Assert.True(retry.WasDuplicate);
        Assert.Single(await db.TelemetryRealtime.ToListAsync());
        events.Verify(x => x.PublishAsync(It.IsAny<TelemetryIngestedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }
    [Fact]
    public async Task CompactOnlineCapture_ReturnsMatchingReceiptAfterStorage_AndDeduplicatesRetry() {
        using var db = Database();
        var controller = new SmartHydroAPI.Controllers.TelemetryController(Service(db, new Mock<IEventPublisher>()), db);
        for (int attempt = 0; attempt < 2; attempt++) {
            var result = await controller.Ingest(new() { DeviceMac = "AABBCCDDEE68", Sequence = 4611686018427387975L,
                OperatingMode = 1, Ph = 6.2m }, default, true);
            var response = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result.Result);
            var json = System.Text.Json.JsonSerializer.SerializeToElement(response.Value);
            Assert.True(json.GetProperty("success").GetBoolean());
            Assert.Equal(4611686018427387975L, json.GetProperty("seq").GetInt64());
            Assert.True(json.GetProperty("utc").GetInt64() > 1704067200);
            Assert.False(json.TryGetProperty("growLight", out _));
            Assert.Single(await db.TelemetryRealtime.ToListAsync());
            Assert.Single(await db.TelemetryReceipts.ToListAsync());
        }
    }
    [Fact]
    public async Task CompactOnlineCapture_StorageFailureCannotProduceSuccessReceipt() {
        using var db = Database(); var service = new Mock<ITelemetryService>();
        service.Setup(x => x.IngestAndSyncAsync(It.IsAny<TelemetryIngestDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("storage unavailable"));
        var controller = new SmartHydroAPI.Controllers.TelemetryController(service.Object, db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.Ingest(new() { DeviceMac = "AABBCCDDEE68", Sequence = 15 }, default, true));
        Assert.Empty(await db.TelemetryRealtime.ToListAsync());
    }
    [Theory]
    [InlineData(300, false)]
    [InlineData(0, true)]
    public async Task HistoricalCapture_DoesNotChangeHeartbeatRelayOrPublish(long age, bool uncertain) {
        using var db = Database(); var events = new Mock<IEventPublisher>();
        var lastSeen = DateTime.UtcNow.AddMinutes(-10);
        db.Devices.Add(new Device { MacAddress = "AA:BB:CC:DD:EE:62", LastSeenAt = lastSeen });
        db.ActuatorStates.Add(new ActuatorState { DeviceMac = "AA:BB:CC:DD:EE:62", GrowLight = false });
        await db.SaveChangesAsync();
        var dto = new TelemetryIngestDto { DeviceMac = "AABBCCDDEE62", Sequence = 46, Age = age,
            CaptureTimeUncertain = uncertain, GrowLight = true, Ph = 6.2m };
        await Service(db, events).IngestAndSyncAsync(dto);
        var row = await db.TelemetryRealtime.SingleAsync();
        Assert.Equal(dto.ReceivedAtUtc!.Value.AddSeconds(-age), row.RecordedAt);
        Assert.Equal(uncertain, row.CaptureTimeUncertain);
        Assert.Equal(lastSeen, (await db.Devices.SingleAsync()).LastSeenAt);
        Assert.False((await db.ActuatorStates.SingleAsync()).GrowLight);
        events.Verify(x => x.PublishAsync(It.IsAny<TelemetryIngestedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        if (uncertain) Assert.Null(await new TelemetryRepository(db).GetLatestByMacAsync(dto.DeviceMac));
    }
    [Fact]
    public async Task SequenceNumbers_AreIndependentForEachDevice() {
        using var db = Database(); var service = Service(db, new Mock<IEventPublisher>());
        foreach (var mac in new[] { "AABBCCDDEE63", "AABBCCDDEE64" })
            await service.IngestAndSyncAsync(new() { DeviceMac = mac, Sequence = 1 });
        Assert.Equal(2, await db.TelemetryRealtime.CountAsync());
    }
    [Fact]
    public void KnownTimestamp_UsesCaptureTimeAndRejectsFutureCapture() {
        var dto = new TelemetryIngestDto { CapturedAtUnix = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds() };
        dto.PrepareCapture(); Assert.InRange(dto.Age!.Value, 299, 301); Assert.False(dto.IsLive);
        var future = new TelemetryIngestDto { CapturedAtUnix = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds() };
        future.PrepareCapture(); Assert.True(future.CaptureTimeUncertain); Assert.False(future.IsLive);
    }
    [Fact]
    public async Task ExistingLegacySummary_KeepsLateRawReadingsRatherThanGuessingCounts() {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var services = new ServiceCollection().AddScoped(_ => new ApplicationDbContext(options)).BuildServiceProvider();
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2));
        using (var db = new ApplicationDbContext(options)) {
            db.TelemetryDailyHistory.Add(new TelemetryDailyHistory { DeviceMac = "AA:BB:CC:DD:EE:66", SummaryDate = date, AvgPh = 6.5m, TotalSamples = 100 });
            db.TelemetryRealtime.Add(new TelemetryRealtime { DeviceMac = "AA:BB:CC:DD:EE:66", RecordedAt = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), PhLevel = 4 });
            await db.SaveChangesAsync();
        }
        await new TelemetryRollupService(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<TelemetryRollupService>.Instance).ExecuteDailyRollupAsync(date);
        using (var db = new ApplicationDbContext(options)) {
            Assert.Equal(6.5m, (await db.TelemetryDailyHistory.SingleAsync()).AvgPh);
            Assert.Single(await db.TelemetryRealtime.ToListAsync());
        }
    }
    [Theory]
    [InlineData(300, false)]
    [InlineData(0, true)]
    public async Task CompactHistoricalCapture_ReturnsReceiptWithoutLiveBroadcast(long age, bool uncertain) {
        using var db = Database();
        var clients = new Mock<Microsoft.AspNetCore.SignalR.IHubClients>();
        var hub = new Mock<Microsoft.AspNetCore.SignalR.IHubContext<SmartHydroAPI.Hubs.ActuatorHub>>();
        hub.SetupGet(x => x.Clients).Returns(clients.Object);
        var controller = new SmartHydroAPI.Controllers.TelemetryController(Service(db, new Mock<IEventPublisher>()), db, hubContext: hub.Object);
        var result = await controller.IngestLoraCompact(new() { DeviceMac = "AABBCCDDEE67", Sequence = 9, Age = age, CaptureTimeUncertain = uncertain, Ph = 6.2m }, default, true);
        var response = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.SerializeToElement(response.Value);
        Assert.Equal(9, json.GetProperty("seq").GetInt64());
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.False(json.TryGetProperty("sync", out _));
        clients.VerifyGet(x => x.All, Times.Never);
    }
    [Fact]
    public async Task LateReplay_MergesValidSamplesAndKeepsUncertainCaptures() {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var services = new ServiceCollection().AddScoped(_ => new ApplicationDbContext(options)).BuildServiceProvider();
        var rollup = new TelemetryRollupService(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<TelemetryRollupService>.Instance);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var time = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        using (var db = new ApplicationDbContext(options)) {
            db.TelemetryRealtime.AddRange(new TelemetryRealtime { DeviceMac = "AA:BB:CC:DD:EE:65", RecordedAt = time, PhLevel = 6, EcMs = -1 },
                new TelemetryRealtime { DeviceMac = "AA:BB:CC:DD:EE:65", RecordedAt = time, PhLevel = 8, EcMs = 2 });
            await db.SaveChangesAsync();
        }
        await rollup.ExecuteDailyRollupAsync(date);
        using (var db = new ApplicationDbContext(options)) {
            db.TelemetryRealtime.AddRange(new TelemetryRealtime { DeviceMac = "AA:BB:CC:DD:EE:65", RecordedAt = time, PhLevel = 4, EcMs = 4 },
                new TelemetryRealtime { DeviceMac = "AA:BB:CC:DD:EE:65", RecordedAt = time, CaptureTimeUncertain = true, PhLevel = 14 });
            await db.SaveChangesAsync();
        }
        await rollup.ExecuteDailyRollupAsync(date);
        await rollup.ExecuteDailyRollupAsync(date);
        using (var db = new ApplicationDbContext(options)) {
            var summary = await db.TelemetryDailyHistory.SingleAsync();
            Assert.Equal(6m, summary.AvgPh); Assert.Equal(3m, summary.AvgEc); Assert.Equal(3, summary.TotalSamples);
            Assert.True((await db.TelemetryRealtime.SingleAsync()).CaptureTimeUncertain);
        }
    }
}
