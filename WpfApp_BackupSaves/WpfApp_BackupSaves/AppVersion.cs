using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace WpfApp_BackupSaves;

public static class AppVersion
{
    /// <summary>Display version, e.g. 1.0.3 or 1.0.3-debug-260919153012</summary>
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

    /// <summary>Reads ProductVersion / InformationalVersion from an exe on disk.</summary>
    public static string? ReadFromExe(string exePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                return null;

            var vi = FileVersionInfo.GetVersionInfo(exePath);
            var raw = vi.ProductVersion;
            if (string.IsNullOrWhiteSpace(raw))
                raw = vi.FileVersion;
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var plus = raw.IndexOf('+');
            return plus >= 0 ? raw[..plus] : raw.Trim();
        }
        catch
        {
            return null;
        }
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

    /// <summary>
    /// Debug stamp from <c>1.0.0-debug-yyMMddHHmmss</c>, or null when absent.
    /// </summary>
    public static long? ParseDebugStamp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var s = raw.Trim();
        const string marker = "-debug-";
        var idx = s.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;

        var stamp = s[(idx + marker.Length)..];
        var end = stamp.IndexOfAny(['+', '-', ' ']);
        if (end >= 0)
            stamp = stamp[..end];

        return long.TryParse(stamp, out var n) ? n : null;
    }

    public static bool IsNewer(string remoteTagOrVersion, Version current)
    {
        var remote = ParseCore(remoteTagOrVersion);
        return remote is not null && remote > current;
    }

    /// <summary>
    /// True when <paramref name="candidate"/> is a newer build than <paramref name="current"/>,
    /// including Debug stamps (<c>1.0.0-debug-yyMMddHHmmss</c>).
    /// Same numeric + newer stamp → newer; Debug at the same number is newer than Release.
    /// </summary>
    public static bool IsBuildNewer(string? candidate, string? current)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(current))
            return false;

        var cNum = ParseCore(candidate);
        var curNum = ParseCore(current);
        if (cNum is null || curNum is null)
            return false;

        if (cNum > curNum)
            return true;
        if (cNum < curNum)
            return false;

        var cStamp = ParseDebugStamp(candidate);
        var curStamp = ParseDebugStamp(current);
        if (cStamp is not null && curStamp is not null)
            return cStamp > curStamp;

        // Debug (stamp) ranks above Release at the same number (1.0.0-debug > 1.0.0).
        if (cStamp is not null && curStamp is null)
            return true;

        return false;
    }

    /// <summary>
    /// Offer a GitHub Release when its numeric version is strictly greater than the installed one.
    /// Same-number Release is not offered over a Debug build (Debug ranks higher).
    /// </summary>
    public static bool ShouldOfferUpdate(string remoteTagOrVersion, Version current)
    {
        return IsNewer(remoteTagOrVersion, current);
    }
}
