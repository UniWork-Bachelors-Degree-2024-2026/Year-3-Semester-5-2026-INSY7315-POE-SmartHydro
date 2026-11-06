using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartHydroAPI.Patterns.Decorator;
using SmartHydroAPI.Patterns.Factory;
using SmartHydroAPI.Patterns.Observer;
using SmartHydroAPI.Patterns.Observer.Events;
using SmartHydroAPI.Patterns.Observer.Handlers;
using SmartHydroAPI.Patterns.Repository;
using SmartHydroAPI.Patterns.Specification;
using SmartHydroAPI.Patterns.Strategy;
using SmartHydroAPI.Services;

namespace SmartHydroAPI.Extensions
{
    // Extension methods registering all Clean Architecture design patterns and services in DI container
    public static class ServiceCollectionExtensions
    {
        // Configures Repository, Strategy, Specification, Observer, Decorator, and Factory pattern dependencies
        public static IServiceCollection AddSmartHydroPatterns(this IServiceCollection services, IConfiguration configuration)
        {
            // 1. Repository Pattern
            services.AddScoped<IActuatorRepository, ActuatorRepository>();
            services.AddScoped<ITelemetryRepository, TelemetryRepository>();

            // 2. Strategy Pattern - Telemetry Ingestion
            services.AddScoped<ITelemetryIngestionStrategy, FireBeetleC6TelemetryStrategy>();
            services.AddScoped<ITelemetryIngestionStrategy, Esp32CamTelemetryStrategy>();
            services.AddScoped<ITelemetryStrategyFactory, TelemetryStrategyFactory>();

            // 2. Strategy Pattern - Image Storage (Defaults to Database with fallback/optional Local Disk)
            var useLocalStorage = configuration.GetValue<bool>("Storage:UseLocalStorage", false);
            if (useLocalStorage)
            {
                services.AddScoped<IImageStorageStrategy, LocalDiskImageStorageStrategy>();
            }
            else
            {
                services.AddScoped<IImageStorageStrategy, DatabaseImageStorageStrategy>();
            }

            // 3. Specification / Rule Engine Pattern
            services.AddScoped<ISpecification<TelemetryEvaluationContext>, PhOutOfBoundsSpecification>();
            services.AddScoped<ISpecification<TelemetryEvaluationContext>, WaterLevelDepletedSpecification>();
            services.AddScoped<ISpecification<TelemetryEvaluationContext>, TemperatureOutOfBoundsSpecification>();
            services.AddScoped<ISpecification<TelemetryEvaluationContext>, EcOutOfBoundsSpecification>();
            services.AddScoped<ITelemetryRuleEngine, TelemetryRuleEngine>();

            // 4. Factory Pattern
            services.AddSingleton<IAlertFactory, AlertFactory>();

            // 5. Observer Pattern
            services.AddScoped<IEventPublisher, InMemoryEventPublisher>();
            services.AddScoped<IEventHandler<TelemetryIngestedEvent>, TelemetryAlertEvaluationHandler>();

            // 6. Core Services
            services.AddScoped<TelemetryService>();
            services.AddScoped<ActuatorService>();
            services.AddScoped<AlertService>();
            services.AddScoped<CameraService>();
            services.AddScoped<DeviceService>();
            services.AddScoped<SiteService>();
            services.AddScoped<StructureService>();
            services.AddScoped<ThresholdService>();
            services.AddScoped<ISyncService, SyncService>();
            services.AddScoped<IDosingService, DosingService>();

            // 7. Decorator Pattern (Cached Service Registrations)
            services.AddScoped<ITelemetryService>(sp =>
            {
                var coreService = sp.GetRequiredService<TelemetryService>();
                var cache = sp.GetRequiredService<IMemoryCache>();
                return new CachedTelemetryServiceDecorator(coreService, cache);
            });

            services.AddScoped<IActuatorService>(sp =>
            {
                var coreService = sp.GetRequiredService<ActuatorService>();
                var cache = sp.GetRequiredService<IMemoryCache>();
                return new CachedActuatorServiceDecorator(coreService, cache);
            });

            return services;
        }
    }
}
