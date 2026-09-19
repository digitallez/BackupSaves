using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using BackupSaves.Core.Models;
using WpfApp_BackupSaves.Services;

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

public sealed class LanguageOption
{
    public string Id { get; init; } = "";
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string FlagUri { get; init; } = "";
    public bool HasFlag => !string.IsNullOrEmpty(FlagUri);
    /// <summary>Text shown in the language combo (from @name, else @code).</summary>
    public string Display => !string.IsNullOrWhiteSpace(Name) ? Name : Code;
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    public ObservableCollection<BackupProfile> Profiles { get; } = [];
    public ObservableCollection<ArchiveListItem> Archives { get; } = [];
    public ObservableCollection<RunHistoryEntry> History { get; } = [];
    public ObservableCollection<LanguageOption> Languages { get; } = [];

    private LanguageOption? _selectedLanguage;
    private bool _suppressLanguageChange;

    public LanguageOption? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (!Set(ref _selectedLanguage, value) || value is null || _suppressLanguageChange)
                return;

            LocalizationService.Instance.SetLanguage(value.Id);
            LanguageChanged?.Invoke(this, value.Id);
        }
    }

    public event EventHandler<string>? LanguageChanged;

    public MainViewModel()
    {
        ReloadLanguages();
        _status = LocalizationService.Text("main.statusReady");
        SelectLanguageSilent(LocalizationService.Instance.Language);
        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            if (_status == LocalizationService.Text("main.statusReady") ||
                string.IsNullOrEmpty(_status) ||
                _status is "Готово" or "Ready")
                Status = LocalizationService.Text("main.statusReady");
        };
    }

    public void ReloadLanguages()
    {
        var selected = _selectedLanguage?.Id;
        Languages.Clear();
        foreach (var info in LocalizationService.Instance.DiscoverLanguages())
        {
            Languages.Add(new LanguageOption
            {
                Id = info.Id,
                Code = info.DisplayCode,
                Name = info.DisplayName,
                FlagUri = info.FlagUri
            });
        }

        if (!string.IsNullOrEmpty(selected))
            SelectLanguageSilent(selected);
        else if (Languages.Count > 0 && _selectedLanguage is null)
            SelectLanguageSilent(LocalizationService.Instance.Language);
    }

    public void SelectLanguageSilent(string id)
    {
        if (Languages.Count == 0)
            return;

        var option = Languages.FirstOrDefault(l =>
                         l.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                     ?? Languages.FirstOrDefault(l =>
                         l.Code.Equals(id, StringComparison.OrdinalIgnoreCase))
                     ?? Languages[0];
        _suppressLanguageChange = true;
        try
        {
            SelectedLanguage = option;
        }
        finally
        {
            _suppressLanguageChange = false;
        }
    }

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

    private string _status = "";
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
