using System.Windows;
using MessageBox = System.Windows.MessageBox;
using BackupSaves.Core.IO;
using BackupSaves.Core.Models;
using BackupSaves.Core.Services;
using WpfApp_BackupSaves.Services;
using WinForms = System.Windows.Forms;

namespace WpfApp_BackupSaves.Dialogs;

public partial class ProfileEditWindow : Window
{
    public BackupProfile Profile { get; }

    public ProfileEditWindow(BackupProfile? existing = null)
    {
        InitializeComponent();
        CustomWindowChrome.Apply(this);
        Profile = existing is null
            ? new BackupProfile()
            : Clone(existing);

        NameBox.Text = Profile.Name;
        RootBox.Text = Profile.BackupRoot;
        RetentionBox.Text = Profile.RetentionCount.ToString();
        ChecksumSkipBox.IsChecked = Profile.SkipUnchangedByChecksum;
        WatchProcessEnabled.IsChecked = Profile.WatchProcessEnabled;
        WatchProcessBox.Text = Profile.WatchProcessPattern ?? "";
        FormatBox.SelectedIndex = Profile.Format == ArchiveFormat.Zip ? 1 : 0;
        ScheduleEnabled.IsChecked = Profile.Schedule.Enabled;
        InAppScheduleEnabled.IsChecked = Profile.Schedule.InAppEnabled;
        ScheduleKindBox.SelectedIndex = Profile.Schedule.Kind switch
        {
            ScheduleKind.Weekly => 1,
            ScheduleKind.OnLogon => 2,
            ScheduleKind.Interval => 3,
            _ => 0
        };
        TimeBox.Text = (Profile.Schedule.TimeOfDay ?? TimeSpan.FromHours(2)).ToString(@"hh\:mm");
        IntervalBox.Text = (Profile.Schedule.IntervalMinutes ?? 60).ToString();
        SourcesList.ItemsSource = Profile.Sources;
    }

    private bool _scheduleModeSync;

