using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace CrmSystem.Models.Identity;

/// <summary>
/// Role. Table: dbo.Roles  (Admin, SalesManager, SalesEmployee)
/// </summary>
public class ApplicationRole : IdentityRole
{
    public ApplicationRole() { }

    public ApplicationRole(string roleName) : base(roleName) { }

    [StringLength(100)]
    public string? DisplayNameEn { get; set; }

    [StringLength(100)]
    public string? DisplayNameAr { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }
}
