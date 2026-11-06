using Group_1_SmartHydro_MVC.Models;
using Group_1_SmartHydro_MVC.Services;
using Microsoft.AspNetCore.Mvc;

namespace Group_1_SmartHydro_MVC.Controllers
{
    public class SensorsController : Controller
    {
        private readonly StructureApiClient _structures;
        private readonly TelemetryApiClient _telemetry;
        private readonly ThresholdApiClient _thresholds;
        private readonly ISiteContext _siteContext;

        // Creates the controller and stores the injected structure, telemetry and threshold services, which are used to find the structure a sensor page belongs to, load its readings and show its optimal range.
        public SensorsController(StructureApiClient structures, TelemetryApiClient telemetry, ThresholdApiClient thresholds, ISiteContext siteContext)
        {
            _structures = structures;
            _telemetry = telemetry;
            _thresholds = thresholds;
            _siteContext = siteContext;
        }

        // Displays the Water Pump sensor page for hydroponic structures. Uses a status-style hero that shows when the plants were last watered, plus a manual pump override control.
        public Task<IActionResult> WaterPump(string? id)
        {
            var model = new SensorDetailViewModel
            {
                SensorName = "Water Pump",
                IconName = "fa-solid fa-droplet",
                IconColorClass = "water",
                HeroKind = SensorHeroKind.Status,
                HeroTitle = "Water",
                HeroLead = "Last Watered",
                ControlLabel = "Water Pump"
            };

            return ShowSensorAsync(id, model, SensorBindings.For(nameof(WaterPump)));
        }

        // Displays the Clean Water (pH) sensor page. Shows the pH reading with buttons to lower or raise the pH, and a control for the pH dosing pump.
        public Task<IActionResult> CleanWater(string? id)
        {
            var model = new SensorDetailViewModel
            {
                SensorName = "Clean Water (pH)",
                IconName = "water_ph",
                IconIsMaterialSymbol = true,
                IconColorClass = "cleanwater",
                HeroUnit = "pH",
                ShowOptimalRange = true,
                HeroSubtitle = "How acidic or alkaline the water is",
                HeroActions = new List<SensorHeroActionViewModel>
                {
                    new() { Icon = "remove", Label = "Lower pH", DoseType = "PH_DOWN" },
                    new() { Icon = "add", Label = "Raise pH", DoseType = "PH_UP" }
                },
                ControlLabel = "pH Pump"
            };

            return ShowSensorAsync(id, model, SensorBindings.For(nameof(CleanWater)));
        }

        // Displays the Temperature sensor page. Shows the reading in degrees Celsius with its optimal range, and a control for the circulation fans.
        public Task<IActionResult> Temperature(string? id)
        {
            var model = new SensorDetailViewModel
            {
                SensorName = "Temperature",
                IconName = "fa-solid fa-temperature-half",
                IconColorClass = "temp",
                HeroUnit = "\u00B0C",
                ShowOptimalRange = true,
                ControlLabel = "Circulation Fans"
            };

            return ShowSensorAsync(id, model, SensorBindings.For(nameof(Temperature)));
        }

        // Displays the Sunlight sensor page for hydroponic structures. Shows the light level as a percentage with its optimal range, and a control for the lights.
        public Task<IActionResult> Sunlight(string? id)
        {
            var model = new SensorDetailViewModel
            {
                SensorName = "Sunlight",
                IconName = "sunny",
                IconIsMaterialSymbol = true,
                IconColorClass = "sunlight",
                HeroUnit = "%",
                ShowOptimalRange = true,
                ControlLabel = "Lights"
            };

            return ShowSensorAsync(id, model, SensorBindings.For(nameof(Sunlight)));
        }

        // Displays the Nutrient (EC) sensor page. Shows the electrical conductivity of the nutrient solution in µS/cm, a button to dose nutrients, and a control for the EC dosing pump.
        public Task<IActionResult> Nutrients(string? id)
        {
            var model = new SensorDetailViewModel
            {
                SensorName = "Nutrient (EC)",
                IconName = "fa-solid fa-jar",
                IconText = "N",
                IconColorClass = "nutrients",
                HeroUnit = "\u00B5S/cm",
                ShowOptimalRange = true,
                HeroSubtitle = "Plant food in the water",
                HeroActions = new List<SensorHeroActionViewModel>
                {
                    new() { Icon = "remove", Label = "Add clean water", DoseType = "DILUTION_DOWN" },
                    new() { Icon = "add", Label = "Add nutrients", Filled = true, DoseType = "NUTRIENT_UP" }
                },
                ControlLabel = "Nutrient Pump"
            };

            return ShowSensorAsync(id, model, SensorBindings.For(nameof(Nutrients)));
        }

