using System.Security.Cryptography;
using System.Text.Json;
using BackupSaves.Core.IO;
using BackupSaves.Core.Models;

namespace BackupSaves.Core.Services;

public static class ChecksumService
{
    public const string SnapshotFileName = ".backup-checksums.json";

    public static string GetSnapshotPath(string profileArchiveDirectory) =>
        Path.Combine(profileArchiveDirectory, SnapshotFileName);

    public static Task<List<FileChecksumEntry>> ComputeAsync(
        IEnumerable<string> sourceFilePaths,
        CancellationToken ct = default,
        Action<long, long>? onProgress = null) =>
        ComputeAsync(sourceFilePaths.Select(p => (p, p)), ct, onProgress);

    /// <summary>Hashes <c>ReadPath</c> but records the entry under <c>SourcePath</c>.</summary>
    public static async Task<List<FileChecksumEntry>> ComputeAsync(
        IEnumerable<(string SourcePath, string ReadPath)> files,
        CancellationToken ct = default,
        Action<long, long>? onProgress = null)
    {
        var ordered = files.OrderBy(p => p.SourcePath, StringComparer.OrdinalIgnoreCase).ToList();
        long totalBytes = 0;
        var sizes = new long[ordered.Count];
        for (var i = 0; i < ordered.Count; i++)
        {
            try
            {
                sizes[i] = new FileInfo(ordered[i].ReadPath).Length;
            }
            catch
            {
                sizes[i] = 0;
            }

            totalBytes += sizes[i];
        }

        onProgress?.Invoke(0, totalBytes);

        var list = new List<FileChecksumEntry>(ordered.Count);
        long done = 0;
        for (var i = 0; i < ordered.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (path, readPath) = ordered[i];
            var full = Path.GetFullPath(path);
            await using var stream = new FileStream(
                readPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1024 * 64, options: FileOptions.Asynchronous | FileOptions.SequentialScan);

            using var sha = SHA256.Create();
            var hash = await sha.ComputeHashAsync(stream, ct);
            list.Add(new FileChecksumEntry
            {
                Path = full,
                Sha256 = Convert.ToHexString(hash),
                Length = stream.Length
            });

            done += sizes[i];
            onProgress?.Invoke(done, totalBytes);
        }

        return list;
    }

    public static bool AreEqual(IReadOnlyList<FileChecksumEntry>? a, IReadOnlyList<FileChecksumEntry>? b)
    {
        if (a is null || b is null)
            return false;
        if (a.Count != b.Count)
            return false;

        var map = b.ToDictionary(x => x.Path, x => x, StringComparer.OrdinalIgnoreCase);
        foreach (var left in a)
        {
            if (!map.TryGetValue(left.Path, out var right))
                return false;
            if (!string.Equals(left.Sha256, right.Sha256, StringComparison.OrdinalIgnoreCase)
                || left.Length != right.Length)
                return false;
        }

        return true;
    }

    public static ChecksumSnapshot? TryLoad(string profileArchiveDirectory)
    {
        var path = GetSnapshotPath(profileArchiveDirectory);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ChecksumSnapshot>(json, JsonDefaults.Options);
        }
        catch (Exception ex)
        {
            AppLog.Default.Warn("Checksum", $"Failed to read {path}: {ex.Message}");
            return null;
        }
    }

    public static void Save(string profileArchiveDirectory, IReadOnlyList<FileChecksumEntry> files)
    {
        Directory.CreateDirectory(profileArchiveDirectory);
        var path = GetSnapshotPath(profileArchiveDirectory);
        var snap = new ChecksumSnapshot
        {
            UpdatedUtc = DateTimeOffset.UtcNow,
            Files = files.ToList()
        };
        var json = JsonSerializer.Serialize(snap, JsonDefaults.Options);
        File.WriteAllText(path, json);
    }
}
