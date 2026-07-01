"use strict";

/* ---------------- API ---------------- */
const api = {
  async call(method, path, body) {
    const res = await fetch(path, {
      method,
      headers: body ? { "Content-Type": "application/json" } : undefined,
      body: body ? JSON.stringify(body) : undefined,
    });
    const text = await res.text();
    const data = text ? JSON.parse(text) : null;
    if (!res.ok) throw new Error((data && (data.error || data.title)) || `HTTP ${res.status}`);
    return data;
  },
  get: (p) => api.call("GET", p),
  post: (p, b) => api.call("POST", p, b),
  patch: (p, b) => api.call("PATCH", p, b),
  del: (p) => api.call("DELETE", p),
};

/* ---------------- State ---------------- */
const state = {
  snapshot: null,
  revision: null,
  health: null,
  openRepos: new Set(JSON.parse(localStorage.getItem("tl.openRepos") || "[]")),
  collapsedSources: new Set(JSON.parse(localStorage.getItem("tl.collapsedSources") || "[]")),
  openLogs: new Set(),                 // key: repoId|worktreePath
  branchCache: new Map(),              // repoId -> branches[]
  commitCache: new Map(),              // key -> { commits, take, end }
  autoRefresh: localStorage.getItem("tl.autoRefresh") !== "false",
  theme: localStorage.getItem("tl.theme") || "dark",
};
const logKey = (repoId, path) => `${repoId}|${path}`;
const persist = () => {
  localStorage.setItem("tl.openRepos", JSON.stringify([...state.openRepos]));
  localStorage.setItem("tl.collapsedSources", JSON.stringify([...state.collapsedSources]));
  localStorage.setItem("tl.autoRefresh", String(state.autoRefresh));
  localStorage.setItem("tl.theme", state.theme);
};

/* ---------------- Helpers ---------------- */
const $ = (id) => document.getElementById(id);
const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const basename = (p) => (p || "").replace(/[\\/]+$/, "").split(/[\\/]/).pop() || p;
const trunc = (s, n = 34) => { s = String(s ?? ""); return s.length > n ? s.slice(0, n - 1) + "…" : s; };
function relTime(iso) {
  if (!iso) return "";
  const d = new Date(iso), s = (Date.now() - d.getTime()) / 1000;
  if (s < 60) return "just now";
  if (s < 3600) return `${Math.floor(s / 60)}m ago`;
  if (s < 86400) return `${Math.floor(s / 3600)}h ago`;
  if (s < 2592000) return `${Math.floor(s / 86400)}d ago`;
  return d.toLocaleDateString();
}

/* ---------------- Toasts ---------------- */
function toast(title, body, kind = "ok") {
  const el = document.createElement("div");
  el.className = `toast ${kind}`;
  el.innerHTML = `<div class="toast-title">${esc(title)}</div>${body ? `<div class="toast-body">${esc(body)}</div>` : ""}`;
  $("toasts").appendChild(el);
  setTimeout(() => el.remove(), kind === "err" ? 8000 : 4000);
}
function reportOp(label, result) {
  if (result && result.ok === false) toast(`${label} failed`, result.error || result.output, "err");
  else if (result && result.output) toast(label, result.output.split("\n").slice(0, 4).join("\n"));
  else toast(label, "Done");
}

/* ---------------- Data loading ---------------- */
async function loadHealth() {
  try {
    state.health = await api.get("/api/health");
    $("gitVersion").textContent = state.health.gitVersion || "git not found";
  } catch { $("gitVersion").textContent = "server unreachable"; }
}
async function loadSnapshot() {
  state.snapshot = await api.get("/api/snapshot");
}
async function loadBranches(repoId) {
  try { state.branchCache.set(repoId, await api.get(`/api/repos/${repoId}/branches`)); }
  catch { state.branchCache.set(repoId, []); }
}
async function loadCommits(repoId, path, take) {
  const key = logKey(repoId, path);
  try {
    const commits = await api.get(`/api/repos/${repoId}/log?worktree=${encodeURIComponent(path)}&skip=0&take=${take}`);
    state.commitCache.set(key, { commits, take, end: commits.length < take });
  } catch { state.commitCache.set(key, { commits: [], take, end: true }); }
}

function eachOpenRepo(fn) {
  for (const node of state.snapshot?.sources || [])
    for (const repo of node.repositories)
      if (state.openRepos.has(repo.id)) fn(repo);
}

/* Re-fetch only the open commit logs (cheap). Branches are NOT refetched on the timer -
   they can be large (hundreds of refs) and rarely change; they load on expand / manual refresh. */
