using Microsoft.Extensions.Caching.Memory;
using Moq;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Patterns.Decorator;

namespace SmartHydroAPI.Tests.Patterns
{
    public class CachedTelemetryServiceDecoratorTests
    {
        private readonly Mock<ITelemetryService> _inner = new();
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        private readonly CachedTelemetryServiceDecorator _decorator;

        // Wraps the mocked inner service in the caching decorator for each test
        public CachedTelemetryServiceDecoratorTests()
        {
            _decorator = new CachedTelemetryServiceDecorator(_inner.Object, _cache);
        }

        // The second request for the same device should come from the cache, not the inner service
        [Fact]
        public async Task GetLatestByMacAsync_SecondCall_UsesCache()
        {
            var reading = new TelemetryRealtime { DeviceMac = "AA:BB:CC:DD:EE:FF", PhLevel = 6.1m };
            _inner.Setup(s => s.GetLatestByMacAsync("aa:bb:cc:dd:ee:ff", It.IsAny<CancellationToken>()))
                .ReturnsAsync(reading);

            var first = await _decorator.GetLatestByMacAsync("aa:bb:cc:dd:ee:ff");
            var second = await _decorator.GetLatestByMacAsync("AA:BB:CC:DD:EE:FF");

            Assert.Same(reading, first);
            Assert.Same(reading, second);
            _inner.Verify(s => s.GetLatestByMacAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        // A missing reading should not be cached, so the next call asks the inner service again
        [Fact]
        public async Task GetLatestByMacAsync_NoReading_IsNotCached()
        {
            _inner.Setup(s => s.GetLatestByMacAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((TelemetryRealtime?)null);

            await _decorator.GetLatestByMacAsync("AA:BB:CC:DD:EE:FF");
            await _decorator.GetLatestByMacAsync("AA:BB:CC:DD:EE:FF");

            _inner.Verify(s => s.GetLatestByMacAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        // Ingesting new telemetry should clear the cached reading so the next read is fresh
        [Fact]
        public async Task IngestAndSyncAsync_ClearsCachedReading()
        {
            var oldReading = new TelemetryRealtime { DeviceMac = "AA:BB:CC:DD:EE:FF", PhLevel = 6.0m };
            var newReading = new TelemetryRealtime { DeviceMac = "AA:BB:CC:DD:EE:FF", PhLevel = 7.0m };
            _inner.SetupSequence(s => s.GetLatestByMacAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(oldReading)
                .ReturnsAsync(newReading);
            _inner.Setup(s => s.IngestAndSyncAsync(It.IsAny<TelemetryIngestDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ActuatorStateResponseDto("AA:BB:CC:DD:EE:FF", false, false, false, false, false, false, false, false, false, DateTime.UtcNow));

            await _decorator.GetLatestByMacAsync("AA:BB:CC:DD:EE:FF");
            await _decorator.IngestAndSyncAsync(new TelemetryIngestDto { DeviceMac = "aa:bb:cc:dd:ee:ff" });
            var afterIngest = await _decorator.GetLatestByMacAsync("AA:BB:CC:DD:EE:FF");

            Assert.Same(newReading, afterIngest);
        }

        // History is not cached, so every call should go straight to the inner service
        [Fact]
        public async Task GetHistoryAsync_AlwaysDelegates()
        {
            _inner.Setup(s => s.GetHistoryAsync("AA:BB:CC:DD:EE:FF", 7, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<TelemetryDailyHistory>());

            await _decorator.GetHistoryAsync("AA:BB:CC:DD:EE:FF", 7);
            await _decorator.GetHistoryAsync("AA:BB:CC:DD:EE:FF", 7);

            _inner.Verify(s => s.GetHistoryAsync("AA:BB:CC:DD:EE:FF", 7, It.IsAny<CancellationToken>()), Times.Exactly(2));
        }
    }
}