using System.Collections.Concurrent;

namespace SmartHydroAPI.Services
{
    // Thread-safe in-memory coordinator for compact LoRa downlink commands destined for the SenseLoRa 4G Gateway
    // Supports both instant long-polling (GET /api/telemetry/lora/poll?waitMs=4000) and uplink piggybacking (POST /api/telemetry/lora)
    public static class LoraDownlinkQueue
    {
        private static readonly ConcurrentDictionary<string, Dictionary<string, object>> PendingByMac = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, DateTime> LastCommandIssuedUtc = new(StringComparer.OrdinalIgnoreCase);
        private static volatile TaskCompletionSource<bool> CommandArrivalSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Gateway Last-Seen Tracking (updated on every POST /api/telemetry/lora and GET /api/telemetry/lora/poll)
        public static DateTime? LastGatewaySeenAt { get; private set; }
        public static DateTime? LastGatewayPostAt { get; private set; }
        public static DateTime? LastGatewayPollAt { get; private set; }
        public static int? LastGatewayRssi { get; private set; }

        public static void RecordGatewayActivity(bool isPost, int? rssi = null)
        {
            var now = DateTime.UtcNow;
            LastGatewaySeenAt = now;
            if (isPost)
            {
                LastGatewayPostAt = now;
            }
            else
            {
                LastGatewayPollAt = now;
            }
            if (rssi.HasValue && rssi.Value != 0)
            {
                LastGatewayRssi = rssi.Value;
            }
        }

        // Canonical Operating Mode Mapping: 0 = "lora" (MODE_LORA_BLE), 1 = "online" (MODE_WIFI_CLOUD), 2 = "softap" (MODE_EMERGENCY_SOFTAP)
        public static int CanonicalModeIndex(string? mode)
        {
            var normalized = (mode ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "1" or "online" or "wifi" or "cloud" or "wifi_cloud" => 1,
                "2" or "softap" or "offline" or "ap" or "hotspot" => 2,
                _ => 0
            };
        }

        public static string CanonicalModeName(int modeIndex) => modeIndex switch
        {
            1 => "online",
            2 => "softap",
            _ => "lora"
        };

        // Holdoff window while a command is waiting in the queue before Gateway polling
        private static readonly TimeSpan CommandGraceWindow = TimeSpan.FromSeconds(15);
        // Short in-flight air-time window (3.5s) after the Gateway dequeues the command
        private static readonly TimeSpan PostDequeueInFlightWindow = TimeSpan.FromSeconds(3.5);

