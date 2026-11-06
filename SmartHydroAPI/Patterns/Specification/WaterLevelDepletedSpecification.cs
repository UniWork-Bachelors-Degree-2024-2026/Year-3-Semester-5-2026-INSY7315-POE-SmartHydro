using SmartHydroAPI.Models.Dtos;

namespace SmartHydroAPI.Patterns.Specification
{
    // Evaluates whether reservoir water level is depleted below the critical float/optical sensor limit
    public class WaterLevelDepletedSpecification : ISpecification<TelemetryEvaluationContext>
    {
        // Checks if water level flag is false or raw ADC value is below empty threshold
        public bool IsSatisfiedBy(TelemetryEvaluationContext context)
        {
            if (!context.Reading.WaterLevelDetected)
                return true;

            if (context.Reading.WaterLevelRaw <= 200)
                return true;

            return false;
        }

        // Creates an emergency alert descriptor for reservoir depletion
        public CreateAlertDto CreateAlert(TelemetryEvaluationContext context)
        {
            return new CreateAlertDto
            {
                StructureId = context.Structure?.Id ?? 0,
                DeviceMac = context.Reading.DeviceMac,
                Component = "WATER_RESERVOIR",
                Severity = "CRITICAL",
                Value = context.Reading.WaterLevelRaw.ToString(),
                Detail = "Main nutrient reservoir water level depleted! Main pump dry-run risk."
            };
        }
    }
}
