using System.Text.Json;
using System.Text.Json.Serialization;

namespace BackupSaves.Core.Models;

public sealed class UiSettings
{
    /// <summary>Unknown JSON fields kept across load/save so older builds do not wipe newer settings.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public bool MinimizeToTray { get; set; } = true;
    public bool StartMinimized { get; set; }

    /// <summary>
    /// Show balloon tip when minimizing / hiding to tray.
    /// Null = not explicitly set: show up to <see cref="TrayBalloonSoftLimit"/> times, then auto-set to false.
    /// Once true/false, only the user choice applies.
    /// </summary>
    public bool? ShowTrayBalloonTip { get; set; }

    /// <summary>Times the tray balloon was shown while <see cref="ShowTrayBalloonTip"/> was unset.</summary>
    public int TrayBalloonTipShownCount { get; set; }

    /// <summary>Soft-intro shows before auto-disabling when the option is unset.</summary>
    public const int TrayBalloonSoftLimit = 3;

    public AppTheme Theme { get; set; } = AppTheme.Dark;

    /// <summary>Active locale id from Locales JSON (@id), e.g. "ru", "cn". Null = resolve from OS @match.</summary>
    public string? Language { get; set; }

    /// <summary>
    /// When true: check GitHub on startup and when the close dialog (exit / tray) is shown.
    /// Manual check in Settings always works. Silent exit/tray preference skips the exit check.
    /// </summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Remote version the user chose to ignore (e.g. 1.0.6).</summary>
    public string? SkippedUpdateVersion { get; set; }

    /// <summary>Deferred update: apply on real exit, do not restart.</summary>
    public string? PendingUpdateVersion { get; set; }

    /// <summary>Local path to already downloaded zip for pending update.</summary>
    public string? PendingUpdateZipPath { get; set; }

    /// <summary>
    /// Fingerprint of local build-folder archive/exe already applied (length:mtimeUtcTicks:…).
    /// Used to avoid re-prompting the same candidate on every startup.
    /// </summary>
    public string? LastAppliedLocalArchiveKey { get; set; }

    /// <summary>
    /// Path to the development/build output folder.
    /// When set, version checks include BackupSaves.exe / archives there
    /// (Debug builds rank above same-number Release).
    /// </summary>
    public string? DevBuildFolder { get; set; }

    /// <summary>Last main-window Left (DIP). Null = no saved placement.</summary>
    public double? WindowLeft { get; set; }

    /// <summary>Last main-window Top (DIP).</summary>
    public double? WindowTop { get; set; }

    /// <summary>Last main-window Width (DIP, restore bounds when maximized).</summary>
    public double? WindowWidth { get; set; }

    /// <summary>Last main-window Height (DIP, restore bounds when maximized).</summary>
    public double? WindowHeight { get; set; }

    /// <summary>Whether the main window was maximized.</summary>
    public bool WindowMaximized { get; set; }

    /// <summary>
    /// Close button behavior. Ask = show dialog; otherwise apply without asking
    /// (still forced when an update option is available).
    /// </summary>
    public CloseActionPreference CloseAction { get; set; } = CloseActionPreference.Ask;

    /// <summary>True after the first-run install/use-here prompt was answered.</summary>
    public bool InstallPromptCompleted { get; set; }

    /// <summary>
    /// When true, the profiles list shows only Enabled profiles; otherwise all profiles.
    /// </summary>
    public bool ShowOnlyActiveProfiles { get; set; }

    /// <summary>When true, the History list and its header are visible.</summary>
    public bool ShowHistory { get; set; } = true;
}
