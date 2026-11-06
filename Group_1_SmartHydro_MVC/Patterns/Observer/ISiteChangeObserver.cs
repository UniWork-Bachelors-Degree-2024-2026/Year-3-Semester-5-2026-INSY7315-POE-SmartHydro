namespace Group_1_SmartHydro_MVC.Patterns.Observer
{
    public interface ISiteChangeObserver
    {
        Task OnSiteChangedAsync(SiteChange change, CancellationToken ct);
    }

    public interface ISiteChangePublisher
    {
        void Subscribe(ISiteChangeObserver observer);

        void Unsubscribe(ISiteChangeObserver observer);

        Task PublishAsync(SiteChange change, CancellationToken ct = default);
    }
}