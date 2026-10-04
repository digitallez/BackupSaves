using System.Text.Json;
using System.Text.Json.Serialization;

namespace BackupSaves.Core.Models;

public sealed class SourceEntry
{
    /// <summary>Unknown JSON fields kept across load/save so older builds do not wipe newer settings.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public string Path { get; set; } = "";
    public SourceType Type { get; set; } = SourceType.Directory;
}
