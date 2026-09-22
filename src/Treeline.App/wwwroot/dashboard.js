"use strict";

/* ---------------- Dashboard ----------------
   The landing view: one card per project, ordered by what needs attention.
   It reads the same snapshot app.js renders as a tree - the tree keeps its own
   tab and none of its behaviour changes. Only the newest commit per project is
   fetched separately, because the snapshot carries no commit data. */

const dash = {
  query: "",
  scope: localStorage.getItem("tl.dash.scope") || "all",     // all | attention | pinned
  sort: localStorage.getItem("tl.dash.sort") || "recent",    // recent | attention | name
  pins: new Set(JSON.parse(localStorage.getItem("tl.dash.pins") || "[]")),
  heads: new Map(),        // repoId -> { sha, commit }
  headsInFlight: false,
};

function dashPersist() {
  localStorage.setItem("tl.dash.scope", dash.scope);
  localStorage.setItem("tl.dash.sort", dash.sort);
  localStorage.setItem("tl.dash.pins", JSON.stringify([...dash.pins]));
}

/* ---------------- Model ---------------- */

/** Folds the worktrees of a repository into the handful of numbers a card shows. */
function dashSummary(repo) {
  const sum = { staged: 0, modified: 0, untracked: 0, conflicted: 0, ahead: 0, behind: 0, missing: 0, prunable: 0, locked: 0 };
  for (const wt of repo.worktrees || []) {
    const st = wt.status || {};
    sum.staged += st.staged || 0;
    sum.modified += st.modified || 0;
    sum.untracked += st.untracked || 0;
    sum.conflicted += st.conflicted || 0;
    sum.ahead += wt.ahead || 0;
    sum.behind += wt.behind || 0;
    if (!wt.exists) sum.missing++;
    if (wt.isPrunable) sum.prunable++;
    if (wt.isLocked) sum.locked++;
  }
  sum.dirty = sum.staged + sum.modified + sum.untracked + sum.conflicted;
  return sum;
}

/** Why a project is asking for something, worst first. Empty means nothing to do. */
function dashAttention(repo, sum) {
  const out = [];
  const plural = (n, word) => `${n} ${word}${n === 1 ? "" : "s"}`;
  if (!repo.isValid) out.push({ kind: "bad", text: repo.error || "invalid repository" });
  if (sum.conflicted) out.push({ kind: "bad", text: plural(sum.conflicted, "conflict") });
  if (sum.missing) out.push({ kind: "bad", text: `${plural(sum.missing, "worktree")} missing` });
  if (sum.prunable) out.push({ kind: "warn", text: `${plural(sum.prunable, "worktree")} prunable` });
  if (sum.behind) out.push({ kind: "warn", text: `${plural(sum.behind, "commit")} behind` });
  if (sum.dirty) out.push({ kind: "warn", text: `${plural(sum.dirty, "change")} uncommitted` });
  if (sum.ahead) out.push({ kind: "info", text: `${plural(sum.ahead, "commit")} unpushed` });
  return out;
}

/** Ranks by the worst reason, so the loudest project floats up inside its group. */
function dashWeight(p) {
  const worst = p.attention[0];
  return worst ? { bad: 3, warn: 2, info: 1 }[worst.kind] : 0;
}

/** When a project last moved. 0 until its log has loaded, which sorts it last. */
function dashRecency(p) {
  const commit = dash.heads.get(p.repo.id)?.commit;
  return commit ? new Date(commit.date).getTime() : 0;
}

/** Pins always lead; below them the chosen sort decides, with name as the tiebreak. */
function dashCompare(a, b) {
  if (a.pinned !== b.pinned) return Number(b.pinned) - Number(a.pinned);
  if (dash.sort === "recent") {
    const byDate = dashRecency(b) - dashRecency(a);
    if (byDate) return byDate;
  } else if (dash.sort === "attention") {
    const byWeight = dashWeight(b) - dashWeight(a);
    if (byWeight) return byWeight;
  }
  return a.repo.name.localeCompare(b.repo.name, undefined, { sensitivity: "base" });
}

