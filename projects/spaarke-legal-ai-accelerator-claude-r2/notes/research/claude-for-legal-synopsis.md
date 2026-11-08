# Claude for Legal: synopsis, configuration, extension, fit for Spaarke r2

> **Researched** 2026-11-07 (researcher sub-agent). **Primary source:** `github.com/anthropics/claude-for-legal`, `main` HEAD
> **`4a6c651`** ("Update plugin content (#104)", committed 2026-07-22). Read from a local clone; nothing executed.
> **Labels:** **Cited** = read in the repo at `4a6c651` or on the named page today. **Inferred** = my reasoning, not stated by a source.

## Summary (10 lines)

1. **No change on `main` since `4a6c651` (2026-07-22).** Repo pushed 2026-11-07, but only PR branches: about 25 open PRs, mostly new connectors. 9,926 stars, Apache-2.0. (Cited)
2. **Contents:** a Claude plugin marketplace (`claude-for-legal`) with 12 first-party plugins, 1 Thomson Reuters plugin (`cocounsel-legal`) and 5 Managed-Agent cookbooks. It has **151 SKILL.md, 10 agents, 0 commands, 0 working hooks** (every `hooks.json` is an empty stub) and 13 `.mcp.json` files. Everything is Markdown and JSON, with no scripts inside any plugin. (Cited)
3. **Method only, by design.** For example, `nda-review` "does not ship with default positions". Positions come from a **practice profile** that the `cold-start-interview` skill writes. (Cited)
4. **The profile is two prose Markdown files outside the plugin:** `~/.claude/plugins/config/claude-for-legal/<plugin>/CLAUDE.md` plus a shared `company-profile.md`. Every skill reads them first and stops if they contain `[PLACEHOLDER]` text. Teams have no built-in way to share one profile. (Cited)
5. **The profile path is broken in Cowork.** Open issues #55 (2026-05-21) and #94 (2026-06-09): Cowork's sandbox blocks writes to `~/.claude`, the profile is not kept between sessions, and symlinks to a shared Drive don't work. In web chat there is no persistent `~` at all (Inferred). Spaarke r1's design (a user-owned profile file in a normal folder) avoids this. (Cited)
6. **Extension is by fork, edit, or a PR to upstream.** Adding a connector means a PR to a plugin's `.mcp.json`. Community skills come in through `legal-builder-hub`, which applies an allowlist, licence check, injection scan and 13-parameter QA before installing to `~/.claude/skills/`. (Cited)
7. **Cross-plugin reuse is slash-command prose only** (for example "run `/privacy-legal:dpa-review`"). Claude Code itself supports `dependencies` in `plugin.json`, including other marketplaces when an allowlist permits it; claude.ai counts a dependency as met only if the user also turns it on. (Cited)
8. **Surfaces.** Skills, commands and connectors work in chat, Cowork and Claude Code. Agents and hooks work in Cowork and Claude Code only. A plugin with a top-level `bin/` cannot be installed in chat or Cowork. Skills enabled in Claude settings show up in Claude for Word and Excel. (Cited)
9. **Licence:** Apache-2.0, "Copyright 2026 Anthropic PBC", with no NOTICE file. Redistribution needs the LICENSE copy, change notices in modified files and no implied endorsement. Plugin names starting `claude-` are reserved. (Cited)
10. **Fit for v1:** don't bundle a fork. Ship a Spaarke add-on plugin plus a pre-filled practice-profile seed, and point users at Anthropic's marketplace for `commercial-legal`. Spaarke's NDA playbook fills the exact gap the plugins leave on purpose: the `NDA triage positions` section that `nda-review` requires and the template omits. (Inferred)

---

## 1. Synopsis

### 1.1 What it is

