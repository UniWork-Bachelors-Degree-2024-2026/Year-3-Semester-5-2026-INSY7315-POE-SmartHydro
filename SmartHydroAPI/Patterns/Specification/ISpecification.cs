using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Patterns.Specification
{
    // Context container passed to specifications during telemetry evaluation
    public record TelemetryEvaluationContext(TelemetryRealtime Reading, Structure? Structure, IEnumerable<Threshold> Thresholds);

    // Specification interface defining threshold and anomaly evaluation rules over incoming telemetry
    public interface ISpecification<in TContext>
    {
        // Evaluates whether the specification condition is met (e.g. threshold violated)
        bool IsSatisfiedBy(TContext context);

        // Produces an alert DTO when the specification condition is satisfied
        CreateAlertDto CreateAlert(TContext context);
    }
}
