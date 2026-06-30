# Treeline HTTP API

Base URL: `http://127.0.0.1:<port>` (default `8787`, loopback only). The live URL is
published to `%APPDATA%\Treeline\endpoint.json`.

All request/response bodies are JSON. Operation endpoints return
`{ "ok": bool, "output": string?, "error": string? }`.

## Health & read model

| Method | Path | Description |
| --- | --- | --- |
| GET | `/api/health` | Status, version, git version, data dir, counts. |
| GET | `/api/snapshot` | Full tree: `sources[] -> repositories[] -> worktrees[]`. |
| GET | `/api/repos/{id}` | One repository from the snapshot. |
| GET | `/api/repos/{id}/branches?remote=true` | Local + remote branches. |
| GET | `/api/repos/{id}/log?worktree=<path>&skip=0&take=5` | Commits for a worktree. |

## Sources

| Method | Path | Body |
| --- | --- | --- |
| GET | `/api/sources` | - |
| POST | `/api/sources` | `{ path, type: "folder"\|"repo", displayName?, scanDepth? }` |
| PATCH | `/api/sources/{id}` | `{ displayName?, scanDepth? }` |
| DELETE | `/api/sources/{id}` | Untrack (no disk changes). |

## Refresh

| Method | Path |
| --- | --- |
| POST | `/api/refresh` |
| POST | `/api/refresh/source/{id}` |
| POST | `/api/refresh/repo/{id}` |

## Git operations (non-destructive)

| Method | Path | Body |
| --- | --- | --- |
| POST | `/api/repos/{id}/fetch` | - |
| POST | `/api/repos/{id}/pull` | `{ worktree }` |
| POST | `/api/repos/{id}/checkout` | `{ worktree, branch }` |
| POST | `/api/repos/{id}/branch` | `{ name, from? }` |
| POST | `/api/repos/{id}/worktree` | `{ path, branch?, createBranch }` |
| POST | `/api/repos/{id}/prune` | - |

## Git operations (destructive, two-phase)

Call once without a token to receive
`{ requiresConfirmation: true, confirmToken, summary }`, then call again with
`confirmToken` to execute. Tokens are single-use and expire in ~120s.

| Method | Path | Body |
| --- | --- | --- |
| POST | `/api/repos/{id}/worktree/remove` | `{ worktree, force?, confirmToken? }` |
| POST | `/api/repos/{id}/branch/delete` | `{ name, force?, confirmToken? }` |
| POST | `/api/repos/{id}/discard` | `{ worktree, confirmToken? }` |

`discard` runs `git reset --hard HEAD` followed by `git clean -fd`.
