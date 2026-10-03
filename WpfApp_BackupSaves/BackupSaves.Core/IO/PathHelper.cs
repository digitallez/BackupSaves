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

    public static string GetProfileArchiveDirectory(BackupProfile profile)
    {
        var root = ExpandPath(profile.BackupRoot);
        return Path.Combine(root, profile.Slug);
    }

    public static string GetArchiveExtension(ArchiveFormat format) =>
        format == ArchiveFormat.SevenZip ? ".7z" : ".zip";

    public static string BuildArchiveFileName(BackupProfile profile, DateTimeOffset createdLocal)
    {
        var stamp = createdLocal.ToString("yyyy-MM-dd_HH-mm-ss");
        return $"{profile.Slug}_{stamp}{GetArchiveExtension(profile.Format)}";
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
}

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