function dashProjects() {
  const list = [];
  for (const node of state.snapshot?.sources || []) {
    for (const repo of node.repositories) {
      const sum = dashSummary(repo);
      list.push({ repo, source: node.source, sum, attention: dashAttention(repo, sum), pinned: dash.pins.has(repo.id) });
    }
  }
  return list.sort(dashCompare);
}

function dashVisible(list) {
  const q = dash.query.trim().toLowerCase();
  return list.filter((p) => {
    if (dash.scope === "attention" && p.attention.length === 0) return false;
    if (dash.scope === "pinned" && !p.pinned) return false;
    if (!q) return true;
    return `${p.repo.name} ${p.repo.currentBranch || ""} ${sourceName(p.source)} ${p.repo.path}`.toLowerCase().includes(q);
  });
}

/* ---------------- Rendering ---------------- */

function renderDashboard() {
  const host = $("dashboard");
  if (!host) return;
  const snap = state.snapshot;

  // The filter survives a background poll: remember focus and caret across the rebuild.
  const search = $("dashSearch");
  const hadFocus = search && document.activeElement === search;
  const caret = search ? search.selectionStart : 0;

  if (!snap || snap.sources.length === 0) {
    host.innerHTML = dashFirstRun();
    enhanceButtons(host);
    return;
  }

  const all = dashProjects();
  const needing = all.filter((p) => p.attention.length > 0);
  const shown = dashVisible(all);
  host.innerHTML =
    dashSummaryStrip(all, needing) +
    dashTools(all, needing) +
    (shown.length
      ? `<div class="dash-grid">${shown.map(dashCard).join("")}</div>`
      : `<div class="empty">Nothing matches that filter.</div>`);

  enhanceButtons(host);
  const input = $("dashSearch");
  if (input) {
    input.oninput = (e) => { dash.query = e.target.value; renderDashboard(); };
    if (hadFocus) { input.focus(); input.setSelectionRange(caret, caret); }
  }
  const sort = $("dashSort");
  if (sort) sort.onchange = (e) => { dash.sort = e.target.value; dashPersist(); renderDashboard(); };
  dashLoadHeads();
}

function dashSummaryStrip(all, needing) {
  const trees = all.reduce((n, p) => n + (p.repo.worktreeCount || 0), 0);
  const dirty = all.filter((p) => p.sum.dirty).length;
  const drift = all.filter((p) => p.sum.ahead || p.sum.behind).length;
  const stat = (value, label, hot) => `<div class="dash-stat${hot ? " hot" : ""}"><b>${value}</b><span>${label}</span></div>`;
  return `<section class="dash-summary">
    ${stat(all.length, all.length === 1 ? "project" : "projects")}
    ${stat(trees, trees === 1 ? "worktree" : "worktrees")}
    ${stat(needing.length, "need attention", needing.length > 0)}
    ${stat(dirty, "with changes")}
    ${stat(drift, "out of sync")}
  </section>`;
}

function dashTools(all, needing) {
  const chip = (key, label, count) =>
    `<button class="fchip${dash.scope === key ? " active" : ""}" data-action="dash-scope" data-scope="${key}">${label} <b>${count}</b></button>`;
  const option = (value, label) =>
    `<option value="${value}"${dash.sort === value ? " selected" : ""}>${label}</option>`;
  return `<div class="dash-tools">
    <input type="text" id="dashSearch" class="dash-search" placeholder="Filter by project, branch or path…" value="${esc(dash.query)}" />
    <div class="filter-chips">
      ${chip("all", "All", all.length)}
      ${chip("attention", "Needs attention", needing.length)}
      ${chip("pinned", "Pinned", dash.pins.size)}
    </div>
    <label class="dash-sort"><span class="k">sort</span>
      <select id="dashSort" class="mini" title="What decides the order of the grid">
        ${option("recent", "Latest commit")}
        ${option("attention", "Needs attention")}
        ${option("name", "Name")}
      </select>
    </label>
  </div>`;
}

