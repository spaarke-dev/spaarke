---
name: cowork-visual-outputs-2026-11-05
description: What visual output a Cowork skill can produce as of 2026-11-05: Adaptive Cards (no contract), App skill = Copilot Managed Runtime (Frontier, preview), HTML/SVG/PNG/PDF/Office previews in file pane, MCP Apps; what is Unknown and the tenant tests that settle it
metadata:
  type: reference
---

# Cowork visual outputs for skills (2026-11-05)

Builds on: cowork-skill-runtime-and-mvp-claims-2026-10-30, cowork-builtin-skills-and-specialist-agent-patterns-2026-10-31, cowork-mcp-apps-author-guide-2026-10-30. Labels: **Cited** (primary says it), **Secondary** (non-Microsoft or blog), **Unknown** (no source).

## Pages re-read (all current as of 2026-11-05)
- use-cowork https://learn.microsoft.com/en-us/copilot/cowork/use-cowork (ms.date 2026-11-03, updated 2026-11-04)
- whats-new (ms.date 2026-11-03), overview /copilot/cowork/ (2026-11-03), cowork-faq (ms.date 2026-11-04, updated 2026-11-05), mcp-apps-support (ms.date 2026-09-08, updated 2026-11-03; no change in content found)
- Managed Runtime: https://learn.microsoft.com/en-us/microsoft-365/managed-apps/index (ms.date 2026-10-05), .../admin/manage/apps/ (2026-10-13), .../managed-apps/developer/ (2026-09-29)

## Findings

### Adaptive Cards (Q1)
- **Cited**: built-in "Adaptive Cards" skill "Generates interactive card-based responses with structured layouts, buttons, and data displays in the conversation" (use-cowork, faq, overview). use-cowork also lists "Interactive cards" under what displays while Cowork works.
- **Unknown (no Learn page at all)**: card JSON contract, `assets/` templates, `${}` templating, schema version, which elements (Chart.*, Table, Badge, ProgressBar, CodeBlock, Icon), Action.Submit/Execute round trip, size limits. No Cowork-specific card page exists; no Microsoft sample or lab (copilot-camp CWRK01, ignite26-LAB741) uses cards.
- **Cited, adjacent only (NOT Cowork)**: Learn "Adaptive Cards for agent design" (ms.date 2026-05-20, updated 2026-09-30) lists Copilot Chat = schema 1.5, Teams = 1.5, Bot Framework Web Chat = 1.6 without Action.Execute; "Cards that use unsupported schema elements might fail silently". Cowork is not in that host table. Copilot Studio doc (ms.date 2026-10-21): supports <=1.6, Teams limited to 1.5. Chart.* elements are not confirmed supported in any Microsoft host table found. The April 2026 demo (donut, KPI tiles, table) is the only evidence charts render in Cowork cards (user-supplied, not re-verified).
- Inference: skill prose "present as an Adaptive Card" is routing only (prior notes). Target 1.5 elements (Table, ColumnSet, FactSet, Container) as the safe subset until tested.

### App skill (Q2)
- **Cited**: App (Frontier) "Build lightweight, interactive apps from a description, then refine, open, publish, and share them" (use-cowork). "This is a preview feature... might not be released for general availability... You need to be part of the Frontier preview program." whats-new: added September 2026 as "App skill (Frontier)". Overview page: use it to "Create interactive visuals from complex data". Invoked by `/app`/describing an app (the Learn Managed Runtime page also calls it "App builder" skill; anchor `#build-apps-with-app-builder-frontier` in cross-links = renamed).
- **Cited**: the artifact is a **Copilot Managed Runtime** app (preview): Microsoft-hosted, Entra auth, 1,500+ connectors "callable directly from JavaScript and TypeScript", Git repo per app (platform-managed or GitHub Enterprise Cloud), governance (DLP, CA, sharing limits, content security policy), listed in M365 admin center and at https://managedapps.cloud.microsoft. Preview in Cowork, then Publish, then share link to anyone in the org "who can use Microsoft 365 Copilot" (anyone with link gets all its data). Builds bill Copilot Credits to the creator's Cowork spending policy; running needs Power Apps Premium or Copilot Credits (0.1 credit per API call; blocked after 20 ops/5 min if uncredited). Cowork path is governed by tenant Frontier onboarding, "On by default" in Frontier public preview.
- **Secondary** (forwardforever.com, Timo Pertila, 2026-09-25): hands-on; Gantt app built in Cowork, ~1,600 credits (~EUR16); source in Git; lives "in the Power Platform environment just like Code Apps"; SharePoint list connection prompted per user on first use. Stack not named by any source (React likely but Unknown).
- **Unknown**: whether a skill can pass a template from `assets/` to the App skill (no contract; prose handoff only); framework; inline vs pane rendering of the running app (Learn says "open" / "preview"; secondary says "opens a preview view"); network egress limits beyond tenant CSP/DLP; GA date.
- Tension for Spaarke: an App is a hosted, governed, per-user-billed Microsoft app, not a file the skill owns. Not a zero-infrastructure, no-Frontier channel.