async function refreshOpenLogs() {
  const jobs = [];
  eachOpenRepo((repo) => {
    for (const wt of repo.worktrees) {
      const key = logKey(repo.id, wt.path);
      if (state.openLogs.has(key)) jobs.push(loadCommits(repo.id, wt.path, state.commitCache.get(key)?.take || 5));
    }
  });
  await Promise.all(jobs);
}

/* Refresh branch caches for open repos - only on explicit user refresh, never on the timer. */
async function reloadOpenBranches() {
  const jobs = [];
  eachOpenRepo((repo) => jobs.push(loadBranches(repo.id)));
  await Promise.all(jobs);
}

/* ---------------- Rendering ---------------- */
function sourceName(s) { return s.displayName || basename(s.path); }

function render() {
  const snap = state.snapshot;
  const content = $("content");
  renderStats();
  $("updated").textContent = snap ? relTime(snap.generatedAt) : "";

  if (!snap || snap.sources.length === 0) {
    content.innerHTML = `<div class="empty">No sources yet.<div class="empty-cta"><button class="btn btn-primary" data-action="add-source">+ Add your first folder or repo</button></div></div>`;
    return;
  }
  content.innerHTML = snap.sources.map(renderSource).join("");
}

function renderStats() {
  const s = state.snapshot;
  if (!s) { $("stats").innerHTML = ""; return; }
  $("stats").innerHTML =
    `<span class="stat-chip"><b>${s.sources.length}</b> sources</span>` +
    `<span class="stat-chip"><b>${s.totalRepositories}</b> repos</span>` +
    `<span class="stat-chip"><b>${s.totalWorktrees}</b> worktrees</span>`;
}

function renderSource(node) {
  const s = node.source;
  const collapsed = state.collapsedSources.has(s.id);
  const badge = s.type === "folder" ? `<span class="badge badge-folder">folder</span>` : `<span class="badge badge-repo">repo</span>`;
  const body = node.error
    ? `<div class="error-banner">${esc(node.error)}</div>`
    : (node.repositories.length === 0
        ? `<div class="muted" style="padding:6px 2px">No git repositories found under this folder.</div>`
        : node.repositories.map(renderRepo).join(""));
  return `
  <section class="source ${collapsed ? "collapsed" : ""}" data-source="${s.id}">
    <div class="source-head" data-action="toggle-source" data-id="${s.id}">
      <span class="caret">▾</span>
      <div class="source-title">
        <span class="source-name">${esc(sourceName(s))} ${badge}</span>
        <span class="source-path">${esc(s.path)}</span>
      </div>
      <div class="source-meta">
        <span class="count">${node.repositories.length} repo${node.repositories.length === 1 ? "" : "s"}</span>
        <button class="btn btn-ghost btn-icon btn-sm" title="Open in Explorer" data-action="reveal" data-path="${esc(s.path)}">📂</button>
        <button class="btn btn-ghost btn-icon btn-sm" title="Refresh this source" data-action="refresh-source" data-id="${s.id}"><span class="ico-refresh">↻</span></button>
        <button class="btn btn-ghost btn-icon btn-sm" title="Stop tracking" data-action="remove-source" data-id="${s.id}" data-name="${esc(sourceName(s))}">✕</button>
      </div>
    </div>
    <div class="source-body">${body}</div>
  </section>`;
}

function renderRepo(repo) {
  const open = state.openRepos.has(repo.id);
  if (!repo.isValid) {
    return `<div class="repo" data-repo="${repo.id}"><div class="repo-head">
      <span class="repo-name">${esc(repo.name)}</span>
      <span class="repo-invalid">${esc(repo.error || "invalid repository")}</span></div></div>`;
  }
  const dirty = repo.worktrees.some((w) => w.status.isDirty);
  const ab = repo.worktrees.reduce((a, w) => ({ ahead: a.ahead + w.ahead, behind: a.behind + w.behind }), { ahead: 0, behind: 0 });
  const statusChip = dirty
    ? `<span class="chip chip-dirty"><span class="dot amber"></span>changes</span>`
    : `<span class="chip chip-clean"><span class="dot green"></span>clean</span>`;
  const abChip = (ab.ahead || ab.behind) ? `<span class="chip chip-ab">↑${ab.ahead} ↓${ab.behind}</span>` : "";
  const branchChip = repo.currentBranch ? `<span class="chip chip-branch" title="${esc(repo.currentBranch)}">⎇ ${esc(trunc(repo.currentBranch, 22))}</span>` : `<span class="chip">detached</span>`;

  return `
  <div class="repo ${open ? "open" : ""}" data-repo="${repo.id}">
    <div class="repo-head" data-action="toggle-repo" data-id="${repo.id}">
      <span class="caret">▾</span>
      <span class="repo-name">${esc(repo.name)}</span>
      ${branchChip}
      <span class="chip">${repo.worktreeCount} tree${repo.worktreeCount === 1 ? "" : "s"}</span>
      ${statusChip}${abChip}
      <div class="repo-actions">
        <button class="btn btn-ghost btn-sm" title="git fetch --all" data-action="fetch" data-id="${repo.id}">fetch</button>
        <button class="btn btn-ghost btn-icon btn-sm" title="Open in Explorer" data-action="reveal" data-path="${esc(repo.path)}">📂</button>
        <button class="btn btn-ghost btn-icon btn-sm" title="Refresh repo" data-action="refresh-repo" data-id="${repo.id}"><span class="ico-refresh">↻</span></button>
      </div>
    </div>
    <div class="repo-body">${open ? renderRepoBody(repo) : ""}</div>
  </div>`;
}

