using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;
using System.Security.Claims;

namespace SmartHydroAPI.Services
{
    // Service managing the safe environmental ranges of each structure. Every range belongs to one structure, so it stays inside that structure's site
    public class ThresholdService
    {
        private readonly ApplicationDbContext _database;

        // Injects database context
        public ThresholdService(ApplicationDbContext database) => _database = database;

        // Lists the structures of one site the caller can see, each with its ranges. Returns null when the caller cannot see the site
        public async Task<List<StructureThresholdsDto>?> GetForSiteAsync(Guid siteId, ClaimsPrincipal user, CancellationToken ct = default)
        {
            var visibleSiteIds = SiteAccess.VisibleSiteIds(_database, user);

            if (!await visibleSiteIds.AnyAsync(id => id == siteId, ct))
                return null;

            var structures = await SiteAccess.VisibleStructures(_database, user)
                .AsNoTracking()
                .Where(s => s.SiteId == siteId && !s.IsDeleted)
                .OrderBy(s => s.Name)
                .ThenBy(s => s.Id)
                .ToListAsync(ct);

            return await BuildAsync(structures, ct);
        }

        // Returns one structure with its ranges, or null when the structure is missing or the caller cannot see it
        public async Task<StructureThresholdsDto?> GetForStructureAsync(int structureId, ClaimsPrincipal user, CancellationToken ct = default)
        {
            var structure = await SiteAccess.VisibleStructures(_database, user)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == structureId && !s.IsDeleted, ct);

            if (structure is null)
                return null;

            var built = await BuildAsync(new List<Structure> { structure }, ct);
            return built.FirstOrDefault();
        }

        // Saves a min/max range for one reading of one structure. Returns null when the structure is missing or the caller may not change it
        public async Task<Threshold?> UpsertAsync(UpdateThresholdDto dto, ClaimsPrincipal user, CancellationToken ct = default)
        {
            var structure = await SiteAccess.ManageableStructures(_database, user)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == dto.StructureId && !s.IsDeleted, ct);

            if (structure is null)
                return null;

            var sensor = dto.SensorType.Trim().ToUpperInvariant();

            if (sensor == "PH" && (dto.MinValue < 0 || dto.MaxValue > 14))
                throw new ArgumentException("pH thresholds must stay between 0 and 14.");
            if (sensor == "EC" && dto.MinValue < 0)
                throw new ArgumentException("EC thresholds cannot be negative.");

            if (!ThresholdDefaults.For(structure.StructureType).Any(r => r.SensorType == sensor))
                throw new ArgumentException($"{structure.Name} has no {sensor} threshold.");

            var threshold = await _database.Thresholds
                .FirstOrDefaultAsync(t => t.StructureId == structure.Id && t.SensorType == sensor, ct);

            if (threshold is null)
            {
                threshold = new Threshold
                {
                    StructureId = structure.Id,
                    StructureType = structure.StructureType.ToUpperInvariant(),
                    SensorType = sensor,
                    MinValue = dto.MinValue,
                    MaxValue = dto.MaxValue
                };
                _database.Thresholds.Add(threshold);
            }
            else
            {
                threshold.MinValue = dto.MinValue;
                threshold.MaxValue = dto.MaxValue;
            }

            await _database.SaveChangesAsync(ct);
            return threshold;
        }

        // Puts a saved range back to the healthy default by deleting it. Returns false when it is missing or belongs to a structure the caller may not change
        public async Task<bool> DeleteAsync(int id, ClaimsPrincipal user, CancellationToken ct = default)
        {
            var structureIds = SiteAccess.ManageableStructureIds(_database, user);

            var threshold = await _database.Thresholds
                .FirstOrDefaultAsync(t => t.Id == id && structureIds.Contains(t.StructureId), ct);

            if (threshold is null) return false;

            _database.Thresholds.Remove(threshold);
            await _database.SaveChangesAsync(ct);
            return true;
        }

        // Attaches each structure's saved ranges, filling any reading without one with its healthy default
        private async Task<List<StructureThresholdsDto>> BuildAsync(List<Structure> structures, CancellationToken ct)
        {
            var ids = structures.Select(s => s.Id).ToList();

            var saved = await _database.Thresholds
                .AsNoTracking()
                .Where(t => ids.Contains(t.StructureId))
                .ToListAsync(ct);

            var savedByStructure = saved
                .GroupBy(t => t.StructureId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var result = new List<StructureThresholdsDto>();

            foreach (var structure in structures)
            {
                savedByStructure.TryGetValue(structure.Id, out var own);

                var rows = ThresholdDefaults.Resolve(structure.StructureType, structure.Id, own ?? new List<Threshold>());

                result.Add(new StructureThresholdsDto
                {
                    StructureId = structure.Id,
                    StructureName = structure.Name,
                    StructureType = structure.StructureType,
                    Thresholds = rows.Select(t => new ThresholdResponseDto
                    {
                        Id = t.Id,
                        StructureId = structure.Id,
                        StructureType = t.StructureType,
                        SensorType = t.SensorType,
                        MinValue = t.MinValue,
                        MaxValue = t.MaxValue,
                        IsDefault = t.Id == 0
                    }).ToList()
                });
            }

            return result;
        }
    }
}
