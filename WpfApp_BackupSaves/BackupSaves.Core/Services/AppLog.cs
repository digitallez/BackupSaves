using System.Text;

namespace BackupSaves.Core.Services;

public enum AppLogLevel
{
    Info,
    Warn,
    Error
}

public interface IAppLog
{
    string LogDirectory { get; }
    void Write(AppLogLevel level, string category, string message, Exception? ex = null);
    void Info(string category, string message);
    void Warn(string category, string message);
    void Error(string category, string message, Exception? ex = null);
}

/// <summary>Thread-safe daily file log under %LocalAppData%\BackupSaves\logs\.</summary>
public sealed class AppLog : IAppLog
{
    private static readonly Lazy<AppLog> DefaultLazy = new(() => new AppLog());
    public static IAppLog Default => DefaultLazy.Value;

    private readonly object _sync = new();
    private readonly string _directory;

    public string LogDirectory => _directory;

    public AppLog(string? logDirectory = null)
    {
        _directory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            SettingsStore.AppFolderName,
            "logs");
        Directory.CreateDirectory(_directory);
        TryMigrateFromRoaming(_directory);
    }

    /// <summary>
    /// Old builds wrote to %AppData%\BackupSaves\logs. Copy today's file once if local is empty.
    /// </summary>
    private static void TryMigrateFromRoaming(string localLogsDir)
    {
        try
        {
            if (Directory.EnumerateFiles(localLogsDir, "*.log").Any())
                return;

            var roaming = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                SettingsStore.AppFolderName,
                "logs");
            if (!Directory.Exists(roaming))
                return;

            foreach (var src in Directory.EnumerateFiles(roaming, "*.log"))
            {
                var dest = Path.Combine(localLogsDir, Path.GetFileName(src));
                if (!File.Exists(dest))
                    File.Copy(src, dest);
            }
        }
        catch
        {
            // ignore migration failures
        }
    }

    public void Info(string category, string message) => Write(AppLogLevel.Info, category, message);
    public void Warn(string category, string message) => Write(AppLogLevel.Warn, category, message);
    public void Error(string category, string message, Exception? ex = null) =>
        Write(AppLogLevel.Error, category, message, ex);

    public void Write(AppLogLevel level, string category, string message, Exception? ex = null)
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var line = new StringBuilder()
            .Append(stamp).Append(" [").Append(level.ToString().ToUpperInvariant()).Append("] ")
            .Append(category).Append(": ").Append(message);

        if (ex is not null)
            line.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);

        line.AppendLine();

        var path = Path.Combine(_directory, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
        lock (_sync)
        {
            try
            {
                File.AppendAllText(path, line.ToString(), Encoding.UTF8);
            }
            catch
            {
                // never break app because of logging
            }
        }
    }
}
