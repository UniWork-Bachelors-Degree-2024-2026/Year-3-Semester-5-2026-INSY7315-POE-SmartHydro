using SmartHydroAPI.Models.Dtos;

namespace SmartHydroAPI.Patterns.Specification
{
    // Evaluates whether Electrical Conductivity (EC in mS/cm) exceeds or drops below safe thresholds
    public class EcOutOfBoundsSpecification : ISpecification<TelemetryEvaluationContext>
    {
        // Checks if EC exceeds configured threshold boundaries
        public bool IsSatisfiedBy(TelemetryEvaluationContext context)
        {
            var threshold = context.Thresholds.FirstOrDefault(t =>
                string.Equals(t.SensorType, "EC", StringComparison.OrdinalIgnoreCase));

            if (threshold is null) return false;

            var ec = context.Reading.EcMs;
            return ec >= 0m && ec <= 15m && (ec < threshold.MinValue || ec > threshold.MaxValue);
        }

        // Creates an alert descriptor for EC nutrient concentration violations
        public CreateAlertDto CreateAlert(TelemetryEvaluationContext context)
        {
            var ec = context.Reading.EcMs;
            var threshold = context.Thresholds.FirstOrDefault(t =>
                string.Equals(t.SensorType, "EC", StringComparison.OrdinalIgnoreCase));

            var detail = threshold != null && ec < threshold.MinValue
                ? $"Nutrient EC level {ec:F2} mS/cm dropped below minimum threshold {threshold.MinValue:F2} mS/cm."
                : $"Nutrient EC level {ec:F2} mS/cm exceeded maximum threshold {threshold?.MaxValue:F2} mS/cm.";

            return new CreateAlertDto
            {
                StructureId = context.Structure?.Id ?? 0,
                DeviceMac = context.Reading.DeviceMac,
                Component = "EC_SENSOR",
                Severity = "WARNING",
                Value = ec.ToString("F2"),
                Detail = detail
            };
        }
    }
}
