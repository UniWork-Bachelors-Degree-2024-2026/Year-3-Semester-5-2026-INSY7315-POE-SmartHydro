using System.Globalization;
using Group_1_SmartHydro_MVC.Models;

namespace Group_1_SmartHydro_MVC.Services;

public static class SensorActivityText
{
    public static IReadOnlyList<string> DescriptionParagraphs(string description) =>
        description.Split(new[] { '|', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ExplainRecordedDetail).ToArray();

    private static string ExplainRecordedDetail(string detail)
    {
        if (detail.StartsWith("ML class ", StringComparison.OrdinalIgnoreCase))
            return "An automatic assessment was recorded. This alone does not confirm equipment operation.";
        if (detail.Equals("local action; no cloud CID", StringComparison.OrdinalIgnoreCase))
            return "This activity was recorded locally by the controller. It was not linked to a cloud command.";
        int reportedAt = detail.IndexOf(" reported ", StringComparison.OrdinalIgnoreCase);
        if (reportedAt > 0) {
            string action = detail[..reportedAt], state = detail[(reportedAt + 10)..];
            if (Title(action) != "Controller activity" && state is "ON" or "OFF")
                return Title(action) + " was reported " + state + " by the controller.";
        }
        return detail switch {
            "GPIO_ON" => "The controller reported its switch output on. Equipment power was not measured.",
            "GPIO_OFF" => "The controller reported its switch output off. Equipment power was not measured.",
            "DOSE_STARTED" => "The controller reported that the dosing cycle started.",
            "DOSE_COMPLETED" => "The controller reported that the dosing cycle finished. Liquid delivery was not measured.",
            "REFUSED" => "The controller did not allow this request.",
            "CANCELLED" => "The controller reported that the cycle was cancelled.",
            "SAFETY_CUTOFF" => "The controller reported that the safety timer stopped the cycle.",
            _ => detail
        };
    }

    public static string Title(string action) => action.ToUpperInvariant() switch
    {
        "PH_UP" => "Raise pH", "PH_DOWN" => "Lower pH", "PH" => "pH pumps",
        "NUTRIENT_UP" => "Add nutrients", "DILUTION_DOWN" => "Add clean water", "EC" => "Nutrient pumps",
        "EC_SENSOR" => "Nutrient reading", "PH_SENSOR" => "pH reading",
        "GL" or "GROW_LIGHT" => "Grow lights", "FC" or "CIRCULATION_FAN" => "Circulation fan",
        "FE" or "EXTRACTOR_FAN" => "Extractor fan", "WP" or "MAIN_WATER_PUMP" => "Water pump",
        "SP" or "SPRAY_SYSTEM" => "Spray system", _ => "Controller activity"
    };

    public static string Source(string source) => source switch
    {
        "User - MVC" => "Dashboard request", "User - SoftAP" => "Local hotspot",
        "User - BLE" => "Local Bluetooth",
        "Threshold Auto-Dosing" => "Automatic control", "ML Recommendation" => "Suggested action",
        "Schedule Timer" => "Schedule", "Safety Interlock" => "Safety check", _ => "Controller"
    };

    public static ActivityLogEntry FromAudit(AuditRecord row)
    {
        var status = row.Status.ToUpperInvariant();
        bool dose = row.Action is "PH_UP" or "PH_DOWN" or "NUTRIENT_UP" or "DILUTION_DOWN";
        string summary = status switch
        {
            "GPIO_ON" => "The controller reported this output switched on.",
            "GPIO_OFF" => "The controller reported this output switched off.",
            "DOSE_STARTED" or "RUNNING" => "The controller reported that the pump cycle started.",
            "DOSE_COMPLETED" or "COMPLETED" => "Pump cycle reported complete. Liquid delivery was not measured.",
            "REFUSED" or "REJECTED" => dose ? "Dose not started." : "The controller refused the change.",
            "RECOMMENDATION" => "Suggested action only. This event does not confirm a pump started.",
            "QUEUED" => "Request saved. Waiting for a controller reply.",
            "SENT" => "Request sent. Waiting for a controller reply.",
            "ACCEPTED" => "The controller accepted the request.",
            "TIMED_OUT" or "EXPIRED" => "The request expired without confirmation. Please check the equipment before trying again.",
            "UNCONFIRMED" => dose ? "We have not received the result of the earlier dose. Dosing is paused to avoid repeating it." : "No controller confirmation was received. Please check the equipment. The last reported state is shown.",
            "CANCELLED" => "The controller reported that the cycle was cancelled.",
            "SAFETY_CUTOFF" => "The controller reported that the safety cutoff stopped the cycle.",
            "BLOCKED" => "Automatic dosing is paused.",
            "REVIEWED" => "An operator checked the pumps and reservoir and reviewed the earlier request. Its delivered amount remains unknown.",
            "SUPERSEDED" => "A newer request replaced this one.",
            _ => "Activity recorded. Open the details for the controller's report."
        };
        if (status is "REFUSED" or "REJECTED" or "CANCELLED" or "SAFETY_CUTOFF" or "BLOCKED")
            summary += Reason(row.Details);

        // Only use the reading saved with this event, never today's reading for an older action.
        string unit = row.Action.StartsWith("PH_", StringComparison.Ordinal) ? "pH" :
            row.Action is "NUTRIENT_UP" or "DILUTION_DOWN" or "EC_SENSOR" ? "µS/cm" : "";
        decimal scale = unit == "µS/cm" ? 1000m : 1m;
        string Number(decimal value) => (value * scale).ToString(unit == "µS/cm" ? "0" : "0.##", CultureInfo.InvariantCulture);
        if (unit.Length > 0 && row.TriggerValue is >= 0)
        {
            summary += $" Recorded reading: {Number(row.TriggerValue.Value)} {unit}.";
            if (row.Minimum.HasValue && row.Maximum.HasValue)
                summary += $" Set range: {Number(row.Minimum.Value)}–{Number(row.Maximum.Value)} {unit}.";
        }
        var technical = row.Details;
        if (row.CommandId > 0) technical = $"Command {row.CommandId}. " + technical;
        return new ActivityLogEntry { Title = Title(row.Action), Source = Source(row.Source), Summary = summary,
            Description = technical, IsWarning = status is "REFUSED" or "REJECTED" or "CANCELLED" or "SAFETY_CUTOFF" or "BLOCKED" or "UNCONFIRMED" or "TIMED_OUT" or "EXPIRED",
            Time = row.CaptureTimeUncertain ? "Time uncertain; received " + SensorBindings.FormatTime(row.ReceivedAt) : SensorBindings.FormatTime(row.RecordedAt) };
    }

    private static string Reason(string details)
    {
        foreach (var part in details.Split('|', StringSplitOptions.TrimEntries))
        {
            var message = part.ToLowerInvariant() switch
            {
                "sensor_fault" or "sensor fault" => " The sensor reading is unavailable. Check the sensor before dosing.",
                "sensor_stale" => " The sensor reading is out of date. Wait for fresh readings.",
                "ph_override_expired" => " The controller refused the override under its pH reading policy. Check that it has the updated tower firmware.",
                "sensor_recovery" or "sensor_recovering" => " Waiting for stable sensor readings after a fault.",
                "mixing_lockout" or "mixing lockout" => " The waiting period after the previous dose has not finished. This does not confirm water circulation.",
                "pump_busy" or "pump busy" or "busy" => " Another pump cycle is already running.",
                "ph_hourly_cap" or "ph hourly cap" => " The hourly pH dosing limit has been reached.",
                "pulse_limit" or "pulse limit" => " The requested cycle exceeds the dosing limit.",
                "safety_storage" or "safety storage unavailable" => " Safety information or a command receipt could not be saved. Check the controller.",
                "cutoff_timer" or "cutoff timer unavailable" => " The safety timer is unavailable. Check the controller.",
                "command_expired" => " The request arrived too late. Check the connection.",
                "clock_unavailable" => " The controller needs to synchronise its clock before accepting this request.",
                "outer safety limit" or "safety_blocked" => " A safety limit prevented dosing. Check the controller details.",
                _ => ""
            };
            if (message.Length > 0) return message;
        }
        return " Open Technical details for the recorded reason.";
    }
}
