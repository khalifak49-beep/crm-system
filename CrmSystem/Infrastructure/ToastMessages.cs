using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace CrmSystem.Infrastructure;

/// <summary>
/// Success / error messages that survive a redirect (Post-Redirect-Get) and are
/// shown as toasts by the layout.
///   TempData.AddToast("success", L["Js.Saved"]);
/// </summary>
public static class ToastMessages
{
    private const string Key = "crm:toasts";

    public sealed record Toast(string Type, string Text);

    public static void AddToast(this ITempDataDictionary tempData, string type, string text)
    {
        var list = Read(tempData, peek: true);
        list.Add(new Toast(type, text));
        tempData[Key] = JsonSerializer.Serialize(list);
    }

    public static List<Toast> TakeToasts(this ITempDataDictionary tempData) => Read(tempData, peek: false);

    private static List<Toast> Read(ITempDataDictionary tempData, bool peek)
    {
        var raw = (peek ? tempData.Peek(Key) : tempData[Key]) as string;
        if (string.IsNullOrEmpty(raw)) return new List<Toast>();
        try { return JsonSerializer.Deserialize<List<Toast>>(raw) ?? new List<Toast>(); }
        catch (JsonException) { return new List<Toast>(); }
    }
}
