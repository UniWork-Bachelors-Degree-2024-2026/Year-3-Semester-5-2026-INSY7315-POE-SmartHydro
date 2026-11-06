using Microsoft.Extensions.Caching.Memory;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Patterns.Decorator
{
    // Decorator pattern implementation caching telemetry queries in memory to protect database from rapid polling
    public class CachedTelemetryServiceDecorator : ITelemetryService
    {
        private readonly ITelemetryService _innerService;
        private readonly IMemoryCache _cache;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(3); // 3-second cache window for high-frequency polling

        // Injects inner service and in-memory cache
        public CachedTelemetryServiceDecorator(ITelemetryService innerService, IMemoryCache cache)
        {
            _innerService = innerService;
            _cache = cache;
        }

        // Passes through ingestion and invalidates cached telemetry for the device
        public async Task<ActuatorStateResponseDto> IngestAndSyncAsync(TelemetryIngestDto dto, CancellationToken ct = default)
        {
            var result = await _innerService.IngestAndSyncAsync(dto, ct);

            // Invalidate cache on new ingest
            _cache.Remove($"Telemetry_Latest_{dto.DeviceMac.ToUpper()}");

            return result;
        }

        // Returns cached latest telemetry reading if available, otherwise queries database
        public async Task<TelemetryRealtime?> GetLatestByMacAsync(string mac, CancellationToken ct = default)
        {
            var cacheKey = $"Telemetry_Latest_{mac.ToUpper()}";

            if (_cache.TryGetValue<TelemetryRealtime>(cacheKey, out var cachedReading))
            {
                return cachedReading;
            }

            var reading = await _innerService.GetLatestByMacAsync(mac, ct);
            if (reading is not null)
            {
                _cache.Set(cacheKey, reading, CacheDuration);
            }

            return reading;
        }

        // Returns cached structure latest telemetry readings
        public async Task<IEnumerable<TelemetryRealtime>> GetLatestByStructureAsync(int structureId, CancellationToken ct = default)
        {
            var cacheKey = $"Telemetry_Structure_{structureId}";

            if (_cache.TryGetValue<IEnumerable<TelemetryRealtime>>(cacheKey, out var cachedReadings) && cachedReadings is not null)
            {
                return cachedReadings;
            }

            var readings = await _innerService.GetLatestByStructureAsync(structureId, ct);
            _cache.Set(cacheKey, readings, CacheDuration);

            return readings;
        }

        // Delegates historical query to inner service
        public Task<IEnumerable<TelemetryDailyHistory>> GetHistoryAsync(string mac, int days = 7, CancellationToken ct = default) =>
            _innerService.GetHistoryAsync(mac, days, ct);
    }
}
