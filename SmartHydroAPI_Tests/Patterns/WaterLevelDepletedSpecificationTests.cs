using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Patterns.Specification;

namespace SmartHydroAPI.Tests.Patterns
{
    public class WaterLevelDepletedSpecificationTests
    {
        private readonly WaterLevelDepletedSpecification _specification = new();

        // Builds an evaluation context for a reading with the given water sensor values and no thresholds
        private static TelemetryEvaluationContext BuildContext(bool detected, int raw)
        {
            var reading = new TelemetryRealtime
            {
                DeviceMac = "11:22:33:44:55:66",
                WaterLevelDetected = detected,
                WaterLevelRaw = raw
            };

            return new TelemetryEvaluationContext(reading, new Structure { Id = 3 }, new List<Threshold>());
        }

        // When the float sensor reports no water the reservoir counts as empty whatever the raw value is
        [Fact]
        public void IsSatisfiedBy_WaterNotDetected_ReturnsTrue()
        {
            var context = BuildContext(false, 900);

            Assert.True(_specification.IsSatisfiedBy(context));
        }

        // A raw reading at or below 200 counts as empty even if the float sensor says water is present
        [Theory]
        [InlineData(0)]
        [InlineData(150)]
        [InlineData(200)]
        public void IsSatisfiedBy_RawAtOrBelowLimit_ReturnsTrue(int raw)
        {
            var context = BuildContext(true, raw);

            Assert.True(_specification.IsSatisfiedBy(context));
        }

        // Water detected with a raw reading above 200 is a healthy reservoir
        [Theory]
        [InlineData(201)]
        [InlineData(2000)]
        public void IsSatisfiedBy_WaterPresent_ReturnsFalse(int raw)
        {
            var context = BuildContext(true, raw);

            Assert.False(_specification.IsSatisfiedBy(context));
        }

        // The depletion alert should be critical, point at the reservoir and carry the raw reading
        [Fact]
        public void CreateAlert_BuildsCriticalReservoirAlert()
        {
            var context = BuildContext(false, 120);

            var alert = _specification.CreateAlert(context);

            Assert.Equal(3, alert.StructureId);
            Assert.Equal("WATER_RESERVOIR", alert.Component);
            Assert.Equal("CRITICAL", alert.Severity);
            Assert.Equal("120", alert.Value);
        }
    }
}