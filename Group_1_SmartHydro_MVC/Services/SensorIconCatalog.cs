namespace Group_1_SmartHydro_MVC.Services
{
    /// <summary>
    /// Describes how a sensor's icon is displayed: the icon name, whether it is a Material
    /// Symbol (otherwise a Font Awesome class), optional text shown inside the icon, and the
    /// CSS colour class. A record is used because the icon details never change once created.
    /// </summary>
    public sealed record SensorIcon(string Name, bool IsMaterialSymbol, string Text, string ColorClass);

    /// <summary>
    /// A central lookup of the icon used for each sensor type. Provides a key constant for
    /// every sensor and maps each key to its SensorIcon, so alerts and pages show consistent
    /// icons. Unknown keys fall back to a generic notification icon.
    /// </summary>
    public static class SensorIconCatalog
    {
        public const string WaterPump = "water-pump";
        public const string CleanWater = "clean-water";
        public const string Temperature = "temperature";
        public const string Sunlight = "sunlight";
        public const string Light = "light";
        public const string Nutrients = "nutrients";
        public const string Humidity = "humidity";
        public const string SpraySystem = "spray-system";

        private static readonly SensorIcon Fallback = new("notifications", true, "", "blue");

        private static readonly Dictionary<string, SensorIcon> Icons = new()
        {
            /* Google Font (2026) documents the use of Material Symbols icon names. */
            /* Font Awesome (2026) documents the use of Font Awesome icon classes and the Solid icon style. */
            [WaterPump] = new SensorIcon("fa-solid fa-droplet", false, "", "water"),
            [CleanWater] = new SensorIcon("water_ph", true, "", "cleanwater"),
            [Temperature] = new SensorIcon("fa-solid fa-temperature-half", false, "", "temp"),
            [Sunlight] = new SensorIcon("sunny", true, "", "sunlight"),
            [Light] = new SensorIcon("sunny", true, "", "sunlight"),
            [Nutrients] = new SensorIcon("fa-solid fa-jar", false, "N", "nutrients"),
            [Humidity] = new SensorIcon("humidity_percentage", true, "", "humidity"),
            [SpraySystem] = new SensorIcon("sprinkler", true, "", "water")
        };

        /// <summary>
        /// Returns the icon for the given sensor key. If the key is not recognised, a generic
        /// blue notification icon is returned instead, so a missing icon never breaks the page.
        /// </summary>
        /// <param name="key">The sensor key, such as SensorIconCatalog.Temperature.</param>
        /// <returns>The matching SensorIcon, or the fallback icon.</returns>
        public static SensorIcon Get(string key)
        {
            if (Icons.TryGetValue(key, out var icon))
            {
                return icon;
            }

            return Fallback;
        }
    }
}

//Reference List:

//Font Awesome, 2026. Font Awesome Icons. [online] Available at: <https://fontawesome.com/icons> [Accessed 22 September 2026].

//Google Fonts, 2026. Material Symbols guide. [online] Available at: <https://fonts.google.com/icons> [Accessed 22 September 2026].