function dashCard(p) {
  const { repo, sum } = p;
  const trees = repo.worktrees || [];
  const main = trees.find((w) => w.isMain) || trees[0] || null;
  const worst = p.attention[0];

  const chips = [];
  if (sum.conflicted) chips.push(`<span class="chip chip-bad">!${sum.conflicted} conflict</span>`);
  if (sum.staged) chips.push(`<span class="chip chip-staged">+${sum.staged} staged</span>`);
  if (sum.modified) chips.push(`<span class="chip chip-dirty">~${sum.modified} modified</span>`);
  if (sum.untracked) chips.push(`<span class="chip">?${sum.untracked} new</span>`);
  if (repo.isValid && !sum.dirty) chips.push(`<span class="chip chip-clean"><span class="dot green"></span>clean</span>`);
  if (sum.ahead || sum.behind) chips.push(`<span class="chip chip-ab">↑${sum.ahead} ↓${sum.behind}</span>`);
  if (sum.missing) chips.push(`<span class="chip chip-bad">${sum.missing} missing</span>`);

  const branch = repo.currentBranch
    ? `<span class="chip chip-branch" title="${esc(repo.currentBranch)}">⎇ ${esc(trunc(repo.currentBranch, 24))}</span>`
    : `<span class="chip">detached</span>`;

  return `<article class="dash-card${worst ? ` attn-${worst.kind}` : ""}${p.pinned ? " pinned" : ""}" data-repo="${esc(repo.id)}">
    <header class="dash-card-head">
      <button class="dash-pin${p.pinned ? " on" : ""}" type="button" data-action="dash-pin" data-id="${esc(repo.id)}"
              title="${p.pinned ? "Unpin" : "Pin to the top"}">${p.pinned ? "★" : "☆"}</button>
      <span class="dash-name" title="${esc(repo.path)}">${esc(repo.name)}</span>
      <span class="dash-source" title="${esc(p.source.path)}">${esc(sourceName(p.source))}</span>
    </header>
    <div class="dash-line">${branch}<span class="chip">${repo.worktreeCount} tree${repo.worktreeCount === 1 ? "" : "s"}</span></div>
    <div class="dash-line">${chips.join("")}</div>
    <div class="dash-commit" data-commit="${esc(repo.id)}">${dashCommitLine(repo.id)}</div>
    ${worst ? `<div class="dash-attn">${p.attention.map((a) => `<span class="attn-item"><span class="attn-dot ${a.kind}"></span>${esc(a.text)}</span>`).join("")}</div>` : ""}
    <footer class="dash-card-actions">
      <button class="btn btn-ghost btn-sm" data-action="fetch" data-id="${esc(repo.id)}" title="git fetch --all">fetch</button>
      ${main ? `<button class="btn btn-ghost btn-sm" data-action="pull" data-id="${esc(repo.id)}" data-wt="${esc(main.path)}" title="git pull --ff-only">pull</button>` : ""}
      <button class="btn btn-ghost btn-icon btn-sm" data-action="reveal" data-path="${esc(repo.path)}" title="Open in Explorer">📂</button>
      <button class="btn btn-ghost btn-icon btn-sm" data-action="refresh-repo" data-id="${esc(repo.id)}" title="Refresh this project"><span class="ico-refresh">↻</span></button>
      <button class="btn btn-ghost btn-sm dash-open" data-action="dash-open" data-id="${esc(repo.id)}" title="Show this project in the tree">worktrees →</button>
    </footer>
  </article>`;
}

function dashCommitLine(repoId) {
  const entry = dash.heads.get(repoId);
  if (!entry) return `<span class="muted">loading…</span>`;
  if (!entry.commit) return `<span class="muted">no commits</span>`;
  const c = entry.commit;
  return `<span class="commit-sha">${esc(c.shortSha)}</span>` +
         `<span class="dash-commit-msg" title="${esc(c.subject)}">${esc(c.subject)}</span>` +
         `<span class="dash-commit-age">${esc(relTime(c.date))}</span>`;
}

