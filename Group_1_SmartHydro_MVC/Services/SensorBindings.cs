using System.Globalization;
using System.Text.RegularExpressions;
using Group_1_SmartHydro_MVC.Models;

namespace Group_1_SmartHydro_MVC.Services
{
    public enum SensorMetric
    {
        None,
        Water,
        Ph,
        Temperature,
        Light,
        Ec,
        Humidity
    }

    // Says which reading and which relay a sensor page or dashboard card is about
    public sealed record SensorBinding(SensorMetric Metric, string? ControlKey);

    // The two lines shown under a page's dose buttons: what is still waiting to run and the last dose that finished
    public sealed record DoseSummary(string Pending, string Last, bool HasPending,
        Guid? DoseId = null, string? Status = null, string? Reason = null);

    // Links each sensor to its reading and relay, and copies the API's readings onto dashboard cards and sensor pages
    public static class SensorBindings
    {
        private const int MaxActivityEntries = 8;

        private static readonly Regex TimedName = new(@"_TIMED_(\d+)S$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Finds the reading and relay for a sensor page by its action name.
        public static SensorBinding For(string? action)
        {
            return action switch
            {
                "WaterPump" => new SensorBinding(SensorMetric.Water, "mainWaterPump"),
                "CleanWater" => new SensorBinding(SensorMetric.Ph, ActuatorControls.Ph),
                "Temperature" => new SensorBinding(SensorMetric.Temperature, "circulationFan"),
                "Sunlight" => new SensorBinding(SensorMetric.Light, "growLight"),
                "Light" => new SensorBinding(SensorMetric.Light, "growLight"),
                "Nutrients" => new SensorBinding(SensorMetric.Ec, ActuatorControls.Ec),
                "Humidity" => new SensorBinding(SensorMetric.Humidity, "extractorFan"),
                "SpraySystem" => new SensorBinding(SensorMetric.Water, "spraySystem"),
                _ => new SensorBinding(SensorMetric.None, null)
            };
        }

        // Formats a time from the API as the local "d MMM, HH:mm" used elsewhere on the site
        public static string FormatTime(DateTime? time)
        {
            if (!time.HasValue || time.Value <= DateTime.UnixEpoch.AddDays(1))
                return "";

            return SouthAfricaTime.ToSast(time.Value).ToString("d MMM, HH:mm", CultureInfo.InvariantCulture);
        }

        // Fills every dashboard card with its reading, its relay state and the control key its toggle sends
        public static void FillCards(IEnumerable<DashboardCardViewModel> cards, StructureReadings readings)
        {
            foreach (var card in cards)
            {
                var binding = For(card.LinkAction);

                if (binding.ControlKey != null)
                    card.ControlKey = binding.ControlKey;

                if (readings.HasTelemetry)
                    FillCardValue(card, binding.Metric, readings);

                if (readings.HasActuators && binding.ControlKey != null)
                {
                    card.ControlOn = ActuatorControls.IsOn(readings.Actuators, binding.ControlKey);

                    var changed = FormatTime(readings.ActuatorsUpdatedAt);
                    if (changed.Length > 0)
                        card.ControlCaption = "Last reported " + changed;
                }
            }
        }

        // Fills one card's value and unit from the latest reading. The water and spray system cards show the float sensor's level as text
        private static void FillCardValue(DashboardCardViewModel card, SensorMetric metric, StructureReadings readings)
        {
            if (metric == SensorMetric.Water)
            {
                card.Value = WaterLevelText(readings);
                card.ValueIsText = true;
                return;
            }

            if (TryFormatReading(metric, readings, out var value, out var unit))
            {
                card.Value = value;
                card.Unit = unit;
                card.ValueIsText = value == "Sensor Fault";
                if (metric == SensorMetric.Ph && (readings.Ph < 0m || readings.Ph > 14m))
                {
                    card.PhDisplayFallback = readings.PhDisplayFallback;
                    card.Value = readings.PhDisplayFallback?.Value.ToString("0.0", CultureInfo.InvariantCulture) ?? "Waiting for a valid reading";
                    card.Unit = readings.PhDisplayFallback == null ? "" : "pH";
                    card.ValueIsText = readings.PhDisplayFallback == null;
                    card.ValueCaption = PhReadingLabel(readings.PhDisplayFallback);
                }
            }
        }

        // Copies the readings onto a sensor page: value, last sync, trend, relay state and activity
        public static void FillSensor(SensorDetailViewModel model, SensorBinding binding, StructureReadings readings, bool canControl)
        {
            var hasControl = binding.ControlKey != null;

            if (hasControl)
                model.ControlKey = binding.ControlKey!;

            model.CanControl = canControl && hasControl;

            if (hasControl && !canControl)
                model.ControlHint = "View only";

            var synced = FormatTime(readings.RecordedAt);
            if (synced.Length > 0)
                model.LastSyncLabel = synced;

            if (model.HeroKind == SensorHeroKind.Reading && readings.HasTelemetry
                && TryFormatReading(binding.Metric, readings, out var value, out _))
            {
                model.HeroValue = value;
                if (value == "Sensor Fault") model.HeroUnit = "";
                if (binding.Metric == SensorMetric.Ph && (readings.Ph < 0m || readings.Ph > 14m))
                {
                    model.PhDisplayFallback = readings.PhDisplayFallback;
                    model.HeroValue = readings.PhDisplayFallback?.Value.ToString("0.0", CultureInfo.InvariantCulture) ?? "Waiting for a valid reading";
                    model.HeroUnit = readings.PhDisplayFallback == null ? "" : "pH";
                    model.PhReadingLabel = PhReadingLabel(readings.PhDisplayFallback);
                }
            }

            if (model.ShowTrend)
                FillTrend(model, binding.Metric, readings);

            if (hasControl)
                FillControl(model, binding.ControlKey!, readings);

            FillDosing(model, binding, readings);

            if (binding.Metric == SensorMetric.Ph && readings.HasTelemetry && readings.Ph >= 0 && (readings.Ph < 2 || readings.Ph > 12.5m))
                model.MeasurementNotice = "This pH reading looks wrong. Check the pH probe and its calibration before adding anything.";

            if (model.HeroKind == SensorHeroKind.Status && hasControl)
                model.HeroDetail = LastOn(readings, binding.ControlKey!);

            FillSprayTiles(model, readings);
        }

        // Fills the relay toggle, its caption and the activity log from the relay state and history
        private static string PhReadingLabel(PhDisplayFallback? previous)
        {
            if (previous == null) return "No previous reading available";
            var age = Math.Max(previous.AgeSeconds, (long)(DateTime.UtcNow - previous.RecordedAt).TotalSeconds);
            var minutes = Math.Max(0, age / 60);
            return minutes == 0 ? "Last reading, less than a minute ago" :
                $"Last reading, {minutes} {(minutes == 1 ? "minute" : "minutes")} ago";
        }

        // Fills the relay toggle, its caption and the activity log from the relay state and history
        private static void FillControl(SensorDetailViewModel model, string controlKey, StructureReadings readings)
        {
            if (readings.HasActuators)
            {
                model.ControlOn = ActuatorControls.IsOn(readings.Actuators, controlKey);

                var changed = FormatTime(readings.ActuatorsUpdatedAt);
                if (changed.Length > 0)
                    model.ControlCaption = "Last reported " + changed;
            }

            var label = model.ControlLabel;
            if (string.IsNullOrWhiteSpace(label))
                label = model.SensorName;

            bool MatchesAudit(AuditRecord row) => controlKey switch {
                "ph" => row.Action.StartsWith("PH_", StringComparison.OrdinalIgnoreCase) || row.Action == "ph",
                "ec" => row.Action is "NUTRIENT_UP" or "DILUTION_DOWN" or "EC_SENSOR" or "ec",
                "growLight" => row.Action == "gl" || ActuatorControls.MatchesHistory(controlKey, row.Action),
                "circulationFan" => row.Action == "fc" || ActuatorControls.MatchesHistory(controlKey, row.Action),
                "extractorFan" => row.Action == "fe" || ActuatorControls.MatchesHistory(controlKey, row.Action),
                "mainWaterPump" => row.Action == "wp" || ActuatorControls.MatchesHistory(controlKey, row.Action),
                "spraySystem" => row.Action == "sp" || ActuatorControls.MatchesHistory(controlKey, row.Action),
                _ => ActuatorControls.MatchesHistory(controlKey, row.Action)
            };
            var entries = readings.Audit.Where(MatchesAudit)
                .Select(row => (At: row.RecordedAt ?? row.ReceivedAt, Entry: SensorActivityText.FromAudit(row)))
                .ToList();
            foreach (var entry in readings.Events.Where(e => ActuatorControls.MatchesHistory(controlKey, e.Actuator)))
            {
                var timed = TimedName.Match(entry.Actuator);
                var title = label + (entry.NewState ? " switched on" : " switched off");
                var summary = "Switch change recorded.";
                if (timed.Success) summary = "A timed request was recorded for " + timed.Groups[1].Value + " seconds. This does not confirm completion.";
                if (entry.ChangedBy.StartsWith("Node ACK", StringComparison.OrdinalIgnoreCase))
                    summary = "The controller confirmed this relay state.";
                entries.Add((entry.ChangedAt, new ActivityLogEntry {
                    Title = title, Summary = summary, Description = "Recorded by " + entry.ChangedBy,
                    Time = FormatTime(entry.ChangedAt)
                }));
            }
            model.ActivityLog.AddRange(entries.OrderByDescending(x => x.At).Take(MaxActivityEntries).Select(x => x.Entry));
        }

        // Fills the pending and last dose shown under the dose buttons of the pH and nutrient pages
        private static void FillDosing(SensorDetailViewModel model, SensorBinding binding, StructureReadings readings)
        {
            var group = DosingCatalog.GroupFor(binding.Metric);

            if (group == null)
                return;

            var summary = SummariseDoses(readings.Doses, group);

            model.ShowDosing = true;
            model.ManualPhOverrideRequired = model.ManualPhOverrideAllowed &&
                (!readings.HasTelemetry || readings.Ph < 0m || readings.Ph > 14m);
            model.DosePendingText = summary.Pending;
            model.DoseLastText = summary.Last;
            model.DoseHasPending = summary.HasPending || summary.Status == "UNCONFIRMED";
            model.DoseReviewId = summary.Status == "UNCONFIRMED" ? summary.DoseId : null;
        }

        // Summarises the waiting and last dose for one pump group.
        public static DoseSummary SummariseDoses(IEnumerable<DoseRecord> doses, string group, Guid? doseId = null)
        {
            var allDoses = doses.ToList();
            var unresolved = allDoses.Where(d => DoseStatus(d) == "UNCONFIRMED").OrderBy(d => d.CreatedAt).FirstOrDefault();
            var inGroup = allDoses.Where(d => DosingCatalog.InGroup(d.DosingType, group)).ToList();

            var pending = inGroup
                .Where(d => IsWaiting(DoseStatus(d)))
                .OrderBy(d => d.CreatedAt)
                .ToList();

            var pendingText = "No dose waiting";
            if (pending.Count == 1)
                pendingText = DoseStatusLabel(DoseStatus(pending[0])) + ": " + DoseName(pending[0]);
            else if (pending.Count > 1)
                pendingText = pending.Count + " doses waiting, next: " + DoseName(pending[0]);

            var last = inGroup
                .Where(d => !IsWaiting(DoseStatus(d)))
                .OrderByDescending(d => d.ExecutedAt ?? d.CreatedAt)
                .FirstOrDefault();

            var lastText = "No dose recorded yet";
            if (last != null)
            {
                var when = FormatTime(last.ExecutedAt ?? last.CreatedAt);
                lastText = DoseStatusLabel(DoseStatus(last)) + ": " + DoseName(last) + ", " + when;
            }
            // An unresolved dose blocks the whole controller, including the other pump group, and is not hidden by newer doses.
            if (unresolved != null)
                pendingText = "Dosing blocked: " + DoseName(unresolved) + ". We could not confirm it finished. Check the controller before dosing again.";
            var selected = unresolved ?? (doseId.HasValue ? allDoses.FirstOrDefault(d => d.Id == doseId) : pending.FirstOrDefault() ?? last);
            if (doseId.HasValue && selected == null)
                return new DoseSummary("Waiting for dose update", lastText, true, doseId, "UNKNOWN");
            return new DoseSummary(pendingText, lastText, pending.Count > 0,
                selected?.Id, selected == null ? null : DoseStatus(selected), selected?.Reason);
        }

        private static string DoseStatus(DoseRecord dose)
        {
            var status = dose.DeliveryStatus ?? (dose.Status.ToUpperInvariant() switch { "PENDING" => "QUEUED", "DISPATCHED" => "SENT", var value => value });
            if (status == "QUEUED" && DateTime.UtcNow > dose.CreatedAt.AddSeconds(60)) return "TIMED_OUT";
            if (status == "SENT" && DateTime.UtcNow > dose.CreatedAt.AddSeconds(60)) return "UNCONFIRMED";
            return status;
        }

        public static string DoseStatusLabel(string status) => status switch
        {
            "QUEUED" => "Queued, waiting to send", "SENT" => "Sent, waiting for reply",
            "RUNNING" => "Pump running", "AWAITING_COMPLETION" => "Started, waiting for completion",
            "COMPLETED" => "Pump cycle completed", "REJECTED" => "Not started, refused by controller",
            "TIMED_OUT" => "No reply from controller, timed out", "UNCONFIRMED" => "Finish not confirmed",
            "CANCELLED" => "Dose cancelled", "REVIEWED" => "Reviewed by operator, delivered amount unknown",
            "FAILED" => "Failed", _ => "Waiting for dose update"
        };

        // A dose is still waiting to run until the board reports it as completed or failed
        private static bool IsWaiting(string status)
        {
            return status is "QUEUED" or "SENT" or "RUNNING" or "AWAITING_COMPLETION" or "UNKNOWN";
        }

        // Names a dose with its label and volume, such as "Raise pH 10 mL"
        private static string DoseName(DoseRecord dose)
        {
            return DosingCatalog.LabelFor(dose.DosingType) + " (estimated " + dose.VolumeMl.ToString("0.##", CultureInfo.InvariantCulture) + " mL)";
        }

        // Fills the seven-day trend bars from the daily averages
        private static void FillTrend(SensorDetailViewModel model, SensorMetric metric, StructureReadings readings)
        {
            if (metric == SensorMetric.None || metric == SensorMetric.Water)
                return;

            var trendPoints = readings.Trend;
            if (trendPoints.Count == 0 && readings.HasTelemetry)
            {
                var sastToday = DateOnly.FromDateTime(SouthAfricaTime.ToSast(readings.RecordedAt ?? DateTime.UtcNow));
                trendPoints = new List<TrendPoint>
                {
                    new(sastToday, readings.Ph, readings.EcMs, readings.LightLux, readings.TemperatureC, readings.HumidityPercent)
                };
            }

            if (trendPoints.Count == 0)
                return;

            trendPoints = trendPoints.Where(point => metric switch
            {
                SensorMetric.Ph => point.AvgPh >= 0m && point.AvgPh <= 14m,
                SensorMetric.Ec => point.AvgEc >= 0m,
                SensorMetric.Temperature => point.AvgTemperature >= -40m && point.AvgTemperature <= 80m,
                SensorMetric.Humidity => point.AvgHumidity >= 0m && point.AvgHumidity <= 100m,
                SensorMetric.Light => point.AvgLight >= 0m,
                _ => true
            }).ToList();
            if (trendPoints.Count == 0) return;

            foreach (var point in trendPoints)
            {
                model.TrendLabels.Add(point.Date.ToString("ddd", CultureInfo.InvariantCulture));
                model.TrendValues.Add(TrendValue(metric, point));
            }

            var first = trendPoints[0].Date.ToString("d MMM", CultureInfo.InvariantCulture);
            var last = trendPoints[trendPoints.Count - 1].Date.ToString("d MMM", CultureInfo.InvariantCulture);
            model.TrendRangeLabel = first + " - " + last;
        }

        // Fills the spray system page's stat tiles: last watering, spray length and reservoir level. There is no spray schedule in the API yet
        private static void FillSprayTiles(SensorDetailViewModel model, StructureReadings readings)
        {
            if (model.StatTiles.Count == 0)
                return;

            foreach (var tile in model.StatTiles)
            {
                if (tile.Label == "Last Watered")
                    tile.Value = LastOn(readings, "spraySystem");

                if (tile.Label == "Spray Duration")
                {
                    foreach (var entry in readings.Events)
                    {
                        var timed = TimedName.Match(entry.Actuator);

                        if (timed.Success && ActuatorControls.MatchesHistory("spraySystem", entry.Actuator))
                        {
                            tile.Value = timed.Groups[1].Value;
                            tile.Unit = "s";
                            break;
                        }
                    }
                }

                if (tile.Label == "Reservoir Level" && readings.HasTelemetry)
                {
                    tile.Value = WaterLevelText(readings);

                    if (readings.WaterLevelDetected)
                    {
                        tile.PillText = "Good";
                        tile.PillLevel = "good";
                    }
                    else
                    {
                        tile.PillText = "Refill";
                        tile.PillLevel = "alert";
                    }
                }
            }
        }

        // Finds when a relay was last switched on, or an empty string when it never has been
        private static string LastOn(StructureReadings readings, string controlKey)
        {
            var last = readings.Events.FirstOrDefault(e => e.NewState && ActuatorControls.MatchesHistory(controlKey, e.Actuator));

            if (last == null)
                return "";

            return FormatTime(last.ChangedAt);
        }

        // Words the float sensor's reading
        private static string WaterLevelText(StructureReadings readings)
        {
            if (readings.WaterLevelDetected)
                return "Level OK";

            return "Low";
        }

        // Formats a sensor's latest reading and unit, returning false for a sensor with no reading column
        private static bool TryFormatReading(SensorMetric metric, StructureReadings readings, out string value, out string unit)
        {
            value = "";
            unit = "";

            bool fault = metric switch
            {
                SensorMetric.Ph => readings.Ph < 0m || readings.Ph > 14m,
                SensorMetric.Ec => readings.EcMs < 0m,
                SensorMetric.Temperature => readings.TemperatureC < -40m || readings.TemperatureC > 80m,
                SensorMetric.Humidity => readings.HumidityPercent < 0m || readings.HumidityPercent > 100m,
                SensorMetric.Light => readings.LightLux < 0m,
                _ => false
            };
            if (fault) { value = "Sensor Fault"; return true; }

            switch (metric)
            {
                case SensorMetric.Ph:
                    value = readings.Ph.ToString("0.0", CultureInfo.InvariantCulture);
                    unit = "pH";
                    return true;
                case SensorMetric.Temperature:
                    value = readings.TemperatureC.ToString("0.0", CultureInfo.InvariantCulture);
                    unit = "\u00B0C";
                    return true;
                case SensorMetric.Humidity:
                    value = readings.HumidityPercent.ToString("0", CultureInfo.InvariantCulture);
                    unit = "%";
                    return true;
                case SensorMetric.Light:
                    value = readings.LightLux.ToString("0", CultureInfo.InvariantCulture);
                    unit = "%";
                    return true;
                case SensorMetric.Ec:
                    value = (readings.EcMs * 1000m).ToString("0", CultureInfo.InvariantCulture);
                    unit = "\u00B5S/cm";
                    return true;
                default:
                    return false;
            }
        }

        // Picks a day's average for a sensor, in the same units as its latest reading
        private static double TrendValue(SensorMetric metric, TrendPoint point)
        {
            switch (metric)
            {
                case SensorMetric.Ph:
                    return (double)Math.Round(point.AvgPh, 1);
                case SensorMetric.Temperature:
                    return (double)Math.Round(point.AvgTemperature, 1);
                case SensorMetric.Humidity:
                    return (double)Math.Round(point.AvgHumidity, 0);
                case SensorMetric.Light:
                    return (double)Math.Round(point.AvgLight, 0);
                case SensorMetric.Ec:
                    return (double)Math.Round(point.AvgEc * 1000m, 0);
                default:
                    return 0;
            }
        }
    }
}