        // Displays the Spray System page for fodder structures. Uses a status-style hero, hides the trend chart, and shows stat tiles for the last watering, next spray cycle, spray duration and reservoir level, plus a control for the irrigation system.
        public Task<IActionResult> SpraySystem(string? id)
        {
            var model = new SensorDetailViewModel
            {
                SensorName = "Spray System",
                IconName = "sprinkler",
                IconIsMaterialSymbol = true,
                IconColorClass = "water",
                HeroKind = SensorHeroKind.Status,
                HeroTitle = "Sprayer",
                HeroLead = "Last Watered",
                ShowTrend = false,
                StatTiles = new List<SensorStatTileViewModel>
                {
                    new() { Label = "Last Watered" },
                    new() { Label = "Next Spray Cycle" },
                    new() { Label = "Spray Duration" },
                    new() { Label = "Reservoir Level" }
                },
                ControlLabel = "Spray Pump"
            };

            return ShowSensorAsync(id, model, SensorBindings.For(nameof(SpraySystem)));
        }

        // Displays the Light sensor page for fodder structures. Shows the light level as a percentage with its optimal range, and a control for the lights.
        public Task<IActionResult> Light(string? id)
        {
            var model = new SensorDetailViewModel
            {
                SensorName = "Light",
                IconName = "sunny",
                IconIsMaterialSymbol = true,
                IconColorClass = "sunlight",
                HeroUnit = "%",
                ShowOptimalRange = true,
                ControlLabel = "Lights"
            };

            return ShowSensorAsync(id, model, SensorBindings.For(nameof(Light)));
        }

        // Displays the Humidity sensor page. Shows the humidity as a percentage with its optimal range, and a control for the extractor fans.
        public Task<IActionResult> Humidity(string? id)
        {
            var model = new SensorDetailViewModel
            {
                SensorName = "Humidity",
                IconName = "humidity_percentage",
                IconIsMaterialSymbol = true,
                IconColorClass = "humidity",
                HeroUnit = "%",
                ShowOptimalRange = true,
                ControlLabel = "Extractor Fans"
            };

            return ShowSensorAsync(id, model, SensorBindings.For(nameof(Humidity)));
        }

        // Shows a sensor page for a structure in the current site. If no structure ID is given, the last structure viewed in this site is used. If the structure cannot be found (for example it belongs to another site), the user is sent to the structure list instead. Otherwise the structure's readings are loaded from the API and copied onto the page.
        private async Task<IActionResult> ShowSensorAsync(string? id, SensorDetailViewModel model, SensorBinding binding)
        {
            var structureId = id;

            if (string.IsNullOrWhiteSpace(structureId))
            {
                var site = await _siteContext.GetCurrentAsync();

                if (site != null)
                {
                    structureId = _siteContext.LastViewedStructureId(site.Id);
                }
            }

            var accessToken = User.GetAccessToken();
            var structure = await _structures.GetStructureAsync(structureId, accessToken, HttpContext.RequestAborted);

            if (structure == null)
            {
                TempData["SelectStructure"] = "Please select a structure to view its sensors.";
                return RedirectToAction("Index", "Structure");
            }

            model.StructureId = structure.Id;
            model.StructureName = structure.Name;
            model.MacAddress = structure.MacAddress ?? "";
            model.ManualPhOverrideAllowed = structure.Type == StructureType.Hydroponic;

            var readings = await _telemetry.GetSensorReadingsAsync(structure, binding, accessToken, HttpContext.RequestAborted);
            var canControl = User.CanControlEquipment() &&!string.IsNullOrWhiteSpace(structure.MacAddress);
            SensorBindings.FillSensor(model, binding, readings, canControl);

            if (!structure.IsOnline && !readings.HasActuators && !readings.HasTelemetry)
            {
                model.ControlOn = false;
            }

            if (model.ShowOptimalRange)
            {
                model.OptimalRange = await OptimalRangeAsync(structure.Id, binding.Metric, accessToken);
            }

            return View(model);
        }

        // Writes the structure's range for a reading as shown on the page ("18-28 °C"), in the page's unit. Returns an empty text when the reading has no range, which the page shows as not set
        private async Task<string> OptimalRangeAsync(string structureId, SensorMetric metric, string accessToken)
        {
            var key = metric switch
            {
                SensorMetric.Ph => "PH",
                SensorMetric.Ec => "EC",
                SensorMetric.Temperature => "TEMPERATURE",
                SensorMetric.Humidity => "HUMIDITY",
                _ => null
            };

            var sensor = key == null ? null : ThresholdCatalog.ForKey(key);

            if (sensor == null || !int.TryParse(structureId, out var id))
                return "";

            var ranges = await _thresholds.GetStructureAsync(id, accessToken, HttpContext.RequestAborted);

            if (ranges == null || !ranges.TryGetValue(sensor.Key, out var range))
                return "";

            var min = (range.Min * sensor.Scale).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            var max = (range.Max * sensor.Scale).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

            return min + "-" + max + " " + sensor.Unit;
        }
    }
}
