<#
.SYNOPSIS
  Builds a Release package (zip) for BackupSaves. Does NOT upload to GitHub.

.DESCRIPTION
  1) dotnet publish -c Release (bumps Version.props patch)
  2) Packs output into releases/BackupSaves-<version>.zip
  3) Writes releases/BackupSaves-<version>.txt with checksum + hints

.PARAMETER NoVersionIncrement
  Pass -p:NoVersionIncrement=true (reuse current Version.props number).

.PARAMETER SkipBuild
  Do not publish; zip an existing publish folder (see -PublishDir).

.PARAMETER PublishDir
  Folder with published binaries. Default: artifacts/publish

.PARAMETER CreateLocalTag
  After packaging, create local git tag v<version> (not pushed).

.PARAMETER UploadToReleasesRepo
  Upload zip to public repo digitallez/BackupSaves-Releases via `gh release create`.
  Requires GitHub CLI (`gh`) authenticated.

.EXAMPLE
  .\publish-release.ps1 -UploadToReleasesRepo
#>
[CmdletBinding()]
param(
    [switch] $NoVersionIncrement,
    [switch] $SkipBuild,
    [string] $PublishDir = "",
    [switch] $CreateLocalTag,
    [switch] $UploadToReleasesRepo,
    [string] $ReleasesRepo = "digitallez/BackupSaves-Releases"
)

$ErrorActionPreference = "Stop"

$RepoRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Get-Location | Select-Object -ExpandProperty Path
}

$SlnDir = Join-Path $RepoRoot "WpfApp_BackupSaves"
$Csproj = Join-Path $SlnDir "WpfApp_BackupSaves\WpfApp_BackupSaves.csproj"
$VersionProps = Join-Path $SlnDir "WpfApp_BackupSaves\Version.props"
$ReleasesDir = Join-Path $RepoRoot "releases"

if ([string]::IsNullOrWhiteSpace($PublishDir)) {
    $PublishDir = Join-Path $RepoRoot "artifacts\publish"
}

function Get-AppVersionFromProps {
    param([string] $Path)
    [xml] $xml = Get-Content -LiteralPath $Path -Raw
    $major = $xml.Project.PropertyGroup.VersionMajor
    $minor = $xml.Project.PropertyGroup.VersionMinor
    $patch = $xml.Project.PropertyGroup.VersionPatch
    return "$major.$minor.$patch"
}

function Assert-Path {
    param([string] $Path, [string] $Label)
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Label not found: $Path"
    }
}

Assert-Path $Csproj "Project"
Assert-Path $VersionProps "Version.props"

New-Item -ItemType Directory -Force -Path $ReleasesDir | Out-Null

if (-not $SkipBuild) {
    Write-Host "==> Publishing Release..." -ForegroundColor Cyan
    $msbuildProps = @()
    if ($NoVersionIncrement) {
        $msbuildProps += "-p:NoVersionIncrement=true"
        Write-Host "    (NoVersionIncrement=true)" -ForegroundColor DarkYellow
    }

    # Fresh publish folder
    if (Test-Path -LiteralPath $PublishDir) {
        Remove-Item -LiteralPath $PublishDir -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null

    & dotnet publish $Csproj `
        -c Release `
        -o $PublishDir `
        --nologo `
        @msbuildProps

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }
}
else {
    Assert-Path $PublishDir "PublishDir"
    Write-Host "==> SkipBuild: using $PublishDir" -ForegroundColor DarkYellow
}

$version = Get-AppVersionFromProps -Path $VersionProps
$exe = Join-Path $PublishDir "BackupSaves.exe"
Assert-Path $exe "BackupSaves.exe"

# Prefer informational version from the built exe if available
try {
    $fvi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
    if ($fvi.ProductVersion -and $fvi.ProductVersion -notmatch "debug") {
        # ProductVersion may be "1.0.1" or with metadata
        $pv = ($fvi.ProductVersion -split "[+\s]")[0]
        if ($pv -match '^\d+\.\d+\.\d+$') {
            $version = $pv
        }
    }
}
catch {
    # keep props version
}