function renderRepoBody(repo) {
  // Trees are the primary view; branches live behind a searchable popup.
  return renderWorktrees(repo) + renderBranchesSummary(repo);
}

function renderBranchesSummary(repo) {
  const branches = state.branchCache.get(repo.id);
  let counts;
  if (!branches) counts = `<span class="muted">loading…</span>`;
  else {
    const local = branches.filter((b) => !b.isRemote).length;
    const remote = branches.filter((b) => b.isRemote).length;
    counts = `<span class="muted">${local} local · ${remote} remote</span>`;
  }
  return `
    <div>
      <div class="section-title">Branches ${counts}
        <span class="grow">
          <button class="btn btn-ghost btn-sm" data-action="view-branches" data-id="${repo.id}">View branches</button>
          <button class="btn btn-ghost btn-sm" data-action="create-branch" data-id="${repo.id}">+ new branch</button>
        </span>
      </div>
    </div>`;
}

function renderWorktrees(repo) {
  const rows = repo.worktrees.map((wt) => renderWorktree(repo, wt)).join("");
  return `
    <div>
      <div class="section-title">Worktrees <span class="muted">${repo.worktreeCount} tree${repo.worktreeCount === 1 ? "" : "s"}</span>
        <span class="grow">
          <button class="btn btn-ghost btn-sm" data-action="add-worktree" data-id="${repo.id}">+ add worktree</button>
          <button class="btn btn-ghost btn-sm" data-action="prune" data-id="${repo.id}" title="git worktree prune">prune</button>
        </span>
      </div>
      ${rows}
    </div>`;
}

function renderWorktree(repo, wt) {
  const st = wt.status;
  const statusBits = [];
  if (st.staged) statusBits.push(`<span class="chip" style="color:var(--accent)">+${st.staged} staged</span>`);
  if (st.modified) statusBits.push(`<span class="chip chip-dirty">~${st.modified} modified</span>`);
  if (st.untracked) statusBits.push(`<span class="chip">?${st.untracked} new</span>`);
  if (st.conflicted) statusBits.push(`<span class="chip" style="color:var(--danger)">!${st.conflicted} conflict</span>`);
  if (!st.isDirty) statusBits.push(`<span class="chip chip-clean"><span class="dot green"></span>clean</span>`);

  const treeName = basename(wt.path);
  const branchPill = wt.branch
    ? `<span class="k">on</span><span class="pill pill-branch" title="${esc(wt.branch)}">⎇ ${esc(trunc(wt.branch))}</span>`
    : `<span class="k">on</span><span class="pill">detached @ ${esc((wt.head || "").slice(0, 7))}</span>`;
  const ab = (wt.ahead || wt.behind) ? `<span class="chip chip-ab">↑${wt.ahead} ↓${wt.behind}</span>` : "";
  const basePill = wt.upstream
    ? `<span class="k">base</span><span class="pill pill-base" title="${esc(wt.upstream)}">↥ ${esc(trunc(wt.upstream))}</span>${ab}`
    : `<span class="k">base</span><span class="muted" style="font-size:11.5px">no upstream</span>`;
  const flags =
    (wt.isLocked ? `<span class="chip" style="color:var(--warn)">locked</span>` : "") +
    (!wt.exists ? `<span class="chip" style="color:var(--danger)">missing</span>` : "");
  const key = logKey(repo.id, wt.path);
  const logOpen = state.openLogs.has(key);

  return `
    <div class="row">
      <div class="row-main">
        <div class="row-title">
          ${wt.isMain ? `<span class="tag-main">main</span>` : ""}
          <span class="tree-name">🌳 ${esc(treeName)}</span>
        </div>
        <div class="row-line">${branchPill}${basePill}</div>
        <div class="row-line">${statusBits.join("")}${flags}</div>
        <div class="row-sub">${esc(wt.path)}</div>
      </div>
      <div class="row-actions">
        <button class="btn btn-ghost btn-icon btn-sm" title="Open in Explorer" data-action="reveal" data-path="${esc(wt.path)}">📂</button>
        <button class="btn btn-ghost btn-sm" data-action="toggle-log" data-id="${repo.id}" data-wt="${esc(wt.path)}">${logOpen ? "hide log" : "log"}</button>
        <button class="btn btn-ghost btn-sm" data-action="pull" data-id="${repo.id}" data-wt="${esc(wt.path)}" title="git pull --ff-only">pull</button>
        <button class="btn btn-danger btn-sm" data-action="discard" data-id="${repo.id}" data-wt="${esc(wt.path)}" ${st.isDirty ? "" : "disabled"} title="Discard all local changes">discard</button>
        ${wt.isMain ? "" : `<button class="btn btn-danger btn-sm" data-action="remove-worktree" data-id="${repo.id}" data-wt="${esc(wt.path)}">remove</button>`}
      </div>
    </div>
    ${logOpen ? renderLog(repo.id, wt.path) : ""}`;
}

