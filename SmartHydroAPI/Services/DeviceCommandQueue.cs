using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Services;

public sealed class DeviceCommandQueue(ApplicationDbContext database)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private async Task<IDbContextTransaction?> BeginAsync(CancellationToken ct)
    {
        if (!database.Database.IsRelational()) return null;
        var transaction = await database.Database.BeginTransactionAsync(ct);
        try
        {
            // Also serialise writers on other API instances.
            await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7315003)", ct);
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }

    public static Dictionary<string, object> WirePayload(DeviceCommand command)
    {
        var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(command.Payload)!;
        payload["m"] = command.DeviceMac.Replace(":", "");
        payload["cid"] = command.CommandId;
        payload["sys"] = command.SystemType;
        payload["ttl"] = Math.Max(0, (int)(command.ExpiresAt - DateTime.UtcNow).TotalMilliseconds);
        payload["exp"] = new DateTimeOffset(command.ExpiresAt).ToUnixTimeSeconds();
        return payload;
    }

    public Task<DeviceCommand> EnqueueAsync(string mac, string target,
        Dictionary<string, object> payload, string source, Guid? doseId = null, CancellationToken ct = default)
        => EnqueueWithDoseAsync(mac, target, payload, source, doseId, null, ct);

    public Task<DeviceCommand> EnqueueDoseAsync(DosingCommand dose, CancellationToken ct = default, bool manualPhOverride = false, string? signingKey = null)
    {
        Dictionary<string, object> payload = new()
        {
            ["dt"] = dose.DosingType, ["dm"] = dose.CalculatedDurationMs,
            ["doseId"] = dose.Id.ToString(), ["volumeMl"] = dose.TargetVolumeMl,
            ["msPerMl"] = 800, ["volumeEstimated"] = true
        };
        if (manualPhOverride) payload["po"] = true;
        return EnqueueWithDoseAsync(dose.DeviceMac, dose.DosingType.StartsWith("PH_") ? "ph" : "ec",
            payload, dose.IssuedBy ?? "User-MVC", dose.Id, dose, ct, signingKey);
    }

    private Task<DeviceCommand> EnqueueWithDoseAsync(string mac, string target,
        Dictionary<string, object> payload, string source, Guid? doseId, DosingCommand? draftDose, CancellationToken ct, string? signingKey = null)
    {
        var requestId = Guid.NewGuid();
        var attempt = 0;
        return database.Database.CreateExecutionStrategy().ExecuteAsync(() =>
        {
            if (attempt++ > 0) database.ChangeTracker.Clear();
            return EnqueueCoreAsync(requestId, mac, target, payload, source, doseId, draftDose, ct, signingKey);
        });
    }

    private async Task<DeviceCommand> EnqueueCoreAsync(Guid requestId, string mac, string target,
        Dictionary<string, object> payload, string source, Guid? doseId, DosingCommand? draftDose, CancellationToken ct, string? signingKey)
    {
        mac = LoraDownlinkQueue.NormalizeMac(mac);
        await Gate.WaitAsync(ct);
        try
        {
            await using var transaction = await BeginAsync(ct);
            var existing = await database.DeviceCommands.AsNoTracking().FirstOrDefaultAsync(x => x.RequestId == requestId, ct);
            if (existing != null) return existing;
            var now = DateTime.UtcNow;
            if (draftDose != null) {
                await ReconcileDosesCoreAsync(mac, ct);
                var compactMac = mac.Replace(":", "");
                if (await database.DosingCommands.AnyAsync(x => (x.DeviceMac == mac || x.DeviceMac == compactMac) && x.Status == "UNCONFIRMED", ct)) {
                    await database.SaveChangesAsync(ct);
                    if (transaction != null) await transaction.CommitAsync(ct);
                    throw new ArgumentException("A previous dose has no confirmed outcome. Check the controller before requesting another dose.");
                }
                database.DosingCommands.Add(draftDose);
            }
            var compact = mac.Replace(":", "");
            var device = await database.Devices.AsNoTracking()
                .FirstOrDefaultAsync(x => x.MacAddress == mac || x.MacAddress == compact, ct)
                ?? throw new ArgumentException("Device not found.");
            var structureType = device.StructureType;
            if (device.StructureId.HasValue)
                structureType = await database.Structures.Where(x => x.Id == device.StructureId)
                    .Select(x => x.StructureType).FirstOrDefaultAsync(ct) ?? structureType;
            var older = await database.DeviceCommands.Where(x => x.DeviceMac == mac && x.Target == target
                && (x.Status == "QUEUED" || x.Status == "SENT")).ToListAsync(ct);
            // Separate dose requests must never be merged or silently replaced.
            if (doseId == null)
                foreach (var item in older)
                {
                    item.Status = "SUPERSEDED";
                    if (item.DosingCommandId is Guid oldDoseId)
                    {
                        var oldDose = await database.DosingCommands.FindAsync([oldDoseId], ct);
                        if (oldDose != null && oldDose.Status is not ("COMPLETED" or "FAILED")) oldDose.Status = "FAILED";
                    }
                }
            var command = new DeviceCommand
            {
                RequestId = requestId, DeviceMac = mac, Target = target, Payload = JsonSerializer.Serialize(payload),
                CommandText = payload.TryGetValue("cmd", out var text) ? text.ToString()! : target,
                SystemType = structureType?.Contains("FODDER", StringComparison.OrdinalIgnoreCase) == true
                    ? "fodder" : "hydroponics",
                Source = source, DosingCommandId = doseId, CreatedAt = now, ExpiresAt = now.AddSeconds(target == "mode" ? 180 : 60)
            };
            database.DeviceCommands.Add(command);
            await database.SaveChangesAsync(ct);
            var previousCid = await database.DeviceCommands.Where(x => x.Id != command.Id && x.DeviceMac == mac)
                .OrderByDescending(x => x.Id).Select(x => (int?)x.CommandId).FirstOrDefaultAsync(ct);
            command.CommandId = (previousCid ?? 0) % 65535 + 1;
            if (payload.TryGetValue("po", out var requestedOverride) && requestedOverride is true)
            {
                if (signingKey?.Length != 64 || !signingKey.All(Uri.IsHexDigit))
                    throw new ArgumentException("Manual override signing is unavailable.");
                var proofText = FormattableString.Invariant($"po1|{mac.Replace(":", "")}|{command.CommandId}|{new DateTimeOffset(command.ExpiresAt).ToUnixTimeSeconds()}|{draftDose!.DosingType}|{draftDose.CalculatedDurationMs}|{draftDose.Id}");
                var proof = System.Security.Cryptography.HMACSHA256.HashData(Convert.FromHexString(signingKey), System.Text.Encoding.UTF8.GetBytes(proofText));
                payload["ps"] = Convert.ToBase64String(proof, 0, 16).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                command.Payload = JsonSerializer.Serialize(payload);
            }
            if (await database.DeviceCommands.AnyAsync(x => x.Id != command.Id && x.DeviceMac == mac
                && x.CommandId == command.CommandId && x.ExpiresAt > now
                && (x.Status == "QUEUED" || x.Status == "SENT"), ct))
                throw new InvalidOperationException("Command ID is still in use.");
            await ActivityLogService.CommandAsync(database, command, "QUEUED", ct);
            await database.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            HomeGatewayConnections.Signal(mac);
            return command;
        }
        finally { Gate.Release(); }
    }

    public Task<List<Dictionary<string, object>>> ClaimAsync(string? mac = null, CancellationToken ct = default, IReadOnlyCollection<string>? deviceMacs = null)
    {
        int attempt = 0;
        return database.Database.CreateExecutionStrategy().ExecuteAsync(() =>
        {
            if (attempt++ > 0) database.ChangeTracker.Clear();
            return ClaimCoreAsync(mac, ct, deviceMacs);
        });
    }

    private async Task<List<Dictionary<string, object>>> ClaimCoreAsync(string? mac, CancellationToken ct, IReadOnlyCollection<string>? deviceMacs)
    {
        await Gate.WaitAsync(ct);
        try
        {
            await using var transaction = await BeginAsync(ct);
            var now = DateTime.UtcNow;
            await ExpireAsync(now, mac, ct);
            var retryBefore = now.AddSeconds(-12);
            var query = database.DeviceCommands.Where(x => x.ExpiresAt > now && x.CommandId > 0
                && (x.Status == "QUEUED" || x.Status == "SENT"));
            if (mac != null) { var canonical = LoraDownlinkQueue.NormalizeMac(mac); query = query.Where(x => x.DeviceMac == canonical); }
            else if (HomeGatewayConnections.ReservedMac is string pilotMac)
                query = query.Where(x => x.DeviceMac != pilotMac);
            if (deviceMacs != null) {
                var allowed = deviceMacs.Select(LoraDownlinkQueue.NormalizeMac).ToArray();
                query = query.Where(x => allowed.Contains(x.DeviceMac));
            }
            var ready = await query.OrderBy(x => x.Target == "mode" ? 0 : 1).ThenBy(x => x.Id).ToListAsync(ct);
            // One outstanding RF command per node; other targets stay in the database.
            var claimed = ready.GroupBy(x => x.DeviceMac).Select(x => x.First())
                .Where(x => x.Status == "QUEUED" || x.SentAt <= retryBefore).Take(2).ToList();
            foreach (var command in claimed)
            {
                if (command.Status == "SENT") command.RetryCount++;
                command.Status = "SENT";
                command.SentAt = now;
            }
            await database.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            return claimed.Select(WirePayload).ToList();
        }
        finally { Gate.Release(); }
    }

    private async Task ExpireAsync(DateTime now, string? mac, CancellationToken ct)
    {
        var query = database.DeviceCommands.Where(x => x.ExpiresAt <= now
            && (x.Status == "QUEUED" || x.Status == "SENT"));
        if (mac != null) { var canonical = LoraDownlinkQueue.NormalizeMac(mac); query = query.Where(x => x.DeviceMac == canonical); }
        foreach (var command in await query.ToListAsync(ct))
        {
            bool sent = command.Status == "SENT" || command.SentAt.HasValue;
            command.Status = "EXPIRED";
            command.Reason = "delivery_timeout";
            await ActivityLogService.CommandAsync(database, command, sent ? "UNCONFIRMED" : "TIMED_OUT", ct);
            if (command.DosingCommandId is Guid id)
            {
                var dose = await database.DosingCommands.FindAsync([id], ct);
                if (dose != null && dose.Status is not ("COMPLETED" or "FAILED"))
                    dose.Status = sent ? "UNCONFIRMED" : "FAILED";
            }
        }
    }

    private Task<T> UpdateDosesAsync<T>(Func<Task<T>> update, CancellationToken ct)
    {
        int attempt = 0;
        return database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await Gate.WaitAsync(ct);
            try
            {
                if (attempt++ > 0) database.ChangeTracker.Clear();
                await using var transaction = await BeginAsync(ct);
                var result = await update();
                await database.SaveChangesAsync(ct);
                if (transaction != null) await transaction.CommitAsync(ct);
                return result;
            }
            finally { Gate.Release(); }
        });
    }

    public Task ReconcileDosesAsync(string mac, CancellationToken ct = default)
        => UpdateDosesAsync(() => ReconcileDosesCoreAsync(mac, ct), ct);

    public Task ReviewUnknownDoseAsync(string mac, Guid doseId, string reviewedBy, string notes,
        bool pumpsCheckedOff, bool reservoirChecked, CancellationToken ct = default) => UpdateDosesAsync(async () =>
    {
        mac = LoraDownlinkQueue.NormalizeMac(mac);
        if (!pumpsCheckedOff || !reservoirChecked || string.IsNullOrWhiteSpace(notes) || notes.Trim().Length < 10 || notes.Length > 500)
            throw new ArgumentException("Check that all dosing pumps are off, check the reservoir and enter a note of 10 to 500 characters.");
        await ReconcileDosesCoreAsync(mac, ct);
        var dose = await database.DosingCommands.SingleOrDefaultAsync(x => x.Id == doseId &&
            (x.DeviceMac == mac || x.DeviceMac == mac.Replace(":", "")), ct)
            ?? throw new ArgumentException("That dose was not found for this controller.");
        var command = await database.DeviceCommands.Where(x => x.DosingCommandId == doseId).OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        if (dose.Status == "FAILED" && command?.Reason == "operator_reviewed_unknown") return true;
        if (dose.Status != "UNCONFIRMED") throw new ArgumentException("Only a dose with an unconfirmed outcome can be reviewed.");
        // Allow expiry, the full possible run and the longer pH waiting period before manual recovery.
        var lastPossibleStart = command?.ExpiresAt ?? dose.CreatedAt.AddSeconds(60);
        if (command?.AckedAt is DateTime acked && acked > lastPossibleStart) lastPossibleStart = acked;
        if (DateTime.UtcNow < lastPossibleStart.AddMilliseconds(Math.Max(0, dose.CalculatedDurationMs)).AddMinutes(15))
            throw new ArgumentException("The earlier request is still within its possible run or waiting period. Recovery is not available yet.");
        if (command == null) throw new ArgumentException("This older dose has no linked command. It needs an administrator to investigate its history.");
        dose.Status = "FAILED";
        // Do not invent an execution timestamp, acknowledgement or relay state.
        command.Reason = "operator_reviewed_unknown";
        database.ActivityLogs.Add(new() { DeviceMac = mac, EventKey = $"dose-review:{doseId}",
            Source = "User - MVC", Action = dose.DosingType, Status = "REVIEWED", CommandId = command.CommandId,
            RecordedAt = DateTime.UtcNow, Details = "Earlier dose outcome remains unknown. Operator confirmed all dosing pumps off and reservoir checked. " +
                "Reviewed by " + reviewedBy + ". Note: " + notes.Trim() });
        return true;
    }, ct);

    public Task RecordPushAttemptAsync(long id, CancellationToken ct = default) => UpdateDosesAsync(async () =>
    {
        var command = await database.DeviceCommands.FindAsync([id], ct);
        if (command != null && (command.Status is "QUEUED" or "SENT")) command.SentAt ??= DateTime.UtcNow;
        return true;
    }, ct);

    private async Task<bool> ReconcileDosesCoreAsync(string mac, CancellationToken ct)
    {
        mac = LoraDownlinkQueue.NormalizeMac(mac);
        var compact = mac.Replace(":", "");
        var now = DateTime.UtcNow;
        await ExpireAsync(now, mac, ct);
        var waiting = await database.DosingCommands.Where(x => (x.DeviceMac == mac || x.DeviceMac == compact)
            && (x.Status == "PENDING" || x.Status == "DISPATCHED")).ToListAsync(ct);
        var linked = await database.DeviceCommands.Where(x => x.DeviceMac == mac && x.DosingCommandId != null)
            .OrderBy(x => x.Id).ToListAsync(ct);
        foreach (var dose in waiting)
        {
            var command = linked.LastOrDefault(x => x.DosingCommandId == dose.Id);
            var deadline = (command?.AckedAt ?? dose.CreatedAt)
                .AddMilliseconds(Math.Max(0, dose.CalculatedDurationMs)).AddSeconds(60);
            if (command == null && now > deadline)
                dose.Status = "UNCONFIRMED";
            else if (command?.Status == "ACKED" && command.Succeeded == true && now > deadline)
            {
                dose.Status = "UNCONFIRMED";
                command.Reason = "completion_unconfirmed";
                await ActivityLogService.CommandAsync(database, command, "UNCONFIRMED", ct);
            }
        }
        return true;
    }

    public Task<bool> CompleteDoseAsync(Guid id, string status, DateTime? executedAt, CancellationToken ct = default) =>
        UpdateDosesAsync(async () =>
        {
            status = status.Trim().ToUpperInvariant();
            if (status is not ("COMPLETED" or "FAILED")) throw new ArgumentException("Unknown dose completion status.");
            var dose = await database.DosingCommands.FindAsync([id], ct);
            if (dose == null) return false;
            if (dose.Status is "COMPLETED" or "FAILED") return dose.Status == status;
            dose.Status = status;
            dose.ExecutedAt = executedAt ?? DateTime.UtcNow;
            var command = await database.DeviceCommands.SingleOrDefaultAsync(x => x.DosingCommandId == id, ct);
            if (command != null)
            {
                command.Status = "ACKED";
                command.AckedAt ??= DateTime.UtcNow;
                command.Succeeded ??= status == "COMPLETED";
                command.Reason = status == "COMPLETED" ? "timer_finished" : "device_failed";
                await ActivityLogService.CommandAsync(database, command, status, ct);
            }
            return true;
        }, ct);

    public Task<DeviceCommand?> AcknowledgeAsync(string mac, int cid, int ok, int relayMask,
        string? reason, string? phase, CancellationToken ct = default,
        Func<DeviceCommand, Task>? stageReportedState = null)
    {
        int attempt = 0;
        return database.Database.CreateExecutionStrategy().ExecuteAsync(() =>
        {
            if (attempt++ > 0) database.ChangeTracker.Clear();
            return AcknowledgeCoreAsync(mac, cid, ok, relayMask, reason, phase, ct, stageReportedState);
        });
    }

    private async Task<DeviceCommand?> AcknowledgeCoreAsync(string mac, int cid, int ok, int relayMask,
        string? reason, string? phase, CancellationToken ct, Func<DeviceCommand, Task>? stageReportedState)
    {
        if (cid is < 1 or > 65535 || ok is < 0 or > 1 || relayMask is < 0 or > 255) return null;
        mac = LoraDownlinkQueue.NormalizeMac(mac);
        await Gate.WaitAsync(ct);
        try
        {
            await using var transaction = await BeginAsync(ct);
            var command = await database.DeviceCommands.Where(x => x.DeviceMac == mac && x.CommandId == cid)
                .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            if (command == null) return null;
            bool wasExpired = command.Status == "EXPIRED";
            bool lateDoseReceipt = command.DosingCommandId.HasValue &&
                (ok == 0 || (ok == 1 && (phase is "completed" or "cancelled")));
            if (command.Status == "SUPERSEDED" || (!lateDoseReceipt && (command.Status == "EXPIRED"
                || (command.Status != "ACKED" && command.ExpiresAt <= DateTime.UtcNow)))) {
                if (stageReportedState == null) return null;
                await stageReportedState(command);
                await database.SaveChangesAsync(ct);
                if (transaction != null) await transaction.CommitAsync(ct);
                return command;
            }
            if (command.Status == "ACKED" && (command.Succeeded != (ok == 1))) return null;
            if (stageReportedState != null && command.Status == "ACKED")
            {
                bool identical = command.RelayBitmask == relayMask && command.Reason == reason;
                bool finishedDose = command.DosingCommandId is Guid finishedId && await database.DosingCommands
                    .AnyAsync(x => x.Id == finishedId && (x.Status == "COMPLETED" || x.Status == "FAILED"), ct);
                // Check under the database lock too: another API instance may have stored it.
                bool hasState = await database.ActuatorStates.AnyAsync(x => x.DeviceMac == mac, ct);
                if ((identical && hasState) || (finishedDose && phase == "accepted")) {
                    await stageReportedState(command);
                    await database.SaveChangesAsync(ct);
                    if (transaction != null) await transaction.CommitAsync(ct);
                    return command;
                }
            }
            command.Status = "ACKED";
            command.AckedAt ??= DateTime.UtcNow;
            command.Succeeded = ok == 1;
            command.RelayBitmask = relayMask;
            command.Reason = reason is { Length: > 100 } ? reason[..100] : reason;
            if (command.DosingCommandId is Guid doseId)
            {
                var dose = await database.DosingCommands.FindAsync([doseId], ct);
                if (dose != null && dose.Status != "COMPLETED" && (dose.Status != "FAILED" || (wasExpired && lateDoseReceipt)))
                {
                    if (dose.Status != "UNCONFIRMED" || ok == 0 || phase is "completed" or "cancelled")
                        dose.Status = ok == 0 || phase == "cancelled" ? "FAILED" : phase == "completed" ? "COMPLETED" : "DISPATCHED";
                    if (dose.Status is "COMPLETED" or "FAILED") dose.ExecutedAt = DateTime.UtcNow;
                }
            }
            await ActivityLogService.CommandAsync(database, command, ok == 0 ? "REFUSED" : phase ?? "accepted", ct);
            // Save the receipt and its GPIO report together, before replying to the gateway.
            if (stageReportedState != null) await stageReportedState(command);
            await database.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            return command;
        }
        finally { Gate.Release(); }
    }
}
