# Treeline HTTP API

Treeline exposes a loopback-only HTTP API used by both the browser UI and local agents.

- Default base URL: `http://127.0.0.1:8787`
- Actual running URL: `%APPDATA%\Treeline\endpoint.json`
- Content type: JSON for both requests and responses unless noted otherwise

## Conventions

Read endpoints generally return domain objects directly.

Operation endpoints generally return:

```json
{
  "ok": true,
  "output": "optional stdout",
  "error": null
}
```

Validation failures typically return:

```json
{
  "error": "Human-readable message"
}
```

Destructive endpoints use a two-phase confirmation flow:

1. Call the endpoint without `confirmToken`.
2. Receive `requiresConfirmation`, `confirmToken`, and `summary`.
3. Show `summary` to the user and obtain approval.
4. Repeat the same request with `confirmToken`.

Confirmation tokens are single-use and expire after roughly 120 seconds.

## Health

### `GET /api/health`

Returns process and snapshot metadata.

Example response:

```json
{
  "status": "ok",
  "version": "1.0.0.0",
  "gitVersion": "git version 2.x",
  "dataDirectory": "C:\\Users\\you\\AppData\\Roaming\\Treeline",
  "generatedAt": "2026-07-11T10:00:00+00:00",
  "sources": 2,
  "repositories": 18,
  "worktrees": 24
}
```

## Snapshot Read Model

### `GET /api/snapshot`

Returns the full immutable snapshot used by the UI:

- tracked sources
- discovered repositories
- linked worktrees
- branch and status summary

Clients should treat this as the primary read endpoint.

### `GET /api/snapshot/revision`

Cheap polling endpoint for clients that want to avoid fetching the full snapshot unless the
server state has changed.

Example response:

```json
{
  "revision": 42,
  "generatedAt": "2026-07-11T10:00:00+00:00"
}
```

### `GET /api/repos/{id}`

Returns one repository object from the current snapshot.

- `404 Not Found` if the repo id is unknown

### `GET /api/repos/{id}/branches?remote=true`

Returns local branches and, by default, remote branches too.

Query parameters:

| Name | Default | Meaning |
| --- | --- | --- |
| `remote` | `true` | Include remote branches in the response. |

### `GET /api/repos/{id}/log?worktree=<path>&skip=0&take=5`

Returns recent commit history for one worktree.

Query parameters:

| Name | Required | Meaning |
| --- | --- | --- |
| `worktree` | yes | Full worktree path from the snapshot. |
| `skip` | no | Number of commits to skip. |
| `take` | no | Number of commits to return. |

- `400 Bad Request` if the worktree path does not belong to the repository
- `404 Not Found` if the repo id is unknown

## Filesystem Endpoints

These endpoints support the source picker flow in the UI.

### `GET /api/fs?path=<directory>`

Returns a browsable directory listing.

- omit `path` to browse a default root
- `400 Bad Request` if the directory does not exist or cannot be read

### `POST /api/fs/reveal`

Opens a directory in the system shell.

Request body:

```json
{
  "path": "C:\\Code\\my-repo"
}
```

- `400 Bad Request` if the path is missing or does not exist

## Source Management

### `GET /api/sources`

Returns all tracked sources.

### `POST /api/sources`

Adds a tracked source and triggers a refresh for that source.

Request body:

```json
{
  "path": "C:\\Code",
  "type": "folder",
  "displayName": "Work",
  "scanDepth": 3
}
```

Fields:

| Field | Required | Meaning |
| --- | --- | --- |
| `path` | yes | Directory path to track. |
| `type` | yes | `"folder"` or `"repo"`. |
| `displayName` | no | Friendly label for the source. |
| `scanDepth` | no | Folder scan depth, used for folder sources. |

### `PATCH /api/sources/{id}`

Updates mutable source fields, then refreshes that source.

Request body:

```json
{
  "displayName": "Updated name",
  "scanDepth": 5
}
```

### `DELETE /api/sources/{id}`

Stops tracking the source and refreshes the global snapshot.

This does not modify any files on disk.

## Refresh

### `POST /api/refresh`

Refreshes every tracked source and repository.

### `POST /api/refresh/source/{id}`

Refreshes one tracked source.

### `POST /api/refresh/repo/{id}`

Refreshes one repository.

## Non-Destructive Git Operations

### `POST /api/repos/{id}/fetch`

Fetches remotes for the repository.

Request body: none

### `POST /api/repos/{id}/pull`

Pulls one worktree.

Request body:

```json
{
  "worktree": "C:\\Code\\project"
}
```

### `POST /api/repos/{id}/checkout`

Checks out a branch in one worktree.

Request body:

```json
{
  "worktree": "C:\\Code\\project",
  "branch": "feature/example"
}
```

### `POST /api/repos/{id}/branch`

Creates a branch in the repository.

Request body:

```json
{
  "name": "feature/example",
  "from": "origin/main"
}
```

`from` is optional.

### `POST /api/repos/{id}/worktree`

Creates a new worktree.

Request body:

```json
{
  "path": "C:\\Code\\project-feature",
  "branch": "feature/example",
  "createBranch": true
}
```

### `POST /api/repos/{id}/prune`

Runs worktree prune for the repository.

Request body: none

## Destructive Git Operations

These endpoints do not execute immediately unless a valid `confirmToken` is supplied.

Phase-one confirmation response:

```json
{
  "requiresConfirmation": true,
  "confirmToken": "token-value",
  "summary": "Human-readable description of the action"
}
```

### `POST /api/repos/{id}/worktree/remove`

Request body:

```json
{
  "worktree": "C:\\Code\\project-feature",
  "force": false,
  "confirmToken": "optional-token"
}
```

### `POST /api/repos/{id}/branch/delete`

Request body:

```json
{
  "name": "feature/example",
  "force": false,
  "confirmToken": "optional-token"
}
```

### `POST /api/repos/{id}/discard`

Request body:

```json
{
  "worktree": "C:\\Code\\project",
  "confirmToken": "optional-token"
}
```

This operation runs the equivalent of:

```bash
git reset --hard HEAD
git clean -fd
```

## Client Notes

- Repo ids are snapshot-derived and should be treated as opaque.
- Worktree paths should come from the snapshot instead of being guessed.
- Clients that poll should prefer `/api/snapshot/revision` and fetch `/api/snapshot` only
  when the revision changes.
- Destructive actions should always surface the server-provided `summary` directly to the
  user before phase-two confirmation.
