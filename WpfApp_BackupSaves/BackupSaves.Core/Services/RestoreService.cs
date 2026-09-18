using System.Text.Json;
using BackupSaves.Core.IO;
using BackupSaves.Core.Models;
using BackupSaves.Core.Results;
using SharpCompress.Archives;

namespace BackupSaves.Core.Services;

public interface IRestoreService
{
    Task<Manifest?> ReadManifestAsync(string archivePath, CancellationToken ct = default);
    Task<RestoreResult> RestoreAsync(string archivePath, bool overwrite, CancellationToken ct = default);
}

public sealed class RestoreService : IRestoreService
{
    public Task<Manifest?> ReadManifestAsync(string archivePath, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath);
            var entry = archive.Entries.FirstOrDefault(e =>
                !e.IsDirectory && e.Key is not null && NormalizeKey(e.Key) == "manifest.json");
            if (entry is null)
                return null;

            using var stream = entry.OpenEntryStream();
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return JsonSerializer.Deserialize<Manifest>(json, JsonDefaults.Options);
        }, ct);
    }

    public async Task<RestoreResult> RestoreAsync(string archivePath, bool overwrite, CancellationToken ct = default)
    {
        AppLog.Default.Info("Restore", $"Старт: archive=\"{archivePath}\" overwrite={overwrite}");

        var manifest = await ReadManifestAsync(archivePath, ct);
        if (manifest is null)
        {
            AppLog.Default.Error("Restore", "Нет manifest.json");
            return RestoreResult.Fail("В архиве нет manifest.json — восстановление невозможно.");
        }

        if (manifest.Entries.Count == 0)
        {
            AppLog.Default.Error("Restore", "Манифест пуст");
            return RestoreResult.Fail("Манифест пуст.");
        }

        var errors = new List<RestoreFileError>();
        var restored = 0;

        try
        {
            await Task.Run(() =>
            {
                using var archive = ArchiveFactory.OpenArchive(archivePath);
                var byKey = archive.Entries
                    .Where(e => !e.IsDirectory && e.Key is not null)
                    .GroupBy(e => NormalizeKey(e.Key!), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                foreach (var item in manifest.Entries)
                {
                    ct.ThrowIfCancellationRequested();

                    if (!PathHelper.IsSafeArchivePath(item.ArchivePath))
                    {
                        errors.Add(new RestoreFileError
                        {
                            SourcePath = item.SourcePath,
                            Message = $"Небезопасный archivePath: {item.ArchivePath}"
                        });
                        continue;
                    }

                    var key = NormalizeKey(item.ArchivePath);
                    if (!byKey.TryGetValue(key, out var entry))
                    {
                        errors.Add(new RestoreFileError
                        {
                            SourcePath = item.SourcePath,
                            Message = "Файл отсутствует в архиве"
                        });
                        continue;
                    }

                    var target = !string.IsNullOrWhiteSpace(item.SourcePathTemplate)
                        ? PathHelper.ExpandPath(item.SourcePathTemplate)
                        : item.SourcePath;

                    try
                    {
                        var dir = Path.GetDirectoryName(target);
                        if (!string.IsNullOrEmpty(dir))
                            Directory.CreateDirectory(dir);

                        if (File.Exists(target) && !overwrite)
                        {
                            errors.Add(new RestoreFileError
                            {
                                SourcePath = target,
                                Message = "Файл существует (overwrite=false)"
                            });
                            continue;
                        }

                        var tmp = target + ".restore.tmp";
                        using (var entryStream = entry.OpenEntryStream())
                        using (var fs = File.Create(tmp))
                        {
                            entryStream.CopyTo(fs);
                        }

                        File.Move(tmp, target, overwrite: true);
                        restored++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add(new RestoreFileError
                        {
                            SourcePath = target,
                            Message = ex.Message
                        });
                    }
                }
            }, ct);
        }
        catch (OperationCanceledException)
        {
            AppLog.Default.Warn("Restore", "Отменено");
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("Restore", "Сбой restore", ex);
            return RestoreResult.Fail(ex.Message);
        }

        var result = new RestoreResult
        {
            Success = errors.Count == 0,
            RestoredCount = restored,
            Errors = errors,
            ErrorMessage = errors.Count == 0 ? null : $"Восстановлено {restored}, ошибок {errors.Count}"
        };

        if (result.Success)
            AppLog.Default.Info("Restore", $"Успех: restored={restored}");
        else
            AppLog.Default.Warn("Restore", $"Частично/ошибка: restored={restored}, errors={errors.Count}");

        return result;
    }

    private static string NormalizeKey(string key) => key.Replace('\\', '/').TrimStart('/');
}
