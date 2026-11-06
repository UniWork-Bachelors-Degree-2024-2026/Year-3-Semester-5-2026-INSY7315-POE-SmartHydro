using SmartHydroAPI.Models.Dtos;

namespace SmartHydroAPI.Services
{
    // Service contract managing volumetric peristaltic dosing commands
    public interface IDosingService
    {
        // Enqueues a one-shot volumetric dosing command, calculating duration at 800ms/mL
        Task<DosingCommandResponseDto> EnqueueCommandAsync(DosingCommandRequestDto dto, CancellationToken ct = default);

        // Microcontroller callback reporting completion of a dosing command
        Task<bool> CompleteCommandAsync(DosingCallbackRequestDto dto, CancellationToken ct = default);

        // Hardware poll endpoint retrieving unexecuted commands for a device
        Task<IEnumerable<DosingCommandResponseDto>> GetPendingCommandsAsync(string deviceMac, CancellationToken ct = default);

        // Historical query retrieving recent dosing events for audit logging
        Task<IEnumerable<DosingCommandResponseDto>> GetHistoryAsync(string deviceMac, int limit = 50, CancellationToken ct = default);
    }
}
