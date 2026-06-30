---
name: treeline
description: Inspect and manage local git repositories and worktrees through the Treeline app's local HTTP API. Use when the user asks about their repos/worktrees/branches across the machine, wants recent git log/status for tracked projects, or wants to run git operations (fetch, pull, checkout, create branch, add/remove worktree, delete branch, discard changes) via Treeline. Treeline must be running.
---

# Treeline agent skill

Treeline runs a local-only HTTP API (bound to `127.0.0.1`) that exposes every tracked
repository, its worktrees, branches and commit logs, plus safe git operations. Use it
instead of shelling out to `git` when the user is working through Treeline.

## 1. Discover the endpoint

Treeline writes its URL to a discovery file when running:

- Windows: `%APPDATA%\Treeline\endpoint.json`

```bash
# bash
URL=$(jq -r .url "$APPDATA/Treeline/endpoint.json")
```
```powershell
# PowerShell
$URL = (Get-Content "$env:APPDATA\Treeline\endpoint.json" | ConvertFrom-Json).url
```

If the file is missing, Treeline is not running. The default URL is `http://127.0.0.1:8787`.
Confirm with `GET $URL/api/health`.

## 2. Read state

- `GET /api/snapshot` - the whole tree: sources -> repositories -> worktrees, with current
  branch, ahead/behind, and working-tree status counts. This is the primary read.
- `GET /api/repos/{repoId}/branches` - local + remote branches with tracking info.
- `GET /api/repos/{repoId}/log?worktree=<path>&skip=0&take=5` - commits for a worktree.

Repo ids and worktree paths come from the snapshot. Always read the snapshot first.

## 3. Manage tracked sources

- `POST /api/sources` `{ "path": "C:\\Code\\proj", "type": "repo|folder", "displayName": null, "scanDepth": 3 }`
- `DELETE /api/sources/{id}` - stop tracking (does not touch disk).
- `POST /api/refresh` - rebuild everything now. `POST /api/refresh/repo/{id}` for one repo.

## 4. Non-destructive git operations

All return `{ ok, output, error }`.

- `POST /api/repos/{id}/fetch`
- `POST /api/repos/{id}/pull` `{ "worktree": "<path>" }`
- `POST /api/repos/{id}/checkout` `{ "worktree": "<path>", "branch": "<name>" }`
- `POST /api/repos/{id}/branch` `{ "name": "<name>", "from": null }`
- `POST /api/repos/{id}/worktree` `{ "path": "<newPath>", "branch": "<name>", "createBranch": true }`
- `POST /api/repos/{id}/prune`

## 5. Destructive operations - TWO-PHASE, REQUIRE USER APPROVAL

These delete data: `worktree/remove`, `branch/delete`, `discard`.

The server enforces a two-phase confirmation. Phase 1 (POST without `confirmToken`)
returns `{ "requiresConfirmation": true, "confirmToken": "...", "summary": "<what will happen>" }`.

You MUST:
1. Show the `summary` to the user verbatim and get explicit approval.
2. Only then resend the same request WITH `confirmToken` to execute.

Tokens are single-use and expire in ~2 minutes. Never auto-confirm on the user's behalf.

```
POST /api/repos/{id}/worktree/remove  { "worktree": "<path>", "force": false }              # phase 1
POST /api/repos/{id}/worktree/remove  { "worktree": "<path>", "force": false, "confirmToken": "<t>" }  # phase 2
POST /api/repos/{id}/branch/delete    { "name": "<branch>", "force": false[, "confirmToken"] }
POST /api/repos/{id}/discard          { "worktree": "<path>"[, "confirmToken"] }
```

## Example: pull every dirty-free repo's main worktree

```bash
URL=$(jq -r .url "$APPDATA/Treeline/endpoint.json")
curl -s "$URL/api/snapshot" | jq -c '.sources[].repositories[]' | while read -r repo; do
  id=$(echo "$repo" | jq -r .id)
  main=$(echo "$repo" | jq -r '.worktrees[] | select(.isMain) | .path')
  curl -s -X POST "$URL/api/repos/$id/pull" -H 'Content-Type: application/json' \
       -d "{\"worktree\":\"$main\"}"
done
```
