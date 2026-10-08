using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using BackupSaves.Core.Models;
using BackupSaves.Core.Services;
using WpfApp_BackupSaves.Services;

namespace WpfApp_BackupSaves.ViewModels;

public sealed class ArchiveListItem : INotifyPropertyChanged
{
    private string _displayName = "";
    private string _manifestProfileName = "";
    private string _createdDisplay = "";
    private string _formatDisplay = "";
    private bool _excludeFromRetention;
    private int _rowNumber;
    private SolidColorBrush? _ageDotBrush;
    private bool _isVeryFresh;
    private bool _isEditingDisplayName;

    public string Path { get; init; } = "";
    public string Name { get; init; } = "";
    public DateTime LastWriteTime { get; init; }
    public long SizeBytes { get; init; }
    public string SizeDisplay => SizeBytes < 1024 * 1024
        ? $"{SizeBytes / 1024.0:0.0} KB"
        : $"{SizeBytes / (1024.0 * 1024):0.00} MB";

    /// <summary>
    /// Age dot: &lt;5m green, &lt;15m yellow, &lt;30m blue, &lt;60m time color, else date color.
    /// Rows younger than 2 minutes also use <see cref="IsVeryFresh"/> row highlight.
    /// </summary>
    public System.Windows.Media.Brush AgeDotBrush =>
        _ageDotBrush ?? System.Windows.Media.Brushes.Gray;

    /// <summary>True when the archive is younger than 2 minutes — highlight the whole row.</summary>
    public bool IsVeryFresh
    {
        get => _isVeryFresh;
        private set
        {
            if (_isVeryFresh == value) return;
            _isVeryFresh = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Recompute age-dot color and row highlight for the current theme and wall clock.</summary>
    public void RefreshAgeDot(DateTime? now = null)
    {
        var clock = now ?? DateTime.Now;
        var age = clock - LastWriteTime;
        var minutes = age <= TimeSpan.Zero ? 0.0 : age.TotalMinutes;

        IsVeryFresh = minutes < 2.0;

        var color = ComputeAgeDotColor(minutes);
        if (_ageDotBrush is not null && _ageDotBrush.Color == color)
            return;

        var brush = new SolidColorBrush(color);
        if (brush.CanFreeze)
            brush.Freeze();
        _ageDotBrush = brush;
        OnPropertyChanged(nameof(AgeDotBrush));
    }

    private static System.Windows.Media.Color ComputeAgeDotColor(double minutes)
    {
        // <2m: green (row also highlighted); <5m green; <15m yellow; <30m blue; <60m time; else date
        if (minutes < 5.0)
            return ThemeColor("ArchiveAgeFreshBrush", System.Windows.Media.Color.FromRgb(0x00, 0xE6, 0x76));
        if (minutes < 15.0)
            return ThemeColor("ArchiveAgeWarmBrush", System.Windows.Media.Color.FromRgb(0xFF, 0xD6, 0x00));
        if (minutes < 30.0)
            return ThemeColor("ArchiveAgeCoolBrush", System.Windows.Media.Color.FromRgb(0x40, 0xC4, 0xFF));
        if (minutes < 60.0)
            return ThemeColor("PrimaryTextBrush", System.Windows.Media.Color.FromRgb(0xE8, 0xE8, 0xE8));

        return ThemeColor("SecondaryTextBrush", System.Windows.Media.Color.FromRgb(0xA0, 0xA0, 0xA0));
    }

    private static System.Windows.Media.Color ThemeColor(string key, System.Windows.Media.Color fallback)
    {
        if (System.Windows.Application.Current?.TryFindResource(key) is SolidColorBrush brush)
            return brush.Color;
        return fallback;
    }
    /// <summary>1-based row index in the archives list.</summary>
    public int RowNumber
    {
        get => _rowNumber;
        set
        {
            if (_rowNumber == value) return;
            _rowNumber = value;
            OnPropertyChanged();
        }
    }

    /// <summary>User-editable title; empty means fall back to file name.</summary>
    public string DisplayName
    {
        get => _displayName;
        set
        {
            var v = value ?? "";
            if (_displayName == v) return;
            _displayName = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ListTitle));
            OnPropertyChanged(nameof(HasCustomDisplayName));
        }
    }

