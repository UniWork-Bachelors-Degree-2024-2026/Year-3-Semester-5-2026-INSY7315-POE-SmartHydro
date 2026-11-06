using System.Globalization;
using System.Net;
using Group_1_SmartHydro_MVC.Models;
using Group_1_SmartHydro_MVC.Patterns.Singleton;

namespace Group_1_SmartHydro_MVC.Services
{
    public sealed record ControlOutcome(bool Ok, int StatusCode, bool On, string Message, int? CommandId = null, bool Pending = false, Guid? RequestId = null);

    public sealed record DoseOutcome(bool Ok, int StatusCode, string Message, Guid? Id = null);
    public sealed record DoseHistoryResult(bool Available, List<DoseRecord> Items);

    // Reads a structure's sensor readings and relay states from the API and sends relay commands. Every call goes through SmartHydroApiHttpClient
    public sealed class TelemetryApiClient
    {
        private const int TrendDays = 7;
        private const int HistoryPageSize = 30;
        private const int DosingHistoryLimit = 20;

        private readonly SmartHydroApiHttpClient _api;

        public TelemetryApiClient(SmartHydroApiHttpClient api)
        {
            _api = api;
        }

        // Loads what a dashboard needs: the structure's latest sensor reading and the state of its relays
        public async Task<StructureReadings> GetDashboardReadingsAsync(StructureViewModel structure, string accessToken, CancellationToken ct = default)
        {
            var readings = new StructureReadings();

            if (!int.TryParse(structure.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || string.IsNullOrWhiteSpace(structure.MacAddress))
                return readings;

            // The latest reading and the relay states do not depend on each other, so both are requested at once
            var rowsTask = GetAsync($"api/Telemetry/structure/{id}/latest", accessToken, new List<TelemetryDto>(), ct);
            var actuatorsTask = LoadActuatorsAsync(readings, structure.MacAddress, accessToken, ct);

            await Task.WhenAll(rowsTask, actuatorsTask);

            var targetCleanMac = CleanMac(structure.MacAddress);
            var latest = rowsTask.Result.FirstOrDefault(r => CleanMac(r.DeviceMac) == targetCleanMac)
                         ?? rowsTask.Result.FirstOrDefault();
            ApplyTelemetry(readings, latest);
            return readings;
        }

        private static string CleanMac(string? mac)
        {
            if (string.IsNullOrWhiteSpace(mac))
                return string.Empty;

            return mac.Trim().Replace(":", "").Replace("-", "").ToUpperInvariant();
        }

        private static string CanonicalMac(string? mac)
        {
            var clean = CleanMac(mac);
            if (clean.Length == 12 && clean.All(Uri.IsHexDigit))
            {
                return string.Join(":", Enumerable.Range(0, 6).Select(i => clean.Substring(i * 2, 2)));
            }

            return (mac ?? string.Empty).Trim();
        }

        // Loads what a sensor page needs: the latest reading, relay state, relay history and the seven-day trend
        public async Task<StructureReadings> GetSensorReadingsAsync(StructureViewModel structure, SensorBinding binding, string accessToken, CancellationToken ct = default)
        {
            var readings = new StructureReadings();
            var mac = CanonicalMac(structure.MacAddress);

            if (string.IsNullOrWhiteSpace(mac))
                return readings;

            var escaped = Uri.EscapeDataString(mac);

            // Every call below is independent of the others, so they are all started together and awaited once instead of one after another
            var auditTask = GetAsync($"api/Telemetry/activity/{escaped}", accessToken, new List<AuditRecord>(), ct);
            var latestTask = GetAsync<TelemetryDto?>($"api/Telemetry/latest/{escaped}", accessToken, null, ct);

            Task actuatorsTask = Task.CompletedTask;
            Task<PagedDto<ActuatorHistoryDto>>? historyTask = null;
            Task<List<DoseRecord>>? dosesTask = null;
            Task<List<DailyDto>>? dailyTask = null;

            if (binding.ControlKey != null)
            {
                actuatorsTask = LoadActuatorsAsync(readings, mac, accessToken, ct);
                historyTask = GetAsync($"api/Actuators/history/{escaped}?page=1&pageSize={HistoryPageSize}", accessToken, new PagedDto<ActuatorHistoryDto>(), ct);
            }

            if (DosingCatalog.GroupFor(binding.Metric) != null)
                dosesTask = GetDosingHistoryAsync(mac, accessToken, ct);

            if (binding.Metric != SensorMetric.None && binding.Metric != SensorMetric.Water)
                dailyTask = GetAsync($"api/Telemetry/history/{escaped}?days={TrendDays}", accessToken, new List<DailyDto>(), ct);

            var pending = new List<Task> { latestTask, actuatorsTask, auditTask };
            if (historyTask != null) pending.Add(historyTask);
            if (dosesTask != null) pending.Add(dosesTask);
            if (dailyTask != null) pending.Add(dailyTask);

            await Task.WhenAll(pending);

            readings.Audit = auditTask.Result;
            ApplyTelemetry(readings, latestTask.Result);

            if (historyTask != null)
            {
                readings.Events = historyTask.Result.Items
                    .OrderByDescending(h => h.ChangedAt)
                    .Select(h => new ActuatorEvent(h.ActuatorName, h.NewState, h.ChangedAt, h.ChangedBy))
                    .ToList();
            }

            if (dosesTask != null)
                readings.Doses = dosesTask.Result;

            if (dailyTask != null)
            {
                readings.Trend = dailyTask.Result
                    .OrderBy(d => d.SummaryDate)
                    .Select(d => new TrendPoint(d.SummaryDate, d.AvgPh, d.AvgEc, d.AvgLight, d.AvgTemperatureC, d.AvgHumidityPercent))
                    .ToList();

                if (readings.HasTelemetry)
                {
                    var sastToday = DateOnly.FromDateTime(SouthAfricaTime.ToSast(readings.RecordedAt ?? DateTime.UtcNow));
                    if (!readings.Trend.Any(t => t.Date == sastToday))
                    {
                        readings.Trend.Add(new TrendPoint(
                            sastToday,
                            readings.Ph,
                            readings.EcMs,
                            readings.LightLux,
                            readings.TemperatureC,
                            readings.HumidityPercent));
                        readings.Trend = readings.Trend.OrderBy(t => t.Date).ToList();
                    }
                }
            }

            return readings;
        }

        // Switches a control on or off for a controller board and reports the state the API ended up with
        public async Task<ControlOutcome> SendCommandAsync(string mac, string control, bool on, string accessToken, CancellationToken ct = default)
        {
            var body = new Dictionary<string, object> { ["deviceMac"] = CanonicalMac(mac) };

            foreach (var channel in ActuatorControls.ChannelsFor(control))
                body[channel] = on;

            try
            {
                using var response = await _api.SendAsync(HttpMethod.Post, "api/Actuators/command", accessToken, body, ct);

                if (response.IsSuccessStatusCode)
                {
                    var state = await SmartHydroApiHttpClient.ReadJsonAsync<ActuatorStateDto?>(response, null, ct);
                    if (state == null || !state.Pending || state.CommandId is not (> 0 and <= 65535))
                        return new ControlOutcome(false, 502, !on, "The controller command could not be confirmed.");
                    var confirmed = on;

                    if (state != null)
                        confirmed = ActuatorControls.IsOn(ToDictionary(state), control);

                    return new ControlOutcome(true, (int)response.StatusCode, confirmed, "", state?.CommandId, state?.Pending ?? false, state?.RequestId);
                }

                var problem = await SmartHydroApiHttpClient.ReadProblemAsync(response, ct);
                return new ControlOutcome(false, (int)response.StatusCode, !on, FailureMessage(response.StatusCode, problem.Message));
            }
            catch (HttpRequestException)
            {
                return new ControlOutcome(false, (int)HttpStatusCode.BadGateway, !on, FailureMessage(HttpStatusCode.BadGateway, null));
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                return new ControlOutcome(false, (int)HttpStatusCode.GatewayTimeout, !on, FailureMessage(HttpStatusCode.GatewayTimeout, null));
            }
        }

        // Loads a board's recent dosing commands, newest first, or an empty list when the API is unreachable or refuses the request
        public Task<System.Text.Json.JsonElement?> ReadCommandStatusAsync(string mac, int cid, string token, CancellationToken ct, Guid? requestId = null) =>
            GetAsync<System.Text.Json.JsonElement?>($"api/Actuators/{Uri.EscapeDataString(CanonicalMac(mac))}/commands/{cid}"
                + (requestId.HasValue ? $"?requestId={requestId}" : ""), token, null, ct);

        public Task<System.Text.Json.JsonElement?> ReadRelayStateAsync(string mac, string token, CancellationToken ct) =>
            GetAsync<System.Text.Json.JsonElement?>($"api/Actuators/{Uri.EscapeDataString(CanonicalMac(mac))}", token, null, ct);

        public async Task<List<DoseRecord>> GetDosingHistoryAsync(string mac, string accessToken, CancellationToken ct = default)
            => (await ReadDosingHistoryAsync(mac, accessToken, ct)).Items;

        public async Task<DoseHistoryResult> ReadDosingHistoryAsync(string mac, string accessToken, CancellationToken ct = default)
        {
            try
            {
                using var response = await _api.SendAsync(HttpMethod.Get,
                    $"api/Dosing/history/{Uri.EscapeDataString(CanonicalMac(mac))}?limit={DosingHistoryLimit}", accessToken, null, ct);
                if (!response.IsSuccessStatusCode) return new(false, new());
                var rows = await SmartHydroApiHttpClient.ReadJsonAsync<List<DosingDto>?>(response, null, ct);
                if (rows == null) return new(false, new());
                return new(true, rows.OrderByDescending(r => r.CreatedAt)
                    .Select(r => new DoseRecord(r.DosingType, r.TargetVolumeMl, r.Status, r.CreatedAt,
                        r.ExecutedAt, r.IssuedBy ?? "", r.Id, r.CommandId, r.DeliveryStatus, r.Reason)).ToList());
            }
            catch (HttpRequestException) { return new(false, new()); }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return new(false, new()); }
        }

