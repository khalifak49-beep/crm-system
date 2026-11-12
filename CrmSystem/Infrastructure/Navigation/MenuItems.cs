using CrmSystem.Models.Identity;

namespace CrmSystem.Infrastructure.Navigation;

/// <summary>
/// One sidebar entry. Icon = Framework7 icon name (iOS style).
/// IsAvailable = false shows the item greyed with a "Soon" badge until its phase is built.
/// </summary>
public sealed record MenuItem(
    string Key,
    string Controller,
    string Action,
    string Icon,
    string[] Roles,
    bool IsAvailable,
    string Section);

public static class MenuItems
{
    private static readonly string[] Everyone = AppRoles.All.ToArray();
    private static readonly string[] AdminOnly = { AppRoles.Admin };

    public static readonly IReadOnlyList<MenuItem> All = new List<MenuItem>
    {
        new("Dashboard",     "Dashboard",     "Index", "square_grid_2x2",          Everyone,  true,  "Main"),
        new("Clients",       "Clients",       "Index", "building_2",               Everyone,  true,  "Main"),
        new("Contacts",      "Contacts",      "Index", "person_2",                 Everyone,  true,  "Main"),
        new("Activities",    "Activities",    "Index", "bubble_left_bubble_right", Everyone,  true,  "Main"),
        new("FollowUps",     "FollowUps",     "Index", "calendar",                 Everyone,  true,  "Main"),
        new("Projects",      "Projects",      "Index", "briefcase",                Everyone,  true,  "Sales"),
        new("Quotations",    "Quotations",    "Index", "doc_text",                 Everyone,  true,  "Sales"),
        new("Notifications", "Notifications", "Index", "bell",                     Everyone,  true,  "Sales"),
        new("Reports",       "Reports",       "Index", "chart_bar_alt_fill",       Everyone,  true,  "Sales"),
        new("Users",         "Users",         "Index", "person_crop_circle_badge_checkmark", AdminOnly, true,  "Admin"),
        new("Settings",      "Settings",      "Index", "gear_alt",                 AdminOnly, true,  "Admin")
    };
}
