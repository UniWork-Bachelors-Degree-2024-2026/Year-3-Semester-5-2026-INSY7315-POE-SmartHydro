using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Hubs;
using Microsoft.AspNetCore.Mvc;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Patterns.Decorator;
using Microsoft.AspNetCore.Authorization;
using SmartHydroAPI.Data;
using SmartHydroAPI.Services;

namespace SmartHydroAPI.Controllers
{
    // Controller handling sensor telemetry ingestion and real-time/historical telemetry querying
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class TelemetryController : ControllerBase
    {
        private readonly ITelemetryService _service;
        private readonly ApplicationDbContext _database;
        private readonly IActuatorService? _actuatorService;
        private readonly IHubContext<ActuatorHub>? _hubContext;

        public TelemetryController(
            ITelemetryService service,
            ApplicationDbContext database,
            IActuatorService? actuatorService = null,
            IHubContext<ActuatorHub>? hubContext = null)
        {
            _service = service;
            _database = database;
            _actuatorService = actuatorService;
            _hubContext = hubContext;
        }

        // High-throughput edge ingestion endpoint: ingests sensor readings and piggybacks latest actuator directives
        [AllowAnonymous]
        [HttpPost]
        public async Task<ActionResult<ActuatorStateResponseDto>> Ingest([FromBody] TelemetryIngestDto dto, CancellationToken ct, [FromQuery] bool compact = false)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            dto.DeviceMac = LoraDownlinkQueue.NormalizeMac(dto.DeviceMac);
            dto.PrepareCapture();
            var actuatorSync = await _service.IngestAndSyncAsync(dto, ct);

            if (_hubContext != null && dto.IsLive)
            {
                try
                {
                    await _hubContext.Clients.Group(LoraDownlinkQueue.NormalizeMac(dto.DeviceMac).Replace(":", "")).SendAsync("ReceiveModeState", await ModeStateService.GetAsync(_database, dto.DeviceMac, ct), ct);
                    await _hubContext.Clients.Group(LoraDownlinkQueue.NormalizeMac(dto.DeviceMac).Replace(":", "")).SendAsync("OnTelemetryUpdated", new
                    {
                        deviceMac = dto.DeviceMac,
                        ph = dto.Ph >= 0m ? (decimal?)dto.Ph : null,
                        phLevel = dto.Ph >= 0m ? (decimal?)dto.Ph : null,
                        phDisplayFallback = await PhDisplayFallback.GetAsync(_database, dto.DeviceMac, dto.Ph, dto.RecordedAtUtc, ct),
                        ec = dto.Ec >= 0m ? (decimal?)dto.Ec : null,
                        ecMs = dto.Ec >= 0m ? (decimal?)dto.Ec : null,
                        temperature = dto.Temperature >= -40m && dto.Temperature <= 80m ? (decimal?)dto.Temperature : null,
                        temperatureC = dto.Temperature >= -40m && dto.Temperature <= 80m ? (decimal?)dto.Temperature : null,
                        humidity = dto.Humidity >= 0m ? (decimal?)dto.Humidity : null,
                        humidityPercent = dto.Humidity >= 0m ? (decimal?)dto.Humidity : null,
                        sensorFaults = SensorFaults(dto),
                        lightLux = dto.Light >= 0m ? (decimal?)dto.Light : null,
                        waterLevelDetected = dto.WaterLevelDetected,
                        relayBitmask = ReportedRelays(dto),
                        reportedSequence = dto.Sequence,
                        recordedAt = dto.RecordedAtUtc,
                        operatingMode = dto.OperatingMode ?? 1,
                        preferredMode = dto.PreferredMode,
                        modeReason = dto.ModeReason
                    }, ct);
                    await _hubContext.Clients.Group(LoraDownlinkQueue.NormalizeMac(dto.DeviceMac).Replace(":", "")).SendAsync("OnActuatorStateChanged", actuatorSync, ct);
                }
                catch { }
            }

            var commands = await new DeviceCommandQueue(_database).ClaimAsync(dto.DeviceMac, ct);
            if (compact) return Ok(new { success = true, seq = dto.Sequence,
                utc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), command = commands.FirstOrDefault() });
            return Ok(actuatorSync with { Command = commands.FirstOrDefault(), ServerUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
        }

        // SenseLoRa 4G Gateway downlink poll endpoint supporting both instant non-blocking polls and sub-second long-polling (?waitMs=5000)
        [AllowAnonymous]
        [HttpGet("lora/poll")]
        public async Task<ActionResult> PollLoraCommands([FromQuery] int waitMs = 0, CancellationToken ct = default, [FromQuery] bool compact = false, [FromQuery] string? macs = null)
        {
            string[]? deviceMacs = null;
            if (macs != null) {
                var parts = macs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length is < 1 or > 8 || parts.Any(x => x.Length != 12 || !x.All(Uri.IsHexDigit)))
                    return BadRequest(new { message = "Supply up to eight compact node MAC addresses." });
                deviceMacs = parts.Select(LoraDownlinkQueue.NormalizeMac).Distinct().ToArray();
            }
            LoraDownlinkQueue.RecordGatewayActivity(isPost: false);
            var queue = new DeviceCommandQueue(_database);
            var commands = await queue.ClaimAsync(ct: ct, deviceMacs: deviceMacs);
            commands.AddRange(LoraDownlinkQueue.DequeueBatch(4 - commands.Count, deviceMacs));
            var deadline = DateTime.UtcNow.AddMilliseconds(Math.Clamp(waitMs, 0, 10000));
            while (commands.Count == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(200, ct);
                commands = await queue.ClaimAsync(ct: ct, deviceMacs: deviceMacs);
                commands.AddRange(LoraDownlinkQueue.DequeueBatch(4 - commands.Count, deviceMacs));
            }
            LoraDownlinkQueue.RecordGatewayActivity(isPost: false);
            if (compact && commands.Count == 0) return NoContent();
            if (compact) return Ok(new { commands });
            return Ok(new { commands, gatewaySeenAt = LoraDownlinkQueue.LastGatewaySeenAt });
        }

