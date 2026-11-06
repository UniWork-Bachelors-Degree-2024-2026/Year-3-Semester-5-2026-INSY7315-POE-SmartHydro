using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Patterns.Repository
{
    // Repository contract managing ActuatorState persistence, thread-safety, and audit trail generation
    public interface IActuatorRepository
    {
        // Retrieves existing state with NoTracking or inserts default baseline
        Task<ActuatorState> GetOrCreateStateAsync(string mac, CancellationToken ct = default);

        // Applies state updates and logs change history
        Task<ActuatorState> UpdateStateAndLogHistoryAsync(ActuatorCommandDto dto, CancellationToken ct = default);

        // Applies a one-shot timed command and logs change history
        Task<ActuatorState> ApplyTimedCommandAsync(TimedActuatorCommandDto dto, CancellationToken ct = default);

        // Queries historical records for a device MAC address
        Task<IEnumerable<ActuatorHistory>> GetHistoryAsync(string mac, int limit = 50, CancellationToken ct = default);

        // Queries paginated historical records for a device MAC address
        Task<PagedResultDto<ActuatorHistory>> GetPagedHistoryAsync(string mac, int page = 1, int pageSize = 20, CancellationToken ct = default);
    }
}