function renderLog(repoId, path) {
  const entry = state.commitCache.get(logKey(repoId, path));
  if (!entry) return `<div class="muted" style="padding:6px 11px">Loading log…</div>`;
  if (entry.commits.length === 0) return `<div class="muted" style="padding:6px 11px">No commits.</div>`;
  const items = entry.commits.map((c) => `
    <div class="commit" title="${esc(c.subject)}">
      <span class="commit-sha">${esc(c.shortSha)}</span>
      <span class="commit-msg">${esc(c.subject)}</span>
      <span class="commit-meta">${esc(c.author)} · ${relTime(c.date)}</span>
    </div>`).join("");
  const more = entry.end ? "" : `<div class="commit" style="justify-content:center"><span class="link" data-action="more-log" data-id="${repoId}" data-wt="${esc(path)}">Show more ↓</span></div>`;
  return `<div class="commit-list" style="margin:4px 0 2px">${items}${more}</div>`;
}

/* ---------------- Event delegation ---------------- */
$("content").addEventListener("click", async (e) => {
  const t = e.target.closest("[data-action]");
  if (!t) return;
  const { action, id, wt, branch, name } = t.dataset;
  e.stopPropagation();
  switch (action) {
    case "add-source": return openAddSource();
    case "reveal": return openExplorer(t.dataset.path);
    case "toggle-source": toggleSet(state.collapsedSources, id); persist(); return render();
    case "toggle-repo": return toggleRepo(id);
    case "toggle-log": return toggleLog(id, wt);
    case "more-log": return moreLog(id, wt);
    case "refresh-source": return withSpin(t, () => refreshSource(id));
    case "refresh-repo": return withSpin(t, () => refreshRepo(id));
    case "remove-source": return removeSource(id, name);
    case "fetch": return withSpin(t, () => gitOp(id, `/api/repos/${id}/fetch`, null, "Fetch"));
    case "pull": return gitOp(id, `/api/repos/${id}/pull`, { worktree: wt }, "Pull");
    case "checkout": return gitOp(id, `/api/repos/${id}/checkout`, { worktree: wt, branch }, `Checkout ${branch}`);
    case "view-branches": return openBranchesModal(id);
    case "create-branch": return openCreateBranch(id);
    case "add-worktree": return openAddWorktree(id);
    case "prune": return gitOp(id, `/api/repos/${id}/prune`, null, "Prune worktrees");
    case "discard": return destructive(`/api/repos/${id}/discard`, { worktree: wt }, "Discard changes", id);
    case "remove-worktree": return destructive(`/api/repos/${id}/worktree/remove`, { worktree: wt, force: false }, "Remove worktree", id);
    case "delete-branch": return destructive(`/api/repos/${id}/branch/delete`, { name: branch, force: false }, "Delete branch", id);
  }
});

function toggleSet(set, key) { set.has(key) ? set.delete(key) : set.add(key); }

async function toggleRepo(id) {
  if (state.openRepos.has(id)) { state.openRepos.delete(id); persist(); return render(); }
  state.openRepos.add(id); persist(); render();
  if (!state.branchCache.has(id)) { await loadBranches(id); render(); }
}
async function toggleLog(repoId, path) {
  const key = logKey(repoId, path);
  if (state.openLogs.has(key)) { state.openLogs.delete(key); return render(); }
  state.openLogs.add(key); render();
  if (!state.commitCache.has(key)) { await loadCommits(repoId, path, 5); render(); }
}
async function moreLog(repoId, path) {
  const key = logKey(repoId, path);
  const take = (state.commitCache.get(key)?.take || 5) + 5;
  await loadCommits(repoId, path, take); render();
}

async function withSpin(btn, fn) {
  const ico = btn.querySelector(".ico-refresh") || btn;
  ico.classList.add("spinning");
  try { await fn(); } finally { ico.classList.remove("spinning"); }
}

