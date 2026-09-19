using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using BackupSaves.Core.Services;

namespace WpfApp_BackupSaves.Services;

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
            $"-ZipPath \"{zipPath}\" -TargetDir \"{targetDir}\" -ExePath \"{exePath}\" " +
            $"-PidToWait {Environment.ProcessId}" +
            (restart ? " -Restart" : "");

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
  [Parameter(Mandatory = $true)][string] $ZipPath,
  [Parameter(Mandatory = $true)][string] $TargetDir,
  [Parameter(Mandatory = $true)][string] $ExePath,
  [Parameter(Mandatory = $true)][int] $PidToWait,
  [switch] $Restart
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
  Write-Log "Helper start. PID wait=$PidToWait target=$TargetDir zip=$ZipPath"
  $deadline = (Get-Date).AddMinutes(3)
  while ((Get-Date) -lt $deadline) {
    $proc = Get-Process -Id $PidToWait -ErrorAction SilentlyContinue
    if (-not $proc) { break }
    Start-Sleep -Milliseconds 400
  }
  Start-Sleep -Seconds 1
  if (-not (Test-Path -LiteralPath $ZipPath)) { throw "Zip not found: $ZipPath" }
  if (-not (Test-Path -LiteralPath $TargetDir)) { throw "Target dir not found: $TargetDir" }

  $extract = Join-Path $env:TEMP ('BackupSaves-extract-' + [guid]::NewGuid().ToString('n'))
  New-Item -ItemType Directory -Force -Path $extract | Out-Null
  Write-Log "Extracting $ZipPath -> $extract"
  Expand-Archive -LiteralPath $ZipPath -DestinationPath $extract -Force

  # If zip contains a single root folder, use it as source root.
  $sourceRoot = $extract
  $top = @(Get-ChildItem -LiteralPath $extract -Force)
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

  Remove-Item -LiteralPath $extract -Recurse -Force -ErrorAction SilentlyContinue
  Remove-Item -LiteralPath $ZipPath -Force -ErrorAction SilentlyContinue
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
