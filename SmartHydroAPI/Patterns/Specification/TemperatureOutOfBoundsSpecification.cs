using SmartHydroAPI.Models.Dtos;

namespace SmartHydroAPI.Patterns.Specification
{
    // Evaluates whether ambient greenhouse temperature exceeds or falls below safe thresholds
    public class TemperatureOutOfBoundsSpecification : ISpecification<TelemetryEvaluationContext>
    {
        // Checks if temperature reading breaches threshold boundaries
        public bool IsSatisfiedBy(TelemetryEvaluationContext context)
        {
            var threshold = context.Thresholds.FirstOrDefault(t =>
                string.Equals(t.SensorType, "TEMPERATURE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.SensorType, "Temperature_C", StringComparison.OrdinalIgnoreCase));

            if (threshold is null) return false;

            var temp = context.Reading.TemperatureC;
            return temp >= -40m && temp <= 80m && (temp < threshold.MinValue || temp > threshold.MaxValue);
        }

        // Creates an alert descriptor for temperature deviations
        public CreateAlertDto CreateAlert(TelemetryEvaluationContext context)
        {
            var temp = context.Reading.TemperatureC;
            var threshold = context.Thresholds.FirstOrDefault(t =>
                string.Equals(t.SensorType, "TEMPERATURE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.SensorType, "Temperature_C", StringComparison.OrdinalIgnoreCase));

            var detail = threshold != null && temp < threshold.MinValue
                ? $"Ambient temperature {temp:F1}°C below minimum threshold {threshold.MinValue:F1}°C."
                : $"Ambient temperature {temp:F1}°C exceeded maximum threshold {threshold?.MaxValue:F1}°C.";

            return new CreateAlertDto
            {
                StructureId = context.Structure?.Id ?? 0,
                DeviceMac = context.Reading.DeviceMac,
                Component = "TEMPERATURE_SENSOR",
                Severity = "WARNING",
                Value = temp.ToString("F1"),
                Detail = detail
            };
        }
    }
}
