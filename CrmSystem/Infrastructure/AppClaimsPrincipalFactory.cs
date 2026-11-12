using System.Security.Claims;
using CrmSystem.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CrmSystem.Infrastructure;

/// <summary>
/// Adds the user's full name (EN/AR) and preferred language to the login cookie,
/// so the layout can show them without a database query on every page.
/// </summary>
public class AppClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>
{
    public const string FullName = "crm:full_name";
    public const string FullNameAr = "crm:full_name_ar";
    public const string Language = "crm:lang";

    public AppClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IOptions<IdentityOptions> options)
        : base(userManager, roleManager, options)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(FullName, user.FullName ?? string.Empty));
        identity.AddClaim(new Claim(FullNameAr, user.FullNameAr ?? string.Empty));
        identity.AddClaim(new Claim(Language, user.PreferredLanguage ?? "en"));
        return identity;
    }
}

public static class ClaimsPrincipalExtensions
{
    public static string DisplayName(this ClaimsPrincipal user, bool arabic)
    {
        var ar = user.FindFirstValue(AppClaimsPrincipalFactory.FullNameAr);
        var en = user.FindFirstValue(AppClaimsPrincipalFactory.FullName);
        if (arabic && !string.IsNullOrWhiteSpace(ar)) return ar!;
        return !string.IsNullOrWhiteSpace(en) ? en! : user.Identity?.Name ?? string.Empty;
    }

    /// <summary>Up to two initials for the avatar circle.</summary>
    public static string Initials(this ClaimsPrincipal user, bool arabic)
    {
        var parts = user.DisplayName(arabic).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant()
        };
    }

    /// <summary>Highest role key for display: Admin > SalesManager > SalesEmployee.</summary>
    public static string? MainRole(this ClaimsPrincipal user) =>
        user.IsInRole(AppRoles.Admin) ? AppRoles.Admin
        : user.IsInRole(AppRoles.SalesManager) ? AppRoles.SalesManager
        : user.IsInRole(AppRoles.SalesEmployee) ? AppRoles.SalesEmployee
        : null;
}
