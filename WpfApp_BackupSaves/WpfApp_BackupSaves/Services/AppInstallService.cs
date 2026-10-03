using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using BackupSaves.Core.Services;

namespace WpfApp_BackupSaves.Services;

/// <summary>
/// Copies the running app into %LocalAppData%\BackupSaves and creates a desktop shortcut.
/// </summary>
public static class AppInstallService
{
    public static string RecommendedInstallDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            SettingsStore.AppFolderName);

    public static string RecommendedExePath =>
        Path.Combine(RecommendedInstallDirectory, "BackupSaves.exe");

    public static bool IsRunningFromInstallDirectory()
    {
        var current = NormalizeDir(AppContext.BaseDirectory);
        var install = NormalizeDir(RecommendedInstallDirectory);
        return string.Equals(current, install, StringComparison.OrdinalIgnoreCase);
    }

    public static void Install(bool createDesktopShortcut = true)
    {
        var sourceDir = AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var targetDir = RecommendedInstallDirectory;

        if (string.Equals(
                NormalizeDir(sourceDir),
                NormalizeDir(targetDir),
                StringComparison.OrdinalIgnoreCase))
        {
            if (createDesktopShortcut)
                CreateDesktopShortcut();
            AppLog.Default.Info("Install", $"Already running from install dir: \"{targetDir}\"");
            return;
        }

        Directory.CreateDirectory(targetDir);
        AppLog.Default.Info("Install", $"Copying \"{sourceDir}\" → \"{targetDir}\"");

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, file);
            if (ShouldSkip(rel))
                continue;

            var dest = Path.Combine(targetDir, rel);
            var destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            File.Copy(file, dest, overwrite: true);
        }

        if (createDesktopShortcut)
            CreateDesktopShortcut();

        AppLog.Default.Info("Install", $"Install complete → \"{RecommendedExePath}\"");
    }

    public static void CreateDesktopShortcut()
    {
        var exePath = File.Exists(RecommendedExePath)
            ? RecommendedExePath
            : Environment.ProcessPath
              ?? throw new InvalidOperationException(LocalizationService.Text("update.errExePath"));

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var lnkPath = Path.Combine(desktop, "BackupSaves.lnk");
        var workDir = Path.GetDirectoryName(exePath) ?? RecommendedInstallDirectory;

        CreateShortcut(lnkPath, exePath, workDir, "BackupSaves");
        AppLog.Default.Info("Install", $"Desktop shortcut → \"{lnkPath}\"");
    }

    public static void LaunchInstalledAndExit()
    {
        var exe = RecommendedExePath;
        if (!File.Exists(exe))
            throw new FileNotFoundException(LocalizationService.Text("install.exeMissing"), exe);

        // Release GUI mutex so the new process can become primary.
        if (System.Windows.Application.Current is App app)
            app.ReleaseSingleInstanceForRelaunch();

        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = RecommendedInstallDirectory,
            UseShellExecute = true
        });
        AppLog.Default.Info("Install", $"Launched installed exe, shutting down: \"{exe}\"");
        System.Windows.Application.Current.Shutdown(0);
    }

    private static bool ShouldSkip(string relativePath)
    {
        var name = Path.GetFileName(relativePath);
        if (name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static string NormalizeDir(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static void CreateShortcut(string lnkPath, string targetPath, string workingDir, string description)
    {
        // WScript.Shell COM via reflection — no Microsoft.CSharp/dynamic dependency.
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
                        ?? throw new InvalidOperationException(LocalizationService.Text("install.shortcutFailed"));
        var shell = Activator.CreateInstance(shellType)
                    ?? throw new InvalidOperationException(LocalizationService.Text("install.shortcutFailed"));
        object? shortcut = null;
        try
        {
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: [lnkPath]);
            if (shortcut is null)
                throw new InvalidOperationException(LocalizationService.Text("install.shortcutFailed"));

            var shortcutType = shortcut.GetType();
            shortcutType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, [targetPath]);
            shortcutType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, [workingDir]);
            shortcutType.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, shortcut, [description]);
            shortcutType.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, shortcut, [targetPath + ",0"]);
            shortcutType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
                Marshal.FinalReleaseComObject(shortcut);
            if (Marshal.IsComObject(shell))
                Marshal.FinalReleaseComObject(shell);
        }
    }
}
