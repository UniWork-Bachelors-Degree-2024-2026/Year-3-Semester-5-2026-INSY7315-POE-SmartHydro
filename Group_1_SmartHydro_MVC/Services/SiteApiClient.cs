using System.Net;
using Group_1_SmartHydro_MVC.Models;
using Group_1_SmartHydro_MVC.Patterns.Singleton;

namespace Group_1_SmartHydro_MVC.Services
{
    public enum SiteSaveResult
    {
        Saved,
        DuplicateName,
        Invalid,
        NotFound,
        Failed
    }

    public sealed record SiteSaveOutcome(SiteSaveResult Result, SiteViewModel? Site, string Message);

    public enum SiteDeleteResult
    {
        Deleted,
        HasStructures,
        NotFound,
        Failed
    }

    public sealed record SiteDeleteOutcome(SiteDeleteResult Result, string Message, int StructureCount);

    public sealed class SiteApiClient
    {
        private readonly SmartHydroApiHttpClient _api;

        public SiteApiClient(SmartHydroApiHttpClient api)
        {
            _api = api;
        }

        // Lists the sites the signed-in user can see, or null when the API could not be reached
        public async Task<List<SiteViewModel>?> GetSitesAsync(string accessToken, CancellationToken ct = default)
        {
            using var response = await _api.SendAsync(HttpMethod.Get, "api/Sites", accessToken, null, ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var sites = await SmartHydroApiHttpClient.ReadJsonAsync(response, new List<SiteDto>(), ct);
            return sites.Select(ToViewModel).ToList();
        }

        // Finds one site the signed-in user can see, or null when it is missing or not theirs
        public async Task<SiteViewModel?> GetSiteAsync(string siteId, string accessToken, CancellationToken ct = default)
        {
            if (!Guid.TryParse(siteId, out var id))
                return null;

            using var response = await _api.SendAsync(HttpMethod.Get, $"api/Sites/{id}", accessToken, null, ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var site = await SmartHydroApiHttpClient.ReadJsonAsync<SiteDto?>(response, null, ct);

            if (site == null)
                return null;

            return ToViewModel(site);
        }

        // Creates a site; an Admin may pass an operator's email to give the site to that operator
        public async Task<SiteSaveOutcome> CreateSiteAsync(string name, string? ownerEmail, string accessToken, CancellationToken ct = default)
        {
            var body = new { name, ownerEmail };

            using var response = await _api.SendAsync(HttpMethod.Post, "api/Sites", accessToken, body, ct);
            return await ReadSaveOutcomeAsync(response, ct);
        }

        // Renames a site
        public async Task<SiteSaveOutcome> UpdateSiteAsync(string siteId, string name, string accessToken, CancellationToken ct = default)
        {
            if (!Guid.TryParse(siteId, out var id))
                return new SiteSaveOutcome(SiteSaveResult.NotFound, null, "That site could not be found.");

            var body = new { name };

            using var response = await _api.SendAsync(HttpMethod.Put, $"api/Sites/{id}", accessToken, body, ct);
            return await ReadSaveOutcomeAsync(response, ct);
        }

        // Deletes a site; without force the API refuses with a 409 while the site has active structures, and this reads that message and count back
        public async Task<SiteDeleteOutcome> DeleteSiteAsync(string siteId, bool force, string accessToken, CancellationToken ct = default)
        {
            if (!Guid.TryParse(siteId, out var id))
                return new SiteDeleteOutcome(SiteDeleteResult.NotFound, "That site could not be found. It may have been removed.", 0);

            var path = $"api/Sites/{id}";

            if (force)
                path += "?force=true";

            using var response = await _api.SendAsync(HttpMethod.Delete, path, accessToken, null, ct);

            if (response.IsSuccessStatusCode)
            {
                var deleted = await SmartHydroApiHttpClient.ReadJsonAsync(response, new DeleteReply(), ct);
                return new SiteDeleteOutcome(SiteDeleteResult.Deleted, deleted.Message ?? "", deleted.StructureCount);
            }

            var problem = await SmartHydroApiHttpClient.ReadJsonAsync(response, new DeleteReply(), ct);
            var message = problem.Message ?? "";

            if (response.StatusCode == HttpStatusCode.Conflict)
                return new SiteDeleteOutcome(SiteDeleteResult.HasStructures, message, problem.StructureCount);

            if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Forbidden)
                return new SiteDeleteOutcome(SiteDeleteResult.NotFound, message, 0);

            return new SiteDeleteOutcome(SiteDeleteResult.Failed, message, 0);
        }

        // Turns the API's reply to a create or update into an outcome the site pop-up can show
        private static async Task<SiteSaveOutcome> ReadSaveOutcomeAsync(HttpResponseMessage response, CancellationToken ct)
        {
            if (response.IsSuccessStatusCode)
            {
                var saved = await SmartHydroApiHttpClient.ReadJsonAsync<SiteDto?>(response, null, ct);

                SiteViewModel? site = null;
                if (saved != null)
                    site = ToViewModel(saved);

                return new SiteSaveOutcome(SiteSaveResult.Saved, site, "");
            }

            var problem = await SmartHydroApiHttpClient.ReadProblemAsync(response, ct);
            var message = problem.Message ?? "";

            if (response.StatusCode == HttpStatusCode.Conflict)
                return new SiteSaveOutcome(SiteSaveResult.DuplicateName, null, message);

            if (response.StatusCode == HttpStatusCode.BadRequest)
                return new SiteSaveOutcome(SiteSaveResult.Invalid, null, message);

            if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Forbidden)
                return new SiteSaveOutcome(SiteSaveResult.NotFound, null, message);

            return new SiteSaveOutcome(SiteSaveResult.Failed, null, message);
        }

        // Maps the API's site record onto the view model used by the pages
        private static SiteViewModel ToViewModel(SiteDto dto)
        {
            return new SiteViewModel
            {
                Id = dto.Id.ToString(),
                Name = dto.Name,
                OwnerEmail = dto.OwnerEmail,
                CreatedAt = dto.CreatedAt,
                StructureCount = dto.StructureCount
            };
        }

        private sealed class DeleteReply
        {
            public string? Message { get; set; }
            public int StructureCount { get; set; }
        }

        private sealed class SiteDto
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = "";
            public DateTime CreatedAt { get; set; }
            public int StructureCount { get; set; }
            public string? OwnerEmail { get; set; }
        }
    }
}