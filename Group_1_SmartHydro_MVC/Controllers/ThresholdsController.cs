using System.Globalization;
using Group_1_SmartHydro_MVC.Models;
using Group_1_SmartHydro_MVC.Services;
using Microsoft.AspNetCore.Mvc;

namespace Group_1_SmartHydro_MVC.Controllers
{
    public class ThresholdsController : Controller
    {
        private readonly ThresholdApiClient _thresholds;
        private readonly ISiteContext _siteContext;

        // Creates the controller and stores the services used to read and save the safe ranges and to find the current site.
        public ThresholdsController(ThresholdApiClient thresholds, ISiteContext siteContext)
        {
            _thresholds = thresholds;
            _siteContext = siteContext;
        }

        // Shows the safe range for each reading, one section per structure in the current site. Operators and admins only, anyone else is sent back to the structure list with a message.
        [HttpGet]
        public async Task<IActionResult> Index(CancellationToken ct)
        {
            if (!User.CanManageSites())
            {
                return RedirectToAction("Index", "Structure", new { denied = 1 });
            }

            var site = await _siteContext.GetCurrentAsync();

            if (site == null)
            {
                return View(new ThresholdsPageViewModel());
            }

            var loaded = await _thresholds.GetSiteAsync(site.Id, User.GetAccessToken(), ct);
            return View(BuildPage(site, loaded, null, null, ""));
        }

        // Saves the ranges typed into one structure's section. Every range is checked first and nothing is saved unless all of them are valid.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(ThresholdSectionForm form, CancellationToken ct)
        {
            if (!User.CanManageSites())
            {
                return RedirectToAction("Index", "Structure", new { denied = 1 });
            }

            var site = await _siteContext.GetCurrentAsync();

            if (site == null)
            {
                return RedirectToAction(nameof(Index));
            }

            var accessToken = User.GetAccessToken();
            var loaded = await _thresholds.GetSiteAsync(site.Id, accessToken, ct);

            if (loaded == null)
            {
                return View("Index", BuildPage(site, null, null, null, ""));
            }

            // Only a structure of the current site can be saved, so a range can never be written to another site's structure
            var structure = loaded.FirstOrDefault(s => string.Equals(s.Id.ToString(CultureInfo.InvariantCulture), form.StructureId?.Trim(), StringComparison.Ordinal));
            var group = structure == null ? null : ThresholdCatalog.ForApiType(structure.Type);

            if (structure == null || group == null)
            {
                return RedirectToAction(nameof(Index));
            }

            var posted = new Dictionary<string, ThresholdRowForm>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in form.Rows)
            {
                if (!string.IsNullOrWhiteSpace(row.Sensor))
                {
                    posted[row.Sensor.Trim()] = row;
                }
            }

            var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var toSave = new List<(ThresholdSensor Sensor, decimal Min, decimal Max)>();

            foreach (var sensor in group.Sensors)
            {
                posted.TryGetValue(sensor.Key, out var row);
                var check = CheckRow(sensor, row?.Min, row?.Max);

                if (check.Error != null)
                {
                    errors[sensor.Key] = check.Error;
                }
                else if (!check.Blank)
                {
                    toSave.Add((sensor, check.Min, check.Max));
                }
            }

            if (errors.Count > 0)
            {
                var invalid = BuildPage(site, loaded, form, errors, "Nothing was saved. Please fix the highlighted values.");
                return View("Index", invalid);
            }

            if (toSave.Count == 0)
            {
                var empty = BuildPage(site, loaded, form, errors, "Enter a minimum and maximum for at least one reading.");
                return View("Index", empty);
            }

            var savedCount = 0;

            foreach (var item in toSave)
            {
                var outcome = await _thresholds.SaveAsync(structure.Id, item.Sensor.Key, item.Min, item.Max, accessToken, ct);

                if (!outcome.Ok)
                {
                    var message = "Could not save " + item.Sensor.Label + ": " + outcome.Message;

                    if (savedCount > 0)
                    {
                        message += " The readings above it were saved.";
                    }

                    errors[item.Sensor.Key] = outcome.Message;
                    var failed = BuildPage(site, loaded, form, errors, message);
                    return View("Index", failed);
                }

                savedCount++;
            }

            TempData["ThresholdsMessage"] = structure.Name + " thresholds saved.";
            return RedirectToAction(nameof(Index));
        }

