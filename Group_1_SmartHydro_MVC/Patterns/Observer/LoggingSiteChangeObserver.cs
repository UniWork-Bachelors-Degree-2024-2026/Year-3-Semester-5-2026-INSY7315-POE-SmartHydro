namespace Group_1_SmartHydro_MVC.Patterns.Observer
{
    public sealed class LoggingSiteChangeObserver : ISiteChangeObserver
    {
        private readonly ILogger<LoggingSiteChangeObserver> _logger;

        public LoggingSiteChangeObserver(ILogger<LoggingSiteChangeObserver> logger)
        {
            _logger = logger;
        }

        // Writes each change to the application log as an audit trail of who changed which site
        public Task OnSiteChangedAsync(SiteChange change, CancellationToken ct)
        {
            _logger.LogInformation("Site change {Kind} on site {SiteId}: {Description}", change.Kind, change.SiteId, change.Describe());
            return Task.CompletedTask;
        }
    }
}