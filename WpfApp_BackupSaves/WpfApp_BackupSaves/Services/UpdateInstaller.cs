using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using BackupSaves.Core.Services;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace WpfApp_BackupSaves.Services;

/// <summary>Debug local update source: archive file or build-output directory.</summary>
public sealed class LocalUpdateCandidate
{
    public required string DisplayPath { get; init; }
    public string? ArchivePath { get; init; }
    public string? BuildDirectory { get; init; }
    public required string VersionLabel { get; init; }
    public required string FingerprintKey { get; init; }
    public bool IsNewer { get; init; }
}

public static class UpdateInstaller
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BackupSaves", AppVersion.Numeric.ToString()));
        return c;
    }

    public static string UpdatesDirectory
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BackupSaves",
                "updates");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string LogsDirectory
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BackupSaves",
                "logs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>
    /// Debug: find a newer local build to offer as an update.
    /// Prefers <paramref name="preferredFolder"/> (exe version compare), then archives next to the running exe.
    /// </summary>
    public static LocalUpdateCandidate? FindLocalUpdateCandidate(string? preferredFolder)
    {
        if (!string.IsNullOrWhiteSpace(preferredFolder))
        {
            var fromDev = TryCandidateFromDirectory(preferredFolder, onlyIfNewer: true);
            if (fromDev is not null)
            {
                AppLog.Default.Info("Update",
                    $"Dev-folder candidate: \"{fromDev.DisplayPath}\" v={fromDev.VersionLabel} newer={fromDev.IsNewer}");
                return fromDev;
            }
        }

        foreach (var dir in EnumerateAppDirs())
        {
            if (!string.IsNullOrWhiteSpace(preferredFolder)
                && PathsEqual(dir, preferredFolder))
                continue;

            var fromApp = TryCandidateFromDirectory(dir, onlyIfNewer: true);
            if (fromApp is not null)
            {
                AppLog.Default.Info("Update",
                    $"Local candidate next to app: \"{fromApp.DisplayPath}\" v={fromApp.VersionLabel}");
                return fromApp;
            }
        }

        AppLog.Default.Info("Update",
            $"No newer local build (devFolder=\"{preferredFolder}\", ProcessPath=\"{Environment.ProcessPath}\")");
        return null;
    }

    /// <summary>
    /// Debug helper: looks for a local update archive next to the exe (and optionally in <paramref name="preferredFolder"/>).
    /// Tries, in order:
    /// <list type="number">
    /// <item><c>{folderName}.7z/.zip</c> (works in <c>bin/Debug/net9.0-…</c>)</item>
    /// <item><c>{exeNameWithoutExt}.7z/.zip</c> (e.g. <c>BackupSaves.7z</c> in a deployed folder)</item>
    /// <item>any <c>net*-windows*.7z/.zip</c> or <c>net*.7z/.zip</c> in the folder
    /// (e.g. <c>net9.0-windows10.0.17763.0.7z</c> inside a folder named <c>BackupSaves</c>)</item>
    /// </list>
    /// When several TFM-named archives match, the newest by mtime wins.
    /// </summary>
    public static string? FindLocalBuildFolderArchive(string? preferredFolder = null)
    {
        if (!string.IsNullOrWhiteSpace(preferredFolder))
        {
            try
            {
                var full = Path.GetFullPath(preferredFolder);
                if (Directory.Exists(full))
                {
                    var found = FindArchiveInDirectory(full);
                    if (found is not null)
                    {
                        AppLog.Default.Info("Update", $"Local build-folder archive found: \"{found}\"");
                        return found;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Default.Warn("Update", $"Dev folder scan failed: {ex.Message}");
            }
        }

        foreach (var dir in EnumerateAppDirs())
        {
            var found = FindArchiveInDirectory(dir);
            if (found is not null)
            {
                AppLog.Default.Info("Update", $"Local build-folder archive found: \"{found}\"");
                return found;
            }
        }

        AppLog.Default.Info("Update",
            $"No local build-folder archive next to exe (ProcessPath=\"{Environment.ProcessPath}\", BaseDirectory=\"{AppContext.BaseDirectory}\")");
        return null;
    }

    private static LocalUpdateCandidate? TryCandidateFromDirectory(string dir, bool onlyIfNewer)
    {
        string full;
        try { full = Path.GetFullPath(dir); }
        catch { return null; }

        if (!Directory.Exists(full))
            return null;

        // Prefer BackupSaves.exe version compare (dev-folder workflow).
        var exePath = Path.Combine(full, "BackupSaves.exe");
        if (File.Exists(exePath) && !IsRunningExe(exePath))
        {
            var version = AppVersion.ReadFromExe(exePath) ?? "?";
            var newer = AppVersion.IsBuildNewer(version, AppVersion.Current);
            if (!onlyIfNewer || newer)
            {
                return new LocalUpdateCandidate
                {
                    DisplayPath = full,
                    BuildDirectory = full,
                    VersionLabel = version,
                    FingerprintKey = GetLocalExeKey(exePath) ?? full,
                    IsNewer = newer
                };
            }
        }

        // Fallback: packed archive in the folder.
        var archive = FindArchiveInDirectory(full);
        if (archive is null)
            return null;

        var archiveNewer = IsLocalArchiveLikelyNewer(archive);
        if (onlyIfNewer && !archiveNewer)
            return null;

        return new LocalUpdateCandidate
        {
            DisplayPath = archive,
            ArchivePath = archive,
            VersionLabel = Path.GetFileName(archive),
            FingerprintKey = GetLocalArchiveKey(archive) ?? archive,
            IsNewer = archiveNewer
        };
    }

    private static bool IsRunningExe(string exePath)
    {
        var current = Environment.ProcessPath;
        return !string.IsNullOrWhiteSpace(current) && PathsEqual(current, exePath);
    }

    private static bool PathsEqual(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string? FindArchiveInDirectory(string dir)
    {
        var folderName = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var exeBase = Environment.ProcessPath is { } exe
            ? Path.GetFileNameWithoutExtension(exe)
            : "BackupSaves";

        // Exact names first: folder name, then exe name (BackupSaves.7z).
        foreach (var baseName in DistinctNames(folderName, exeBase))
        {
            foreach (var ext in new[] { ".7z", ".zip" })
            {
                var candidate = Path.Combine(dir, baseName + ext);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        // Deployed folder may be "BackupSaves" while the archive keeps the TFM name.
        try
        {
            var tfmArchives = Directory.EnumerateFiles(dir)
                .Where(IsTfmNamedArchive)
                .Select(p => new FileInfo(p))
                .OrderByDescending(fi => fi.Extension.Equals(".7z", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(fi => fi.LastWriteTimeUtc)
                .Select(fi => fi.FullName)
                .FirstOrDefault();

            return tfmArchives;
        }
        catch (Exception ex)
        {
            AppLog.Default.Warn("Update", $"Scan for TFM archives in \"{dir}\" failed: {ex.Message}");
            return null;
        }
    }

    private static IEnumerable<string> DistinctNames(params string?[] names)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in names)
        {
            if (string.IsNullOrWhiteSpace(n))
                continue;
            if (seen.Add(n))
                yield return n;
        }
    }

    /// <summary>
    /// Matches publish/output-style names like <c>net9.0-windows10.0.17763.0.7z</c>.
    /// </summary>
    private static bool IsTfmNamedArchive(string path)
    {
        var name = Path.GetFileName(path);
        var ext = Path.GetExtension(name);
        if (!ext.Equals(".7z", StringComparison.OrdinalIgnoreCase)
            && !ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            return false;

        var stem = Path.GetFileNameWithoutExtension(name);
        // net9.0-windows… or net9.0-windows10.0.17763.0
        return stem.StartsWith("net", StringComparison.OrdinalIgnoreCase)
               && stem.Contains("windows", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Fingerprint for a local debug archive: length + mtime + file name.
    /// </summary>
    public static string? GetLocalArchiveKey(string archivePath)
    {
        try
        {
            if (!File.Exists(archivePath))
                return null;
            var fi = new FileInfo(archivePath);
            return $"{fi.Length}:{fi.LastWriteTimeUtc.Ticks}:{fi.Name}";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Fingerprint for a local debug exe: length + mtime + ProductVersion.
    /// </summary>
    public static string? GetLocalExeKey(string exePath)
    {
        try
        {
            if (!File.Exists(exePath))
                return null;
            var fi = new FileInfo(exePath);
            var ver = AppVersion.ReadFromExe(exePath) ?? "";
            return $"{fi.Length}:{fi.LastWriteTimeUtc.Ticks}:{ver}";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// True when the archive file is newer than the running exe (rough freshness check).
    /// Older archives often cause a silent downgrade and wipe newer Debug features.
    /// </summary>
    public static bool IsLocalArchiveLikelyNewer(string archivePath)
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath) || !File.Exists(archivePath))
                return true;

            return File.GetLastWriteTimeUtc(archivePath) > File.GetLastWriteTimeUtc(exePath);
        }
        catch
        {
            return true;
        }
    }

    private static IEnumerable<string> EnumerateAppDirs()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string?> raw =
        [
            Environment.ProcessPath is { } p ? Path.GetDirectoryName(p) : null,
            AppContext.BaseDirectory,
            AppDomain.CurrentDomain.BaseDirectory
        ];

        foreach (var dir in raw)
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;

            string full;
            try { full = Path.GetFullPath(dir); }
            catch { continue; }

            if (!Directory.Exists(full) || !seen.Add(full))
                continue;

            yield return full;
        }
    }

    public static async Task<string> DownloadAsync(ReleaseInfo release, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var zipPath = Path.Combine(UpdatesDirectory, release.ZipName);
        AppLog.Default.Info("Update", $"Downloading {release.ZipUrl} -> {zipPath}");

        using var response = await Http.GetAsync(release.ZipUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;

        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(zipPath);
        var buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            readTotal += read;
            if (total is > 0)
                progress?.Report(readTotal / (double)total.Value);
        }

        AppLog.Default.Info("Update", $"Download complete ({readTotal} bytes)");
        return zipPath;
    }

    /// <summary>
    /// Starts a detached PowerShell helper that waits for this process to exit,
    /// extracts the zip over the install directory, optionally restarts the app.
    /// </summary>
    public static void ApplyAndExit(string zipPath, bool restart)
    {
        LaunchHelper(zipPath: zipPath, sourceDir: null, restart: restart, keepArchive: false);
    }

    /// <summary>
    /// Applies a local .zip/.7z sitting next to the exe (debug workflow). Leaves the archive in place.
    /// Pre-extracts with SharpCompress so .7z works; helper then copies over the install dir after exit.
    /// </summary>
    public static void ApplyLocalArchiveAndExit(string archivePath, bool restart = true)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException(LocalizationService.Text("update.errLocalArchive"), archivePath);

        AppLog.Default.Info("Update", $"Local archive apply: \"{archivePath}\" restart={restart}");
        var sourceDir = ExtractArchiveToTemp(archivePath);
        LaunchHelper(zipPath: null, sourceDir: sourceDir, restart: restart, keepArchive: true);
    }

    /// <summary>
    /// Applies files from a build-output directory (debug workflow). Copies to a temp folder first
    /// so the helper can delete the staging dir without touching the development tree.
    /// </summary>
    public static void ApplyFromBuildDirectoryAndExit(string buildDirectory, bool restart = true)
    {
        if (!Directory.Exists(buildDirectory))
            throw new DirectoryNotFoundException(buildDirectory);

        var exePath = Path.Combine(buildDirectory, "BackupSaves.exe");
        if (!File.Exists(exePath))
            throw new FileNotFoundException(LocalizationService.Text("update.errLocalArchive"), exePath);

        AppLog.Default.Info("Update", $"Local build-dir apply: \"{buildDirectory}\" restart={restart}");
        var staging = CopyBuildDirectoryToTemp(buildDirectory);
        LaunchHelper(zipPath: null, sourceDir: staging, restart: restart, keepArchive: true);
    }

    /// <summary>Applies a <see cref="LocalUpdateCandidate"/> (archive or build folder).</summary>
    public static void ApplyLocalCandidateAndExit(LocalUpdateCandidate candidate, bool restart = true)
    {
        if (!string.IsNullOrWhiteSpace(candidate.ArchivePath))
        {
            ApplyLocalArchiveAndExit(candidate.ArchivePath, restart);
            return;
        }

        if (!string.IsNullOrWhiteSpace(candidate.BuildDirectory))
        {
            ApplyFromBuildDirectoryAndExit(candidate.BuildDirectory, restart);
            return;
        }

        throw new InvalidOperationException("Local update candidate has no archive or build directory.");
    }

    private static string CopyBuildDirectoryToTemp(string buildDirectory)
    {
        var sourceRoot = buildDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var staging = Path.Combine(Path.GetTempPath(), "BackupSaves-frombuild-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(staging);
        AppLog.Default.Info("Update", $"Staging build dir \"{sourceRoot}\" -> \"{staging}\"");

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceRoot, file);
            if (ShouldSkipBuildFile(rel))
                continue;

            var dest = Path.Combine(staging, rel);
            var destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            File.Copy(file, dest, overwrite: true);
        }

        return staging;
    }

    private static bool ShouldSkipBuildFile(string relativePath)
    {
        var name = Path.GetFileName(relativePath);
        if (name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static string ExtractArchiveToTemp(string archivePath)
    {
        var extract = Path.Combine(Path.GetTempPath(), "BackupSaves-extract-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(extract);
        AppLog.Default.Info("Update", $"Extracting \"{archivePath}\" -> \"{extract}\"");

        using (var archive = ArchiveFactory.OpenArchive(archivePath))
        {
            foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
            {
                entry.WriteToDirectory(extract, new ExtractionOptions
                {
                    ExtractFullPath = true,
                    Overwrite = true
                });
            }
        }

        return extract;
    }

    private static void LaunchHelper(string? zipPath, string? sourceDir, bool restart, bool keepArchive)
    {
        if (string.IsNullOrWhiteSpace(zipPath) && string.IsNullOrWhiteSpace(sourceDir))
            throw new ArgumentException("Either zipPath or sourceDir is required.");

        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException(LocalizationService.Text("update.errExePath"));
        var targetDir = Path.GetDirectoryName(exePath)
            ?? throw new InvalidOperationException(LocalizationService.Text("update.errInstallDir"));

        // Ensure log folder exists before helper starts (helper also creates it).
        _ = LogsDirectory;

        var scriptPath = Path.Combine(UpdatesDirectory, "apply-update.ps1");
        // UTF-8 BOM: Windows PowerShell 5.1 otherwise mis-parses the script.
        File.WriteAllText(scriptPath, ApplyScript, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        // Detach via cmd START so the helper survives Application.Shutdown()
        // (direct Process.Start with UseShellExecute=false often dies with the parent).
        var psArgs =
            $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\" " +
            $"-TargetDir \"{targetDir}\" -ExePath \"{exePath}\" " +
            $"-PidToWait {Environment.ProcessId}";

        if (!string.IsNullOrWhiteSpace(zipPath))
            psArgs += $" -ZipPath \"{zipPath}\"";
        if (!string.IsNullOrWhiteSpace(sourceDir))
            psArgs += $" -SourceDir \"{sourceDir}\"";
        if (restart)
            psArgs += " -Restart";
        if (keepArchive)
            psArgs += " -KeepArchive";

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c start \"BackupSavesUpdater\" /b powershell.exe {psArgs}",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = UpdatesDirectory
        };

        AppLog.Default.Info("Update",
            $"Launching updater helper (restart={restart}, target=\"{targetDir}\")");
        var proc = Process.Start(psi)
            ?? throw new InvalidOperationException(LocalizationService.Text("update.errHelper"));
        AppLog.Default.Info("Update", $"Updater launcher started pid={proc.Id}");
    }

    // ASCII-only script body: safe for Windows PowerShell 5.1.
    private const string ApplyScript = """
param(
  [string] $ZipPath,
  [string] $SourceDir,
  [Parameter(Mandatory = $true)][string] $TargetDir,
  [Parameter(Mandatory = $true)][string] $ExePath,
  [Parameter(Mandatory = $true)][int] $PidToWait,
  [switch] $Restart,
  [switch] $KeepArchive
)
$ErrorActionPreference = 'Stop'
$logDir = Join-Path $env:LOCALAPPDATA 'BackupSaves\logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir 'update-apply.log'
function Write-Log([string] $m) {
  $line = '{0} {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $m
  Add-Content -LiteralPath $log -Value $line -Encoding UTF8
}
function Show-Fail([string] $m) {
  try {
    Add-Type -AssemblyName System.Windows.Forms -ErrorAction SilentlyContinue
    [System.Windows.Forms.MessageBox]::Show($m, 'BackupSaves update failed', 'OK', 'Error') | Out-Null
  } catch { }
}
try {
  Write-Log "Helper start. PID wait=$PidToWait target=$TargetDir zip=$ZipPath source=$SourceDir"
  $deadline = (Get-Date).AddMinutes(3)
  while ((Get-Date) -lt $deadline) {
    $proc = Get-Process -Id $PidToWait -ErrorAction SilentlyContinue
    if (-not $proc) { break }
    Start-Sleep -Milliseconds 400
  }
  Start-Sleep -Seconds 1
  if (-not (Test-Path -LiteralPath $TargetDir)) { throw "Target dir not found: $TargetDir" }

  $extract = $null
  $sourceRoot = $null
  if ($SourceDir -and (Test-Path -LiteralPath $SourceDir)) {
    $sourceRoot = $SourceDir
    Write-Log "Using pre-extracted source: $sourceRoot"
  }
  else {
    if (-not $ZipPath -or -not (Test-Path -LiteralPath $ZipPath)) { throw "Zip not found: $ZipPath" }
    $extract = Join-Path $env:TEMP ('BackupSaves-extract-' + [guid]::NewGuid().ToString('n'))
    New-Item -ItemType Directory -Force -Path $extract | Out-Null
    Write-Log "Extracting $ZipPath -> $extract"
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $extract -Force
    $sourceRoot = $extract
  }

  # If source contains a single root folder, use it as source root.
  $top = @(Get-ChildItem -LiteralPath $sourceRoot -Force)
  if ($top.Count -eq 1 -and $top[0].PSIsContainer) {
    $sourceRoot = $top[0].FullName
    Write-Log "Using nested root: $sourceRoot"
  }

  Write-Log "Copying into $TargetDir"
  Get-ChildItem -LiteralPath $sourceRoot -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($sourceRoot.Length).TrimStart('\')
    $dest = Join-Path $TargetDir $rel
    $parent = Split-Path $dest -Parent
    if (-not (Test-Path -LiteralPath $parent)) {
      New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    Copy-Item -LiteralPath $_.FullName -Destination $dest -Force
  }

  if ($extract) {
    Remove-Item -LiteralPath $extract -Recurse -Force -ErrorAction SilentlyContinue
  }
  elseif ($SourceDir -and (Test-Path -LiteralPath $SourceDir)) {
    Remove-Item -LiteralPath $SourceDir -Recurse -Force -ErrorAction SilentlyContinue
  }
  if ($ZipPath -and -not $KeepArchive) {
    Remove-Item -LiteralPath $ZipPath -Force -ErrorAction SilentlyContinue
  }
  Write-Log 'Update apply OK'

  if ($Restart) {
    Write-Log "Starting $ExePath"
    Start-Process -FilePath $ExePath
  }
}
catch {
  $msg = $_.Exception.Message
  Write-Log ("ERROR: " + $msg)
  Show-Fail ("BackupSaves update failed:`n" + $msg + "`n`nLog: " + $log)
}
""";
}
