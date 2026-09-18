<#
.SYNOPSIS
  Pack already-built Release output and upload to GitHub Releases.
  Does NOT build or change Version.props.

.DESCRIPTION
  1) Finds BackupSaves.exe under bin\Release\net9.0-windows*
  2) Reads version from the exe ProductVersion
  3) Zips contents (no .pdb / .xml) -> releases\BackupSaves-<ver>.zip
  4) Creates/updates release on digitallez/BackupSaves-Releases via gh

.EXAMPLE
  .\upload-release.ps1
  .\upload-release.bat
#>
[CmdletBinding()]
param(
    [string] $BinDir = "",
    [string] $ReleasesRepo = "digitallez/BackupSaves-Releases",
    [switch] $NoUpload
)

$ErrorActionPreference = "Stop"

$RepoRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Get-Location).Path
}

$ReleasesDir = Join-Path $RepoRoot "releases"
$DefaultBinRoot = Join-Path $RepoRoot "WpfApp_BackupSaves\WpfApp_BackupSaves\bin\Release"

function Assert-Path {
    param([string] $Path, [string] $Label)
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Label not found: $Path"
    }
}

function Resolve-ReleaseBinDir {
    param([string] $Hint)

    if (-not [string]::IsNullOrWhiteSpace($Hint)) {
        Assert-Path $Hint "BinDir"
        $exe = Join-Path $Hint "BackupSaves.exe"
        Assert-Path $exe "BackupSaves.exe"
        return (Resolve-Path -LiteralPath $Hint).Path
    }

    Assert-Path $DefaultBinRoot "Release bin root"

    $candidates = Get-ChildItem -LiteralPath $DefaultBinRoot -Directory -Filter "net9.0-windows*" -ErrorAction SilentlyContinue |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName "BackupSaves.exe") } |
        Sort-Object LastWriteTime -Descending

    if (-not $candidates -or $candidates.Count -eq 0) {
        $exeHit = Get-ChildItem -LiteralPath $DefaultBinRoot -Recurse -Filter "BackupSaves.exe" -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
        if (-not $exeHit) {
            throw "BackupSaves.exe not found under $DefaultBinRoot. Build Release in Visual Studio first."
        }
        return $exeHit.Directory.FullName
    }

    return $candidates[0].FullName
}

function Get-ExeVersion {
    param([string] $ExePath)
    $fvi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($ExePath)
    $raw = $fvi.ProductVersion
    if ([string]::IsNullOrWhiteSpace($raw)) {
        $raw = $fvi.FileVersion
    }
    if ([string]::IsNullOrWhiteSpace($raw)) {
        throw "Cannot read version from $ExePath"
    }
    if ($raw -match 'debug') {
        throw "Refusing to upload a Debug build (ProductVersion='$raw'). Rebuild as Release."
    }
    $pv = ($raw -split '[+\s]')[0]
    if ($pv -notmatch '^\d+\.\d+\.\d+') {
        throw "Unexpected ProductVersion '$raw' (want x.y.z)."
    }
    $parts = $pv.Split('.')
    return "$($parts[0]).$($parts[1]).$($parts[2])"
}

function Resolve-GhExe {
    # Fresh install often updates Machine PATH, but Explorer/double-click still has old PATH.
    $machinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    if ($machinePath -or $userPath) {
        $env:Path = "$machinePath;$userPath"
    }

    $cmd = Get-Command gh -ErrorAction SilentlyContinue
    if ($cmd -and $cmd.Source) {
        return $cmd.Source
    }

    $candidates = @(
        (Join-Path $env:ProgramFiles "GitHub CLI\gh.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "GitHub CLI\gh.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\GitHub CLI\gh.exe"),
        (Join-Path $env:LOCALAPPDATA "GitHub CLI\gh.exe")
    )

    foreach ($c in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($c) -and (Test-Path -LiteralPath $c)) {
            return $c
        }
    }

    return $null
}

