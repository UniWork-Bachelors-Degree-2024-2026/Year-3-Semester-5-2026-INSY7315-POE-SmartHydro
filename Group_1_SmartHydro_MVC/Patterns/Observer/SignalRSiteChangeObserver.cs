using Group_1_SmartHydro_MVC.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Group_1_SmartHydro_MVC.Patterns.Observer
{
    public sealed class SignalRSiteChangeObserver : ISiteChangeObserver
    {
        public const string ClientMethod = "siteChanged";

        private readonly IHubContext<SiteUpdatesHub> _hub;

        public SignalRSiteChangeObserver(IHubContext<SiteUpdatesHub> hub)
        {
            _hub = hub;
        }

        // Pushes the change to every open page watching the site and to every signed-in admin
        public async Task OnSiteChangedAsync(SiteChange change, CancellationToken ct)
        {
            var payload = new
            {
                siteId = change.SiteId,
                kind = change.Kind.ToString(),
                subject = change.Subject,
                actorEmail = change.ActorEmail,
                message = change.Describe()
            };

            var groups = new List<string> { SiteUpdatesHub.AdminsGroup };

            if (!string.IsNullOrWhiteSpace(change.SiteId))
            {
                groups.Add(SiteUpdatesHub.SiteGroup(change.SiteId));
            }

            await _hub.Clients.Groups(groups).SendAsync(ClientMethod, payload, ct);
        }
    }
}