using CrmSystem.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmSystem.Data.Configurations;

public class ActivityTypeConfiguration : IEntityTypeConfiguration<ActivityType>
{
    public void Configure(EntityTypeBuilder<ActivityType> b)
    {
        b.ToTable("ActivityTypes");
        b.HasKey(x => x.ActivityTypeId);

        b.Property(x => x.Code).HasMaxLength(30).IsUnicode(false).IsRequired();
        b.Property(x => x.NameEn).HasMaxLength(100).IsRequired();
        b.Property(x => x.NameAr).HasMaxLength(100).IsRequired();
        b.Property(x => x.IconName).HasMaxLength(50).IsUnicode(false).IsRequired();
        b.Property(x => x.ColorHex).HasMaxLength(7).IsUnicode(false).IsFixedLength().IsRequired();
        b.Property(x => x.ExcelColumnName).HasMaxLength(50);

        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UQ_ActivityTypes_Code");
    }
}

public class ActivityConfiguration : IEntityTypeConfiguration<Activity>
{
    public void Configure(EntityTypeBuilder<Activity> b)
    {
        b.ToTable("Activities");
        b.HasKey(x => x.ActivityId);

        b.Property(x => x.ActivityDate).HasColumnType("date");
        b.Property(x => x.ActivityTime).HasColumnType("time(0)");
        b.Property(x => x.Subject).HasMaxLength(250);
        b.Property(x => x.Outcome).HasMaxLength(500);
        b.Property(x => x.PerformedById).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasOne(x => x.Client)
         .WithMany(x => x.Activities)
         .HasForeignKey(x => x.ClientId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Contact)
         .WithMany()
         .HasForeignKey(x => x.ContactId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.ActivityType)
         .WithMany()
         .HasForeignKey(x => x.ActivityTypeId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Project)
         .WithMany(x => x.Activities)
         .HasForeignKey(x => x.ProjectId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Quotation)
         .WithMany()
         .HasForeignKey(x => x.QuotationId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Tender)
         .WithMany()
         .HasForeignKey(x => x.TenderId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.PerformedBy)
         .WithMany()
         .HasForeignKey(x => x.PerformedById)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.ImportBatch)
         .WithMany(x => x.Activities)
         .HasForeignKey(x => x.ImportBatchId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.ClientId, x.ActivityDate }).HasDatabaseName("IX_Activities_Client_Date");
        b.HasIndex(x => new { x.ActivityDate, x.ActivityTypeId }).HasDatabaseName("IX_Activities_Date_Type");
        b.HasIndex(x => new { x.PerformedById, x.ActivityDate }).HasDatabaseName("IX_Activities_PerformedBy_Date");
    }
}

public class FollowUpConfiguration : IEntityTypeConfiguration<FollowUp>
{
    public void Configure(EntityTypeBuilder<FollowUp> b)
    {
        b.ToTable("FollowUps");
        b.HasKey(x => x.FollowUpId);

        b.Property(x => x.Description).HasMaxLength(1000).IsRequired();
        b.Property(x => x.DueDate).HasColumnType("date");
        b.Property(x => x.DueTime).HasColumnType("time(0)");
        b.Property(x => x.TimeZoneId).HasMaxLength(64).IsUnicode(false).IsRequired();
        b.Property(x => x.DueAtUtc).HasColumnType("datetime2(0)");
        b.Property(x => x.AssignedToId).IsRequired();
        b.Property(x => x.Priority).HasConversion<byte>();
        b.Property(x => x.Status).HasConversion<byte>();
        b.Property(x => x.ReminderStatus).HasConversion<byte>();
        b.Property(x => x.CompletionNotes).HasMaxLength(1000);
        b.Property(x => x.CancelReason).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsRowVersion();

        // PERSISTED computed column - moves automatically when DueAtUtc changes
        b.Property(x => x.ReminderAtUtc)
         .HasColumnType("datetime2(0)")
         .HasComputedColumnSql("DATEADD(MINUTE, -[ReminderMinutesBefore], [DueAtUtc])", stored: true);

        b.HasOne(x => x.Client)
         .WithMany(x => x.FollowUps)
         .HasForeignKey(x => x.ClientId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Contact)
         .WithMany()
         .HasForeignKey(x => x.ContactId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.FollowUpType)
         .WithMany()
         .HasForeignKey(x => x.FollowUpTypeId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.SourceActivity)
         .WithMany(x => x.CreatedFollowUps)
         .HasForeignKey(x => x.SourceActivityId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.CompletedActivity)
         .WithMany()
         .HasForeignKey(x => x.CompletedActivityId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.ParentFollowUp)
         .WithMany(x => x.ChildFollowUps)
         .HasForeignKey(x => x.ParentFollowUpId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Project)
         .WithMany(x => x.FollowUps)
         .HasForeignKey(x => x.ProjectId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Quotation)
         .WithMany()
         .HasForeignKey(x => x.QuotationId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Tender)
         .WithMany()
         .HasForeignKey(x => x.TenderId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.AssignedTo)
         .WithMany()
         .HasForeignKey(x => x.AssignedToId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.CompletedBy)
         .WithMany()
         .HasForeignKey(x => x.CompletedById)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.ReminderAtUtc).HasDatabaseName("IX_FollowUps_ReminderScan");
        b.HasIndex(x => new { x.AssignedToId, x.Status, x.DueAtUtc }).HasDatabaseName("IX_FollowUps_Assigned_Status_Due");
        b.HasIndex(x => new { x.Status, x.DueAtUtc }).HasDatabaseName("IX_FollowUps_Status_Due");
    }
}
