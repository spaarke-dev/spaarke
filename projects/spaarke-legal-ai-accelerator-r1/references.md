# Spaarke Solution Accelerator R1 — Reference Material

> **Purpose:** the curated starting set for anyone building or reviewing this project, human or Claude Code. Every link was checked on 2026-10-29; dates are the page's `ms.date` (last update in parentheses where different).
> **Shorthand:** `L/` = `https://learn.microsoft.com/en-us/` · `CW/` = `L/copilot/cowork/` · `X/` = `L/microsoft-365/copilot/extensibility/`
> **Rule:** these products change monthly. When a page here disagrees with training-data knowledge, the page wins; when two pages disagree, note it in `notes/` and test in the tenant. Snapshots for Claude Code live in `knowledge/` (design.md §11).

---

## 1. Microsoft Copilot for legal

| Reference | What it teaches |
|---|---|
| [Manage Copilot for legal](https://learn.microsoft.com/en-us/microsoft-365/admin/manage/manage-copilot-for-legal?view=o365-worldwide) (2026-10-13) | **Authoritative taxonomy**: two legal plugins, Word and Cowork; Word policy "Legal Skills in M365 Copilot in Word"; Cowork plugin "Legal for Cowork" |
| [Get started with Copilot for legal](https://support.microsoft.com/en-us/word/get-started-with-copilot-for-legal) (undated) | The Word playbook-review flow: `.docx` playbook → topics, rules, example clauses → Run Review → tracked changes; drafting from precedent |
| [Copilot for Legal transparency note](https://support.microsoft.com/en-au/word/copilot-for-legal-transparency-documentation) (undated) | Intended uses and limits; "a set of legal capabilities for Copilot in Word desktop and Cowork" |
| [Introducing Microsoft Copilot for legal](https://techcommunity.microsoft.com/blog/microsoft-copilot-blog/introducing-microsoft-copilot-for-legal/4564246) (2026-10-15) | Launch scope and partners. Marketing: several claims have no documented step (strategy §3.4). WebFetch returns only the title; read raw HTML |
| [MC1509799](https://mc.merill.net/message/MC1509799) (2026-10-15, mirror) | GA rollout to mid-December; Legal Agent retirement |
| [Get started with the Legal Agent (Frontier)](https://support.microsoft.com/en-us/word/get-started-with-the-legal-agent-frontier) | Legacy page; says a playbook is converted into a skill |
| [Available plugins for Cowork](https://learn.microsoft.com/en-us/copilot/cowork/cowork-available-plugins) (2026-10-26) | The 20 Legal for Cowork skill names: the **overlap guard** for our skills |

## 2. Copilot Cowork: plugins and skills

| Reference | What it teaches |
|---|---|
| [**Build plugins for Cowork**](https://learn.microsoft.com/en-us/copilot/cowork/cowork-plugin-development) (2026-10-02, upd 10-21) | **The primary build reference.** Zip layout, SKILL.md rules, validation codes (ASKILL-M/P), connectors and auth, MCP tool annotations, base64 file inputs, Claude-plugin conversion (`atk import openplugin`), what is dropped |
| [Use Cowork](https://learn.microsoft.com/en-us/copilot/cowork/use-cowork) (upd 10-21) | User skills (50 max; 1 MB SKILL.md; ≤ 20 companions / 10 MB), how tasks run, outputs to the OneDrive Cowork folder |
| [Customize Cowork](https://learn.microsoft.com/en-us/copilot/cowork/cowork-customize) (upd 10-21) | Uploading skills (.md/.zip/.skill) and plugins; sharing "Only you" / "Specific users" |
| [Cowork plugins](https://learn.microsoft.com/en-us/copilot/cowork/cowork-plugins) (upd 10-29) | User plugin upload, Claude package auto-conversion, per-user connector sign-in |
| [Manage Cowork plugins](https://learn.microsoft.com/en-us/copilot/cowork/cowork-manage-plugins) (upd 10-29) | Admin side; "no single tenant setting that turns off plugin sharing"; org-wide publishing approval |
| [**MCP apps plugin author guide for Cowork**](https://learn.microsoft.com/en-us/copilot/cowork/mcp-apps-support) (2026-09-08, upd 10-21; also at `/microsoft-365/copilot/cowork/mcp-apps-support`) | **The UI build reference.** Covers: SEP-1865 model (`ui://` `resourceUri` on the tool → HTML via `resources/read`, `text/html;profile=mcp-app`, self-contained); inline/fullscreen only; widget may call only `tools/call`, `resources/read`, `ui/message` (+ `ui/open-link` with confirmation); auth attached by Cowork, never in the iframe; `visibility`, CSP, permissions, `validDomains[]` (fail-closed); limits (64 KiB, 60 calls/min, 10 s init, 200–720 × 640 px); widget vs elicitation guidance; quick checklist. No GA/preview label. Note: `.claude/agent-memory/researcher/cowork-mcp-apps-author-guide-2026-10-30.md` |
| [MCP Apps spec (SEP-1865, 2026-01-26)](https://github.com/modelcontextprotocol/ext-apps/blob/main/specification/2026-01-26/apps.mdx) | The open standard Cowork implements; SDK `@modelcontextprotocol/ext-apps` |
| [Elicitation forms](https://learn.microsoft.com/en-us/copilot/cowork/cowork-elicitation-forms) (2026-05-27) | MCP form-mode elicitation; flat schemas only |
| [Cowork projects](https://learn.microsoft.com/en-us/copilot/cowork/cowork-projects) (2026-10-28) | Projects are private to the creator |
| [Cowork access](https://learn.microsoft.com/en-us/copilot/cowork/cowork-access) (upd 10-21) · [Admin governance](https://learn.microsoft.com/en-us/copilot/cowork/cowork-admin-governance) (10-27) · [FAQ](https://learn.microsoft.com/en-us/copilot/cowork/cowork-faq) (upd 10-29) · [Models](https://learn.microsoft.com/en-us/copilot/cowork/cowork-models) (10-24) · [What's new](https://learn.microsoft.com/en-us/copilot/cowork/whats-new) (10-29) | Spending-policy access; Purview; by-design limits (no local or encrypted files, no deletes); model picker (EU Data Boundary = GPT only) |
| [Manage plugins, skills and MCP servers](https://learn.microsoft.com/en-us/microsoft-agent-365/admin/manage-plugins-skills-mcp-servers) (10-14, upd 10-23) | Plugin registry, MCP server consent, per-tool toggles |
| [Plugin authentication](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/plugin-authentication) (10-28) | OAuth registration → `referenceId` (OAuthPluginVault) |
| [Usage-based billing and the USL](https://learn.microsoft.com/en-us/microsoft-365/copilot/user-subscription-license-usage-based-billing) (10-05) · [Manage Copilot Credits](https://learn.microsoft.com/en-us/microsoft-365/copilot/usage-based-billing-manage-copilot-credits) (10-28) | Credits plus licence; minimum policy limits |

**Tooling**
- `wiqd` CLI (preview): [docs](https://microsoft.github.io/wiqd/) · [CLI reference](https://microsoft.github.io/wiqd/cli/reference/) · repo `microsoft/wiqd` @ `8bd50d04` (v0.22.0). `plugin validate --mode deep|provision|package|share`; `plugin export --format claude-plugin`.
- Microsoft 365 Agents Toolkit CLI `atk` ≥ 1.1.12: `atk import openplugin` / `atk export openplugin`; `atk package`; `atk install --scope Personal`.

## 3. SharePoint and Agent Builder (T1)

| Reference | What it teaches |
|---|---|
| [Skills in Copilot in SharePoint](https://learn.microsoft.com/en-us/sharepoint/copilot-in-sharepoint-skills) (2026-09-28) | Site skills at `/Agent Assets/Skills/<name>/SKILL.md`; Edit creates, View runs; no external systems or custom code |
| [Upgrade SharePoint agents](https://learn.microsoft.com/en-us/sharepoint/upgrade-sharepoint-agents) (10-15) | `.agent` files retiring: none new after mid-November 2026, broken after March 2027 |
| [Agent Builder](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/agent-builder) (10-26) · [Add skills](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/agent-builder-add-skills) (10-26) · [Declarative agent skills](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/declarative-agent-skills) (10-26) | ≤ 8 skills per agent; .zip ≤ 50 MB; instructions < 20,000 chars; ≤ 350 files; depth 3; scripts run without network; ISVs must not submit agents with skills to Partner Center |
| [Add knowledge](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/agent-builder-add-knowledge) (09-30) · [Share and manage](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/agent-builder-share-manage-agents) (upd 10-12) | 100 SharePoint files, 50 OneDrive files, 20 embedded files; sharing to users, groups, link, org |
| [Site templates and site scripts](https://learn.microsoft.com/en-us/sharepoint/dev/declarative-customization/site-design-overview) (10-28) | Creating templates needs an admin; site owners can only apply them |

## 4. Interactive UI (T2, for reference)

| Reference | What it teaches |
|---|---|
| [Copilot UX components are GA](https://devblogs.microsoft.com/microsoft365dev/copilot-ux-components-are-generally-available-bring-interactive-experiences-into-copilot/) (2026-10-28) | Announcement |
| [SPFx Copilot apps overview](https://learn.microsoft.com/en-us/sharepoint/dev/spfx/copilot/overview-copilot-apps) (10-28) | SPFx components: Chat and Teams only; admin deploy; Copilot licence |
| [MCP Apps in declarative agents](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/plugin-mcp-apps) (09-30) · [UI guidelines](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/plugin-mcp-apps-ui-guidelines) (07-02) · [Build MCP plugins](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/build-mcp-plugins) (09-30) | MCP Apps model, bridge calls, sandbox, limits |
| [Declarative agent manifest 1.9](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/declarative-agent-manifest-1.9) (upd 10-22) | `agent_skills`, `Fabric` capability |

## 5. Agent Skills format and authoring practice

| Reference | What it teaches |
|---|---|
| [Agent Skills specification](https://agentskills.io/specification) | The open format: `name` ≤ 64 chars, `description` ≤ 1,024, optional `license`/`compatibility`/`metadata`; keep SKILL.md under 500 lines |
| `agentskills/agentskills` @ `69ef37e9` — `skills-ref` | Reference validator (`skills-ref validate`) |
| [Skill authoring best practices](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices) (Anthropic) | Third-person descriptions with triggers; references one level deep; ≥ 3 evals per skill; test across models; eval-first |
| [Agent Plugins](https://agent-plugins.org/) | Agent Plugins 1.0.0 layout |

## 6. Legal content sources

| Source | Licence | Use in this project |
|---|---|---|
| [anthropics/claude-for-legal](https://github.com/anthropics/claude-for-legal) @ `4a6c651` | Apache-2.0 | **Method and guardrails**: cold-start interview, side-of-deal logic, `[verify]` tagging, scope traps. 13 plugins, 151 skills; ships no default positions; does **not** import into Cowork as-is; adapt it to read our legal playbook files (design.md §5.3, §5.5) |
| [Open-Legal-Products/mike-workflows](https://github.com/Open-Legal-Products/mike-workflows) @ `20bed20` | MIT (per-skill authors; `PROVENANCE.md`) | **Checklist content**: NDA, employment and commercial-lease review plus tabular column sets. Jurisdiction-general; lease content is UK law |
| [Open-Legal-Products/mikegc-workflows](https://github.com/Open-Legal-Products/mikegc-workflows) | Apache-2.0 + NOTICE | In-house skills (contract intake, markup, obligation tracking) |
| `C:\code_files\lavern-reference-vault` @ `7c2efe6` (Lavern v0.15.0) | Apache-2.0 + NOTICE | **Evaluation method** (`evals/jv/RUBRIC.md`: pre-registered rubric, recall/precision/hallucination counts); NDA templates; U.S. lease checklist lines in `src/agents/prompts/real-estate-counsel.ts` |
| CUAD (Contract Understanding Atticus Dataset) | CC BY 4.0 | Evaluation documents, with attribution |

## 7. Microsoft samples

| Sample | What it shows |
|---|---|
| `microsoft/copilot-camp` @ `b5b1f8a1` — `src/cowork/` | `CopilotDevCamp-for-cowork` (3 skills + no-auth MCP connector), `zava-claims-sso` (OAuthPluginVault connector), `weekly-status-mail` (single skill); labs in `docs/pages/copilot-cowork/` (CWRK0–04) |
| `microsoft/cat-agent-skills` @ `3678d23a` (MIT) | Power CAT skill gallery: `vendor-contract-risk-review`, `redlining-content`, `cowork-plugin-engineer`, `skill-authoring-coach`, `_template` |

## 8. Spaarke internal

| Reference | Why |
|---|---|
| [solution-strategy.md](solution-strategy.md) | Program strategy (v0.3) |
| `projects/spaarke-ontology-platform-r1/notes/spaarke-ontology-strategy-synopsis-v2.md` §2.5b | Microsoft platform state, October 2026 |
| `.claude/agent-memory/researcher/*-2026-10-29.md` | Evidence behind every claim above: `copilot-for-legal-mechanics-walkthrough`, `copilot-legal-taxonomy-and-practice-pack-dev-refs`, `copilot-legal-enablement-kit-deployment-paths`, `copilot-ux-components-and-kit-package-anatomy`, `practice-pack-content-sources`, `ms-copilot-for-legal-cowork-registry` |
| `infrastructure/ai-foundry/evaluation/` | Existing legal evaluation config and citation-accuracy metric |
| `knowledge/README.md`, `knowledge/REFRESH-PROCEDURE.md` | Knowledge-base conventions for design.md §11 |