        private static void SignalCommandArrived()
        {
            var current = CommandArrivalSignal;
            current.TrySetResult(true);
            if (CommandArrivalSignal.Task.IsCompleted)
            {
                Interlocked.Exchange(ref CommandArrivalSignal, new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
            }
        }

        public static async Task<bool> WaitForCommandAsync(TimeSpan timeout, CancellationToken ct)
        {
            if (!PendingByMac.IsEmpty || timeout <= TimeSpan.Zero)
            {
                return !PendingByMac.IsEmpty;
            }

            var signalTask = CommandArrivalSignal.Task;
            if (!PendingByMac.IsEmpty)
            {
                return true;
            }

            try
            {
                using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var delayTask = Task.Delay(timeout, delayCts.Token);
                var completed = await Task.WhenAny(signalTask, delayTask).ConfigureAwait(false);
                if (completed == signalTask)
                {
                    delayCts.Cancel();
                    return true;
                }
            }
            catch (OperationCanceledException)
            {
            }

            return !PendingByMac.IsEmpty;
        }

        public static string NormalizeMac(string? mac)
        {
            var trimmed = (mac ?? string.Empty).Trim().ToUpperInvariant();
            var clean = trimmed.Replace(":", "").Replace("-", "");
            if (clean.Length == 12 && clean.All(Uri.IsHexDigit))
            {
                return string.Join(":", Enumerable.Range(0, 6).Select(i => clean.Substring(i * 2, 2)));
            }
            return trimmed;
        }

        public static bool HasPendingForMac(string? mac)
        {
            var canonical = NormalizeMac(mac);
            return !string.IsNullOrEmpty(canonical) && PendingByMac.ContainsKey(canonical);
        }

        public static bool IsInCommandGraceWindow(string? mac)
        {
            var canonical = NormalizeMac(mac);
            if (string.IsNullOrEmpty(canonical)) return false;

            if (PendingByMac.ContainsKey(canonical))
                return true;

            if (LastCommandIssuedUtc.TryGetValue(canonical, out var issuedAt))
            {
                if (DateTime.UtcNow - issuedAt < CommandGraceWindow)
                    return true;
            }

            return false;
        }

        public static void ClearCommandGraceWindow(string? mac)
        {
            var canonical = NormalizeMac(mac);
            if (!string.IsNullOrEmpty(canonical))
            {
                LastCommandIssuedUtc.TryRemove(canonical, out _);
            }
        }

        public static void EnqueueActuatorState(
            string mac,
            bool? mainWaterPump = null,
            bool? spraySystem = null,
            bool? circulationFan = null,
            bool? extractorFan = null,
            bool? growLight = null,
            bool? phUpPump = null,
            bool? phDownPump = null,
            bool? nutrientUpPump = null,
            bool? dilutionDownPump = null)
        {
            var canonical = NormalizeMac(mac);
            if (string.IsNullOrEmpty(canonical)) return;

            LastCommandIssuedUtc[canonical] = DateTime.UtcNow;

            PendingByMac.AddOrUpdate(
                canonical,
                _ => BuildActuatorDict(canonical, null, mainWaterPump, spraySystem, circulationFan, extractorFan, growLight, phUpPump, phDownPump, nutrientUpPump, dilutionDownPump),
                (_, existing) =>
                {
                    lock (existing)
                    {
                        return BuildActuatorDict(canonical, existing, mainWaterPump, spraySystem, circulationFan, extractorFan, growLight, phUpPump, phDownPump, nutrientUpPump, dilutionDownPump);
                    }
                });
            SignalCommandArrived();
        }

        public static void EnqueueTimedActuator(string mac, string actuatorType, int durationSeconds)
        {
            var canonical = NormalizeMac(mac);
            if (string.IsNullOrEmpty(canonical)) return;

            LastCommandIssuedUtc[canonical] = DateTime.UtcNow;

            PendingByMac.AddOrUpdate(
                canonical,
                _ =>
                {
                    var d = new Dictionary<string, object>(StringComparer.Ordinal) { ["m"] = canonical };
                    ApplyTimedActuator(d, actuatorType, durationSeconds);
                    return d;
                },
                (_, existing) =>
                {
                    lock (existing)
                    {
                        ApplyTimedActuator(existing, actuatorType, durationSeconds);
                        return existing;
                    }
                });
            SignalCommandArrived();
        }

        public static void EnqueueDosing(string mac, string dosingType, int durationMs)
        {
            var canonical = NormalizeMac(mac);
            if (string.IsNullOrEmpty(canonical)) return;

            LastCommandIssuedUtc[canonical] = DateTime.UtcNow;
            var upperType = dosingType.Trim().ToUpperInvariant();

            PendingByMac.AddOrUpdate(
                canonical,
                _ =>
                {
                    var d = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["m"] = canonical,
                        ["dt"] = upperType,
                        ["dm"] = durationMs
                    };
                    ApplyDosingPumpFlags(d, upperType);
                    return d;
                },
                (_, existing) =>
                {
                    lock (existing)
                    {
                        existing["m"] = canonical;
                        existing["dt"] = upperType;
                        existing["dm"] = durationMs;
                        ApplyDosingPumpFlags(existing, upperType);
                        return existing;
                    }
                });
            SignalCommandArrived();
        }

        private static void ApplyDosingPumpFlags(Dictionary<string, object> dict, string upperType)
        {
            if (upperType is "PH_UP" or "PHUP" or "PHUPPUMP")
            {
                dict["pu"] = 1;
                dict["pd"] = 0;
            }
            else if (upperType is "PH_DOWN" or "PHDOWN" or "PHDOWNPUMP")
            {
                dict["pd"] = 1;
                dict["pu"] = 0;
            }
            else if (upperType is "NUTRIENT_UP" or "NUTRIENTUP" or "EC_UP" or "ECUP" or "NUTRIENTUPPUMP")
            {
                dict["nu"] = 1;
                dict["nd"] = 0;
            }
            else if (upperType is "DILUTION_DOWN" or "DILUTIONDOWN" or "EC_DOWN" or "ECDOWN" or "DILUTIONDOWNPUMP")
            {
                dict["nd"] = 1;
                dict["nu"] = 0;
            }
        }

        public static void EnqueueMode(string mac, string mode)
        {
            var canonical = NormalizeMac(mac);
            if (string.IsNullOrEmpty(canonical)) return;

            LastCommandIssuedUtc[canonical] = DateTime.UtcNow;
            var modeIndex = CanonicalModeIndex(mode);
            var modeName = CanonicalModeName(modeIndex);

            PendingByMac.AddOrUpdate(
                canonical,
                _ => new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["m"] = canonical,
                    ["mode"] = modeName,
                    ["operatingMode"] = modeIndex
                },
                (_, existing) =>
                {
                    lock (existing)
                    {
                        existing["m"] = canonical;
                        existing["mode"] = modeName;
                        existing["operatingMode"] = modeIndex;
                        return existing;
                    }
                });
            SignalCommandArrived();
        }

        public static void EnqueueThresholds(
            string mac,
            decimal phMin, decimal phMax,
            decimal ecMin, decimal ecMax,
            decimal tempMin, decimal tempMax,
            decimal humMin, decimal humMax)
        {
            var canonical = NormalizeMac(mac);
            if (string.IsNullOrEmpty(canonical)) return;

            LastCommandIssuedUtc[canonical] = DateTime.UtcNow;

            PendingByMac.AddOrUpdate(
                canonical,
                _ => BuildThresholdsDict(canonical, null, phMin, phMax, ecMin, ecMax, tempMin, tempMax, humMin, humMax),
                (_, existing) =>
                {
                    lock (existing)
                    {
                        return BuildThresholdsDict(canonical, existing, phMin, phMax, ecMin, ecMax, tempMin, tempMax, humMin, humMax);
                    }
                });
            SignalCommandArrived();
        }

