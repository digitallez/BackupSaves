using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using BackupSaves.Core.Models;

namespace WpfApp_BackupSaves.ViewModels;

public sealed class ArchiveListItem
{
    public string Path { get; init; } = "";
    public string Name { get; init; } = "";
    public DateTime LastWriteTime { get; init; }
    public long SizeBytes { get; init; }
    public string SizeDisplay => SizeBytes < 1024 * 1024
        ? $"{SizeBytes / 1024.0:0.0} KB"
        : $"{SizeBytes / (1024.0 * 1024):0.00} MB";
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    public ObservableCollection<BackupProfile> Profiles { get; } = [];
    public ObservableCollection<ArchiveListItem> Archives { get; } = [];
    public ObservableCollection<RunHistoryEntry> History { get; } = [];

    private BackupProfile? _selectedProfile;
    public BackupProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (Set(ref _selectedProfile, value))
                OnPropertyChanged(nameof(HasProfile));
        }
    }

    private ArchiveListItem? _selectedArchive;
    public ArchiveListItem? SelectedArchive
    {
        get => _selectedArchive;
        set
        {
            if (Set(ref _selectedArchive, value))
                OnPropertyChanged(nameof(HasArchive));
        }
    }

    private string _status = "Готово";
    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (Set(ref _isBusy, value))
                OnPropertyChanged(nameof(CanInteract));
        }
    }

    public bool HasProfile => SelectedProfile is not null;
    public bool HasArchive => SelectedArchive is not null;
    public bool CanInteract => !IsBusy;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    public void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
