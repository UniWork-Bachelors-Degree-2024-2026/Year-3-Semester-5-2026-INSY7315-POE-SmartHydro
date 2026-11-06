using System.Globalization;
using System.Net;
using Group_1_SmartHydro_MVC.Models;
using Group_1_SmartHydro_MVC.Patterns.Singleton;

namespace Group_1_SmartHydro_MVC.Services
{
    public enum StructureSaveResult
    {
        Saved,
        Conflict,
        Invalid,
        NotFound,
        Failed
    }

    public sealed record StructureSaveOutcome(StructureSaveResult Result, string Field, string Message, string? StructureId);

    // CameraMac is empty when the structure has no camera
    public sealed record StructureInput(string SiteId, string Name, string MacAddress, StructureType Type, string LabelColour, string CameraMac = "");

    public sealed class StructureApiClient
    {
        private const string FodderApiType = "FODDER_TRAY";
        private const string HydroponicApiType = "VERTICAL_TOWER";

        private static readonly TimeSpan RecentlySeen = TimeSpan.FromMinutes(30);

        private readonly SmartHydroApiHttpClient _api;

        public StructureApiClient(SmartHydroApiHttpClient api)
        {
            _api = api;
        }

        // Lists the active structures in a site, or null when the API could not be reached
        public async Task<List<StructureViewModel>?> GetStructuresAsync(string siteId, string accessToken, CancellationToken ct = default)
        {
            return await GetListAsync($"api/Structures?siteId={Uri.EscapeDataString(siteId)}", accessToken, ct);
        }

        // Lists the deleted structures in a site that can still be restored or purged
        public async Task<List<StructureViewModel>?> GetDeletedStructuresAsync(string siteId, string accessToken, CancellationToken ct = default)
        {
            return await GetListAsync($"api/Structures/deleted?siteId={Uri.EscapeDataString(siteId)}", accessToken, ct);
        }

