using CrmSystem.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CrmSystem.Data;

/// <summary>
/// Runs once at application startup:
///  1. checks the connection to CRMDatabase,
///  2. makes sure the 3 roles exist (normally created by 05_SeedData.sql),
///  3. creates the first Admin user if no Admin exists yet.
///
/// The admin password is NEVER in code or appsettings.json. It is read from:
///   - Development : dotnet user-secrets set "SeedAdmin:Password" "YourStrongP@ss1"
///   - Production  : environment variable  SeedAdmin__Password  (set on the IIS site)
/// After the first start the value can be removed.
/// </summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DbInitializer));
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var config = sp.GetRequiredService<IConfiguration>();

        if (!await db.Database.CanConnectAsync())
        {
            logger.LogCritical("Cannot connect to the database. Check ConnectionStrings:CRMDatabase and run Database/RunAll.ps1.");
            throw new InvalidOperationException("Database CRMDatabase is not reachable.");
        }

        await EnsureRolesAsync(sp, logger);
        await EnsureAdminAsync(sp, config, logger);
    }

    private static async Task EnsureRolesAsync(IServiceProvider sp, ILogger logger)
    {
        var roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();

        var roles = new[]
        {
            (Id: AppRoles.AdminRoleId,         Name: AppRoles.Admin,         En: "Administrator",  Ar: "مدير النظام"),
            (Id: AppRoles.SalesManagerRoleId,  Name: AppRoles.SalesManager,  En: "Sales Manager",  Ar: "مدير المبيعات"),
            (Id: AppRoles.SalesEmployeeRoleId, Name: AppRoles.SalesEmployee, En: "Sales Employee", Ar: "موظف مبيعات")
        };

        foreach (var r in roles)
        {
            if (await roleManager.RoleExistsAsync(r.Name)) continue;

            var result = await roleManager.CreateAsync(new ApplicationRole(r.Name)
            {
                Id = r.Id,
                DisplayNameEn = r.En,
                DisplayNameAr = r.Ar
            });

            if (result.Succeeded)
                logger.LogInformation("Role {Role} created.", r.Name);
            else
                logger.LogError("Could not create role {Role}: {Errors}", r.Name, string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    private static async Task EnsureAdminAsync(IServiceProvider sp, IConfiguration config, ILogger logger)
    {
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();

        var admins = await userManager.GetUsersInRoleAsync(AppRoles.Admin);
        if (admins.Count > 0) return;

        var section = config.GetSection("SeedAdmin");
        var userName = section["UserName"];
        var email = section["Email"];
        var fullName = section["FullName"] ?? "System Administrator";
        var password = section["Password"];

        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "No Admin user exists. Set SeedAdmin:UserName, SeedAdmin:Email and SeedAdmin:Password " +
                "(user-secrets in Development, environment variables in Production) and restart.");
            return;
        }

        var user = await userManager.FindByNameAsync(userName) ?? await userManager.FindByEmailAsync(email);
        if (user == null)
        {
            user = new ApplicationUser
            {
                UserName = userName,
                Email = email,
                EmailConfirmed = true,
                FullName = fullName,
                FullNameAr = "مدير النظام",
                JobTitle = "Administrator",
                PreferredLanguage = "en",
                IsActive = true,
                LockoutEnabled = true
            };

            var create = await userManager.CreateAsync(user, password);
            if (!create.Succeeded)
            {
                logger.LogError("Could not create Admin user: {Errors}", string.Join("; ", create.Errors.Select(e => e.Description)));
                return;
            }
        }

        var addRole = await userManager.AddToRoleAsync(user, AppRoles.Admin);
        if (addRole.Succeeded)
            logger.LogInformation("Admin user {UserName} created.", userName);
        else
            logger.LogError("Could not add Admin role: {Errors}", string.Join("; ", addRole.Errors.Select(e => e.Description)));
    }
}