/* ---------------- Operations ---------------- */
async function gitOp(repoId, url, body, label) {
  try {
    const r = await api.post(url, body);
    reportOp(label, r);
    await softRefresh();
  } catch (err) { toast(`${label} failed`, err.message, "err"); }
}

async function destructive(url, payload, label, repoId, onDone) {
  try {
    const phase1 = await api.post(url, payload);          // no token -> server asks for confirmation
    if (!phase1 || !phase1.requiresConfirmation) { reportOp(label, phase1); await softRefresh(); if (onDone) await onDone(); return; }
    confirmModal(label, phase1.summary, async () => {
      const r = await api.post(url, { ...payload, confirmToken: phase1.confirmToken });
      reportOp(label, r);
      await softRefresh();
      if (onDone) await onDone();
    });
  } catch (err) { toast(`${label} failed`, err.message, "err"); }
}

async function refreshAll() {
  try {
    state.snapshot = await api.post("/api/refresh", null);
    state.revision = state.snapshot.revision;
    await Promise.all([refreshOpenLogs(), reloadOpenBranches()]);
    render();
  } catch (err) { toast("Refresh failed", err.message, "err"); }
}
async function refreshSource(id) { await api.post(`/api/refresh/source/${id}`, null); await reloadOpenBranches(); await softRefresh(); }
async function refreshRepo(id) { state.branchCache.delete(id); await api.post(`/api/refresh/repo/${id}`, null); if (state.openRepos.has(id)) await loadBranches(id); await softRefresh(); }

async function removeSource(id, name) {
  confirmModal("Stop tracking source", `Stop tracking "${name}"? This only removes it from Treeline. Nothing on disk is deleted.`, async () => {
    await api.del(`/api/sources/${id}`);
    toast("Source removed", name);
    await softRefresh();
  }, { soft: true });
}

/* Poll snapshot + refresh open details without server-side rebuild.
   When force is false (background poll) we skip re-rendering if nothing changed,
   so interaction animations never replay and the DOM stays stable. */
async function softRefresh() {
  await loadSnapshot();
  state.revision = state.snapshot.revision;
  await refreshOpenLogs();
  render();
}

/* ---------------- Modals ---------------- */
function closeModal() { $("modalOverlay").hidden = true; $("modal").innerHTML = ""; }
$("modalOverlay").addEventListener("click", (e) => { if (e.target === $("modalOverlay")) closeModal(); });
document.addEventListener("keydown", (e) => { if (e.key === "Escape") closeModal(); });

function showModal(html) { $("modal").innerHTML = html; $("modalOverlay").hidden = false; }

async function openExplorer(path) {
  try { await api.post("/api/fs/reveal", { path }); }
  catch (err) { toast("Open failed", err.message, "err"); }
}

/* Server-backed folder picker. Resolves to a path string, or null if cancelled. */
function browseFolder(start) {
  return new Promise((resolve) => {
    const ov = document.createElement("div");
    ov.className = "modal-overlay";
    document.body.appendChild(ov);
    let cur = start || "";
    const done = (val) => { ov.remove(); resolve(val); };
    ov.addEventListener("click", (e) => { if (e.target === ov) done(null); });

    async function load(path) {
      let data;
      try { data = await api.get("/api/fs" + (path ? `?path=${encodeURIComponent(path)}` : "")); }
      catch (err) { toast("Browse failed", err.message, "err"); return; }
      cur = data.path || "";
      paint(data);
    }
    function paint(data) {
      const rows = data.entries.length
        ? data.entries.map((en) => `
            <div class="fs-row" data-path="${esc(en.path)}">
              <span class="fs-ico">${en.isRepo ? "🌿" : "📁"}</span>
              <span class="fs-name">${esc(en.name)}</span>
              ${en.isRepo ? `<span class="badge badge-repo">repo</span>` : ""}
              <button class="btn btn-ghost btn-sm fs-pick" data-pick="${esc(en.path)}">select</button>
            </div>`).join("")
        : `<div class="fs-empty">No subfolders here</div>`;
      ov.innerHTML = `
        <div class="modal" style="width:560px">
          <h3>Select a folder</h3>
          <div class="fs-bar">
            <button class="btn btn-ghost btn-sm" id="fsUp" ${data.isRoot ? "disabled" : ""}>↑ Up</button>
            <button class="btn btn-ghost btn-sm" id="fsRoot">This PC</button>
            <span class="fs-cur">${data.isRoot ? "This PC — drives" : esc(cur)}</span>
          </div>
          <div class="fs-list">${rows}</div>
          <div class="modal-actions">
            <button class="btn btn-ghost" id="fsCancel">Cancel</button>
            <button class="btn btn-primary" id="fsUse" ${data.isRoot ? "disabled" : ""}>Select this folder</button>
          </div>
        </div>`;
      ov.querySelector("#fsCancel").onclick = () => done(null);
      ov.querySelector("#fsUse").onclick = () => done(cur);
      ov.querySelector("#fsRoot").onclick = () => load("");
      ov.querySelector("#fsUp").onclick = () => load(data.parent || "");
      ov.querySelectorAll(".fs-row").forEach((row) => {
        row.onclick = (e) => { if (e.target.closest(".fs-pick")) return; load(row.dataset.path); };
      });
      ov.querySelectorAll(".fs-pick").forEach((b) => { b.onclick = (e) => { e.stopPropagation(); done(b.dataset.pick); }; });
    }
    load(cur);
  });
}

