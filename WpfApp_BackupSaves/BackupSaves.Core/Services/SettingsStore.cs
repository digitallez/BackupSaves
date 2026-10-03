using System.Text.Json;
using BackupSaves.Core.IO;
using BackupSaves.Core.Models;

namespace BackupSaves.Core.Services;

public interface ISettingsStore
{
    string SettingsPath { get; }
    AppSettings Load();
    void Save(AppSettings settings);
    Task<AppSettings> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AppSettings settings, CancellationToken ct = default);
}

public sealed class SettingsStore : ISettingsStore
{
    public const string AppFolderName = "BackupSaves";

    public string SettingsPath { get; }

    public SettingsStore(string? settingsPath = null)
    {
        if (!string.IsNullOrWhiteSpace(settingsPath))
        {
            SettingsPath = settingsPath;
            return;
        }

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppFolderName);
        Directory.CreateDirectory(dir);
        SettingsPath = Path.Combine(dir, "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return new AppSettings();

        var json = File.ReadAllText(SettingsPath);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonDefaults.Options) ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(settings, JsonDefaults.Options);
        var tmp = SettingsPath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, SettingsPath, overwrite: true);
        AppLog.Default.Info("Settings",
            $"Saved settings.json (profiles={settings.Profiles.Count}, theme={settings.Ui.Theme})");
    }

    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(SettingsPath))
            return new AppSettings();

        await using var stream = File.OpenRead(SettingsPath);
        return await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonDefaults.Options, ct)
               ?? new AppSettings();
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = SettingsPath + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonDefaults.Options, ct);
        }

        File.Move(tmp, SettingsPath, overwrite: true);
        AppLog.Default.Info("Settings",
            $"Saved settings.json (profiles={settings.Profiles.Count}, theme={settings.Ui.Theme})");
    }
}
