namespace SmartHydroAPI.Services
{
    public class AuthEmailWorker : BackgroundService
    {
        private readonly AuthEmailQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AuthEmailWorker> _logger;

        public AuthEmailWorker(AuthEmailQueue queue, IServiceScopeFactory scopeFactory, ILogger<AuthEmailWorker> logger)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        // Processes queued sign-ups, resends and password resets one at a time, outside the request, so the API answers in the same way and the same time whether or not an account exists
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await foreach (var job in _queue.ReadAllAsync(stoppingToken))
                {
                    await ProcessAsync(job);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Authentication email worker is stopping.");
            }
        }

        // Runs one job in its own scope, logging a failure without stopping the worker
        private async Task ProcessAsync(AuthEmailJob job)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var authentication = scope.ServiceProvider.GetRequiredService<AuthenticationService>();
                await authentication.ProcessQueuedJobAsync(job);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not process the {Kind} job for {Email}.", job.Kind, job.Email);
            }
        }
    }
}