function findRepo(id) {
  for (const node of state.snapshot?.sources || [])
    for (const repo of node.repositories) if (repo.id === id) return repo;
  return null;
}

/* Branches popup: search, local/remote filter, pagination, checkout/delete. Stands on its own overlay. */
function openBranchesModal(repoId) {
  const repo = findRepo(repoId);
  if (!repo) return;
  const PAGE = 10;
  let all = [], query = "", filter = "all", page = 1;

  const ov = document.createElement("div");
  ov.className = "modal-overlay";
  document.body.appendChild(ov);
  const close = () => { ov.remove(); document.removeEventListener("keydown", onKey); };
  const onKey = (e) => { if (e.key === "Escape") close(); };
  document.addEventListener("keydown", onKey);
  ov.addEventListener("click", (e) => { if (e.target === ov) close(); });

  ov.innerHTML = `
    <div class="modal" style="width:640px">
      <div class="bx-head">
        <h3>Branches — ${esc(repo.name)}</h3>
        <button class="btn btn-ghost btn-sm" id="bxClose">✕ Close</button>
      </div>
      <div class="bx-tools">
        <input class="bx-search" type="text" id="bxSearch" placeholder="Search branches…" autofocus />
        <div class="filter-chips" id="bxFilters"></div>
      </div>
      <div class="bx-list" id="bxList"><div class="bx-empty">Loading…</div></div>
      <div class="bx-foot" id="bxFoot"></div>
    </div>`;

  const $$ = (id) => ov.querySelector(id);
  $$("#bxClose").onclick = close;
  const search = $$("#bxSearch");
  search.oninput = () => { query = search.value; page = 1; paint(); };

  function filtered() {
    const q = query.toLowerCase();
    return all.filter((b) =>
      (filter === "all" || (filter === "local" && !b.isRemote) || (filter === "remote" && b.isRemote)) &&
      b.name.toLowerCase().includes(q));
  }

  function paintFilters() {
    const local = all.filter((b) => !b.isRemote).length;
    const remote = all.filter((b) => b.isRemote).length;
    const defs = [["all", "All", all.length], ["local", "Local", local], ["remote", "Remote", remote]];
    $$("#bxFilters").innerHTML = defs.map(([f, label, n]) =>
      `<button class="fchip ${filter === f ? "active" : ""}" data-f="${f}">${label} ${n}</button>`).join("");
    ov.querySelectorAll(".fchip").forEach((c) => (c.onclick = () => { filter = c.dataset.f; page = 1; paint(); }));
  }

  function paint() {
    paintFilters();
    const f = filtered();
    const pages = Math.max(1, Math.ceil(f.length / PAGE));
    if (page > pages) page = pages;
    const slice = f.slice((page - 1) * PAGE, page * PAGE);

    $$("#bxList").innerHTML = slice.length ? slice.map((b) => {
      const base = b.upstream ? `base: ${esc(b.upstream)}` : (b.isRemote ? "remote-tracking branch" : "no upstream");
      const ab = (b.ahead || b.behind) ? ` · ↑${b.ahead} ↓${b.behind}` : "";
      const when = b.lastCommitDate ? ` · ${relTime(b.lastCommitDate)}` : "";
      const subj = b.lastCommitSubject ? ` · ${esc(b.lastCommitSubject)}` : "";
      return `
        <div class="brow">
          <div class="brow-main">
            <div class="bname ${b.isRemote ? "remote" : "local"}">
              ${b.isCurrent ? `<span class="tag-main">current</span> ` : ""}${b.isRemote ? "🌐 " : "⎇ "}${esc(b.name)}
            </div>
            <div class="bsub">${base}${ab}${when}${subj}</div>
          </div>
          <div class="brow-actions">
            ${b.isRemote ? "" : `<button class="btn btn-ghost btn-sm" data-co="${esc(b.name)}">checkout</button>`}
            ${(b.isCurrent || b.isRemote) ? "" : `<button class="btn btn-danger btn-sm" data-del="${esc(b.name)}">delete</button>`}
          </div>
        </div>`;
    }).join("") : `<div class="bx-empty">No matching branches.</div>`;

    $$("#bxFoot").innerHTML = `
      <span class="muted">${f.length} branch${f.length === 1 ? "" : "es"}</span>
      <div class="pager">
        <button class="btn btn-ghost btn-sm" id="bxPrev" ${page <= 1 ? "disabled" : ""}>← Prev</button>
        <span>Page ${page} / ${pages}</span>
        <button class="btn btn-ghost btn-sm" id="bxNext" ${page >= pages ? "disabled" : ""}>Next →</button>
      </div>`;
    $$("#bxPrev").onclick = () => { if (page > 1) { page--; paint(); } };
    $$("#bxNext").onclick = () => { if (page < pages) { page++; paint(); } };
    ov.querySelectorAll("[data-co]").forEach((btn) => (btn.onclick = () => doCheckout(btn.dataset.co)));
    ov.querySelectorAll("[data-del]").forEach((btn) => (btn.onclick = () => doDelete(btn.dataset.del)));
  }

  async function reload() {
    try { all = await api.get(`/api/repos/${repoId}/branches`); state.branchCache.set(repoId, all); }
    catch { all = []; }
    paint();
  }
  async function doCheckout(branch) {
    try {
      const r = await api.post(`/api/repos/${repoId}/checkout`, { worktree: repo.path, branch });
      reportOp(`Checkout ${branch}`, r);
      await softRefresh(); await reload();
    } catch (err) { toast("Checkout failed", err.message, "err"); }
  }
  function doDelete(branch) {
    destructive(`/api/repos/${repoId}/branch/delete`, { name: branch, force: false }, `Delete branch ${branch}`, repoId, reload);
  }

  reload();
}

