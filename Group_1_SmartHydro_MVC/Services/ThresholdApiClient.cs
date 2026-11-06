using System.Net;
using Group_1_SmartHydro_MVC.Patterns.Singleton;

namespace Group_1_SmartHydro_MVC.Services
{
    public sealed record ThresholdValue(decimal Min, decimal Max);

    // One structure of a site with the range of each reading it has (saved or the healthy default), keyed by reading (PH, EC, ...) in the API's units
    public sealed record StructureThresholds(int Id, string Name, string Type, Dictionary<string, ThresholdValue> Values);

    public sealed record ThresholdSaveOutcome(bool Ok, int StatusCode, string Message);

    // Reads and saves the safe ranges of each structure. Every call goes through SmartHydroApiHttpClient
    public sealed class ThresholdApiClient
    {
        private readonly SmartHydroApiHttpClient _api;

        public ThresholdApiClient(SmartHydroApiHttpClient api)
        {
            _api = api;
        }

        // Loads every structure of a site with its ranges, or null when the API could not be reached or refused the request
        public async Task<List<StructureThresholds>?> GetSiteAsync(string siteId, string accessToken, CancellationToken ct = default)
        {
            try
            {
                using var response = await _api.SendAsync(HttpMethod.Get, $"api/Thresholds/site/{Uri.EscapeDataString(siteId)}", accessToken, null, ct);

                if (!response.IsSuccessStatusCode)
                    return null;

                var rows = await SmartHydroApiHttpClient.ReadJsonAsync(response, new List<StructureThresholdsDto>(), ct);
                var structures = new List<StructureThresholds>();

                foreach (var row in rows)
                {
                    var values = new Dictionary<string, ThresholdValue>(StringComparer.OrdinalIgnoreCase);

                    foreach (var threshold in row.Thresholds)
                        values.TryAdd(threshold.SensorType, new ThresholdValue(threshold.MinValue, threshold.MaxValue));

                    structures.Add(new StructureThresholds(row.StructureId, row.StructureName, row.StructureType, values));
                }

                return structures;
            }
            catch (HttpRequestException)
            {
                return null;
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                return null;
            }
        }

        // Loads the range of each reading of one structure (saved or the healthy default), keyed by reading (PH, EC, ...) in the API's units, or null when the API could not be reached or refused the request
        public async Task<Dictionary<string, ThresholdValue>?> GetStructureAsync(int structureId, string accessToken, CancellationToken ct = default)
        {
            try
            {
                using var response = await _api.SendAsync(HttpMethod.Get, $"api/Thresholds/structure/{structureId}", accessToken, null, ct);

                if (!response.IsSuccessStatusCode)
                    return null;

                var row = await SmartHydroApiHttpClient.ReadJsonAsync(response, new StructureThresholdsDto(), ct);
                var values = new Dictionary<string, ThresholdValue>(StringComparer.OrdinalIgnoreCase);

                foreach (var threshold in row.Thresholds)
                    values.TryAdd(threshold.SensorType, new ThresholdValue(threshold.MinValue, threshold.MaxValue));

                return values;
            }
            catch (HttpRequestException)
            {
                return null;
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                return null;
            }
        }

        // Saves one reading's range for one structure, in the API's units, and reports whether the API accepted it
        public async Task<ThresholdSaveOutcome> SaveAsync(int structureId, string sensorType, decimal min, decimal max, string accessToken, CancellationToken ct = default)
        {
            var body = new Dictionary<string, object>
            {
                ["structureId"] = structureId,
                ["sensorType"] = sensorType,
                ["minValue"] = min,
                ["maxValue"] = max
            };

            try
            {
                using var response = await _api.SendAsync(HttpMethod.Put, "api/Thresholds", accessToken, body, ct);

                if (response.IsSuccessStatusCode)
                    return new ThresholdSaveOutcome(true, (int)response.StatusCode, "");

                var problem = await SmartHydroApiHttpClient.ReadProblemAsync(response, ct);
                return new ThresholdSaveOutcome(false, (int)response.StatusCode, FailureMessage(response.StatusCode, problem.Message));
            }
            catch (HttpRequestException)
            {
                return new ThresholdSaveOutcome(false, (int)HttpStatusCode.BadGateway, FailureMessage(HttpStatusCode.BadGateway, null));
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                return new ThresholdSaveOutcome(false, (int)HttpStatusCode.GatewayTimeout, FailureMessage(HttpStatusCode.GatewayTimeout, null));
            }
        }

        // Chooses the message shown when the API did not accept a range
        private static string FailureMessage(HttpStatusCode status, string? apiMessage)
        {
            if (status == HttpStatusCode.Forbidden)
                return "Only operators can change thresholds.";

            if (status == HttpStatusCode.NotFound)
                return "This structure was not found, or you cannot change its thresholds.";

            if (status == HttpStatusCode.BadRequest && !string.IsNullOrWhiteSpace(apiMessage))
                return apiMessage;

            return "The thresholds could not be saved. Please try again.";
        }

        private sealed class StructureThresholdsDto
        {
            public int StructureId { get; set; }
            public string StructureName { get; set; } = "";
            public string StructureType { get; set; } = "";
            public List<ThresholdDto> Thresholds { get; set; } = new();
        }

        private sealed class ThresholdDto
        {
            public string SensorType { get; set; } = "";
            public decimal MinValue { get; set; }
            public decimal MaxValue { get; set; }
        }
    }
}
