using Microsoft.Extensions.DependencyInjection;

namespace SmartHydroAPI.Patterns.Observer
{
    // In-memory event publisher resolving and invoking all registered event handlers via service provider
    public class InMemoryEventPublisher : IEventPublisher
    {
        private readonly IServiceProvider _serviceProvider;

        // Injects service provider for scoped handler resolution
        public InMemoryEventPublisher(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

        // Dispatches domain event across all resolved handlers
        public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default) where TEvent : IDomainEvent
        {
            using var scope = _serviceProvider.CreateScope();
            var handlers = scope.ServiceProvider.GetServices<IEventHandler<TEvent>>();

            foreach (var handler in handlers)
            {
                await handler.HandleAsync(domainEvent, ct);
            }
        }
    }
}