$zipName = "BackupSaves-$version.zip"
$zipPath = Join-Path $ReleasesDir $zipName
$infoPath = Join-Path $ReleasesDir "BackupSaves-$version.txt"

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

# Stage without PDBs / XML docs
$stageDir = Join-Path $RepoRoot "artifacts\stage-$version"
if (Test-Path -LiteralPath $stageDir) {
    Remove-Item -LiteralPath $stageDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $stageDir | Out-Null

Get-ChildItem -LiteralPath $PublishDir -Recurse -File |
    Where-Object {
        $_.Extension -notin @(".pdb", ".xml") -and
        $_.Name -notmatch '\.dev\.json$'
    } |
    ForEach-Object {
        $rel = $_.FullName.Substring($PublishDir.Length).TrimStart("\", "/")
        $dest = Join-Path $stageDir $rel
        $destParent = Split-Path $dest -Parent
        if (-not (Test-Path -LiteralPath $destParent)) {
            New-Item -ItemType Directory -Force -Path $destParent | Out-Null
        }
        Copy-Item -LiteralPath $_.FullName -Destination $dest -Force
    }

Write-Host "==> Packing $zipName ..." -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $stageDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
$sizeMb = [math]::Round((Get-Item -LiteralPath $zipPath).Length / 1MB, 2)

$notes = @"
BackupSaves $version
Built: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
Zip:   $zipName
SHA256: $hash
Size:  $sizeMb MB

Contents: framework-dependent publish (.NET 9 Windows Desktop Runtime required).
Auto-update source: https://github.com/digitallez/BackupSaves-Releases/releases

--- Upload to public releases repo ---
  .\publish-release.ps1 -SkipBuild -NoVersionIncrement -UploadToReleasesRepo
  # or after this build:
  gh release create v$version `"$zipPath`" --repo digitallez/BackupSaves-Releases --title "BackupSaves $version" --notes "Release $version"

--- Source repo (private) ---
1. git add WpfApp_BackupSaves/WpfApp_BackupSaves/Version.props
2. git commit -m "Release v$version"
3. git push origin main
"@

Set-Content -LiteralPath $infoPath -Value $notes -Encoding UTF8

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  Version : $version"
Write-Host "  Zip     : $zipPath"
Write-Host "  SHA256  : $hash"
Write-Host "  Notes   : $infoPath"

if ($CreateLocalTag) {
    $tag = "v$version"
    $existing = git -C $RepoRoot tag -l $tag
    if ($existing) {
        Write-Host "  Tag $tag already exists locally (skipped)." -ForegroundColor DarkYellow
    }
    else {
        git -C $RepoRoot tag $tag
        Write-Host "  Local tag created: $tag (not pushed)" -ForegroundColor Green
    }
}

if ($UploadToReleasesRepo) {
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if (-not $gh) {
        throw "GitHub CLI (gh) not found in PATH. Install from https://cli.github.com/ or upload zip manually."
    }

    $tag = "v$version"
    Write-Host "==> Uploading to $ReleasesRepo ($tag) ..." -ForegroundColor Cyan

    $existingRelease = & gh release view $tag --repo $ReleasesRepo 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  Release $tag exists — uploading asset (clobber)..." -ForegroundColor DarkYellow
        & gh release upload $tag $zipPath --repo $ReleasesRepo --clobber
        if ($LASTEXITCODE -ne 0) { throw "gh release upload failed" }
    }
    else {
        & gh release create $tag $zipPath `
            --repo $ReleasesRepo `
            --title "BackupSaves $version" `
            --notes "BackupSaves $version`nSHA256: $hash"
        if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }
    }

    Write-Host "  Published: https://github.com/$ReleasesRepo/releases/tag/$tag" -ForegroundColor Green
}
else {
    Write-Host ""
    Write-Host "GitHub upload skipped (pass -UploadToReleasesRepo to publish)." -ForegroundColor DarkGray
}
