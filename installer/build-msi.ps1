#requires -Version 5.1
<#
.SYNOPSIS
    Builds the classic (unpackaged) barcodrod.io MSI installer.

.DESCRIPTION
    Produces a standalone, self-contained MSI that installs barcodrod.io into Program Files
    with a Start Menu shortcut and ARP/uninstall entry. The app runs fully unpackaged
    (no MSIX, no Microsoft Store, no Store license) which makes it suitable for offline and
    locked-down environments (see issue #25).

    The build is intentionally split into three reproducible steps so it works the same
    locally and in CI (GitHub-hosted runners), with no Visual Studio required:

      1. Packaged build -> generates the app resource index (resources.pri) that WinUI needs.
         Symbol packaging is disabled because mspdbcmf.exe ships only with full Visual Studio.
      2. Unpackaged self-contained publish -> produces the runtime payload (exe + all DLLs).
         MSIX tooling is disabled so no AppxManifest/MakeAppx step runs.
      3. The resources.pri from (1) is copied next to the published exe, then WiX packages
         everything into an MSI.

.PARAMETER Version
    Four-part product version embedded in the MSI (e.g. 2.0.0.0).

.PARAMETER Architecture
    Target architecture: x64 (default) or arm64.

.PARAMETER Configuration
    Build configuration. Defaults to Release.
#>
[CmdletBinding()]
param(
    [string]$Version = "2.0.0.0",
    [ValidateSet("x64", "arm64")]
    [string]$Architecture = "x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot   = Split-Path -Parent $PSScriptRoot
$project    = Join-Path $repoRoot "barcodrod.io\barcodrod.io.csproj"
$rid        = "win-$Architecture"
$tfm        = "net9.0-windows10.0.22000"
$publishDir = Join-Path $repoRoot "artifacts\publish\$Architecture"
$msiDir     = Join-Path $repoRoot "artifacts\msi"
$msiPath    = Join-Path $msiDir "barcodrod.io-$Version-$Architecture.msi"
$wix        = Join-Path $env:USERPROFILE ".dotnet\tools\wix.exe"

Write-Host "==> [1/4] Building packaged layout to generate resources.pri + processed Assets" -ForegroundColor Cyan
dotnet build $project -c $Configuration -p:Platform=$Architecture `
    -p:AppxBundlePlatforms=$Architecture `
    -p:AppxPackageSigningEnabled=false `
    -p:AppxSymbolPackageEnabled=false `
    -p:AppxBundle=Never
if ($LASTEXITCODE -ne 0) { throw "Packaged build failed (exit $LASTEXITCODE)." }

# The packaged build emits resources.pri under an "AppX" layout for x64; for arm64 it sits at the
# runtime-identifier root. Resolve both locations robustly.
$ridRoot = Join-Path $repoRoot "barcodrod.io\bin\$Architecture\$Configuration\$tfm\$rid"
$priFound = @(
    (Join-Path $ridRoot "AppX\resources.pri"),
    (Join-Path $ridRoot "resources.pri")
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $priFound) { throw "resources.pri not found under $ridRoot" }

# Stage resources.pri out of the build tree immediately: the unpackaged publish in step 2 shares the
# same bin tree and can clobber a resources.pri that sits at the runtime-identifier root (arm64).
New-Item -ItemType Directory -Force $msiDir | Out-Null
$priSource = Join-Path $msiDir "resources-$Architecture.pri"
Copy-Item $priFound $priSource -Force

# The app loads its window icon/logo from \Assets at runtime. Use the committed source Assets
# folder (architecture-independent and always present) rather than the packaged tile assets, which
# are only produced for the bundle platform and are otherwise Store-tile-only.
$assetsSource = Join-Path $repoRoot "barcodrod.io\Assets"
if (-not (Test-Path (Join-Path $assetsSource "WindowIcon.ico"))) { throw "Source Assets not found at $assetsSource" }

Write-Host "==> [2/4] Publishing unpackaged, self-contained runtime ($rid)" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
dotnet publish $project -c $Configuration -r $rid --self-contained true -p:Platform=$Architecture `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -p:EnableMsixTooling=false `
    -p:AppxPackage=false `
    -p:AppxBundle=Never `
    -p:FileVersion=$Version `
    -p:InformationalVersion=$Version `
    -p:IncludeSourceRevisionInInformationalVersion=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "Unpackaged publish failed (exit $LASTEXITCODE)." }

Write-Host "==> [3/4] Staging resources.pri and Assets into the publish output" -ForegroundColor Cyan
Copy-Item $priSource (Join-Path $publishDir "resources.pri") -Force
# The app loads its window icon and logos from \Assets at runtime, but an unpackaged publish does not
# emit them. Stage the committed source Assets so the installed app shows its icons (1:1 with MSIX).
$destAssets = Join-Path $publishDir "Assets"
New-Item -ItemType Directory -Force $destAssets | Out-Null
Copy-Item (Join-Path $assetsSource "*") $destAssets -Recurse -Force

Write-Host "==> [4/4] Building MSI with WiX" -ForegroundColor Cyan
if (-not (Test-Path $wix)) { throw "WiX not found at $wix. Install with: dotnet tool install --global wix --version 5.0.2" }
New-Item -ItemType Directory -Force -Path $msiDir | Out-Null
& $wix build (Join-Path $PSScriptRoot "barcodrod.io.wxs") `
    -arch $Architecture `
    -d "Version=$Version" `
    -d "PublishDir=$publishDir" `
    -o $msiPath
if ($LASTEXITCODE -ne 0) { throw "WiX build failed (exit $LASTEXITCODE)." }

Write-Host ""
Write-Host "MSI created: $msiPath" -ForegroundColor Green
Write-Host ("Size: {0:N1} MB" -f ((Get-Item $msiPath).Length / 1MB))
