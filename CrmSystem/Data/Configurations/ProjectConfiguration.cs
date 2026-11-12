using CrmSystem.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmSystem.Data.Configurations;

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> b)
    {
        b.ToTable("Projects");
        b.HasKey(x => x.ProjectId);

        b.Property(x => x.ProjectName).HasMaxLength(200).IsRequired();
        b.Property(x => x.ProjectValue).HasPrecision(18, 3);
        b.Property(x => x.Currency).HasMaxLength(3).IsUnicode(false).IsFixedLength().IsRequired();
        b.Property(x => x.ExpectedStartDate).HasColumnType("date");
        b.Property(x => x.ExpectedEndDate).HasColumnType("date");
        b.Property(x => x.ClosedDate).HasColumnType("date");
        b.Property(x => x.ProjectStatus).HasConversion<byte>();
        b.Property(x => x.LostReason).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasOne(x => x.Client)
         .WithMany(x => x.Projects)
         .HasForeignKey(x => x.ClientId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.AssignedTo)
         .WithMany()
         .HasForeignKey(x => x.AssignedToId)
         .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProjectNoteConfiguration : IEntityTypeConfiguration<ProjectNote>
{
    public void Configure(EntityTypeBuilder<ProjectNote> b)
    {
        b.ToTable("ProjectNotes");
        b.HasKey(x => x.ProjectNoteId);

        b.Property(x => x.NoteText).IsRequired();

        // The table only has IsDeleted
        b.Ignore(x => x.DeletedAt);
        b.Ignore(x => x.DeletedBy);

        b.HasOne(x => x.Project)
         .WithMany(x => x.Notes)
         .HasForeignKey(x => x.ProjectId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}

public class TenderConfiguration : IEntityTypeConfiguration<Tender>
{
    public void Configure(EntityTypeBuilder<Tender> b)
    {
        b.ToTable("Tenders");
        b.HasKey(x => x.TenderId);

        b.Property(x => x.TenderNumber).HasMaxLength(50).IsRequired();
        b.Property(x => x.Title).HasMaxLength(250).IsRequired();
        b.Property(x => x.IssueDate).HasColumnType("date");
        b.Property(x => x.SubmissionDate).HasColumnType("date");
        b.Property(x => x.ClosingDate).HasColumnType("datetime2(0)");
        b.Property(x => x.TenderValue).HasPrecision(18, 3);
        b.Property(x => x.Currency).HasMaxLength(3).IsUnicode(false).IsFixedLength().IsRequired();
        b.Property(x => x.Status).HasConversion<byte>();
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasOne(x => x.Client)
         .WithMany(x => x.Tenders)
         .HasForeignKey(x => x.ClientId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Project)
         .WithMany(x => x.Tenders)
         .HasForeignKey(x => x.ProjectId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.AssignedTo)
         .WithMany()
         .HasForeignKey(x => x.AssignedToId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.ClientId, x.TenderNumber })
         .IsUnique()
         .HasFilter("[IsDeleted] = 0")
         .HasDatabaseName("UX_Tenders_Client_TenderNumber");
    }
}

public class QuotationConfiguration : IEntityTypeConfiguration<Quotation>
{
    public void Configure(EntityTypeBuilder<Quotation> b)
    {
        b.ToTable("Quotations");
        b.HasKey(x => x.QuotationId);

        b.Property(x => x.QuotationNumber).HasMaxLength(50).IsRequired();
        b.Property(x => x.Title).HasMaxLength(250);
        b.Property(x => x.SubmissionDate).HasColumnType("date");
        b.Property(x => x.ValidUntil).HasColumnType("date");
        b.Property(x => x.QuotationValue).HasPrecision(18, 3);
        b.Property(x => x.Currency).HasMaxLength(3).IsUnicode(false).IsFixedLength().IsRequired();
        b.Property(x => x.Status).HasConversion<byte>();
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasOne(x => x.Client)
         .WithMany(x => x.Quotations)
         .HasForeignKey(x => x.ClientId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Contact)
         .WithMany()
         .HasForeignKey(x => x.ContactId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Project)
         .WithMany(x => x.Quotations)
         .HasForeignKey(x => x.ProjectId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Tender)
         .WithMany(x => x.Quotations)
         .HasForeignKey(x => x.TenderId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.AssignedTo)
         .WithMany()
         .HasForeignKey(x => x.AssignedToId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.QuotationNumber)
         .IsUnique()
         .HasFilter("[IsDeleted] = 0")
         .HasDatabaseName("UX_Quotations_QuotationNumber");
    }
}