        // Queues a one-shot dose for a controller board and reports whether the API accepted it
        public async Task<DoseOutcome> SendDoseAsync(string mac, string dosingType, decimal volumeMl, string accessToken, CancellationToken ct = default, bool manualPhOverride = false)
        {
            var body = new Dictionary<string, object>
            {
                ["deviceMac"] = CanonicalMac(mac),
                ["dosingType"] = dosingType,
                ["targetVolumeMl"] = volumeMl
            };
            if (manualPhOverride) body["manualPhOverride"] = true;

            try
            {
                using var response = await _api.SendAsync(HttpMethod.Post, "api/Dosing/command", accessToken, body, ct);

                if (response.IsSuccessStatusCode)
                {
                    var dose = await SmartHydroApiHttpClient.ReadJsonAsync<DosingDto?>(response, null, ct);
                    if (dose == null || dose.Id == Guid.Empty)
                        return new DoseOutcome(false, 502, "The dose request could not be confirmed. Check dose history before trying again.");
                    return new DoseOutcome(true, (int)response.StatusCode, "", dose.Id);
                }

                var problem = await SmartHydroApiHttpClient.ReadProblemAsync(response, ct);
                return new DoseOutcome(false, (int)response.StatusCode, DoseFailureMessage(response.StatusCode, problem.Message));
            }
            catch (HttpRequestException)
            {
                return new DoseOutcome(false, (int)HttpStatusCode.BadGateway, DoseFailureMessage(HttpStatusCode.BadGateway, null));
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                return new DoseOutcome(false, (int)HttpStatusCode.GatewayTimeout, DoseFailureMessage(HttpStatusCode.GatewayTimeout, null));
            }
        }

