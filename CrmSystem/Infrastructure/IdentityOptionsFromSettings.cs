using System.Globalization;
using CrmSystem.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace CrmSystem.Infrastructure;

/// <summary>
/// Applies the security values from Settings (password length, lockout) to ASP.NET Core Identity.
/// Reads dbo.SystemSettings with a plain SqlConnection: EF Core itself needs IdentityOptions
/// while building its model, so going through ApplicationDbContext here would deadlock.
/// Identity options are built once - changes take effect after the site restarts.
/// </summary>
public sealed class IdentityOptionsFromSettings : IPostConfigureOptions<IdentityOptions>
{
    private readonly IConfiguration _config;
    private readonly ILogger<IdentityOptionsFromSettings> _logger;

    public IdentityOptionsFromSettings(IConfiguration config, ILogger<IdentityOptionsFromSettings> logger)
    {
        _config = config;
        _logger = logger;
    }

    public void PostConfigure(string? name, IdentityOptions options)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var connection = new SqlConnection(_config.GetConnectionString("CRMDatabase"));
            connection.Open();
            using var command = new SqlCommand("SELECT SettingKey, SettingValue FROM dbo.SystemSettings WHERE GroupName = @group", connection);
            command.Parameters.Add(new SqlParameter("@group", System.Data.SqlDbType.VarChar, 50) { Value = "Security" });
            using var reader = command.ExecuteReader();
            while (reader.Read()) values[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read security settings; using defaults from appsettings.json.");
            return;
        }

        int Get(string key, int fallback, int min, int max) =>
            values.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Math.Clamp(n, min, max) : fallback;

        options.Password.RequiredLength = Get(SettingKeys.PasswordMinLength, options.Password.RequiredLength, 6, 64);
        options.Lockout.MaxFailedAccessAttempts = Get(SettingKeys.MaxFailedAccessAttempts, options.Lockout.MaxFailedAccessAttempts, 3, 20);
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(Get(SettingKeys.LockoutMinutes, (int)options.Lockout.DefaultLockoutTimeSpan.TotalMinutes, 1, 1440));
    }
}