function openAddSource() {
  showModal(`
    <h3>Add source</h3>
    <p>Track a single repository, or a folder that Treeline scans for repositories.</p>
    <div class="seg">
      <button class="btn active" id="typeRepo">Repository</button>
      <button class="btn" id="typeFolder">Folder (scan)</button>
    </div>
    <label>Path</label>
    <div class="input-row">
      <input type="text" id="srcPath" placeholder="C:\\Users\\you\\Code\\my-project" autofocus />
      <button class="btn btn-ghost" id="srcBrowse" type="button">Browse…</button>
    </div>
    <label>Display name (optional)</label>
    <input type="text" id="srcName" placeholder="My project" />
    <div id="depthWrap" style="display:none">
      <label>Scan depth (folder levels)</label>
      <input type="number" id="srcDepth" value="3" min="1" max="8" />
    </div>
    <div class="field-err" id="srcErr"></div>
    <div class="modal-actions">
      <button class="btn btn-ghost" data-close>Cancel</button>
      <button class="btn btn-primary" id="srcSubmit">Add</button>
    </div>`);
  let type = "repo";
  const depthWrap = $("depthWrap");
  $("typeRepo").onclick = () => { type = "repo"; $("typeRepo").classList.add("active"); $("typeFolder").classList.remove("active"); depthWrap.style.display = "none"; };
  $("typeFolder").onclick = () => { type = "folder"; $("typeFolder").classList.add("active"); $("typeRepo").classList.remove("active"); depthWrap.style.display = "block"; };
  $("srcBrowse").onclick = async () => { const p = await browseFolder($("srcPath").value.trim()); if (p) $("srcPath").value = p; };
  wireClose();
  $("srcSubmit").onclick = async () => {
    const path = $("srcPath").value.trim();
    if (!path) { $("srcErr").textContent = "Path is required."; return; }
    try {
      const body = { path, type, displayName: $("srcName").value.trim() || null, scanDepth: Number($("srcDepth").value) || 3 };
      const s = await api.post("/api/sources", body);
      closeModal();
      toast("Source added", sourceName(s));
      await softRefresh();
    } catch (err) { $("srcErr").textContent = err.message; }
  };
}

function openCreateBranch(repoId) {
  showModal(`
    <h3>Create branch</h3>
    <label>Branch name</label>
    <input type="text" id="bName" placeholder="feature/my-change" autofocus />
    <label>Start point (optional, e.g. main or a SHA)</label>
    <input type="text" id="bFrom" placeholder="HEAD" />
    <div class="field-err" id="bErr"></div>
    <div class="modal-actions">
      <button class="btn btn-ghost" data-close>Cancel</button>
      <button class="btn btn-primary" id="bSubmit">Create</button>
    </div>`);
  wireClose();
  $("bSubmit").onclick = async () => {
    const name = $("bName").value.trim();
    if (!name) { $("bErr").textContent = "Name is required."; return; }
    try {
      const r = await api.post(`/api/repos/${repoId}/branch`, { name, from: $("bFrom").value.trim() || null });
      closeModal(); reportOp("Create branch", r); state.branchCache.delete(repoId); await softRefresh();
    } catch (err) { $("bErr").textContent = err.message; }
  };
}

