using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Patterns.Factory
{
    // Factory interface defining methods for creating standardized Alert domain entities
    public interface IAlertFactory
    {
        // Creates a new un-resolved Alert entity initialized with UTC timestamp
        Alert CreateAlert(CreateAlertDto dto);
    }
}