        public static bool TryTakeForMac(string? mac, out Dictionary<string, object>? commandPayload)
        {
            var canonical = NormalizeMac(mac);
            if (!string.IsNullOrEmpty(canonical) && PendingByMac.TryRemove(canonical, out var payload))
            {
                // Keep only a short 3.5-second in-flight RF grace window after the Gateway dequeues the command
                LastCommandIssuedUtc[canonical] = DateTime.UtcNow - (CommandGraceWindow - PostDequeueInFlightWindow);
                commandPayload = payload;
                return true;
            }
            commandPayload = null;
            return false;
        }

        public static List<Dictionary<string, object>> DequeueBatch(int maxCount = 4, IReadOnlyCollection<string>? deviceMacs = null)
        {
            var list = new List<Dictionary<string, object>>();
            var allowed = deviceMacs?.Select(NormalizeMac).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var key in PendingByMac.Keys.Where(key => allowed == null || allowed.Contains(key)).Take(Math.Max(0, maxCount)).ToList())
            {
                if (PendingByMac.TryRemove(key, out var payload))
                {
                    // Keep only a short 3.5-second in-flight RF grace window after the Gateway dequeues the command
                    LastCommandIssuedUtc[key] = DateTime.UtcNow - (CommandGraceWindow - PostDequeueInFlightWindow);
                    list.Add(payload);
                }
            }
            return list;
        }

        private static Dictionary<string, object> BuildActuatorDict(
            string canonical,
            Dictionary<string, object>? target,
            bool? mainWaterPump,
            bool? spraySystem,
            bool? circulationFan,
            bool? extractorFan,
            bool? growLight,
            bool? phUpPump,
            bool? phDownPump,
            bool? nutrientUpPump,
            bool? dilutionDownPump)
        {
            var dict = target ?? new Dictionary<string, object>(StringComparer.Ordinal);
            dict["m"] = canonical;

            if (mainWaterPump.HasValue) dict["w"] = mainWaterPump.Value ? 1 : 0;
            if (spraySystem.HasValue) dict["s"] = spraySystem.Value ? 1 : 0;
            if (circulationFan.HasValue) dict["fc"] = circulationFan.Value ? 1 : 0;
            if (extractorFan.HasValue) dict["fe"] = extractorFan.Value ? 1 : 0;
            if (growLight.HasValue) dict["gl"] = growLight.Value ? 1 : 0;
            if (phUpPump.HasValue) dict["pu"] = phUpPump.Value ? 1 : 0;
            if (phDownPump.HasValue) dict["pd"] = phDownPump.Value ? 1 : 0;
            if (nutrientUpPump.HasValue) dict["nu"] = nutrientUpPump.Value ? 1 : 0;
            if (dilutionDownPump.HasValue) dict["nd"] = dilutionDownPump.Value ? 1 : 0;

            return dict;
        }

        private static void ApplyTimedActuator(Dictionary<string, object> dict, string actuatorType, int durationSeconds)
        {
            if (string.Equals(actuatorType, "SpraySystem", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(actuatorType, "spray_system", StringComparison.OrdinalIgnoreCase))
            {
                dict["s"] = 1;
                dict["st"] = durationSeconds;
            }
            else if (string.Equals(actuatorType, "MainWaterPump", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(actuatorType, "main_water_pump", StringComparison.OrdinalIgnoreCase))
            {
                dict["w"] = 1;
                dict["wt"] = durationSeconds;
            }
            else if (string.Equals(actuatorType, "CirculationFan", StringComparison.OrdinalIgnoreCase))
            {
                dict["fc"] = 1;
            }
            else if (string.Equals(actuatorType, "ExtractorFan", StringComparison.OrdinalIgnoreCase))
            {
                dict["fe"] = 1;
            }
            else if (string.Equals(actuatorType, "GrowLight", StringComparison.OrdinalIgnoreCase))
            {
                dict["gl"] = 1;
            }
        }

        private static Dictionary<string, object> BuildThresholdsDict(
            string canonical,
            Dictionary<string, object>? target,
            decimal phMin, decimal phMax,
            decimal ecMin, decimal ecMax,
            decimal tempMin, decimal tempMax,
            decimal humMin, decimal humMax)
        {
            var dict = target ?? new Dictionary<string, object>(StringComparer.Ordinal);
            dict["m"] = canonical;
            dict["p0"] = Math.Round(phMin, 2);
            dict["p1"] = Math.Round(phMax, 2);
            dict["p2"] = Math.Round(ecMin, 2);
            dict["p3"] = Math.Round(ecMax, 2);
            dict["p4"] = Math.Round(tempMin, 1);
            dict["p5"] = Math.Round(tempMax, 1);
            dict["p6"] = Math.Round(humMin, 1);
            dict["p7"] = Math.Round(humMax, 1);
            return dict;
        }
    }
}
