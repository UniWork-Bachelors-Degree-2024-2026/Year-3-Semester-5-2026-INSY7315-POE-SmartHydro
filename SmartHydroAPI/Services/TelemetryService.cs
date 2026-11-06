using SmartHydroAPI.Data;
using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Patterns.Decorator;
using SmartHydroAPI.Patterns.Observer;
using SmartHydroAPI.Patterns.Observer.Events;
using SmartHydroAPI.Patterns.Repository;
using SmartHydroAPI.Patterns.Strategy;

namespace SmartHydroAPI.Services
{
    // High-throughput telemetry service consuming Repository, Strategy, and Observer patterns
    public class TelemetryService : ITelemetryService
    {
        private readonly ITelemetryRepository _telemetryRepo;
        private readonly IActuatorRepository _actuatorRepo;
        private readonly ITelemetryStrategyFactory _strategyFactory;
        private readonly IEventPublisher _eventPublisher;
        private readonly ApplicationDbContext _database;

        // Injects repository, strategy factory, and event publisher dependencies
        public TelemetryService(
            ITelemetryRepository telemetryRepo,
            IActuatorRepository actuatorRepo,
            ITelemetryStrategyFactory strategyFactory,
            IEventPublisher eventPublisher,
            ApplicationDbContext database)
        {
            _telemetryRepo = telemetryRepo;
            _actuatorRepo = actuatorRepo;
            _strategyFactory = strategyFactory;
            _eventPublisher = eventPublisher;
            _database = database;
        }

        // Ingests live telemetry using resolved strategy, persists reading, dispatches domain event, and piggybacks actuator state
        public Task<ActuatorStateResponseDto> IngestAndSyncAsync(TelemetryIngestDto dto, CancellationToken ct = default) =>
            TelemetryCaptureGate.RunAsync(_database, dto, () => IngestCoreAsync(dto, ct), ct);

