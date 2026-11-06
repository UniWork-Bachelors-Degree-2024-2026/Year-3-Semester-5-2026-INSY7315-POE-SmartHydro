using System.Globalization;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Patterns.Specification;

namespace SmartHydroAPI.Tests.Patterns
{
    public class EcOutOfBoundsSpecificationTests
    {
        private readonly EcOutOfBoundsSpecification _specification = new();

        // Fixes the culture so decimal values format with a dot on any machine, including en-ZA which uses a comma
        public EcOutOfBoundsSpecificationTests()
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        }

        // Builds an evaluation context holding one reading with the given value and a 1.0 to 2.5 EC range
        private static TelemetryEvaluationContext BuildContext(decimal value)
        {
            var reading = new TelemetryRealtime { DeviceMac = "AA:BB:CC:DD:EE:FF", EcMs = value };
            var structure = new Structure { Id = 7, Name = "Tower A" };
            var thresholds = new List<Threshold>
            {
                new Threshold { StructureId = 7, SensorType = "EC", MinValue = 1.0m, MaxValue = 2.5m }
            };

            return new TelemetryEvaluationContext(reading, structure, thresholds);
        }

        // A value inside the range, or exactly on either edge, should not raise an alert
        [Theory]
        [InlineData(1.0)]
        [InlineData(1.8)]
        [InlineData(2.5)]
        public void IsSatisfiedBy_ValueInsideRange_ReturnsFalse(double value)
        {
            var context = BuildContext((decimal)value);

            var result = _specification.IsSatisfiedBy(context);

            Assert.False(result);
        }

        // A value below the minimum or above the maximum should raise an alert
        [Theory]
        [InlineData(0.9)]
        [InlineData(2.6)]
        public void IsSatisfiedBy_ValueOutsideRange_ReturnsTrue(double value)
        {
            var context = BuildContext((decimal)value);

            var result = _specification.IsSatisfiedBy(context);

            Assert.True(result);
        }

        // With no EC range configured the rule cannot be judged, so it should stay quiet
        [Fact]
        public void IsSatisfiedBy_NoThreshold_ReturnsFalse()
        {
            var reading = new TelemetryRealtime { EcMs = 999m };
            var context = new TelemetryEvaluationContext(reading, null, new List<Threshold>());

            var result = _specification.IsSatisfiedBy(context);

            Assert.False(result);
        }

        // A low reading alert should name the sensor, use the right severity and say it dropped below the minimum
        [Fact]
        public void CreateAlert_ValueTooLow_BuildsBelowMinimumAlert()
        {
            var context = BuildContext(0.4m);

            var alert = _specification.CreateAlert(context);

            Assert.Equal(7, alert.StructureId);
            Assert.Equal("AA:BB:CC:DD:EE:FF", alert.DeviceMac);
            Assert.Equal("EC_SENSOR", alert.Component);
            Assert.Equal("WARNING", alert.Severity);
            Assert.Equal("0.40", alert.Value);
            Assert.Contains("dropped below minimum threshold", alert.Detail);
        }

        // A high reading alert should say the value exceeded the maximum
        [Fact]
        public void CreateAlert_ValueTooHigh_DescribesExceededMaximum()
        {
            var context = BuildContext(3.1m);

            var alert = _specification.CreateAlert(context);

            Assert.Contains("exceeded maximum threshold", alert.Detail);
        }
    }
}