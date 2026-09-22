using System.Text.Json;
using BackupSaves.Core.IO;
using BackupSaves.Core.Models;

namespace BackupSaves.Core.Services;

public static class ArchiveMetaStore
{
    public const string SidecarSuffix = ".meta.json";

    public static string GetMetaPath(string archivePath) => archivePath + SidecarSuffix;

    public static ArchiveMeta Load(string archivePath)
    {
        var metaPath = GetMetaPath(archivePath);
        if (!File.Exists(metaPath))
            return new ArchiveMeta();

        try
        {
            var json = File.ReadAllText(metaPath);
            return JsonSerializer.Deserialize<ArchiveMeta>(json, JsonDefaults.Options)
                   ?? new ArchiveMeta();
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("ArchiveMeta", $"Failed to read meta for \"{archivePath}\"", ex);
            return new ArchiveMeta();
        }
    }

    public static string? LoadDisplayName(string archivePath)
    {
        var name = Load(archivePath).DisplayName?.Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    public static bool IsExcludedFromRetention(string archivePath) =>
        Load(archivePath).ExcludeFromRetention;

    /// <summary>Persists display name, preserving other meta fields. Empty name + no flags deletes the sidecar.</summary>
    public static void SaveDisplayName(string archivePath, string? displayName)
    {
        var meta = Load(archivePath);
        meta.DisplayName = displayName?.Trim();
        if (string.IsNullOrEmpty(meta.DisplayName))
            meta.DisplayName = null;
        Save(archivePath, meta);
    }

    public static void SaveExcludeFromRetention(string archivePath, bool excludeFromRetention)
    {
        var meta = Load(archivePath);
        meta.ExcludeFromRetention = excludeFromRetention;
        Save(archivePath, meta);
    }

    public static void Save(string archivePath, ArchiveMeta meta)
    {
        var metaPath = GetMetaPath(archivePath);
        var trimmed = meta.DisplayName?.Trim();
        var hasName = !string.IsNullOrEmpty(trimmed);
        if (!hasName && !meta.ExcludeFromRetention)
        {
            TryDelete(metaPath);
            return;
        }

        try
        {
            var toWrite = new ArchiveMeta
            {
                DisplayName = hasName ? trimmed : null,
                ExcludeFromRetention = meta.ExcludeFromRetention
            };
            var json = JsonSerializer.Serialize(toWrite, JsonDefaults.Options);
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
