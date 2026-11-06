namespace Group_1_SmartHydro_MVC.Services
{
    // Names the relays a page can switch and how each one appears in the API and in the actuator history
    public static class ActuatorControls
    {
        public const string Ph = "ph";
        public const string Ec = "ec";

        private static readonly Dictionary<string, string[]> Channels = new(StringComparer.OrdinalIgnoreCase)
        {
            ["growLight"] = new[] { "growLight" },
            ["circulationFan"] = new[] { "circulationFan" },
            ["mainWaterPump"] = new[] { "mainWaterPump" },
            ["extractorFan"] = new[] { "extractorFan" },
            ["nutrientUpPump"] = new[] { "nutrientUpPump" },
            ["dilutionDownPump"] = new[] { "dilutionDownPump" },
            ["spraySystem"] = new[] { "spraySystem" },
            [Ph] = new[] { "phUpPump", "phDownPump" },
            [Ec] = new[] { "nutrientUpPump", "dilutionDownPump" }
        };

        private static readonly Dictionary<string, string[]> HistoryNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["growLight"] = new[] { "grow_light" },
            ["circulationFan"] = new[] { "circulation_fan" },
            ["mainWaterPump"] = new[] { "main_water_pump", "MainWaterPump_TIMED_" },
            ["extractorFan"] = new[] { "extractor_fan" },
            ["nutrientUpPump"] = new[] { "nutrient_up_pump" },
            ["dilutionDownPump"] = new[] { "dilution_down_pump" },
            ["spraySystem"] = new[] { "spray_system", "SpraySystem_TIMED_" },
            [Ph] = new[] { "ph_up_pump", "ph_down_pump" },
            [Ec] = new[] { "nutrient_up_pump", "dilution_down_pump" }
        };

        // Checks that a control name sent from a page is one of the relays this site can switch
        public static bool IsKnown(string? key)
        {
            return !string.IsNullOrWhiteSpace(key) && Channels.ContainsKey(key);
        }

        // Lists the API relay names a control switches (the pH and EC controls cover both dosing pumps)
        public static string[] ChannelsFor(string key)
        {
            return Channels[key];
        }

        // The pH and EC pumps run in opposite directions, so a dosing control can be switched off but not on without saying which way to dose
        public static bool NeedsDirectionToTurnOn(string key)
        {
            return string.Equals(key, Ph, StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, Ec, StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "nutrientUpPump", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "dilutionDownPump", StringComparison.OrdinalIgnoreCase);
        }

        // Reports whether any relay behind a control is currently on
        public static bool IsOn(IReadOnlyDictionary<string, bool> actuators, string key)
        {
            foreach (var channel in Channels[key])
            {
                if (actuators.TryGetValue(channel, out var on) && on)
                    return true;
            }

            return false;
        }

        // Checks whether an actuator history entry belongs to a control
        public static bool MatchesHistory(string key, string historyName)
        {
            if (!HistoryNames.TryGetValue(key, out var names))
                return false;

            return names.Any(n => historyName.StartsWith(n, StringComparison.OrdinalIgnoreCase));
        }
    }
}
