using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BackupSaves.Core.Models;
using BackupSaves.Core.Services;
using Microsoft.Win32;

namespace WpfApp_BackupSaves.Services;

public sealed class SteamGame
{
    public int AppId { get; init; }
    public string Name { get; init; } = "";
    public string Command => $"steam://rungameid/{AppId}";
    public string? IconPath { get; init; }
    public string? InstallDir { get; init; }
}

/// <summary>Launch command stored on a profile: exe, command line, or a Steam game.</summary>
public static class GameLaunchService
{
    private static readonly object Sync = new();
    private static readonly Dictionary<int, SteamGame> Games = new();
    private static readonly HashSet<int> MissingApps = [];
    private static readonly ConcurrentDictionary<int, string> ExeByApp = new();
    private static readonly ConcurrentDictionary<string, string> ShortcutTargets = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, ImageSource> IconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> IconMiss = new(StringComparer.OrdinalIgnoreCase);

    private static string? _steamPath;
    private static List<string> _libraries = [];
    private static DateTimeOffset _steamReadUtc;

    private static readonly string[] LaunchExtensions = [".exe", ".lnk", ".bat", ".cmd", ".com"];

    private readonly record struct ParsedCommand(string FileName, string Arguments, int? SteamAppId);

    public static string? Command(BackupProfile profile)
    {
        var raw = profile.LaunchCommand?.Trim();
        return string.IsNullOrEmpty(raw) ? null : raw;
    }

    public static bool HasCommand(BackupProfile profile) => Command(profile) is not null;

    public static string DisplayName(string command)
    {
        if (!TryParse(command, out var parsed))
            return command.Trim();

        if (parsed.SteamAppId is int id)
        {
            var game = GetOrLoad(id);
            return string.IsNullOrWhiteSpace(game?.Name) ? $"Steam {id}" : game!.Name;
        }

        var file = parsed.FileName;
        if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var target = CachedShortcut(file);
            if (!string.IsNullOrEmpty(target))
                file = target;
        }

