using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Data;
using SmartHydroAPI.Extensions;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Services
{
    public class SiteService
    {
        // Structures deleted together with a site share its deletion time, so restoring the site brings back only those
        private static readonly TimeSpan RestoreWindow = TimeSpan.FromSeconds(5);

        private readonly ApplicationDbContext _database;
        private readonly StructureService _structures;

        public SiteService(ApplicationDbContext database, StructureService structures)
        {
            _database = database;
            _structures = structures;
        }

        // Lists the active sites the caller can see: owned sites for an Operator, assigned sites for a Worker, every site for an Admin
        public async Task<IEnumerable<SiteDto>> GetAllAsync(ClaimsPrincipal user, CancellationToken ct = default)
        {
            var visibleIds = SiteAccess.VisibleSiteIds(_database, user);

            return await _database.Sites
                .AsNoTracking()
                .Where(s => visibleIds.Contains(s.Id))
                .OrderBy(s => s.Name)
                .Select(s => new SiteDto(
                    s.Id,
                    s.Name,
                    s.CreatedAt,
                    s.Structures.Count(st => !st.IsDeleted),
                    s.OwnerEmail))
                .ToListAsync(ct);
        }

        // Finds one active site the caller can see
        public async Task<SiteDto?> GetByIdAsync(Guid id, ClaimsPrincipal user, CancellationToken ct = default)
        {
            var visibleIds = SiteAccess.VisibleSiteIds(_database, user);

            return await _database.Sites
                .AsNoTracking()
                .Where(s => s.Id == id && visibleIds.Contains(s.Id))
                .Select(s => new SiteDto(
                    s.Id,
                    s.Name,
                    s.CreatedAt,
                    s.Structures.Count(st => !st.IsDeleted),
                    s.OwnerEmail))
                .FirstOrDefaultAsync(ct);
        }

        // Creates a site owned by the caller, or by the chosen approved operator when an Admin gives a site away
        public async Task<SiteDto> CreateAsync(CreateSiteDto dto, ClaimsPrincipal user, CancellationToken ct = default)
        {
            var ownerEmail = await ResolveOwnerEmailAsync(dto.OwnerEmail, user, ct);
            var trimmedName = dto.Name.Trim();

            if (await NameTakenAsync(ownerEmail, trimmedName, null, ct))
                throw new InvalidOperationException($"You already have a site named '{trimmedName}'.");

            var siteId = Guid.NewGuid();

            if (dto.Id.HasValue && dto.Id.Value != Guid.Empty)
                siteId = dto.Id.Value;

            var site = new Site
            {
                Id = siteId,
                Name = trimmedName,
                OwnerEmail = ownerEmail,
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false
            };

            _database.Sites.Add(site);
            await _database.SaveChangesAsync(ct);

            return new SiteDto(site.Id, site.Name, site.CreatedAt, 0, site.OwnerEmail);
        }

        // Renames a site the caller manages, rejecting a name the same owner already uses
        public async Task<SiteDto?> UpdateAsync(Guid id, UpdateSiteDto dto, ClaimsPrincipal user, CancellationToken ct = default)
        {
            var manageableIds = SiteAccess.ManageableSiteIds(_database, user);
            var site = await _database.Sites.FirstOrDefaultAsync(s => s.Id == id && manageableIds.Contains(s.Id), ct);

            if (site is null)
                return null;

            var trimmedName = dto.Name.Trim();

            if (await NameTakenAsync(site.OwnerEmail ?? string.Empty, trimmedName, id, ct))
                throw new InvalidOperationException($"A site named '{trimmedName}' already exists for this owner.");

            site.Name = trimmedName;

            await _database.SaveChangesAsync(ct);

            var structureCount = await _database.Structures.CountAsync(s => s.SiteId == id && !s.IsDeleted, ct);
            return new SiteDto(site.Id, site.Name, site.CreatedAt, structureCount, site.OwnerEmail);
        }

        // The database retries on transient failures, so the whole transaction runs as one unit the retry can repeat
        public Task<DeleteSiteResultDto> DeleteAsync(Guid id, bool force, ClaimsPrincipal user, CancellationToken ct = default)
        {
            var strategy = _database.Database.CreateExecutionStrategy();
            return strategy.ExecuteAsync(() => DeleteInTransactionAsync(id, force, user, ct));
        }

        // Soft-deletes a site the caller manages, refusing while it still has active structures unless force is set; forcing moves those structures to deleted items and cancels the site's open invitations
        private async Task<DeleteSiteResultDto> DeleteInTransactionAsync(Guid id, bool force, ClaimsPrincipal user, CancellationToken ct)
        {
            var manageableIds = SiteAccess.ManageableSiteIds(_database, user);
            var site = await _database.Sites.FirstOrDefaultAsync(s => s.Id == id && manageableIds.Contains(s.Id), ct);

            if (site is null)
                return new DeleteSiteResultDto(false, $"Site '{id}' was not found.", 0);

            var linkedStructures = await _database.Structures.CountAsync(s => s.SiteId == id && !s.IsDeleted, ct);

            if (linkedStructures > 0 && !force)
            {
                return new DeleteSiteResultDto(
                    false,
                    $"Site '{site.Name}' cannot be deleted because it contains {linkedStructures} active structure(s). Reassign or delete structures first, or confirm deletion with force=true.",
                    linkedStructures);
            }

            var now = DateTime.UtcNow;
            var deletedBy = SiteAccess.NormalizedEmail(user);

            await using var transaction = await _database.Database.BeginTransactionAsync(ct);

            if (linkedStructures > 0)
            {
                await _database.Structures
                    .Where(s => s.SiteId == id && !s.IsDeleted)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(st => st.IsDeleted, true)
                        .SetProperty(st => st.DeletedAt, now)
                        .SetProperty(st => st.DeletedByEmail, deletedBy), ct);
            }

            await _database.SiteInvites.Where(i => i.SiteId == id).ExecuteDeleteAsync(ct);

            // Worker links (site_users) are kept on purpose: deleted sites are hidden from workers, and restoring the site gives them their access back
            site.IsDeleted = true;
            site.DeletedAt = now;
            site.DeletedByEmail = deletedBy;

            await _database.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            var message = $"Site '{site.Name}' was successfully deleted.";

            if (linkedStructures > 0)
                message = $"Site '{site.Name}' was successfully deleted. {linkedStructures} structure(s) were moved to deleted items.";

            return new DeleteSiteResultDto(true, message, linkedStructures);
        }

        // Lists the deleted sites the caller can restore, newest deletion first
        public async Task<IEnumerable<DeletedSiteDto>> GetDeletedAsync(ClaimsPrincipal user, CancellationToken ct = default)
        {
            return await SiteAccess.ManageableDeletedSites(_database, user)
                .AsNoTracking()
                .OrderByDescending(s => s.DeletedAt)
                .ThenBy(s => s.Name)
                .Select(s => new DeletedSiteDto(
                    s.Id,
                    s.Name,
                    s.OwnerEmail,
                    s.CreatedAt,
                    s.DeletedAt,
                    s.DeletedByEmail,
                    s.Structures.Count(st => st.IsDeleted)))
                .ToListAsync(ct);
        }

        // The database retries on transient failures, so the whole transaction runs as one unit the retry can repeat
        public Task<SiteDto?> RestoreAsync(Guid id, ClaimsPrincipal user, CancellationToken ct = default)
        {
            var strategy = _database.Database.CreateExecutionStrategy();
            return strategy.ExecuteAsync(() => RestoreInTransactionAsync(id, user, ct));
        }

        // Restores a deleted site the caller manages, together with the structures that were deleted along with it
        private async Task<SiteDto?> RestoreInTransactionAsync(Guid id, ClaimsPrincipal user, CancellationToken ct)
        {
            var site = await SiteAccess.ManageableDeletedSites(_database, user).FirstOrDefaultAsync(s => s.Id == id, ct);

            if (site is null)
                return null;

            if (await NameTakenAsync(site.OwnerEmail ?? string.Empty, site.Name, id, ct))
                throw new InvalidOperationException($"An active site named '{site.Name}' already exists for this owner. Rename it before restoring this one.");

            await using var transaction = await _database.Database.BeginTransactionAsync(ct);

            if (site.DeletedAt.HasValue)
            {
                var windowStart = site.DeletedAt.Value - RestoreWindow;
                var windowEnd = site.DeletedAt.Value + RestoreWindow;

                await _database.Structures
                    .Where(s => s.SiteId == id && s.IsDeleted && s.DeletedAt != null && s.DeletedAt >= windowStart && s.DeletedAt <= windowEnd)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(st => st.IsDeleted, false)
                        .SetProperty(st => st.DeletedAt, (DateTime?)null)
                        .SetProperty(st => st.DeletedByEmail, (string?)null), ct);
            }

            site.IsDeleted = false;
            site.DeletedAt = null;
            site.DeletedByEmail = null;

            await _database.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            var structureCount = await _database.Structures.CountAsync(s => s.SiteId == id && !s.IsDeleted, ct);
            return new SiteDto(site.Id, site.Name, site.CreatedAt, structureCount, site.OwnerEmail);
        }

        // The database retries on transient failures, so the whole transaction runs as one unit the retry can repeat
        public Task<int?> PurgeAsync(Guid id, ClaimsPrincipal user, CancellationToken ct = default)
        {
            var strategy = _database.Database.CreateExecutionStrategy();
            return strategy.ExecuteAsync(() => PurgeInTransactionAsync(id, user, ct));
        }

        // Permanently removes a deleted site the caller manages, with every structure it held, their data and thresholds, its invitations and worker links. Returns the number of structures removed, or null when the site is not in the deleted items
        private async Task<int?> PurgeInTransactionAsync(Guid id, ClaimsPrincipal user, CancellationToken ct)
        {
            var site = await SiteAccess.ManageableDeletedSites(_database, user).FirstOrDefaultAsync(s => s.Id == id, ct);

            if (site is null)
                return null;

            var structureIds = await _database.Structures
                .Where(s => s.SiteId == id)
                .Select(s => s.Id)
                .ToListAsync(ct);

            await using var transaction = await _database.Database.BeginTransactionAsync(ct);

            // A purge only works on deleted structures, so any left active inside a deleted site are moved to deleted items first
            await _database.Structures
                .Where(s => s.SiteId == id && !s.IsDeleted)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(st => st.IsDeleted, true)
                    .SetProperty(st => st.DeletedAt, DateTime.UtcNow)
                    .SetProperty(st => st.DeletedByEmail, SiteAccess.NormalizedEmail(user)), ct);

            foreach (var structureId in structureIds)
                await _structures.PurgeAsync(structureId, ct);

            await _database.SiteInvites.Where(i => i.SiteId == id).ExecuteDeleteAsync(ct);
            await _database.SiteUsers.Where(su => su.SiteId == id).ExecuteDeleteAsync(ct);
            await _database.Sites.Where(s => s.Id == id).ExecuteDeleteAsync(ct);

            await transaction.CommitAsync(ct);
            return structureIds.Count;
        }

        // Works out who will own a new site; only an Admin may name another owner, and that owner must be an approved operator
        private async Task<string> ResolveOwnerEmailAsync(string? requestedOwner, ClaimsPrincipal user, CancellationToken ct)
        {
            var callerEmail = SiteAccess.NormalizedEmail(user);

            if (string.IsNullOrWhiteSpace(requestedOwner) || !user.IsAdmin())
                return callerEmail;

            var ownerEmail = requestedOwner.Trim().ToLowerInvariant();

            var isApprovedOperator = await _database.Users.AnyAsync(
                u => u.Email == ownerEmail && u.Role == UserRoles.Operator && u.Status == UserStatus.Approved,
                ct);

            if (!isApprovedOperator)
                throw new ArgumentException("The chosen owner must be an approved operator.");

            return ownerEmail;
        }

        // Checks whether the owner already has another active site with this name, ignoring case
        private async Task<bool> NameTakenAsync(string ownerEmail, string name, Guid? excludeId, CancellationToken ct)
        {
            var lowerName = name.ToLower();
            var owned = SiteAccess.OwnedSites(_database.Sites.Where(s => !s.IsDeleted), ownerEmail.ToLowerInvariant());

            if (excludeId.HasValue)
                owned = owned.Where(s => s.Id != excludeId.Value);

            return await owned.AnyAsync(s => s.Name.ToLower() == lowerName, ct);
        }
    }
}