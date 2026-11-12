using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Localization;

namespace CrmSystem.Infrastructure.Localization;

/// <summary>Marker type: inject IStringLocalizer&lt;SharedResource&gt; anywhere.</summary>
public sealed class SharedResource { }

/// <summary>
/// Loads translations from Resources/i18n/{lang}.json (flat "Key": "Text" pairs).
/// One shared dictionary per language for the whole app - simple to edit and review.
/// Missing keys fall back to English, then to the key itself.
/// </summary>
public sealed class JsonStringLocalizerFactory : IStringLocalizerFactory
{
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _languages;
    private readonly ILogger<JsonStringLocalizerFactory> _logger;
    private readonly ConcurrentDictionary<string, byte> _reportedMissing = new();

    public JsonStringLocalizerFactory(IWebHostEnvironment env, ILogger<JsonStringLocalizerFactory> logger)
    {
        _logger = logger;
        var folder = Path.Combine(env.ContentRootPath, "Resources", "i18n");
        // Files are named {lang}.json or {lang}.{module}.json (e.g. en.json, en.crm.json); all files of a language are merged.
        var merged = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(folder))
        {
            foreach (var file in Directory.GetFiles(folder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var lang = Path.GetFileName(file).Split('.')[0];
                var json = File.ReadAllText(file);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json, new JsonSerializerOptions
                {
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                }) ?? new Dictionary<string, string>();
                if (!merged.TryGetValue(lang, out var target))
                    merged[lang] = target = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var pair in dict) target[pair.Key] = pair.Value;
            }
        }
        else
        {
            logger.LogWarning("Localization folder not found: {Folder}", folder);
        }

        _languages = merged.ToDictionary(p => p.Key, p => (IReadOnlyDictionary<string, string>)p.Value, StringComparer.OrdinalIgnoreCase);
    }

    public IStringLocalizer Create(Type resourceSource) => new JsonStringLocalizer(this);

    public IStringLocalizer Create(string baseName, string location) => new JsonStringLocalizer(this);

    internal string? Find(string key, string lang)
    {
        if (_languages.TryGetValue(lang, out var dict) && dict.TryGetValue(key, out var value))
            return value;
        if (lang != "en" && _languages.TryGetValue("en", out var en) && en.TryGetValue(key, out var enValue))
        {
            Report(key, lang);
            return enValue;
        }
        Report(key, lang);
        return null;
    }

    internal IEnumerable<KeyValuePair<string, string>> All(string lang)
    {
        if (_languages.TryGetValue("en", out var en))
        {
            _languages.TryGetValue(lang, out var current);
            foreach (var pair in en)
                yield return new(pair.Key, current != null && current.TryGetValue(pair.Key, out var v) ? v : pair.Value);
        }
    }

    private void Report(string key, string lang)
    {
        if (_reportedMissing.TryAdd(lang + ":" + key, 0))
            _logger.LogDebug("Missing translation '{Key}' for language '{Lang}'", key, lang);
    }
}

public sealed class JsonStringLocalizer : IStringLocalizer
{
    private readonly JsonStringLocalizerFactory _factory;

    public JsonStringLocalizer(JsonStringLocalizerFactory factory) => _factory = factory;

    private static string CurrentLang => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

    public LocalizedString this[string name]
    {
        get
        {
            var value = _factory.Find(name, CurrentLang);
            return new LocalizedString(name, value ?? name, resourceNotFound: value == null);
        }
    }

    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var value = _factory.Find(name, CurrentLang);
            var format = value ?? name;
            return new LocalizedString(name, string.Format(CultureInfo.CurrentCulture, format, arguments), resourceNotFound: value == null);
        }
    }

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
        _factory.All(CurrentLang).Select(p => new LocalizedString(p.Key, p.Value, false));
}
