using Microsoft.AspNetCore.SignalR;
using SmartHydroAPI.Data;
using SmartHydroAPI.Hubs;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Patterns.Decorator;
using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Patterns.Repository;

namespace SmartHydroAPI.Services;

public static class CommandAcknowledgements
{
    private static readonly SemaphoreSlim TowerReceipts = new(1, 1);
    public static async Task<bool> AcceptAsync(ApplicationDbContext database, IActuatorService service,
        IHubContext<ActuatorHub> hub, LoraCompactTelemetryDto ack, CancellationToken ct = default)
    {
        if (ack.CommandId is not (>= 1 and <= 65535) || ack.CommandOk is not (0 or 1) || ack.RelayBitmask is not (>= 0 and <= 255)) return false;
        if (ack.ReportedSequence.HasValue && (ack.ReportedSequence <= 0 || ack.ReportedSequence >= (1L << 62))) return false;
        await TowerReceipts.WaitAsync(ct);
        try
        {
            var mac = LoraDownlinkQueue.NormalizeMac(ack.DeviceMac);
            var previous = await database.DeviceCommands.AsNoTracking()
                .Where(x => x.DeviceMac == mac && x.CommandId == ack.CommandId)
                .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            if (!ack.ReportedSequence.HasValue && previous?.Status == "ACKED" && previous.Succeeded == (ack.CommandOk == 1))
            {
                bool identical = previous.RelayBitmask == ack.RelayBitmask && previous.Reason == ack.CommandReason;
                bool finishedDose = previous.DosingCommandId is Guid id && await database.DosingCommands.AsNoTracking()
                    .AnyAsync(x => x.Id == id && (x.Status == "COMPLETED" || x.Status == "FAILED"), ct);
                // Confirm receipt again without replaying an old GPIO snapshot into the live state.
                bool hasState = await database.ActuatorStates.AsNoTracking().AnyAsync(x => x.DeviceMac == mac, ct);
                if ((identical && hasState) || (finishedDose && ack.CommandPhase == "accepted")) return true;
            }
            return await AcceptCoreAsync(database, service, hub, ack, ct);
        }
        finally { TowerReceipts.Release(); }
    }

    private static async Task<bool> AcceptCoreAsync(ApplicationDbContext database, IActuatorService service,
        IHubContext<ActuatorHub> hub, LoraCompactTelemetryDto ack, CancellationToken ct)
    {
        if (ack.CommandId == null || ack.CommandOk == null || ack.RelayBitmask == null) return false;
        var mask = ack.RelayBitmask.Value;
        var command = await new DeviceCommandQueue(database).AcknowledgeAsync(ack.DeviceMac,
            ack.CommandId.Value, ack.CommandOk.Value, ack.RelayBitmask.Value,
            ack.CommandReason, ack.CommandPhase, ct, async receipt =>
        {
            // Keep the receipt, but let fresh telemetry reconcile an older command's snapshot.
            if (!ack.ReportedSequence.HasValue && await database.DeviceCommands.AnyAsync(x => x.DeviceMac == receipt.DeviceMac
                && x.Id > receipt.Id && x.Status == "ACKED", ct)) return;
            await new ActuatorRepository(database).StageStateAndLogHistoryAsync(new ActuatorCommandDto
            {
                DeviceMac = receipt.DeviceMac, ChangedBy = $"Node ACK {receipt.CommandId}", ReportedSequence = ack.ReportedSequence,
                MainWaterPump = receipt.SystemType == "hydroponics" ? (mask & 1) != 0 : null,
                SpraySystem = receipt.SystemType == "fodder" ? (mask & 1) != 0 : null,
                GrowLight = (mask & 2) != 0, CirculationFan = (mask & 4) != 0,
                ExtractorFan = (mask & 8) != 0, PhUpPump = (mask & 16) != 0,
                PhDownPump = (mask & 32) != 0, NutrientUpPump = (mask & 64) != 0,
                DilutionDownPump = (mask & 128) != 0
            }, false, ct);
        });
        if (command == null) return false;
        var state = await service.RefreshStateAsync(command.DeviceMac, ct);
        await hub.Clients.Group(LoraDownlinkQueue.NormalizeMac(command.DeviceMac).Replace(":", "")).SendAsync("ReceiveCommandAck", new
        {
            deviceMac = command.DeviceMac, commandId = command.CommandId, requestId = command.RequestId,
            ok = command.Succeeded, reason = command.Reason, relayBitmask = mask,
            phase = ack.CommandPhase, status = command.Status, doseId = command.DosingCommandId, state
        }, ct);
        await hub.Clients.Group(LoraDownlinkQueue.NormalizeMac(command.DeviceMac).Replace(":", "")).SendAsync("OnActuatorStateChanged", state, ct);
        // Mode queries must not delay delivery of the already-stored GPIO outcome.
        await hub.Clients.Group(LoraDownlinkQueue.NormalizeMac(command.DeviceMac).Replace(":", "")).SendAsync("ReceiveModeState", await ModeStateService.GetAsync(database, command.DeviceMac, ct), ct);
        return true;
    }
}
