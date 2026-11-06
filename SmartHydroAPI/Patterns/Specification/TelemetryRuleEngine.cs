using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Services;

namespace SmartHydroAPI.Patterns.Specification
{
    // Concrete rule engine coordinating specifications and structure thresholds
    public class TelemetryRuleEngine : ITelemetryRuleEngine
    {
        private readonly ApplicationDbContext _database;
        private readonly IEnumerable<ISpecification<TelemetryEvaluationContext>> _specifications;

        // Injects database and all registered specifications
        public TelemetryRuleEngine(
            ApplicationDbContext database,
            IEnumerable<ISpecification<TelemetryEvaluationContext>> specifications)
        {
            _database = database;
            _specifications = specifications;
        }

        // Evaluates rules against the ranges of the structure the device belongs to
        public async Task<IEnumerable<CreateAlertDto>> EvaluateRulesAsync(TelemetryRealtime reading, CancellationToken ct = default)
        {
            var device = await _database.Devices
                .AsNoTracking()
                .Include(d => d.Structure)
                    .ThenInclude(s => s!.Site)
                .FirstOrDefaultAsync(d => d.MacAddress == reading.DeviceMac, ct);

            var structure = device?.Structure;
            // If device is not yet assigned to any greenhouse structure, skip structure-level threshold alerts
            if (structure == null)
            {
                return Enumerable.Empty<CreateAlertDto>();
            }

            var structureType = structure.StructureType ?? "VERTICAL_TOWER";

            // Only this structure's own saved ranges count, so another site's settings can never reach it; any reading nobody changed uses the healthy default
            var saved = structure is null
                ? new List<Threshold>()
                : await _database.Thresholds
                    .AsNoTracking()
                    .Where(t => t.StructureId == structure.Id)
                    .ToListAsync(ct);

            var thresholds = ThresholdDefaults.Resolve(structureType, structure?.Id ?? 0, saved);

            var context = new TelemetryEvaluationContext(reading, structure, thresholds);
            var triggeredAlerts = new List<CreateAlertDto>();

            foreach (var spec in _specifications)
            {
                if (spec.IsSatisfiedBy(context))
                {
                    triggeredAlerts.Add(spec.CreateAlert(context));
                }
            }

            return triggeredAlerts;
        }
    }
}
