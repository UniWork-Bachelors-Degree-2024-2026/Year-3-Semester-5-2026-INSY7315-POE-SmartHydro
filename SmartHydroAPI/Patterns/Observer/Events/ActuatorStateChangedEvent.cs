using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Patterns.Observer.Events
{
    // Domain event emitted when relay actuator state is modified
    public record ActuatorStateChangedEvent(string DeviceMac, ActuatorState NewState, string? ChangedBy, DateTime Timestamp) : IDomainEvent;
}
