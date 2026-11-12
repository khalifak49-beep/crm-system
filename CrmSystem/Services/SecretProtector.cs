using Microsoft.AspNetCore.DataProtection;

namespace CrmSystem.Services;

/// <summary>
/// Encrypts / decrypts secret settings (SMTP password) with ASP.NET Core Data Protection.
/// Keys live in dbo.DataProtectionKeys, so values stay readable after restarts.
/// </summary>
public sealed class SecretProtector
{
    private readonly IDataProtector _protector;
    private readonly ILogger<SecretProtector> _logger;

    public SecretProtector(IDataProtectionProvider provider, ILogger<SecretProtector> logger)
    {
        _protector = provider.CreateProtector("CrmSystem.SystemSettings.Secrets.v1");
        _logger = logger;
    }

    public string Protect(string plain) => _protector.Protect(plain);

    public string? Unprotect(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return null;
        try { return _protector.Unprotect(cipher); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not decrypt a secret setting (keys changed?). Re-enter the value in Settings.");
            return null;
        }
    }
}
