<div align="center">
  <img src="src/Treeline.App/wwwroot/assets/treeline.png" width="96" alt="Treeline" />
  <h1>Treeline</h1>
  <p>A local dashboard for every git repository and worktree on your machine.</p>
</div>

Treeline lives in your system tray and serves a modern web dashboard on `localhost`. Add
folders or individual repositories, then drill down through repos -> worktrees -> branches
and commit history, and run common git operations - all kept up to date automatically.

It is also **agent-friendly**: the same local HTTP API the UI uses is available to any
agent on the machine, and ships with a Claude Code skill.

## Features

- **Add folders or repos.** A *folder* source is scanned (configurable depth) for git
  repositories; a *repo* source points at exactly one. Linked worktrees are collapsed onto
  their parent repository automatically.
- **Drill-down tree.** Sources -> repositories -> worktrees, with current branch,
  ahead/behind, and working-tree status (staged / modified / untracked / conflicts).
- **Branches & logs.** Per-repo branch list with tracking info; per-worktree commit log
  showing the last 5 commits with a **Show more** control.
- **Git operations.** Fetch, pull (ff-only), checkout, create branch, add worktree, prune.
- **Destructive operations are double-confirmed.** Remove worktree, delete branch, and
  discard changes require an explicit confirmation in the UI *and* a single-use,
  server-issued confirmation token - enforced for the API too, so agents cannot skip it.
- **Always fresh.** The server re-reads git state every 10 seconds; a global refresh and
  per-source / per-repo refresh buttons trigger an immediate update.
- **Local persistence.** Tracked sources and config live in plain JSON under
  `%APPDATA%\Treeline` - no database engine, nothing leaves your machine.
- **Tray app.** Quick access to the dashboard, refresh, and data folder. Loopback-only.

## Requirements

- Windows 10/11
- `git` on `PATH`
- .NET 10 SDK (to build). The installer can produce a **self-contained** build that needs
  no .NET runtime on the target machine.

## Quick start

```powershell
git clone https://github.com/lukr-99/treeline.git
cd treeline
./install/install.ps1
```

The installer publishes Treeline, installs it to `%LOCALAPPDATA%\Programs\Treeline`, adds a
Start Menu shortcut, registers auto-start (tray on login), installs the Claude agent skill,
and launches it. Useful switches:

```powershell
./install/install.ps1 -Port 9000          # use a different port
./install/install.ps1 -FrameworkDependent # smaller build, requires .NET 10 runtime
./install/install.ps1 -NoStartup          # do not auto-start on login
./install/uninstall.ps1                    # remove (add -PurgeData to delete local data)
```

## Usage

- Double-click the tray icon (or **Open Treeline**) to open the dashboard.
- Click **+ Add source** and point it at a repo or a folder of repos.
- Expand a repo to see its branches and worktrees. Expand a worktree's **log** for commits.
- Toolbar: **Auto** toggles 10s polling, **↻ Refresh** updates everything now, the theme
  button switches dark/light.

## Configuration

Stored in `%APPDATA%\Treeline\config.json`:

| Key | Default | Meaning |
| --- | --- | --- |
| `port` | `8787` | Loopback port for the API/UI. |
| `refreshIntervalSeconds` | `10` | Server-side background refresh cadence (3-600). |

Tracked sources are in `%APPDATA%\Treeline\sources.json`. The running URL is published to
`%APPDATA%\Treeline\endpoint.json` for discovery.

## Agent access

Treeline exposes its API on loopback for local agents. Discover the URL from
`%APPDATA%\Treeline\endpoint.json`, then call the endpoints below. A ready-made Claude Code
skill is in [`skills/treeline`](skills/treeline/SKILL.md) (installed automatically) and the
full endpoint reference is in [`docs/API.md`](docs/API.md).

Destructive endpoints are two-phase: the first call returns a `confirmToken` and a human
summary; the operation only runs when called again with that token. Agents must surface the
summary and get user approval before confirming.

## Architecture

```
src/
  Treeline.Core/   net10.0          Domain models, git service, JSON stores, snapshot engine
  Treeline.App/    net10.0-windows  ASP.NET Core API + static UI host + WinForms tray
    wwwroot/                          Vanilla-JS dashboard (no build step)
skills/treeline/                      Claude agent skill
install/                              install / uninstall scripts
docs/                                 API reference
```

- `GitRunner` shells out to `git` with argument lists (no string interpolation) and a
  non-interactive environment.
- `SnapshotService` builds an immutable read model; readers always see a complete snapshot.
- `ConfirmationService` issues and consumes single-use tokens for destructive operations.

## Development

```powershell
dotnet build Treeline.slnx
dotnet run --project src/Treeline.App --             # tray + dashboard
dotnet run --project src/Treeline.App -- --headless --port 8787   # server only (for agents)
```

## License

MIT
