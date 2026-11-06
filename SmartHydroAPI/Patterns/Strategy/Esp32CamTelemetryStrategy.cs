using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Patterns.Strategy
{
    // Concrete ingestion strategy tailored for the AI-Thinker ESP32-CAM optical monitoring module
    public class Esp32CamTelemetryStrategy : ITelemetryIngestionStrategy
    {
        // Matches camera node classification strings
        public bool Supports(string deviceType)
        {
            var type = (deviceType ?? "").Replace('-', '_');

            return string.Equals(type, "CAMERA_NODE", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(type, "ESP32_CAM", StringComparison.OrdinalIgnoreCase);
        }

        // Processes lightweight camera health telemetry and ambient telemetry if attached
        public TelemetryRealtime ProcessReading(TelemetryIngestDto dto, DateTime recordedAt)
        {
            return new TelemetryRealtime
            {
                DeviceMac = dto.DeviceMac,
                LightLux = dto.Light >= 0m ? dto.Light : -1m,
                TemperatureC = dto.Temperature >= -40m && dto.Temperature <= 80m ? dto.Temperature : -99m,
                HumidityPercent = dto.Humidity >= 0m && dto.Humidity <= 100m ? dto.Humidity : -1m,
                RecordedAt = recordedAt
            };
        }
    }
}
