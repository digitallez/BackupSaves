using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BackupSaves.Core.Models;

namespace BackupSaves.Core.IO;

public static class PathHelper
{
    private static readonly Regex UnsafeSlug = new(@"[^a-z0-9\-]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string ExpandPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        var expanded = path
            .Replace("%USERPROFILE%", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase)
            .Replace("%APPDATA%", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), StringComparison.OrdinalIgnoreCase)
            .Replace("%LOCALAPPDATA%", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StringComparison.OrdinalIgnoreCase)
            .Replace("%DOCUMENTS%", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), StringComparison.OrdinalIgnoreCase);

        return Environment.ExpandEnvironmentVariables(expanded);
    }

    public static string ToSlug(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "profile";

        var slug = UnsafeSlug.Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrEmpty(slug) ? "profile" : slug;
    }

    /// <summary>Stored slug, or <see cref="ToSlug"/> of the name when slug is empty.</summary>
    public static string EffectiveSlug(BackupProfile profile) =>
        string.IsNullOrWhiteSpace(profile.Slug) ? ToSlug(profile.Name) : profile.Slug;

    /// <summary>
    /// Returns <paramref name="desired"/> or <c>desired-2</c>, <c>desired-3</c>, … so it does not
    /// collide with any other profile's <see cref="EffectiveSlug"/>.
    /// </summary>
    public static string EnsureUniqueSlug(string desired, Guid selfId, IEnumerable<BackupProfile> profiles)
    {
        var slug = string.IsNullOrWhiteSpace(desired) ? "profile" : desired;
        if (!IsSlugTaken(slug, selfId, profiles))
            return slug;

        for (var n = 2; n < 10_000; n++)
        {
            var candidate = $"{slug}-{n}";
            if (!IsSlugTaken(candidate, selfId, profiles))
                return candidate;
        }

        return $"{slug}-{selfId:N}";
    }

    private static bool IsSlugTaken(string slug, Guid selfId, IEnumerable<BackupProfile> profiles) =>
        profiles.Any(p => p.Id != selfId &&
                          string.Equals(EffectiveSlug(p), slug, StringComparison.OrdinalIgnoreCase));

    public static string GetProfileArchiveDirectory(BackupProfile profile)
    {
        var root = ExpandPath(profile.BackupRoot);
        return Path.Combine(root, EffectiveSlug(profile));
    }

    public static string GetArchiveExtension(ArchiveFormat format) =>
        format == ArchiveFormat.SevenZip ? ".7z" : ".zip";

    public static string BuildArchiveFileName(BackupProfile profile, DateTimeOffset createdLocal)
    {
        var stamp = createdLocal.ToString("yyyy-MM-dd_HH-mm-ss");
        return $"{EffectiveSlug(profile)}_{stamp}{GetArchiveExtension(profile.Format)}";
    }

    public static bool IsUnderDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(ExpandPath(path)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullDir = Path.GetFullPath(ExpandPath(directory)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.Equals(fullDir, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(fullDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSafeArchivePath(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
            return false;

        var normalized = archivePath.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.Contains("://", StringComparison.Ordinal))
            return false;
        if (normalized.Split('/').Any(p => p is ".." or ""))
            return false;
        return !Path.IsPathRooted(archivePath);
    }

    public static string ToArchiveRelativePath(string rootExpanded, string fileExpanded)
    {
        var root = Path.GetFullPath(rootExpanded).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var file = Path.GetFullPath(fileExpanded);
        var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
        var rootName = Path.GetFileName(root);
        if (string.IsNullOrEmpty(rootName))
            rootName = "root";
        return $"data/{rootName}/{relative}";
    }

    public static string ToArchiveRelativeFile(string fileExpanded)
    {
        var name = Path.GetFileName(fileExpanded);
        var parent = Path.GetFileName(Path.GetDirectoryName(fileExpanded) ?? "file");
        return $"data/{parent}/{name}";
    }

    public static bool ContainsPathMask(string path) =>
        !string.IsNullOrEmpty(path) && (path.Contains('*') || path.Contains('?'));

    /// <summary>
    /// Splits an expanded path whose <em>file name</em> contains <c>*</c>/<c>?</c>
    /// into directory + pattern for <see cref="Directory.EnumerateFiles"/>.
    /// Masks in folder segments are not supported.
    /// </summary>
    public static bool TryGetFileMask(string expandedPath, out string directory, out string pattern)
    {
        directory = "";
        pattern = "";
        if (string.IsNullOrWhiteSpace(expandedPath) || !ContainsPathMask(expandedPath))
            return false;

        pattern = Path.GetFileName(expandedPath);
        directory = Path.GetDirectoryName(expandedPath) ?? "";
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(directory))
            return false;

        if (!ContainsPathMask(pattern) || ContainsPathMask(directory))
            return false;

        return true;
    }
}

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Local JSON (meta, settings, history): keep Cyrillic readable, not \u0415…
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