        // Returns live SenseLoRa 4G Gateway reachability and last-seen timestamps
        [AllowAnonymous]
        [HttpGet("gateway-status")]
        public async Task<ActionResult> GetGatewayStatus([FromQuery] string? mac = null, CancellationToken ct = default)
        {
            if (mac != null && !await DeviceAccess.CanReadDeviceAsync(_database, User, mac, ct)) return NotFound();
            var lastSeen = LoraDownlinkQueue.LastGatewaySeenAt;
            var isOnline = lastSeen.HasValue && (DateTime.UtcNow - lastSeen.Value) <= TimeSpan.FromSeconds(60);
            return Ok(new
            {
                isOnline, lastGatewaySeenAt = lastSeen,
                lastGatewayPostAt = LoraDownlinkQueue.LastGatewayPostAt,
                lastGatewayPollAt = LoraDownlinkQueue.LastGatewayPollAt,
                lastGatewayRssi = LoraDownlinkQueue.LastGatewayRssi,
                modeState = mac == null ? null : await ModeStateService.GetAsync(_database, mac, ct)
            });
        }

        // Edge SenseLoRa 4G Gateway direct uplink: ingests Compact Minified JSON over cellular HTTPS POST
        [AllowAnonymous]
        [HttpPost("lora")]
        public async Task<ActionResult> IngestLoraCompact([FromBody] LoraCompactTelemetryDto dto, CancellationToken ct, [FromQuery] bool compact = false)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.DeviceMac))
            {
                return BadRequest(new { message = "DeviceMac ('m') is required." });
            }

            var canonicalMac = LoraDownlinkQueue.NormalizeMac(dto.DeviceMac);
            dto.DeviceMac = canonicalMac;
            if (dto.ActivityTransport != "wifi") LoraDownlinkQueue.RecordGatewayActivity(isPost: true, dto.GatewayRssi ?? dto.Rssi);

            if (dto.ActivityPhase.HasValue)
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);
                try { await ActivityLogService.IngestAsync(_database, dto, ct); }
                catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
                return Ok(new { success = true, seq = dto.Sequence, utc = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
            }

            if (dto.CommandAck == 1)
            {
                if (_actuatorService == null || _hubContext == null) return StatusCode(503);
                var accepted = await CommandAcknowledgements.AcceptAsync(_database, _actuatorService, _hubContext, dto, ct);
                return Ok(new { success = accepted });
            }

            dto.PrepareCapture();

            // Decode 8-bit relay bitmask if provided in LoRa packet
            // Bit 0: WaterPump (Tower) / SpraySystem (Fodder)
            // Bit 1: GrowLight
            // Bit 2: CirculationFan
            // Bit 3: ExtractorFan
            // Bit 4: PhUpPump, Bit 5: PhDownPump, Bit 6: NutrientUpPump, Bit 7: DilutionDownPump
            if (dto.RelayBitmask.HasValue)
            {
                int r = dto.RelayBitmask.Value;
                int bit0 = (r & 1) != 0 ? 1 : 0;
                dto.MainWaterPump = bit0;
                dto.SprayPump = bit0;
                dto.GrowLight = ((r & 2) != 0 ? 1 : 0);
                dto.CirculationFan = ((r & 4) != 0 ? 1 : 0);
                dto.ExtractorFan = ((r & 8) != 0 ? 1 : 0);
                dto.PhUpPump = ((r & 16) != 0 ? 1 : 0);
                dto.PhDownPump = ((r & 32) != 0 ? 1 : 0);
                dto.NutrientUpPump = ((r & 64) != 0 ? 1 : 0);
                dto.DilutionDownPump = ((r & 128) != 0 ? 1 : 0);
            }

            int? effectiveWaterPump = dto.MainWaterPump ?? dto.WaterPumpAlt ?? dto.SprayPump;
            int? effectiveSpray = dto.SprayPump ?? dto.MainWaterPump ?? dto.WaterPumpAlt;

            var ingestDto = new TelemetryIngestDto
            {
                DeviceMac = canonicalMac,
                Sequence = dto.Sequence, Age = dto.Age, CapturedAtUnix = dto.CapturedAtUnix,
                CaptureTimeUncertain = dto.CaptureTimeUncertain, ReceivedAtUtc = dto.ReceivedAtUtc,
                SystemType = dto.SystemType, Transport = "lora", OperatingMode = dto.OperatingMode,
                PreferredMode = dto.PreferredMode, ModeReason = dto.ModeReason,
                Ph = dto.Ph ?? -1m,
                Ec = dto.Ec ?? -1m,
                Light = dto.LightLux ?? -1m,
                Temperature = dto.Temperature ?? -99m,
                Humidity = dto.Humidity ?? -1m,
                WaterLevelRaw = dto.WaterLevelRaw ?? 0,
                MlPh = dto.ModelPredictions.HasValue ? (dto.ModelPredictions.Value & 3) : 2,
                MlEc = dto.ModelPredictions.HasValue ? ((dto.ModelPredictions.Value >> 2) & 3) : 2,
                MlTemp = dto.ModelPredictions.HasValue ? ((dto.ModelPredictions.Value >> 4) & 3) : 2,
                MlHumidity = dto.ModelPredictions.HasValue ? ((dto.ModelPredictions.Value >> 6) & 3) : 2,
                WaterLevelDetected = dto.WaterLevel.HasValue && dto.WaterLevel.Value == 1,
                GrowLight = dto.GrowLight.HasValue ? (dto.GrowLight.Value == 1) : null,
                CirculationFan = dto.CirculationFan.HasValue ? (dto.CirculationFan.Value == 1) : null,
                ExtractorFan = dto.ExtractorFan.HasValue ? (dto.ExtractorFan.Value == 1) : null,
                MainWaterPump = effectiveWaterPump.HasValue ? (effectiveWaterPump.Value == 1) : null,
                SpraySystem = effectiveSpray.HasValue ? (effectiveSpray.Value == 1) : null,
                PhUpPump = dto.PhUpPump.HasValue ? (dto.PhUpPump.Value == 1) : null,
                PhDownPump = dto.PhDownPump.HasValue ? (dto.PhDownPump.Value == 1) : null,
                NutrientUpPump = dto.NutrientUpPump.HasValue ? (dto.NutrientUpPump.Value == 1) : null,
                DilutionDownPump = dto.DilutionDownPump.HasValue ? (dto.DilutionDownPump.Value == 1) : null
            };

            // If an active dosing pulse is reported by the edge node (bits 4..7 = 1), clear any command grace holdoff immediately
            bool activeDoseReported = (dto.PhUpPump == 1) || (dto.PhDownPump == 1) || (dto.NutrientUpPump == 1) || (dto.DilutionDownPump == 1);
            var actuatorSync = await _service.IngestAndSyncAsync(ingestDto, ct);
            dto.WasDuplicate = ingestDto.WasDuplicate;
            if (activeDoseReported && ingestDto.IsLive)
                LoraDownlinkQueue.ClearCommandGraceWindow(canonicalMac);

            // Apply & broadcast real-time actuator states from the LoRa packet ONLY if no user command is in-flight
            bool inGraceWindow = LoraDownlinkQueue.IsInCommandGraceWindow(canonicalMac);
            if (ingestDto.IsLive && !inGraceWindow && (effectiveSpray.HasValue || effectiveWaterPump.HasValue || dto.CirculationFan.HasValue || dto.ExtractorFan.HasValue || dto.GrowLight.HasValue || dto.PhUpPump.HasValue || dto.PhDownPump.HasValue || dto.NutrientUpPump.HasValue || dto.DilutionDownPump.HasValue))
            {
                var cmd = new ActuatorCommandDto
                {
                    DeviceMac = canonicalMac,
                    ChangedBy = "SenseLoRa-Gateway",
                    MainWaterPump = effectiveWaterPump.HasValue ? (effectiveWaterPump.Value == 1) : null,
                    SpraySystem = effectiveSpray.HasValue ? (effectiveSpray.Value == 1) : null,
                    CirculationFan = dto.CirculationFan.HasValue ? (dto.CirculationFan.Value == 1) : null,
                    ExtractorFan = dto.ExtractorFan.HasValue ? (dto.ExtractorFan.Value == 1) : null,
                    GrowLight = dto.GrowLight.HasValue ? (dto.GrowLight.Value == 1) : null,
                    PhUpPump = dto.PhUpPump.HasValue ? (dto.PhUpPump.Value == 1) : null,
                    PhDownPump = dto.PhDownPump.HasValue ? (dto.PhDownPump.Value == 1) : null,
                    NutrientUpPump = dto.NutrientUpPump.HasValue ? (dto.NutrientUpPump.Value == 1) : null,
                    DilutionDownPump = dto.DilutionDownPump.HasValue ? (dto.DilutionDownPump.Value == 1) : null
                };

                if (_actuatorService != null)
                {
                    try
                    {
                        actuatorSync = await _actuatorService.ApplyCommandAsync(cmd, ct);
                    }
                    catch { }
                }
            }

            if (_hubContext != null && ingestDto.IsLive)
            {
                try
                {
                    await _hubContext.Clients.Group(LoraDownlinkQueue.NormalizeMac(dto.DeviceMac).Replace(":", "")).SendAsync("ReceiveModeState", await ModeStateService.GetAsync(_database, dto.DeviceMac, ct), ct);
                    await _hubContext.Clients.Group(LoraDownlinkQueue.NormalizeMac(dto.DeviceMac).Replace(":", "")).SendAsync("OnTelemetryUpdated", new
                    {
                        deviceMac = canonicalMac,
                        ph = ingestDto.Ph >= 0m ? (decimal?)ingestDto.Ph : null,
                        phLevel = ingestDto.Ph >= 0m ? (decimal?)ingestDto.Ph : null,
                        phDisplayFallback = await PhDisplayFallback.GetAsync(_database, canonicalMac, ingestDto.Ph, ingestDto.RecordedAtUtc, ct),
                        ec = ingestDto.Ec >= 0m ? (decimal?)ingestDto.Ec : null,
                        ecMs = ingestDto.Ec >= 0m ? (decimal?)ingestDto.Ec : null,
                        temperature = ingestDto.Temperature >= -40m && ingestDto.Temperature <= 80m ? (decimal?)ingestDto.Temperature : null,
                        temperatureC = ingestDto.Temperature >= -40m && ingestDto.Temperature <= 80m ? (decimal?)ingestDto.Temperature : null,
                        humidity = ingestDto.Humidity >= 0m ? (decimal?)ingestDto.Humidity : null,
                        humidityPercent = ingestDto.Humidity >= 0m ? (decimal?)ingestDto.Humidity : null,
                        sensorFaults = SensorFaults(ingestDto),
                        lightLux = ingestDto.Light >= 0m ? (decimal?)ingestDto.Light : null,
                        waterLevelDetected = ingestDto.WaterLevelDetected,
                        relayBitmask = ReportedRelays(ingestDto),
                        reportedSequence = ingestDto.Sequence,
                        recordedAt = ingestDto.RecordedAtUtc,
                        operatingMode = dto.OperatingMode ?? 0,
                        preferredMode = dto.PreferredMode,
                        modeReason = dto.ModeReason,
                        bridgeRssi = dto.BridgeRssi,
                        gatewayRssi = dto.GatewayRssi ?? dto.Rssi,
                        gatewayLastSeenAt = LoraDownlinkQueue.LastGatewaySeenAt
                    }, ct);

                    await _hubContext.Clients.Group(LoraDownlinkQueue.NormalizeMac(dto.DeviceMac).Replace(":", "")).SendAsync("OnActuatorStateChanged", actuatorSync, ct);
                }
                catch { }
            }

            // If a downlink command is waiting for this MAC, return both wrapped and direct structures so Gateway immediately radiates it
            var durableCommands = await new DeviceCommandQueue(_database).ClaimAsync(canonicalMac, ct);
            Dictionary<string, object>? pendingDownlink = durableCommands.FirstOrDefault();
            if (pendingDownlink != null || LoraDownlinkQueue.TryTakeForMac(canonicalMac, out pendingDownlink))
            {
                if (compact) return Ok(new { success = true, seq = dto.Sequence, utc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    duplicate = dto.WasDuplicate, hasPendingCommand = true, command = pendingDownlink });
                return Ok(new
                {
                    success = true,
                    hasPendingCommand = true,
                    mac = canonicalMac,
                    sync = pendingDownlink,
                    command = pendingDownlink
                });
            }

            if (compact) return Ok(new { success = true, seq = dto.Sequence, duplicate = dto.WasDuplicate, utc = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
            return Ok(new { success = true, mac = canonicalMac, sync = actuatorSync });
        }


        private static int? ReportedRelays(TelemetryIngestDto dto)
        {
            bool?[] relays = [dto.MainWaterPump ?? dto.SpraySystem, dto.GrowLight, dto.CirculationFan,
                dto.ExtractorFan, dto.PhUpPump, dto.PhDownPump, dto.NutrientUpPump, dto.DilutionDownPump];
            if (relays.Any(x => !x.HasValue)) return null;
            return relays.Select((value, bit) => value == true ? 1 << bit : 0).Sum();
        }

        private static string[] SensorFaults(TelemetryIngestDto dto)
        {
            var faults = new List<string>();
            if (dto.Ph < 0m || dto.Ph > 14m) faults.Add("ph");
            if (!dto.Ec.HasValue || dto.Ec < 0m || dto.Ec > 15m) faults.Add("ec");
            if (dto.Temperature < -40m || dto.Temperature > 80m) faults.Add("temperature");
            if (dto.Humidity < 0m || dto.Humidity > 100m) faults.Add("humidity");
            if (dto.Light < 0m) faults.Add("light");
            return faults.ToArray();
        }

        [HttpGet("activity/{mac}")]
        public async Task<ActionResult> Activity(string mac, CancellationToken ct = default)
        {
            if (!await DeviceAccess.CanReadDeviceAsync(_database, User, mac, ct)) return NotFound();
            mac = LoraDownlinkQueue.NormalizeMac(mac);
            return Ok(await _database.ActivityLogs.AsNoTracking().Where(x => x.DeviceMac == mac)
                .OrderByDescending(x => x.ReceivedAt).Take(100).ToListAsync(ct));
        }

        [HttpGet("mode-state/{mac}")]
        public async Task<ActionResult> ModeState(string mac, CancellationToken ct = default)
        {
            if (!await DeviceAccess.CanReadDeviceAsync(_database, User, mac, ct)) return NotFound();
            var state = await ModeStateService.GetAsync(_database, mac, ct);
            return state == null ? NotFound() : Ok(state);
        }

        [HttpGet("captures/{mac}")]
        public async Task<ActionResult> Captures(string mac, [FromQuery] int take = 100, CancellationToken ct = default)
        {
            mac = LoraDownlinkQueue.NormalizeMac(mac);
            if (!await DeviceAccess.CanReadDeviceAsync(_database, User, mac, ct))
                return NotFound(new { message = $"No telemetry found for {mac}" });
            var rows = await _database.TelemetryRealtime.AsNoTracking().Where(x => x.DeviceMac == mac)
                .OrderByDescending(x => x.ReceivedAt ?? x.RecordedAt).Take(Math.Clamp(take, 1, 500)).ToListAsync(ct);
            return Ok(rows.Select(x => new { x.SampleSequence, x.PhLevel, x.EcMs, x.TemperatureC,
                x.HumidityPercent, x.LightLux, x.WaterLevelRaw, x.WaterLevelDetected, x.RelayBitmask,
                x.OperatingMode, receivedAt = x.ReceivedAt ?? x.RecordedAt, x.CaptureTimeUncertain,
                capturedAt = x.CaptureTimeUncertain ? (DateTime?)null : x.RecordedAt }));
        }

        // Retrieves the most recent real-time sensor telemetry packet for a specific device MAC address
        [HttpGet("latest/{mac}")]
        public async Task<ActionResult<TelemetryRealtime>> GetLatestByMac(string mac, CancellationToken ct)
        {
            if (!await DeviceAccess.CanReadDeviceAsync(_database, User, mac, ct))
                return NotFound(new { message = $"No telemetry found for {mac}" });

            var latest = await _service.GetLatestByMacAsync(mac, ct);
            if (latest != null)
                latest.PhDisplayFallback = await PhDisplayFallback.GetAsync(_database, mac, latest.PhLevel, latest.RecordedAt, ct);
            return latest is null ? NotFound(new { message = $"No telemetry found for {mac}" }) : Ok(latest);
        }

        // Retrieves the latest telemetry readings for all sensor nodes associated with a structure
        [HttpGet("structure/{structureId:int}/latest")]
        public async Task<ActionResult<IEnumerable<TelemetryRealtime>>> GetLatestByStructure(int structureId, CancellationToken ct)
        {
            if (!await DeviceAccess.CanReadStructureAsync(_database, User, structureId, ct))
                return NotFound(new { message = $"Structure #{structureId} was not found." });

            var readings = (await _service.GetLatestByStructureAsync(structureId, ct)).ToList();
            foreach (var reading in readings)
                reading.PhDisplayFallback = await PhDisplayFallback.GetAsync(_database, reading.DeviceMac, reading.PhLevel, reading.RecordedAt, ct);
            return Ok(readings);
        }

        // Retrieves aggregated daily history metrics over a specified rolling window of days
        [HttpGet("history/{mac}")]
        public async Task<ActionResult<IEnumerable<TelemetryDailyHistory>>> GetHistory(string mac, [FromQuery] int days = 7, CancellationToken ct = default)
        {
            if (!await DeviceAccess.CanReadDeviceAsync(_database, User, mac, ct))
                return NotFound(new { message = $"No telemetry found for {mac}" });

            return Ok(await _service.GetHistoryAsync(mac, Math.Clamp(days, 1, 90), ct));
        }
    }
}
