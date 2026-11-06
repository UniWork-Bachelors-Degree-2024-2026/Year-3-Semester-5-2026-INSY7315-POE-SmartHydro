using Group_1_SmartHydro_MVC.Patterns.Singleton;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Group_1_SmartHydro_MVC.Services
{
    public sealed class AdminApiClient
    {
        private readonly HttpClient _http;

        public AdminApiClient(SmartHydroApiHttpClient api)
        {
            _http = api.Client;
        }

        // Fetches the farm operators awaiting approval, or null when the list could not be loaded
        public async Task<List<PendingOperator>?> GetPendingOperatorsAsync(string accessToken, CancellationToken ct = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/admin/pending-operators");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var operators = await response.Content.ReadFromJsonAsync<List<PendingOperator>>(cancellationToken: ct);

            if (operators is null)
                return new List<PendingOperator>();

            return operators;
        }

        // Lists every account in the system for the admin dashboard, or null when it could not be loaded
        public async Task<List<AdminUser>?> GetUsersAsync(string accessToken, CancellationToken ct = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/admin/users");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
                return null;

            return await SmartHydroApiHttpClient.ReadJsonAsync(response, new List<AdminUser>(), ct);
        }

        // Lists every active site with its owning operator, or null when it could not be loaded
        public async Task<List<AdminSite>?> GetSitesAsync(string accessToken, CancellationToken ct = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/admin/sites");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
                return null;

            return await SmartHydroApiHttpClient.ReadJsonAsync(response, new List<AdminSite>(), ct);
        }

        // Lists the workers and open invitations on every active site, or null when they could not be loaded
        public async Task<List<TeamMember>?> GetWorkersAsync(string accessToken, CancellationToken ct = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/admin/workers");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
                return null;

            return await SmartHydroApiHttpClient.ReadJsonAsync(response, new List<TeamMember>(), ct);
        }

        // Lists the deleted sites an admin can restore, or null when they could not be loaded
        public async Task<List<AdminDeletedSite>?> GetDeletedSitesAsync(string accessToken, CancellationToken ct = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/Sites/deleted");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
                return null;

            return await SmartHydroApiHttpClient.ReadJsonAsync(response, new List<AdminDeletedSite>(), ct);
        }

        // Deletes a site, moving its structures to deleted items and cancelling its open invitations
        public Task<OperatorReviewOutcome> DeleteSiteAsync(Guid siteId, string accessToken, CancellationToken ct = default)
        {
            return ReviewAsync($"api/Sites/{siteId}?force=true", accessToken, ct, HttpMethod.Delete);
        }

        // Restores a deleted site together with the structures that were deleted along with it
        public Task<OperatorReviewOutcome> RestoreSiteAsync(Guid siteId, string accessToken, CancellationToken ct = default)
        {
            return ReviewAsync($"api/Sites/{siteId}/restore", accessToken, ct);
        }

        // Permanently removes a deleted site together with its structures; this cannot be undone
        public Task<OperatorReviewOutcome> PurgeSiteAsync(Guid siteId, string accessToken, CancellationToken ct = default)
        {
            return ReviewAsync($"api/Sites/{siteId}/purge", accessToken, ct, HttpMethod.Delete);
        }

        // Takes a worker off a site; they keep their account and any other sites they belong to
        public Task<OperatorReviewOutcome> RemoveWorkerAsync(Guid siteId, int userId, string accessToken, CancellationToken ct = default)
        {
            return ReviewAsync($"api/admin/sites/{siteId}/workers/{userId}", accessToken, ct, HttpMethod.Delete);
        }

        // Cancels an invitation that has not been used yet
        public Task<OperatorReviewOutcome> CancelInvitationAsync(Guid siteId, string email, string accessToken, CancellationToken ct = default)
        {
            return ReviewAsync($"api/admin/sites/{siteId}/invitations?email={Uri.EscapeDataString(email)}", accessToken, ct, HttpMethod.Delete);
        }

        // Gives a site to an approved operator, moving its structures to them as well
        public async Task<OperatorReviewOutcome> AssignSiteOwnerAsync(Guid siteId, string ownerEmail, string accessToken, CancellationToken ct = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, $"api/admin/sites/{siteId}/owner")
            {
                Content = JsonContent.Create(new { ownerEmail })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _http.SendAsync(request, ct);

            var body = await ReadMessageAsync(response, ct);

            if (response.IsSuccessStatusCode)
                return new OperatorReviewOutcome(true, body);

            if (string.IsNullOrWhiteSpace(body))
                body = "The site owner could not be changed. Please try again.";

            return new OperatorReviewOutcome(false, body);
        }

        // Saves an account's name, starting site name and role
        public async Task<OperatorReviewOutcome> UpdateUserAsync(int id, AdminUserUpdate update, string accessToken, CancellationToken ct = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, $"api/admin/users/{id}")
            {
                Content = JsonContent.Create(update)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _http.SendAsync(request, ct);

            var body = await ReadMessageAsync(response, ct);

            if (response.IsSuccessStatusCode)
                return new OperatorReviewOutcome(true, body);

            if (string.IsNullOrWhiteSpace(body))
                body = "The account could not be saved. Please try again.";

            return new OperatorReviewOutcome(false, body);
        }

        // Moves a denied account back to pending approval
        public Task<OperatorReviewOutcome> ReopenUserAsync(int id, string accessToken, CancellationToken ct = default)
        {
            return ReviewAsync($"api/admin/users/{id}/reopen", accessToken, ct);
        }

        // Approves a pending or denied farm operator by id
        public Task<OperatorReviewOutcome> ApproveOperatorAsync(int id, string accessToken, CancellationToken ct = default)
        {
            return ReviewAsync($"api/admin/users/{id}/approve", accessToken, ct);
        }

        // Denies a pending farm operator by id
        public Task<OperatorReviewOutcome> DenyOperatorAsync(int id, string accessToken, CancellationToken ct = default)
        {
            return ReviewAsync($"api/admin/users/{id}/deny", accessToken, ct);
        }

        // Disables an approved account so the user is signed out and cannot sign in
        public Task<OperatorReviewOutcome> DeactivateUserAsync(int id, string accessToken, CancellationToken ct = default)
        {
            return ReviewAsync($"api/admin/users/{id}/deactivate", accessToken, ct);
        }

        // Re-enables a disabled account
        public Task<OperatorReviewOutcome> ReactivateUserAsync(int id, string accessToken, CancellationToken ct = default)
        {
            return ReviewAsync($"api/admin/users/{id}/reactivate", accessToken, ct);
        }

        // Deletes an account, moving the sites and structures it owned to deleted items
        public Task<OperatorReviewOutcome> DeleteUserAsync(int id, string accessToken, CancellationToken ct = default)
        {
            return ReviewAsync($"api/admin/users/{id}", accessToken, ct, HttpMethod.Delete);
        }

        // Sends an account decision (POST by default) and reads back the API's message
        private async Task<OperatorReviewOutcome> ReviewAsync(string path, string accessToken, CancellationToken ct, HttpMethod? method = null)
        {
            using var request = new HttpRequestMessage(method ?? HttpMethod.Post, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _http.SendAsync(request, ct);

            var body = await ReadMessageAsync(response, ct);

            if (response.IsSuccessStatusCode)
                return new OperatorReviewOutcome(true, body);

            if (string.IsNullOrWhiteSpace(body))
                body = "The request could not be completed. Please try again.";

            return new OperatorReviewOutcome(false, body);
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

    public sealed record OperatorReviewOutcome(bool Succeeded, string Message);

    public sealed class ApiMessage
    {
        public string? Message { get; set; }
    }

    public sealed class PendingOperator
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? FullName { get; set; }
        public string FarmName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public sealed class AdminUser
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? FullName { get; set; }
        public string? FarmName { get; set; }
        public string Role { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool IsVerified { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public int OwnedSiteCount { get; set; }
        public List<string> AssignedSites { get; set; } = new();
        public bool IsProtected { get; set; }
        public bool IsCurrentUser { get; set; }
        public List<string> AllowedActions { get; set; } = new();
        public List<AdminStatusOption> StatusOptions { get; set; } = new();
    }

    public sealed class AdminStatusOption
    {
        public string Status { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
    }

    public sealed record AdminUserUpdate(string? FullName, string? FarmName, string Role);

    public sealed class AdminDeletedSite
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? OwnerEmail { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
        public string? DeletedByEmail { get; set; }
        public int StructureCount { get; set; }
    }

    public sealed class AdminSite
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? OwnerEmail { get; set; }
        public DateTime CreatedAt { get; set; }
        public int StructureCount { get; set; }
        public int WorkerCount { get; set; }
    }
}