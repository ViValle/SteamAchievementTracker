# ==============================================================================
# Script: build-release.ps1
# Description: Automated local binary build, package & checksum generation
# Target: Windows x64 Single-File Standalone Executable (.NET 8 WPF)
# ==============================================================================

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir
)

$ErrorActionPreference = "Stop"

$SolutionRoot = (Resolve-Path "$PSScriptRoot\..").Path
if (-not $OutputDir) {
    $OutputDir = Join-Path $SolutionRoot "dist"
}
$DistDir = [System.IO.Path]::GetFullPath($OutputDir)

# Ensure dotnet SDK in environment if custom path exists
if (Test-Path "C:\Users\Valle\dotnet_sdk\dotnet.exe") {
    $env:DOTNET_ROOT = "C:\Users\Valle\dotnet_sdk"
    $env:PATH = "C:\Users\Valle\dotnet_sdk;$env:PATH"
}

Write-Host "==> Checking .NET SDK version..." -ForegroundColor Cyan
dotnet --version

$ProjectDir = Join-Path $SolutionRoot "src\SteamAchievementTracker.UI"
$ProjectFile = Join-Path $ProjectDir "SteamAchievementTracker.UI.csproj"

Write-Host "==> Running Test Suite before build..." -ForegroundColor Cyan
dotnet test (Join-Path $SolutionRoot "tests\SteamAchievementTracker.Tests\SteamAchievementTracker.Tests.csproj") -c $Configuration --no-restore

Write-Host "==> Publishing Self-Contained Single-File Binary..." -ForegroundColor Cyan
$TempPublishDir = Join-Path $DistDir "temp_win64"
if (Test-Path $TempPublishDir) { Remove-Item $TempPublishDir -Recurse -Force }
New-Item -ItemType Directory -Path $TempPublishDir -Force | Out-Null

dotnet publish $ProjectFile `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $TempPublishDir

# Version extraction from Directory.Build.props
[xml]$props = Get-Content (Join-Path $SolutionRoot "Directory.Build.props")
$Version = $props.Project.PropertyGroup.Version
if (-not $Version) { $Version = "0.1.0" }

$ZipName = "SteamAchievementTracker-v$Version-$Runtime.zip"
$ZipPath = Join-Path $DistDir $ZipName

if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }

Write-Host "==> Packaging into $ZipName..." -ForegroundColor Cyan
Compress-Archive -Path "$TempPublishDir\*" -DestinationPath $ZipPath -Force

Write-Host "==> Generating SHA256 checksum..." -ForegroundColor Cyan
$Hash = (Get-FileHash -Path $ZipPath -Algorithm SHA256).Hash.ToLower()
$ShaLine = "$Hash  $ZipName"
$ChecksumFile = Join-Path $DistDir "SHA256SUMS.txt"
Set-Content -Path $ChecksumFile -Value $ShaLine

# Cleanup temporary folder
Remove-Item $TempPublishDir -Recurse -Force

Write-Host "`n[SUCCESS] Binary Release built successfully!" -ForegroundColor Green
Write-Host "Package: $ZipPath"
Write-Host "SHA256:  $Hash"
Write-Host "Manifest: $ChecksumFile"
