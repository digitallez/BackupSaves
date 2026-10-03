namespace BackupSaves.Core.Services;

/// <summary>
/// Copies source files to a private temp folder so the slow compression step never holds
/// handles on live save files (games lock them while writing).
/// </summary>
public sealed class SourceStaging : IDisposable
{
    private const int CopyAttempts = 3;

    public string Directory { get; }

    private SourceStaging(string directory)
    {
        Directory = directory;
    }

    public static SourceStaging Create()
    {
        var dir = Path.Combine(Path.GetTempPath(), "BackupSaves", "staging", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        return new SourceStaging(dir);
    }

    /// <returns>Staged file path for each input, in the same order.</returns>
    public Task<List<string>> CopyAsync(
        IReadOnlyList<(string ArchivePath, string SourceFilePath)> files,
        CancellationToken ct,
        Action<long, long>? onProgress = null)
    {
        return Task.Run(() =>
        {
            long totalBytes = 0;
            var sizes = new long[files.Count];
            for (var i = 0; i < files.Count; i++)
            {
                try
                {
                    sizes[i] = new FileInfo(files[i].SourceFilePath).Length;
                }
                catch
                {
                    sizes[i] = 0;
                }

                totalBytes += sizes[i];
            }

            var staged = new List<string>(files.Count);
            long done = 0;
            onProgress?.Invoke(0, totalBytes);

            for (var i = 0; i < files.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var (archivePath, sourceFilePath) = files[i];
                var dest = Path.Combine(Directory, archivePath.Replace('/', Path.DirectorySeparatorChar));
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                CopyWithRetry(sourceFilePath, dest, ct);
                staged.Add(dest);

                done += sizes[i];
                onProgress?.Invoke(done, totalBytes);
            }

            return staged;
        }, ct);
    }

    private static void CopyWithRetry(string source, string dest, CancellationToken ct)
    {
        Exception? last = null;
        for (var i = 0; i < CopyAttempts; i++)
        {
            if (i > 0)
                Thread.Sleep(500 * i);
            ct.ThrowIfCancellationRequested();

            try
            {
                using (var src = new FileStream(source, FileMode.Open, FileAccess.Read,
                           FileShare.ReadWrite | FileShare.Delete, 1024 * 128, FileOptions.SequentialScan))
                using (var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128))
                {
                    src.CopyTo(dst);
                }

                File.SetLastWriteTimeUtc(dest, File.GetLastWriteTimeUtc(source));
                return;
            }
            catch (FileNotFoundException)
            {
                throw;
            }
            catch (IOException ex)
            {
                last = ex;
            }
            catch (UnauthorizedAccessException ex)
            {
                last = ex;
            }

            AppLog.Default.Warn("Backup", $"Copy attempt {i + 1}/{CopyAttempts} failed for \"{source}\": {last.Message}");
        }

        throw new IOException(LocalizationService.Text("core.fileBusy", source), last);
    }

    public void Dispose()
    {
        try
        {
            if (System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (Exception ex)
        {
            AppLog.Default.Warn("Backup", $"Failed to clean staging \"{Directory}\": {ex.Message}");
        }
    }
}
