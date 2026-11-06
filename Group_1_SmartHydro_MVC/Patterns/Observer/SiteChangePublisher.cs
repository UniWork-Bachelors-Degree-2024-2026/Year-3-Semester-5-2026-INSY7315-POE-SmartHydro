namespace Group_1_SmartHydro_MVC.Patterns.Observer
{
    public sealed class SiteChangePublisher : ISiteChangePublisher
    {
        private readonly object _lock = new();
        private readonly List<ISiteChangeObserver> _observers = new();
        private readonly ILogger<SiteChangePublisher> _logger;

        public SiteChangePublisher(IEnumerable<ISiteChangeObserver> initialObservers, ILogger<SiteChangePublisher> logger)
        {
            _logger = logger;

            foreach (var observer in initialObservers)
            {
                Subscribe(observer);
            }
        }

        // Adds an observer so it is told about every later change, ignoring one that is already subscribed
        public void Subscribe(ISiteChangeObserver observer)
        {
            lock (_lock)
            {
                if (!_observers.Contains(observer))
                {
                    _observers.Add(observer);
                }
            }
        }

        // Removes an observer so it stops being told about changes
        public void Unsubscribe(ISiteChangeObserver observer)
        {
            lock (_lock)
            {
                _observers.Remove(observer);
            }
        }

        // Notifies every subscribed observer of a change; one failing observer never stops the others or the user's request
        public async Task PublishAsync(SiteChange change, CancellationToken ct = default)
        {
            List<ISiteChangeObserver> snapshot;

            lock (_lock)
            {
                snapshot = _observers.ToList();
            }

            foreach (var observer in snapshot)
            {
                try
                {
                    await observer.OnSiteChangedAsync(change, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Observer {Observer} could not handle {Kind} for site {SiteId}.", observer.GetType().Name, change.Kind, change.SiteId);
                }
            }
        }
    }
}