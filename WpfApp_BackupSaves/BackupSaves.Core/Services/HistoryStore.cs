using System.Text.Json;
using BackupSaves.Core.IO;
using BackupSaves.Core.Models;

namespace BackupSaves.Core.Services;

public interface IHistoryStore
{
    string HistoryPath { get; }
    Task<IReadOnlyList<RunHistoryEntry>> LoadAsync(CancellationToken ct = default);
    Task AppendAsync(RunHistoryEntry entry, CancellationToken ct = default);
}

public sealed class HistoryStore : IHistoryStore
{
    public const int MaxEntries = 1000;
    public const int UiInitialCount = 200;

    private readonly SemaphoreSlim _gate = new(1, 1);

    public string HistoryPath { get; }

    public HistoryStore(string? historyPath = null)
    {
        if (!string.IsNullOrWhiteSpace(historyPath))
        {
            HistoryPath = historyPath;
            return;
        }

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            SettingsStore.AppFolderName);
        Directory.CreateDirectory(dir);
        HistoryPath = Path.Combine(dir, "history.json");
    }

    public HistoryStore(ISettingsStore settings)
        : this(Path.Combine(
            Path.GetDirectoryName(settings.SettingsPath)
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), SettingsStore.AppFolderName),
            "history.json"))
    {
    }

    public async Task<IReadOnlyList<RunHistoryEntry>> LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await LoadUnlockedAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendAsync(RunHistoryEntry entry, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var list = (await LoadUnlockedAsync(ct)).ToList();
            list.Insert(0, entry);
            if (list.Count > MaxEntries)
                list = list.Take(MaxEntries).ToList();
            await SaveUnlockedAsync(list, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<RunHistoryEntry>> LoadUnlockedAsync(CancellationToken ct)
    {
        if (!File.Exists(HistoryPath))
            return [];

        await using var stream = File.OpenRead(HistoryPath);
        var list = await JsonSerializer.DeserializeAsync<List<RunHistoryEntry>>(stream, JsonDefaults.Options, ct);
        return list ?? [];
    }

    private async Task SaveUnlockedAsync(List<RunHistoryEntry> entries, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(HistoryPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = HistoryPath + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, entries, JsonDefaults.Options, ct);
        }

        File.Move(tmp, HistoryPath, overwrite: true);
        AppLog.Default.Info("History", $"Saved history.json (entries={entries.Count})");
    }
}