function dashFirstRun() {
  return `<section class="dash-firstrun">
    <span class="motion-lab-kicker">First run</span>
    <h1>Nothing tracked yet</h1>
    <p>Treeline keeps a live view of the git repositories on this machine - branch, worktrees,
       what is dirty and what is behind. Point it at the folder your projects live in and every
       repository under it becomes a card here.</p>
    <div class="dash-firstrun-actions">
      <button class="btn btn-primary" data-action="add-source">+ Add your first source</button>
    </div>
    <ul class="dash-firstrun-hints">
      <li><b>Folder</b> - scanned a few levels deep; every repository under it shows up.</li>
      <li><b>Repository</b> - a single checkout, with its linked worktrees grouped under it.</li>
    </ul>
  </section>`;
}

/* ---------------- Newest commit per project ----------------
   One `git log -1` per project, only when its head moved, four at a time so that a
   folder source holding dozens of repos cannot flood git. Each result patches its own
   line instead of re-rendering, so a background load never disturbs the page. */
async function dashLoadHeads() {
  if (dash.headsInFlight) return;
  const jobs = [];
  for (const node of state.snapshot?.sources || []) {
    for (const repo of node.repositories) {
      if (!repo.isValid) continue;
      const trees = repo.worktrees || [];
      const main = trees.find((w) => w.isMain) || trees[0];
      if (!main || !main.exists || !main.head) continue;
      if (dash.heads.get(repo.id)?.sha === main.head) continue;
      jobs.push({ id: repo.id, path: main.path, sha: main.head });
    }
  }
  if (jobs.length === 0) return;

  dash.headsInFlight = true;
  try {
    const queue = jobs.slice();
    const worker = async () => {
      while (queue.length) {
        const job = queue.shift();
        let commit = null;
        try {
          const commits = await api.get(`/api/repos/${job.id}/log?worktree=${encodeURIComponent(job.path)}&skip=0&take=1`);
          commit = commits[0] || null;
        } catch { /* an unreadable log leaves the line empty rather than failing the view */ }
        dash.heads.set(job.id, { sha: job.sha, commit });
        dashPaintCommit(job.id);
      }
    };
    await Promise.all(Array.from({ length: Math.min(4, queue.length) }, worker));
  } finally {
    dash.headsInFlight = false;
  }
  // Recency is only known once the logs land, so settle the order once they have.
  // The re-render calls back in here, but by then there is nothing left to fetch.
  if (dash.sort === "recent") renderDashboard();
}

function dashPaintCommit(repoId) {
  const node = [...document.querySelectorAll("[data-commit]")].find((el) => el.dataset.commit === repoId);
  if (node) node.innerHTML = dashCommitLine(repoId);
}

/* ---------------- Events ---------------- */
$("dashboard").addEventListener("click", (e) => {
  const t = e.target.closest("[data-action]");
  if (!t) return;
  const { action, id, wt, scope } = t.dataset;
  e.stopPropagation();
  switch (action) {
    case "add-source": return openAddSource();
    case "reveal": return openExplorer(t.dataset.path);
    case "fetch": return withSpin(t, () => gitOp(id, `/api/repos/${id}/fetch`, null, "Fetch"));
    case "pull": return gitOp(id, `/api/repos/${id}/pull`, { worktree: wt }, "Pull");
    case "refresh-repo": return withSpin(t, () => refreshRepo(id));
    case "dash-pin": return dashTogglePin(id);
    case "dash-scope": return dashSetScope(scope);
    case "dash-open": return dashOpenInTree(id);
  }
});

function dashTogglePin(id) {
  dash.pins.has(id) ? dash.pins.delete(id) : dash.pins.add(id);
  dashPersist();
  renderDashboard();
}

function dashSetScope(scope) {
  dash.scope = scope;
  dashPersist();
  renderDashboard();
}

/** Hands a project over to the tree tab, expanded and scrolled into view. */
function dashOpenInTree(id) {
  if (!state.openRepos.has(id)) {
    state.openRepos.add(id);
    persist();
  }
  setView("home");
  render();
  const node = [...document.querySelectorAll(".repo")].find((el) => el.dataset.repo === id);
  if (node) node.scrollIntoView({ behavior: "smooth", block: "center" });
}
