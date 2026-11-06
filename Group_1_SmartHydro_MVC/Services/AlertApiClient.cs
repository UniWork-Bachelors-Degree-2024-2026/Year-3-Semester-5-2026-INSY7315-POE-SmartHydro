using System.Globalization;
using System.Net;
using Group_1_SmartHydro_MVC.Models;
using Group_1_SmartHydro_MVC.Patterns.Singleton;

namespace Group_1_SmartHydro_MVC.Services
{
    public sealed class AlertApiClient
    {
        private const int PageSize = 100;
        private const int MaxPages = 5;

        private static readonly Dictionary<string, string> ComponentNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["PH_SENSOR"] = "pH Sensor",
            ["EC_SENSOR"] = "EC Sensor",
            ["TEMPERATURE_SENSOR"] = "Temperature Sensor",
            ["WATER_RESERVOIR"] = "Water Reservoir"
        };

        private readonly SmartHydroApiHttpClient _api;

        public AlertApiClient(SmartHydroApiHttpClient api)
        {
            _api = api;
        }

        // Lists every alert (active and resolved) for a site, or null when the API could not be reached
        public async Task<List<AlertItemViewModel>?> GetAlertsAsync(string siteId, string accessToken, CancellationToken ct = default)
        {
            var alerts = new List<AlertItemViewModel>();

            for (var page = 1; page <= MaxPages; page++)
            {
                var path = $"api/Alerts?siteId={Uri.EscapeDataString(siteId)}&unresolvedOnly=false&page={page}&pageSize={PageSize}";
                using var response = await _api.SendAsync(HttpMethod.Get, path, accessToken, null, ct);

                if (!response.IsSuccessStatusCode)
                    return null;

                var reply = await SmartHydroApiHttpClient.ReadJsonAsync(response, new AlertPageDto(), ct);
                alerts.AddRange(reply.Items.Select(a => ToViewModel(a, siteId)));

                if (page >= reply.TotalPages)
                    break;
            }

            return alerts;
        }

