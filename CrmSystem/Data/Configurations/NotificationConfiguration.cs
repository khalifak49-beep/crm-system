using CrmSystem.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmSystem.Data.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("Notifications");
        b.HasKey(x => x.NotificationId);

        b.Property(x => x.NotificationType).HasConversion<byte>();
        b.Property(x => x.Title).HasMaxLength(250).IsRequired();
        b.Property(x => x.Message).HasMaxLength(2000).IsRequired();
        b.Property(x => x.Url).HasMaxLength(500);
        b.Property(x => x.EventAtLocal).HasColumnType("datetime2(0)");
        b.Property(x => x.DedupKey).HasMaxLength(200).IsUnicode(false).IsRequired();

        b.HasOne(x => x.User)
         .WithMany()
         .HasForeignKey(x => x.UserId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.FollowUp).WithMany().HasForeignKey(x => x.FollowUpId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Quotation).WithMany().HasForeignKey(x => x.QuotationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Tender).WithMany().HasForeignKey(x => x.TenderId).OnDelete(DeleteBehavior.Restrict);

        // UNIQUE - prevents duplicate reminders
        b.HasIndex(x => x.DedupKey).IsUnique().HasDatabaseName("UQ_Notifications_DedupKey");
        b.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAtUtc }).HasDatabaseName("IX_Notifications_User_Unread");
    }
}

public class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    public void Configure(EntityTypeBuilder<NotificationLog> b)
    {
        b.ToTable("NotificationLogs");
        b.HasKey(x => x.NotificationLogId);

        b.Property(x => x.Channel).HasConversion<byte>();
        b.Property(x => x.Status).HasConversion<byte>();
        b.Property(x => x.Recipient).HasMaxLength(256);
        b.Property(x => x.ErrorMessage).HasMaxLength(2000);

        b.HasOne(x => x.Notification)
         .WithMany(x => x.Logs)
         .HasForeignKey(x => x.NotificationId)
         .OnDelete(DeleteBehavior.Cascade);

        // UNIQUE - one log per channel per notification
        b.HasIndex(x => new { x.NotificationId, x.Channel })
         .IsUnique()
         .HasDatabaseName("UQ_NotificationLogs_Notification_Channel");
    }
}

public class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> b)
    {
        b.ToTable("PushSubscriptions");
        b.HasKey(x => x.PushSubscriptionId);

        b.Property(x => x.Endpoint).HasMaxLength(800).IsRequired();
        b.Property(x => x.P256dh).HasMaxLength(200).IsRequired();
        b.Property(x => x.Auth).HasMaxLength(100).IsRequired();
        b.Property(x => x.UserAgent).HasMaxLength(400);

        b.HasOne(x => x.User)
         .WithMany()
         .HasForeignKey(x => x.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.Endpoint).IsUnique().HasDatabaseName("UQ_PushSubscriptions_Endpoint");
    }
}