### Images and file previews (Q3) - Cited, strongest finding
- use-cowork "Supported preview formats" (and FAQ "Can I preview files without downloading them?"): PDF "Renders inline with page navigation"; Word/Excel/PowerPoint "open in an online preview"; CSV formatted table; Markdown "Renders with full formatting"; code with syntax highlighting; Images "Displays inline (png, jpg, jpeg, gif, webp, bmp, svg, ico)"; **HTML "Renders in the preview pane"**; email.
- Files Cowork writes appear in side panel "Output folder" with Download and Preview buttons; "Download All" zip (<=50 files, 500 MB). Output goes to OneDrive `Cowork` folder.
- "Cowork can already create interactive HTML files as part of a task" (Tech Community "Cowork in progress", May 2026, updated 2026-10-21) - the example is "an interactive HTML dashboard with a side-by-side view of all three markets".
- **Unknown**: whether a PNG appears **inline in the chat transcript** (docs say generated images are "saved to your session and to your OneDrive output folder"; only the preview pane is documented for files); whether HTML preview executes JavaScript; sandbox/CSP of the HTML preview (inline script? external CDN? fetch?); whether SVG in preview is sanitized.

### Markdown tables / Mermaid / LaTeX in chat (Q4)
- **Cited**: Markdown **files** render with full formatting in preview. Microsoft's own sample skill (cowork-plugin-development) specifies a Markdown table output (`| Clause | Category | Risk Level | Summary |`) and advises "Define output format. Show the exact table".
- **Unknown**: Markdown tables, Mermaid, LaTeX **in the chat bubble**. Mermaid and LaTeX not mentioned anywhere on Learn.

### Pre-built HTML/JS bundle in `assets/` (Q5)
- **No documented path.** Skill = instructions + companion files read/executed by the agent; Cowork does not render companion files. The only documented bundle-to-render routes are (a) MCP Apps (needs MCP server, `ui://` + `resources/read`), (b) App skill (Managed Runtime), (c) agent writes an HTML/SVG/PNG file into the output folder, which the preview pane shows (Cited rendering, Unknown JS/CSP).
- **Strongest candidate that needs no server**: skill script (or the model) **writes a self-contained HTML file from an `assets/` template into the output folder**, user clicks Preview. Cited: HTML preview exists; 20 companion files and 10 MB per skill fit a single-file React/Fluent build. Everything about interactivity/JS is Unknown.
- Scripts: egress blocked by design (prior note); a built single-file HTML must be fully offline (inline JS/CSS, data: URLs). Packages beyond tenant probe (Python 3.12.15, lxml, python-docx, soffice, pdftoppm) Unknown: matplotlib, openpyxl, pandas, Pillow, Node not yet probed.

### Microsoft guidance / samples showing visuals (Q6)
- None found. Legal for Cowork (launched 2026-10-15, "more than 20 purpose-built legal skills", Tech Community "Introducing Microsoft Copilot for legal") - announcement says nothing on cards/dashboards/charts; output described as native Word tracked changes/content controls. No public review-contract SKILL.md text for Microsoft's version found. Cowork learn sample = Markdown table. copilot-camp CWRK01 produces PPTX/email only; ignite26-LAB741 (AI Impact Coach) Word-based. Community (Secondary): kesslernity/awesome-copilot-cowork-skills has `project-dashboard-builder` "Single-file offline HTML dashboard" - no rendering evidence, says "No scripts. Whether Cowork executes bundled scripts is undocumented" (older than our tenant test).
- Microsoft-side adjacent: MCP Apps guide itself says "display widgets (charts, previews)" are the intended use.

### Ignite 2026 / Message Center (Q7)
- Ignite is 17-20 Nov 2026 (Moscone + digital); not yet happened. Pre-event catalog found only BRK201 "The evolution of the Microsoft Copilot app" (Chat, Cowork, Code, Autopilot) via a third-party preview (Secondary). No announced item on Cowork visuals/apps/cards. Message Center items 2026-11-03/04 (RM567315 Planner, MC1536053 Accounting in Excel/Cowork, RM577746 D365 Journeys) don't touch visuals. whats-new Nov 2026 has only Planner plugin. Treat as Unknown; re-check 2026-11-18+.

## Tenant tests to run (one line each)
1. Probe script: `import matplotlib, openpyxl, pandas, PIL, plotly; node --version` - settles chart-rendering toolchain.
2. Ask a skill to write `risk.png` (stdlib or matplotlib) to output folder: does it show inline in chat, or only as Preview in the side panel?
3. Write a self-contained `dashboard.html` with inline `<script>` that draws an SVG chart and a button toggling a table: does Preview execute JS? Add `fetch('https://example.com')` and a CDN `<script src>` to see CSP.
4. Put an Adaptive Card JSON (1.5 with Table/ColumnSet/FactSet; then 1.6 with Chart.Donut) in `assets/card.json`; instruct "present with the Adaptive Cards skill using this template": does it render, does `${}` get filled by the model, which elements fall back?
5. Same card with Action.Submit, Action.Execute, Action.OpenUrl, Action.ShowCard: which render, does a click post a new user turn?
6. Ask chat for a Markdown table, a Mermaid block, and `$$\sum$$` LaTeX: which render.
7. Preview a script-built .xlsx (with a native chart), .pptx (native chart), .pdf (matplotlib PDF) and .svg: confirm fidelity in the pane (charts in Office online preview).
8. (Frontier tenant only) `/app` with "dashboard from this JSON of clause risks": record stack, where it renders, share link behaviour, credit cost via `/cost`.

## Open questions
- Does the Adaptive Cards skill accept a skill-supplied template at all, or only free generation?
- Is HTML preview sandboxed with JS enabled (interactive dashboards are the cheapest visual route if yes)?
- Is the "Adaptive Cards" skill's schema 1.5 like Copilot Chat or newer?
