namespace Group_1_SmartHydro_MVC.Services
{
    // One reading a threshold can be set for: the name the API stores it under, the label and unit shown on the page, the factor from the API's unit to the shown unit, and the lowest and highest value that makes sense
    public sealed record ThresholdSensor(string Key, string Label, string Unit, decimal Scale, decimal Lowest, decimal Highest);

    // One structure type on the thresholds page: how the API names it, how the page names it and which readings it has thresholds for
    public sealed record ThresholdGroup(string Key, string ApiType, string Title, string Subtitle, IReadOnlyList<ThresholdSensor> Sensors);

    // Lists the structure types and readings the thresholds page can set. Only readings the site actually shows for that structure type are listed
    public static class ThresholdCatalog
    {
        // The API stores EC in mS/cm, but the site shows it in microsiemens (uS/cm), so the page converts with a factor of 1000
        private static readonly ThresholdSensor Ph = new("PH", "pH", "pH", 1m, 0m, 14m);
        private static readonly ThresholdSensor Ec = new("EC", "Nutrient (EC)", "\u00B5S/cm", 1000m, 0m, 20000m);
        private static readonly ThresholdSensor Temperature = new("TEMPERATURE", "Temperature", "\u00B0C", 1m, -10m, 60m);
        private static readonly ThresholdSensor Humidity = new("HUMIDITY", "Humidity", "%", 1m, 0m, 100m);

        private static readonly ThresholdGroup[] AllGroups =
        {
            new("hydroponic", "VERTICAL_TOWER", "Hydroponic structure", "Vertical towers grown in a nutrient water solution.", new[] { Ph, Ec, Temperature, Humidity }),
            new("fodder", "FODDER_TRAY", "Fodder structure", "Fodder trays grown from sprouted feed.", new[] { Ph, Ec, Temperature, Humidity })
        };

        public static IReadOnlyList<ThresholdGroup> Groups
        {
            get { return AllGroups; }
        }

        // Finds a reading by the name the API stores it under (PH, EC, TEMPERATURE, HUMIDITY), or null when it has no threshold
        public static ThresholdSensor? ForKey(string key)
        {
            return AllGroups
                .SelectMany(g => g.Sensors)
                .FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        // Finds a structure type by the name the API uses (VERTICAL_TOWER, FODDER_TRAY), or null when it is not one the site sets thresholds for
        public static ThresholdGroup? ForApiType(string? apiType)
        {
            if (string.IsNullOrWhiteSpace(apiType))
                return null;

            return AllGroups.FirstOrDefault(g => string.Equals(g.ApiType, apiType.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }
}
