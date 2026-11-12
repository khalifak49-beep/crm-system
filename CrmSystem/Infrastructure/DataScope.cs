using CrmSystem.Models.Entities;

namespace CrmSystem.Infrastructure;

/// <summary>
/// Row-level access rules (who may see which data). Use these everywhere so the
/// rule lives in one place:
///   Admin / Sales Manager : everything.
///   Sales Employee        : clients he owns (AccountManagerId) or is assigned to
///                           (ClientAssignments), when the setting
///                           Security.EmployeesSeeOnlyAssignedClients = true.
/// </summary>
public static class DataScope
{
    public static IQueryable<Client> VisibleTo(this IQueryable<Client> query, ICurrentUser user, SettingsSnapshot settings)
    {
        if (user.CanSeeAllData || !settings.EmployeesSeeOnlyAssignedClients)
            return query;
        return query.OwnedOrAssignedTo(user.UserId ?? string.Empty);
    }

    public static IQueryable<Client> OwnedOrAssignedTo(this IQueryable<Client> query, string userId) =>
        query.Where(c => c.AccountManagerId == userId || c.Assignments.Any(a => a.UserId == userId));

    /// <summary>
    /// For data owned by a person (activities, follow-ups, projects):
    /// managers may filter by any employee; employees are always limited to themselves.
    /// Returns the user id to filter on, or null for "all employees".
    /// </summary>
    public static string? EffectiveEmployeeFilter(this ICurrentUser user, string? requestedUserId) =>
        user.CanSeeAllData
            ? (string.IsNullOrWhiteSpace(requestedUserId) ? null : requestedUserId)
            : user.UserId;
}
