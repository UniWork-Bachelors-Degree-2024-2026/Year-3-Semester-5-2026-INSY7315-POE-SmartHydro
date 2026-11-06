namespace Group_1_SmartHydro_MVC.Services
{
    // One dose a sensor page can send: the type the API expects, the name shown to the user, the fixed volume and the group of doses shown together
    public sealed record DoseOption(string Type, string Label, decimal VolumeMl, string Group);

    // Lists the one-shot doses the site can send and which sensor page each belongs to. The volume is fixed here so a page cannot ask for any other amount
    public static class DosingCatalog
    {
        public const string PhGroup = "ph";
        public const string NutrientGroup = "nutrient";

        private static readonly DoseOption[] Options =
        {
            new("PH_DOWN", "Lower pH", 2m, PhGroup),
            new("PH_UP", "Raise pH", 2m, PhGroup),
            new("NUTRIENT_UP", "Add nutrients", 20m, NutrientGroup),
            new("DILUTION_DOWN", "Add clean water", 25m, NutrientGroup)
        };

        // Finds a dose by the type sent from a page, or null when it is not one this site can send
        public static DoseOption? Find(string? type)
        {
            if (string.IsNullOrWhiteSpace(type))
                return null;

            return Options.FirstOrDefault(o => string.Equals(o.Type, type.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        // Says which group of doses a sensor page shows, or null for a sensor that has no dosing
        public static string? GroupFor(SensorMetric metric)
        {
            return metric switch
            {
                SensorMetric.Ph => PhGroup,
                SensorMetric.Ec => NutrientGroup,
                _ => null
            };
        }

        // Checks whether a dose type from the API's history belongs to a group
        public static bool InGroup(string type, string group)
        {
            var option = Find(type);

            return option != null && option.Group == group;
        }

        // Words a dose type from the API's history, falling back to the type itself for one this site does not send
        public static string LabelFor(string type)
        {
            return Find(type)?.Label ?? type;
        }
    }
}
