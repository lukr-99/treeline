<#
.SYNOPSIS
  Build, install and register Treeline on this machine.

.DESCRIPTION
  Publishes Treeline, copies it to a per-user install directory, creates Start Menu
  and (optionally) Startup shortcuts so the tray app is always available, and installs
  the Claude agent skill. By default it produces a self-contained build that needs no
  .NET runtime on the target machine.

.EXAMPLE
  ./install.ps1
  ./install.ps1 -FrameworkDependent -Port 9000 -NoStartup
#>
[CmdletBinding()]
param(
  [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\Treeline'),
  [int]$Port = 8787,
  [switch]$FrameworkDependent,
  [switch]$NoStartup,
  [switch]$NoShortcuts,
  [switch]$NoSkill,
  [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $repo 'src\Treeline.App\Treeline.App.csproj'

function Step($m) { Write-Host "==> $m" -ForegroundColor Green }

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw "The .NET SDK ('dotnet') was not found on PATH. Install .NET 10 SDK and retry."
}

# --- publish ---
$stage = Join-Path $env:TEMP ("treeline-publish-" + [guid]::NewGuid().ToString('N'))
Step "Publishing Treeline ($([bool]$FrameworkDependent ? 'framework-dependent' : 'self-contained')) ..."
$args = @('publish', $proj, '-c', 'Release', '-r', 'win-x64', '-o', $stage, '-p:PublishSingleFile=true')
if ($FrameworkDependent) { $args += '--self-contained'; $args += 'false' }
else { $args += '--self-contained'; $args += 'true' }
& dotnet @args | Out-Null
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

# --- copy to install dir ---
Step "Installing to $InstallDir"
if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item (Join-Path $stage '*') $InstallDir -Recurse -Force
Remove-Item $stage -Recurse -Force
$exe = Join-Path $InstallDir 'Treeline.exe'

# --- persist chosen port ---
if ($Port -ne 8787) {
  $cfgDir = Join-Path $env:APPDATA 'Treeline'
  New-Item -ItemType Directory -Force -Path $cfgDir | Out-Null
  $cfg = Join-Path $cfgDir 'config.json'
  $obj = if (Test-Path $cfg) { Get-Content $cfg -Raw | ConvertFrom-Json } else { [pscustomobject]@{} }
  $obj | Add-Member -NotePropertyName 'port' -NotePropertyValue "$Port" -Force
  $obj | ConvertTo-Json | Set-Content $cfg -Encoding utf8
}

# --- shortcuts ---
function New-Shortcut($linkPath, $target, $iconPath) {
  $sh = New-Object -ComObject WScript.Shell
  $sc = $sh.CreateShortcut($linkPath)
  $sc.TargetPath = $target
  $sc.IconLocation = $iconPath
  $sc.WorkingDirectory = (Split-Path $target -Parent)
  $sc.Description = 'Treeline - git worktree dashboard'
  $sc.Save()
}

if (-not $NoShortcuts) {
  Step "Creating Start Menu shortcut"
  $programs = [Environment]::GetFolderPath('Programs')
  New-Shortcut (Join-Path $programs 'Treeline.lnk') $exe $exe
}

if (-not $NoStartup) {
  Step "Registering auto-start (tray on login)"
  $startup = [Environment]::GetFolderPath('Startup')
  New-Shortcut (Join-Path $startup 'Treeline.lnk') $exe $exe
}

# --- agent skill ---
if (-not $NoSkill) {
  $skillSrc = Join-Path $repo 'skills\treeline'
  if (Test-Path $skillSrc) {
    $skillDst = Join-Path $env:USERPROFILE '.claude\skills\treeline'
    Step "Installing Claude agent skill to $skillDst"
    New-Item -ItemType Directory -Force -Path $skillDst | Out-Null
    Copy-Item (Join-Path $skillSrc '*') $skillDst -Recurse -Force
  }
}

Step "Done. Installed Treeline to $exe"
Write-Host "    Dashboard: http://127.0.0.1:$Port" -ForegroundColor Cyan

if (-not $NoLaunch) {
  Step "Launching Treeline"
  Start-Process $exe
}
