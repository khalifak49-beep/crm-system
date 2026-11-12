using CrmSystem.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmSystem.Data.Configurations;

public class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> b)
    {
        b.ToTable("ImportBatches");
        b.HasKey(x => x.ImportBatchId);

        b.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        b.Property(x => x.FileHash).HasMaxLength(64).IsUnicode(false).IsFixedLength().IsRequired();
        b.Property(x => x.Status).HasConversion<byte>();
        b.Property(x => x.ImportedBy).IsRequired();

        b.HasOne(x => x.ImportedByUser)
         .WithMany()
         .HasForeignKey(x => x.ImportedBy)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.DefaultPerformer)
         .WithMany()
         .HasForeignKey(x => x.DefaultPerformerId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.FileHash).HasDatabaseName("IX_ImportBatches_FileHash");
    }
}

public class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> b)
    {
        b.ToTable("SystemSettings");
        b.HasKey(x => x.SettingId);

        b.Property(x => x.SettingKey).HasMaxLength(100).IsUnicode(false).IsRequired();
        b.Property(x => x.GroupName).HasMaxLength(50).IsUnicode(false).IsRequired();
        b.Property(x => x.DataType).HasMaxLength(20).IsUnicode(false).IsRequired();
        b.Property(x => x.DescriptionEn).HasMaxLength(300);
        b.Property(x => x.DescriptionAr).HasMaxLength(300);

        b.HasIndex(x => x.SettingKey).IsUnique().HasDatabaseName("UQ_SystemSettings_Key");
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLogs");
        b.HasKey(x => x.AuditLogId);

        b.Property(x => x.UserName).HasMaxLength(256);
        b.Property(x => x.Action).HasMaxLength(50).IsUnicode(false).IsRequired();
        b.Property(x => x.EntityName).HasMaxLength(100).IsUnicode(false);
        b.Property(x => x.EntityId).HasMaxLength(100).IsUnicode(false);
        b.Property(x => x.IpAddress).HasMaxLength(45).IsUnicode(false);
        b.Property(x => x.UserAgent).HasMaxLength(400);
    }
}
