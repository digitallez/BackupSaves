using System.Text.Json;
using BackupSaves.Core.IO;
using BackupSaves.Core.Models;
using BackupSaves.Core.Results;

namespace BackupSaves.Core.Services;

public interface IRetentionService
{
    int Apply(string profileArchiveDirectory, ArchiveFormat format, int keepCount);
}

public sealed class RetentionService : IRetentionService
{
    public int Apply(string profileArchiveDirectory, ArchiveFormat format, int keepCount)
    {
        if (keepCount < 1 || !Directory.Exists(profileArchiveDirectory))
            return 0;

        var ext = PathHelper.GetArchiveExtension(format);
        var archives = Directory.EnumerateFiles(profileArchiveDirectory, "*" + ext)
            .Where(f => !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();

        var deleted = 0;
        foreach (var old in archives.Skip(keepCount))
        {
            try
            {
                ArchiveMetaStore.DeleteForArchive(old.FullName);
                old.Delete();
                deleted++;
            }
            catch
            {
                // best-effort
            }
        }

        return deleted;
    }
}

public interface IBackupService
{
    Task<BackupResult> BackupAsync(BackupProfile profile, CancellationToken ct = default);
}

public sealed class BackupService : IBackupService
{
    private readonly IRetentionService _retention;

    public BackupService(IRetentionService? retention = null)
    {
        _retention = retention ?? new RetentionService();
    }

