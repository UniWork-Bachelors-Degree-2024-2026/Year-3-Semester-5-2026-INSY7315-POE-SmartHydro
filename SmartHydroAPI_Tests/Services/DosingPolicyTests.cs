using Microsoft.EntityFrameworkCore;
using Moq;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Services;
namespace SmartHydroAPI_Tests.Services;

public class DosingPolicyTests
{
    [Theory]
    [InlineData(1, "sensor fault")]
    [InlineData(10, "sensor_stale")]
    [InlineData(11, "sensor_recovering")]
    public async Task SafetyEventsPreserveFaultFreshnessAndRecoveryReasons(int reason, string expected)
    {
        using var db = Database();
        await ActivityLogService.IngestAsync(db, new LoraCompactTelemetryDto {
            DeviceMac = "AABBCCDDEE81", Sequence = (1L << 62) + 1, ActivityPhase = 4,
            ActivityAction = 4, ActivitySource = 6, ActivityReason = reason, CaptureTimeUncertain = true
        }, default);
        var entry = await db.ActivityLogs.SingleAsync();
        Assert.Contains(" | " + expected, entry.Details);
        Assert.Equal("REFUSED", entry.Status);
        Assert.Empty(await db.DeviceCommands.ToListAsync());
        Assert.Empty(await db.TelemetryRealtime.ToListAsync());
    }

    static ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    [Theory]
    [InlineData(6.82, 5.5, 6.5, 0, true, "PH_DOWN", 1.70, 1360)]
    [InlineData(4, 5.5, 6.5, 1, true, "PH_UP", 2, 1600)]
    [InlineData(0.4, 1, 2, 1, false, "NUTRIENT_UP", 5, 4000)]
    [InlineData(1.8, 0.5, 1.5, 0, false, "DILUTION_DOWN", 4.2, 3360)]
    public void Plan_HasSameBoundedFormulaAsFirmware(double value, double low, double high, int model, bool ph, string pump, double ml, int ms) {
        var plan = DosingPolicy.Plan((decimal)value, (decimal)low, (decimal)high, model, ph);
        Assert.Equal(pump, plan!.Pump); Assert.Equal((decimal)ml, plan.EstimatedMl); Assert.Equal(ms, plan.RuntimeMs);
    }
    [Theory]
    [InlineData(6, true)]
    [InlineData(-1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    public void InvalidOrNormalReadings_DoNotProduceDoses(double value, bool ph) =>
        Assert.Null(DosingPolicy.Plan((decimal)value, ph ? 5.5m : 1m, ph ? 6.5m : 2.5m, 2, ph));
    [Fact]
    public void MlOnlyNearTheBoundary_IsAttributedSeparately() {
        var plan = DosingPolicy.Plan(6.45m, 5.5m, 6.5m, 0, true);
        Assert.Equal("ML Recommendation", plan!.Source);
        Assert.Null(DosingPolicy.Plan(6.45m, 5.5m, 6.5m, 2, true));
    }
    [Fact]
    public async Task EdgeEvents_AreDeduplicatedAndKeepUncertainCaptureTime() {
        using var db = Database();
        var dto = new LoraCompactTelemetryDto { DeviceMac = "AABBCCDDEE81", Sequence = (1L << 62) + 1,
            ActivityPhase = 2, ActivityAction = 5, ActivitySource = 3, ActivityValue = 6.82m,
            ActivityMin = 5.5m, ActivityMax = 6.5m, ActivityDuration = 1360, ActivityCid = 42, CaptureTimeUncertain = true };
        await ActivityLogService.IngestAsync(db, dto, default); await ActivityLogService.IngestAsync(db, dto, default);
        var entry = await db.ActivityLogs.SingleAsync(); Assert.Null(entry.RecordedAt); Assert.True(entry.CaptureTimeUncertain);
        Assert.Equal("Threshold Auto-Dosing", entry.Source); Assert.Equal(1.7m, entry.EstimatedMl);
        Assert.Contains("0.25-pH shift limit is unverified", entry.Details); Assert.Contains("cid #42", entry.Details);
        Assert.Empty(await db.TelemetryRealtime.ToListAsync());
    }
    [Fact]
    public async Task QueuedAndAcknowledgedDose_HasSeparateCorrelatedAuditStatuses() {
        using var db = Database(); const string mac = "AA:BB:CC:DD:EE:82";
        db.Devices.Add(new Device { MacAddress = mac }); await db.SaveChangesAsync();
        var dose = await new DosingService(db).EnqueueCommandAsync(new() { DeviceMac = mac, DosingType = "PH_DOWN", TargetVolumeMl = 2 });
        var command = await db.DeviceCommands.SingleAsync();
        Assert.Equal("QUEUED", (await db.ActivityLogs.SingleAsync()).Status);
        var queue = new DeviceCommandQueue(db);
        await queue.AcknowledgeAsync(mac, command.CommandId, 1, 32, "executed", "accepted");
        await queue.AcknowledgeAsync(mac, command.CommandId, 1, 32, "executed", "accepted");
        await queue.AcknowledgeAsync(mac, command.CommandId, 1, 0, "timer_finished", "completed");
        Assert.Equal(3, await db.ActivityLogs.CountAsync());
        Assert.All(await db.ActivityLogs.ToListAsync(), x => Assert.Equal(command.CommandId, x.CommandId));
        await Assert.ThrowsAsync<ArgumentException>(() => new DosingService(db).EnqueueCommandAsync(new() { DeviceMac = mac, DosingType = "PH_UP", TargetVolumeMl = 10 }));
    }
    [Fact]
    public async Task CloudRecommendations_DoNotCreateDuplicateActuationCommands() {
        using var db = Database(); var service = new AutomationService(db);
        var dto = new TelemetryIngestDto { DeviceMac = "AA:BB:CC:DD:EE:83", Sequence = 1, Ph = 6.82m, Ec = 1.5m };
        dto.PrepareCapture(); await service.ObserveAsync(dto, 5.5m, 6.5m, 1m, 2.5m, default);
        dto.Sequence = 2; await service.ObserveAsync(dto, 5.5m, 6.5m, 1m, 2.5m, default);
        Assert.Single(await db.ActivityLogs.ToListAsync()); Assert.Empty(await db.DeviceCommands.ToListAsync());
    }
}