function openAddWorktree(repoId) {
  showModal(`
    <h3>Add worktree</h3>
    <p>Create a new linked working directory for this repository.</p>
    <label>New worktree path</label>
    <div class="input-row">
      <input type="text" id="wPath" placeholder="C:\\Users\\you\\Code\\my-project-feature" autofocus />
      <button class="btn btn-ghost" id="wBrowse" type="button">Browse…</button>
    </div>
    <label>Branch</label>
    <input type="text" id="wBranch" placeholder="feature/my-change" />
    <label class="confirm-check"><input type="checkbox" id="wNew" /> Create this branch (otherwise check out an existing one)</label>
    <div class="field-err" id="wErr"></div>
    <div class="modal-actions">
      <button class="btn btn-ghost" data-close>Cancel</button>
      <button class="btn btn-primary" id="wSubmit">Add worktree</button>
    </div>`);
  // For a new worktree the path must not exist yet, so Browse picks the parent folder.
  $("wBrowse").onclick = async () => { const p = await browseFolder($("wPath").value.trim()); if (p) { $("wPath").value = p.replace(/[\\/]+$/, "") + "\\"; $("wPath").focus(); } };
  wireClose();
  $("wSubmit").onclick = async () => {
    const path = $("wPath").value.trim();
    if (!path) { $("wErr").textContent = "Path is required."; return; }
    try {
      const body = { path, branch: $("wBranch").value.trim() || null, createBranch: $("wNew").checked };
      const r = await api.post(`/api/repos/${repoId}/worktree`, body);
      closeModal(); reportOp("Add worktree", r); state.branchCache.delete(repoId); await softRefresh();
    } catch (err) { $("wErr").textContent = err.message; }
  };
}

/* Double-confirm modal: explicit checkbox + button (server already required a token). */
function confirmModal(title, summary, onConfirm, opts = {}) {
  const soft = opts.soft === true;
  showModal(`
    <h3>${esc(title)}</h3>
    <div class="danger-box">${esc(summary)}</div>
    ${soft ? "" : `<label class="confirm-check"><input type="checkbox" id="cChk" /> I understand this action is permanent and cannot be undone.</label>`}
    <div class="modal-actions">
      <button class="btn btn-ghost" data-close>Cancel</button>
      <button class="btn ${soft ? "btn-primary" : "btn-danger"}" id="cGo" ${soft ? "" : "disabled"}>${soft ? "Confirm" : "Yes, do it"}</button>
    </div>`);
  wireClose();
  if (!soft) $("cChk").onchange = (e) => { $("cGo").disabled = !e.target.checked; };
  $("cGo").onclick = async () => {
    $("cGo").disabled = true;
    try { await onConfirm(); closeModal(); }
    catch (err) { closeModal(); toast(`${title} failed`, err.message, "err"); }
  };
}

function wireClose() { $("modal").querySelectorAll("[data-close]").forEach((b) => (b.onclick = closeModal)); }

/* ---------------- Toolbar wiring ---------------- */
$("addSourceBtn").onclick = openAddSource;
$("refreshAllBtn").onclick = (e) => withSpin(e.currentTarget, refreshAll);
$("autoRefresh").checked = state.autoRefresh;
$("autoRefresh").onchange = (e) => { state.autoRefresh = e.target.checked; persist(); };
$("themeBtn").onclick = () => { state.theme = state.theme === "dark" ? "light" : "dark"; document.documentElement.dataset.theme = state.theme; persist(); };
document.documentElement.dataset.theme = state.theme;

/* ---------------- Boot + polling ---------------- */
/* Poll a tiny revision endpoint; only pull the full snapshot when it actually changed.
   Skips entirely when auto-refresh is off or the tab is hidden. */
async function tick() {
  if (!state.autoRefresh || document.hidden) return;
  try {
    const { revision, generatedAt } = await api.get("/api/snapshot/revision");
    if (revision !== state.revision) await softRefresh();
    else if (generatedAt) $("updated").textContent = relTime(generatedAt); // keep freshness label live
  } catch { /* transient */ }
}

document.addEventListener("visibilitychange", () => { if (!document.hidden) tick(); });

(async function boot() {
  await loadHealth();
  try {
    // Force one immediate build so data shows even if the background loop was idle.
    state.snapshot = await api.post("/api/refresh", null);
    state.revision = state.snapshot.revision;
  } catch (err) {
    $("content").innerHTML = `<div class="error-banner">Could not reach Treeline server: ${esc(err.message)}</div>`;
    return;
  }
  await Promise.all([refreshOpenLogs(), reloadOpenBranches()]);
  render();
  setInterval(tick, 10000);
})();
