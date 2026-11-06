using Group_1_SmartHydro_MVC.Models;
using Group_1_SmartHydro_MVC.Services;

namespace Group_1_SmartHydro_MVC_Tests.Services;

public class SensorIntegrityTests
{
    [Theory]
    [InlineData(-40)]
    [InlineData(-20)]
    [InlineData(80)]
    public void Dht22Temperature_AtSupportedExtremes_RemainsAReading(int temperature)
    {
        var card = new DashboardCardViewModel { LinkAction = "Temperature" };
        SensorBindings.FillCards(new[] { card }, new StructureReadings
        {
            HasTelemetry = true, TemperatureC = temperature
        });
        Assert.NotEqual("Sensor Fault", card.Value);
        Assert.Equal("\u00B0C", card.Unit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void Light_ValidEndpoints_AreRelativePercentages(int percent)
    {
        var card = new DashboardCardViewModel { LinkAction = "Light" };
        SensorBindings.FillCards(new[] { card }, new StructureReadings
        {
            HasTelemetry = true, LightLux = percent
        });
        Assert.NotEqual("Sensor Fault", card.Value);
        Assert.Equal("%", card.Unit);
    }

    [Fact]
    public void SensorDetail_FaultedPh_ClearsUnitAndSkipsInvalidTrend()
    {
        var model = new SensorDetailViewModel { HeroUnit = "pH", ShowTrend = true };
        SensorBindings.FillSensor(model, SensorBindings.For("CleanWater"), new StructureReadings
        {
            HasTelemetry = true, Ph = -1m,
            Trend = new() { new TrendPoint(DateOnly.FromDateTime(DateTime.UtcNow), -1m, 1m, 50m, 25m, 60m) }
        }, false);
        Assert.Equal("Waiting for a valid reading", model.HeroValue);
        Assert.Equal("No previous reading available", model.PhReadingLabel);
        Assert.Equal("", model.HeroUnit);
        Assert.Empty(model.TrendValues);
    }

    [Theory]
    [InlineData("CleanWater")]
    [InlineData("Nutrients")]
    [InlineData("Temperature")]
    [InlineData("Humidity")]
    [InlineData("Light")]
    public void Dashboard_FaultedMeasurement_ShowsUnavailableReading(string action)
    {
        var card = new DashboardCardViewModel { LinkAction = action };
        SensorBindings.FillCards(new[] { card }, new StructureReadings
        {
            HasTelemetry = true, Ph = -1m, EcMs = -1m, TemperatureC = -99m,
            HumidityPercent = -1m, LightLux = -1m
        });
        Assert.Equal(action == "CleanWater" ? "Waiting for a valid reading" : "Sensor Fault", card.Value);
    }

    [Fact]
    public void Light_ReportsPercentageAndEc_ReportsMicrosiemens()
    {
        var light = new DashboardCardViewModel { LinkAction = "Light" };
        var ec = new DashboardCardViewModel { LinkAction = "Nutrients" };
        SensorBindings.FillCards(new[] { light, ec }, new StructureReadings
        {
            HasTelemetry = true, LightLux = 50m, EcMs = 1.413m
        });
        Assert.Equal("50", light.Value);
        Assert.Equal("%", light.Unit);
        Assert.Equal("1413", ec.Value);
        Assert.Equal("\u00B5S/cm", ec.Unit);
    }
}
