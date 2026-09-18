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
                return Fail("У профиля нет источников.");

            if (string.IsNullOrWhiteSpace(profile.BackupRoot))
                return Fail("Не указан BackupRoot.");

            if (string.IsNullOrWhiteSpace(profile.Slug))
                profile.Slug = PathHelper.ToSlug(profile.Name);

            var backupRoot = PathHelper.ExpandPath(profile.BackupRoot);
            var archiveDir = PathHelper.GetProfileArchiveDirectory(profile);
            Directory.CreateDirectory(archiveDir);

            var created = DateTimeOffset.Now;
            var fileName = PathHelper.BuildArchiveFileName(profile, created);
            var finalPath = Path.Combine(archiveDir, fileName);
            var tempPath = finalPath + ".tmp";

            AppLog.Default.Info("Backup", $"Архивация → temp=\"{tempPath}\" format={profile.Format}");

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
                    return Fail($"Источник пересекается с BackupRoot: {template}");

                if (source.Type == SourceType.File)
                {
                    if (!File.Exists(expanded))
                        return Fail($"Файл не найден: {expanded}");

                    var archivePath = PathHelper.ToArchiveRelativeFile(expanded);
                    if (!PathHelper.IsSafeArchivePath(archivePath))
                        return Fail($"Некорректный путь в архиве: {archivePath}");

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
                        return Fail($"Папка не найдена: {expanded}");

                    foreach (var file in Directory.EnumerateFiles(expanded, "*", SearchOption.AllDirectories))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (PathHelper.IsUnderDirectory(file, archiveDir))
                            continue;

                        var archivePath = PathHelper.ToArchiveRelativePath(expanded, file);
                        if (!PathHelper.IsSafeArchivePath(archivePath))
                            return Fail($"Некорректный путь в архиве: {archivePath}");

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
                return Fail("Нет файлов для бэкапа.");

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

            AppLog.Default.Info("Backup", $"Запись архива: {files.Count} файл(ов), manifest entries={manifestEntries.Count}");

            try
            {
                await writer.WriteAsync(tempPath, writeList, manifestJson, ct);
            }
            catch (IOException ex)
            {
                TryDelete(tempPath);
                AppLog.Default.Error("Backup", "IO при записи архива", ex);
                return BackupResult.Fail(ex.Message);
            }
            catch (Exception ex)
            {
                TryDelete(tempPath);
                AppLog.Default.Error("Backup", "Ошибка архивации", ex);
                return BackupResult.Fail($"Ошибка архивации: {ex.Message}");
            }

            File.Move(tempPath, finalPath, overwrite: false);
            var deleted = _retention.Apply(archiveDir, profile.Format, profile.RetentionCount);
            AppLog.Default.Info("Backup",
                $"Архив готов: \"{finalPath}\" (atomic rename), retention deleted={deleted}");

            return BackupResult.Ok(finalPath, files.Count);
        }
        catch (OperationCanceledException)
        {
            AppLog.Default.Warn("Backup", "Бэкап отменён");
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("Backup", "Необработанная ошибка бэкапа", ex);
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
