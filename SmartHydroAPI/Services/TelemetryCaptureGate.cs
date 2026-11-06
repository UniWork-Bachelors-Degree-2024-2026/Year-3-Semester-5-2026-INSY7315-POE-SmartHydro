using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Services;

public static class TelemetryCaptureGate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static Task<T> RunAsync<T>(ApplicationDbContext db, TelemetryIngestDto dto,
        Func<Task<T>> ingest, CancellationToken ct)
    {
        dto.PrepareCapture();
        int attempt = 0;
        return db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await Gate.WaitAsync(ct);
            try
            {
                if (attempt++ > 0) db.ChangeTracker.Clear();
                await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
                if (transaction != null) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7315003)", ct);
                var mac = LoraDownlinkQueue.NormalizeMac(dto.DeviceMac);
                dto.WasDuplicate = dto.Sequence.HasValue && (await db.TelemetryReceipts.AnyAsync(x => x.DeviceMac == mac && x.Sequence == dto.Sequence, ct)
                    || await db.TelemetryRealtime.AnyAsync(x => x.DeviceMac == mac && x.SampleSequence == dto.Sequence, ct));
                dto.IsOutOfOrder = await db.TelemetryRealtime.AnyAsync(x => x.DeviceMac == mac &&
                    !x.CaptureTimeUncertain && x.RecordedAt > dto.RecordedAtUtc, ct)
                    || await db.Devices.AnyAsync(x => (x.MacAddress == mac || x.MacAddress == mac.Replace(":", ""))
                        && x.ModeReportedAt > dto.RecordedAtUtc, ct);
                if (dto.Sequence.HasValue && !dto.WasDuplicate)
                {
                    db.TelemetryReceipts.Add(new TelemetryReceipt { DeviceMac = mac, Sequence = dto.Sequence.Value,
                        ReceivedAt = dto.ReceivedAtUtc!.Value });
                }
                var result = await ingest();
                var cutoff = dto.ReceivedAtUtc!.Value.AddMinutes(-30);
                var expired = await db.TelemetryReceipts.Where(x => x.ReceivedAt < cutoff).ToListAsync(ct);
                db.TelemetryReceipts.RemoveRange(expired);
                await db.SaveChangesAsync(ct);
                if (transaction != null) await transaction.CommitAsync(ct);
                return result;
            }
            finally { Gate.Release(); }
        });
    }
}
