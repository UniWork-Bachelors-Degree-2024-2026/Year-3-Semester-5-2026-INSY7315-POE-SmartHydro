using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Patterns.Strategy
{
    // Strategy interface defining telemetry ingestion and normalization rules per hardware device family
    public interface ITelemetryIngestionStrategy
    {
        // Indicates whether this strategy supports the specified device type identifier
        bool Supports(string deviceType);

        // Validates and transforms raw incoming payload into a standardized TelemetryRealtime domain entity
        TelemetryRealtime ProcessReading(TelemetryIngestDto dto, DateTime recordedAt);
    }
}
