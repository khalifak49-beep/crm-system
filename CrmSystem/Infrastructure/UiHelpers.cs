using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;

namespace CrmSystem.Infrastructure;

/// <summary>Helpers for building translated drop-down lists in views.</summary>
public static class UiHelpers
{
    /// <summary>
    /// Options for an enum, text from translation keys "{prefix}.{numeric value}"
    /// e.g. EnumOptions&lt;ProjectStatus&gt;(L, "ProjectStatus", model.Status).
    /// </summary>
    public static List<SelectListItem> EnumOptions<TEnum>(IStringLocalizer l, string prefix, TEnum? selected = null, bool includeEmpty = false, string? emptyText = null)
        where TEnum : struct, Enum
    {
        var items = Enum.GetValues<TEnum>().Select(v =>
        {
            var number = Convert.ToInt32(v);
            return new SelectListItem(l[$"{prefix}.{number}"], number.ToString(), selected.HasValue && selected.Value.Equals(v));
        }).ToList();
        if (includeEmpty) items.Insert(0, new SelectListItem(emptyText ?? l["Common.All"], "", !selected.HasValue));
        return items;
    }

    public static string Initials(string? name)
    {
        var parts = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant()
        };
    }
}
