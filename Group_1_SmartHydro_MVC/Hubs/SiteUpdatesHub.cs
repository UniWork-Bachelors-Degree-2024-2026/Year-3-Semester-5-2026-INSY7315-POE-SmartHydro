using Group_1_SmartHydro_MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Group_1_SmartHydro_MVC.Hubs
{
    [Authorize]
    public class SiteUpdatesHub : Hub
    {
        public const string Path = "/hubs/site-updates";
        public const string AdminsGroup = "admins";

        private readonly SiteApiClient _siteApi;

        public SiteUpdatesHub(SiteApiClient siteApi)
        {
            _siteApi = siteApi;
        }

        // Builds the group name used for everyone watching one site
        public static string SiteGroup(string siteId)
        {
            return "site:" + siteId.ToLowerInvariant();
        }

        // Adds admins to the admins group as soon as they connect so they hear about every site
        public override async Task OnConnectedAsync()
        {
            if (Context.User != null && Context.User.IsInRole("Admin"))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, AdminsGroup);
            }

            await base.OnConnectedAsync();
        }

        // Starts sending a site's changes to this page, but only after the API confirms the user can see that site
        public async Task<bool> JoinSite(string siteId)
        {
            if (Context.User == null || string.IsNullOrWhiteSpace(siteId))
            {
                return false;
            }

            var site = await _siteApi.GetSiteAsync(siteId, Context.User.GetAccessToken(), Context.ConnectionAborted);

            if (site == null)
            {
                return false;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, SiteGroup(site.Id));
            return true;
        }

        // Stops sending a site's changes to this page
        public async Task LeaveSite(string siteId)
        {
            if (string.IsNullOrWhiteSpace(siteId))
            {
                return;
            }

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, SiteGroup(siteId));
        }
    }
}