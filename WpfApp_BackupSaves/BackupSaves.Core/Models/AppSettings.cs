namespace BackupSaves.Core.Models;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public List<BackupProfile> Profiles { get; set; } = [];
    public UiSettings Ui { get; set; } = new();
}