    private void ScheduleMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_scheduleModeSync) return;
        _scheduleModeSync = true;
        try
        {
            if (ReferenceEquals(sender, ScheduleEnabled) && ScheduleEnabled.IsChecked == true)
                InAppScheduleEnabled.IsChecked = false;
            else if (ReferenceEquals(sender, InAppScheduleEnabled) && InAppScheduleEnabled.IsChecked == true)
                ScheduleEnabled.IsChecked = false;
        }
        finally
        {
            _scheduleModeSync = false;
        }
    }

    private static BackupProfile Clone(BackupProfile p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Slug = p.Slug,
        BackupRoot = p.BackupRoot,
        Format = p.Format,
        RetentionCount = p.RetentionCount,
        SkipUnchangedByChecksum = p.SkipUnchangedByChecksum,
        WatchProcessEnabled = p.WatchProcessEnabled,
        WatchProcessPattern = p.WatchProcessPattern,
        WatchProcessWasRunning = p.WatchProcessWasRunning,
        Sources = p.Sources.Select(s => new SourceEntry { Path = s.Path, Type = s.Type }).ToList(),
        Schedule = new ScheduleConfig
        {
            Enabled = p.Schedule.Enabled,
            InAppEnabled = p.Schedule.InAppEnabled,
            Kind = p.Schedule.Kind,
            TimeOfDay = p.Schedule.TimeOfDay,
            DaysOfWeek = p.Schedule.DaysOfWeek.ToList(),
            IntervalMinutes = p.Schedule.IntervalMinutes,
            LastInAppBackupUtc = p.Schedule.LastInAppBackupUtc
        }
    };

    private void BrowseRoot_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new WinForms.FolderBrowserDialog
        {
            Description = LocalizationService.Text("profile.browseRootDesc"),
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() == WinForms.DialogResult.OK)
            RootBox.Text = dlg.SelectedPath;
    }

    private void BrowseExe_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = LocalizationService.Text("profile.browseExeTitle"),
            Filter = LocalizationService.Text("profile.browseExeFilter"),
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) == true)
            WatchProcessBox.Text = dlg.FileName;
    }

    private void PickProcess_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ProcessPickerWindow { Owner = this };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.SelectedPattern))
            WatchProcessBox.Text = dlg.SelectedPattern;
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new WinForms.FolderBrowserDialog
        {
            Description = LocalizationService.Text("profile.browseFolderDesc"),
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() != WinForms.DialogResult.OK)
            return;

        Profile.Sources.Add(new SourceEntry { Path = dlg.SelectedPath, Type = SourceType.Directory });
        SourcesList.Items.Refresh();
    }

    private void AddFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Title = LocalizationService.Text("profile.browseFilesTitle")
        };
        if (dlg.ShowDialog(this) != true)
            return;

        foreach (var f in dlg.FileNames)
            Profile.Sources.Add(new SourceEntry { Path = f, Type = SourceType.File });
        SourcesList.Items.Refresh();
    }

    private void RemoveSource_Click(object sender, RoutedEventArgs e)
    {
        if (SourcesList.SelectedItem is SourceEntry s)
        {
            Profile.Sources.Remove(s);
            SourcesList.Items.Refresh();
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show(this, LocalizationService.Text("profile.errName"),
                LocalizationService.Text("common.appName"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(RootBox.Text))
        {
            MessageBox.Show(this, LocalizationService.Text("profile.errRoot"),
                LocalizationService.Text("common.appName"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (Profile.Sources.Count == 0)
        {
            MessageBox.Show(this, LocalizationService.Text("profile.errSources"),
                LocalizationService.Text("common.appName"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(RetentionBox.Text, out var retention) || retention < 1)
        {
            MessageBox.Show(this, LocalizationService.Text("profile.errRetention"),
                LocalizationService.Text("common.appName"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var watchEnabled = WatchProcessEnabled.IsChecked == true;
        var watchPattern = WatchProcessBox.Text?.Trim();
        if (watchEnabled && !ProcessWatchService.IsMatchPatternConfigured(watchPattern))
        {
            MessageBox.Show(this, LocalizationService.Text("profile.errWatchProcess"),
                LocalizationService.Text("common.appName"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Profile.Name = NameBox.Text.Trim();
        Profile.Slug = PathHelper.ToSlug(Profile.Name);
        Profile.BackupRoot = RootBox.Text.Trim();
        Profile.RetentionCount = retention;
        Profile.SkipUnchangedByChecksum = ChecksumSkipBox.IsChecked == true;
        Profile.WatchProcessEnabled = watchEnabled;
        Profile.WatchProcessPattern = string.IsNullOrWhiteSpace(watchPattern) ? null : watchPattern;
        if (Profile.WatchProcessEnabled && ProcessWatchService.IsMatchPatternConfigured(Profile.WatchProcessPattern))
        {
            Profile.WatchProcessWasRunning =
                ProcessWatchService.IsAnyMatchingProcessRunning(Profile.WatchProcessPattern);
        }
        else
        {
            Profile.WatchProcessWasRunning = false;
        }

        Profile.Format = (FormatBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() == "Zip"
            ? ArchiveFormat.Zip
            : ArchiveFormat.SevenZip;

        Profile.Schedule.Enabled = ScheduleEnabled.IsChecked == true;
        Profile.Schedule.InAppEnabled = InAppScheduleEnabled.IsChecked == true;
        if (Profile.Schedule.Enabled)
            Profile.Schedule.InAppEnabled = false;
        if (Profile.Schedule.InAppEnabled)
            Profile.Schedule.Enabled = false;

        Profile.Schedule.Kind = (ScheduleKindBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() switch
        {
            "Weekly" => ScheduleKind.Weekly,
            "OnLogon" => ScheduleKind.OnLogon,
            "Interval" => ScheduleKind.Interval,
            _ => ScheduleKind.Daily
        };

        if (TimeSpan.TryParse(TimeBox.Text, out var tod))
            Profile.Schedule.TimeOfDay = tod;

        if (int.TryParse(IntervalBox.Text, out var mins) && mins > 0)
            Profile.Schedule.IntervalMinutes = mins;
        else
            Profile.Schedule.IntervalMinutes = null;

        if (Profile.Schedule.InAppEnabled)
        {
            if (Profile.Schedule.IntervalMinutes is null or < 1)
            {
                MessageBox.Show(this, LocalizationService.Text("profile.errInterval"),
                    LocalizationService.Text("common.appName"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Start counting interval from save time (don't backup immediately).
            if (Profile.Schedule.LastInAppBackupUtc is null)
                Profile.Schedule.LastInAppBackupUtc = DateTimeOffset.UtcNow;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
