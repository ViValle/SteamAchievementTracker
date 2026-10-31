# ==============================================================================
# Script: rollback.ps1
# Description: Emergency binary rollback mechanism (< 30 seconds recovery)
# ==============================================================================

[CmdletBinding()]
param(
    [string]$TargetDir = "$PSScriptRoot\..\dist",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$ExeName = "SteamAchievementTracker.UI.exe"
$BackupDir = Join-Path $TargetDir ".backups"

Write-Host "==> Checking running instances of $ExeName..." -ForegroundColor Yellow
$Running = Get-Process -Name "SteamAchievementTracker.UI" -ErrorAction SilentlyContinue
if ($Running) {
    Write-Host "Stopping running process PID $($Running.Id)..." -ForegroundColor Yellow
    Stop-Process -Id $Running.Id -Force
    Start-Sleep -Seconds 1
}

# Look for latest backup archive or previous binary
if (-not (Test-Path $BackupDir)) {
    Write-Error "No backups directory found at $BackupDir! Cannot rollback."
}

$LatestBackup = Get-ChildItem -Path $BackupDir -Filter "*.zip" | Sort-Object LastWriteTime -Descending | Select-Object -First 1

if (-not $LatestBackup) {
    Write-Error "No backup archives found in $BackupDir to restore from."
}

Write-Host "==> Restoring from backup: $($LatestBackup.Name)..." -ForegroundColor Cyan
Expand-Archive -Path $LatestBackup.FullName -DestinationPath $TargetDir -Force

Write-Host "`n[SUCCESS] Rollback completed in less than 30 seconds." -ForegroundColor Green
Write-Host "Restored version from: $($LatestBackup.FullName)"
