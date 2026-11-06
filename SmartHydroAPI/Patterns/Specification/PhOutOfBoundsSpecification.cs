using SmartHydroAPI.Models.Dtos;

namespace SmartHydroAPI.Patterns.Specification
{
    // Evaluates whether current pH level violates defined safe cultivation thresholds
    public class PhOutOfBoundsSpecification : ISpecification<TelemetryEvaluationContext>
    {
        // Checks if pH is outside the safe min/max boundary
        public bool IsSatisfiedBy(TelemetryEvaluationContext context)
        {
            var threshold = context.Thresholds.FirstOrDefault(t =>
                string.Equals(t.SensorType, "PH", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.SensorType, "pH_Level", StringComparison.OrdinalIgnoreCase));

            if (threshold is null) return false;

            var ph = context.Reading.PhLevel;
            return ph >= 0m && ph <= 14m && (ph < threshold.MinValue || ph > threshold.MaxValue);
        }

        // Creates an alert descriptor for pH boundary violations
        public CreateAlertDto CreateAlert(TelemetryEvaluationContext context)
        {
            var ph = context.Reading.PhLevel;
            var threshold = context.Thresholds.FirstOrDefault(t =>
                string.Equals(t.SensorType, "PH", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.SensorType, "pH_Level", StringComparison.OrdinalIgnoreCase));

            var detail = threshold != null && ph < threshold.MinValue
                ? $"pH reading {ph:F1} dropped below safe minimum {threshold.MinValue:F1}."
                : $"pH reading {ph:F1} exceeded safe maximum {threshold?.MaxValue:F1}.";

            return new CreateAlertDto
            {
                StructureId = context.Structure?.Id ?? 0,
                DeviceMac = context.Reading.DeviceMac,
                Component = "PH_SENSOR",
                Severity = "CRITICAL",
                Value = ph.ToString("F1"),
                Detail = detail
            };
        }
    }
}