function Write-GhAuthHelp {
    param(
        [string] $Reason,
        [string] $ZipPathReady = ""
    )

    $helpPath = Join-Path $PSScriptRoot "upload-release.help.txt"
    if (Test-Path -LiteralPath $helpPath) {
        $text = [System.IO.File]::ReadAllText($helpPath, [System.Text.UTF8Encoding]::new($true))
        $text = $text.Replace("{REASON}", $Reason).Replace("{ZIP}", $ZipPathReady)
        Write-Host ""
        Write-Host $text -ForegroundColor Yellow
        Write-Host ""
        return
    }

    Write-Host ""
    Write-Host "FAILED: cannot publish release to GitHub" -ForegroundColor Yellow
    Write-Host "Reason: $Reason" -ForegroundColor Red
    Write-Host "Fix: open cmd and run:  gh auth login" -ForegroundColor Cyan
    if (-not [string]::IsNullOrWhiteSpace($ZipPathReady)) {
        Write-Host "Zip ready: $ZipPathReady" -ForegroundColor Green
    }
    Write-Host ""
}

function Test-GhAuthErrorText {
    param([string] $Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $false }
    return $Text -match '(?i)auth|login|401|403|credential|not logged|to re-authenticate|GH_TOKEN|permission denied|forbidden'
}

$binDir = Resolve-ReleaseBinDir -Hint $BinDir
$exe = Join-Path $binDir "BackupSaves.exe"
$version = Get-ExeVersion -ExePath $exe

Write-Host "==> Source : $binDir" -ForegroundColor Cyan
Write-Host "==> Version: $version (from BackupSaves.exe)" -ForegroundColor Cyan

New-Item -ItemType Directory -Force -Path $ReleasesDir | Out-Null

$zipName = "BackupSaves-$version.zip"
$zipPath = Join-Path $ReleasesDir $zipName
$infoPath = Join-Path $ReleasesDir "BackupSaves-$version.txt"

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

