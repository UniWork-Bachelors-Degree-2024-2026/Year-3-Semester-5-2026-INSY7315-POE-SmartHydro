using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Patterns.Strategy
{
    // Concrete ingestion strategy tailored for the DFRobot FireBeetle 2 ESP32-C6 multi-sensor node
    public class FireBeetleC6TelemetryStrategy : ITelemetryIngestionStrategy
    {
        // Matches sensor node device classification strings
        public bool Supports(string deviceType) =>
            string.Equals(deviceType, "SENSOR_NODE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(deviceType, "FIREBEETLE_C6", StringComparison.OrdinalIgnoreCase);

        // Processes and cleans environmental sensor readings including pH, EC (mS/cm), light, water level, temperature, and humidity
        public TelemetryRealtime ProcessReading(TelemetryIngestDto dto, DateTime recordedAt)
        {
            return new TelemetryRealtime
            {
                DeviceMac = dto.DeviceMac,
                PhLevel = dto.Ph >= 0m && dto.Ph <= 14m ? dto.Ph : -1m,
                EcMs = dto.Ec >= 0m && dto.Ec <= 15m ? dto.Ec.Value : -1m,
                LightLux = dto.Light >= 0m ? dto.Light : -1m,
                WaterLevelRaw = dto.WaterLevelRaw,
                WaterLevelDetected = dto.WaterLevelDetected,
                TemperatureC = dto.Temperature >= -40m && dto.Temperature <= 80m ? dto.Temperature : -99m,
                HumidityPercent = dto.Humidity >= 0m && dto.Humidity <= 100m ? dto.Humidity : -1m,
                MlPhPrediction = dto.MlPh,
                MlEcPrediction = dto.MlEc,
                MlTempPrediction = dto.MlTemp,
                MlHumidityPrediction = dto.MlHumidity,
                RecordedAt = recordedAt
            };
        }
    }
}
