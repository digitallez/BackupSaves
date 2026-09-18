using System.Reflection;

namespace WpfApp_BackupSaves;

public static class AppVersion
{
    /// <summary>Display version, e.g. 1.0.3 or 1.0.3-debug</summary>
    public static string Current { get; } = Resolve();

    /// <summary>Numeric core for comparisons, e.g. 1.0.3 (strips -debug).</summary>
    public static Version Numeric { get; } = ParseCore(Current) ?? new Version(0, 0, 0);

    /// <summary>True when InformationalVersion has the -debug marker.</summary>
    public static bool IsDebug { get; } =
        Current.Contains("-debug", StringComparison.OrdinalIgnoreCase);

    private static string Resolve()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }

        var v = asm.GetName().Version;
        return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    public static Version? ParseCore(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var s = raw.Trim();
        if (s.StartsWith('v') || s.StartsWith('V'))
            s = s[1..];

        var cut = s.IndexOfAny(['-', '+']);
        if (cut >= 0)
            s = s[..cut];

        return Version.TryParse(s, out var v) ? v : null;
    }

    public static bool IsNewer(string remoteTagOrVersion, Version current)
    {
        var remote = ParseCore(remoteTagOrVersion);
        return remote is not null && remote > current;
    }

    /// <summary>
    /// Offer update when remote is newer, or when local is Debug and remote equals the same number
    /// (so 1.0.0-debug can replace itself with Release 1.0.0).
    /// </summary>
    public static bool ShouldOfferUpdate(string remoteTagOrVersion, Version current, bool localIsDebug)
    {
        var remote = ParseCore(remoteTagOrVersion);
        if (remote is null)
            return false;
        if (remote > current)
            return true;
        return localIsDebug && remote == current;
    }
}
