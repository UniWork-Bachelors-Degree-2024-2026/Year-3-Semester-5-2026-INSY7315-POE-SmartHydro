namespace SmartHydroAPI.Patterns.Observer
{
    // Marker interface for domain and integration events
    public interface IDomainEvent { }

    // Handler interface for observing and processing domain events asynchronously
    public interface IEventHandler<in TEvent> where TEvent : IDomainEvent
    {
        // Handles the emitted event
        Task HandleAsync(TEvent domainEvent, CancellationToken ct = default);
    }

    // Publisher interface dispatching domain events to registered in-memory observers
    public interface IEventPublisher
    {
        // Publishes an event to all subscribed handlers
        Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default) where TEvent : IDomainEvent;
    }
}
