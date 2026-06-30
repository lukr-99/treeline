<#
.SYNOPSIS
  Remove Treeline from this machine.
.DESCRIPTION
  Stops a running instance, removes the install directory, shortcuts and the agent skill.
  Local data (%APPDATA%\Treeline) is kept unless -PurgeData is given.
#>
[CmdletBinding()]
param(
  [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\Treeline'),
  [switch]$PurgeData
)

$ErrorActionPreference = 'SilentlyContinue'
function Step($m) { Write-Host "==> $m" -ForegroundColor Yellow }

Step "Stopping running Treeline"
Get-Process Treeline -ErrorAction SilentlyContinue | Stop-Process -Force

Step "Removing shortcuts"
Remove-Item (Join-Path ([Environment]::GetFolderPath('Programs')) 'Treeline.lnk') -Force
Remove-Item (Join-Path ([Environment]::GetFolderPath('Startup')) 'Treeline.lnk') -Force

Step "Removing install directory"
Remove-Item $InstallDir -Recurse -Force

Step "Removing agent skill"
Remove-Item (Join-Path $env:USERPROFILE '.claude\skills\treeline') -Recurse -Force

if ($PurgeData) {
  Step "Purging local data"
  Remove-Item (Join-Path $env:APPDATA 'Treeline') -Recurse -Force
} else {
  Write-Host "    Local data kept at $(Join-Path $env:APPDATA 'Treeline') (use -PurgeData to remove)" -ForegroundColor Cyan
}

Write-Host "==> Treeline removed." -ForegroundColor Yellow
