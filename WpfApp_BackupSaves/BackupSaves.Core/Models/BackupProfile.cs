using System.Text.Json;
using System.Text.Json.Serialization;

namespace BackupSaves.Core.Models;

public sealed class BackupProfile
{
    /// <summary>Unknown JSON fields kept across load/save so older builds do not wipe newer settings.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string BackupRoot { get; set; } = "";
    /// <summary>Default for real use is SevenZip; Zip is a fallback.</summary>
    public ArchiveFormat Format { get; set; } = ArchiveFormat.SevenZip;
    public int RetentionCount { get; set; } = 10;
    /// <summary>If true, skip creating an archive when all source file SHA-256 match the last snapshot.</summary>
    public bool SkipUnchangedByChecksum { get; set; }

    /// <summary>Only run backups while a matching process is running (+ one backup after it exits).</summary>
    public bool WatchProcessEnabled { get; set; }

    /// <summary>Exe name, full path, or wildcard mask (e.g. game.exe, *elden*, C:\Games\*\game.exe).</summary>
    public string? WatchProcessPattern { get; set; }

    /// <summary>How often the UI/in-app watch re-checks whether the process is running (seconds). Default 10.</summary>
    public int WatchProcessScanSeconds { get; set; } = 10;

    /// <summary>Persisted: matching process was observed running on a previous check.</summary>
    public bool WatchProcessWasRunning { get; set; }

    /// <summary>Exe path, command line, or steam://rungameid/N to start from the profile list.</summary>
    public string? LaunchCommand { get; set; }

    /// <summary>Legacy settings field (up to 3 slots); migrated into <see cref="LaunchCommand"/> on load.</summary>
    public List<string>? LaunchCommands
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(LaunchCommand) || value is null || value.Count == 0)
                return;
            LaunchCommand = value
                .Select(s => s?.Trim())
                .FirstOrDefault(s => !string.IsNullOrEmpty(s));
        }
    }

    public List<SourceEntry> Sources { get; set; } = [];
    public ScheduleConfig Schedule { get; set; } = new();
}
