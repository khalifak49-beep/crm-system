using System.ComponentModel.DataAnnotations;
using CrmSystem.Models.Common;

namespace CrmSystem.Models.Entities;

/// <summary>
/// Person inside a company. Table: dbo.Contacts
/// A company can have many contacts but only ONE primary contact.
/// </summary>
public class Contact : IAuditable, ISoftDelete
{
    public int ContactId { get; set; }

    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    [Required, StringLength(150)]
    public string FullName { get; set; } = string.Empty;

    [StringLength(100)]
    public string? JobTitle { get; set; }

    [StringLength(100)]
    public string? Department { get; set; }

    [StringLength(30)]
    public string? Phone { get; set; }

    [StringLength(30)]
    public string? Mobile { get; set; }

    [StringLength(256), EmailAddress]
    public string? Email { get; set; }

    public string? Notes { get; set; }

    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
