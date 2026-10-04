using System.Text.Json;
using System.Text.Json.Serialization;

namespace BackupSaves.Core.Models;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public List<BackupProfile> Profiles { get; set; } = [];
    public UiSettings Ui { get; set; } = new();

    /// <summary>Unknown JSON fields kept across load/save so older builds do not wipe newer settings.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
