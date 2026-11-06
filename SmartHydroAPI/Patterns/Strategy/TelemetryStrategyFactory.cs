namespace SmartHydroAPI.Patterns.Strategy
{
    // Concrete factory implementation that resolves registered telemetry ingestion strategies
    public class TelemetryStrategyFactory : ITelemetryStrategyFactory
    {
        private readonly IEnumerable<ITelemetryIngestionStrategy> _strategies;
        private readonly ITelemetryIngestionStrategy _defaultStrategy;

        // Injects all registered strategies and establishes default sensor node fallback
        public TelemetryStrategyFactory(IEnumerable<ITelemetryIngestionStrategy> strategies)
        {
            _strategies = strategies;
            _defaultStrategy = strategies.FirstOrDefault(s => s is FireBeetleC6TelemetryStrategy)
                ?? strategies.First();
        }

        // Resolves strategy matching the device type string or returns the default FireBeetle C6 strategy
        public ITelemetryIngestionStrategy GetStrategy(string? deviceType)
        {
            if (string.IsNullOrWhiteSpace(deviceType)) return _defaultStrategy;

            return _strategies.FirstOrDefault(s => s.Supports(deviceType)) ?? _defaultStrategy;
        }
    }
}
