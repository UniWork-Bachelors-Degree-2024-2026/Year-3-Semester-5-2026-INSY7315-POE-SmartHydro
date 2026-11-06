using Group_1_SmartHydro_MVC.Patterns.Singleton;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Group_1_SmartHydro_MVC.Services
{
    public sealed class SiteInvitationApiClient
    {
        private readonly HttpClient _http;

        public SiteInvitationApiClient(SmartHydroApiHttpClient api)
        {
            _http = api.Client;
        }

        // Lists the sites the signed-in Operator owns, or null when they could not be loaded
        public async Task<List<OperatorSite>?> GetMySitesAsync(string accessToken, CancellationToken ct = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/Sites");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var sites = await response.Content.ReadFromJsonAsync<List<OperatorSite>>(cancellationToken: ct);

            if (sites is null)
                return new List<OperatorSite>();

            return sites;
        }

        // Lists the workers on the Operator's sites and the invitations they have sent, or null when they could not be loaded
        public async Task<List<TeamMember>?> GetTeamAsync(string accessToken, CancellationToken ct = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/team");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
                return null;

            return await SmartHydroApiHttpClient.ReadJsonAsync(response, new List<TeamMember>(), ct);
        }

        // Takes a worker off one of the Operator's sites
        public Task<TeamActionOutcome> RemoveMemberAsync(Guid siteId, int userId, string accessToken, CancellationToken ct = default)
        {
            return SendTeamActionAsync($"api/team/sites/{siteId}/members/{userId}", accessToken, ct);
        }

        // Cancels an invitation that has not been used yet
        public Task<TeamActionOutcome> CancelInviteAsync(Guid siteId, string email, string accessToken, CancellationToken ct = default)
        {
            return SendTeamActionAsync($"api/team/sites/{siteId}/invitations?email={Uri.EscapeDataString(email)}", accessToken, ct);
        }

        // Sends a team DELETE request and reads back the API's message
        private async Task<TeamActionOutcome> SendTeamActionAsync(string path, string accessToken, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _http.SendAsync(request, ct);

            var message = await ReadMessageAsync(response, ct);

            if (response.IsSuccessStatusCode)
                return new TeamActionOutcome(true, message);

            if (string.IsNullOrWhiteSpace(message))
                message = "That change could not be made. Please try again.";

            return new TeamActionOutcome(false, message);
        }

        // Sends POST /api/sites/{siteId}/invitations and maps the API's rate limit, ownership and delivery outcomes
        public async Task<SiteInviteOutcome> InviteWorkerAsync(Guid siteId, string email, string accessToken, CancellationToken ct = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"api/sites/{siteId}/invitations")
            {
                Content = JsonContent.Create(new { email })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _http.SendAsync(request, ct);
            var message = await ReadMessageAsync(response, ct);

            if (response.IsSuccessStatusCode)
                return new SiteInviteOutcome(SiteInviteResult.Sent, message);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return new SiteInviteOutcome(SiteInviteResult.RateLimited, message);

            if (response.StatusCode == HttpStatusCode.Forbidden)
                return new SiteInviteOutcome(SiteInviteResult.Forbidden, message);

            if (response.StatusCode == HttpStatusCode.BadRequest)
                return new SiteInviteOutcome(SiteInviteResult.InvalidInput, message);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return new SiteInviteOutcome(SiteInviteResult.SessionExpired, message);

            return new SiteInviteOutcome(SiteInviteResult.Failed, message);
        }

        // Reads the message field from an API response body, returning an empty string when there is none
        private static async Task<string> ReadMessageAsync(HttpResponseMessage response, CancellationToken ct)
        {
            try
            {
                var payload = await response.Content.ReadFromJsonAsync<ApiMessage>(cancellationToken: ct);

                if (payload is null || string.IsNullOrWhiteSpace(payload.Message))
                    return string.Empty;

                return payload.Message;
            }
            catch (System.Text.Json.JsonException)
            {
                return string.Empty;
            }
            catch (NotSupportedException)
            {
                return string.Empty;
            }
        }
    }

    public enum SiteInviteResult
    {
        Sent,
        RateLimited,
        Forbidden,
        InvalidInput,
        SessionExpired,
        Failed
    }

    public sealed record SiteInviteOutcome(SiteInviteResult Result, string Message);

    public sealed record TeamActionOutcome(bool Succeeded, string Message);

    public sealed class TeamMember
    {
        public Guid SiteId { get; set; }
        public int? UserId { get; set; }
        public string SiteName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? FullName { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime Since { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }

    public sealed class OperatorSite
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int StructureCount { get; set; }
    }
}