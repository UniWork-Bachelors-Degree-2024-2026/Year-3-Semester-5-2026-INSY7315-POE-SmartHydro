using Microsoft.EntityFrameworkCore;
using SmartHydroAPI.Models.Entities;

namespace SmartHydroAPI.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<Site> Sites => Set<Site>();
        public DbSet<Structure> Structures => Set<Structure>();
        public DbSet<Device> Devices => Set<Device>();
        public DbSet<TelemetryRealtime> TelemetryRealtime => Set<TelemetryRealtime>();
        public DbSet<TelemetryReceipt> TelemetryReceipts => Set<TelemetryReceipt>();
        public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
        public DbSet<TelemetryDailyHistory> TelemetryDailyHistory => Set<TelemetryDailyHistory>();
        public DbSet<ActuatorState> ActuatorStates => Set<ActuatorState>();
        public DbSet<ActuatorHistory> ActuatorHistory => Set<ActuatorHistory>();
        public DbSet<DosingCommand> DosingCommands => Set<DosingCommand>();
        public DbSet<DeviceCommand> DeviceCommands => Set<DeviceCommand>();
        public DbSet<CameraCapture> CameraCaptures => Set<CameraCapture>();
        public DbSet<CameraCommand> CameraCommands => Set<CameraCommand>();
        public DbSet<Alert> Alerts => Set<Alert>();
        public DbSet<Threshold> Thresholds => Set<Threshold>();
        public DbSet<User> Users => Set<User>();
        public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
        public DbSet<PasswordResetCode> PasswordResetCodes => Set<PasswordResetCode>();
        public DbSet<PendingRegistration> PendingRegistrations => Set<PendingRegistration>();
        public DbSet<EmailRateLimit> EmailRateLimits => Set<EmailRateLimit>();
        public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();
        public DbSet<PasswordHistory> PasswordHistory => Set<PasswordHistory>();
        public DbSet<SiteInvite> SiteInvites => Set<SiteInvite>();
        public DbSet<SiteUser> SiteUsers => Set<SiteUser>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<TelemetryReceipt>().HasKey(x => new { x.DeviceMac, x.Sequence });
            modelBuilder.Entity<TelemetryReceipt>().HasIndex(x => x.ReceivedAt);
            modelBuilder.Entity<TelemetryRealtime>().HasIndex(x => new { x.DeviceMac, x.SampleSequence }).IsUnique();
            modelBuilder.Entity<DeviceCommand>().HasIndex(x => x.RequestId).IsUnique();

            modelBuilder.Entity<Site>(entity =>
            {
                entity.HasIndex(s => new { s.Name, s.IsDeleted })
                    .HasDatabaseName("idx_sites_name_deleted");
                entity.HasIndex(s => s.OwnerEmail).HasDatabaseName("idx_sites_owner_email");
            });

            modelBuilder.Entity<Structure>(entity =>
            {
                entity.Property(s => s.Latitude).HasPrecision(9, 6);
                entity.Property(s => s.Longitude).HasPrecision(9, 6);
                entity.HasIndex(s => s.SiteId).HasDatabaseName("idx_structures_site_id");
                entity.HasIndex(s => s.OwnerEmail).HasDatabaseName("idx_structures_owner_email");
            });

            modelBuilder.Entity<SiteUser>(entity =>
            {
                entity.HasKey(su => new { su.SiteId, su.UserId });
                entity.HasIndex(su => su.UserId).HasDatabaseName("idx_site_users_user_id");
            });

            modelBuilder.Entity<User>(entity =>
            {
                entity.HasIndex(u => u.Email).HasDatabaseName("idx_users_email");
            });

            modelBuilder.Entity<LoginAttempt>(entity =>
            {
                entity.HasKey(a => new { a.Email, a.ClientIp });
            });

            modelBuilder.Entity<PasswordHistory>(entity =>
            {
                entity.HasIndex(p => new { p.Email, p.CreatedAt }).HasDatabaseName("idx_password_history_email_created");
            });

            modelBuilder.Entity<CameraCapture>(entity =>
            {
                entity.HasIndex(c => c.IsDeleted).HasDatabaseName("idx_camera_captures_deleted");
            });

            modelBuilder.Entity<Alert>(entity =>
            {
                entity.HasIndex(a => a.IsDeleted).HasDatabaseName("idx_alerts_deleted");
            });

            modelBuilder.Entity<Device>(entity =>
            {
                entity.HasIndex(d => d.StructureId)
                    .HasDatabaseName("idx_devices_structure");

                entity.HasIndex(d => d.StructureType)
                    .HasDatabaseName("idx_devices_structure_type");
            });

            modelBuilder.Entity<TelemetryRealtime>(entity =>
            {
                entity.HasIndex(t => new { t.DeviceMac, t.RecordedAt })
                    .HasDatabaseName("idx_telemetry_realtime_mac_time");

                entity.Property(t => t.PhLevel).HasPrecision(4, 2);
                entity.Property(t => t.EcMs).HasPrecision(4, 2);
                entity.Property(t => t.LightLux).HasPrecision(7, 1);
                entity.Property(t => t.TemperatureC).HasPrecision(5, 2);
                entity.Property(t => t.HumidityPercent).HasPrecision(5, 2);
            });

            modelBuilder.Entity<ActivityLog>().HasIndex(x => new { x.DeviceMac, x.EventKey }).IsUnique().HasDatabaseName("ix_activity_event");
            modelBuilder.Entity<TelemetryDailyHistory>(entity =>
            {
                entity.HasIndex(d => new { d.DeviceMac, d.SummaryDate })
                    .IsUnique()
                    .HasDatabaseName("uq_daily_history_mac_date");

                entity.Property(d => d.AvgPh).HasPrecision(4, 2);
                entity.Property(d => d.AvgEc).HasPrecision(4, 2);
                entity.Property(d => d.AvgLight).HasPrecision(7, 1);
                entity.Property(d => d.AvgTemperatureC).HasPrecision(5, 2);
                entity.Property(d => d.AvgHumidityPercent).HasPrecision(5, 2);
            });

            modelBuilder.Entity<ActuatorHistory>()
                .HasIndex(a => new { a.DeviceMac, a.ChangedAt })
                .HasDatabaseName("idx_actuator_history_mac_time");

            modelBuilder.Entity<DosingCommand>(entity =>
            {
                entity.HasIndex(d => new { d.DeviceMac, d.Status })
                    .HasDatabaseName("idx_dosing_commands_mac_status");

                entity.Property(d => d.TargetVolumeMl).HasPrecision(6, 2);
            });

            modelBuilder.Entity<CameraCapture>()
                .HasIndex(c => new { c.StructureId, c.CapturedAt })
                .HasDatabaseName("idx_camera_captures_structure_time");

            modelBuilder.Entity<CameraCapture>()
                .HasIndex(c => new { c.DeviceMac, c.CapturedAt })
                .HasDatabaseName("idx_camera_captures_mac_time");

            modelBuilder.Entity<CameraCommand>()
                .HasIndex(c => new { c.DeviceMac, c.IsPending })
                .HasDatabaseName("idx_camera_commands_pending");

            modelBuilder.Entity<Alert>()
                .HasIndex(a => new { a.StructureId, a.IsResolved })
                .HasDatabaseName("idx_alerts_structure_resolved");

            modelBuilder.Entity<Threshold>(entity =>
            {
                // The unique index over these columns (one range per structure and reading) is created in Program.cs
                entity.HasIndex(t => new { t.StructureId, t.SensorType })
                    .HasDatabaseName("idx_thresholds_structure_sensor");

                entity.Property(t => t.MinValue).HasPrecision(7, 2);
                entity.Property(t => t.MaxValue).HasPrecision(7, 2);
            });
        }
    }
}
