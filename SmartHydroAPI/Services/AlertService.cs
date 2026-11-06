using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;
using SmartHydroAPI.Patterns.Factory;
using System.Security.Claims;
using SmartHydroAPI.Extensions;

namespace SmartHydroAPI.Services
{
    // Service managing alarm generation, active alert queries, and resolution workflows
    public class AlertService
    {
        private readonly ApplicationDbContext _database;
        private readonly IAlertFactory _alertFactory;

        // Injects database context and alert factory
        public AlertService(ApplicationDbContext database, IAlertFactory alertFactory)
        {
            _database = database;
            _alertFactory = alertFactory;
        }

        // Narrows alerts to those on structures the caller can see, which for a Worker means the structures in their assigned sites
        private IQueryable<Alert> VisibleAlerts(ClaimsPrincipal user)
        {
            if (user.IsAdmin())
                return _database.Alerts;

            var structureIds = SiteAccess.VisibleStructureIds(_database, user);
            return _database.Alerts.Where(a => structureIds.Contains(a.StructureId));
        }

        // Narrows alerts to those the caller may delete or restore; Workers can read and resolve alerts but not remove them
        private IQueryable<Alert> ManageableAlerts(ClaimsPrincipal user)
        {
            if (user.IsAdmin())
                return _database.Alerts;

            var structureIds = SiteAccess.ManageableStructureIds(_database, user);
            return _database.Alerts.Where(a => structureIds.Contains(a.StructureId));
        }

        // Returns count of open/unresolved alerts for badge notifications
        public async Task<int> GetUnresolvedCountAsync(int? structureId = null, ClaimsPrincipal? user = null, CancellationToken ct = default, Guid? siteId = null)
        {
            var alerts = _database.Alerts.AsQueryable();
            if (user is not null)
                alerts = VisibleAlerts(user);

            var query = alerts.AsNoTracking().Where(a => !a.IsResolved && !a.IsDeleted);
            if (structureId.HasValue)
                query = query.Where(a => a.StructureId == structureId.Value);

            if (siteId.HasValue)
                query = query.Where(a => a.Structure!.SiteId == siteId.Value);

            return await query.CountAsync(ct);
        }

        // Fetches a single alert by ID
        public async Task<AlertResponseDto?> GetByIdAsync(long id, ClaimsPrincipal user, CancellationToken ct = default)
        {
            return await VisibleAlerts(user)
                .AsNoTracking()
                .Include(a => a.Structure)
                .Include(a => a.Device)
                .Where(a => a.Id == id && !a.IsDeleted)
                .Select(a => new AlertResponseDto
                {
                    Id = a.Id,
                    StructureId = a.StructureId,
                    StructureName = a.Structure!.Name,
                    DeviceMac = a.DeviceMac,
                    DeviceName = a.Device!.DeviceName,
                    Component = a.Component,
                    Severity = a.Severity,
                    Value = a.Value,
                    Detail = a.Detail,
                    OccurredAt = a.OccurredAt,
                    IsResolved = a.IsResolved,
                    ClearedBy = a.ClearedBy,
                    ResolvedAt = a.ResolvedAt
                })
                .FirstOrDefaultAsync(ct);
        }

        // Paginated query for alerts with structure and open-only filtering
        public async Task<PagedResultDto<AlertResponseDto>> GetPagedAlertsAsync(int? structureId, bool unresolvedOnly, int page, int pageSize, ClaimsPrincipal user, CancellationToken ct = default, Guid? siteId = null)
        {
            var query = VisibleAlerts(user).AsNoTracking().Include(a => a.Structure).Include(a => a.Device).Where(a => !a.IsDeleted);

            if (structureId.HasValue) query = query.Where(a => a.StructureId == structureId.Value);
            if (siteId.HasValue) query = query.Where(a => a.Structure!.SiteId == siteId.Value);
            if (unresolvedOnly) query = query.Where(a => !a.IsResolved);

            var totalCount = await query.CountAsync(ct);
            var safePage = Math.Max(1, page);
            var safePageSize = Math.Clamp(pageSize, 1, 100);

            var items = await query
                .OrderByDescending(a => a.OccurredAt)
                .Skip((safePage - 1) * safePageSize)
                .Take(safePageSize)
                .Select(a => new AlertResponseDto
                {
                    Id = a.Id,
                    StructureId = a.StructureId,
                    StructureName = a.Structure!.Name,
                    DeviceMac = a.DeviceMac,
                    DeviceName = a.Device!.DeviceName,
                    Component = a.Component,
                    Severity = a.Severity,
                    Value = a.Value,
                    Detail = a.Detail,
                    OccurredAt = a.OccurredAt,
                    IsResolved = a.IsResolved,
                    ClearedBy = a.ClearedBy,
                    ResolvedAt = a.ResolvedAt
                })
                .ToListAsync(ct);

            var totalPages = (int)Math.Ceiling(totalCount / (double)safePageSize);
            return new PagedResultDto<AlertResponseDto>(items, totalCount, safePage, safePageSize, totalPages);
        }

