using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Patterns.Decorator
{
    // Interface defining contract for actuator operations (target of Decorator Pattern)
    public interface IActuatorService
    {
        // Retrieves current relay states for a device MAC address
        Task<ActuatorStateResponseDto> GetStateAsync(string mac, CancellationToken ct = default);

        Task<ActuatorStateResponseDto> RefreshStateAsync(string mac, CancellationToken ct = default)
            => GetStateAsync(mac, ct);

        // Applies incoming relay toggle commands, audits state transitions, and emits domain event
        Task<ActuatorStateResponseDto> ApplyCommandAsync(ActuatorCommandDto dto, CancellationToken ct = default);

        // Applies a one-shot timed actuator command (e.g. Fodder misting spray for 8 seconds)
        Task<ActuatorStateResponseDto> ApplyTimedCommandAsync(TimedActuatorCommandDto dto, CancellationToken ct = default);

        // Retrieves recent actuator switching history log
        Task<IEnumerable<ActuatorHistory>> GetHistoryAsync(string mac, int limit = 50, CancellationToken ct = default);

        // Retrieves paginated actuator history log
        Task<PagedResultDto<ActuatorHistory>> GetPagedHistoryAsync(string mac, int page = 1, int pageSize = 20, CancellationToken ct = default);
    }
}