$stageDir = Join-Path $RepoRoot "artifacts\upload-stage-$version"
if (Test-Path -LiteralPath $stageDir) {
    Remove-Item -LiteralPath $stageDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $stageDir | Out-Null

Get-ChildItem -LiteralPath $binDir -Recurse -File |
    Where-Object {
        $_.Extension -notin @(".pdb", ".xml") -and
        $_.Name -notmatch '\.dev\.json$' -and
        $_.FullName -notmatch '[\\/]ref[\\/]'
    } |
    ForEach-Object {
        $rel = $_.FullName.Substring($binDir.Length).TrimStart("\", "/")
        $dest = Join-Path $stageDir $rel
        $destParent = Split-Path $dest -Parent
        if (-not (Test-Path -LiteralPath $destParent)) {
            New-Item -ItemType Directory -Force -Path $destParent | Out-Null
        }
        Copy-Item -LiteralPath $_.FullName -Destination $dest -Force
    }

Assert-Path (Join-Path $stageDir "BackupSaves.exe") "Staged BackupSaves.exe"

Write-Host "==> Packing $zipName ..." -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $stageDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
$sizeMb = [math]::Round((Get-Item -LiteralPath $zipPath).Length / 1MB, 2)

$notes = @"
BackupSaves $version
Packed from bin\Release (no rebuild): $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
Source: $binDir
Zip:    $zipName
SHA256: $hash
Size:   $sizeMb MB

Framework-dependent (.NET 9 Windows Desktop Runtime required).
"@

Set-Content -LiteralPath $infoPath -Value $notes -Encoding UTF8

Write-Host ""
Write-Host "Zip ready." -ForegroundColor Green
Write-Host "  Zip    : $zipPath"
Write-Host "  SHA256 : $hash"
Write-Host "  Notes  : $infoPath"

if ($NoUpload) {
    Write-Host "Upload skipped (-NoUpload)." -ForegroundColor DarkYellow
    exit 0
}

$GhExe = Resolve-GhExe
if (-not $GhExe) {
    Write-GhAuthHelp -Reason "GitHub CLI (gh) is not installed or not found in PATH." -ZipPathReady $zipPath
    exit 2
}

Write-Host "==> Using gh: $GhExe" -ForegroundColor DarkGray
Write-Host "==> Checking GitHub login (gh auth status)..." -ForegroundColor Cyan
$authOut = & $GhExe auth status 2>&1 | Out-String
$authCode = $LASTEXITCODE
if ($authCode -ne 0) {
    Write-GhAuthHelp -Reason "Not logged in to GitHub CLI. Run: gh auth login" -ZipPathReady $zipPath
    if (-not [string]::IsNullOrWhiteSpace($authOut)) {
        Write-Host "gh output:" -ForegroundColor DarkGray
        Write-Host $authOut.TrimEnd() -ForegroundColor DarkGray
    }
    exit 2
}

$tag = "v$version"
Write-Host "==> Uploading to $ReleasesRepo ($tag) ..." -ForegroundColor Cyan

function Ensure-ReleasesRepoHasCommit {
    param([string] $Repo, [string] $Gh)

    Write-Host "==> Releases repo looks empty - creating initial README commit..." -ForegroundColor DarkYellow
    $readme = "# BackupSaves Releases`n`nPublic binaries for BackupSaves auto-update.`nDo not put source code here.`n"
    $b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($readme))

    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $out = & $Gh api --method PUT "repos/$Repo/contents/README.md" `
        -f message="Initial commit" `
        -f content=$b64 2>&1 | Out-String
    $code = $LASTEXITCODE
    $ErrorActionPreference = $prevEap

    if ($code -ne 0) {
        Write-Host $out.TrimEnd() -ForegroundColor Red
        throw "Cannot seed empty repo $Repo (need at least one commit before releases)."
    }

    Write-Host "  Initial commit created on $Repo" -ForegroundColor Green
}

function Invoke-GhUpload {
    param([scriptblock] $Action, [string] $FailLabel, [switch] $AllowEmptyRepoRetry)

    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $output = & $Action 2>&1 | Out-String
    $code = $LASTEXITCODE
    $ErrorActionPreference = $prevEap

    if ($code -eq 0) {
        if (-not [string]::IsNullOrWhiteSpace($output)) {
            Write-Host $output.TrimEnd() -ForegroundColor DarkGray
        }
        return
    }

    if ($AllowEmptyRepoRetry -and ($output -match '(?i)Repository is empty|422')) {
        Ensure-ReleasesRepoHasCommit -Repo $ReleasesRepo -Gh $GhExe
        $ErrorActionPreference = "Continue"
        $output = & $Action 2>&1 | Out-String
        $code = $LASTEXITCODE
        $ErrorActionPreference = $prevEap
        if ($code -eq 0) {
            if (-not [string]::IsNullOrWhiteSpace($output)) {
                Write-Host $output.TrimEnd() -ForegroundColor DarkGray
            }
            return
        }
    }

    if (Test-GhAuthErrorText $output) {
        Write-GhAuthHelp -Reason "GitHub rejected the request. Authentication required: gh auth login" -ZipPathReady $zipPath
        if (-not [string]::IsNullOrWhiteSpace($output)) {
            Write-Host "gh output:" -ForegroundColor DarkGray
            Write-Host $output.TrimEnd() -ForegroundColor DarkGray
        }
        exit 2
    }

    Write-Host ""
    Write-Host "Error: $FailLabel (exit code $code)" -ForegroundColor Red
    if (-not [string]::IsNullOrWhiteSpace($output)) {
        Write-Host $output.TrimEnd() -ForegroundColor Red
    }
    Write-Host "Zip kept locally: $zipPath" -ForegroundColor Yellow
    exit $code
}

$releaseExists = $false
try {
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    & $GhExe release view $tag --repo $ReleasesRepo 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { $releaseExists = $true }
}
finally {
    $ErrorActionPreference = $prevEap
}

if ($releaseExists) {
    Write-Host "  Release $tag exists - replacing asset..." -ForegroundColor DarkYellow
    Invoke-GhUpload -FailLabel "gh release upload failed" -Action {
        & $GhExe release upload $tag $zipPath --repo $ReleasesRepo --clobber
    }
}
else {
    Invoke-GhUpload -FailLabel "gh release create failed" -AllowEmptyRepoRetry -Action {
        & $GhExe release create $tag $zipPath `
            --repo $ReleasesRepo `
            --title "BackupSaves $version" `
            --notes "BackupSaves $version`nSHA256: $hash`nPacked from verified bin\Release (no rebuild)."
    }
}

Write-Host "  Published: https://github.com/$ReleasesRepo/releases/tag/$tag" -ForegroundColor Green
