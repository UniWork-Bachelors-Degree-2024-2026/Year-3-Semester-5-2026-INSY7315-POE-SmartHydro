using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Patterns.Factory
{
    // Concrete factory implementation creating normalized Alert domain entities
    public class AlertFactory : IAlertFactory
    {
        // Instantiates Alert entity with validated timestamp and default unresolved state
        public Alert CreateAlert(CreateAlertDto dto)
        {
            return new Alert
            {
                StructureId = dto.StructureId,
                DeviceMac = dto.DeviceMac,
                Component = dto.Component?.ToUpper() ?? "SYSTEM",
                Severity = dto.Severity?.ToUpper() ?? "WARNING",
                Value = dto.Value,
                Detail = dto.Detail ?? "Automated threshold anomaly detected.",
                OccurredAt = DateTime.UtcNow,
                IsResolved = false
            };
        }
    }
}
