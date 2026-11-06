using System.Globalization;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Patterns.Specification;

namespace SmartHydroAPI.Tests.Patterns
{
    public class PhOutOfBoundsSpecificationTests
    {
        private readonly PhOutOfBoundsSpecification _specification = new();

        // Fixes the culture so decimal values format with a dot on any machine, including en-ZA which uses a comma
        public PhOutOfBoundsSpecificationTests()
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        }

        // Builds an evaluation context holding one reading with the given pH and a 5.5 to 6.5 pH range
        private static TelemetryEvaluationContext BuildContext(decimal ph)
        {
            var reading = new TelemetryRealtime { DeviceMac = "AA:BB:CC:DD:EE:FF", PhLevel = ph };
            var structure = new Structure { Id = 7, Name = "Tower A" };
            var thresholds = new List<Threshold>
            {
                new Threshold { StructureId = 7, SensorType = "PH", MinValue = 5.5m, MaxValue = 6.5m }
            };

            return new TelemetryEvaluationContext(reading, structure, thresholds);
        }

        // A pH inside the range, or exactly on either edge, should not raise an alert
        [Theory]
        [InlineData(5.5)]
        [InlineData(6.0)]
        [InlineData(6.5)]
        public void IsSatisfiedBy_PhInsideRange_ReturnsFalse(double ph)
        {
            var context = BuildContext((decimal)ph);

            var result = _specification.IsSatisfiedBy(context);

            Assert.False(result);
        }

        // A pH below the minimum or above the maximum should raise an alert
        [Theory]
        [InlineData(5.4)]
        [InlineData(6.6)]
        [InlineData(0.0)]
        [InlineData(14.0)]
        public void IsSatisfiedBy_PhOutsideRange_ReturnsTrue(double ph)
        {
            var context = BuildContext((decimal)ph);

            var result = _specification.IsSatisfiedBy(context);

            Assert.True(result);
        }

        // With no pH range configured the rule cannot be judged, so it should stay quiet
        [Fact]
        public void IsSatisfiedBy_NoPhThreshold_ReturnsFalse()
        {
            var reading = new TelemetryRealtime { PhLevel = 1.0m };
            var context = new TelemetryEvaluationContext(reading, null, new List<Threshold>());

            var result = _specification.IsSatisfiedBy(context);

            Assert.False(result);
        }

        // The older "pH_Level" sensor name should be matched the same as "PH"
        [Fact]
        public void IsSatisfiedBy_LegacySensorName_IsRecognised()
        {
            var reading = new TelemetryRealtime { PhLevel = 9.0m };
            var thresholds = new List<Threshold>
            {
                new Threshold { SensorType = "pH_Level", MinValue = 5.5m, MaxValue = 6.5m }
            };
            var context = new TelemetryEvaluationContext(reading, null, thresholds);

            var result = _specification.IsSatisfiedBy(context);

            Assert.True(result);
        }

        // A low pH alert should be critical, name the PH sensor and say the reading dropped below the minimum
        [Fact]
        public void CreateAlert_PhTooLow_BuildsCriticalBelowMinimumAlert()
        {
            var context = BuildContext(4.2m);

            var alert = _specification.CreateAlert(context);

            Assert.Equal(7, alert.StructureId);
            Assert.Equal("AA:BB:CC:DD:EE:FF", alert.DeviceMac);
            Assert.Equal("PH_SENSOR", alert.Component);
            Assert.Equal("CRITICAL", alert.Severity);
            Assert.Equal("4.2", alert.Value);
            Assert.Contains("below safe minimum", alert.Detail);
        }

        // A high pH alert should say the reading exceeded the maximum
        [Fact]
        public void CreateAlert_PhTooHigh_DescribesExceededMaximum()
        {
            var context = BuildContext(7.8m);

            var alert = _specification.CreateAlert(context);

            Assert.Contains("exceeded safe maximum", alert.Detail);
        }

        // An alert for a device with no structure should fall back to structure id 0
        [Fact]
        public void CreateAlert_NoStructure_UsesZeroStructureId()
        {
            var reading = new TelemetryRealtime { PhLevel = 9.0m };
            var thresholds = new List<Threshold>
            {
                new Threshold { SensorType = "PH", MinValue = 5.5m, MaxValue = 6.5m }
            };
            var context = new TelemetryEvaluationContext(reading, null, thresholds);

            var alert = _specification.CreateAlert(context);

            Assert.Equal(0, alert.StructureId);
        }
    }
}