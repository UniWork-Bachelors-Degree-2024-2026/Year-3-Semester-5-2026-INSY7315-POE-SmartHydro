using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Services;
namespace SmartHydroAPI.Controllers;

[Authorize, ApiController, Route("api/home-gateway")]
public sealed class HomeGatewayController(HomeGatewayPackets packets, ApplicationDbContext database) : ControllerBase
{
    private string? Mac => User.FindFirst("homeGatewayMac")?.Value;
    [HttpPost("packet")]
    public async Task<IActionResult> Packet(LoraCompactTelemetryDto packet, CancellationToken ct) {
        if (Mac == null) return Forbid();
        try { return Ok(await packets.ProcessAsync(packet, Mac, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }
    [HttpGet("commands")]
    public async Task<IActionResult> Commands(CancellationToken ct) {
        if (Mac == null) return Forbid();
        try { await packets.RequireFodderAsync(Mac, ct); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        return Ok(new { commands = await new DeviceCommandQueue(database).ClaimAsync(Mac, ct), utc = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
    }
}
