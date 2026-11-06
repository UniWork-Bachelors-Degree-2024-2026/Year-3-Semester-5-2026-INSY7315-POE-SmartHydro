using System.Globalization;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Patterns.Specification;

namespace SmartHydroAPI.Tests.Patterns
{
    public class TemperatureOutOfBoundsSpecificationTests
    {
        private readonly TemperatureOutOfBoundsSpecification _specification = new();

        // Fixes the culture so decimal values format with a dot on any machine, including en-ZA which uses a comma
        public TemperatureOutOfBoundsSpecificationTests()
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        }

        // Builds an evaluation context holding one reading with the given value and a 18 to 28 TEMPERATURE range
        private static TelemetryEvaluationContext BuildContext(decimal value)
        {
            var reading = new TelemetryRealtime { DeviceMac = "AA:BB:CC:DD:EE:FF", TemperatureC = value };
            var structure = new Structure { Id = 7, Name = "Tower A" };
            var thresholds = new List<Threshold>
            {
                new Threshold { StructureId = 7, SensorType = "TEMPERATURE", MinValue = 18m, MaxValue = 28m }
            };

            return new TelemetryEvaluationContext(reading, structure, thresholds);
        }

        // A value inside the range, or exactly on either edge, should not raise an alert
        [Theory]
        [InlineData(18.0)]
        [InlineData(23.0)]
        [InlineData(28.0)]
        public void IsSatisfiedBy_ValueInsideRange_ReturnsFalse(double value)
        {
            var context = BuildContext((decimal)value);

            var result = _specification.IsSatisfiedBy(context);

            Assert.False(result);
        }

        // A value below the minimum or above the maximum should raise an alert
        [Theory]
        [InlineData(17.9)]
        [InlineData(28.1)]
        public void IsSatisfiedBy_ValueOutsideRange_ReturnsTrue(double value)
        {
            var context = BuildContext((decimal)value);

            var result = _specification.IsSatisfiedBy(context);

            Assert.True(result);
        }

        // With no TEMPERATURE range configured the rule cannot be judged, so it should stay quiet
        [Fact]
        public void IsSatisfiedBy_NoThreshold_ReturnsFalse()
        {
            var reading = new TelemetryRealtime { TemperatureC = 999m };
            var context = new TelemetryEvaluationContext(reading, null, new List<Threshold>());

            var result = _specification.IsSatisfiedBy(context);

            Assert.False(result);
        }

        // The older "Temperature_C" sensor name should be matched the same as "TEMPERATURE"
        [Fact]
        public void IsSatisfiedBy_LegacySensorName_IsRecognised()
        {
            var reading = new TelemetryRealtime { TemperatureC = 35.0m };
            var thresholds = new List<Threshold>
            {
                new Threshold { SensorType = "Temperature_C", MinValue = 18m, MaxValue = 28m }
            };
            var context = new TelemetryEvaluationContext(reading, null, thresholds);

            var result = _specification.IsSatisfiedBy(context);

            Assert.True(result);
        }

        // A low reading alert should name the sensor, use the right severity and say it dropped below the minimum
        [Fact]
        public void CreateAlert_ValueTooLow_BuildsBelowMinimumAlert()
        {
            var context = BuildContext(12.5m);

            var alert = _specification.CreateAlert(context);

            Assert.Equal(7, alert.StructureId);
            Assert.Equal("AA:BB:CC:DD:EE:FF", alert.DeviceMac);
            Assert.Equal("TEMPERATURE_SENSOR", alert.Component);
            Assert.Equal("WARNING", alert.Severity);
            Assert.Equal("12.5", alert.Value);
            Assert.Contains("below minimum threshold", alert.Detail);
        }

        // A high reading alert should say the value exceeded the maximum
        [Fact]
        public void CreateAlert_ValueTooHigh_DescribesExceededMaximum()
        {
            var context = BuildContext(35.0m);

            var alert = _specification.CreateAlert(context);

            Assert.Contains("exceeded maximum threshold", alert.Detail);
        }
    }
}