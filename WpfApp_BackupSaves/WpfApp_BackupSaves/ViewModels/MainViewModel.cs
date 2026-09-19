using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using BackupSaves.Core.Models;
using BackupSaves.Core.Services;

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

/// <summary>UI row for the profiles list: live process highlight + next-backup countdown.</summary>
public sealed class ProfileListItem : INotifyPropertyChanged
{
    private BackupProfile _profile;
    private bool _isWatchProcessRunning;
    private string _nextBackupDisplay = "";

    public ProfileListItem(BackupProfile profile) => _profile = profile;

    public BackupProfile Profile => _profile;
    public Guid Id => _profile.Id;
    public string Name => _profile.Name;

    public bool IsWatchProcessRunning
    {
        get => _isWatchProcessRunning;
        private set
        {
            if (_isWatchProcessRunning == value) return;
            _isWatchProcessRunning = value;
            OnPropertyChanged();
        }
    }

    public string NextBackupDisplay
    {
        get => _nextBackupDisplay;
        private set
        {
            if (_nextBackupDisplay == value) return;
            _nextBackupDisplay = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasNextBackupDisplay));
        }
    }

    public bool HasNextBackupDisplay => !string.IsNullOrEmpty(_nextBackupDisplay);

    public void Refresh(
        BackupProfile profile,
        DateTimeOffset now,
        DateTimeOffset? taskNextRun,
        bool? watchProcessRunning = null)
    {
        _profile = profile;
        OnPropertyChanged(nameof(Profile));
        OnPropertyChanged(nameof(Id));
        OnPropertyChanged(nameof(Name));

        var watchOn = profile.WatchProcessEnabled
                      && ProcessWatchService.IsMatchPatternConfigured(profile.WatchProcessPattern);
        var running = watchProcessRunning
                      ?? (watchOn
                          && ProcessWatchService.IsAnyMatchingProcessRunning(profile.WatchProcessPattern));
        if (!watchOn)
            running = false;
        IsWatchProcessRunning = running;

        NextBackupDisplay = BuildNextBackupDisplay(profile, now, taskNextRun, watchOn, running);
    }

    private static string BuildNextBackupDisplay(
        BackupProfile profile,
        DateTimeOffset now,
        DateTimeOffset? taskNextRun,
        bool watchOn,
        bool running)
    {
        var s = profile.Schedule;
        var hasSchedule = s.Enabled || s.InAppEnabled;
        if (!hasSchedule)
            return "";

        if (watchOn && !running)
            return LocalizationService.Text("main.nextBackupWaiting");

        if (s.Enabled)
        {
            if (s.Kind == ScheduleKind.OnLogon)
                return LocalizationService.Text("main.nextBackupOnLogon");

            if (taskNextRun is DateTimeOffset next && next > DateTimeOffset.MinValue)
                return FormatCountdown(next - now);

            var computed = ComputeNextTaskLocal(profile, now);
            return computed is null ? "" : FormatCountdown(computed.Value - now);
        }

        // In-app interval while BackupSaves is open
        var mins = Math.Max(1, s.IntervalMinutes ?? 0);
        if ((s.IntervalMinutes ?? 0) < 1)
            return "";

        if (s.LastInAppBackupUtc is null)
            return LocalizationService.Text("main.nextBackupDue");

        var dueAt = s.LastInAppBackupUtc.Value.AddMinutes(mins);
        return FormatCountdown(dueAt - now);
    }

    private static DateTimeOffset? ComputeNextTaskLocal(BackupProfile profile, DateTimeOffset now)
    {
        var s = profile.Schedule;
        var local = now.ToLocalTime();
        var tod = s.TimeOfDay ?? new TimeSpan(2, 0, 0);

        switch (s.Kind)
        {
            case ScheduleKind.Daily:
            {
                var candidate = local.Date.Add(tod);
                if (candidate <= local)
                    candidate = candidate.AddDays(1);
                return new DateTimeOffset(candidate);
            }
            case ScheduleKind.Weekly:
            {
                var days = s.DaysOfWeek.Count > 0
                    ? s.DaysOfWeek
                    : [DayOfWeek.Monday];
                for (var i = 0; i < 8; i++)
                {
                    var day = local.Date.AddDays(i);
                    if (!days.Contains(day.DayOfWeek))
                        continue;
                    var candidate = day.Add(tod);
                    if (candidate > local)
                        return new DateTimeOffset(candidate);
                }

                return null;
            }
            case ScheduleKind.Interval:
            {
                var mins = Math.Max(1, s.IntervalMinutes ?? 60);
                return local.AddMinutes(mins);
            }
            default:
                return null;
        }
    }

    private static string FormatCountdown(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
            return LocalizationService.Text("main.nextBackupDue");

        string span;
        if (remaining.TotalDays >= 1)
            span = LocalizationService.Text("main.nextBackupSpanDh", (int)remaining.TotalDays, remaining.Hours);
        else if (remaining.TotalHours >= 1)
            span = LocalizationService.Text("main.nextBackupSpanHm", (int)remaining.TotalHours, remaining.Minutes);
        else if (remaining.TotalMinutes >= 1)
            span = LocalizationService.Text("main.nextBackupSpanMs", remaining.Minutes, remaining.Seconds);
        else
            span = LocalizationService.Text("main.nextBackupSpanS", Math.Max(1, remaining.Seconds));

        return LocalizationService.Text("main.nextBackupIn", span);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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
    public ObservableCollection<ProfileListItem> Profiles { get; } = [];
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
        SetReadyStatus();
        SelectLanguageSilent(LocalizationService.Instance.Language);
        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            if (_statusIsReady || string.IsNullOrEmpty(_status))
                SetReadyStatus();
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

    private ProfileListItem? _selectedProfile;
    public ProfileListItem? SelectedProfile
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
    private bool _statusIsReady;

    public string Status
    {
        get => _status;
        set
        {
            if (Set(ref _status, value))
                _statusIsReady = false;
        }
    }

    public void SetReadyStatus()
    {
        _status = LocalizationService.Text("main.statusReady");
        _statusIsReady = true;
        OnPropertyChanged(nameof(Status));
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
