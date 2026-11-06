using Group_1_SmartHydro_MVC.Models;
using Group_1_SmartHydro_MVC.Services;
using Group_1_SmartHydro_MVC.Tests.TestSupport;

namespace Group_1_SmartHydro_MVC_Tests.Services;

public class PhReadingDisplayTests
{
    [Fact]
    public async Task DashboardClientKeepsTheCurrentFaultSeparateFromRetainedMetadata()
    {
        await using var api = await FakeApiServer.StartAsync();
        api.ResponseBody = """[{"deviceMac":"FC:01:2C:EE:29:F0","phLevel":-1,"recordedAt":"2026-11-02T07:00:00Z","phDisplayFallback":{"value":6.2,"recordedAt":"2026-11-02T06:55:00Z","ageSeconds":300}}]""";
        var readings = await new TelemetryApiClient(api.CreateClient()).GetDashboardReadingsAsync(
            new() { Id = "13", MacAddress = "FC:01:2C:EE:29:F0" }, "test-token");
        Assert.Equal(-1m, readings.Ph); Assert.Equal(6.2m, readings.PhDisplayFallback!.Value);
        var card = new DashboardCardViewModel { LinkAction = "CleanWater" };
        SensorBindings.FillCards(new[] { card }, readings);
        Assert.Equal("6.2", card.Value); Assert.Equal("pH", card.Unit);
        Assert.StartsWith("Last reading, ", card.ValueCaption);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(240)]
    public void RetainedNumberUsesExistingLayoutAndShortAgeLabel(int minutes)
    {
        var readings = new StructureReadings { HasTelemetry = true, Ph = -1,
            RecordedAt = DateTime.UtcNow, PhDisplayFallback = new(6.2m, DateTime.UtcNow.AddMinutes(-minutes), minutes * 60) };
        var page = new SensorDetailViewModel { HeroUnit = "pH", ShowTrend = false };
        SensorBindings.FillSensor(page, new(SensorMetric.Ph, "ph"), readings, true);
        Assert.Equal("6.2", page.HeroValue); Assert.Equal("pH", page.HeroUnit);
        Assert.Equal($"Last reading, {minutes} minutes ago", page.PhReadingLabel);
        Assert.Empty(page.MeasurementNotice); Assert.Equal(-1m, readings.Ph);
        Assert.DoesNotContain("fallback", page.PhReadingLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingHistoryDoesNotDisplayInventedNumber()
    {
        var page = new SensorDetailViewModel { ShowTrend = false };
        SensorBindings.FillSensor(page, new(SensorMetric.Ph, "ph"), new() { HasTelemetry = true, Ph = -1 }, true);
        Assert.Equal("Waiting for a valid reading", page.HeroValue);
        Assert.Equal("No previous reading available", page.PhReadingLabel); Assert.Empty(page.HeroUnit);
    }

    [Fact]
    public void FreshReadingTakesPrecedenceOverRetainedNumber()
    {
        var page = new SensorDetailViewModel { ShowTrend = false, HeroUnit = "pH" };
        SensorBindings.FillSensor(page, new(SensorMetric.Ph, "ph"), new() { HasTelemetry = true, Ph = 6.8m,
            PhDisplayFallback = new(6.2m, DateTime.UtcNow.AddMinutes(-5), 300) }, true);
        Assert.Equal("6.8", page.HeroValue); Assert.Empty(page.PhReadingLabel);
    }

    [Theory]
    [InlineData("CleanWater")]
    [InlineData("Nutrients")]
    public void PhFaultWithoutHistoryRequiresManualConfirmationOnBothDosePages(string sensor)
    {
        var page = new SensorDetailViewModel { ShowTrend = false };
        SensorBindings.FillSensor(page, SensorBindings.For(sensor), new() { HasTelemetry = true, Ph = -1 }, true);
        Assert.True(page.ManualPhOverrideRequired);
        Assert.Null(page.PhDisplayFallback);
    }

    [Fact]
    public void FodderDoesNotAcquireTheTowerManualOverride()
    {
        var page = new SensorDetailViewModel { ShowTrend = false, ManualPhOverrideAllowed = false };
        SensorBindings.FillSensor(page, SensorBindings.For("Nutrients"), new() { HasTelemetry = true, Ph = -1 }, true);
        Assert.False(page.ManualPhOverrideRequired);
    }
}
