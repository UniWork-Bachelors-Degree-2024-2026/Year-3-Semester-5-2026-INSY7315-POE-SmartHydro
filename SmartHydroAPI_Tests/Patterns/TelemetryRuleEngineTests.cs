using Microsoft.EntityFrameworkCore;
using Moq;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Patterns.Specification;

namespace SmartHydroAPI.Tests.Patterns
{
    public class TelemetryRuleEngineTests
    {
        private const string Mac = "AA:BB:CC:DD:EE:FF";

        // Creates a fresh in-memory database per test so tests never share data
        private static ApplicationDbContext CreateDatabase()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            return new ApplicationDbContext(options);
        }

        // Adds a hydroponic tower with one sensor device attached to it
        private static void SeedDeviceOnStructure(ApplicationDbContext database)
        {
            database.Structures.Add(new Structure { Id = 1, Name = "Tower A", StructureType = "VERTICAL_TOWER" });
            database.Devices.Add(new Device { MacAddress = Mac, DeviceName = "Node 1", StructureId = 1 });
            database.SaveChanges();
        }

        // A device that is not assigned to a structure should produce no alerts and never ask the specifications
        [Fact]
        public async Task EvaluateRulesAsync_DeviceWithoutStructure_ReturnsNoAlerts()
        {
            using var database = CreateDatabase();
            database.Devices.Add(new Device { MacAddress = Mac, DeviceName = "Loose node" });
            database.SaveChanges();
            var specification = new Mock<ISpecification<TelemetryEvaluationContext>>();
            var engine = new TelemetryRuleEngine(database, new[] { specification.Object });

            var alerts = await engine.EvaluateRulesAsync(new TelemetryRealtime { DeviceMac = Mac });

            Assert.Empty(alerts);
            specification.Verify(s => s.IsSatisfiedBy(It.IsAny<TelemetryEvaluationContext>()), Times.Never);
        }

        // Only specifications that are satisfied should add an alert to the result
        [Fact]
        public async Task EvaluateRulesAsync_OnlySatisfiedSpecificationsCreateAlerts()
        {
            using var database = CreateDatabase();
            SeedDeviceOnStructure(database);
            var triggered = new Mock<ISpecification<TelemetryEvaluationContext>>();
            triggered.Setup(s => s.IsSatisfiedBy(It.IsAny<TelemetryEvaluationContext>())).Returns(true);
            triggered.Setup(s => s.CreateAlert(It.IsAny<TelemetryEvaluationContext>()))
                .Returns(new CreateAlertDto { Component = "TEST" });
            var quiet = new Mock<ISpecification<TelemetryEvaluationContext>>();
            quiet.Setup(s => s.IsSatisfiedBy(It.IsAny<TelemetryEvaluationContext>())).Returns(false);
            var engine = new TelemetryRuleEngine(database, new[] { triggered.Object, quiet.Object });

            var alerts = await engine.EvaluateRulesAsync(new TelemetryRealtime { DeviceMac = Mac });

            var alert = Assert.Single(alerts);
            Assert.Equal("TEST", alert.Component);
            quiet.Verify(s => s.CreateAlert(It.IsAny<TelemetryEvaluationContext>()), Times.Never);
        }

        // A range saved for this structure should be used instead of the default when judging a reading
        [Fact]
        public async Task EvaluateRulesAsync_UsesSavedStructureThreshold()
        {
            using var database = CreateDatabase();
            SeedDeviceOnStructure(database);
            database.Thresholds.Add(new Threshold { StructureId = 1, SensorType = "PH", MinValue = 7.0m, MaxValue = 8.0m });
            database.SaveChanges();
            var engine = new TelemetryRuleEngine(database, new[] { new PhOutOfBoundsSpecification() });

            var alerts = await engine.EvaluateRulesAsync(new TelemetryRealtime { DeviceMac = Mac, PhLevel = 6.0m });

            var alert = Assert.Single(alerts);
            Assert.Equal("PH_SENSOR", alert.Component);
        }

        // A range saved for a different structure must not affect this structure's readings
        [Fact]
        public async Task EvaluateRulesAsync_IgnoresOtherStructuresThresholds()
        {
            using var database = CreateDatabase();
            SeedDeviceOnStructure(database);
            database.Thresholds.Add(new Threshold { StructureId = 2, SensorType = "PH", MinValue = 7.0m, MaxValue = 8.0m });
            database.SaveChanges();
            var engine = new TelemetryRuleEngine(database, new[] { new PhOutOfBoundsSpecification() });

            var alerts = await engine.EvaluateRulesAsync(new TelemetryRealtime { DeviceMac = Mac, PhLevel = 6.0m });

            Assert.Empty(alerts);
        }
    }
}