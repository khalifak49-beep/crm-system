using System.Linq.Expressions;
using System.Text.Json;
using CrmSystem.Infrastructure;
using CrmSystem.Models.Common;
using CrmSystem.Models.Entities;
using CrmSystem.Models.Identity;
using CrmSystem.Models.ReadModels;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace CrmSystem.Data;

/// <summary>
/// The single EF Core context of the application.
/// - Maps to the tables created by Database/*.sql (no EF migrations).
/// - Identity tables use custom names (ApplicationUsers, Roles, UserRoles...).
/// - SaveChanges automatically:
///     * fills CreatedAt/CreatedBy/UpdatedAt/UpdatedBy (IAuditable),
///     * converts Remove() into soft delete (ISoftDelete),
///     * writes AuditLogs rows with old/new values as JSON.
/// </summary>
public class ApplicationDbContext
    : IdentityDbContext<ApplicationUser, ApplicationRole, string>, IDataProtectionKeyContext
{
    private readonly ICurrentUser? _currentUser;

    private static readonly JsonSerializerOptions AuditJson = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping // keep Arabic readable
    };

    /// <summary>Properties never written to AuditLogs.</summary>
    private static readonly HashSet<string> AuditExcludedProperties = new(StringComparer.Ordinal)
    {
        "RowVersion", "PasswordHash", "SecurityStamp", "ConcurrencyStamp", "NormalizedName"
    };

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ICurrentUser? currentUser = null)
        : base(options)
    {
        _currentUser = currentUser;
    }

    /// <summary>
    /// Set to false for bulk operations (e.g. Excel import) to avoid one audit row per record;
    /// write a single summary AuditLog instead.
    /// </summary>
    public bool AuditEnabled { get; set; } = true;

    // ---------------- Tables ----------------
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<ClientAssignment> ClientAssignments => Set<ClientAssignment>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<ActivityType> ActivityTypes => Set<ActivityType>();
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<FollowUp> FollowUps => Set<FollowUp>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectNote> ProjectNotes => Set<ProjectNote>();
    public DbSet<Quotation> Quotations => Set<Quotation>();
    public DbSet<Tender> Tenders => Set<Tender>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    // ---------------- Views (read-only) ----------------
    public DbSet<ClientSummary> ClientSummaries => Set<ClientSummary>();
    public DbSet<ActivityDetails> ActivityDetails => Set<ActivityDetails>();
    public DbSet<FollowUpDetails> FollowUpDetails => Set<FollowUpDetails>();
    public DbSet<ClientTimelineEntry> ClientTimeline => Set<ClientTimelineEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ----- Identity: custom table names (see Database/02_IdentityTables.sql) -----
        builder.Entity<ApplicationUser>(b =>
        {
            b.ToTable("ApplicationUsers");
            b.Property(u => u.PreferredLanguage).HasMaxLength(5).IsUnicode(false);
            b.HasOne(u => u.Manager)
             .WithMany(u => u.TeamMembers)
             .HasForeignKey(u => u.ManagerId)
             .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ApplicationRole>().ToTable("Roles");
        builder.Entity<IdentityUserRole<string>>().ToTable("UserRoles");
        builder.Entity<IdentityUserClaim<string>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<string>>().ToTable("UserLogins");
        builder.Entity<IdentityUserToken<string>>().ToTable("UserTokens");
        builder.Entity<IdentityRoleClaim<string>>().ToTable("RoleClaims");
        builder.Entity<DataProtectionKey>().ToTable("DataProtectionKeys");

        // ----- CRM tables & views: one configuration class per entity (Data/Configurations) -----
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // ----- Soft delete: hide deleted rows from every query -----
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDelete).IsAssignableFrom(entityType.ClrType) || entityType.IsOwned())
                continue;

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var body = Expression.Equal(
                Expression.Property(parameter, nameof(ISoftDelete.IsDeleted)),
                Expression.Constant(false));
            builder.Entity(entityType.ClrType).HasQueryFilter(Expression.Lambda(body, parameter));
        }
    }

    // =====================================================================
    // SaveChanges: audit fields, soft delete, audit log
    // =====================================================================

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var pending = PrepareChanges();
        var result = base.SaveChanges(acceptAllChangesOnSuccess);
        if (pending.Count > 0)
        {
            AddAuditRows(pending);
            base.SaveChanges(acceptAllChangesOnSuccess);
        }
        return result;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var pending = PrepareChanges();
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (pending.Count > 0)
        {
            AddAuditRows(pending);
            await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        return result;
    }

    private sealed class PendingAudit
    {
        public required EntityEntry Entry { get; init; }
        public required string Action { get; init; }
        public Dictionary<string, object?>? OldValues { get; init; }
        public Dictionary<string, object?>? NewValues { get; set; }
    }

    /// <summary>Runs before the INSERT/UPDATE statements are sent.</summary>
    private List<PendingAudit> PrepareChanges()
    {
        ChangeTracker.DetectChanges();

        var now = DateTime.UtcNow;
        var userId = _currentUser?.UserId;
        var pending = new List<PendingAudit>();

        foreach (var entry in ChangeTracker.Entries().ToList())
        {
            if (entry.State is EntityState.Detached or EntityState.Unchanged) continue;
            if (entry.Entity is AuditLog) continue;

            // Clean company names so "OQ   BI " is stored as "OQ BI"
            if (entry.Entity is Client client && entry.State is EntityState.Added or EntityState.Modified)
            {
                var cleaned = Client.CleanName(client.CompanyName);
                if (cleaned != client.CompanyName) client.CompanyName = cleaned;
            }

            string action;
            Dictionary<string, object?>? oldValues = null;
            Dictionary<string, object?>? newValues = null;

            if (entry.State == EntityState.Deleted && entry.Entity is ISoftDelete softDelete)
            {
                // Convert physical delete into soft delete
                oldValues = new() { [nameof(ISoftDelete.IsDeleted)] = false };
                entry.State = EntityState.Unchanged;
                softDelete.IsDeleted = true;
                softDelete.DeletedAt = now;
                softDelete.DeletedBy = userId;
                entry.Property(nameof(ISoftDelete.IsDeleted)).IsModified = true;
                if (entry.Metadata.FindProperty(nameof(ISoftDelete.DeletedAt)) != null)
                {
                    entry.Property(nameof(ISoftDelete.DeletedAt)).IsModified = true;
                    entry.Property(nameof(ISoftDelete.DeletedBy)).IsModified = true;
                }
                if (entry.Entity is IAuditable a)
                {
                    a.UpdatedAt = now;
                    a.UpdatedBy = userId;
                    entry.Property(nameof(IAuditable.UpdatedAt)).IsModified = true;
                    entry.Property(nameof(IAuditable.UpdatedBy)).IsModified = true;
                }
                action = AuditActions.SoftDelete;
                newValues = new() { [nameof(ISoftDelete.IsDeleted)] = true };
            }
            else if (entry.Entity is IAuditable auditable)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        auditable.CreatedAt = now;
                        auditable.CreatedBy ??= userId;
                        action = AuditActions.Create;
                        break;

                    case EntityState.Modified:
                        // UpdatedAt/UpdatedBy record USER edits. Background jobs (no signed-in user,
                        // e.g. the reminder engine marking a reminder as sent) leave them unchanged.
                        if (userId != null)
                        {
                            auditable.UpdatedAt = now;
                            auditable.UpdatedBy = userId;
                        }
                        // CreatedAt / CreatedBy can never be changed by an update
                        entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
                        entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;

                        var restored = entry.Entity is ISoftDelete sd2
                                       && entry.Property(nameof(ISoftDelete.IsDeleted)).IsModified
                                       && !sd2.IsDeleted;
                        action = restored ? AuditActions.Restore : AuditActions.Update;

                        oldValues = new();
                        newValues = new();
                        foreach (var p in entry.Properties.Where(p => p.IsModified && !AuditExcludedProperties.Contains(p.Metadata.Name)))
                        {
                            if (Equals(p.OriginalValue, p.CurrentValue)) continue;
                            oldValues[p.Metadata.Name] = p.OriginalValue;
                            newValues[p.Metadata.Name] = p.CurrentValue;
                        }
                        break;

                    case EntityState.Deleted:
                        action = AuditActions.Delete;
                        oldValues = SnapshotValues(entry, original: true);
                        break;

                    default:
                        continue;
                }
            }
            else
            {
                continue; // only IAuditable entities are audited automatically
            }

            if (AuditEnabled)
            {
                pending.Add(new PendingAudit
                {
                    Entry = entry,
                    Action = action,
                    OldValues = oldValues,
                    NewValues = newValues
                });
            }
        }

        return pending;
    }

    /// <summary>Runs after the save, when generated keys (identity, CompanyCode...) are known.</summary>
    private void AddAuditRows(List<PendingAudit> pending)
    {
        foreach (var p in pending)
        {
            if (p.Action == AuditActions.Create)
                p.NewValues = SnapshotValues(p.Entry, original: false);

            if (p.Action == AuditActions.Update && (p.NewValues == null || p.NewValues.Count == 0))
                continue; // nothing really changed

            AuditLogs.Add(new AuditLog
            {
                UserId = _currentUser?.UserId,
                UserName = _currentUser?.UserName,
                Action = p.Action,
                EntityName = p.Entry.Metadata.ClrType.Name,
                EntityId = GetPrimaryKey(p.Entry),
                OldValues = p.OldValues is { Count: > 0 } ? JsonSerializer.Serialize(p.OldValues, AuditJson) : null,
                NewValues = p.NewValues is { Count: > 0 } ? JsonSerializer.Serialize(p.NewValues, AuditJson) : null,
                IpAddress = Truncate(_currentUser?.IpAddress, 45),
                UserAgent = _currentUser?.UserAgent,
                CreatedAtUtc = DateTime.UtcNow
            });
        }
    }

    private static Dictionary<string, object?> SnapshotValues(EntityEntry entry, bool original)
    {
        var values = new Dictionary<string, object?>();
        foreach (var p in entry.Properties)
        {
            if (AuditExcludedProperties.Contains(p.Metadata.Name)) continue;
            var value = original ? p.OriginalValue : p.CurrentValue;
            if (value is byte[]) continue;
            values[p.Metadata.Name] = value;
        }
        return values;
    }

    private static string? GetPrimaryKey(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key == null) return null;
        var parts = key.Properties.Select(p => entry.Property(p.Name).CurrentValue?.ToString());
        return Truncate(string.Join(",", parts), 100);
    }

    private static string? Truncate(string? value, int max) =>
        value == null ? null : value.Length <= max ? value : value[..max];

    /// <summary>
    /// Writes a manual audit row (Login, Export, Import, SettingsChanged...).
    /// Call SaveChangesAsync afterwards.
    /// </summary>
    public void AddAudit(string action, string? entityName = null, string? entityId = null, object? newValues = null, object? oldValues = null)
    {
        AuditLogs.Add(new AuditLog
        {
            UserId = _currentUser?.UserId,
            UserName = _currentUser?.UserName,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            OldValues = oldValues == null ? null : JsonSerializer.Serialize(oldValues, AuditJson),
            NewValues = newValues == null ? null : JsonSerializer.Serialize(newValues, AuditJson),
            IpAddress = Truncate(_currentUser?.IpAddress, 45),
            UserAgent = _currentUser?.UserAgent,
            CreatedAtUtc = DateTime.UtcNow
        });
    }
}