        // Finds one active structure the signed-in user can see, or null when it is missing or not theirs
        public async Task<StructureViewModel?> GetStructureAsync(string? structureId, string accessToken, CancellationToken ct = default)
        {
            if (!int.TryParse(structureId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                return null;

            using var response = await _api.SendAsync(HttpMethod.Get, $"api/Structures/{id}", accessToken, null, ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var dto = await SmartHydroApiHttpClient.ReadJsonAsync<StructureDto?>(response, null, ct);

            if (dto == null)
                return null;

            return ToViewModel(dto);
        }

        // Creates a structure in the given site and links its controller board and its camera when one was given
        public async Task<StructureSaveOutcome> CreateAsync(StructureInput input, string accessToken, CancellationToken ct = default)
        {
            string? cameraMac = null;
            if (!string.IsNullOrWhiteSpace(input.CameraMac))
                cameraMac = input.CameraMac;

            var body = new
            {
                name = input.Name,
                structureType = ToApiType(input.Type),
                labelColour = StructureLabelColours.ToApi(input.LabelColour),
                siteId = input.SiteId,
                controllerMac = input.MacAddress,
                cameraMac
            };

            using var response = await _api.SendAsync(HttpMethod.Post, "api/Structures", accessToken, body, ct);

            if (response.IsSuccessStatusCode)
            {
                var created = await SmartHydroApiHttpClient.ReadJsonAsync<StructureDto?>(response, null, ct);

                string? createdId = null;
                if (created != null)
                    createdId = created.Id.ToString(CultureInfo.InvariantCulture);

                return new StructureSaveOutcome(StructureSaveResult.Saved, "", "", createdId);
            }

            return await ReadFailureAsync(response, ct);
        }

        // Updates a structure's name, type, colour, controller board and camera. An empty camera MAC unlinks the camera
        public async Task<StructureSaveOutcome> UpdateAsync(string structureId, StructureInput input, string accessToken, CancellationToken ct = default)
        {
            var body = new
            {
                name = input.Name,
                structureType = ToApiType(input.Type),
                labelColour = StructureLabelColours.ToApi(input.LabelColour),
                controllerMac = input.MacAddress,
                cameraMac = input.CameraMac ?? ""
            };

            using var response = await _api.SendAsync(HttpMethod.Put, $"api/Structures/{Uri.EscapeDataString(structureId)}", accessToken, body, ct);

            if (response.IsSuccessStatusCode)
                return new StructureSaveOutcome(StructureSaveResult.Saved, "", "", structureId);

            return await ReadFailureAsync(response, ct);
        }

        // Soft-deletes a structure so it can still be restored
        public Task<bool> DeleteAsync(string structureId, string accessToken, CancellationToken ct = default)
        {
            return SendCommandAsync(HttpMethod.Delete, $"api/Structures/{Uri.EscapeDataString(structureId)}", accessToken, ct);
        }

        // Restores a soft-deleted structure, reporting the API's reason when it refuses (for example the name is now taken or its site is deleted)
        public async Task<StructureSaveOutcome> RestoreAsync(string structureId, string accessToken, CancellationToken ct = default)
        {
            using var response = await _api.SendAsync(HttpMethod.Post, $"api/Structures/{Uri.EscapeDataString(structureId)}/restore", accessToken, null, ct);

            if (response.IsSuccessStatusCode)
                return new StructureSaveOutcome(StructureSaveResult.Saved, "", "", structureId);

            return await ReadFailureAsync(response, ct);
        }

        // Permanently removes a soft-deleted structure (Admin only)
        public Task<bool> PurgeAsync(string structureId, string accessToken, CancellationToken ct = default)
        {
            return SendCommandAsync(HttpMethod.Delete, $"api/Structures/{Uri.EscapeDataString(structureId)}/purge", accessToken, ct);
        }

        // Fetches a list of structures from the given API path and maps each one to a view model
        private async Task<List<StructureViewModel>?> GetListAsync(string path, string accessToken, CancellationToken ct)
        {
            using var response = await _api.SendAsync(HttpMethod.Get, path, accessToken, null, ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var structures = await SmartHydroApiHttpClient.ReadJsonAsync(response, new List<StructureDto>(), ct);
            return structures.Select(ToViewModel).ToList();
        }

        // Sends a body-less command and reports whether the API accepted it
        private async Task<bool> SendCommandAsync(HttpMethod method, string path, string accessToken, CancellationToken ct)
        {
            using var response = await _api.SendAsync(method, path, accessToken, null, ct);
            return response.IsSuccessStatusCode;
        }

        // Turns a failed create or update into an outcome naming the form field the error belongs to
        private static async Task<StructureSaveOutcome> ReadFailureAsync(HttpResponseMessage response, CancellationToken ct)
        {
            var problem = await SmartHydroApiHttpClient.ReadProblemAsync(response, ct);
            var message = problem.Message ?? "";
            var field = problem.Field ?? "";

            if (response.StatusCode == HttpStatusCode.Conflict)
                return new StructureSaveOutcome(StructureSaveResult.Conflict, field, message, null);

            if (response.StatusCode == HttpStatusCode.BadRequest)
                return new StructureSaveOutcome(StructureSaveResult.Invalid, field, message, null);

            if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Forbidden)
                return new StructureSaveOutcome(StructureSaveResult.NotFound, field, message, null);

            return new StructureSaveOutcome(StructureSaveResult.Failed, field, message, null);
        }

        // Converts the MVC structure type into the API's structure type code
        private static string ToApiType(StructureType type)
        {
            if (type == StructureType.Fodder)
                return FodderApiType;

            return HydroponicApiType;
        }

        // Maps the API's structure record onto the view model, choosing the icon and status line
        private static StructureViewModel ToViewModel(StructureDto dto)
        {
            var model = new StructureViewModel
            {
                Id = dto.Id.ToString(CultureInfo.InvariantCulture),
                SiteId = dto.SiteId?.ToString() ?? "",
                Name = dto.Name,
                Type = StructureType.Hydroponic,
                IconName = "eco",
                IconIsMaterialSymbol = true,
                IsOnline = dto.IsOnline,
                LastSeenAt = dto.LastSeenAt,
                LabelColour = StructureLabelColours.FromApi(dto.LabelColour),
                MacAddress = dto.ControllerMac ?? "",
                CameraMac = dto.CameraMac ?? "",
                CreatedAt = dto.CreatedAt
            };

            if (string.Equals(dto.StructureType, FodderApiType, StringComparison.OrdinalIgnoreCase))
            {
                model.Type = StructureType.Fodder;
                model.IconName = "fa-solid fa-seedling";
                model.IconIsMaterialSymbol = false;
            }

            if (dto.Temperature.HasValue)
                model.Temperature = $"{dto.Temperature.Value:0.0} \u00B0C";

            if (dto.Humidity.HasValue)
                model.Humidity = $"{dto.Humidity.Value:0.0}%";

            if (dto.Ec.HasValue)
            {
                var ecMicroSiemens = (int)Math.Round(dto.Ec.Value * 1000m);
                model.Ec = $"{ecMicroSiemens} \u00B5S/cm";
            }

            if (dto.Ph.HasValue)
                model.Ph = $"{dto.Ph.Value:0.00} pH";

            ApplyStatus(model);
            return model;
        }

        // Fills in the status line from when the structure's boards last reported in
        private static void ApplyStatus(StructureViewModel model)
        {
            if (model.IsOnline)
            {
                model.StatusText = "Online";
                model.StatusLevel = "good";
                model.UpdatedAt = "Just now";
                return;
            }

            if (!model.LastSeenAt.HasValue || model.LastSeenAt.Value <= DateTime.UnixEpoch.AddDays(1))
            {
                model.StatusText = "No data yet";
                model.StatusLevel = "none";
                model.StatusNote = "Waiting for the controller to report in.";
                return;
            }

            var lastSeen = SouthAfricaTime.ToSast(model.LastSeenAt.Value);
            model.UpdatedAt = lastSeen.ToString("d MMM, HH:mm", CultureInfo.InvariantCulture);
            model.StatusText = "Not reporting";
            model.StatusLevel = "alert";

            if (DateTime.UtcNow - (model.LastSeenAt.Value.Kind == DateTimeKind.Utc ? model.LastSeenAt.Value : model.LastSeenAt.Value.ToUniversalTime()) < RecentlySeen)
                model.StatusLevel = "caution";

            model.StatusNote = "Last seen " + model.UpdatedAt;
        }

        private sealed class StructureDto
        {
            public int Id { get; set; }
            public Guid? SiteId { get; set; }
            public string Name { get; set; } = "";
            public string StructureType { get; set; } = "";
            public string LabelColour { get; set; } = "";
            public DateTime CreatedAt { get; set; }
            public bool IsOnline { get; set; }
            public DateTime? LastSeenAt { get; set; }
            public string? CameraMac { get; set; }
            public string? ControllerMac { get; set; }
            public decimal? Temperature { get; set; }
            public decimal? Humidity { get; set; }
            public decimal? Ec { get; set; }
            public decimal? Ph { get; set; }
        }
    }
}