using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Controllers;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Dtos;
namespace SmartHydroAPI.Services;

public sealed class HomeGatewayPackets(IServiceProvider services, ApplicationDbContext database)
{
    public async Task RequireFodderAsync(string mac, CancellationToken ct) {
        var compact = mac.Replace(":", "");
        var device = await database.Devices.Include(x => x.Structure).FirstOrDefaultAsync(x => x.MacAddress == mac || x.MacAddress == compact, ct);
        if (device == null) throw new ArgumentException($"Pilot controller {mac} is not registered in this API database. Check the API's HomeGateway__NodeMac and database configuration.");
        // The linked structure is authoritative when an old device profile disagrees.
        var type = device.Structure?.StructureType ?? device.StructureType;
        if (type?.Contains("FODDER", StringComparison.OrdinalIgnoreCase) != true) {
            var profile = device.Structure == null
                ? "device profile (no linked structure found)"
                : $"linked structure {device.Structure.Id}";
            throw new ArgumentException($"Pilot controller {mac}: {profile} has type '{type ?? "unset"}'. Assign this pilot to a registered fodder controller. Check the API database and deployed version; do not change a tower into fodder.");
        }
    }
    public async Task<object?> ProcessAsync(LoraCompactTelemetryDto packet, string allowedMac, CancellationToken ct) {
        if (LoraDownlinkQueue.NormalizeMac(packet.DeviceMac) != allowedMac) throw new ArgumentException("This gateway is not assigned to that controller.");
        await RequireFodderAsync(allowedMac, ct);
        if (!Validator.TryValidateObject(packet, new ValidationContext(packet), new List<ValidationResult>(), true)) throw new ArgumentException("Invalid controller packet.");
        // Reuse the ingestion path that commits telemetry_realtime before returning success.
        var controller = ActivatorUtilities.CreateInstance<TelemetryController>(services);
        var result = await controller.IngestLoraCompact(packet, ct, compact: true);
        if (result is OkObjectResult ok) return ok.Value;
        throw new ArgumentException("Controller packet was not accepted.");
    }
}
