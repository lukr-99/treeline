/* About page. Author name, links and credits live in this one constant. */
const ABOUT = {
  author: "Lukáš Krejčí",
  instagram: { url: "https://www.instagram.com/lukas9krejci/", handle: "@lukas9krejci" },
  github: { url: "https://github.com/lukr-99", handle: "lukr-99" },
  issues: { url: "https://github.com/lukr-99/treeline/issues", handle: "Issues" },
  licence: { name: "PolyForm Noncommercial 1.0.0", url: "https://polyformproject.org/licenses/noncommercial/1.0.0" },
  credits: [
    { name: "Treeline licence", what: "PolyForm Noncommercial 1.0.0. Free for personal and other noncommercial use.", link: "https://polyformproject.org/licenses/noncommercial/1.0.0" },
    { name: ".NET and ASP.NET Core", what: "Runtime and local web server. MIT licence.", link: "https://github.com/dotnet/runtime" },
    { name: "DotNetLib.Core", what: "The author's own shared library, used for the in-app updater.", link: "https://github.com/lukr-99" },
    { name: "Git", what: "Treeline runs the git on your PATH. It is not bundled. GPL v2.", link: "https://git-scm.com/" },
    { name: "Type and icons", what: "System fonts only (Segoe UI, Cascadia, Consolas). The tree logo is original to Treeline.", link: null },
  ],
};

const ABOUT_ICONS = {
  instagram: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="3" width="18" height="18" rx="5" fill="none" stroke="currentColor" stroke-width="2"/><circle cx="12" cy="12" r="4" fill="none" stroke="currentColor" stroke-width="2"/><circle cx="17.3" cy="6.7" r="1.2" fill="currentColor"/></svg>',
  github: '<svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M12 2a10 10 0 0 0-3.16 19.49c.5.09.68-.22.68-.48v-1.7c-2.78.6-3.37-1.34-3.37-1.34-.45-1.15-1.11-1.46-1.11-1.46-.9-.62.07-.6.07-.6 1 .07 1.53 1.03 1.53 1.03.9 1.52 2.34 1.08 2.9.83.09-.64.35-1.08.63-1.33-2.22-.25-4.55-1.11-4.55-4.94 0-1.09.39-1.98 1.03-2.68-.1-.25-.45-1.27.1-2.64 0 0 .84-.27 2.75 1.02a9.5 9.5 0 0 1 5 0c1.91-1.29 2.75-1.02 2.75-1.02.55 1.37.2 2.39.1 2.64.64.7 1.03 1.59 1.03 2.68 0 3.84-2.34 4.69-4.57 4.94.36.31.68.92.68 1.86v2.76c0 .27.18.58.69.48A10 10 0 0 0 12 2Z"/></svg>',
  issues: '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="9" fill="none" stroke="currentColor" stroke-width="2"/><path d="M12 7v6" stroke="currentColor" stroke-width="2" stroke-linecap="round"/><circle cx="12" cy="16.5" r="1.2" fill="currentColor"/></svg>',
};

function renderAbout() {
  const root = document.getElementById("aboutRoot");
  if (!root) return;
  const version = (typeof state !== "undefined" && state.health && state.health.version) || "";
  const link = (key, label) => {
    const l = ABOUT[key];
    return `<a class="about-link" href="${esc(l.url)}" target="_blank" rel="noopener noreferrer">
      <span class="about-link-ico">${ABOUT_ICONS[key]}</span>
      <span class="about-link-text"><span class="about-link-label">${esc(label)}</span><span class="about-link-handle">${esc(l.handle)}</span></span>
    </a>`;
  };
  const credit = (c) => `<li class="about-credit">
      <span class="about-credit-name">${c.link ? `<a href="${esc(c.link)}" target="_blank" rel="noopener noreferrer">${esc(c.name)}</a>` : esc(c.name)}</span>
      <span class="about-credit-what">${esc(c.what)}</span></li>`;
  root.innerHTML = `
    <section class="about-hero">
      <div class="about-hero-mark">${document.querySelector(".brand-mark").outerHTML}</div>
      <h1>Treeline</h1>
      <p class="about-tagline">A local dashboard for the git repositories and worktrees on your machine.</p>
      <div class="about-chips">
        <span class="about-chip">${version ? `Version ${esc(version)}` : "Version unknown"}</span>
        <span class="about-chip">Windows</span>
        <span class="about-chip">Loopback only</span>
      </div>
    </section>

    <section class="about-author" aria-label="Author">
      <div class="about-author-glow" aria-hidden="true"></div>
      <span class="about-kicker">Made by</span>
      <h2>${esc(ABOUT.author)}</h2>
      <p>Feedback is welcome: what works, what does not, what is missing.</p>
      <div class="about-links">
        ${link("instagram", "Instagram")}
        ${link("github", "GitHub")}
        ${link("issues", "Report an issue")}
      </div>
    </section>

    <section class="about-facts" aria-label="Facts">
      <div class="about-fact"><span class="about-fact-k">What it does</span><span class="about-fact-v">Tracks repos, worktrees, branches and status from one place</span></div>
      <div class="about-fact"><span class="about-fact-k">Where it runs</span><span class="about-fact-v">Tray app with a web UI on localhost</span></div>
      <div class="about-fact"><span class="about-fact-k">Your data</span><span class="about-fact-v">Stays on this machine, no telemetry</span></div>
    </section>

    <section class="about-credits" aria-label="Credits and licences">
      <h3>Credits and licences</h3>
      <ul>${ABOUT.credits.map(credit).join("")}</ul>
      <p class="about-copy">Copyright 2026 ${esc(ABOUT.author)}</p>
    </section>`;
}
