using System.Security.Claims;
using Group_1_SmartHydro_MVC.Models;

namespace Group_1_SmartHydro_MVC.Services
{
    public static class SiteCookies
    {
        public const string SiteCookieName = "sh_site";
        public const string StructureCookieName = "sh_structure";

        private const char Separator = '|';

        // Reads the id of the site the browser last chose, or null when none has been chosen
        public static string? ReadSiteId(HttpContext? httpContext)
        {
            if (httpContext == null)
                return null;

            var value = httpContext.Request.Cookies[SiteCookieName];

            if (string.IsNullOrWhiteSpace(value) || !Guid.TryParse(value, out var siteId))
                return null;

            return siteId.ToString();
        }

        // Remembers the chosen site on the browser for a year
        public static void WriteSiteId(HttpContext httpContext, string siteId)
        {
            httpContext.Response.Cookies.Append(SiteCookieName, siteId, BuildOptions(httpContext));
        }

        // Forgets the chosen site, for example after it was deleted, so the site picker opens
        public static void ClearSiteId(HttpContext httpContext)
        {
            httpContext.Response.Cookies.Delete(SiteCookieName);
        }

        // Reads the structure last opened in the given site, or null when it was opened in another site
        public static string? ReadStructureId(HttpContext? httpContext, string? siteId)
        {
            if (httpContext == null || string.IsNullOrEmpty(siteId))
                return null;

            var value = httpContext.Request.Cookies[StructureCookieName];

            if (string.IsNullOrWhiteSpace(value))
                return null;

            var parts = value.Split(Separator);

            if (parts.Length != 2 || !string.Equals(parts[0], siteId, StringComparison.OrdinalIgnoreCase))
                return null;

            return parts[1];
        }

        // Remembers the structure the user just opened, tied to the site it belongs to
        public static void WriteStructureId(HttpContext httpContext, string siteId, string structureId)
        {
            httpContext.Response.Cookies.Append(StructureCookieName, siteId + Separator + structureId, BuildOptions(httpContext));
        }

        // Forgets the last opened structure, for example after it was deleted
        public static void ClearStructureId(HttpContext httpContext)
        {
            httpContext.Response.Cookies.Delete(StructureCookieName);
        }

        // Builds cookie settings that keep the values away from scripts and other sites
        private static CookieOptions BuildOptions(HttpContext httpContext)
        {
            return new CookieOptions
            {
                HttpOnly = true,
                Secure = httpContext.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                Expires = DateTimeOffset.UtcNow.AddYears(1)
            };
        }
    }

    public interface ISiteContext
    {
        bool SitesLoadFailed { get; }

        Task<IReadOnlyList<SiteViewModel>> GetSitesAsync();

        Task<SiteViewModel?> GetCurrentAsync();

        Task<string> GetDisplayNameAsync();

        void Select(string siteId);

        string? LastViewedStructureId(string? siteId);

        void RememberStructure(string siteId, string structureId);

        void ForgetStructure(string structureId);
    }

    public class SiteContext : ISiteContext
    {
        private const string FallbackName = "Smart Hydro";

        private readonly SiteApiClient _siteApi;
        private readonly IHttpContextAccessor _httpContextAccessor;

        private IReadOnlyList<SiteViewModel>? _sites;
        private string? _selectedOverride;

        public SiteContext(SiteApiClient siteApi, IHttpContextAccessor httpContextAccessor)
        {
            _siteApi = siteApi;
            _httpContextAccessor = httpContextAccessor;
        }

        public bool SitesLoadFailed { get; private set; }

        // Loads the sites the signed-in user can see once per request, returning an empty list when the API is unavailable
        public async Task<IReadOnlyList<SiteViewModel>> GetSitesAsync()
        {
            if (_sites != null)
                return _sites;

            var httpContext = _httpContextAccessor.HttpContext;

            if (httpContext == null || httpContext.User.Identity?.IsAuthenticated != true)
            {
                _sites = new List<SiteViewModel>();
                return _sites;
            }

            var loaded = await _siteApi.GetSitesAsync(httpContext.User.GetAccessToken(), httpContext.RequestAborted);

            if (loaded == null)
            {
                SitesLoadFailed = true;
                loaded = new List<SiteViewModel>();
            }

            _sites = loaded;
            return _sites;
        }

        // Returns the chosen site if the user can still see it. With no valid choice it opens the only site the user has, or returns null so the site picker asks when there are several
        public async Task<SiteViewModel?> GetCurrentAsync()
        {
            var sites = await GetSitesAsync();
            var selectedId = SelectedSiteId();

            var current = sites.FirstOrDefault(s => string.Equals(s.Id, selectedId, StringComparison.OrdinalIgnoreCase));

            if (current == null && sites.Count == 1)
            {
                // Nothing valid is remembered but there is only one site to be in, so open it instead of asking
                current = sites[0];
                Select(current.Id);
            }

            // With several sites and none remembered, no site is chosen for the user; returning null makes the site picker open so they choose
            return current;
        }

        // Returns the current site's name for page titles, or the product name when no site is chosen
        public async Task<string> GetDisplayNameAsync()
        {
            var current = await GetCurrentAsync();

            if (current == null)
                return FallbackName;

            return current.Name;
        }

        // Makes the given site the current one for this browser
        public void Select(string siteId)
        {
            _selectedOverride = siteId;

            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext != null && !httpContext.Response.HasStarted)
                SiteCookies.WriteSiteId(httpContext, siteId);
        }

        // Returns the structure last opened in the given site, or null when there is none
        public string? LastViewedStructureId(string? siteId)
        {
            return SiteCookies.ReadStructureId(_httpContextAccessor.HttpContext, siteId);
        }

        // Remembers the structure the user just opened so the Readings link can reopen it
        public void RememberStructure(string siteId, string structureId)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext != null && !httpContext.Response.HasStarted)
                SiteCookies.WriteStructureId(httpContext, siteId, structureId);
        }

        // Forgets the last opened structure when it is the one being removed
        public void ForgetStructure(string structureId)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null || httpContext.Response.HasStarted)
                return;

            var siteId = SelectedSiteId();
            if (string.Equals(SiteCookies.ReadStructureId(httpContext, siteId), structureId, StringComparison.Ordinal))
                SiteCookies.ClearStructureId(httpContext);
        }

        // Returns the site chosen during this request, or the one remembered on the browser
        private string? SelectedSiteId()
        {
            if (_selectedOverride != null)
                return _selectedOverride;

            return SiteCookies.ReadSiteId(_httpContextAccessor.HttpContext);
        }
    }

    public static class ClaimsPrincipalExtensions
    {
        // Reads the API bearer token stored on the signed-in user's cookie
        public static string GetAccessToken(this ClaimsPrincipal user)
        {
            return user.FindFirst("access_token")?.Value ?? string.Empty;
        }

        // Reads the signed-in user's email
        public static string GetEmail(this ClaimsPrincipal user)
        {
            return user.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;
        }

        // Checks whether the signed-in user may create and change sites and structures
        public static bool CanManageSites(this ClaimsPrincipal user)
        {
            return user.IsInRole("Admin") || user.IsInRole("Operator");
        }

        // Checks whether the signed-in user may switch relays and add doses; Workers may, on the sites they were invited to
        public static bool CanControlEquipment(this ClaimsPrincipal user)
        {
            return user.IsInRole("Admin") || user.IsInRole("Operator") || user.IsInRole("Worker");
        }
    }
}