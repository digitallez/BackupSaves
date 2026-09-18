using System.IO;
using BackupSaves.Core.IO;
using BackupSaves.Core.Models;

namespace WpfApp_BackupSaves.Services;

/// <summary>FileSystemWatcher + polling fallback for archive folders.</summary>
public sealed class ArchiveFolderWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly System.Timers.Timer _pollTimer;
    private readonly Action _onChanged;
    private CancellationTokenSource? _debounceCts;

    public ArchiveFolderWatcher(Action onChanged)
    {
        _onChanged = onChanged;
        _pollTimer = new System.Timers.Timer(7000);
        _pollTimer.Elapsed += (_, _) => Raise();
        _pollTimer.AutoReset = true;
    }

    public void Watch(IEnumerable<BackupProfile> profiles)
    {
        StopWatchers();

        foreach (var profile in profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.BackupRoot) || string.IsNullOrWhiteSpace(profile.Slug))
                continue;

            var dir = PathHelper.GetProfileArchiveDirectory(profile);
            Directory.CreateDirectory(dir);

            var w = new FileSystemWatcher(dir)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            w.Created += OnFs;
            w.Changed += OnFs;
            w.Deleted += OnFs;
            w.Renamed += OnFs;
            _watchers.Add(w);
        }

        _pollTimer.Start();
        Raise();
    }

    private void OnFs(object sender, FileSystemEventArgs e)
    {
        var name = e.Name ?? e.FullPath;
        if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            return;

        _ = DebounceRaiseAsync();
    }

    private async Task DebounceRaiseAsync()
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;
        try
        {
            await Task.Delay(400, token);
            Raise();
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
    }

    private void Raise() => _onChanged();

    private void StopWatchers()
    {
        _pollTimer.Stop();
        foreach (var w in _watchers)
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
        }
        _watchers.Clear();
    }

    public void Dispose()
    {
        StopWatchers();
        _pollTimer.Dispose();
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
    }
}
