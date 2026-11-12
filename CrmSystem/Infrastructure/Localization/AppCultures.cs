using System.Globalization;
using Microsoft.AspNetCore.Localization;

namespace CrmSystem.Infrastructure.Localization;

/// <summary>
/// The two supported cultures.
/// English: en-GB (dd/MM/yyyy).
/// Arabic : ar-OM with Gregorian calendar and Latin digits / "." decimal separator,
///          so numbers and dates bind and display the same in both languages
///          (month and day names are still Arabic).
/// </summary>
public static class AppCultures
{
    public const string English = "en-GB";
    public const string Arabic = "ar-OM";

    public static readonly CultureInfo EnglishCulture = BuildEnglish();
    public static readonly CultureInfo ArabicCulture = BuildArabic();

    public static IList<CultureInfo> Supported => new[] { EnglishCulture, ArabicCulture };

    /// <summary>"ar" / "en" -> culture name.</summary>
    public static string FromLanguage(string? lang) => lang == "ar" ? Arabic : English;

    public static bool IsArabic => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    private static CultureInfo BuildEnglish()
    {
        var c = (CultureInfo)CultureInfo.GetCultureInfo(English).Clone();
        c.DateTimeFormat.ShortDatePattern = "dd/MM/yyyy";
        c.DateTimeFormat.ShortTimePattern = "hh:mm tt";
        c.DateTimeFormat.AMDesignator = "AM";
        c.DateTimeFormat.PMDesignator = "PM";
        return c;
    }

    private static CultureInfo BuildArabic()
    {
        var c = (CultureInfo)CultureInfo.GetCultureInfo(Arabic).Clone();
        c.NumberFormat = (NumberFormatInfo)CultureInfo.GetCultureInfo(English).NumberFormat.Clone();
        try
        {
            var gregorian = c.OptionalCalendars.OfType<GregorianCalendar>().FirstOrDefault() ?? new GregorianCalendar();
            c.DateTimeFormat.Calendar = gregorian;
        }
        catch (ArgumentException)
        {
            // keep the default calendar if Gregorian is not accepted
        }
        c.DateTimeFormat.ShortDatePattern = "dd/MM/yyyy";
        c.DateTimeFormat.ShortTimePattern = "hh:mm tt";
        return c;
    }
}

/// <summary>
/// When the user has no language cookie, use System.DefaultLanguage from Settings.
/// </summary>
public sealed class SettingsDefaultCultureProvider : RequestCultureProvider
{
    public override async Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var settings = httpContext.RequestServices.GetRequiredService<IAppSettings>();
        var snapshot = await settings.GetAsync(httpContext.RequestAborted);
        var culture = AppCultures.FromLanguage(snapshot.DefaultLanguage);
        return new ProviderCultureResult(culture, culture);
    }
}
