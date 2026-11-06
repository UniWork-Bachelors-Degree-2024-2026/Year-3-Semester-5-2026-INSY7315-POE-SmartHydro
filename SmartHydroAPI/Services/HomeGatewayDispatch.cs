using Microsoft.AspNetCore.SignalR;
using SmartHydroAPI.Data;
using SmartHydroAPI.Hubs;
namespace SmartHydroAPI.Services;

public sealed class HomeGatewayDispatch(IServiceScopeFactory scopes, IHubContext<HomeGatewayHub> hub, ILogger<HomeGatewayDispatch> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            foreach (var connection in HomeGatewayConnections.Snapshot()) {
                try {
                    using var scope = scopes.CreateScope();
                    var queue = new DeviceCommandQueue(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
                    foreach (var command in await queue.ClaimAsync(connection.Key, stoppingToken))
                        await hub.Clients.Client(connection.Value).SendAsync("Command", command, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception ex) { logger.LogWarning(ex, "Home gateway dispatch failed; commands remain subject to retry and expiry."); }
            }
            try { await HomeGatewayConnections.WaitAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
