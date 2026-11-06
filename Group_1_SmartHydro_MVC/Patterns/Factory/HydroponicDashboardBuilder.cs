using Group_1_SmartHydro_MVC.Models;
using Group_1_SmartHydro_MVC.Services;

namespace Group_1_SmartHydro_MVC.Patterns.Factory
{
    public sealed class HydroponicDashboardBuilder : IDashboardBuilder
    {
        public StructureType Type
        {
            get { return StructureType.Hydroponic; }
        }

        // Builds the hydroponic dashboard: water, clean water, temperature, sunlight, nutrients and humidity cards with their linked controls, filled from the latest readings
        public DashboardViewModel Build(StructureReadings readings)
        {
            var model = new DashboardViewModel
            {
                StatusTitle = "Hydroponic Status",
                Cards = new List<DashboardCardViewModel>
                {
                    new()
                    {
                        Label = "Water",
                        IconName = "fa-solid fa-droplet",
                        IconColorClass = "water",
                        ControlLabel = "Water Pump",
                        ControlTag = "PUMP",
                        LinkController = "Sensors",
                        LinkAction = "WaterPump"
                    },
                    new()
                    {
                        Label = "Clean Water",
                        IconName = "water_ph",
                        IconIsMaterialSymbol = true,
                        IconColorClass = "cleanwater",
                        ControlLabel = "pH Pump",
                        ControlTag = "PUMP",
                        LinkController = "Sensors",
                        LinkAction = "CleanWater"
                    },
                    new()
                    {
                        Label = "Temperature",
                        IconName = "fa-solid fa-temperature-half",
                        IconColorClass = "temp",
                        ControlLabel = "Circulation Fans",
                        ControlTag = "FANS",
                        LinkController = "Sensors",
                        LinkAction = "Temperature"
                    },
                    new()
                    {
                        Label = "Sunlight",
                        IconName = "sunny",
                        IconIsMaterialSymbol = true,
                        IconColorClass = "sunlight",
                        ControlLabel = "Lights",
                        ControlTag = "LIGHT",
                        LinkController = "Sensors",
                        LinkAction = "Sunlight"
                    },
                    new()
                    {
                        Label = "Nutrients (EC)",
                        IconName = "fa-solid fa-jar",
                        IconText = "N",
                        IconColorClass = "nutrients",
                        ControlLabel = "EC Pump",
                        ControlTag = "PUMP",
                        LinkController = "Sensors",
                        LinkAction = "Nutrients"
                    },
                    new()
                    {
                        Label = "Humidity",
                        IconName = "humidity_percentage",
                        IconIsMaterialSymbol = true,
                        IconColorClass = "humidity",
                        ControlLabel = "Extractor Fans",
                        ControlTag = "FANS",
                        LinkController = "Sensors",
                        LinkAction = "Humidity"
                    }
                },
                Alerts = new List<DashboardAlertViewModel>()
            };

            SensorBindings.FillCards(model.Cards, readings);
            return model;
        }
    }
}