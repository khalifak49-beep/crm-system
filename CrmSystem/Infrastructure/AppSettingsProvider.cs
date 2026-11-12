using System.Globalization;
using CrmSystem.Data;
using CrmSystem.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CrmSystem.Infrastructure;

/// <summary>
/// Immutable snapshot of dbo.SystemSettings + time-zone helpers.
/// All "local" times are in the configured time zone (default Asia/Muscat).
/// </summary>
public sealed class SettingsSnapshot
{
    private readonly IReadOnlyDictionary<string, string?> _values;

    public SettingsSnapshot(IReadOnlyDictionary<string, string?> values)
    {
        _values = values;
        TimeZone = ResolveTimeZone(Get(SettingKeys.TimeZone, "Asia/Muscat"));
    }

    public string Get(string key, string defaultValue = "") =>
        _values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v! : defaultValue;

    public int GetInt(string key, int defaultValue) =>
        int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : defaultValue;

    public bool GetBool(string key, bool defaultValue) =>
        bool.TryParse(Get(key), out var v) ? v : defaultValue;

    // ----- Typed settings -----
    public string CompanyName => Get(SettingKeys.CompanyName, "CRM System");
    public string CompanyNameAr => Get(SettingKeys.CompanyNameAr, CompanyName);
    public string LogoPath => Get(SettingKeys.CompanyLogoPath);
    public string DefaultLanguage => Get(SettingKeys.DefaultLanguage, "en") == "ar" ? "ar" : "en";
    public string TimeZoneId => Get(SettingKeys.TimeZone, "Asia/Muscat");
    public string Currency => Get(SettingKeys.Currency, "OMR");
    public int PollIntervalSeconds => Math.Clamp(GetInt(SettingKeys.PollIntervalSeconds, 30), 10, 600);
    public bool InAppEnabled => GetBool(SettingKeys.InAppEnabled, true);
    public bool EmailEnabled => GetBool(SettingKeys.EmailEnabled, false);
    public bool BrowserEnabled => GetBool(SettingKeys.BrowserEnabled, true);
    public int FollowUpReminderMinutes => Math.Clamp(GetInt(SettingKeys.FollowUpMinutesBefore, 1440), 0, 43200);
    public bool EmployeesSeeOnlyAssignedClients => GetBool(SettingKeys.EmployeesSeeOnlyAssignedClients, true);

    public string CompanyDisplayName(bool arabic) => arabic ? CompanyNameAr : CompanyName;

    // ----- Time helpers -----
    public TimeZoneInfo TimeZone { get; }

    public DateTime NowLocal => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZone);

    public DateOnly TodayLocal => DateOnly.FromDateTime(NowLocal);

    public DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZone);

    public DateTime ToUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeZone);

    /// <summary>UTC range [start, end) covering one local calendar day.</summary>
    public (DateTime StartUtc, DateTime EndUtc) LocalDayRangeUtc(DateOnly day) =>
        (ToUtc(day.ToDateTime(TimeOnly.MinValue)), ToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue)));

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception) { }
        try { return TimeZoneInfo.FindSystemTimeZoneById("Arabian Standard Time"); }
        catch (Exception) { }
        return TimeZoneInfo.CreateCustomTimeZone("Asia/Muscat", TimeSpan.FromHours(4), "Asia/Muscat", "Asia/Muscat");
    }
}

public interface IAppSettings
{
    Task<SettingsSnapshot> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Call after saving settings so the next request reloads them.</summary>
    void Invalidate();
}

/// <summary>
/// Reads all SystemSettings once and caches them for 5 minutes (singleton).
/// </summary>
public sealed class AppSettingsProvider : IAppSettings
{
    private const string CacheKey = "crm:system-settings";
    private readonly IMemoryCache _cache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AppSettingsProvider> _logger;

    public AppSettingsProvider(IMemoryCache cache, IServiceScopeFactory scopeFactory, ILogger<AppSettingsProvider> logger)
    {
        _cache = cache;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<SettingsSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(CacheKey, out SettingsSnapshot? cached) && cached != null)
            return cached;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var values = await db.SystemSettings.AsNoTracking()
                .Where(s => !s.IsEncrypted) // secrets are read separately, never cached in clear text
                .ToDictionaryAsync(s => s.SettingKey, s => s.SettingValue, StringComparer.OrdinalIgnoreCase, cancellationToken);

            var snapshot = new SettingsSnapshot(values);
            _cache.Set(CacheKey, snapshot, TimeSpan.FromMinutes(5));
            return snapshot;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load system settings; using defaults.");
            return new SettingsSnapshot(new Dictionary<string, string?>());
        }
    }

    public void Invalidate() => _cache.Remove(CacheKey);
}
