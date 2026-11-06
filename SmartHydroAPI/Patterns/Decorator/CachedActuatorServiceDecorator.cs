using Microsoft.Extensions.Caching.Memory;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Patterns.Decorator
{
    // Decorator caching actuator state lookups in memory to minimize database roundtrips
    public class CachedActuatorServiceDecorator : IActuatorService
    {
        private readonly IActuatorService _inner;
        private readonly IMemoryCache _cache;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(5);

        // Injects wrapped service and in-memory cache
        public CachedActuatorServiceDecorator(IActuatorService inner, IMemoryCache cache)
        {
            _inner = inner;
            _cache = cache;
        }

        // Live relay reports can arrive through either command ACKs or telemetry.
        public async Task<ActuatorStateResponseDto> GetStateAsync(string mac, CancellationToken ct = default)
        {
            // Telemetry also writes relay states. Read through so those reports are visible immediately.
            return await RefreshStateAsync(mac, ct);
        }

        public async Task<ActuatorStateResponseDto> RefreshStateAsync(string mac, CancellationToken ct = default)
        {
            var cacheKey = $"actuator_state_{mac.ToUpperInvariant()}";
            var fresh = await _inner.GetStateAsync(mac, ct);
            _cache.Set(cacheKey, fresh, CacheDuration);
            return fresh;
        }

        // Applies state change, evicts cached state, and caches updated snapshot
        public async Task<ActuatorStateResponseDto> ApplyCommandAsync(ActuatorCommandDto dto, CancellationToken ct = default)
        {
            var result = await _inner.ApplyCommandAsync(dto, ct);
            var cacheKey = $"actuator_state_{dto.DeviceMac.ToUpperInvariant()}";
            _cache.Set(cacheKey, result, CacheDuration);
            return result;
        }

        // Applies timed command, evicts cached state
        public async Task<ActuatorStateResponseDto> ApplyTimedCommandAsync(TimedActuatorCommandDto dto, CancellationToken ct = default)
        {
            var result = await _inner.ApplyTimedCommandAsync(dto, ct);
            var cacheKey = $"actuator_state_{dto.DeviceMac.ToUpperInvariant()}";
            _cache.Set(cacheKey, result, CacheDuration);
            return result;
        }

        // Delegates historical log retrieval to underlying service without caching
        public Task<IEnumerable<ActuatorHistory>> GetHistoryAsync(string mac, int limit = 50, CancellationToken ct = default) =>
            _inner.GetHistoryAsync(mac, limit, ct);

        // Delegates paginated historical log retrieval to underlying service
        public Task<PagedResultDto<ActuatorHistory>> GetPagedHistoryAsync(string mac, int page = 1, int pageSize = 20, CancellationToken ct = default) =>
            _inner.GetPagedHistoryAsync(mac, page, pageSize, ct);
    }
}
