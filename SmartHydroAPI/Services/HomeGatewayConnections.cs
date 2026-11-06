using System.Collections.Concurrent;
namespace SmartHydroAPI.Services;

public static class HomeGatewayConnections
{
    private static readonly ConcurrentDictionary<string, string> Connections = new();
    private static readonly SemaphoreSlim Wake = new(0, 1);
    public static string? ReservedMac { get; private set; }
    public static void Configure(IConfiguration config) {
        ReservedMac = config.GetValue<bool>("HomeGateway:Enabled") ? LoraDownlinkQueue.NormalizeMac(config["HomeGateway:NodeMac"]) : null;
    }
    public static KeyValuePair<string, string>[] Snapshot() => Connections.ToArray();
    public static void Attach(string mac, string connection) { Connections[mac] = connection; Signal(mac); }
    public static void Remove(string mac, string connection) => ((ICollection<KeyValuePair<string, string>>)Connections).Remove(new(mac, connection));
    public static bool IsCurrent(string mac, string connection) => Connections.TryGetValue(mac, out var current) && current == connection;
    public static void Signal(string mac) {
        if (!Connections.ContainsKey(mac)) return;
        try { Wake.Release(); } catch (SemaphoreFullException) { }
    }
    public static Task<bool> WaitAsync(CancellationToken ct) => Wake.WaitAsync(TimeSpan.FromSeconds(2), ct);
}