        // Lists the soft-deleted alerts the caller can restore, newest deletion first, optionally narrowed to one site
        public async Task<IEnumerable<AlertResponseDto>> GetDeletedAlertsAsync(Guid? siteId, ClaimsPrincipal user, CancellationToken ct = default)
        {
            var query = ManageableAlerts(user).AsNoTracking().Include(a => a.Structure).Include(a => a.Device).Where(a => a.IsDeleted);

            if (siteId.HasValue) query = query.Where(a => a.Structure!.SiteId == siteId.Value);

            return await query
                .OrderByDescending(a => a.DeletedAt)
                .Take(100)
                .Select(a => new AlertResponseDto
                {
                    Id = a.Id,
                    StructureId = a.StructureId,
                    StructureName = a.Structure!.Name,
                    DeviceMac = a.DeviceMac,
                    DeviceName = a.Device!.DeviceName,
                    Component = a.Component,
                    Severity = a.Severity,
                    Value = a.Value,
                    Detail = a.Detail,
                    OccurredAt = a.OccurredAt,
                    IsResolved = a.IsResolved,
                    ClearedBy = a.ClearedBy,
                    ResolvedAt = a.ResolvedAt
                })
                .ToListAsync(ct);
        }

        // Queries alerts (legacy non-paginated)
        public async Task<IEnumerable<AlertResponseDto>> GetAlertsAsync(int? structureId, bool unresolvedOnly = true, CancellationToken ct = default)
        {
            var query = _database.Alerts.AsNoTracking().Include(a => a.Structure).Include(a => a.Device).Where(a => !a.IsDeleted).AsQueryable();

            if (structureId.HasValue) query = query.Where(a => a.StructureId == structureId.Value);
            if (unresolvedOnly) query = query.Where(a => !a.IsResolved);

            return await query
                .OrderByDescending(a => a.OccurredAt)
                .Select(a => new AlertResponseDto
                {
                    Id = a.Id,
                    StructureId = a.StructureId,
                    StructureName = a.Structure!.Name,
                    DeviceMac = a.DeviceMac,
                    DeviceName = a.Device!.DeviceName,
                    Component = a.Component,
                    Severity = a.Severity,
                    Value = a.Value,
                    Detail = a.Detail,
                    OccurredAt = a.OccurredAt,
                    IsResolved = a.IsResolved,
                    ClearedBy = a.ClearedBy,
                    ResolvedAt = a.ResolvedAt
                })
                .ToListAsync(ct);
        }

        // Inserts a new alert constructed by AlertFactory
        public async Task<Alert> CreateAsync(CreateAlertDto dto, CancellationToken ct = default)
        {
            var alert = _alertFactory.CreateAlert(dto);

            _database.Alerts.Add(alert);
            await _database.SaveChangesAsync(ct);
            return alert;
        }

        // Atomically marks an alert as resolved in PostgreSQL using ExecuteUpdateAsync
        public async Task<bool> ResolveAsync(long id, ResolveAlertDto dto, ClaimsPrincipal user, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            return await VisibleAlerts(user)
                .Where(a => a.Id == id && !a.IsDeleted)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.IsResolved, true)
                    .SetProperty(a => a.ClearedBy, dto.ClearedBy)
                    .SetProperty(a => a.ResolvedAt, now), ct) > 0;
        }

        // Soft-deletes an alert on a structure the caller manages
        public async Task<bool> DeleteAsync(long id, ClaimsPrincipal user, CancellationToken ct = default)
        {
            var alert = await ManageableAlerts(user)
                .FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted, ct);
            if (alert is null) return false;

            alert.IsDeleted = true;
            alert.DeletedAt = DateTime.UtcNow;
            alert.DeletedByEmail = user.GetEmail();
            await _database.SaveChangesAsync(ct);
            return true;
        }

        // Restores a soft-deleted alert on a structure the caller manages
        public async Task<bool> RestoreAsync(long id, ClaimsPrincipal user, CancellationToken ct = default)
        {
            var alert = await ManageableAlerts(user)
                .FirstOrDefaultAsync(a => a.Id == id && a.IsDeleted, ct);
            if (alert is null) return false;

            alert.IsDeleted = false;
            alert.DeletedAt = null;
            alert.DeletedByEmail = null;
            await _database.SaveChangesAsync(ct);
            return true;
        }

        // Permanently removes a soft-deleted alert (Admin only)
        public async Task<bool> PurgeAsync(long id, CancellationToken ct = default)
        {
            var alert = await _database.Alerts.FirstOrDefaultAsync(a => a.Id == id && a.IsDeleted, ct);
            if (alert is null) return false;

            _database.Alerts.Remove(alert);
            await _database.SaveChangesAsync(ct);
            return true;
        }
    }
}