namespace CrmSystem.Models.Identity;

/// <summary>
/// Role names and fixed role Ids (must match Database/05_SeedData.sql).
/// Use in controllers:  [Authorize(Roles = AppRoles.Admin)]
/// </summary>
public static class AppRoles
{
    public const string Admin = "Admin";
    public const string SalesManager = "SalesManager";
    public const string SalesEmployee = "SalesEmployee";

    /// <summary>Admin or Sales Manager (comma list for [Authorize(Roles = ...)]).</summary>
    public const string AdminOrManager = Admin + "," + SalesManager;

    /// <summary>All roles.</summary>
    public const string AllStaff = Admin + "," + SalesManager + "," + SalesEmployee;

    public const string AdminRoleId = "a1b2c3d4-0001-4000-8000-000000000001";
    public const string SalesManagerRoleId = "a1b2c3d4-0002-4000-8000-000000000002";
    public const string SalesEmployeeRoleId = "a1b2c3d4-0003-4000-8000-000000000003";

    public static readonly IReadOnlyList<string> All = new[] { Admin, SalesManager, SalesEmployee };
}

/// <summary>Authorization policy names registered in Program.cs.</summary>
public static class AppPolicies
{
    public const string AdminOnly = "AdminOnly";
    public const string ManagerOrAdmin = "ManagerOrAdmin";
    public const string AllStaff = "AllStaff";
}
