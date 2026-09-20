using System.Text.Json;
using BackupSaves.Core.IO;
using BackupSaves.Core.Models;

namespace BackupSaves.Core.Services;

public static class ArchiveMetaStore
{
    public const string SidecarSuffix = ".meta.json";

    public static string GetMetaPath(string archivePath) => archivePath + SidecarSuffix;

    public static string? LoadDisplayName(string archivePath)
    {
        var metaPath = GetMetaPath(archivePath);
        if (!File.Exists(metaPath))
            return null;

        try
        {
            var json = File.ReadAllText(metaPath);
            var meta = JsonSerializer.Deserialize<ArchiveMeta>(json, JsonDefaults.Options);
            var name = meta?.DisplayName?.Trim();
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("ArchiveMeta", $"Failed to read meta for \"{archivePath}\"", ex);
            return null;
        }
    }

    /// <summary>Persists display name. Empty/whitespace deletes the sidecar.</summary>
    public static void SaveDisplayName(string archivePath, string? displayName)
    {
        var metaPath = GetMetaPath(archivePath);
        var trimmed = displayName?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            TryDelete(metaPath);
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(new ArchiveMeta { DisplayName = trimmed }, JsonDefaults.Options);
            var tmp = metaPath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, metaPath, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("ArchiveMeta", $"Failed to save meta for \"{archivePath}\"", ex);
            throw;
        }
    }

    public static void DeleteForArchive(string archivePath) => TryDelete(GetMetaPath(archivePath));

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("ArchiveMeta", $"Failed to delete \"{path}\"", ex);
        }
    }
}