        var name = Path.GetFileName(file);
        return string.IsNullOrEmpty(name) ? command.Trim() : name;
    }

    public static ImageSource? GetIcon(string command)
    {
        var key = command.Trim();
        if (key.Length == 0)
            return null;
        if (IconCache.TryGetValue(key, out var cached))
            return cached;
        if (IconMiss.ContainsKey(key))
            return null;

        ImageSource? created = null;
        try
        {
            created = CreateIcon(key);
        }
        catch
        {
            created = null;
        }

        if (created is null)
        {
            IconMiss[key] = 1;
            return null;
        }

        if (created.CanFreeze && !created.IsFrozen)
            created.Freeze();
        IconCache[key] = created;
        return created;
    }

    /// <summary>Icon for Steam library list / profile row. Prefers Steam cache/shortcut, then game exe.</summary>
    public static ImageSource? GetLibraryIcon(SteamGame game)
    {
        var key = game.Command;
        if (IconCache.TryGetValue(key, out var cached))
            return cached;

        ImageSource? created = null;
        try
        {
            if (!string.IsNullOrEmpty(game.IconPath))
                created = LoadImageFile(game.IconPath);

            if (created is null)
            {
                var exe = PrimaryExe(game);
                if (!string.IsNullOrEmpty(exe))
                    created = LoadExeIcon(exe);
            }
        }
        catch
        {
            created = null;
        }

        if (created is null)
            return null;

        if (created.CanFreeze && !created.IsFrozen)
            created.Freeze();
        IconCache[key] = created;
        return created;
    }

    public static bool IsRunning(string command)
    {
        try
        {
            if (!TryParse(command, out var parsed))
                return false;

            if (parsed.SteamAppId is int id && IsSteamRegistryRunning(id))
                return true;

            var watch = WatchTarget(parsed);
            if (string.IsNullOrWhiteSpace(watch))
                return false;

            return ProcessWatchService.IsAnyMatchingProcessRunning(watch, TimeSpan.FromSeconds(2));
        }
        catch
        {
            return false;
        }
    }

    public static bool TryStart(string command, out string? error)
    {
        error = null;
        if (!TryParse(command, out var parsed))
        {
            error = LocalizationService.Text("profile.launchFailed", command.Trim());
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo { UseShellExecute = true };
            if (parsed.SteamAppId is not null)
            {
                psi.FileName = parsed.FileName;
            }
            else
            {
                psi.FileName = parsed.FileName;
                if (!string.IsNullOrEmpty(parsed.Arguments))
                    psi.Arguments = parsed.Arguments;
                var dir = Path.GetDirectoryName(parsed.FileName);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    psi.WorkingDirectory = dir;
            }

            Process.Start(psi);
            var watch = WatchTarget(parsed);
            if (!string.IsNullOrWhiteSpace(watch))
                ProcessWatchService.Invalidate(watch);
            return true;
        }
        catch (Exception ex)
        {
            error = LocalizationService.Text("profile.launchFailed", ex.Message);
            return false;
        }
    }

    public static IReadOnlyList<SteamGame> LoadSteamGames()
    {
        var (steam, libs) = SteamLibraries();
        var list = new List<SteamGame>();
        if (steam is null)
            return list;

        var seen = new HashSet<int>();
        foreach (var lib in libs)
        {
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps))
                continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(apps, "appmanifest_*.acf");
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                SteamGame? game;
                try
                {
                    game = ParseManifest(steam, lib, file);
                }
                catch
                {
                    continue;
                }

                if (game is null || !seen.Add(game.AppId))
                    continue;
                list.Add(game);
            }
        }

        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        lock (Sync)
        {
            foreach (var game in list)
            {
                Games[game.AppId] = game;
                MissingApps.Remove(game.AppId);
            }
        }

        return list;
    }

    private static ImageSource? CreateIcon(string command)
    {
        if (!TryParse(command, out var parsed))
            return null;

        if (parsed.SteamAppId is int id)
        {
            var game = GetOrLoad(id);
            // Prefer square Steam app icon (.ico / *_icon.*), then the game exe icon.
            // Never use library header/logo art — those are large landscape images.
            if (game?.IconPath is string iconPath)
            {
                var bmp = LoadImageFile(iconPath);
                if (bmp is not null)
                    return bmp;
            }

            var exe = game is null ? null : PrimaryExe(game);
            return string.IsNullOrEmpty(exe) ? null : LoadExeIcon(exe);
        }

        var file = parsed.FileName;
        if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var fromLnk = LoadExeIcon(file);
            if (fromLnk is not null)
                return fromLnk;
            var target = CachedShortcut(file);
            return string.IsNullOrEmpty(target) ? null : LoadExeIcon(target);
        }

        return File.Exists(file) ? LoadExeIcon(file) : null;
    }

    private static string WatchTarget(ParsedCommand parsed)
    {
        if (parsed.SteamAppId is int id)
        {
            var game = GetOrLoad(id);
            return game is null ? "" : PrimaryExe(game) ?? "";
        }

        var file = parsed.FileName;
        if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var target = CachedShortcut(file);
            if (!string.IsNullOrEmpty(target))
                return target;
        }

        return file;
    }

    private static bool TryParse(string? command, out ParsedCommand parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(command))
            return false;

        var text = Environment.ExpandEnvironmentVariables(command.Trim());
        var steam = Regex.Match(text, @"^steam://rungameid/(\d+)", RegexOptions.IgnoreCase);
        if (steam.Success && int.TryParse(steam.Groups[1].Value, out var appId))
        {
            parsed = new ParsedCommand(text, "", appId);
            return true;
        }

        string file;
        string args;
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            if (end > 1)
            {
                file = text[1..end];
                args = text[(end + 1)..].Trim();
            }
            else
            {
                file = text.Trim('"');
                args = "";
            }
        }
        else
        {
            SplitUnquoted(text, out file, out args);
        }

        if (string.IsNullOrWhiteSpace(file))
            return false;

        parsed = new ParsedCommand(file, args, null);
        return true;
    }

    private static void SplitUnquoted(string command, out string file, out string args)
    {
        foreach (var ext in LaunchExtensions)
        {
            var idx = command.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                continue;

            var end = idx + ext.Length;
            if (end < command.Length && command[end] != ' ' && command[end] != '"')
                continue;

            file = command[..end].Trim().Trim('"');
            args = end < command.Length ? command[end..].Trim().Trim('"') : "";
            return;
        }

        var space = command.IndexOf(' ');
        if (space < 0)
        {
            file = command;
            args = "";
            return;
        }

        file = command[..space];
        args = command[(space + 1)..].Trim();
    }

    private static SteamGame? GetOrLoad(int appId)
    {
        lock (Sync)
        {
            if (Games.TryGetValue(appId, out var cached))
                return cached;
            if (MissingApps.Contains(appId))
                return null;
        }

        var game = TryReadApp(appId);
        lock (Sync)
        {
            if (game is null)
                MissingApps.Add(appId);
            else
            {
                MissingApps.Remove(appId);
                Games[appId] = game;
            }
        }

        return game;
    }

    private static SteamGame? TryReadApp(int appId)
    {
        var (steam, libs) = SteamLibraries();
        if (steam is null)
            return null;

        foreach (var lib in libs)
        {
            var acf = Path.Combine(lib, "steamapps", $"appmanifest_{appId}.acf");
            if (!File.Exists(acf))
                continue;
            try
            {
                return ParseManifest(steam, lib, acf);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static (string? Steam, List<string> Libraries) SteamLibraries()
    {
        lock (Sync)
        {
            if (_steamReadUtc != default && DateTimeOffset.UtcNow - _steamReadUtc < TimeSpan.FromMinutes(2))
                return (_steamPath, _libraries.ToList());

            _steamPath = FindSteamInstall();
            _libraries = _steamPath is null ? [] : ReadLibraryPaths(_steamPath);
            _steamReadUtc = DateTimeOffset.UtcNow;
            return (_steamPath, _libraries.ToList());
        }
    }

    private static string? FindSteamInstall()
    {
        static string? Clean(object? value)
        {
            if (value is not string s || string.IsNullOrWhiteSpace(s))
                return null;
            var path = s.Replace('/', '\\').Trim().Trim('"');
            return Directory.Exists(path) ? path : null;
        }

        try
        {
            using var hkcu = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var path = Clean(hkcu?.GetValue("SteamPath")) ?? Clean(hkcu?.GetValue("InstallPath"));
            if (path is not null)
                return path;
        }
        catch
        {
            // ignore
        }

        try
        {
            using var hklm = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")
                             ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
            return Clean(hklm?.GetValue("InstallPath"));
        }
        catch
        {
            return null;
        }
    }

    private static List<string> ReadLibraryPaths(string steam)
    {
        var paths = new List<string>();
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf))
            vdf = Path.Combine(steam, "config", "libraryfolders.vdf");

        if (File.Exists(vdf))
        {
            string text;
            try
            {
                text = File.ReadAllText(vdf);
            }
            catch
            {
                text = "";
            }

            foreach (Match match in Regex.Matches(text, "\"path\"\\s+\"((?:\\\\.|[^\"])*)\"", RegexOptions.IgnoreCase))
            {
                var path = UnescapeVdf(match.Groups[1].Value).Replace('/', '\\');
                if (Directory.Exists(path))
                    paths.Add(path);
            }
        }

        if (!paths.Any(p => string.Equals(p, steam, StringComparison.OrdinalIgnoreCase)))
            paths.Insert(0, steam);

        return paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static SteamGame? ParseManifest(string steam, string library, string acfPath)
    {
        var text = File.ReadAllText(acfPath);
        if (!int.TryParse(ReadVdfField(text, "appid"), out var appId) || appId <= 0 || appId == 228980)
            return null;

        if (int.TryParse(ReadVdfField(text, "StateFlags"), out var flags) && (flags & 4) == 0)
            return null;

        var name = ReadVdfField(text, "name");
        if (string.IsNullOrWhiteSpace(name))
            return null;

        string? installDir = null;
        var installName = ReadVdfField(text, "installdir");
        if (!string.IsNullOrWhiteSpace(installName))
        {
            var full = Path.Combine(library, "steamapps", "common", installName);
            if (Directory.Exists(full))
                installDir = full;
        }

        return new SteamGame
        {
            AppId = appId,
            Name = name.Trim(),
            IconPath = FindSteamIcon(steam, appId, ReadVdfField(text, "clienticon")),
            InstallDir = installDir
        };
    }

    /// <summary>
    /// Square Steam app icons: shortcut .ico, Start Menu .url IconFile, librarycache *_icon.
    /// Skips header/logo art (large landscape images).
    /// </summary>
    private static string? FindSteamIcon(string steam, int appId, string? clientIcon)
    {
        if (!string.IsNullOrWhiteSpace(clientIcon))
        {
            var hash = clientIcon.Trim();
            string[] icoCandidates =
            [
                Path.Combine(steam, "steam", "games", hash + ".ico"),
                Path.Combine(steam, "steam", "games", hash + ".jpg"),
                Path.Combine(steam, "steam", "games", hash + ".png")
            ];
            var hit = icoCandidates.FirstOrDefault(File.Exists);
            if (hit is not null)
                return hit;
        }

        if (SteamUrlIcons().TryGetValue(appId, out var fromUrl) && File.Exists(fromUrl))
            return fromUrl;

        var cache = Path.Combine(steam, "appcache", "librarycache");
        if (!Directory.Exists(cache))
            return null;

        string[] known =
        [
            Path.Combine(cache, $"{appId}_icon.jpg"),
            Path.Combine(cache, $"{appId}_icon.png"),
            Path.Combine(cache, appId.ToString(), "icon.jpg"),
            Path.Combine(cache, appId.ToString(), "icon.png"),
            Path.Combine(cache, appId.ToString(), "icon_2x.jpg"),
            Path.Combine(cache, appId.ToString(), "icon_2x.png")
        ];
        var knownHit = known.FirstOrDefault(File.Exists);
        if (knownHit is not null)
            return knownHit;

        try
        {
            foreach (var file in Directory.EnumerateFiles(cache, $"{appId}_icon*"))
                return file;
        }
        catch
        {
            // ignore
        }

        var nested = Path.Combine(cache, appId.ToString());
        if (Directory.Exists(nested))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(nested)
                             .Where(IsSquareIconFileName)
                             .OrderBy(p => p.Length))
                    return file;
            }
            catch
            {
                // ignore
            }
        }

        return FindUserGridIcon(steam, appId);
    }

    private static bool IsSquareIconFileName(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Contains("header", StringComparison.OrdinalIgnoreCase)
            || name.Contains("hero", StringComparison.OrdinalIgnoreCase)
            || name.Contains("logo", StringComparison.OrdinalIgnoreCase)
            || name.Contains("capsule", StringComparison.OrdinalIgnoreCase)
            || name.Contains("library_", StringComparison.OrdinalIgnoreCase)
            || name.Contains("600x900", StringComparison.OrdinalIgnoreCase)
            || name.Contains("portrait", StringComparison.OrdinalIgnoreCase))
            return false;

        return name.Contains("icon", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindUserGridIcon(string steam, int appId)
    {
        var userdata = Path.Combine(steam, "userdata");
        if (!Directory.Exists(userdata))
            return null;

        try
        {
            foreach (var userDir in Directory.EnumerateDirectories(userdata))
            {
                var grid = Path.Combine(userDir, "config", "grid");
                if (!Directory.Exists(grid))
                    continue;

                string[] files =
                [
                    Path.Combine(grid, $"{appId}_icon.jpg"),
                    Path.Combine(grid, $"{appId}_icon.png"),
                    Path.Combine(grid, $"{appId}p.jpg"),
                    Path.Combine(grid, $"{appId}p.png")
                ];
                // Prefer *_icon; skip tall portrait (…p) unless nothing else.
                var icon = files.Take(2).FirstOrDefault(File.Exists);
                if (icon is not null)
                    return icon;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static Dictionary<int, string>? _urlIcons;
    private static DateTimeOffset _urlIconsUtc;

    private static IReadOnlyDictionary<int, string> SteamUrlIcons()
    {
        lock (Sync)
        {
            if (_urlIcons is not null && DateTimeOffset.UtcNow - _urlIconsUtc < TimeSpan.FromMinutes(2))
                return _urlIcons;

            _urlIcons = ScanSteamUrlIcons();
            _urlIconsUtc = DateTimeOffset.UtcNow;
            return _urlIcons;
        }
    }

    private static Dictionary<int, string> ScanSteamUrlIcons()
    {
        var map = new Dictionary<int, string>();
        string[] roots =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs", "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Steam"),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        ];

        foreach (var root in roots.Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d)))
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.url", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
                TryAddUrlIcon(map, file);
        }

        return map;
    }

    private static void TryAddUrlIcon(Dictionary<int, string> map, string urlPath)
    {
        try
        {
            string? iconFile = null;
            var appId = 0;
            foreach (var raw in File.ReadLines(urlPath))
            {
                var line = raw.Trim();
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                {
                    var m = Regex.Match(line, @"steam://rungameid/(\d+)", RegexOptions.IgnoreCase);
                    if (m.Success)
                        int.TryParse(m.Groups[1].Value, out appId);
                }
                else if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
                {
                    iconFile = line["IconFile=".Length..].Trim().Trim('"');
                }
            }

            if (appId <= 0 || string.IsNullOrWhiteSpace(iconFile) || !File.Exists(iconFile))
                return;

            map.TryAdd(appId, iconFile);
        }
        catch
        {
            // ignore
        }
    }

    private static string? ReadVdfField(string text, string key)
    {
        var pattern = "\"" + Regex.Escape(key) + "\"\\s+\"((?:\\\\.|[^\"])*)\"";
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        return match.Success ? UnescapeVdf(match.Groups[1].Value) : null;
    }

    private static string UnescapeVdf(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                sb.Append(value[i + 1]);
                i++;
                continue;
            }

            sb.Append(value[i]);
        }

        return sb.ToString();
    }

    private static bool IsSteamRegistryRunning(int appId)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Valve\Steam\Apps\{appId}");
            var value = key?.GetValue("Running");
            if (value is int n)
                return n != 0;
            return value is string s && int.TryParse(s, out var parsed) && parsed != 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? PrimaryExe(SteamGame game)
    {
        var exe = ExeByApp.GetOrAdd(game.AppId, _ => FindPrimaryExe(game.InstallDir) ?? "");
        return string.IsNullOrEmpty(exe) ? null : exe;
    }

    private static string? FindPrimaryExe(string? installDir)
    {
        if (string.IsNullOrEmpty(installDir) || !Directory.Exists(installDir))
            return null;

        try
        {
            var exes = ListExes(installDir);

            string[] nested =
            [
                "Binaries\\Win64", "Binaries\\Win32", "bin\\Win64", "bin\\x64", "bin", "x64", "win64", "Win64"
            ];
            foreach (var rel in nested)
            {
                var dir = Path.Combine(installDir, rel);
                if (Directory.Exists(dir))
                    exes.AddRange(ListExes(dir));
            }

            if (exes.Count == 0)
            {
                foreach (var sub in Directory.EnumerateDirectories(installDir).Take(12))
                {
                    exes.AddRange(ListExes(sub));
                    if (exes.Count >= 20)
                        break;
                }
            }

            if (exes.Count == 0)
                return null;

            var folder = Path.GetFileName(installDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return exes
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(p => ScoreExe(p, folder))
                .ThenByDescending(SafeLength)
                .First();
        }
        catch
        {
            return null;
        }
    }

    private static List<string> ListExes(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(p => !IsHelperExe(p))
                .Take(12)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static bool IsHelperExe(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        return name is "steam.exe" or "steamwebhelper.exe" or "steamservice.exe"
               || name.Contains("unins", StringComparison.Ordinal)
               || name.Contains("crashhandler", StringComparison.Ordinal)
               || name.Contains("unitycrash", StringComparison.Ordinal)
               || name.Contains("redist", StringComparison.Ordinal)
               || name.Contains("setup", StringComparison.Ordinal)
               || name.StartsWith("vc_redist", StringComparison.Ordinal)
               || name.Contains("dotnet", StringComparison.Ordinal)
               || name is "dxsetup.exe";
    }

    private static int ScoreExe(string path, string folder)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (name.Equals(folder, StringComparison.OrdinalIgnoreCase))
            return 3;
        if (name.Length >= 4 && folder.Contains(name, StringComparison.OrdinalIgnoreCase))
            return 2;
        if (folder.Length >= 4 && name.Contains(folder, StringComparison.OrdinalIgnoreCase))
            return 2;
        return 1;
    }

    private static long SafeLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static string? CachedShortcut(string lnkPath)
    {
        if (ShortcutTargets.TryGetValue(lnkPath, out var cached))
            return string.IsNullOrEmpty(cached) ? null : cached;

        var target = ResolveShortcut(lnkPath) ?? "";
        ShortcutTargets[lnkPath] = target;
        return string.IsNullOrEmpty(target) ? null : target;
    }

    private static string? ResolveShortcut(string lnkPath)
    {
        try
        {
            if (!File.Exists(lnkPath))
                return null;

            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null)
                return null;

            object shell = Activator.CreateInstance(type)!;
            object shortcut = type.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                shell,
                [lnkPath])!;
            var target = shortcut.GetType().InvokeMember(
                "TargetPath",
                System.Reflection.BindingFlags.GetProperty,
                null,
                shortcut,
                null) as string;
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? LoadImageFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        if (path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
            return LoadExeIcon(path);

        return LoadBitmap(path);
    }

    private static BitmapImage? LoadBitmap(string path)
    {
        if (!File.Exists(path))
            return null;

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.DecodePixelWidth = 32;
        bmp.UriSource = new Uri(path, UriKind.Absolute);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static ImageSource? LoadExeIcon(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
        if (icon is null)
            return null;

        // Prefer a small square frame for the profile list (20×20).
        using var sized = new System.Drawing.Icon(icon, 32, 32);
        var src = Imaging.CreateBitmapSourceFromHIcon(
            sized.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromWidthAndHeight(32, 32));
        src.Freeze();
        return src;
    }
}