        public async Task<DoseOutcome> ReviewUnknownDoseAsync(string mac, Guid doseId, bool pumpsCheckedOff,
            bool reservoirChecked, string notes, string accessToken, CancellationToken ct)
        {
            try
            {
                using var response = await _api.SendAsync(HttpMethod.Post,
                    $"api/Dosing/{Uri.EscapeDataString(CanonicalMac(mac))}/{doseId}/review", accessToken,
                    new { pumpsCheckedOff, reservoirChecked, notes }, ct);
                if (response.IsSuccessStatusCode) return new DoseOutcome(true, 200, "");
                var problem = await SmartHydroApiHttpClient.ReadProblemAsync(response, ct);
                return new DoseOutcome(false, (int)response.StatusCode, problem.Message ?? "The earlier dose could not be reviewed.");
            }
            catch (HttpRequestException) { return new DoseOutcome(false, 503, "Recovery could not be confirmed. Refresh dose status before trying again."); }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return new DoseOutcome(false, 503, "Recovery could not be confirmed. Refresh dose status before trying again."); }
        }

        // Reads the relay states for a board and stores them on the readings
        private async Task LoadActuatorsAsync(StructureReadings readings, string mac, string accessToken, CancellationToken ct)
        {
            var state = await GetAsync<ActuatorStateDto?>($"api/Actuators/{Uri.EscapeDataString(CanonicalMac(mac))}", accessToken, null, ct);

            if (state == null)
                return;

            readings.HasActuators = true;
            readings.Actuators = ToDictionary(state);
            readings.ActuatorsUpdatedAt = state.UpdatedAt;
        }