    public async Task<BackupResult> BackupAsync(BackupProfile profile, CancellationToken ct = default)
    {
        try
        {
            if (profile.Sources.Count == 0)
                return Fail(LocalizationService.Text("core.noSources"));

            if (string.IsNullOrWhiteSpace(profile.BackupRoot))
                return Fail(LocalizationService.Text("core.noBackupRoot"));

            if (string.IsNullOrWhiteSpace(profile.Slug))
                profile.Slug = PathHelper.ToSlug(profile.Name);

            var backupRoot = PathHelper.ExpandPath(profile.BackupRoot);
            var archiveDir = PathHelper.GetProfileArchiveDirectory(profile);
            Directory.CreateDirectory(archiveDir);

            var created = DateTimeOffset.Now;
            var fileName = PathHelper.BuildArchiveFileName(profile, created);
            var finalPath = Path.Combine(archiveDir, fileName);
            var tempPath = finalPath + ".tmp";

            AppLog.Default.Info("Backup", $"Archiving → temp=\"{tempPath}\" format={profile.Format}");

            if (File.Exists(tempPath))
                File.Delete(tempPath);

            var files = new List<(string ArchivePath, string SourceFilePath, string Template, string ExpandedRoot)>();
            var manifestEntries = new List<ManifestEntry>();

            foreach (var source in profile.Sources)
            {
                ct.ThrowIfCancellationRequested();
                var template = source.Path;
                var expanded = PathHelper.ExpandPath(template);

                if (PathHelper.IsUnderDirectory(expanded, backupRoot) || PathHelper.IsUnderDirectory(expanded, archiveDir))
                    return Fail(LocalizationService.Text("core.sourceOverlapsRoot", template));

                if (source.Type == SourceType.File)
                {
                    if (!File.Exists(expanded))
                        return Fail(LocalizationService.Text("core.fileNotFound", expanded));

                    var archivePath = PathHelper.ToArchiveRelativeFile(expanded);
                    if (!PathHelper.IsSafeArchivePath(archivePath))
                        return Fail(LocalizationService.Text("core.badArchivePath", archivePath));

                    files.Add((archivePath, expanded, template, expanded));
                    manifestEntries.Add(new ManifestEntry
                    {
                        SourcePath = expanded,
                        SourcePathTemplate = template,
                        ArchivePath = archivePath,
                        Type = SourceType.File
                    });
                }
                else
                {
                    if (!Directory.Exists(expanded))
                        return Fail(LocalizationService.Text("core.folderNotFound", expanded));

                    foreach (var file in Directory.EnumerateFiles(expanded, "*", SearchOption.AllDirectories))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (PathHelper.IsUnderDirectory(file, archiveDir))
                            continue;

                        var archivePath = PathHelper.ToArchiveRelativePath(expanded, file);
                        if (!PathHelper.IsSafeArchivePath(archivePath))
                            return Fail(LocalizationService.Text("core.badArchivePath", archivePath));

                        files.Add((archivePath, file, template, expanded));
                        manifestEntries.Add(new ManifestEntry
                        {
                            SourcePath = file,
                            SourcePathTemplate = CombineTemplate(template, Path.GetRelativePath(expanded, file)),
                            ArchivePath = archivePath,
                            Type = SourceType.File
                        });
                    }
                }
            }

            if (files.Count == 0)
                return Fail(LocalizationService.Text("core.noFiles"));

            List<FileChecksumEntry>? checksums = null;
            if (profile.SkipUnchangedByChecksum)
            {
                AppLog.Default.Info("Backup", $"Checksum: computing SHA-256 for {files.Count} file(s)…");
                try
                {
                    checksums = await ChecksumService.ComputeAsync(
                        files.Select(f => f.SourceFilePath), ct);
                }
                catch (IOException ex)
                {
                    AppLog.Default.Error("Backup", "IO while computing checksum", ex);
                    return BackupResult.Fail(LocalizationService.Text("core.checksumReadFailed", ex.Message));
                }

                var previous = ChecksumService.TryLoad(archiveDir);
                if (previous is not null && ChecksumService.AreEqual(checksums, previous.Files))
                {
                    AppLog.Default.Info("Backup",
                        $"Checksum: no changes ({checksums.Count} file(s)) — skip archive");
                    return BackupResult.SkippedUnchanged(checksums.Count);
                }

                AppLog.Default.Info("Backup", "Checksum: changes detected — creating archive");
            }

            var manifest = new Manifest
            {
                Version = 1,
                ProfileId = profile.Id,
                ProfileName = profile.Name,
                CreatedUtc = created.ToUniversalTime(),
                Format = profile.Format,
                Entries = manifestEntries
            };

            var manifestJson = JsonSerializer.Serialize(manifest, JsonDefaults.Options);
            var writer = ArchiveWriterFactory.Create(profile.Format);
            var writeList = files.Select(f => (f.ArchivePath, f.SourceFilePath)).ToList();

            AppLog.Default.Info("Backup", $"Writing archive: {files.Count} file(s), manifest entries={manifestEntries.Count}");

            try
            {
                await writer.WriteAsync(tempPath, writeList, manifestJson, ct);
            }
            catch (IOException ex)
            {
                TryDelete(tempPath);
                AppLog.Default.Error("Backup", "IO while writing archive", ex);
                return BackupResult.Fail(ex.Message);
            }
            catch (Exception ex)
            {
                TryDelete(tempPath);
                AppLog.Default.Error("Backup", "Archive write failed", ex);
                return BackupResult.Fail(LocalizationService.Text("core.archiveError", ex.Message));
            }

            File.Move(tempPath, finalPath, overwrite: false);
            var deleted = _retention.Apply(archiveDir, profile.Format, profile.RetentionCount);

            if (profile.SkipUnchangedByChecksum)
            {
                try
                {
                    checksums ??= await ChecksumService.ComputeAsync(
                        files.Select(f => f.SourceFilePath), ct);
                    ChecksumService.Save(archiveDir, checksums);
                }
                catch (Exception ex)
                {
                    AppLog.Default.Warn("Backup", $"Failed to save checksum snapshot: {ex.Message}");
                }
            }

            AppLog.Default.Info("Backup",
                $"Archive ready: \"{finalPath}\" (atomic rename), retention deleted={deleted}");

            return BackupResult.Ok(finalPath, files.Count);
        }
        catch (OperationCanceledException)
        {
            AppLog.Default.Warn("Backup", "Backup cancelled");
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("Backup", "Unhandled backup error", ex);
            return BackupResult.Fail(ex.Message);
        }
    }

    private static BackupResult Fail(string message)
    {
        AppLog.Default.Error("Backup", message);
        return BackupResult.Fail(message);
    }

    private static string CombineTemplate(string rootTemplate, string relative)
    {
        var rel = relative.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(rootTemplate.TrimEnd('\\', '/'), rel);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }
}
