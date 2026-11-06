using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Patterns.Specification
{
    // Rule engine interface coordinating composite specification evaluation over telemetry readings
    public interface ITelemetryRuleEngine
    {
        // Evaluates all registered specifications against incoming reading and yields detected alert DTOs
        Task<IEnumerable<CreateAlertDto>> EvaluateRulesAsync(TelemetryRealtime reading, CancellationToken ct = default);
    }
}
