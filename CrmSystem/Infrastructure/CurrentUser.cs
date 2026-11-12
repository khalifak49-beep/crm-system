using System.Security.Claims;
using CrmSystem.Models.Identity;

namespace CrmSystem.Infrastructure;

/// <summary>
/// Who is doing the current request. Used by ApplicationDbContext to fill
/// CreatedBy / UpdatedBy / DeletedBy and AuditLogs.
/// In background jobs (no HTTP request) all values are null = "system".
/// </summary>
public interface ICurrentUser
{
    string? UserId { get; }
    string? UserName { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
    bool IsAdmin { get; }
    bool IsManager { get; }
    /// <summary>Admin or Sales Manager: can see all employees' data.</summary>
    bool CanSeeAllData { get; }
}

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public string? UserId => Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

    public string? UserName => Principal?.Identity?.Name;

    public string? IpAddress => _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent
    {
        get
        {
            var ua = _accessor.HttpContext?.Request.Headers.UserAgent.ToString();
            if (string.IsNullOrEmpty(ua)) return null;
            return ua.Length > 400 ? ua[..400] : ua;
        }
    }

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;

    public bool IsAdmin => IsInRole(AppRoles.Admin);

    public bool IsManager => IsInRole(AppRoles.SalesManager);

    public bool CanSeeAllData => IsAdmin || IsManager;
}