    /// <summary>List label: custom display name if set, otherwise file name.</summary>
    public string ListTitle =>
        string.IsNullOrWhiteSpace(_displayName) ? Name : _displayName.Trim();

    /// <summary>True when the user set a custom title (not just the file name).</summary>
    public bool HasCustomDisplayName => !string.IsNullOrWhiteSpace(_displayName);

    /// <summary>Inline title TextBox visible in the archives list (second click on selected row).</summary>
    public bool IsEditingDisplayName
    {
        get => _isEditingDisplayName;
        set
        {
            if (_isEditingDisplayName == value) return;
            _isEditingDisplayName = value;
            OnPropertyChanged();
        }
    }
    /// <summary>When true, archive is ignored by retention count and never auto-deleted.</summary>
    public bool ExcludeFromRetention
    {
        get => _excludeFromRetention;
        set
        {
            if (_excludeFromRetention == value) return;
            _excludeFromRetention = value;
            OnPropertyChanged();
        }
    }

    public string ManifestProfileName
    {
        get => _manifestProfileName;
        set
        {
            if (_manifestProfileName == value) return;
            _manifestProfileName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasManifestProfileName));
        }
    }

    public bool HasManifestProfileName => !string.IsNullOrWhiteSpace(_manifestProfileName);

    public string CreatedDisplay
    {
        get => _createdDisplay;
        set
        {
            if (_createdDisplay == value) return;
            _createdDisplay = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCreatedDisplay));
        }
    }

    public bool HasCreatedDisplay => !string.IsNullOrWhiteSpace(_createdDisplay);

    public string FormatDisplay
    {
        get => _formatDisplay;
        set
        {
            if (_formatDisplay == value) return;
            _formatDisplay = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>UI row for the profiles list: live process highlight + next-backup countdown.</summary>
public sealed class ProfileListItem : INotifyPropertyChanged
{
    private BackupProfile _profile;
    private bool _isWatchProcessRunning;
    private string _nextBackupDisplay = "";
    private bool _hasLaunchCommand;
    private bool _canLaunch;
    private bool _launchBlocked;
    private string _launchToolTip = "";
    private string? _launchSig;
    private string? _launchIconKey;
    private ImageSource? _launchIcon;

    public ProfileListItem(BackupProfile profile)
    {
        _profile = profile;
        ApplyLaunch(profile, launchBlocked: false);
    }

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

    public bool HasLaunchCommand => _hasLaunchCommand;

    public bool CanLaunch => _canLaunch;

    public string LaunchToolTip => _launchToolTip;

    public ImageSource? LaunchIcon
    {
        get => _launchIcon;
        private set
        {
            if (ReferenceEquals(_launchIcon, value))
                return;
            _launchIcon = value;
            OnPropertyChanged();
        }
    }

    public void SetLaunchBlocked(bool blocked) => ApplyLaunch(_profile, blocked);

    public void Refresh(
        BackupProfile profile,
        DateTimeOffset now,
        DateTimeOffset? taskNextRun,
        bool? watchProcessRunning = null,
        bool launchBlocked = false)
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
        ApplyLaunch(profile, launchBlocked);
    }

    private void ApplyLaunch(BackupProfile profile, bool launchBlocked)
    {
        var cmd = GameLaunchService.Command(profile);
        var sig = cmd ?? "";
        if (string.Equals(sig, _launchSig, StringComparison.Ordinal) && launchBlocked == _launchBlocked)
            return;

        _launchSig = sig;
        _launchBlocked = launchBlocked;

        var has = cmd is not null;
        if (_hasLaunchCommand != has)
        {
            _hasLaunchCommand = has;
            OnPropertyChanged(nameof(HasLaunchCommand));
        }

        var can = has && !launchBlocked;
        if (_canLaunch != can)
        {
            _canLaunch = can;
            OnPropertyChanged(nameof(CanLaunch));
        }

        var name = has ? GameLaunchService.DisplayName(cmd!) : "";
        var tip = !has
            ? ""
            : launchBlocked
                ? LocalizationService.Text("profile.launchButtonRunning", name)
                : LocalizationService.Text("profile.launchButtonTip", name);
        if (_launchToolTip != tip)
        {
            _launchToolTip = tip;
            OnPropertyChanged(nameof(LaunchToolTip));
        }

        if (string.Equals(cmd, _launchIconKey, StringComparison.OrdinalIgnoreCase))
            return;

        _launchIconKey = cmd;
        LaunchIcon = cmd is null ? null : GameLaunchService.GetIcon(cmd);
    }

    private static string BuildNextBackupDisplay(
        BackupProfile profile,
        DateTimeOffset now,
        DateTimeOffset? taskNextRun,
        bool watchOn,
        bool running)
    {
        if (!profile.Enabled)
            return LocalizationService.Text("main.nextBackupDisabled");

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

public sealed class HistoryLoadMoreItem
{
    public string Caption { get; set; } = "";
}

/// <summary>One distinct archive size available for from/to filter combos.</summary>
public sealed class ArchiveSizeFilterOption
{
    public string Label { get; init; } = "";
    public long SizeBytes { get; init; }
    public bool IsAny { get; init; }

    public override string ToString() => Label;
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    public ObservableCollection<ProfileListItem> Profiles { get; } = [];
    public ObservableCollection<ArchiveListItem> Archives { get; } = [];
    public ObservableCollection<object> History { get; } = [];
    public ObservableCollection<LanguageOption> Languages { get; } = [];
    public ObservableCollection<ArchiveSizeFilterOption> ArchiveSizeFromOptions { get; } = [];
    public ObservableCollection<ArchiveSizeFilterOption> ArchiveSizeToOptions { get; } = [];

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
            var had = _selectedArchive is not null;
            if (!Set(ref _selectedArchive, value))
                return;
            // Only notify when presence flips — spurious HasArchive raises
            // re-run the detail Border Visibility DataTrigger (Collapsed↔Visible blink).
            if (had != (value is not null))
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

    private bool _isBackupProgressVisible;
    public bool IsBackupProgressVisible
    {
        get => _isBackupProgressVisible;
        private set => Set(ref _isBackupProgressVisible, value);
    }

    private double _backupProgressPercent;
    public double BackupProgressPercent
    {
        get => _backupProgressPercent;
        private set => Set(ref _backupProgressPercent, value);
    }

    private string _backupProgressTip = "";
    public string BackupProgressTip
    {
        get => _backupProgressTip;
        private set => Set(ref _backupProgressTip, value);
    }

    public void BeginBackupProgress()
    {
        BackupProgressPercent = 0;
        BackupProgressTip = LocalizationService.Text("main.backupProgressTip", 0);
        IsBackupProgressVisible = true;
    }

    public void ReportBackupProgress(BackupProgress progress)
    {
        var pct = Math.Clamp(progress.Fraction * 100, 0, 100);
        BackupProgressPercent = pct;
        BackupProgressTip = LocalizationService.Text("main.backupProgressTip", (int)Math.Round(pct));
    }

    public void EndBackupProgress()
    {
        IsBackupProgressVisible = false;
        BackupProgressPercent = 0;
        BackupProgressTip = "";
    }

    public bool HasProfile => SelectedProfile is not null;

    private bool _backupUnconditional;
    public bool BackupUnconditional
    {
        get => _backupUnconditional;
        set => Set(ref _backupUnconditional, value);
    }
    public bool HasArchive => SelectedArchive is not null;
    public bool CanInteract => !IsBusy;

    private bool _archivesFiltersVisible;
    public bool ArchivesFiltersVisible
    {
        get => _archivesFiltersVisible;
        set => Set(ref _archivesFiltersVisible, value);
    }

    private string _archiveFilterName = "";
    public string ArchiveFilterName
    {
        get => _archiveFilterName;
        set
        {
            if (Set(ref _archiveFilterName, value ?? ""))
                ArchivesFilterChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private string _archiveFilterHasCustomName = "any";
    public string ArchiveFilterHasCustomName
    {
        get => _archiveFilterHasCustomName;
        set
        {
            if (!Set(ref _archiveFilterHasCustomName, value ?? "any"))
                return;
            OnPropertyChanged(nameof(ArchiveFilterHasCustomNameIsAny));
            OnPropertyChanged(nameof(ArchiveFilterHasCustomNameIsYes));
            OnPropertyChanged(nameof(ArchiveFilterHasCustomNameIsNo));
            ArchivesFilterChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool ArchiveFilterHasCustomNameIsAny
    {
        get => _archiveFilterHasCustomName == "any";
        set { if (value) ArchiveFilterHasCustomName = "any"; }
    }

    public bool ArchiveFilterHasCustomNameIsYes
    {
        get => _archiveFilterHasCustomName == "yes";
        set { if (value) ArchiveFilterHasCustomName = "yes"; }
    }

    public bool ArchiveFilterHasCustomNameIsNo
    {
        get => _archiveFilterHasCustomName == "no";
        set { if (value) ArchiveFilterHasCustomName = "no"; }
    }

    private string _archiveFilterKeepForever = "any";
    public string ArchiveFilterKeepForever
    {
        get => _archiveFilterKeepForever;
        set
        {
            if (!Set(ref _archiveFilterKeepForever, value ?? "any"))
                return;
            OnPropertyChanged(nameof(ArchiveFilterKeepForeverIsAny));
            OnPropertyChanged(nameof(ArchiveFilterKeepForeverIsYes));
            OnPropertyChanged(nameof(ArchiveFilterKeepForeverIsNo));
            ArchivesFilterChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool ArchiveFilterKeepForeverIsAny
    {
        get => _archiveFilterKeepForever == "any";
        set { if (value) ArchiveFilterKeepForever = "any"; }
    }

    public bool ArchiveFilterKeepForeverIsYes
    {
        get => _archiveFilterKeepForever == "yes";
        set { if (value) ArchiveFilterKeepForever = "yes"; }
    }

    public bool ArchiveFilterKeepForeverIsNo
    {
        get => _archiveFilterKeepForever == "no";
        set { if (value) ArchiveFilterKeepForever = "no"; }
    }

    private DateTime? _archiveFilterDateFrom;
    public DateTime? ArchiveFilterDateFrom
    {
        get => _archiveFilterDateFrom;
        set
        {
            if (Set(ref _archiveFilterDateFrom, value))
                ArchivesFilterChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private DateTime? _archiveFilterDateTo;
    public DateTime? ArchiveFilterDateTo
    {
        get => _archiveFilterDateTo;
        set
        {
            if (Set(ref _archiveFilterDateTo, value))
                ArchivesFilterChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private readonly List<ArchiveSizeFilterOption> _archiveSizesAll = [];
    private bool _suppressSizeFilterUi;
    private ArchiveSizeFilterOption? _archiveFilterSizeFrom;
    private ArchiveSizeFilterOption? _archiveFilterSizeTo;

    public ArchiveSizeFilterOption? ArchiveFilterSizeFrom
    {
        get => _archiveFilterSizeFrom;
        set
        {
            if (SameSizeSelection(_archiveFilterSizeFrom, value))
                return;

            _archiveFilterSizeFrom = value;
            OnPropertyChanged();
            // Dropdown rebuild assigns this while suppressed. A filter refresh here
            // resets the virtualized archives list and can drop the newest row.
            if (_suppressSizeFilterUi)
                return;

            _suppressSizeFilterUi = true;
            try { RefreshSizeToOptions(); }
            finally { _suppressSizeFilterUi = false; }

            ArchivesFilterChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ArchiveSizeFilterOption? ArchiveFilterSizeTo
    {
        get => _archiveFilterSizeTo;
        set
        {
            if (SameSizeSelection(_archiveFilterSizeTo, value))
                return;

            _archiveFilterSizeTo = value;
            OnPropertyChanged();
            if (_suppressSizeFilterUi)
                return;

            _suppressSizeFilterUi = true;
            try { RefreshSizeFromOptions(); }
            finally { _suppressSizeFilterUi = false; }

            ArchivesFilterChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ArchivesFilterChanged;

    public bool MatchesArchiveFilter(ArchiveListItem a)
    {
        if (!string.IsNullOrWhiteSpace(_archiveFilterName))
        {
            var q = _archiveFilterName.Trim();
            if (a.ListTitle.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) < 0
                && a.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) < 0)
                return false;
        }

        if (_archiveFilterHasCustomName == "yes" && !a.HasCustomDisplayName)
            return false;
        if (_archiveFilterHasCustomName == "no" && a.HasCustomDisplayName)
            return false;

        if (_archiveFilterKeepForever == "yes" && !a.ExcludeFromRetention)
            return false;
        if (_archiveFilterKeepForever == "no" && a.ExcludeFromRetention)
            return false;

        if (_archiveFilterDateFrom is { } from && a.LastWriteTime.Date < from.Date)
            return false;
        if (_archiveFilterDateTo is { } to && a.LastWriteTime.Date > to.Date)
            return false;

        if (_archiveFilterSizeFrom is { IsAny: false } sizeFrom && a.SizeBytes < sizeFrom.SizeBytes)
            return false;
        if (_archiveFilterSizeTo is { IsAny: false } sizeTo && a.SizeBytes > sizeTo.SizeBytes)
            return false;

        return true;
    }

    private static bool SameSizeSelection(ArchiveSizeFilterOption? a, ArchiveSizeFilterOption? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.IsAny && b.IsAny) return true;
        return !a.IsAny && !b.IsAny
               && a.SizeBytes == b.SizeBytes
               && string.Equals(a.Label, b.Label, StringComparison.Ordinal);
    }

    private static ArchiveSizeFilterOption CreateSizeAnyOption() =>
        new()
        {
            Label = LocalizationService.Text("archive.filterAny"),
            IsAny = true
        };

    /// <summary>
    /// Rebuild size from/to dropdowns from current archives (distinct labels, small list).
    /// </summary>
    public void RebuildArchiveSizeFilterOptions()
    {
        const int maxOptions = 32;

        var all = Archives
            .GroupBy(a => a.SizeDisplay, StringComparer.Ordinal)
            .Select(g => new ArchiveSizeFilterOption
            {
                Label = g.Key,
                SizeBytes = g.Min(x => x.SizeBytes)
            })
            .OrderByDescending(g => g.SizeBytes)
            .ThenBy(g => g.Label, StringComparer.Ordinal)
            .ToList();

        var groups = SampleSizeFilterOptions(all, maxOptions);

        var same = groups.Count == _archiveSizesAll.Count
                   && groups.Select(g => g.Label)
                       .SequenceEqual(_archiveSizesAll.Select(g => g.Label), StringComparer.Ordinal);
        if (!same)
        {
            _archiveSizesAll.Clear();
            _archiveSizesAll.AddRange(groups);
        }

        RefreshSizeFilterDropdowns(keepSelection: true);
    }

    /// <summary>
    /// Keep up to <paramref name="maxCount"/> sizes from largest to smallest;
    /// if there are more distinct sizes, skip evenly spaced intermediates so the range still spans max→min.
    /// </summary>
    private static List<ArchiveSizeFilterOption> SampleSizeFilterOptions(
        IReadOnlyList<ArchiveSizeFilterOption> orderedLargestFirst,
        int maxCount)
    {
        if (orderedLargestFirst.Count <= maxCount)
            return orderedLargestFirst is List<ArchiveSizeFilterOption> list
                ? list
                : orderedLargestFirst.ToList();

        var result = new List<ArchiveSizeFilterOption>(maxCount);
        var lastIndex = orderedLargestFirst.Count - 1;
        for (var i = 0; i < maxCount; i++)
        {
            var index = i * lastIndex / (maxCount - 1);
            result.Add(orderedLargestFirst[index]);
        }

        return result;
    }

    private void RefreshSizeFilterDropdowns(bool keepSelection)
    {
        var prevFrom = keepSelection ? _archiveFilterSizeFrom : null;
        var prevTo = keepSelection ? _archiveFilterSizeTo : null;

        _suppressSizeFilterUi = true;
        try
        {
            RefreshSizeFromOptions(prevFrom);
            RefreshSizeToOptions(prevTo);
        }
        finally
        {
            _suppressSizeFilterUi = false;
        }
    }

    private void RefreshSizeFromOptions(ArchiveSizeFilterOption? prefer = null)
    {
        prefer ??= _archiveFilterSizeFrom;
        var maxBytes = _archiveFilterSizeTo is { IsAny: false } t ? t.SizeBytes : long.MaxValue;

        ArchiveSizeFromOptions.Clear();
        var any = CreateSizeAnyOption();
        ArchiveSizeFromOptions.Add(any);
        foreach (var s in _archiveSizesAll.Where(s => s.SizeBytes <= maxBytes))
            ArchiveSizeFromOptions.Add(s);

        _archiveFilterSizeFrom = ResolveSizeSelection(prefer, ArchiveSizeFromOptions, any);
        OnPropertyChanged(nameof(ArchiveFilterSizeFrom));
    }

    private void RefreshSizeToOptions(ArchiveSizeFilterOption? prefer = null)
    {
        prefer ??= _archiveFilterSizeTo;
        var minBytes = _archiveFilterSizeFrom is { IsAny: false } f ? f.SizeBytes : 0L;

        ArchiveSizeToOptions.Clear();
        var any = CreateSizeAnyOption();
        ArchiveSizeToOptions.Add(any);
        foreach (var s in _archiveSizesAll.Where(s => s.SizeBytes >= minBytes))
            ArchiveSizeToOptions.Add(s);

        _archiveFilterSizeTo = ResolveSizeSelection(prefer, ArchiveSizeToOptions, any);
        OnPropertyChanged(nameof(ArchiveFilterSizeTo));
    }

    private static ArchiveSizeFilterOption ResolveSizeSelection(
        ArchiveSizeFilterOption? prefer,
        ObservableCollection<ArchiveSizeFilterOption> options,
        ArchiveSizeFilterOption any)
    {
        if (prefer is null || prefer.IsAny)
            return any;

        var match = options.FirstOrDefault(o =>
            !o.IsAny && o.SizeBytes == prefer.SizeBytes
            && string.Equals(o.Label, prefer.Label, StringComparison.Ordinal));
        return match ?? any;
    }

    private Guid? _historyFilterProfileId;
    public Guid? HistoryFilterProfileId
    {
        get => _historyFilterProfileId;
        set
        {
            if (Set(ref _historyFilterProfileId, value))
            {
                OnPropertyChanged(nameof(HasHistoryFilter));
                OnPropertyChanged(nameof(HistoryFilterDisplay));
            }
        }
    }

    public bool HasHistoryFilter => HistoryFilterProfileId is not null;

    private string _historyFilterDisplay = "";
    public string HistoryFilterDisplay
    {
        get => _historyFilterDisplay;
        set => Set(ref _historyFilterDisplay, value);
    }

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