        // The result of checking one row of the form: it is blank (nothing to save), has an error, or holds the range in the API's units.
        private sealed record RowCheck(bool Blank, string? Error, decimal Min, decimal Max);

        // Checks one reading's minimum and maximum as typed. Both may be left empty to skip the reading, otherwise both must be numbers in the reading's allowed span with the minimum below the maximum.
        private static RowCheck CheckRow(ThresholdSensor sensor, string? minText, string? maxText)
        {
            var minBlank = string.IsNullOrWhiteSpace(minText);
            var maxBlank = string.IsNullOrWhiteSpace(maxText);

            if (minBlank && maxBlank)
            {
                return new RowCheck(true, null, 0, 0);
            }

            if (minBlank || maxBlank)
            {
                return new RowCheck(false, "Enter both a minimum and a maximum.", 0, 0);
            }

            if (!TryParse(minText, out var min) || !TryParse(maxText, out var max))
            {
                return new RowCheck(false, "Enter numbers only.", 0, 0);
            }

            if (min < sensor.Lowest || min > sensor.Highest || max < sensor.Lowest || max > sensor.Highest)
            {
                return new RowCheck(false, "Values must be between " + Show(sensor.Lowest) + " and " + Show(sensor.Highest) + " " + sensor.Unit + ".", 0, 0);
            }

            if (min >= max)
            {
                return new RowCheck(false, "The minimum must be lower than the maximum.", 0, 0);
            }

            var apiMin = Math.Round(min / sensor.Scale, 2, MidpointRounding.AwayFromZero);
            var apiMax = Math.Round(max / sensor.Scale, 2, MidpointRounding.AwayFromZero);

            if (apiMin >= apiMax)
            {
                return new RowCheck(false, "The minimum and maximum are too close together.", 0, 0);
            }

            return new RowCheck(false, null, apiMin, apiMax);
        }

        // Reads a number typed on the page. A comma is accepted as the decimal point, and the invariant culture is used so the site's language cannot change the meaning.
        private static bool TryParse(string? text, out decimal value)
        {
            value = 0;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var clean = text.Trim().Replace(',', '.');
            return decimal.TryParse(clean, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
        }

        // Formats a number without trailing zeros, always with a full stop as the decimal point.
        private static string Show(decimal value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        // Builds the page from the site's structures and their ranges (saved or default). When a section was just submitted and refused, that section shows what was typed, with its errors, instead of the saved values.
        private static ThresholdsPageViewModel BuildPage(SiteViewModel site, List<StructureThresholds>? loaded, ThresholdSectionForm? submitted, Dictionary<string, string>? rowErrors, string sectionMessage)
        {
            var page = new ThresholdsPageViewModel { HasSite = true };

            if (loaded == null)
            {
                page.LoadFailed = true;
                return page;
            }

            foreach (var structure in loaded)
            {
                var group = ThresholdCatalog.ForApiType(structure.Type);

                if (group == null)
                {
                    continue;
                }

                var section = new ThresholdSectionViewModel
                {
                    Key = structure.Id.ToString(CultureInfo.InvariantCulture),
                    Title = structure.Name,
                    Subtitle = group.Title + " in " + site.Name
                };

                var isSubmitted = submitted != null && string.Equals(submitted.StructureId?.Trim(), section.Key, StringComparison.Ordinal);

                if (isSubmitted)
                {
                    section.Message = sectionMessage;
                }

                foreach (var sensor in group.Sensors)
                {
                    var row = new ThresholdRowViewModel
                    {
                        Key = sensor.Key,
                        Label = sensor.Label,
                        Unit = sensor.Unit,
                        Lowest = Show(sensor.Lowest),
                        Highest = Show(sensor.Highest)
                    };

                    if (isSubmitted)
                    {
                        var typed = submitted!.Rows.FirstOrDefault(r => string.Equals(r.Sensor?.Trim(), sensor.Key, StringComparison.OrdinalIgnoreCase));

                        row.Min = typed?.Min?.Trim() ?? "";
                        row.Max = typed?.Max?.Trim() ?? "";

                        if (rowErrors != null && rowErrors.TryGetValue(sensor.Key, out var error))
                        {
                            row.Error = error;
                        }
                    }
                    else if (structure.Values.TryGetValue(sensor.Key, out var value))
                    {
                        row.Min = Show(value.Min * sensor.Scale);
                        row.Max = Show(value.Max * sensor.Scale);
                    }

                    section.Rows.Add(row);
                }

                page.Sections.Add(section);
            }

            return page;
        }
    }
}