        private async Task<ActuatorStateResponseDto> IngestCoreAsync(TelemetryIngestDto dto, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var canonicalMac = LoraDownlinkQueue.NormalizeMac(dto.DeviceMac);
            var compactMac = canonicalMac.Replace(":", "");
            dto.DeviceMac = canonicalMac;

            // 1. Upsert Device and Heartbeat via Repository
            var device = !dto.IsLive ? await _database.Devices.FirstOrDefaultAsync(x =>
                x.MacAddress == canonicalMac || x.MacAddress == compactMac, ct) : null;
            if (device == null)
            {
                device = await _telemetryRepo.UpsertDeviceHeartbeatAsync(dto.DeviceMac, dto.LocalIp, now, ct);
                if (!dto.IsLive) device.LastSeenAt = dto.CaptureTimeUncertain ? DateTime.UnixEpoch : dto.RecordedAtUtc;
            }

            if (dto.IsLive && dto.OperatingMode.HasValue && (!device.ModeReportedAt.HasValue || dto.RecordedAtUtc >= device.ModeReportedAt))
            {
                device.IsOfflineMode = dto.OperatingMode == 2;
                device.ReportedMode = dto.OperatingMode;
                device.ReportedPreferredMode = dto.PreferredMode;
                device.ReportedModeReason = dto.ModeReason;
                device.ModeReportedAt = dto.RecordedAtUtc;
                device.LastTelemetryTransport = dto.Transport;
                var modeRequest = await _database.DeviceCommands.Where(x => x.DeviceMac == canonicalMac && x.Target == "mode")
                    .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
                if (modeRequest != null && device.ModeReportedAt >= modeRequest.CreatedAt)
                {
                    using var payload = System.Text.Json.JsonDocument.Parse(modeRequest.Payload);
                    if (payload.RootElement.TryGetProperty("operatingMode", out var requested) && requested.GetInt32() == dto.OperatingMode)
                        device.ModeRequestConfirmedAt = modeRequest.CreatedAt;
                }
            }

            // 2. Normalize and Clean Ingested Reading via Strategy Pattern
            var strategy = _strategyFactory.GetStrategy(device.DeviceType);
            var reading = strategy.ProcessReading(dto, dto.RecordedAtUtc);
            reading.SampleSequence = dto.Sequence;
            reading.ReceivedAt = dto.ReceivedAtUtc;
            reading.CaptureTimeUncertain = dto.CaptureTimeUncertain;
            reading.OperatingMode = dto.OperatingMode;
            if (dto.MainWaterPump.HasValue || dto.SpraySystem.HasValue || dto.GrowLight.HasValue)
                reading.RelayBitmask = ((dto.MainWaterPump ?? dto.SpraySystem ?? false) ? 1 : 0) |
                    (dto.GrowLight == true ? 2 : 0) | (dto.CirculationFan == true ? 4 : 0) |
                    (dto.ExtractorFan == true ? 8 : 0) | (dto.PhUpPump == true ? 16 : 0) |
                    (dto.PhDownPump == true ? 32 : 0) | (dto.NutrientUpPump == true ? 64 : 0) |
                    (dto.DilutionDownPump == true ? 128 : 0);

            // 3. Persist Reading via Telemetry Repository
            if (!dto.WasDuplicate) await _telemetryRepo.InsertReadingAsync(reading, ct);

            // 4. Publish TelemetryIngestedEvent to in-memory observers (Alert evaluation rule engine)
            if (dto.IsLive) await _eventPublisher.PublishAsync(new TelemetryIngestedEvent(reading, now), ct);

            // 5. Retrieve Actuator State Directive and Dynamic Recipe Thresholds
            var state = await _actuatorRepo.GetOrCreateStateAsync(dto.DeviceMac, ct);

            state = _database.ActuatorStates.Local.FirstOrDefault(x => x.DeviceMac == state.DeviceMac) ?? state;
            if (_database.Entry(state).State == EntityState.Detached) _database.ActuatorStates.Attach(state);
            if (_database.Entry(state).State == EntityState.Unchanged) await _database.Entry(state).ReloadAsync(ct);

            // Synchronize edge-reported relay states if provided by edge hardware (unless a user command is currently in-flight)
            if (dto.IsLive && !LoraDownlinkQueue.IsInCommandGraceWindow(dto.DeviceMac)
                && (!state.ReportedSequence.HasValue || (dto.Sequence.HasValue && dto.Sequence > state.ReportedSequence)))
            {
                bool stateChanged = false;
                if (dto.GrowLight.HasValue && state.GrowLight != dto.GrowLight.Value)
                {
                    state.GrowLight = dto.GrowLight.Value;
                    stateChanged = true;
                }
                if (dto.MainWaterPump.HasValue && state.MainWaterPump != dto.MainWaterPump.Value)
                {
                    state.MainWaterPump = dto.MainWaterPump.Value;
                    stateChanged = true;
                }
                if (dto.SpraySystem.HasValue && state.SpraySystem != dto.SpraySystem.Value)
                {
                    state.SpraySystem = dto.SpraySystem.Value;
                    stateChanged = true;
                }
                if (dto.CirculationFan.HasValue && state.CirculationFan != dto.CirculationFan.Value)
                {
                    state.CirculationFan = dto.CirculationFan.Value;
                    stateChanged = true;
                }
                if (dto.ExtractorFan.HasValue && state.ExtractorFan != dto.ExtractorFan.Value)
                {
                    state.ExtractorFan = dto.ExtractorFan.Value;
                    stateChanged = true;
                }
                if (dto.PhUpPump.HasValue && state.PhUpPump != dto.PhUpPump.Value)
                {
                    state.PhUpPump = dto.PhUpPump.Value;
                    stateChanged = true;
                }
                if (dto.PhDownPump.HasValue && state.PhDownPump != dto.PhDownPump.Value)
                {
                    state.PhDownPump = dto.PhDownPump.Value;
                    stateChanged = true;
                }
                if (dto.NutrientUpPump.HasValue && state.NutrientUpPump != dto.NutrientUpPump.Value)
                {
                    state.NutrientUpPump = dto.NutrientUpPump.Value;
                    stateChanged = true;
                }
                if (dto.DilutionDownPump.HasValue && state.DilutionDownPump != dto.DilutionDownPump.Value)
                {
                    state.DilutionDownPump = dto.DilutionDownPump.Value;
                    stateChanged = true;
                }
                if (stateChanged)
                {
                    state.UpdatedAt = now;
                    await _database.SaveChangesAsync(ct);
                    await _eventPublisher.PublishAsync(new ActuatorStateChangedEvent(dto.DeviceMac, state, "EdgeSync", now), ct);
                }
                // A versioned ACK/report opts a node in. Older fodder firmware keeps its existing ACK path.
                if (state.ReportedSequence.HasValue && dto.Sequence.HasValue) {
                    state.ReportedSequence = dto.Sequence;
                    await _database.SaveChangesAsync(ct);
                }
            }

            decimal? minPh = null, maxPh = null, minEc = null, maxEc = null;
            if (device.StructureId.HasValue)
            {
                var thresholds = await _database.Thresholds
                    .AsNoTracking()
                    .Where(t => t.StructureId == device.StructureId.Value)
                    .ToListAsync(ct);
                var resolved = ThresholdDefaults.Resolve(device.Structure?.StructureType, device.StructureId.Value, thresholds);
                var ph = resolved.FirstOrDefault(t => t.SensorType.Equals("PH", StringComparison.OrdinalIgnoreCase));
                var ec = resolved.FirstOrDefault(t => t.SensorType.Equals("EC", StringComparison.OrdinalIgnoreCase));
                if (ph != null) { minPh = ph.MinValue; maxPh = ph.MaxValue; }
                if (ec != null) { minEc = ec.MinValue; maxEc = ec.MaxValue; }
            }

            // 6. Offer the next pending dose. Only a command-specific callback can
            // confirm completion: an idle relay report also occurs before execution,
            // after a reboot, or when a downlink was lost.

            string? pendingDosingType = null;
            int? pendingDosingDurationMs = null;
            string? pendingDosingCommandId = null;

            if (dto.IsLive && minPh.HasValue && maxPh.HasValue && minEc.HasValue && maxEc.HasValue)
                await new AutomationService(_database).ObserveAsync(dto, minPh.Value, maxPh.Value, minEc.Value, maxEc.Value, ct);

            var pendingDose = await _database.DosingCommands
                .Where(d => (d.DeviceMac == canonicalMac || d.DeviceMac == compactMac) && d.Status == "PENDING"
                    && d.CreatedAt > now.AddSeconds(-60)
                    && !_database.DeviceCommands.Any(c => c.DosingCommandId == d.Id))
                .OrderBy(d => d.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (dto.IsLive && pendingDose != null)
            {
                pendingDosingType = pendingDose.DosingType;
                pendingDosingDurationMs = pendingDose.CalculatedDurationMs;
                pendingDosingCommandId = pendingDose.Id.ToString();

                pendingDose.Status = "DISPATCHED";
                await _database.SaveChangesAsync(ct);
            }

            // 7. Check if user requested an offline transition for this device
            bool dispatchOfflineTransition = dto.IsLive && device.PendingOfflineTransition;
            if (dispatchOfflineTransition)
            {
                // Clear immediately in DB so the instruction is sent only once and does not trap the device in a reboot loop
                device.PendingOfflineTransition = false;
                device.IsOfflineMode = true;
                await _database.SaveChangesAsync(ct);
            }
            else if (dto.IsLive && !dto.OperatingMode.HasValue && device.IsOfflineMode)
            {
                // The device is actively communicating with the API online, so clear offline mode flag
                device.IsOfflineMode = false;
                await _database.SaveChangesAsync(ct);
            }

            return new ActuatorStateResponseDto(
                state.DeviceMac,
                state.GrowLight,
                state.CirculationFan,
                state.MainWaterPump,
                state.ExtractorFan,
                state.PhUpPump,
                state.PhDownPump,
                state.NutrientUpPump,
                state.DilutionDownPump,
                state.SpraySystem,
                state.UpdatedAt,
                minPh,
                maxPh,
                minEc,
                maxEc,
                pendingDosingType,
                pendingDosingDurationMs,
                pendingDosingCommandId,
                dispatchOfflineTransition,
                ReportedSequence: state.ReportedSequence
            );
        }

        // Retrieves the most recent real-time sensor packet for a device
        public Task<TelemetryRealtime?> GetLatestByMacAsync(string mac, CancellationToken ct = default) =>
            _telemetryRepo.GetLatestByMacAsync(mac, ct);

        // Retrieves latest readings for all nodes attached to a structure
        public Task<IEnumerable<TelemetryRealtime>> GetLatestByStructureAsync(int structureId, CancellationToken ct = default) =>
            _telemetryRepo.GetLatestByStructureAsync(structureId, ct);

        // Queries historical daily summary records over a rolling window, formulating missing days on-the-fly from real-time data
        public async Task<IEnumerable<TelemetryDailyHistory>> GetHistoryAsync(string mac, int days = 7, CancellationToken ct = default)
        {
            if (days < 1) days = 7;
            var upperMac = LoraDownlinkQueue.NormalizeMac(mac);
            var compactMac = upperMac.Replace(":", "");
            var history = (await _telemetryRepo.GetDailyHistoryAsync(upperMac, days, ct)).ToList();

            // Use South Africa Standard Time (UTC+2) calendar day boundaries while also covering UTC timestamps
            var sastNow = DateTime.UtcNow.AddHours(2);
            var today = DateOnly.FromDateTime(sastNow);
            var firstDay = today.AddDays(-(days - 1));

            // Ensure every date in the requested window [firstDay .. today] has its daily average formulated,
            // and always refresh today's summary from live TelemetryRealtime readings if new packets arrived.
            for (var d = firstDay; d <= today; d = d.AddDays(1))
            {
                var targetDate = d;
                var existingEntry = history.FirstOrDefault(h => h.SummaryDate == targetDate);
                if (existingEntry == null || targetDate == today)
                {
                    // SAST midnight is 22:00 UTC the previous day; for today, extend window through UtcNow + 2h
                    var dayStartUtc = targetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-2);
                    var dayEndUtc = targetDate == today
                        ? DateTime.UtcNow.AddHours(2)
                        : targetDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

                    var daySummary = await _database.TelemetryRealtime
                        .AsNoTracking()
                        .Where(t => (t.DeviceMac.ToUpper() == upperMac || t.DeviceMac.ToUpper() == compactMac)
                                 && t.RecordedAt >= dayStartUtc && t.RecordedAt <= dayEndUtc)
                        .GroupBy(t => 1)
                        .Select(g => new
                        {
                            Samples = g.Count(),
                            Ph = (g.Where(t => t.PhLevel >= 0m).Average(t => (decimal?)t.PhLevel) ?? -1m),
                            Ec = (g.Where(t => t.EcMs >= 0m).Average(t => (decimal?)t.EcMs) ?? -1m),
                            Light = (g.Where(t => t.LightLux >= 0m).Average(t => (decimal?)t.LightLux) ?? -1m),
                            Temperature = (g.Where(t => t.TemperatureC >= -40m && t.TemperatureC <= 80m).Average(t => (decimal?)t.TemperatureC) ?? -99m),
                            Humidity = (g.Where(t => t.HumidityPercent >= 0m).Average(t => (decimal?)t.HumidityPercent) ?? -1m)
                        })
                        .FirstOrDefaultAsync(ct);

                    if (daySummary != null && daySummary.Samples > 0)
                    {
                        if (existingEntry != null)
                        {
                            history.Remove(existingEntry);
                        }

                        history.Add(new TelemetryDailyHistory
                        {
                            DeviceMac = upperMac,
                            SummaryDate = targetDate,
                            AvgPh = Math.Round(daySummary.Ph, 2),
                            AvgEc = Math.Round(daySummary.Ec, 2),
                            AvgLight = Math.Round(daySummary.Light, 1),
                            AvgTemperatureC = Math.Round(daySummary.Temperature, 2),
                            AvgHumidityPercent = Math.Round(daySummary.Humidity, 2),
                            TotalSamples = daySummary.Samples,
                            CalculatedAt = DateTime.UtcNow
                        });
                    }
                }
            }

            return history
                .Where(h => h.SummaryDate >= firstDay)
                .OrderBy(h => h.SummaryDate)
                .ToList();
        }
    }
}
