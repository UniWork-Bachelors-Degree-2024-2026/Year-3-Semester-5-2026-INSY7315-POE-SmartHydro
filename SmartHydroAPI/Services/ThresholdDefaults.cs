using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Services
{
    // The generally healthy range each structure type starts with until someone changes it. EC is in mS/cm, temperature in degrees Celsius, humidity in percent
    public static class ThresholdDefaults
    {
        public sealed record DefaultRange(string SensorType, decimal Min, decimal Max);

        private static readonly DefaultRange[] Hydroponic =
        {
            new("PH", 5.5m, 6.5m),
            new("EC", 1.0m, 2.5m),
            new("TEMPERATURE", 18m, 28m),
            new("HUMIDITY", 50m, 80m)
        };

        private static readonly DefaultRange[] Fodder =
        {
            new("PH", 5.5m, 6.5m),
            new("EC", 0.5m, 1.5m),
            new("TEMPERATURE", 18m, 26m),
            new("HUMIDITY", 60m, 80m)
        };

        // Lists the readings a structure type has a range for, with their healthy defaults. An unknown type is treated as a hydroponic tower
        public static IReadOnlyList<DefaultRange> For(string? structureType)
        {
            if (string.Equals(structureType?.Trim(), "FODDER_TRAY", StringComparison.OrdinalIgnoreCase))
                return Fodder;

            return Hydroponic;
        }

        // Builds the range for every reading a structure has: the saved one where someone changed it, otherwise the healthy default (Id 0)
        public static List<Threshold> Resolve(string? structureType, int structureId, IEnumerable<Threshold> saved)
        {
            var savedBySensor = new Dictionary<string, Threshold>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in saved)
                savedBySensor.TryAdd(row.SensorType, row);

            var type = string.IsNullOrWhiteSpace(structureType) ? "VERTICAL_TOWER" : structureType.Trim().ToUpperInvariant();
            var resolved = new List<Threshold>();

            foreach (var range in For(structureType))
            {
                if (savedBySensor.TryGetValue(range.SensorType, out var row))
                {
                    resolved.Add(row);
                    continue;
                }

                resolved.Add(new Threshold
                {
                    Id = 0,
                    StructureId = structureId,
                    StructureType = type,
                    SensorType = range.SensorType,
                    MinValue = range.Min,
                    MaxValue = range.Max
                });
            }

            return resolved;
        }
    }
}
