# Releasing & the in-app updater

How Treeline is built into an installer and how the in-app updater ships new versions.

## How the in-app updater works

The tray menu → **Check for updates** ([`TrayApplicationContext`](../src/Treeline.App/Tray/TrayApplicationContext.cs))
uses the shared `DotNetLib.Core.Updating` service (from the local NuGet feed — see `nuget.config`):

1. Queries `https://api.github.com/repos/lukr-99/Treeline/releases/latest` (public API, no auth).
2. Compares the release tag against the running assembly version.
3. If newer, prompts, then downloads the `.exe` asset and launches it with
   `/SILENT /SUPPRESSMSGBOXES /NORESTART`, and exits so the installer can replace files.

For this to work, each release must attach the **installer `.exe`** built below, and the repo's
Releases must be public.

## Building the installer

Requires the .NET 10 SDK and **Inno Setup 6** (ISCC). One command:

```
./installer/build-installer.ps1            # reads <Version> from the csproj
./installer/build-installer.ps1 -Version 1.2.0
```

It publishes a self-contained `win-x64` build and compiles
[`installer/Treeline.iss`](../installer/Treeline.iss) into
`installer/Treeline-Setup-<version>.exe`. That installer:

- installs per-user to `%LocalAppData%\Programs\Treeline` (no admin/UAC),
- adds a Start-menu shortcut and (opt-in, default on) a **Startup** shortcut so the tray auto-starts,
- sets `CloseApplications=yes` so the updater can replace a running copy,
- registers an uninstaller in "Installed apps".

The built `.exe` and `installer/publish/` are gitignored — they're Release assets, not source.

> `install/install.ps1` is the older dev-install path (also installs the Claude agent skill). The
> installer is what end-users and the updater use.

## Publishing a release the updater will find

1. Bump `<Version>` in `src/Treeline.App/Treeline.App.csproj`.
2. `./installer/build-installer.ps1`.
3. Create a **GitHub Release** tagged `v<version>` and attach `Treeline-Setup-<version>.exe`.
4. Ensure the repo's Releases are **public**.

## Current state (2026-08-23)

- Updater wired (v1.1.0). Installer script + `Treeline.iss` added; `Treeline-Setup-1.1.0.exe` builds
  successfully. No GitHub Release published yet — until one exists with the `.exe` asset, the tray
  check reports up to date.