- Anthropic describes it as "Reference agents, skills, and data connectors for the legal workflows we see most", available "two ways from one source": Cowork or Claude Code plugins, or Claude Managed Agents. (Cited: README)
- It launched 2026-05-12 with "more than 20" connectors and 12 practice-area plugins. It builds on a February 2026 Cowork legal plugin. (Cited: [LawSites 2026-05-12](https://lawnext.com/2026/05/anthropic-goes-all-in-on-legal-releasing-more-than-20-connectors-and-12-practice-area-plugins-for-claude.html))
- That February plugin still exists separately as `legal` v1.3.0 in `anthropics/knowledge-work-plugins` (Apache-2.0). It has 9 skills: `review-contract`, `triage-nda`, `brief`, `compliance-check`, `legal-response`, `legal-risk-assessment`, `meeting-briefing`, `signature-request`, `vendor-check`. Don't confuse it with `commercial-legal`. (Cited: GitHub API, `legal/` last commit `c71b6f3` 2026-09-21)
- All 12 first-party plugins are at **v1.0.2**. No `plugin.json` has a `license` field. The marketplace name is `claude-for-legal` and its owner is "Anthropic". (Cited)

### 1.2 Per-plugin inventory (counted from `git ls-files` at `4a6c651`)

"Hooks" means `hooks/hooks.json`. Every one that exists contains `{"hooks": {}}`. (Cited)

| Plugin | Purpose (one line, from marketplace.json) | Skills | Agents | Cmds | Hooks | `.mcp.json` servers |
|---|---|---|---|---|---|---|
| `commercial-legal` | Vendor, NDA and SaaS review against a sales-side or purchasing-side playbook; renewals; escalations; stakeholder summaries | 12 | 3 (`renewal-watcher`, `deal-debrief`, `playbook-monitor`) | 0 | stub | 7: Ironclad, DocuSign, iManage, TopCounsel, Definely, Slack, Google Drive |
| `corporate-legal` | M&A diligence (cited tabular review), disclosure schedules, closing checklist, consents and minutes, entity compliance | 13 | 1 (`dataroom-watcher`) | 0 | stub | 7: Slack, GDrive, Box, iManage, TopCounsel, Definely, Solve Intelligence |
| `employment-legal` | Hiring and termination review, worker classification, leave tracking, investigations, policies with state supplements | 20 | 1 (`leave-tracker`) | 0 | stub | 2: Slack, GDrive |
| `privacy-legal` | PIA vs DPIA triage, PIA generation, DPA review, DSAR response, policy drift | 9 | 0 | 0 | stub | 2: Slack, GDrive |
| `product-legal` | Launch review, marketing claims, "is this a problem?", feature risk | 7 | 1 (`launch-watcher`) | 0 | stub | 5: Slack, GDrive, Linear, Atlassian, Asana |
| `regulatory-legal` | Regulatory feed watcher, policy diff, gap tracker, NPRM comments, policy redraft | 9 | 1 (`reg-change-monitor`) | 0 | stub | 2: Slack, GDrive |
| `ai-governance-legal` | AI use-case triage, AIA, vendor AI terms, AI regulatory gaps, EU AI Act inventory, policy starter | 10 | 0 | 0 | **none** | 2: Slack, GDrive |
| `ip-legal` | TM clearance, FTO, invention intake, C&D, DMCA, OSS, IP clauses, portfolio | 12 | 1 (`ip-renewal-watcher`) | 0 | stub | 5: Solve, CourtListener, Descrybe, Slack, GDrive |
| `litigation-legal` | Matters, holds, demands, subpoenas, chronology, claim charts, depo prep, privilege log, briefs | 19 | 1 (`docket-watcher`) | 0 | **none** | 7: Slack, GDrive, Everlaw, TopCounsel, CourtListener, Aurora (Consilio), Trellis |
| `legal-clinic` | Clinic setup, student ramp, intake, deadlines, memos, letters, semester handoff | 16 | 0 | 0 | stub | 5: Slack, GDrive, CourtListener, Courtroom5, Descrybe |
| `law-student` | Socratic drill, briefs, outlines, IRAC grading, bar prep, flashcards | 13 | 0 | 0 | stub | 4: Slack, GDrive, CourtListener, Descrybe |
| `legal-builder-hub` | Find, vet and install community legal skills | 10 | 1 (`registry-sync`) | 0 | stub | 3: Slack, GDrive, Lawve AI |
| `cocounsel-legal` (Thomson Reuters, `external_plugins/`) | Westlaw Deep Research reports | 1 | 0 | 0 | none | 1: `cocounsel-legal` (OAuth, subscription) |
| **Total** | | **151** | **10** | **0** | **0 live** | 52 entries |

Correction to the r1 notes: "agents/hooks/CLAUDE.md that `atk import openplugin` drops" overstates what is lost. **No hooks do anything.** The real Claude-only components are the **10 agents**. `CLAUDE.md` is a template that is never loaded as context. (Cited: repo `CLAUDE.md` "Plugin CLAUDE.md is a template, not project context"; [Claude Code plugins reference](https://code.claude.com/docs/en/plugins-reference): "A `CLAUDE.md` at the plugin root isn't loaded as context")

- Every plugin has `cold-start-interview`, `customize` and (except `law-student` and `legal-clinic`) `matter-workspace`. So about 34 of the 151 skills are setup and plumbing. (Cited / count Inferred)
- The README lists **80+ "named agents"**. These are marketing names for skills, for example "NDA Triager" = `/commercial-legal:review`. Only 10 are real `agents/*.md` files. (Cited)
- **Agent files have no schedule key.** Frontmatter is `name`, `description`, `model: sonnet` and `tools`, with the cadence described in prose ("Weekly, Monday morning"). The README's "cron-style schedule" claim is not reflected in the files. In practice, scheduling needs Cowork scheduled tasks or the Managed-Agent cookbooks. (Cited for the files; mechanism Inferred)

### 1.3 Flagship workflows

| Workflow | Entry point | Notes |
|---|---|---|
| Vendor MSA review, NDA triage, SaaS review | `/commercial-legal:review` routes by document title to `vendor-agreement-review`, `nda-review` or `saas-msa-review` (the last two are `user-invocable: false`) | `confirm_routing` preference; produces one memo; offers a `.docx` redline "Use the docx skill", meaning Anthropic's built-in docx skill (Cited) |
| NDA triage GREEN/YELLOW/RED | `nda-review` | "Scope check" auto-YELLOWs NDAs with standstill, licence, non-solicit or similar clauses. **GREEN is refused unless the profile has attorney-reviewed `NDA triage positions`.** (Cited) |
| Diligence tabular review | `/corporate-legal:tabular-review` | Multi-sheet `.xlsx` with a sources sheet; references for Excel and Google Sheets output plus an M&A column set (Cited) |
| DPA review, DSAR, PIA | `privacy-legal` | (Cited) |
| Claim chart, chronology, deposition prep | `litigation-legal` | (Cited) |
| Contract skills in Word | README: "Every contract-touching skill … is authored to work in the Claude for Word sidebar, with tracked changes as the output mode" | (Cited README; not verified live) |

Shared guardrails are repeated in each plugin `CLAUDE.md` (about 6.7k words in `commercial-legal`). They include: work-product header by role, one "Reviewer note" block, source tags (`[CourtListener]`, `[model knowledge — verify]`, `[settled — last confirmed YYYY-MM-DD]`), a cross-skill severity floor, a destination/privilege check, retrieved-content-is-data, large-input coverage reporting, a next-steps decision tree, a dashboard offer, a verification log, and "scaffolding, not blinders". (Cited)

### 1.4 `cold-start-interview`, `legal-builder-hub`, Managed-Agent cookbooks

- **`cold-start-interview`** is in every plugin. `commercial-legal`'s version is 7,651 words. It offers a "quick" (2 min) or "full" (10–15 min) interview and takes `--redo`, `--check-integrations` and `--side sales|purchasing`. Parts: 0 role, setting and integrations probe (✓ only if an MCP call actually succeeded); 1 team, volume and side; 2 playbook, including a 7-dimension AI/ML-training sub-checklist; 3 escalation matrix; 4 seed documents (templates plus 5–20 signed agreements). The seed documents feed "the delta is the real playbook". It supports pause and resume (`<!-- SETUP PAUSED AT -->`, `[PENDING]`). It refuses to use ambient memory or prior chats: "Fresh professional profile". (Cited)
- **`legal-builder-hub`** is the "app store" for community skills. It browses registries, then runs `skill-installer`: allowlist gate, then fetch, then show the **raw** SKILL.md, then a heuristic injection scan, then a structural trust check (hooks, `.mcp.json`, tools, write paths, network), then a LICENSE-vs-metadata check, then `skills-qa` (13 design parameters; the README says "nine"), then role-aware routing (non-lawyers never get an install prompt on concerns), then an explicit typed `yes`. It installs to `~/.claude/skills/<name>/` and keeps an install log. Updates are SHA-pinned. Watched registries are `legalopsconsulting/lpm-skills`, `lawvable/awesome-legal-skills` and `lawvable/agent-skills`. The licence allowlist defaults to MIT, Apache-2.0, BSD and CC0. (Cited)
  - Inconsistency: the README says the default mode is `permissive`, while `references/allowlist-default.yaml` says `mode: restrictive` ("fail-closed"). (Cited)
  - It also defines per-skill freshness frontmatter (`last_verified`, `freshness_window`, `freshness_category`, `verified_against`), a convention worth copying. (Cited)
- **Managed-Agent cookbooks** (`managed-agent-cookbooks/`) are `reg-monitor`, `renewal-watcher`, `diligence-grid`, `launch-radar` and `docket-watcher`. Each has an `agent.yaml` (orchestrator, `model: claude-opus-4-7`, local tools only), `subagents/*.yaml` leaves in a reader/analyzer/writer tier with only the writer allowed to `Write`, `steering-examples.json` and a README. Deployment is `scripts/deploy-managed-agent.sh <slug>` with `ANTHROPIC_API_KEY`: it uploads the plugin's skills and POSTs `/v1/agents`. MCP URLs are environment variables (`${IRONCLAD_MCP_URL}`). Cross-agent handoffs go through `scripts/orchestrate.py`. `callable_agents` is a "research preview", one delegation level deep. The README says they are "cookbooks, not products … will not work out of the box". (Cited)

## 2. Practice configuration

| Aspect | What the repo does | Label |
|---|---|---|
| Mechanism | `/<plugin>:cold-start-interview` interviews the user and reads seed documents. "It **is** the customization mechanism." `/<plugin>:customize` makes targeted edits later. | Cited |
| Files written | (a) `~/.claude/plugins/config/claude-for-legal/company-profile.md`: shared across all 12 plugins and written by the first plugin set up. Holds setting, name, industry, jurisdictions, regulators, risk appetite and key people (template `references/company-profile-template.md`). (b) `~/.claude/plugins/config/claude-for-legal/<plugin>/CLAUDE.md`: the per-plugin practice profile. The template is `${CLAUDE_PLUGIN_ROOT}/CLAUDE.md`, "replaced on every plugin update. Never write user data here." | Cited |
| Side files | `verification-log.md` per plugin; `matters/<slug>/matter.md` when matter workspaces are on (private practice only); `legal-builder-hub/allowlist.yaml` and `install-log.yaml`; registers such as the renewal register | Cited |
| Format | Prose Markdown with tables, deliberately "Don't write YAML". Sections include Who we are, Who's using this (role, which drives the work-product header), Available integrations, Playbook (Active side; **Sales-side** and **Purchasing-side** each with LoL as four positions, Indemnification, Data protection, Term, Governing law, "The one thing"), Escalation matrix, House style, Outputs, guardrails, Matter workspaces, `confirm_routing: true`, `closing_action`, Seed documents reviewed. Placeholder markers are `[PLACEHOLDER]`, `[DEFAULT]` (quick start), `[PENDING]` (paused) and `[LIMITED DATA — N agreements reviewed]`. | Cited |
| How skills read it | Each SKILL.md says "Load `~/.claude/plugins/config/…/CLAUDE.md`" as step 1, with a hard stop if it is missing or contains placeholders. The template header gives the rule ("READ configuration from that path. Not from this file") and migrates profiles forward from the old cache path. Skills that hit a missing position ask the user and **write the answer back** into the profile. | Cited |
| Positions | **Method only, with no default positions.** `nda-review`: "This skill does not ship with default positions on NDA terms." GREEN "cannot be issued against default or absent positions". | Cited |
| Gap | `nda-review` reads `## Playbook → <side> → NDA triage positions`, but neither the template nor the interview writes that section. Open PR #125 (2026-09-29) adds it. Separately, issue #138 (2026-11-03): the template's own instruction line contains a literal `[PLACEHOLDER]`, so a finished profile can trip the stop rule. | Cited |
| Team sharing | **Not supported.** Profiles are per user and per machine, and the interview asks for "your personal overrides" on top of a team playbook. Issue #94: "there's no way to point everyone at one shared config; I tried symlinking … Cowork can't follow it". | Cited |
| Cowork | Issue #55: "I can't write to your ~/.claude directory from here (Cowork blocks application-internal paths)". Users copy files into hidden folders by hand. Both issues are open and unanswered by maintainers. | Cited |
| Chat (claude.ai) | Not stated. Chat skills run without a persistent home directory, so the profile cannot persist there. | Inferred |
| Claude Code `userConfig` | Not used by these plugins. Even if used, Cowork "doesn't prompt for values" for `${user_config.*}` ([platform support](https://claude.com/docs/plugins/platform-support.md)). | Cited |

## 3. Extension

| Route | How | Label |
|---|---|---|
| Add a skill | `<plugin>/skills/<name>/SKILL.md` with frontmatter `name`, `description` (≤ 1024 characters, "the trigger signal") and `argument-hint`. `user-invocable: false` marks reference-only skills. It is invoked as `/<plugin>:<name>`. Bump the plugin version (patch for behaviour, minor for new skills). | Cited (README, CONTRIBUTING) |
| House style | "Fork skills for house style. Every skill is a markdown file"; "Drop your terminology … templates into the plugin's `CLAUDE.md` and `references/`". | Cited |
| Overlay vs fork | The repo only describes **forking and editing**. It has no overlay or layering mechanism. Anthropic's guide says firms "install them as shipped or fork them to add their own playbooks". Overlay can be done without forking by **seeding the practice profile** (supported data, survives updates) or by a **separate add-on plugin**. | Cited / Inferred |
| Add a practice plugin | Same shape as the others: `.claude-plugin/plugin.json`, `CLAUDE.md` template, `skills/`, `agents/`, `.mcp.json`, plus a `marketplace.json` entry. Validate with `claude plugin validate`; marketplace invariants I1–I11. CLA required for PRs. | Cited |
| Add an MCP connector | PR to a plugin's `.mcp.json` (remote HTTPS MCP, OAuth or API key, read-heavy, provenance in results, no instruction-like content). Anthropic tests "retrieval-quality and injection-resistance"; servers that fail go to the README only (CONNECTORS.md). | Cited |
| Connector auth | `.mcp.json` entries are just `type: http`, `url`, `title` and `description`, with **no auth fields**. Auth is per user at connect time: Cowork via Customize › Plugins › Connectors tab (each "Connected / Not connected / Not added", and Team/Enterprise Owners add org connectors); Claude Code prompts "the first time a skill needs it". Custom connectors must be reachable from Anthropic IP ranges. | Cited (repo; [plugins overview](https://claude.com/docs/plugins/overview.md); [use plugins](https://support.claude.com/en/articles/13837440-use-plugins-in-claude)) |
| Pre-configured connectors | Slack and Google Drive in all 12; iManage (commercial, corporate); Ironclad, DocuSign (commercial); Definely, TopCounsel; Box; Solve Intelligence; CourtListener (`https://mcp.courtlistener.com/`: clinic, ip, litigation, law-student); Descrybe; Everlaw; Trellis; Aurora/Consilio; Courtroom5; Lawve AI; Linear, Atlassian, Asana; CoCounsel (separate plugin). **NetDocuments, Relativity, Harvey, Legora, Midpage and Datasite were announced but appear in no `.mcp.json`.** SharePoint and Microsoft 365 are not included (the knowledge-work plugins removed broken M365 HTTP entries, `181c4f6`). | Cited |
| Community skills | `legal-builder-hub` (§1.4). It installs to Claude Code's `~/.claude/skills/`, so it is effectively Claude Code-centric. | Cited / Inferred (Cowork path) |
| Third-party add-on that reuses c4l skills | (a) **Prose handoff**: c4l itself does this ("`/privacy-legal:dpa-review`"). It works if the user has the plugin installed. (b) **Declared dependency**: `plugin.json` `"dependencies": ["commercial-legal@claude-for-legal"]`. Claude Code only installs it if the root marketplace sets `allowCrossMarketplaceDependenciesOn`, or the user already has it enabled. For plugins synced from claude.ai, the dependency is met only if the user also turned on a same-name plugin. (c) **Reading c4l's profile** (`~/.claude/plugins/config/claude-for-legal/...`): technically possible, but it inherits the Cowork bug. There is no API for calling a skill from another skill. | Cited (Claude Code [dependencies](https://code.claude.com/docs/en/plugins/dependencies.md)) / Inferred (c) |

## 4. Surface support

| Component | Chat (web, desktop, mobile) | Cowork | Claude Code | Claude for Word/Excel | Source |
|---|---|---|---|---|---|
| Skills | Yes | Yes | Yes | "Skills you've enabled in your Claude settings are available in all Claude for M365 add-ins"; `/` to invoke. Plugin skills are "listed in Customize > Skills". **Known issue:** uploaded skills missing after the Sept/Oct 2026 M365 updates on Windows. | [platform-support](https://claude.com/docs/plugins/platform-support.md), [connectors-and-skills](https://claude.com/docs/office-agents/connectors-and-skills.md), [Word](https://claude.com/docs/office-agents/word.md) |
| Commands | Load as skills | `/plugin:command` | Yes | (via skills) | platform-support |
| Agents (10 in c4l) | **Ignored** | Yes | Yes | No | platform-support |
| Hooks | Ignored | Yes | Yes | No | platform-support (c4l has none anyway) |
| Remote MCP | Connect on the Connectors tab | Yes | Yes | Add-in "Connectors" | platform-support |
| Local MCP / `.mcpb` | Ignored | Only when the session runs locally | Yes | No | platform-support |
| Top-level `bin/` | **Cannot install** | **Cannot install** | Yes | – | plugins-reference |
| Plugin `CLAUDE.md` | Not loaded anywhere (c4l uses it only as a template) | | | | plugins-reference |
| Practice-profile writes to `~/.claude/...` | No persistent home (Inferred) | **Blocked or not kept** (#55, #94) | Yes | No | issues |

- **Plans.** "Plugins are available to all paid plans (Pro, Max, Ultra, Team, Enterprise)". Plugins are saved to the account and sync one way to Claude Code (v2.1.273+, not when Claude Code is signed in with an API key or runs on Bedrock or Vertex). Claude for Word is GA on Pro, Max, Team and Enterprise. (Cited)
- **Only in Claude Code:** the `/plugin marketplace add` install path in QUICKSTART, the reliable practice-profile writes, `legal-builder-hub` installs, `scripts/*` (validate, deploy). Managed Agents need the API. (Cited / Inferred)
- **Repo's own statement:** install as "a Claude Cowork or Claude Code plugin", or through Managed Agents. In Cowork: "upload a custom plugin file (any plugin directory zipped up)". QUICKSTART tells Claude Code users to install **user-scoped**, because project scope blocks reads outside the project folder. (Cited)
- **Distribution limits (Cowork and chat):** upload a `.zip` or `.plugin` holding one `.claude-plugin/plugin.json`; up to 200 MB and 5,000 files per plugin; up to 25 self-added marketplaces. Team/Enterprise Owners can set each plugin Not available / Available / Installed by default / Required, and members can share plugins with colleagues. (Cited: [Cowork plugins](https://claude.com/docs/cowork/guide/plugins.md), [admin](https://support.claude.com/en/articles/13837433-manage-plugins-for-your-organization))

## 5. Licence and attribution

| Item | Finding | Label |
|---|---|---|
| Licence | Apache-2.0 (the `LICENSE` file is the standard text). README: "Copyright 2026 Anthropic PBC." There is **no NOTICE file**, so there is no NOTICE text to carry forward. | Cited |
| Per-plugin | No `license` in any `plugin.json`. `cocounsel-legal` lives in the same repo, but "the vendor owns the code, the connector". Its files are covered by the repo licence, but treat its connector and branding as Thomson Reuters'. | Cited / Inferred |
| To redistribute (verbatim or modified) in a zip | §4(a) include a copy of the Apache-2.0 licence; §4(b) mark modified files with "prominent notices stating that You changed the files"; §4(c) keep the copyright and attribution notices in source form. Spaarke's own files may carry Spaarke's licence. | Inferred (standard Apache-2.0 reading; not legal advice) |
| Trademarks | Apache §6 grants no trademark rights. Don't present Spaarke content as Anthropic's or "Claude for Legal". Claude Code reserves plugin names beginning `claude-` or `anthropic-` (error), and warns on `claude` as a whole word anywhere. | Cited (plugins-reference) / Inferred |
| Content caveat | README: plugins "do not represent Anthropic's legal positions"; every output is "a draft for attorney review". Keep that disclaimer if redistributing. | Cited |
| Contributing back | Requires signing the CLA (`CLA.md`, CLA Assistant bot). | Cited |

## 6. Fit for Spaarke's "email a zip" v1

**Position (Inferred):** build on c4l as a **prerequisite**, not a payload. Anthropic updates its plugins through the marketplace, and a forked copy in the zip would drift (the problem r1 already solved for playbooks) and would need a new name anyway. Spaarke adds what c4l leaves out on purpose: **positions**, **engines** and **Spaarke-specific skills**.

| Element | Recommendation for v1 | Why |
|---|---|---|
| c4l plugins (`commercial-legal`, optionally `privacy-legal` and `corporate-legal`) | **Don't bundle.** The zip's `README` says: Customize › Plugins › Add marketplace › `anthropics/claude-for-legal` (or Discover, if listed) › Add `commercial-legal`. | No fork drift; Anthropic's updates arrive automatically; no licence chores. Whether it is in Discover by default is unverified. |
| **Profile seed** (Spaarke's overlay) | Ship `company-profile.md` and `commercial-legal/CLAUDE.md` **pre-filled in c4l's own format**, carrying Spaarke's NDA positions in an `### NDA triage positions` subsection under each side, plus LoL, indemnity and the rest. Mark it "attorney-reviewed by: ____" so GREEN can be issued after the customer signs off. | This is the supported customization surface. It fills the exact `nda-review` gap (PR #125) and lets the c4l skills run with Spaarke positions on day 1. |
| Where the profile lives | Use r1's approach: a user-owned file (`Documents/…/Profile/legal-profile.md`) read fresh each run. Also give Claude Code users a one-line copy step into `~/.claude/plugins/config/claude-for-legal/commercial-legal/`. | c4l's own path fails in Cowork (#55, #94) and doesn't exist in chat. |
| Spaarke add-on plugin (`spaarke-legal`, name must not start with `claude-`) | Skills only, plus optional remote OAuth MCP: `spaarke-nda-review` and `spaarke-nda-library-review` with the r1 Python engines, and the topic catalogue. Its SKILL.md gives prose handoffs to `/commercial-legal:review`, `escalation-flagger` and `stakeholder-summary`. **No top-level `bin/`** (blocks Cowork and chat install), no hooks, and no `${user_config}` in MCP URLs (Cowork won't prompt). | It runs in chat, Cowork, Code and (as skills) Word/Excel. It matches the M365 package shape (skills plus `agentConnectors`). |
| `dependencies` on `commercial-legal@claude-for-legal` | Optional, and Claude Code only. Leave it out of the v1 zip (an uploaded zip has no marketplace to resolve it against); document the install order instead. | Cross-marketplace allowlist and synced-plugin rules (§3). |
| Agents (renewal-watcher and the rest), scheduled work | **Out of v1.** They are ignored in chat, have no schedule key, and need connectors (Ironclad, Slack). | Too heavy; owner Q4 is scheduled work. |
| Managed-Agent cookbooks | **Out of v1.** They need an API key, a deploy script, a research-preview feature, and per-customer MCP wiring. | "Not production-ready" by their own README. |
| `legal-builder-hub` | Out of v1 as a dependency. Borrow its **freshness frontmatter** and licence-gate ideas for Spaarke's own content provenance. | Claude Code-centric install path. |
| Connectors | v1 recommends only CourtListener (free; c4l's citation guardrails look for it) plus whatever document store the user already has. iManage and NetDocuments wait for v2. | Others need subscriptions; NetDocuments isn't preconfigured anyway. |
| Guardrail vocabulary | Reuse c4l's tags (`[review]`, `[model knowledge — verify]`, the reviewer note, the 🔴/🟠/🟡/🟢 floor) in Spaarke skill outputs so the two products read as one. | Consistent output when a user chains c4l and Spaarke skills. Inferred. |

**Claude-specific strengths to use (Inferred from the Cited surface table):**
1. Plugin skills also work in **chat** and, through Claude settings, in **Claude for Word**. On Microsoft, third-party plugins don't run in Word (r1 L21/L25), so Claude's Word path may run Spaarke skills directly; this needs a live test, given the M365 "missing uploaded skills" issue.
2. Personal plugin upload works on Pro and Max, so the zip suits individual lawyers, not just Team/Enterprise tenants.
3. A Team/Enterprise Owner can later set the plugin to "Installed by default", which is the upgrade path from "email a zip".

## Caveats

- `main` hasn't moved in 3.5 months while many PRs are queued. The content is stable, but #55, #94, #125 and #138 are unfixed, and the profile path may change if Anthropic fixes the Cowork bug.
- Where plugin skills appear in Claude for Word is inferred from two docs plus the c4l README; not verified live.
- Not verified: whether `claude-for-legal` appears in the default Cowork **Discover** catalog, or must be added as a marketplace. Also not verified: whether Spaarke's Python engines run in chat or Cowork code execution with access to user files.
- The licence reading is a standard Apache-2.0 interpretation, not legal advice.

## Recommended follow-ups

1. Live check (Cowork plus chat): upload a zip containing a Spaarke skill and a seeded c4l profile. Does `commercial-legal:review` pick up seeded positions held in a user folder rather than `~/.claude/...`? This decides whether the overlay works without the Cowork path.
2. Live check in Claude for Word: is a plugin-installed `spaarke-nda-review` listed under `/` and run?
3. Live check: do r1's Python engines run from a skill in Cowork and in chat, and can they read the user's file?
4. Check whether `claude-for-legal` is in Cowork Discover by default (this changes the zip's install instructions).
5. Watch PR #125, issue #94 and the `NDA triage positions` section name, so Spaarke's seed stays aligned if upstream renames it.

## Sources

- **Repo (most authoritative):** `https://github.com/anthropics/claude-for-legal` @ `4a6c651`: `README.md`, `QUICKSTART.md`, `CLAUDE.md`, `CONTRIBUTING.md`, `CONNECTORS.md`, `LICENSE`, `.claude-plugin/marketplace.json`; `commercial-legal/{CLAUDE.md, .mcp.json, hooks/hooks.json, agents/*.md, skills/{cold-start-interview,review,nda-review,customize}/SKILL.md}`; `legal-builder-hub/{README.md, references/allowlist-default.yaml, skills/skill-installer/SKILL.md, skills/skills-qa/SKILL.md, skills/registry-browser/references/registries.yaml}`; `managed-agent-cookbooks/{README.md, renewal-watcher/agent.yaml}`; `references/company-profile-template.md`; `external_plugins/cocounsel-legal/README.md`; all `.mcp.json` and `plugin.json` files.
- Issues and PRs: #55, #94, #138; PRs #125 and #114–#144 (list as of 2026-11-07).
- `https://github.com/anthropics/knowledge-work-plugins/tree/main/legal` (`legal` v1.3.0; `c71b6f3` 2026-09-21).
- Anthropic docs (accessed 2026-11-07, undated pages): [Plugins overview](https://claude.com/docs/plugins/overview.md), [Plugin feature support across platforms](https://claude.com/docs/plugins/platform-support.md), [Install plugins (Cowork)](https://claude.com/docs/cowork/guide/plugins.md), [Use plugins in Claude](https://support.claude.com/en/articles/13837440-use-plugins-in-claude), [Manage plugins for your organization](https://support.claude.com/en/articles/13837433-manage-plugins-for-your-organization), [Claude for Word](https://claude.com/docs/office-agents/word.md), [Connectors and Skills in M365 add-ins](https://claude.com/docs/office-agents/connectors-and-skills.md), [Claude for the legal industry: product overview](https://claude.com/resources/guides/claude-for-the-legal-industry/product-overview), [Claude Code plugin manifest reference](https://code.claude.com/docs/en/plugins-reference), [Claude Code plugin dependencies](https://code.claude.com/docs/en/plugins/dependencies.md).
- Press: [LawSites 2026-05-12](https://lawnext.com/2026/05/anthropic-goes-all-in-on-legal-releasing-more-than-20-connectors-and-12-practice-area-plugins-for-claude.html), [ABA Journal](https://www.abajournal.com/news/article/anthropic-launches-claude-for-legal-giving-lawyers-20-new-program-integrations-and-12-practice-area-plugins/).
- Background: `projects/spaarke-legal-ai-accelerator-claude-r2/research.md`; r1 researcher notes `legal-skill-content-sources-2026-10-31.md` and `copilot-legal-taxonomy-and-practice-pack-dev-refs-2026-10-29.md`.
