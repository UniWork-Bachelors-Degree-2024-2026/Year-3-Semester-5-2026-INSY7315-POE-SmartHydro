using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Patterns.Observer.Events
{
    // Domain event emitted immediately after a telemetry reading is successfully ingested and saved
    public record TelemetryIngestedEvent(TelemetryRealtime Reading, DateTime IngestedAt) : IDomainEvent;
}
