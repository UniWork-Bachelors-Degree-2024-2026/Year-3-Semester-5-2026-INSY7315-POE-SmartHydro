using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Patterns.Decorator;
using SmartHydroAPI.Patterns.Observer;
using SmartHydroAPI.Patterns.Observer.Events;
using SmartHydroAPI.Patterns.Repository;

namespace SmartHydroAPI.Services
{
    // Service managing 9-channel relay states consuming Repository and Observer patterns
    public class ActuatorService : IActuatorService
    {
        private readonly IActuatorRepository _actuatorRepo;
        private readonly IEventPublisher _eventPublisher;

        // Injects repository and event publisher dependencies
        public ActuatorService(IActuatorRepository actuatorRepo, IEventPublisher eventPublisher)
        {
            _actuatorRepo = actuatorRepo;
            _eventPublisher = eventPublisher;
        }

        // Retrieves current relay states for a device MAC address
        public async Task<ActuatorStateResponseDto> GetStateAsync(string mac, CancellationToken ct = default)
        {
            var state = await _actuatorRepo.GetOrCreateStateAsync(mac, ct);
            return ToDto(state);
        }

        // Applies incoming relay toggle commands, audits state transitions, and emits domain event
        public async Task<ActuatorStateResponseDto> ApplyCommandAsync(ActuatorCommandDto dto, CancellationToken ct = default)
        {
            var state = await _actuatorRepo.UpdateStateAndLogHistoryAsync(dto, ct);

            // Publish event to observers
            await _eventPublisher.PublishAsync(new ActuatorStateChangedEvent(dto.DeviceMac, state, dto.ChangedBy, DateTime.UtcNow), ct);

            return ToDto(state, dto.ChangedBy);
        }

        // Applies a one-shot timed actuator command (e.g. Fodder misting spray for 8 seconds)
        public async Task<ActuatorStateResponseDto> ApplyTimedCommandAsync(TimedActuatorCommandDto dto, CancellationToken ct = default)
        {
            var state = await _actuatorRepo.ApplyTimedCommandAsync(dto, ct);

            // Publish event to observers
            await _eventPublisher.PublishAsync(new ActuatorStateChangedEvent(dto.DeviceMac, state, dto.ChangedBy, DateTime.UtcNow), ct);

            return ToDto(state, dto.ChangedBy);
        }

        // Retrieves recent actuator switching history log
        public Task<IEnumerable<ActuatorHistory>> GetHistoryAsync(string mac, int limit = 50, CancellationToken ct = default) =>
            _actuatorRepo.GetHistoryAsync(mac, limit, ct);

        // Retrieves paginated actuator history log
        public Task<PagedResultDto<ActuatorHistory>> GetPagedHistoryAsync(string mac, int page = 1, int pageSize = 20, CancellationToken ct = default) =>
            _actuatorRepo.GetPagedHistoryAsync(mac, page, pageSize, ct);

        // Maps ActuatorState entity to ActuatorStateResponseDto
        private static ActuatorStateResponseDto ToDto(ActuatorState state, string? changedBy = null)
        {
            return new ActuatorStateResponseDto(
                state.DeviceMac,
                state.GrowLight,
                state.CirculationFan,
                state.MainWaterPump,
                state.ExtractorFan,
                state.PhUpPump,
                state.PhDownPump,
                state.NutrientUpPump,
                state.DilutionDownPump,
                state.SpraySystem,
                state.UpdatedAt,
                ChangedBy: changedBy,
                ReportedSequence: state.ReportedSequence
            );
        }
    }
}
