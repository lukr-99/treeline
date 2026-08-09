<div align="center">
  <img src="src/Treeline.App/wwwroot/assets/treeline.png" width="96" alt="Treeline" />
  <h1>Treeline</h1>
  <p>A local Windows dashboard for the git repositories and worktrees on your machine.</p>
</div>

Treeline runs as a tray app and serves a loopback-only web UI on `localhost`. You add
folders or individual repositories, and Treeline keeps a live read model of repos,
worktrees, branches, status, and recent commit history. The same HTTP API that powers the
UI is available to local tools and agents.

## Why Treeline

- Track many repos from one place without opening each one in a separate tool.
- See linked worktrees grouped under their parent repository.
- Run common git operations from a local UI or through a local HTTP API.
- Keep destructive actions gated behind explicit, server-enforced confirmation.
- Stay local: no cloud sync, no database server, no telemetry pipeline.

## Features

- Add either a single repository or a folder that should be scanned for repositories.
- Browse a tree of tracked sources, repositories, and worktrees.
- See branch, ahead/behind, dirty-state, and conflict information at a glance.
- Inspect branch lists and paged commit history for each worktree.
- Run fetch, pull, checkout, branch creation, worktree creation, and prune operations.
- Remove worktrees, delete branches, and discard changes through a two-phase confirmation flow.
- Use global refresh, per-source refresh, per-repo refresh, or background refresh polling.
- Open a dedicated UI Components view for isolated interaction experiments.
- Access the same state and operations over a local HTTP API for agent workflows.

## Requirements

- Windows 10 or Windows 11
- `git` available on `PATH`
- .NET 10 SDK to build from source

The installer can publish a self-contained build, so the installed app does not require a
separate .NET runtime on the target machine.

## Quick Start

```powershell
git clone https://github.com/lukr-99/treeline.git
cd treeline
./install/install.ps1
```

By default, the installer:

- publishes a Release build
- installs Treeline to `%LOCALAPPDATA%\Programs\Treeline`
- creates a Start Menu shortcut
- registers the tray app to start on login
- installs the bundled Treeline skill into `.claude\skills\treeline` if present
- launches the app after install

Useful installer switches:

```powershell
./install/install.ps1 -Port 9000
./install/install.ps1 -FrameworkDependent
./install/install.ps1 -NoStartup
./install/install.ps1 -NoLaunch
./install/install.ps1 -NoShortcuts
./install/install.ps1 -NoSkill
./install/uninstall.ps1
./install/uninstall.ps1 -PurgeData
```

## Using Treeline

1. Launch Treeline from the Start Menu or system tray.
2. Open the dashboard in your browser.
3. Add a source as either:
   - a `repo` source for one repository
   - a `folder` source for recursive scanning
4. Expand repositories to inspect worktrees, branches, and recent commits.
5. Use the toolbar to refresh, toggle polling, or switch theme.

Treeline binds only to `127.0.0.1`. It is intended for local use on your machine, not as a
remote service.

## Data And Configuration

Treeline stores plain JSON under `%APPDATA%\Treeline`.

| File | Purpose |
| --- | --- |
| `config.json` | Runtime configuration such as the selected port. |
| `sources.json` | Tracked folder and repository sources. |
| `endpoint.json` | Published loopback URL for the running instance. |

Current config keys:

| Key | Default | Meaning |
| --- | --- | --- |
| `port` | `8787` | Loopback port used by the UI and API. |
| `refreshIntervalSeconds` | `10` | Background refresh interval, constrained server-side. |

## HTTP API

Treeline exposes a local HTTP API for automation and agent workflows.

- Base URL: `http://127.0.0.1:<port>`
- Discovery: `%APPDATA%\Treeline\endpoint.json`
- Reference: [`docs/API.md`](docs/API.md)

Important behavior:

- Read operations expose the current immutable snapshot of tracked sources and repos.
- Operation endpoints return JSON objects with `ok`, `output`, and `error`.
- Destructive endpoints are two-phase. The first call returns a confirmation token and a
  human summary. The second call must resend the request with that token.

## Architecture

```text
src/
  Treeline.Core/          Domain models, git wrappers, storage, snapshot services
  Treeline.App/           Windows host, tray app, ASP.NET Core API, static web UI
    wwwroot/              Vanilla JS and CSS frontend, no separate build step
docs/                     Project documentation
install/                  Install and uninstall scripts
skills/treeline/          Local agent skill definition
```

Implementation notes:

- `GitRunner` executes `git` with explicit argument lists in a non-interactive process.
- `SnapshotService` builds and swaps complete snapshots so readers never see partial state.
- `ConfirmationService` issues single-use confirmation tokens for destructive actions.
- The UI is static content served directly by the app host.

## Development

Build the solution:

```powershell
dotnet build Treeline.slnx
```

Run the full tray application:

```powershell
dotnet run --project src/Treeline.App
```

Run headless server mode:

```powershell
dotnet run --project src/Treeline.App -- --headless --port 8787
```

## Repository Layout

| Path | Description |
| --- | --- |
| `src/Treeline.App` | Windows host, API endpoints, tray integration, static frontend |
| `src/Treeline.Core` | Git services, models, scanning, persistence, snapshots |
| `docs` | API and project documentation |
| `install` | Local install and uninstall scripts |
| `skills/treeline` | Bundled skill for agent use |

## License

[PolyForm Noncommercial 1.0.0](LICENSE.md) — free for personal and non-commercial use; selling or other commercial use requires permission.