        // Loads the next page of a site's active alerts, or null when the API could not be reached. Alerts that get resolved drop out of this list, so asking again returns the ones that were behind them
        public async Task<List<AlertItemViewModel>?> GetActiveAlertsPageAsync(string siteId, string accessToken, CancellationToken ct = default)
        {
            var path = $"api/Alerts?siteId={Uri.EscapeDataString(siteId)}&unresolvedOnly=true&page=1&pageSize={PageSize}";
            using var response = await _api.SendAsync(HttpMethod.Get, path, accessToken, null, ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var reply = await SmartHydroApiHttpClient.ReadJsonAsync(response, new AlertPageDto(), ct);
            return reply.Items.Select(a => ToViewModel(a, siteId)).ToList();
        }

        // Lists the deleted alerts of a site that can still be restored, or null when the API could not be reached
        public async Task<List<AlertItemViewModel>?> GetDeletedAlertsAsync(string siteId, string accessToken, CancellationToken ct = default)
        {
            using var response = await _api.SendAsync(HttpMethod.Get, $"api/Alerts/deleted?siteId={Uri.EscapeDataString(siteId)}", accessToken, null, ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var items = await SmartHydroApiHttpClient.ReadJsonAsync(response, new List<AlertDto>(), ct);
            return items.Select(a => ToViewModel(a, siteId)).ToList();
        }

        // Counts the alerts in a site that still need attention, returning 0 when the API could not be reached
        public async Task<int> GetUnresolvedCountAsync(string siteId, string accessToken, CancellationToken ct = default)
        {
            using var response = await _api.SendAsync(HttpMethod.Get, $"api/Alerts/unresolved-count?siteId={Uri.EscapeDataString(siteId)}", accessToken, null, ct);

            if (!response.IsSuccessStatusCode)
                return 0;

            var reply = await SmartHydroApiHttpClient.ReadJsonAsync(response, new CountDto(), ct);
            return reply.UnresolvedCount;
        }

        // Marks one alert as resolved and records who cleared it
        public async Task<bool> ResolveAsync(string alertId, string clearedBy, string accessToken, CancellationToken ct = default)
        {
            if (!long.TryParse(alertId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                return false;

            return await SendAsync(HttpMethod.Put, $"api/Alerts/{id}/resolve", new { clearedBy }, accessToken, ct);
        }

        // Moves an alert to deleted items
        public async Task<bool> DeleteAsync(string alertId, string accessToken, CancellationToken ct = default)
        {
            if (!long.TryParse(alertId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                return false;

            return await SendAsync(HttpMethod.Delete, $"api/Alerts/{id}", null, accessToken, ct);
        }

        // Brings a deleted alert back
        public async Task<bool> RestoreAsync(string alertId, string accessToken, CancellationToken ct = default)
        {
            if (!long.TryParse(alertId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                return false;

            return await SendAsync(HttpMethod.Post, $"api/Alerts/{id}/restore", null, accessToken, ct);
        }

        // Permanently removes a deleted alert (Admin only)
        public async Task<bool> PurgeAsync(string alertId, string accessToken, CancellationToken ct = default)
        {
            if (!long.TryParse(alertId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                return false;

            return await SendAsync(HttpMethod.Delete, $"api/Alerts/{id}/purge", null, accessToken, ct);
        }

        // Sends a request that answers with no body and reports whether the API accepted it
        private async Task<bool> SendAsync(HttpMethod method, string path, object? body, string accessToken, CancellationToken ct)
        {
            using var response = await _api.SendAsync(method, path, accessToken, body, ct);
            return response.StatusCode == HttpStatusCode.NoContent || response.IsSuccessStatusCode;
        }

        // Maps an API alert onto the row the alerts page shows
        private static AlertItemViewModel ToViewModel(AlertDto dto, string siteId)
        {
            var icon = SensorIconCatalog.Get(IconKeyFor(dto.Component));

            var severity = AlertSeverity.Caution;
            if (string.Equals(dto.Severity, "CRITICAL", StringComparison.OrdinalIgnoreCase))
                severity = AlertSeverity.Alert;

            var occurredAt = dto.OccurredAt;
            if (occurredAt.Kind == DateTimeKind.Utc)
                occurredAt = occurredAt.ToLocalTime();
            else if (occurredAt.Kind == DateTimeKind.Unspecified)
                occurredAt = DateTime.SpecifyKind(occurredAt, DateTimeKind.Utc).ToLocalTime();

            var location = dto.StructureName;
            if (string.IsNullOrWhiteSpace(location))
                location = "Unknown structure";

            return new AlertItemViewModel
            {
                Id = dto.Id.ToString(CultureInfo.InvariantCulture),
                SiteId = siteId,
                Component = DisplayName(dto.Component),
                Location = location,
                IconName = icon.Name,
                IconIsMaterialSymbol = icon.IsMaterialSymbol,
                IconText = icon.Text,
                IconColorClass = icon.ColorClass,
                Severity = severity,
                IsResolved = dto.IsResolved,
                Value = dto.Value ?? "",
                Detail = dto.Detail ?? "",
                OccurredAt = occurredAt,
                ClearedBy = dto.ClearedBy ?? ""
            };
        }

        // Picks the sensor icon that matches the API's component name
        private static string IconKeyFor(string component)
        {
            var name = (component ?? string.Empty).ToUpperInvariant();

            if (name.Contains("PH"))
                return SensorIconCatalog.CleanWater;

            if (name.Contains("EC") || name.Contains("NUTRIENT"))
                return SensorIconCatalog.Nutrients;

            if (name.Contains("TEMP"))
                return SensorIconCatalog.Temperature;

            if (name.Contains("HUMID"))
                return SensorIconCatalog.Humidity;

            if (name.Contains("LIGHT") || name.Contains("LUX"))
                return SensorIconCatalog.Light;

            if (name.Contains("SPRAY"))
                return SensorIconCatalog.SpraySystem;

            if (name.Contains("WATER") || name.Contains("PUMP"))
                return SensorIconCatalog.WaterPump;

            return "";
        }

        // Turns a component code such as WATER_RESERVOIR into a readable name
        private static string DisplayName(string component)
        {
            if (string.IsNullOrWhiteSpace(component))
                return "System";

            if (ComponentNames.TryGetValue(component, out var known))
                return known;

            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(component.Replace('_', ' ').ToLowerInvariant());
        }

        private sealed class AlertPageDto
        {
            public List<AlertDto> Items { get; set; } = new();
            public int TotalPages { get; set; }
        }

        private sealed class AlertDto
        {
            public long Id { get; set; }
            public string? StructureName { get; set; }
            public string Component { get; set; } = "";
            public string Severity { get; set; } = "";
            public string? Value { get; set; }
            public string? Detail { get; set; }
            public DateTime OccurredAt { get; set; }
            public bool IsResolved { get; set; }
            public string? ClearedBy { get; set; }
        }

        private sealed class CountDto
        {
            public int UnresolvedCount { get; set; }
        }
    }
}
