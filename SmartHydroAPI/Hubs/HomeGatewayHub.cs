using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Services;
namespace SmartHydroAPI.Hubs;

[Authorize]
public sealed class HomeGatewayHub(HomeGatewayPackets packets) : Hub
{
    private string Mac => Context.User?.FindFirst("homeGatewayMac")?.Value ?? throw new HubException("Gateway authentication required.");
    public override async Task OnConnectedAsync() {
        try { await packets.RequireFodderAsync(Mac, Context.ConnectionAborted); }
        catch (ArgumentException ex) { throw new HubException(ex.Message); }
        HomeGatewayConnections.Attach(Mac, Context.ConnectionId);
        await Clients.Caller.SendAsync("Ready", new { mac = Mac, utc = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
        await base.OnConnectedAsync();
    }
    public async Task<object?> Packet(LoraCompactTelemetryDto packet) {
        if (!HomeGatewayConnections.IsCurrent(Mac, Context.ConnectionId)) throw new HubException("Gateway connection was replaced.");
        try { return await packets.ProcessAsync(packet, Mac, Context.ConnectionAborted); }
        catch (ArgumentException ex) { throw new HubException(ex.Message); }
    }
    public override async Task OnDisconnectedAsync(Exception? exception) {
        HomeGatewayConnections.Remove(Mac, Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
