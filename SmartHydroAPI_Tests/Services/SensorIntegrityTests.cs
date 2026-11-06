using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;
using SmartHydroAPI.Controllers;
using SmartHydroAPI.Data;
using SmartHydroAPI.Hubs;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Patterns.Decorator;
using SmartHydroAPI.Patterns.Specification;
using SmartHydroAPI.Patterns.Strategy;
using SmartHydroAPI.Patterns.Repository;
using SmartHydroAPI.Patterns.Observer;
using SmartHydroAPI.Services;
using System.Text.Json;

namespace SmartHydroAPI_Tests.Services;

public class SensorIntegrityTests
{
    [Theory]
    [InlineData(-40, true)]
    [InlineData(-20, true)]
    [InlineData(0, true)]
    [InlineData(80, true)]
    [InlineData(-41, false)]
    [InlineData(81, false)]
    [InlineData(-99, false)]
    public void Dht22Temperature_AcceptsPhysicalRange_AndRejectsFaults(int temperature, bool valid)
    {
        var reading = new FireBeetleC6TelemetryStrategy().ProcessReading(
            new TelemetryIngestDto { Temperature = temperature }, DateTime.UtcNow);
        Assert.Equal(valid ? temperature : -99m, reading.TemperatureC);
        var context = new TelemetryEvaluationContext(reading, null, new[]
        {
            new Threshold { SensorType = "TEMPERATURE", MinValue = 18m, MaxValue = 28m }
        });
        Assert.Equal(valid, new TemperatureOutOfBoundsSpecification().IsSatisfiedBy(context));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task History_ExcludesFaultedSamples_AndKeepsUnavailableDaysExplicit(bool includeValid)
    {
        const string mac = "AA:BB:CC:DD:EE:92";
        using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        database.TelemetryRealtime.Add(new TelemetryRealtime
        {
            DeviceMac = mac, PhLevel = -1m, EcMs = -1m, TemperatureC = -99m,
            HumidityPercent = -1m, LightLux = -1m, RecordedAt = DateTime.UtcNow
        });
        if (includeValid) database.TelemetryRealtime.Add(new TelemetryRealtime
        {
            DeviceMac = mac, PhLevel = 6m, EcMs = 1.4m, TemperatureC = -20m,
            HumidityPercent = 65m, LightLux = 50m, RecordedAt = DateTime.UtcNow
        });
        await database.SaveChangesAsync();
        var repository = new Mock<ITelemetryRepository>();
        repository.Setup(x => x.GetDailyHistoryAsync(mac, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TelemetryDailyHistory>());
        var service = new TelemetryService(repository.Object, Mock.Of<IActuatorRepository>(),
            Mock.Of<ITelemetryStrategyFactory>(), Mock.Of<IEventPublisher>(), database);

        var day = Assert.Single(await service.GetHistoryAsync(mac, 1));

        Assert.Equal(includeValid ? 6m : -1m, day.AvgPh);
        Assert.Equal(includeValid ? 1.4m : -1m, day.AvgEc);
        Assert.Equal(includeValid ? -20m : -99m, day.AvgTemperatureC);
        Assert.Equal(includeValid ? 65m : -1m, day.AvgHumidityPercent);
        Assert.Equal(includeValid ? 50m : -1m, day.AvgLight);
    }

    [Fact]
    public async Task MissingLoraMeasurements_StayUnavailableAndBroadcastFaults()
    {
        using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        TelemetryIngestDto? received = null;
        var service = new Mock<ITelemetryService>();
        service.Setup(x => x.IngestAndSyncAsync(It.IsAny<TelemetryIngestDto>(), It.IsAny<CancellationToken>()))
            .Callback<TelemetryIngestDto, CancellationToken>((dto, _) => received = dto)
            .ReturnsAsync((ActuatorStateResponseDto)null!);
        object? broadcast = null;
        var proxy = new Mock<IClientProxy>();
        proxy.Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((name, args, _) =>
            {
                if (name == "OnTelemetryUpdated") broadcast = args[0];
            }).Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>();
        clients.SetupGet(x => x.All).Returns(proxy.Object);
        clients.Setup(x => x.Group(It.IsAny<string>())).Returns(proxy.Object);
        var hub = new Mock<IHubContext<ActuatorHub>>();
        hub.SetupGet(x => x.Clients).Returns(clients.Object);
        var controller = new TelemetryController(service.Object, database, hubContext: hub.Object);

        await controller.IngestLoraCompact(new LoraCompactTelemetryDto { DeviceMac = "AA:BB:CC:DD:EE:91" }, default);

        Assert.NotNull(received);
        var stored = new FireBeetleC6TelemetryStrategy().ProcessReading(received!, DateTime.UtcNow);
        Assert.Equal(-1m, stored.PhLevel);
        Assert.Equal(-1m, stored.EcMs);
        Assert.Equal(-99m, stored.TemperatureC);
        Assert.Equal(-1m, stored.HumidityPercent);
        Assert.Equal(-1m, stored.LightLux);
        var json = JsonSerializer.SerializeToElement(broadcast);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("ph").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("ec").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("temperature").ValueKind);
        Assert.Equal(5, json.GetProperty("sensorFaults").GetArrayLength());
    }

    [Theory]
    [InlineData(-1, -1, -99, false)]
    [InlineData(0, 0, -5, true)]
    [InlineData(15, 16, -99, false)]
    public void InvalidMeasurements_DoNotTriggerThresholdAlerts(double ph, double ec, double temperature, bool expected)
    {
        var reading = new TelemetryRealtime { PhLevel = (decimal)ph, EcMs = (decimal)ec, TemperatureC = (decimal)temperature };
        var context = new TelemetryEvaluationContext(reading, null, new[]
        {
            new Threshold { SensorType = "PH", MinValue = 5.5m, MaxValue = 6.5m },
            new Threshold { SensorType = "EC", MinValue = 1m, MaxValue = 2m },
            new Threshold { SensorType = "TEMPERATURE", MinValue = 18m, MaxValue = 28m }
        });
        Assert.Equal(expected, new PhOutOfBoundsSpecification().IsSatisfiedBy(context));
        Assert.Equal(expected, new EcOutOfBoundsSpecification().IsSatisfiedBy(context));
        Assert.Equal(expected, new TemperatureOutOfBoundsSpecification().IsSatisfiedBy(context));
    }

    [Fact]
    public void ValidZeroReadings_AreNotReplacedWithFaultSentinels()
    {
        var reading = new FireBeetleC6TelemetryStrategy().ProcessReading(new TelemetryIngestDto
        {
            Ph = 0m, Ec = 0m, Light = 0m, Temperature = -5m, Humidity = 0m
        }, DateTime.UtcNow);
        Assert.Equal(0m, reading.PhLevel);
        Assert.Equal(0m, reading.EcMs);
        Assert.Equal(0m, reading.LightLux);
        Assert.Equal(-5m, reading.TemperatureC);
        Assert.Equal(0m, reading.HumidityPercent);
    }
}
