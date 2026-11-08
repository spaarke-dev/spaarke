# Research: how visuals can be presented on Claude surfaces

> **Date:** 2026-11-07 · **Author:** researcher subagent · **Project:** `spaarke-legal-ai-accelerator-claude-r2`
> **Scope:** every visual mechanism on Claude, what each needs (server or not), interactivity and security limits,
> overlap with Microsoft 365 Copilot / Cowork, and a v1 ("email a zip") vs v2 (hosted Spaarke MCP connector) recommendation.
> **Labels:** **[C]** Cited (primary Anthropic / MCP source says it) · **[S]** Secondary (third-party coverage only) ·
> **[I]** Inferred (our reasoning from cited facts) · **[U]** Unknown (no source; a test settles it).
> **Companion note:** `claude-install-and-runtime.md` (same folder) covers install paths, scripts, profile and data terms.
> **Microsoft side (not redone):** r1 researcher notes `cowork-visual-outputs-2026-11-05`, `cowork-mcp-apps-author-guide-2026-10-30`,
> `mcp-server-dotnet-bff-copilot-widget-2026-11-05`.

---

## Summary (10 lines)

1. Claude has **seven visual channels**: chat artifacts (now template-based: Docs, Slides, Design, Dashboards beta, plus free-form pages), inline visuals in replies, Cowork artifacts (the old "live artifacts"), Claude Code published artifacts, MCP Apps, generated Office/PDF/HTML files, and in-document output in Claude for Word/Excel/PowerPoint. [C]
2. **Only two channels need no server and no Anthropic-hosted page:** generated **files** (.docx/.xlsx/.pdf/.html) and **Claude for Word in-document output** (tracked changes, comment replies, tables). Both are zip-shippable as skills + scripts/templates. [C/I]
3. **Artifacts need no server of ours**, but they live on Anthropic infrastructure (claude.ai) and are **Claude-only**. A skill can carry an HTML template in `assets/` and tell Claude to fill it; there is no template contract, so fidelity is best-effort. [C/I]
4. **MCP Apps can ship inside the zip** as a local stdio server (a `.mcpb` bundle referenced from the plugin's `mcpServers`), but then they render **only in Claude Desktop** (Desktop chat via the extension, and local Cowork sessions via the plugin). Not claude.ai web, mobile, cloud Cowork, the Office add-ins, or Microsoft Copilot. [C/I]
5. Claude Code calls MCP App tools **as text** and does not render the UI. The Desktop app's Code tab does render MCP App widgets. [C]
6. **Write once, run on both** is realistic for two things: **files** (Microsoft Cowork previews HTML/PDF/Office; Claude Desktop previews PDF/Word/Excel/PowerPoint) and **MCP Apps served by a remote HTTPS server** (same `ui://` HTML in Claude and in M365 Copilot/Cowork). That second one is the v2 path. [C/I]
7. **Claude is clearly stronger** at: artifacts (persistent, shareable, can call Claude and the viewer's connectors, 20 MB storage), inline visuals, Claude Code-published dashboards with live connector calls, and **Claude for Word running third-party skills** (Microsoft Word does not run Spaarke's plugin; r1 L21/L25). [C]
8. **v1 recommendation:** a Markdown table in chat for every output; an `.xlsx` and/or `.docx` as the deliverable; a **self-contained HTML report file** for the 16-NDA dashboard and the redline comparison; Claude for Word tracked changes for the redline. Artifacts as a Claude-only "nice view", driven by an `assets/` template. [I]
9. **v2 recommendation:** one single-file widget (React or vanilla, ext-apps SDK) served as `ui://` by the Spaarke BFF MCP endpoint, used by **both** Claude and M365 Copilot: review grid inline, library dashboard and redline viewer fullscreen. [I]
10. Biggest unknowns: whether a local `.mcpb` MCP App renders in a local Cowork task (cited pieces say yes, untested); whether Cowork's HTML file preview runs scripts on local sessions; whether skill scripts run in Claude for Word. Tests T1–T8 at the end. [U]

---

## Mechanism × surface matrix

Rows = mechanism. Cells: **Yes** / **No** / **Partial** / **?** (unknown). Footnote letters point to the details sections.

| Mechanism | claude.ai web chat | Desktop chat | Cowork (local, Desktop) | Cowork (cloud / web) | Mobile | Claude Code CLI | Desktop Code tab | Claude for Word / Excel / PPT | **Needs our server?** | **Zip-shippable?** |
|---|---|---|---|---|---|---|---|---|---|---|
| Chat artifact (free-form page) | Yes [C] | Yes [C] | Yes (since 2026-08-19, artifacts made in Cowork are ordinary artifacts) [C] | Yes [I] | View/edit; sharing on web/desktop only [C] | No (publishes instead, row 4) | — | No [I] | No (Anthropic-hosted) | Template in `assets/` + instructions [I] |
| Artifact templates: Docs, Slides, Design | Yes, all plans [C] | Yes [C] | Yes [I] | Yes [I] | Create/view/edit [C] | `/slides`, `/design`, Docs connector [C] | Yes [C] | No [I] | No | Prompt-driven only [I] |
| Dashboards (beta) | Paid plans; Enterprise off by default [C] | Yes [C] | Listed [C] | ? | ? | Listed [C] | ? | No | No, but needs **data connectors** (warehouse, CRM) [C] | No (data source is the point) [I] |
| Inline visuals in replies | Yes, all plans (beta) [C] | Yes [C] | Yes, paid plans (2026-04-22) [C] | ? | ? | [S] only | ? | No | No | Prompt-only; ephemeral [C] |
| Claude Code published artifact | — | — | — | — | — | Yes (Pro+; claude.ai login; Anthropic API only) [C] | Yes (Desktop ≥ 1.13576.0) [C] | — | No (Anthropic-hosted page) | A skill can tell Claude Code to publish one [C] |
| MCP App, **remote** HTTPS connector | Yes [C] | Yes [C] | Yes [C] | Yes [C] | Yes (WebView, connector added on web/desktop first) [C] | **No, text only** [C] | Yes (widgets in conversation) [C] | **?** [U] | **Yes** | `.mcp.json` URL entry only |
| MCP App, **local stdio / `.mcpb`** | **No** (plugin local servers ignored) [C] | Yes, via `claude_desktop_config.json` or installed `.mcpb` extension [C] | Loads in plugin [C]; UI render **?** (expected yes) [I/U] | Only through the Desktop app while open (sibling note) [C] | No [C] | Tools load, no UI [C] | ? | No [I] | **No** | **Yes**: plugin `mcpServers: "./servers/x.mcpb"` [C] |
| Generated files (.docx/.xlsx/.pptx/.pdf) | Yes: built-in Anthropic file skills; "Create and edit files" must be on [C] | Yes; inline previews in Files pane [C] | Yes; writes to the user's folder [C] | Yes [I] | Download [I] | Yes (scripts on the machine) [C] | Yes; Office previews [C] | n/a (works on the open doc) | No | **Yes**: skill + `scripts/` + `assets/` [C] |
| Generated HTML file | Download / preview [I] | Preview pane [C] | Preview pane; script execution **?** [U] | ? | ? | Opens in browser [I] | Preview pane runs inline scripts (SSH/WSL change implies local already did) [C/I] | No | No | **Yes** [I] |
| Claude for Word in-document output | — | — | — | — | — | — | — | Word: tracked changes, comment replies, template/table fill, section citations [C]; Excel: pivots, conditional formatting, validation [C] | No | Skills enabled on the account appear in the add-in [C]; scripts **?** [U] |
| Markdown table / Mermaid in chat text | Yes [I] | Yes [I] | Yes [I] | Yes [I] | Yes [I] | Terminal text | Yes [I] | Sidebar chat [I] | No | Yes (skill output spec) |

Notes on the matrix:
- "Chat" in Anthropic's plugin docs means claude.ai web, Desktop chat and mobile together. From **2026-11-10**, Desktop's Unified Claude starts each home-screen message as a **Cowork session** on machines that can run the Cowork VM [C] (3P doc; 1P rollout "Cowork in every conversation" from 2026-09-16 [C]). So on Desktop, a plugin's local MCP App should become reachable from the normal conversation box. [I]
- Mermaid, Chart.js and Grid.js are **bundled with Claude Desktop** for artifacts (changelog: "charts, tables, and diagrams in artifacts ... The libraries now come with the app") [C].

---

## 1. Inventory and per-mechanism detail

### 1a. Chat artifacts (post-2026-09-16 model)

- **What:** "anything Claude makes for you that you'd put in front of someone": documents, code, single-page websites, images, diagrams, dashboards, small interactive tools; opens beside the conversation; editable, revisitable, shareable. [C] support 17153992
- **Change on 2026-09-16:** chat artifacts made before that date are **legacy**; new ones start from templates (Docs, Slides, Design; Dashboards beta; Motion beta) or from a chat request. "Artifacts, including Claude Design, Claude Slides, and Claude Docs, are available on every plan, including Free." [C] release notes 2026-09-16; support 17153992. Requires the "Create and edit files" (code execution) capability on. [C]
- **Storage:** personal or shared, **20 MB per artifact, text only**; new artifacts don't need publishing to store. [C]
- **AI-powered:** artifacts can call Claude; usage counts against **each viewer's** plan; new artifacts ask permission on first use. [C]
- **Connectors:** artifacts can call the user's connected apps and custom connectors (Pro and above, web + desktop); tools needing per-action approval are unavailable; **each viewer uses their own connections**. [C]
- **Sharing:** private by default; "Anyone with the link" on all plans; org sharing on Team/Enterprise, external sharing owner-controlled; link-only visitors get no connector or Claude features; artifacts with uploaded files still being checked can't use public links. Embedding only for published **legacy** artifacts. [C] support 9547008
- **Technical model of a new chat artifact:** not documented on support pages (no HTML/React/SVG/Mermaid list, no CSP). [U] The Claude Code artifact page documents the shared viewer model: one self-contained HTML or Markdown page, served from `*.claudeusercontent.com` under a strict CSP; scripts only from cdnjs, unpkg, Tailwind CDN, jQuery CDN and jsDelivr `/npm/`; fonts only from Google Fonts; `fetch`/XHR/WebSocket only to its own origin; no external images; downloads only through a declared capability; ≤ 16 MiB rendered. [C] code.claude.com/docs/en/artifacts. Assume the same constraints for chat artifacts. [I]
- **Exports:** Docs → Word, PDF, Markdown, Google Docs; Slides → PowerPoint, PDF, Google Slides; Design → .zip, PDF, PowerPoint, **standalone HTML**. [C]
- **Local files:** no documented way for an artifact to read local files. [U]
- **Zip-shippable?** Not as an artifact itself. A skill can carry `assets/nda-review-view.html` and instruct "render the result as an artifact using this template" [I]. No documented contract forces Claude to use the template verbatim [U]; treat it as a strong hint, check the result.

### 1b. Inline visuals ("Imagine with Claude")

- Custom charts, diagrams and interactive visuals **inside the reply**, launched 2026-03-12 (beta, all plans), extended to Cowork on paid plans 2026-04-22. They "change or disappear as the conversation evolves"; artifacts are the persistent option. [C] claude.com/resources/articles/claude-builds-visuals
- Built with HTML/SVG per secondary coverage. [S]
- No template, no export, no persistence. Useful for "show me the risk spread" moments, not deliverables. [I]

### 1c. Cowork artifacts (formerly "live artifacts")

- Live artifacts (Cowork dashboards and trackers "connected to your apps and files" that refresh when opened) were announced 2026-04-20 [S: Anthropic X post]. Artifacts made in Cowork **before 2026-08-19** stay "live artifacts" (open/share, republish to edit); after that date they are ordinary artifacts. [C] support 14729249
- Sharing stays inside the organization; viewers use their own access. Organizations on CMEK, ZDR or HIPAA-ready keep the old live-artifact behaviour. [C]
- Desktop changelog mentions live-artifact `askClaude()` calls, connector approval prompts inside live artifacts, and a link-open confirmation. [C] Desktop changelog

### 1d. Claude Code published artifacts

- One self-contained HTML/MD page published to a **private claude.ai URL**, updated in place, versioned; share with people/org (Team/Enterprise) or publicly (owner must enable). [C] code.claude.com/docs/en/artifacts
- **Live connector calls** (Claude Code ≥ 2.1.209): the page declares which claude.ai connectors it may call; each viewer's own connection is used; responses cached in the viewer's browser; refresh on load, interval or control. **Local `.mcp.json` servers can feed data at build time but the published page cannot call them.** [C]
- Comments on the page with "Send to Claude" (≥ 2.1.221); file downloads via a declared capability. [C]
- **Availability limits:** Pro+; claude.ai login (no API key, gateway token, Bedrock, Vertex or Foundry); not with CMEK, HIPAA or ZDR orgs; off in Agent SDK and `-p`. [C]
- "Turn an artifact prompt you reuse into a skill" — Anthropic's own guidance, so a Spaarke skill can drive publishing. [C]

### 1e. MCP Apps (ext-apps, `io.modelcontextprotocol/ui`)

- **Contract:** tool declares `_meta.ui.resourceUri = ui://…`; resource returns HTML (`text/html;profile=mcp-app`, SDK constant `RESOURCE_MIME_TYPE`); host renders it in a sandboxed frame; tool returns `content` (text, for non-UI hosts) + `structuredContent` (for the UI). [C] claude.com/docs/connectors/building/mcp-apps/quickstart. Spec stable 2026-01-26 (prior r1 note) [C].
- **Where Claude renders them:** "Interactive connectors are available for all users on Claude, Cowork, Claude Desktop, and Claude for iOS/Android." [C] support 13454812. MCP client matrix: Claude (web) ✓, Claude Desktop ✓, **Claude Code ✗**, Microsoft 365 Copilot ✓, VS Code Copilot ✓, ChatGPT ✓. [C] modelcontextprotocol docs `client-matrix.mdx`. Desktop Code tab renders "interactive MCP app widgets in the conversation". [C] Desktop changelog. Office add-ins: not mentioned anywhere. [U]
- **Local servers render:** the Getting-started page runs example apps as **local `npx … --stdio` servers in `claude_desktop_config.json`** and they render inline. [C] "Custom and local connectors always show the [open-link] modal" and "local connectors have no URL to hash, so `ui.domain` isn't available". [C]
- **Display modes:** inline card (auto height, ≤ 2 actions, ≤ 4–5 data points, no menus/popovers, no nested scroll), inline carousel (3–8 items), fullscreen (composer stays visible; no floating panels). `pip` is in the mode list. [C] design-guidelines
- **Interactivity:** `app.callServerTool` back to the **same** server; `ui/open-link` (modal unless a directory connector allowlists the destination); `ui/request-display-mode`; host style variables (Anthropic Sans, light/dark tokens) and `safeAreaInsets`. [C] Spec-level `ui/message`, `ui/update-model-context`, `ui/download-file` exist (prior r1 note) [C]; Claude-specific support for each beyond the cited ones not listed. [U]
- **Security:** sandboxed iframe (desktop/web) or native WebView (mobile); **all external origins blocked by default**, declare `_meta.ui.csp.connectDomains/resourceDomains/baseUriDomains`; `frameDomains` restricted in Claude; no camera/mic/location on mobile; MCP tool calls proxied through Anthropic's backend for remote servers. [C]
- **Size:** tool results over **~150,000 characters** get written to the sandbox filesystem and the app never hydrates (claude.ai/Desktop); Claude Code default 25,000 tokens. Paginate; fetch detail via app-initiated tool calls. [C] troubleshooting
- **Inline ext-apps script:** the iframe CSP blocks CDN script loads unless declared; bundle into one HTML file. [C] quickstart (declares `resourceDomains: ['https://unpkg.com']` for the SDK) / [S] skill pages.
- **Packaging without a server:** `.mcpb` = zip of a local stdio server + `manifest.json`, one-click install in Claude Desktop, Node bundled with Desktop on macOS/Windows, works offline, no OAuth. Directory no longer accepts `.mcpb`; **distribute a local server inside a plugin**. [C] mcpb page. A plugin's `mcpServers` can point at `./servers/x.mcpb` (or `.dxt`). [C] code.claude.com plugins/components. Platform table: local MCP server "including `.mcpb` bundles" → Chat **Ignored**, Cowork **Loads when the session runs on your computer**, Claude Code Loads. [C] plugins/platform-support
- **Tooling:** `/plugin marketplace add modelcontextprotocol/ext-apps` + `/plugin install mcp-apps@mcp-apps` gives Claude Code skills to scaffold an app; examples in vanilla JS, React, Vue, Svelte. [C]

### 1f. Generated files

- Anthropic ships built-in skills for Excel, Word, PowerPoint and PDF that "activate automatically when relevant". [C] skills overview
- Custom skills may run scripts "in any language available where the skill runs" (machine in Claude Code/Cowork; code-execution sandbox in claude.ai chat, where the whole skill folder is copied into the sandbox). `${CLAUDE_SKILL_DIR}` resolves in Claude Code and Cowork. [C] skills how-to
- Cowork "creates polished deliverables including Excel spreadsheets with functional formulas, PowerPoint presentations, and formatted documents", reading and writing local files directly. [C] cowork overview
- Desktop Files pane: "inline previews for PDF, Word, Excel, and PowerPoint files"; HTML preview pane with an address bar. [C] changelog
- **Zip-shippable:** yes. r1's Python engines (stdlib + lxml + python-docx) are the same pattern. Runtime package availability on Claude's sandbox is covered in `claude-install-and-runtime.md`.

### 1g. Claude for Word / Excel / PowerPoint

- **Word:** tracked-changes mode ("every edit lands as a revision you can accept or reject in Word's native review pane"), comment-driven editing with thread replies, summarise counterparty redlines and flag dealbreakers, fill templates and populate tables in place, clickable section citations, semantic navigation. GA for Pro, Max, Team, Enterprise. [C] claude.com/docs/office-agents/word
- **Skills:** "Skills you've enabled in your Claude settings are available in all Claude for M365 add-ins", invoked automatically or by `/`. With a Claude account sign-in, each request also carries the name and description of each plugin the user turned on. [C] connectors-and-skills; data-storage. Script execution: [U] (sibling note).
- **Excel:** pivots, conditional formatting, data validation, model population. [C]
- **Known issue:** after the Sept/Oct 2026 Microsoft 365 updates on Windows (2608/2609), some users lose add-in chat history and **uploaded** skills; skills saved to the Claude account are the workaround. [C] data-storage
- Anthropic's own `claude-for-legal` plugins target this: `commercial-legal:review` and similar "produce redlines a reviewer can accept or reject" in the Word sidebar; tabular review and trackers go to `.xlsx`. No HTML artifacts, dashboards or MCP Apps in that repo. [C] github.com/anthropics/claude-for-legal (Apache-2.0)

### 1h. Newer items

- **Claude Design** is now an artifact template in any chat, Claude Code (`/design`) and the Artifacts tab; standalone `claude.ai/design` **closes 2026-12-14**; exports .zip, PDF, PPTX, Google Slides, standalone HTML; design systems import (GitHub, files, `/design-sync`). [C] support 14604416
- **Claude Docs** reaches Claude Code as a claude.ai connector ("claude.ai Claude Docs"). [C]
- **Dashboards (beta, 2026-10-08):** built from data platforms (Redshift, BigQuery, ClickHouse, Databricks, Snowflake) and CRM connectors; charts show last refresh; Enterprise off by default. Not a fit for a file-based v1. [C] claude.com/resources/articles/dashboards-and-motion
- **Drafts open beside the chat** for in-place editing (2026-06-12). [C] release notes

---

## 2. Server, packaging, interactivity, security per mechanism

| Mechanism | Our server? | Ships in the zip as | Calls tools back? | Persists state? | Reads local files? | Security / CSP notes |
|---|---|---|---|---|---|---|
| Chat / Cowork artifact | No (Anthropic hosts) | `assets/*.html` template + skill text telling Claude to use it [I] | Yes: the viewer's claude.ai connectors (not per-action-approval tools) and Claude itself [C] | Yes, 20 MB text, personal or shared [C] | Not documented [U] | Strict CSP, 5 CDNs [C, Code doc]; client content stored on Anthropic infra under org retention [C]; shared storage visible to all viewers [C] |
| Inline visual | No | Nothing (prompt) | Not documented [U] | No (ephemeral) [C] | No [I] | — |
| Claude Code published artifact | No (Anthropic hosts) | Skill that tells Claude to publish [C] | Yes: declared claude.ai connectors per viewer [C] | Versions; browser cache [C] | Build time only [C] | Not with CMEK/HIPAA/ZDR, not on Bedrock/Vertex/Foundry [C] |
| MCP App, local `.mcpb` | No | `servers/spaarke-views.mcpb` referenced from `plugin.json` `mcpServers` [C] | Yes: `callServerTool` to the same local server, which can read local files [C/I] | Server-side files under the user's profile [I]; no stable iframe origin (no `ui.domain` for local) [C] | Yes, via the server [C] | Default-deny CSP; open-link always confirms for local servers [C]; Desktop-only [C] |
| MCP App, remote (v2) | **Yes** (Spaarke BFF `/mcp`) | `.mcp.json` URL entry [C] | Yes [C] | Server-side (Dataverse) [I] | No (server data only) [I] | OAuth; CORS for `{hash}.claudemcpcontent.com`; ~150k-char result limit [C] |
| Files (.docx/.xlsx/.pdf/.html) | No | `scripts/` + `assets/` templates [C] | No (static) | It is the file | Scripts can (local Cowork/Code) [C] | HTML file preview JS behaviour [U]; HTML opened in a browser has full browser rights [I] |
| Claude for Word output | No | Skill instructions + Markdown playbook references [C/I] | n/a (Claude edits the doc) | In the document [C] | Only the open document [C] | Prompt-injection warning for counterparty docs [C]; 30-day backend deletion; not in Enterprise audit logs [C] |

---

## 3. Cross-platform: Claude vs Microsoft 365 Copilot / Cowork

| Mechanism | Claude | Microsoft Cowork / M365 Copilot (r1 notes) | Author once? |
|---|---|---|---|
| Markdown table in chat | Yes [I] | Microsoft's own sample skill specifies a Markdown table; in-chat rendering not documented [C/U] | **Yes**, the baseline |
| `.docx` / `.xlsx` / `.pdf` file | Yes [C] | Yes; Office "online preview", PDF inline [C] | **Yes** (same engines; r1 already does this) |
| Self-contained `.html` file | Preview pane [C]; JS in local Cowork [U] | "HTML renders in the preview pane"; JS/CSP [U] | **Yes, if** it degrades without JS (static table + optional script). One test per side (T3) |
| MCP App, remote server | Yes, web/desktop/mobile/Cowork [C] | Yes: Copilot Chat and Cowork; inline + fullscreen; `requestModal`, `pip`, `prefersBorder`, `domain`, `frameDomains` not supported in M365; Cowork limits 64 KiB inline result, 60 calls/min, 640 px inline height [C] | **Yes**: same HTML; design to the stricter M365/Cowork subset |
| MCP App, local stdio / `.mcpb` | Desktop only [C] | **No**: Cowork `agentConnectors` take HTTPS URLs only [C] | No (Claude-only v1 bonus) |
| Artifacts, inline visuals, Claude Code artifacts, Dashboards | Yes [C] | No equivalent; the nearest is Cowork's **App skill** (Copilot Managed Runtime, Frontier preview, hosted, billed) [C] | No |
| Adaptive Cards | No [I] | Built-in Cowork skill; no contract documented [C/U] | No |
| In-Word tracked changes by **Spaarke's** skills | **Yes**: enabled skills run in Claude for Word [C] | **No**: third-party plugins listed but don't run in Word (r1 L21/L25) | No; Microsoft side stays playbook-file based |

**Where Claude is clearly stronger** (all [C] except where marked):
1. Spaarke skills work **inside Word** with native tracked changes and comment replies (Microsoft's Word runs only Microsoft's own Legal plugin).
2. Artifacts: persistent, shareable, AI-powered, connector-aware and stateful with no hosting, on every plan (templates) or paid plans (storage, connectors).
3. Zero-server interactive UI through a local `.mcpb` MCP App on Desktop. Microsoft needs a hosted HTTPS server for any widget.
4. Claude Code artifacts with live connector refresh and comment threads, useful for an internal "NDA library status" page once v2 exists.
5. Higher MCP App result ceiling (~150k chars vs Cowork's 64 KiB inline). [C both]

**Where Microsoft is level or stronger:** org governance of the file outputs (OneDrive Cowork folder, Purview), and Adaptive Cards for quick structured replies. [I]

---

## 4. Recommendations

### Design principle (both versions) [I]

Build **one view bundle per output**: a single self-contained HTML file (vanilla or React, inlined CSS/JS, no CDN) that takes its data from **either** an embedded JSON block (`<script type="application/json" id="spaarke-data">`) **or** the ext-apps bridge (`app.ontoolresult` → `structuredContent`). The same file then serves as:
- v1: the template a script fills to write `*.html` report files, and the template a skill hands Claude for an artifact;
- v1 (optional, Claude Desktop only): the `ui://` resource of a local `.mcpb` server;
- v2: the `ui://` resource of the Spaarke BFF MCP endpoint, for Claude **and** M365 Copilot.

Keep the data JSON identical to the engine output schema r1 already has, so every channel reads the same contract. Use Claude host style variables with fallbacks so it also looks right in a browser and in Microsoft's widget host.

### Per output

| Legal output | v1: email a zip, no server | v2: hosted Spaarke MCP connector |
|---|---|---|
| **NDA / contract review** (clause-by-clause GREEN/YELLOW/RED + fallback language) | (1) Markdown table in the reply (all surfaces, both platforms). (2) **`.docx` review memo** with the four-column table (r1 engine; cross-platform). (3) In **Claude for Word**: the same skill applies fallback language as **tracked changes with a comment per clause** (Claude's strongest channel; instruction-only skill). (4) Optional Claude-only artifact from `assets/review-view.html` for an interactive filter-by-colour view. | **MCP App inline card** (counts by colour + top 3 RED items) that expands to a **fullscreen grid** with clause text, rationale and fallback; "Insert fallback" → `callServerTool`. Same widget in M365 Copilot (stay within its unsupported list: no `requestModal`, no `pip`). |
| **Issues list** | `.xlsx` (sortable, filterable; one row per issue, severity, owner, status, clause ref) + Markdown table in chat. Claude for Excel can then pivot or format it. | MCP App fullscreen table with status edits written back through app-only tools (`visibility:["app"]`), paginated to stay under the 64 KiB Cowork / ~150k Claude limits. |
| **Library review dashboard** (16 NDAs) | **Self-contained `library-dashboard.html`** written by a script from the template (heatmap NDA × topic, counts, outliers), plus an `.xlsx` with a native chart as the fallback. On Claude, also offer "open as artifact" from the same template (chat/Cowork). Do **not** use Claude Dashboards (needs warehouse/CRM connectors). | **MCP App fullscreen dashboard** fed by an app-only paging tool. Claude-only extra: a Claude Code-published artifact with live connector calls for an org-shared status page (Pro+, Anthropic-hosted; check client-confidentiality policy first). |
| **Redline comparison** | (1) **`.docx` with real tracked changes** (`w:ins`/`w:del`) produced by the engine: opens as a native redline everywhere. (2) In **Claude for Word**: "summarize what the counterparty changed and flag dealbreakers" on the counterparty's tracked changes, and tracked-changes mode for our counter-edits [C]. (3) Optional side-by-side `redline.html` (static diff; works with JS off). | **MCP App fullscreen side-by-side diff** with accept/reject per change via `callServerTool`, and an `openLink` to the document in SPE. Word remains the system of record for the final redline. |

### Optional v1.5: a local `.mcpb` MCP App (Claude Desktop only) [I]

Worth a spike, not a v1 dependency: it proves the v2 widget with no hosting and gives Desktop users a real interactive grid. Cost: Node server code in the zip, Desktop-only reach, untested in Cowork (T1). Ship it as `servers/spaarke-views.mcpb` referenced from `plugin.json`; skills must still produce the file outputs, because chat on the web and Microsoft Cowork ignore it.

### Rules for skill authors [I]

- Every skill ends with a Markdown table and names the file it wrote. Visuals are additive, never the only output (the same rule MCP Apps follow: `content` must stand alone).
- HTML templates: one file, inline everything, no external fetch, ≤ a few MB, render a static table before any script runs.
- No client data in shared artifact storage; prefer private artifacts and files in the user's folder. Treat counterparty documents as prompt-injection risk (Anthropic's own Word warning).

---

## 5. Tests to run (one line each)

- **T1** Local `.mcpb` with one `ui://` tool, referenced from a plugin: does the UI render in a **local Cowork task** on Desktop, and in Desktop chat after the 2026-11-10 Unified Claude switch?
- **T2** Same plugin uploaded on claude.ai web: confirm the server is ignored and the skill falls back to files.
- **T3** Write `dashboard.html` with inline `<script>` that draws an SVG chart: does Claude Desktop's Cowork preview run it? Same file in Microsoft Cowork's preview pane.
- **T4** Skill with `assets/review-view.html` + "render as an artifact using this template": how faithfully does Claude reuse it (chat, Cowork, mobile view)?
- **T5** Claude for Word: does a plugin skill's tracked-changes instruction produce one revision + one comment per clause on a 10-page NDA; do `scripts/` run there?
- **T6** Remote MCP App (dev tunnel) rendered in Claude web and in M365 Copilot from one HTML file: list layout differences (theme tokens, height cap).
- **T7** Artifact sharing on a Team plan: private vs org link; confirm shared storage stays off for client data.
- **T8** Claude Desktop Office previews of the engine's `.docx` with tracked changes: are revisions visible in the preview?

---

## Sources

Most authoritative first.

**Anthropic docs (claude.com/docs, code.claude.com):**
- https://claude.com/docs/plugins/platform-support (component support per surface; local/`.mcpb` row)
- https://claude.com/docs/plugins/build · https://claude.com/docs/connectors/building/what-to-build
- https://claude.com/docs/connectors/building/mcp-apps/getting-started · /quickstart · /design-guidelines · /troubleshooting · /external-links
- https://claude.com/docs/connectors/building/mcpb
- https://code.claude.com/docs/en/plugins/components (`.mcpb` in `mcpServers`; local servers vs claude.ai/Cowork)
- https://code.claude.com/docs/en/artifacts (published artifacts, CSP, connectors, availability)
- https://claude.com/docs/skills/overview · https://claude.com/docs/skills/how-to
- https://claude.com/docs/office-agents/word · /excel · /connectors-and-skills · /data-storage · /performance
- https://claude.com/docs/cowork/overview · https://claude.com/docs/cowork/changelog (Desktop release notes to v4.23118.0, 2026-11-05)
- https://claude.com/docs/third-party/claude-desktop/unified-claude

**Anthropic help centre and announcements:**
- https://support.claude.com/en/articles/17153992-what-are-artifacts · /9487310 · /9547008 (sharing) · /14729249 (live artifacts) · /14604416 (Claude Design) · /13454812 (interactive connectors) · /12138966 (release notes)
- https://claude.com/resources/articles/claude-builds-visuals · https://claude.com/resources/articles/dashboards-and-motion
- https://claude.com/blog/interactive-tools-in-claude

**MCP:**
- https://raw.githubusercontent.com/modelcontextprotocol/modelcontextprotocol/main/docs/extensions/client-matrix.mdx (MCP Apps client matrix)
- https://github.com/modelcontextprotocol/ext-apps (spec 2026-01-26, SDK, examples, skills plugin)
- https://github.com/modelcontextprotocol/mcpb

**Reference implementation:** https://github.com/anthropics/claude-for-legal (Apache-2.0)

**Secondary (used only where labelled [S]):** x.com/claudeai status 2046328619249684989 (live artifacts launch); vktr.com, alternativeto.net (inline visuals HTML/SVG); deckary.com, contextbolt.com (template dates).

**Microsoft side:** r1 researcher notes `cowork-visual-outputs-2026-11-05.md`, `cowork-mcp-apps-author-guide-2026-10-30.md`, `mcp-server-dotnet-bff-copilot-widget-2026-11-05.md` (in `spaarke-wt-spaarke-legal-ai-accelerator-r1/.claude/agent-memory/researcher/`).

## Caveats

- The post-2026-09-16 chat artifact runtime (formats, CSP, `window.storage`/Claude-call APIs) is not documented on support pages; the CSP figures come from the Claude Code artifact page. Treat chat-artifact limits as [I] until T4.
- MCP Apps in the Office add-ins: undocumented. Assume none.
- Dates for Dashboards and template GA come from Anthropic's article (2026-10-08) and help centre; some third-party dates differ.
- Unified Claude (Chat → Cowork) changes which surface a Desktop message lands on from 2026-11-10; re-check the local-MCP rows after that date.
