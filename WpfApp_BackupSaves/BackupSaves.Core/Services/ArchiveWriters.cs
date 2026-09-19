using BackupSaves.Core.Models;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.SevenZip;
using SharpCompress.Writers.Zip;

namespace BackupSaves.Core.Services;

public interface IArchiveWriter
{
    ArchiveFormat Format { get; }
    string Extension { get; }
    Task WriteAsync(string tempArchivePath, IReadOnlyList<(string ArchivePath, string SourceFilePath)> files, string manifestJson, CancellationToken ct);
}

public sealed class ZipArchiveWriter : IArchiveWriter
{
    public ArchiveFormat Format => ArchiveFormat.Zip;
    public string Extension => ".zip";

    public Task WriteAsync(string tempArchivePath, IReadOnlyList<(string ArchivePath, string SourceFilePath)> files, string manifestJson, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            using var stream = File.Create(tempArchivePath);
            using var writer = ZipWriter.OpenWriter(stream, new ZipWriterOptions(CompressionType.Deflate));
            WriteAll(writer, files, manifestJson, ct);
        }, ct);
    }

    internal static void WriteAll(IWriter writer, IReadOnlyList<(string ArchivePath, string SourceFilePath)> files, string manifestJson, CancellationToken ct)
    {
        using (var manifestStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(manifestJson)))
        {
            writer.Write("manifest.json", manifestStream, DateTime.UtcNow);
        }

        foreach (var (archivePath, sourceFilePath) in files)
        {
            ct.ThrowIfCancellationRequested();
            using var fs = OpenWithRetry(sourceFilePath);
            writer.Write(archivePath.Replace('\\', '/'), fs, File.GetLastWriteTimeUtc(sourceFilePath));
        }
    }

    internal static FileStream OpenWithRetry(string path, int attempts = 3)
    {
        Exception? last = null;
        for (var i = 0; i < attempts; i++)
        {
            try
            {
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            }
            catch (IOException ex)
            {
                last = ex;
                Thread.Sleep(200 * (i + 1));
            }
            catch (UnauthorizedAccessException ex)
            {
                last = ex;
                Thread.Sleep(200 * (i + 1));
            }
        }

        throw new IOException(LocalizationService.Text("core.fileBusy", path), last);
    }
}

public sealed class SevenZipArchiveWriter : IArchiveWriter
{
    public ArchiveFormat Format => ArchiveFormat.SevenZip;
    public string Extension => ".7z";

    public Task WriteAsync(string tempArchivePath, IReadOnlyList<(string ArchivePath, string SourceFilePath)> files, string manifestJson, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            using var stream = File.Create(tempArchivePath);
            using var writer = SevenZipWriter.OpenWriter(stream, new SevenZipWriterOptions(CompressionType.LZMA));
            ZipArchiveWriter.WriteAll(writer, files, manifestJson, ct);
        }, ct);
    }
}

public static class ArchiveWriterFactory
{
    public static IArchiveWriter Create(ArchiveFormat format) => format switch
    {
        ArchiveFormat.SevenZip => new SevenZipArchiveWriter(),
        ArchiveFormat.Zip => new ZipArchiveWriter(),
        _ => new SevenZipArchiveWriter()
    };
}
