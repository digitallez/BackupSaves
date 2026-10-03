using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BackupSaves.Core.IO;
using BackupSaves.Core.Models;
using BackupSaves.Core.Services;
using WpfApp_BackupSaves.Services;
using WpfApp_BackupSaves.Dialogs;
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
        WatchProcessScanBox.Text = (Profile.WatchProcessScanSeconds <= 0 ? 10 : Profile.WatchProcessScanSeconds).ToString();
        LaunchBox.Text = Profile.LaunchCommand ?? "";
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
        WatchProcessScanSeconds = p.WatchProcessScanSeconds,
        WatchProcessWasRunning = p.WatchProcessWasRunning,
        LaunchCommand = p.LaunchCommand,
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

    private void BrowseLaunch_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = LocalizationService.Text("profile.launchBrowseTitle"),
            Filter = LocalizationService.Text("profile.launchBrowseFilter"),
            CheckFileExists = true
        };
        var current = LaunchBox.Text?.Trim().Trim('"');
        if (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(current))
                dlg.InitialDirectory = Path.GetDirectoryName(current);
            else
            {
                var dir = Path.GetDirectoryName(current);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    dlg.InitialDirectory = dir;
            }
        }

        if (dlg.ShowDialog(this) == true)
            LaunchBox.Text = dlg.FileName;
    }

    private void PickSteam_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SteamLibraryWindow { Owner = this };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.SelectedCommand))
            LaunchBox.Text = dlg.SelectedCommand;
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

    private void SourcesList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var dep = e.OriginalSource as DependencyObject;
        while (dep is not null && dep is not System.Windows.Controls.ListBoxItem)
            dep = VisualTreeHelper.GetParent(dep);

        if (dep is System.Windows.Controls.ListBoxItem item)
            item.IsSelected = true;
    }

    private void SourcesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SourcesList.SelectedItem is SourceEntry)
            OpenSelectedSourceInExplorer();
    }

    private void SourcesList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.F2)
        {
            EditSelectedSourcePath();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            RemoveSource_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            OpenSelectedSourceInExplorer();
            e.Handled = true;
        }
    }

    private void OpenSourceInExplorer_Click(object sender, RoutedEventArgs e) =>
        OpenSelectedSourceInExplorer();

    private void EditSourcePath_Click(object sender, RoutedEventArgs e) =>
        EditSelectedSourcePath();

    private void CopySourcePath_Click(object sender, RoutedEventArgs e)
    {
        if (SourcesList.SelectedItem is not SourceEntry s || string.IsNullOrWhiteSpace(s.Path))
            return;

        try
        {
            System.Windows.Clipboard.SetText(s.Path);
        }
        catch
        {
            // clipboard can be locked by another process
        }
    }

    private void OpenSelectedSourceInExplorer()
    {
        if (SourcesList.SelectedItem is not SourceEntry s)
            return;

        var path = PathHelper.ExpandPath(s.Path);
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var isDir = s.Type == SourceType.Directory || Directory.Exists(path);
            if (isDir && !File.Exists(path))
            {
                if (!Directory.Exists(path))
                {
                    AppMessageBox.Show(this, LocalizationService.Text("profile.errSourceMissing", path),
                        LocalizationService.Text("common.appName"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true
                });
                return;
            }

            if (!File.Exists(path))
            {
                AppMessageBox.Show(this, LocalizationService.Text("profile.errSourceMissing", path),
                    LocalizationService.Text("common.appName"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(this, LocalizationService.Text("msg.explorerFailed", ex.Message),
                LocalizationService.Text("common.appName"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void EditSelectedSourcePath()
    {
        if (SourcesList.SelectedItem is not SourceEntry s)
            return;

        var dlg = new SourcePathEditWindow(s.Path) { Owner = this };
        if (dlg.ShowDialog() != true)
            return;

        s.Path = dlg.PathText;
        InferSourceType(s);
        SourcesList.Items.Refresh();
    }

    private static void InferSourceType(SourceEntry entry)
    {
        var expanded = PathHelper.ExpandPath(entry.Path);
        if (Directory.Exists(expanded))
            entry.Type = SourceType.Directory;
        else if (File.Exists(expanded))
            entry.Type = SourceType.File;
        else if (entry.Path.EndsWith('\\') || entry.Path.EndsWith('/'))
            entry.Type = SourceType.Directory;
        // otherwise keep previous Type (path may use env vars that don't exist yet)
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            AppMessageBox.Show(this, LocalizationService.Text("profile.errName"),
                LocalizationService.Text("common.appName"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(RootBox.Text))
        {
            AppMessageBox.Show(this, LocalizationService.Text("profile.errRoot"),
                LocalizationService.Text("common.appName"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (Profile.Sources.Count == 0)
        {
            AppMessageBox.Show(this, LocalizationService.Text("profile.errSources"),
                LocalizationService.Text("common.appName"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(RetentionBox.Text, out var retention) || retention < 1)
        {
            AppMessageBox.Show(this, LocalizationService.Text("profile.errRetention"),
                LocalizationService.Text("common.appName"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var watchEnabled = WatchProcessEnabled.IsChecked == true;
        var watchPattern = WatchProcessBox.Text?.Trim();
        if (watchEnabled && !ProcessWatchService.IsMatchPatternConfigured(watchPattern))
        {
            AppMessageBox.Show(this, LocalizationService.Text("profile.errWatchProcess"),
                LocalizationService.Text("common.appName"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(WatchProcessScanBox.Text, out var scanSec) || scanSec < 1)
        {
            AppMessageBox.Show(this, LocalizationService.Text("profile.errProcessScanSec"),
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
        ProcessWatchService.Invalidate(Profile.WatchProcessPattern);
        Profile.WatchProcessPattern = string.IsNullOrWhiteSpace(watchPattern) ? null : watchPattern;
        Profile.WatchProcessScanSeconds = scanSec;
        ProcessWatchService.Invalidate(Profile.WatchProcessPattern);
        if (Profile.WatchProcessEnabled && ProcessWatchService.IsMatchPatternConfigured(Profile.WatchProcessPattern))
        {
            Profile.WatchProcessWasRunning =
                ProcessWatchService.IsAnyMatchingProcessRunning(Profile.WatchProcessPattern);
        }
        else
        {
            Profile.WatchProcessWasRunning = false;
        }

        Profile.LaunchCommand = string.IsNullOrWhiteSpace(LaunchBox.Text) ? null : LaunchBox.Text.Trim();

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
                AppMessageBox.Show(this, LocalizationService.Text("profile.errInterval"),
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
