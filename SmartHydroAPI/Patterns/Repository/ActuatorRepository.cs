using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Data;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Patterns.Repository
{
    // Concrete repository managing ActuatorState persistence, thread-safety, and audit trail generation
    public class ActuatorRepository : IActuatorRepository
    {
        private readonly ApplicationDbContext _database;

        // Injects database context
        public ActuatorRepository(ApplicationDbContext database) => _database = database;

        // Retrieves existing state with NoTracking or inserts default baseline
        public async Task<ActuatorState> GetOrCreateStateAsync(string mac, CancellationToken ct = default)
        {
            var normalizedMac = mac.Trim().ToUpperInvariant();
            var state = await _database.ActuatorStates.AsNoTracking().FirstOrDefaultAsync(a => a.DeviceMac == normalizedMac, ct);
            if (state is not null) return state;

            var newState = new ActuatorState { DeviceMac = normalizedMac, UpdatedAt = DateTime.UtcNow };
            _database.ActuatorStates.Add(newState);
            await _database.SaveChangesAsync(ct);
            return newState;
        }

        // Applies state updates and logs change history
        public Task<ActuatorState> UpdateStateAndLogHistoryAsync(ActuatorCommandDto dto, CancellationToken ct = default)
        {
            int attempt = 0;
            return _database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                if (attempt++ > 0) _database.ChangeTracker.Clear();
                await using var transaction = _database.Database.IsRelational() && _database.Database.CurrentTransaction == null
                    ? await _database.Database.BeginTransactionAsync(ct) : null;
                if (transaction != null) await _database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7315003)", ct);
                var state = await StageStateAndLogHistoryAsync(dto, true, ct);
                if (transaction != null) await transaction.CommitAsync(ct);
                return state;
            });
        }

        internal async Task<ActuatorState> StageStateAndLogHistoryAsync(ActuatorCommandDto dto, bool saveChanges, CancellationToken ct = default)
        {
            var normalizedMac = dto.DeviceMac.Trim().ToUpperInvariant();
            if (dto.ReportedSequence.HasValue && (dto.ReportedSequence <= 0 || dto.ReportedSequence >= (1L << 62)))
                throw new ArgumentException("Invalid relay report sequence.");
            var state = await _database.ActuatorStates.FindAsync([normalizedMac], ct);
            if (state is null)
            {
                state = new ActuatorState { DeviceMac = normalizedMac };
                _database.ActuatorStates.Add(state);
            }
            else if (_database.Entry(state).State == EntityState.Unchanged) await _database.Entry(state).ReloadAsync(ct);

            var now = DateTime.UtcNow;
            if (state.ReportedSequence.HasValue && (!dto.ReportedSequence.HasValue || dto.ReportedSequence <= state.ReportedSequence))
                return state;
            if (dto.ReportedSequence.HasValue) state.ReportedSequence = dto.ReportedSequence;

            void LogDelta(string name, bool current, bool? incoming)
            {
                if (incoming.HasValue && incoming.Value != current)
                {
                    _database.ActuatorHistory.Add(new ActuatorHistory
                    {
                        DeviceMac = normalizedMac,
                        ActuatorName = name,
                        NewState = incoming.Value,
                        ChangedBy = dto.ChangedBy,
                        ChangedAt = now
                    });
                }
            }

            LogDelta("grow_light", state.GrowLight, dto.GrowLight);
            LogDelta("circulation_fan", state.CirculationFan, dto.CirculationFan);
            LogDelta("main_water_pump", state.MainWaterPump, dto.MainWaterPump);
            LogDelta("extractor_fan", state.ExtractorFan, dto.ExtractorFan);
            LogDelta("ph_up_pump", state.PhUpPump, dto.PhUpPump);
            LogDelta("ph_down_pump", state.PhDownPump, dto.PhDownPump);
            LogDelta("nutrient_up_pump", state.NutrientUpPump, dto.NutrientUpPump);
            LogDelta("dilution_down_pump", state.DilutionDownPump, dto.DilutionDownPump);
            LogDelta("spray_system", state.SpraySystem, dto.SpraySystem);

            if (dto.GrowLight.HasValue) state.GrowLight = dto.GrowLight.Value;
            if (dto.CirculationFan.HasValue) state.CirculationFan = dto.CirculationFan.Value;
            if (dto.MainWaterPump.HasValue) state.MainWaterPump = dto.MainWaterPump.Value;
            if (dto.ExtractorFan.HasValue) state.ExtractorFan = dto.ExtractorFan.Value;
            if (dto.PhUpPump.HasValue) state.PhUpPump = dto.PhUpPump.Value;
            if (dto.PhDownPump.HasValue) state.PhDownPump = dto.PhDownPump.Value;
            if (dto.NutrientUpPump.HasValue) state.NutrientUpPump = dto.NutrientUpPump.Value;
            if (dto.DilutionDownPump.HasValue) state.DilutionDownPump = dto.DilutionDownPump.Value;
            if (dto.SpraySystem.HasValue) state.SpraySystem = dto.SpraySystem.Value;

            state.UpdatedAt = now;
            if (saveChanges) await _database.SaveChangesAsync(ct);
            return state;
        }

        // Applies a one-shot timed command and logs change history
        public async Task<ActuatorState> ApplyTimedCommandAsync(TimedActuatorCommandDto dto, CancellationToken ct = default)
        {
            var normalizedMac = dto.DeviceMac.Trim().ToUpperInvariant();
            var state = await _database.ActuatorStates.FindAsync([normalizedMac], ct);
            if (state is null)
            {
                state = new ActuatorState { DeviceMac = normalizedMac };
                _database.ActuatorStates.Add(state);
            }

            var now = DateTime.UtcNow;

            // Log timed activation
            _database.ActuatorHistory.Add(new ActuatorHistory
            {
                DeviceMac = normalizedMac,
                ActuatorName = $"{dto.ActuatorName}_TIMED_{dto.DurationSeconds}S",
                NewState = true,
                ChangedBy = dto.ChangedBy,
                ChangedAt = now
            });

            // If spray system, temporarily toggle spray system flag
            if (dto.ActuatorName.Equals("SpraySystem", StringComparison.OrdinalIgnoreCase))
            {
                state.SpraySystem = true;
            }
            else if (dto.ActuatorName.Equals("MainWaterPump", StringComparison.OrdinalIgnoreCase))
            {
                state.MainWaterPump = true;
            }

            state.UpdatedAt = now;
            await _database.SaveChangesAsync(ct);
            return state;
        }

        // Queries historical records for a device MAC address
        public async Task<IEnumerable<ActuatorHistory>> GetHistoryAsync(string mac, int limit = 50, CancellationToken ct = default)
        {
            var normalizedMac = mac.Trim().ToUpperInvariant();
            return await _database.ActuatorHistory
                .AsNoTracking()
                .Where(h => h.DeviceMac == normalizedMac)
                .OrderByDescending(h => h.ChangedAt)
                .Take(limit)
                .ToListAsync(ct);
        }

        // Queries paginated historical records for a device MAC address
        public async Task<PagedResultDto<ActuatorHistory>> GetPagedHistoryAsync(string mac, int page = 1, int pageSize = 20, CancellationToken ct = default)
        {
            var normalizedMac = mac.Trim().ToUpperInvariant();
            var query = _database.ActuatorHistory
                .AsNoTracking()
                .Where(h => h.DeviceMac == normalizedMac);

            var totalCount = await query.CountAsync(ct);
            var safePage = Math.Max(1, page);
            var safePageSize = Math.Clamp(pageSize, 1, 100);

            var items = await query
                .OrderByDescending(h => h.ChangedAt)
                .Skip((safePage - 1) * safePageSize)
                .Take(safePageSize)
                .ToListAsync(ct);

            var totalPages = (int)Math.Ceiling(totalCount / (double)safePageSize);
            return new PagedResultDto<ActuatorHistory>(items, totalCount, safePage, safePageSize, totalPages);
        }
    }
}
