namespace SmartHydroAPI.Patterns.Strategy
{
    // Factory interface resolving the appropriate telemetry ingestion strategy at runtime based on device hardware family
    public interface ITelemetryStrategyFactory
    {
        // Resolves the matching ITelemetryIngestionStrategy for a device type or falls back to standard sensor node strategy
        ITelemetryIngestionStrategy GetStrategy(string? deviceType);
    }
}
