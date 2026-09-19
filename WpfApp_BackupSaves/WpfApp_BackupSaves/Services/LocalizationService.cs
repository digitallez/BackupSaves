using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace WpfApp_BackupSaves.Services;

/// <summary>
/// One locale = one JSON in Locales/. Filename is arbitrary.
/// Meta keys (not shown as UI strings): @id, @code, @name, @flag, @match.
/// </summary>
public sealed record LocaleInfo(
    string Id,
    string DisplayCode,
    string DisplayName,
    string FlagUri,
    string FilePath,
    IReadOnlyList<string> MatchTags);

public sealed class LocalizationService : INotifyPropertyChanged
{
    public const string MetaId = "@id";
    public const string MetaCode = "@code";
    public const string MetaName = "@name";
    public const string MetaFlag = "@flag";
    public const string MetaMatch = "@match";

    public static LocalizationService Instance { get; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true
    };

    private Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);
    private List<LocaleInfo> _locales = [];

    /// <summary>Stable id of the active locale (from @id or file name).</summary>
    public string Language { get; private set; } = "";

    public static string LocalesDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Locales");

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    public string this[string key] =>
        string.IsNullOrEmpty(key) || key.StartsWith('@') ? "" :
        _map.TryGetValue(key, out var value) ? value : key;

    public string T(string key, params object[] args)
    {
        var template = this[key];
        if (args.Length == 0)
            return template;
        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    public static string Text(string key, params object[] args) => Instance.T(key, args);

    public IReadOnlyList<LocaleInfo> DiscoverLanguages()
    {
        var dir = LocalesDirectory;
        if (!Directory.Exists(dir))
        {
            _locales = [];
            return _locales;
        }

        var list = new List<LocaleInfo>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var info = TryReadLocaleInfo(path);
            if (info is not null)
                list.Add(info);
        }

        _locales = list
            .OrderBy(l => l.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(l => l.DisplayCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return _locales;
    }

    public IReadOnlyList<LocaleInfo> GetCachedLanguages() =>
        _locales.Count > 0 ? _locales : DiscoverLanguages();

    /// <summary>
    /// Prefer saved id; else first locale whose @match fits OS culture;
    /// else first available (no invented aliases).
    /// </summary>
    public string ResolveInitialLanguage(string? savedId)
    {
        var locales = DiscoverLanguages();
        if (locales.Count == 0)
            return "";

        if (!string.IsNullOrWhiteSpace(savedId))
        {
            var saved = locales.FirstOrDefault(l =>
                l.Id.Equals(savedId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (saved is not null)
                return saved.Id;
        }

        var os = CultureInfo.CurrentUICulture;
        var osTags = new[]
        {
            os.Name,
            os.TwoLetterISOLanguageName
        };

        foreach (var loc in locales)
        {
            if (loc.MatchTags.Any(tag =>
                    osTags.Any(o => o.Equals(tag, StringComparison.OrdinalIgnoreCase))))
                return loc.Id;
        }

        // No locale claimed this OS culture — pick a neutral default, not "first alphabetically"
        return locales.FirstOrDefault(l => l.Id.Equals("en", StringComparison.OrdinalIgnoreCase))?.Id
               ?? locales.FirstOrDefault(l => l.Id.Equals("ru", StringComparison.OrdinalIgnoreCase))?.Id
               ?? locales[0].Id;
    }

    public void SetLanguage(string? idOrCode)
    {
        var locales = GetCachedLanguages();
        if (locales.Count == 0)
        {
            DiscoverLanguages();
            locales = _locales;
        }

        LocaleInfo? chosen = null;
        if (!string.IsNullOrWhiteSpace(idOrCode))
        {
            var key = idOrCode.Trim();
            chosen = locales.FirstOrDefault(l =>
                         l.Id.Equals(key, StringComparison.OrdinalIgnoreCase))
                     ?? locales.FirstOrDefault(l =>
                         l.DisplayCode.Equals(key, StringComparison.OrdinalIgnoreCase));
        }

        chosen ??= locales.Count > 0 ? locales[0] : null;
        if (chosen is null)
        {
            _map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Language = "";
            NotifyLanguageChanged();
            return;
        }

        LoadFromFile(chosen.FilePath);
        Language = chosen.Id;
        NotifyLanguageChanged();
    }

    private void NotifyLanguageChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private static LocaleInfo? TryReadLocaleInfo(string path)
    {
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(
                           File.ReadAllText(path), JsonOptions)
                       ?? new Dictionary<string, string>();

            var fileStem = Path.GetFileNameWithoutExtension(path) ?? "";
            var id = Meta(dict, MetaId) ?? fileStem;
            if (string.IsNullOrWhiteSpace(id))
                return null;

            id = id.Trim();
            var code = (Meta(dict, MetaCode) ?? id.ToUpperInvariant()).Trim();
            var name = (Meta(dict, MetaName) ?? code).Trim();
            var flagName = Meta(dict, MetaFlag);
            var matchRaw = Meta(dict, MetaMatch) ?? "";

            var tags = ParseMatchTags(matchRaw, code, id);
            var flagUri = ResolveFlagUri(flagName, Path.GetDirectoryName(path)!);

            return new LocaleInfo(id, code, name, flagUri, path, tags);
        }
        catch
        {
            return null;
        }
    }

    private static string? Meta(Dictionary<string, string> dict, string key) =>
        dict.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    private static IReadOnlyList<string> ParseMatchTags(string matchRaw, string code, string id)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in matchRaw.Split([',', '|', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            tags.Add(part);
        // Also allow matching by explicit @code / @id as written in the file
        if (!string.IsNullOrWhiteSpace(code))
            tags.Add(code);
        if (!string.IsNullOrWhiteSpace(id))
            tags.Add(id);
        return tags.ToList();
    }

    private static string ResolveFlagUri(string? flagName, string localesDir)
    {
        if (string.IsNullOrWhiteSpace(flagName))
            return "";

        flagName = flagName.Trim();
        var path = Path.IsPathRooted(flagName)
            ? flagName
            : Path.Combine(localesDir, flagName);
        return File.Exists(path) ? path : "";
    }

    private void LoadFromFile(string path)
    {
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(
                           File.ReadAllText(path), JsonOptions)
                       ?? new Dictionary<string, string>();

            _map = dict
                .Where(kv => !kv.Key.StartsWith('@'))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            _map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
