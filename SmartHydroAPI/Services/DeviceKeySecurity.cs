using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace SmartHydroAPI.Services;

public static class DeviceKeySecurity
{
    public const string Header = "X-SmartHydro-Device-Key";
    public static bool Matches(string? expected, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(expected) || expected.Length < 32 || string.IsNullOrEmpty(supplied)) return false;
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));
    }

    public static async Task InvokeAsync(HttpContext context, IConfiguration configuration, RequestDelegate next)
    {
        var path = context.Request.Path.Value?.TrimEnd('/') ?? "";
        bool home = path.Equals("/hubs/home-gateway", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/hubs/home-gateway/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/home-gateway/", StringComparison.OrdinalIgnoreCase);
        if (home) {
            var mac = LoraDownlinkQueue.NormalizeMac(configuration["HomeGateway:NodeMac"]);
            var compact = mac.Replace(":", "");
            var key = configuration["Security:HomeGatewayKey"];
            if (!configuration.GetValue<bool>("HomeGateway:Enabled") || compact.Length != 12 || !compact.All(Uri.IsHexDigit) || key?.Length != 64 || !key.All(Uri.IsHexDigit)
                || Matches(configuration["Security:DeviceKey"], key)) {
                context.Response.StatusCode = 503; return;
            }
            if (context.Request.Headers[Header].Count != 1 || !Matches(key, context.Request.Headers[Header].ToString())) {
                context.Response.StatusCode = 401; return;
            }
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("homeGatewayMac", mac)], "HomeGatewayKey"));
            await next(context); return;
        }
        bool hub = path.Equals("/hubs/actuator", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/hubs/actuator/", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/hubs/actuators", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/hubs/actuators/", StringComparison.OrdinalIgnoreCase);
        bool ingestion = (path.Equals("/api/telemetry", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/dosing/callback", StringComparison.OrdinalIgnoreCase)) && HttpMethods.IsPost(context.Request.Method);
        bool gateway = path.Equals("/api/telemetry/lora", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/telemetry/lora/", StringComparison.OrdinalIgnoreCase);
        if (!ingestion && !gateway && !hub) { await next(context); return; }
        var expected = configuration["Security:DeviceKey"];
        bool valid = context.Request.Headers[Header].Count == 1 && Matches(expected, context.Request.Headers[Header].ToString());
        if ((ingestion || gateway) && !valid)
        {
            context.Response.StatusCode = string.IsNullOrWhiteSpace(expected) || expected.Length < 32 ? 503 : 401;
            await context.Response.WriteAsJsonAsync(new { message = "Device authentication required." });
            return;
        }
        if (hub && valid)
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("device", "true")], "DeviceKey"));
        await next(context);
    }
}