        // Copies a telemetry row onto the readings, leaving them empty when the board has never reported
        private static void ApplyTelemetry(StructureReadings readings, TelemetryDto? row)
        {
            if (row == null)
                return;

            readings.HasTelemetry = true;
            readings.Ph = row.PhLevel;
            readings.PhDisplayFallback = row.PhDisplayFallback;
            readings.EcMs = row.EcMs;
            readings.LightLux = row.LightLux;
            readings.TemperatureC = row.TemperatureC;
            readings.HumidityPercent = row.HumidityPercent;
            readings.WaterLevelRaw = row.WaterLevelRaw;
            readings.WaterLevelDetected = row.WaterLevelDetected;
            readings.RecordedAt = row.RecordedAt;
        }

        // Turns the API's relay record into a lookup by relay name
        private static Dictionary<string, bool> ToDictionary(ActuatorStateDto state)
        {
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["growLight"] = state.GrowLight,
                ["circulationFan"] = state.CirculationFan,
                ["mainWaterPump"] = state.MainWaterPump,
                ["extractorFan"] = state.ExtractorFan,
                ["phUpPump"] = state.PhUpPump,
                ["phDownPump"] = state.PhDownPump,
                ["nutrientUpPump"] = state.NutrientUpPump,
                ["dilutionDownPump"] = state.DilutionDownPump,
                ["spraySystem"] = state.SpraySystem
            };
        }

        // Sends a GET and reads the JSON body, returning the fallback when the API is unreachable or refuses the request
        private async Task<T> GetAsync<T>(string path, string accessToken, T fallback, CancellationToken ct)
        {
            try
            {
                using var response = await _api.SendAsync(HttpMethod.Get, path, accessToken, null, ct);

                if (!response.IsSuccessStatusCode)
                    return fallback;

                return await SmartHydroApiHttpClient.ReadJsonAsync(response, fallback, ct);
            }
            catch (HttpRequestException)
            {
                return fallback;
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                return fallback;
            }
        }

        public Task<System.Text.Json.JsonElement?> GetModeStateAsync(string mac, string accessToken, CancellationToken ct = default) =>
            GetAsync<System.Text.Json.JsonElement?>($"api/Telemetry/mode-state/{Uri.EscapeDataString(CanonicalMac(mac))}", accessToken, null, ct);

        // Requests the edge controller board to transition into primary LoRa 868.1 MHz + BLE mode
        public async Task<bool> RequestLoraModeAsync(string mac, string accessToken, CancellationToken ct = default)
        {
            try
            {
                using var response = await _api.SendAsync(HttpMethod.Post, $"api/Devices/{Uri.EscapeDataString(CanonicalMac(mac))}/lora-mode", accessToken, null, ct);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        // Requests the edge controller board to transition into online cloud mode
        public async Task<bool> RequestOnlineModeAsync(string mac, string accessToken, CancellationToken ct = default)
        {
            try
            {
                using var response = await _api.SendAsync(HttpMethod.Post, $"api/Devices/{Uri.EscapeDataString(CanonicalMac(mac))}/online-mode", accessToken, null, ct);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        // Requests the edge controller board to transition into local farm SoftAP offline mode
        public async Task<bool> RequestOfflineModeAsync(string mac, string accessToken, CancellationToken ct = default)
        {
            try
            {
                using var response = await _api.SendAsync(HttpMethod.Post, $"api/Devices/{Uri.EscapeDataString(CanonicalMac(mac))}/offline-mode", accessToken, null, ct);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        // Chooses the message shown beside a control when the API did not accept a command
        private static string FailureMessage(HttpStatusCode status, string? apiMessage)
        {
            if (status == HttpStatusCode.Forbidden)
                return "You do not have permission to change controls.";

            if (status == HttpStatusCode.NotFound)
                return "This structure's controller could not be found.";

            if (status == HttpStatusCode.BadRequest && !string.IsNullOrWhiteSpace(apiMessage))
                return apiMessage;

            return "The controller could not be reached. Please try again.";
        }

        // Chooses the message shown beside the dose buttons when the API did not accept a dose
        private static string DoseFailureMessage(HttpStatusCode status, string? apiMessage)
        {
            if (status == HttpStatusCode.Forbidden)
                return "You do not have permission to dose.";

            if (status == HttpStatusCode.NotFound)
                return "This structure's controller could not be found.";

            if (status == HttpStatusCode.BadRequest && !string.IsNullOrWhiteSpace(apiMessage))
                return apiMessage;

            return "The dose could not be sent. Please try again.";
        }

        private sealed class DosingDto
        {
            public Guid Id { get; set; }
            public int? CommandId { get; set; }
            public string? DeliveryStatus { get; set; }
            public string? Reason { get; set; }
            public string DosingType { get; set; } = "";
            public decimal TargetVolumeMl { get; set; }
            public string Status { get; set; } = "";
            public DateTime CreatedAt { get; set; }
            public DateTime? ExecutedAt { get; set; }
            public string? IssuedBy { get; set; }
        }

        private sealed class TelemetryDto
        {
            public string DeviceMac { get; set; } = "";
            public decimal PhLevel { get; set; }
            public PhDisplayFallback? PhDisplayFallback { get; set; }
            public decimal EcMs { get; set; }
            public decimal LightLux { get; set; }
            public int WaterLevelRaw { get; set; }
            public bool WaterLevelDetected { get; set; }
            public decimal TemperatureC { get; set; }
            public decimal HumidityPercent { get; set; }
            public DateTime RecordedAt { get; set; }
        }

        private sealed class DailyDto
        {
            public DateOnly SummaryDate { get; set; }
            public decimal AvgPh { get; set; }
            public decimal AvgEc { get; set; }
            public decimal AvgLight { get; set; }
            public decimal AvgTemperatureC { get; set; }
            public decimal AvgHumidityPercent { get; set; }
        }

        private sealed class ActuatorStateDto
        {
            public Guid? RequestId { get; set; }
            public int? CommandId { get; set; }
            public bool Pending { get; set; }
            public bool GrowLight { get; set; }
            public bool CirculationFan { get; set; }
            public bool MainWaterPump { get; set; }
            public bool ExtractorFan { get; set; }
            public bool PhUpPump { get; set; }
            public bool PhDownPump { get; set; }
            public bool NutrientUpPump { get; set; }
            public bool DilutionDownPump { get; set; }
            public bool SpraySystem { get; set; }
            public DateTime UpdatedAt { get; set; }
        }

        private sealed class ActuatorHistoryDto
        {
            public string ActuatorName { get; set; } = "";
            public bool NewState { get; set; }
            public DateTime ChangedAt { get; set; }
            public string ChangedBy { get; set; } = "";
        }

        private sealed class PagedDto<T>
        {
            public List<T> Items { get; set; } = new();
        }
    }
}
