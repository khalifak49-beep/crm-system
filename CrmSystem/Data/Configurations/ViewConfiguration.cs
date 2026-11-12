using CrmSystem.Models.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmSystem.Data.Configurations;

/*
 * Keyless, read-only mappings to the SQL views in Database/04_Views.sql.
 * Always query them with AsNoTracking() (keyless entities are never tracked anyway).
 */

public class ClientSummaryConfiguration : IEntityTypeConfiguration<ClientSummary>
{
    public void Configure(EntityTypeBuilder<ClientSummary> b)
    {
        b.HasNoKey();
        b.ToView("vw_ClientSummary");
        b.Property(x => x.Status).HasConversion<byte>();
        b.Property(x => x.LastActivityDate).HasColumnType("date");
        b.Property(x => x.NextFollowUpDate).HasColumnType("date");
        b.Property(x => x.NextFollowUpTime).HasColumnType("time(0)");
    }
}

public class ActivityDetailsConfiguration : IEntityTypeConfiguration<ActivityDetails>
{
    public void Configure(EntityTypeBuilder<ActivityDetails> b)
    {
        b.HasNoKey();
        b.ToView("vw_ActivityDetails");
        b.Property(x => x.ActivityDate).HasColumnType("date");
        b.Property(x => x.ActivityTime).HasColumnType("time(0)");
        b.Property(x => x.ActivityTypeCode).IsUnicode(false);
    }
}

public class FollowUpDetailsConfiguration : IEntityTypeConfiguration<FollowUpDetails>
{
    public void Configure(EntityTypeBuilder<FollowUpDetails> b)
    {
        b.HasNoKey();
        b.ToView("vw_FollowUpDetails");
        b.Property(x => x.Priority).HasConversion<byte>();
        b.Property(x => x.Status).HasConversion<byte>();
        b.Property(x => x.EffectiveStatus).HasConversion<byte>();
        b.Property(x => x.ReminderStatus).HasConversion<byte>();
        b.Property(x => x.DueDate).HasColumnType("date");
        b.Property(x => x.DueTime).HasColumnType("time(0)");
        b.Property(x => x.SourceActivityDate).HasColumnType("date");
        b.Property(x => x.TimeZoneId).IsUnicode(false);
        b.Property(x => x.FollowUpTypeCode).IsUnicode(false);
    }
}

public class ClientTimelineConfiguration : IEntityTypeConfiguration<ClientTimelineEntry>
{
    public void Configure(EntityTypeBuilder<ClientTimelineEntry> b)
    {
        b.HasNoKey();
        b.ToView("vw_ClientTimeline");
        b.Property(x => x.EventKind).IsUnicode(false);
        b.Property(x => x.EventCode).IsUnicode(false);
        b.Property(x => x.IconName).IsUnicode(false);
        b.Property(x => x.ColorHex).IsUnicode(false);
    }
}
