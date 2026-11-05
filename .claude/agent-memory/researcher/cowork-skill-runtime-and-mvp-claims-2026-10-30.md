---
name: cowork-skill-runtime-and-mvp-claims-2026-10-30
description: Cowork skill runtime: OneDrive /Documents/Cowork/skills install, 50-skill cap, scripts/ executed (runtime undocumented), /skill-name invocation, Automations (user-owned; ISVs supply triggers only), Adaptive Cards vs MCP Apps vs SPFx; MVP redliner claims checked
metadata:
  type: reference
---

# Copilot Cowork skill runtime: state as of 2026-10-30

Prior context: this builds on researcher notes from 2026-10-29 (Cowork plugin and skill limits, legal kit deployment paths, UX components). What's new here: the runtime and sandbox evidence, triggers from non-Microsoft systems, how skills get picked or invoked, and checking the MVP's claims one by one.

Labels: **C** = Cited (a primary source says it). **I** = Inferred (follows from primary sources but isn't stated). **U** = Unknown (no primary source).

## Primary sources (dates are ms.date / updated_at)
- [S1] use-cowork: https://learn.microsoft.com/en-us/copilot/cowork/use-cowork (ms.date 2026-09-14, updated 2026-10-21). The MVP's URL (`/microsoft-365/copilot/cowork/use-cowork`) redirects here.
- [S2] cowork-customize: https://learn.microsoft.com/en-us/copilot/cowork/cowork-customize (2026-09-14, updated 2026-10-21)
- [S3] cowork-plugins: https://learn.microsoft.com/en-us/copilot/cowork/cowork-plugins (2026-08-27, updated 2026-10-29)
- [S4] cowork-plugin-development: https://learn.microsoft.com/en-us/copilot/cowork/cowork-plugin-development (2026-10-02, updated 2026-10-21)
- [S5] cowork-faq: https://learn.microsoft.com/en-us/copilot/cowork/cowork-faq (2026-09-29, updated 2026-10-29)
- [S6] whats-new: https://learn.microsoft.com/en-us/copilot/cowork/whats-new (2026-10-28, updated 2026-10-29)
- [S7] cowork-admin-governance: https://learn.microsoft.com/en-us/copilot/cowork/cowork-admin-governance (2026-09-14, updated 2026-10-30)
- [S8] Application card: https://learn.microsoft.com/en-us/copilot/cowork/responsible-ai/copilot-cowork-application-card (2026-09-22, updated 2026-10-29)
- [S9] cowork-manage-plugins: https://learn.microsoft.com/en-us/copilot/cowork/cowork-manage-plugins (2026-08-31, updated 2026-10-29)
- [S10] mcp-apps-support: https://learn.microsoft.com/en-us/copilot/cowork/mcp-apps-support (2026-09-08, updated 2026-10-21)
- [S11] Cowork overview: https://learn.microsoft.com/en-us/copilot/cowork/ (2026-09-29, updated 2026-10-29)
- [S12] cowork-network-endpoints: https://learn.microsoft.com/en-us/copilot/cowork/cowork-network-endpoints (2026-09-04, updated 2026-10-21)
- [S13] Tech Community "What's New in Cowork: Expanded plugins and role-based skills": https://techcommunity.microsoft.com/blog/microsoft-copilot-blog/what%E2%80%99s-new-in-cowork-expanded-plugins-and-role-based-skills/4566221 (published 2026-10-21)
- [S14] Tech Community "Cowork in progress" (running release log): https://techcommunity.microsoft.com/blog/microsoft-copilot-blog/cowork-in-progress/4511672 (published 2026-05-05, modified 2026-10-21)
- [S15] declarative-agent-skills: https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/declarative-agent-skills (2026-10-26, updated 2026-10-27)
- [S16] excel-skills (preview): https://learn.microsoft.com/en-us/office/dev/add-ins/excel/excel-skills (2026-08-13)
- [S17] manage-public-web-access: https://learn.microsoft.com/en-us/microsoft-365/copilot/manage-public-web-access (2026-10-29)
- [S18] Microsoft APM, Copilot Cowork integration: https://microsoft.github.io/apm/integrations/copilot-cowork/ (last updated 2026-10-22, experimental)
- [S19] microsoft/copilot-camp lab CWRK01: `docs/pages/copilot-cowork/01-cowork-skills.md` (latest commit to the folder 1ef8b368, 2026-10-20), with samples under `src/cowork/`
- [S20] microsoft/wiqd v0.22.0 (2026-10-29, preview). It has nothing on the runtime.
- Prior note (2026-10-29): SPFx Copilot UX components went GA on 2026-10-28 (devblogs microsoft365dev). They render in M365 Copilot web/desktop and Teams, but not yet in Cowork.

---

## Verdict on the MVP's claims

| MVP claim | Verdict | Evidence |
|---|---|---|
| Custom skills live in OneDrive `/Documents/Cowork/Skills/<name>/` and installing means copying a folder | **Mostly true.** Learn writes the folder in lowercase (`/Documents/Cowork/skills/`). OneDrive paths are case-insensitive, so this is cosmetic. (C, I) | S1: "Create a subfolder... create a file named SKILL.md... Cowork discovers your custom skills automatically at the start of each session." S18: Microsoft's own APM tool installs skills by copying the folder to `<onedrive-root>/Documents/Cowork/skills/<name>/`. |
| Built-in skills sit at `/opt/workspace-config/.claude/skills/`; user skills mount at `/mnt/user-config/.claude/skills/<name>/` | **Unverifiable (U).** No primary source mentions either path. S7 says users "can't view or access this environment". | It looks like output from asking the agent to list its own container. Treat it as an implementation detail that could change. Don't hard-code these paths in skills. |
| A skill folder holds SKILL.md + `references/` + `scripts/` (Python stdlib, JSON out) + `assets/` (templates with [PLACEHOLDER]) | **Partly true.** `references/` and `scripts/` are documented (C). `assets/` isn't named in the Cowork docs, but any companion file is allowed (I). "Stdlib only" and "JSON output" aren't documented (U). [PLACEHOLDER] substitution is something the model does, not a platform feature (I). | S4: layering table (frontmatter → body → `references/` on demand → `scripts/` "Executed, not loaded into context"). Example `scripts/extract-clauses.py`. |
| Built-ins: docx, xlsx, pptx, pdf, render-ui (Adaptive Card), calendar-management (+`scripts/analyze_calendar.py`), daily-briefing, meeting-intel, stakeholder-comms | **The display names match (C). The internal folder names and the script can't be verified (U).** | S1 table: Word, Excel, PowerPoint, PDF, Email, Scheduling, Calendar Management, Meetings, Daily Briefing, Enterprise Search, Deep Research, Communications, **Adaptive Cards**, App (Frontier). S8 adds "Skill Management". S14 has a `/cost` skill. S1 has an image-generation skill. |
| A skill can call built-in skills and run them as parallel sub-agents | **Calling built-ins: yes (C/I). Parallel sub-agents: no documentation, and the docs point the other way.** | S3: plugin skills "coordinate with other active skills". S8: Cowork "executes them **sequentially**". S4: Claude-plugin `agents/` (sub-agents) are "Not yet supported". |
| A skill can web-fetch public sites (e.g. legislation.gov.uk) | **Probably true for the agent (I). Unknown for scripts.** | S17: Cowork web search is governed by the "Allow web search in Copilot" policy, and Cowork has no user toggle for it. S19's lab skill builds a deck from a public URL the user supplies. No source covers network access from skill scripts. In declarative-agent skill sandboxes, scripts have **no network** (S15). |
| Limits: max 20 custom skills; 1 MB per SKILL.md | **20 is out of date. The limit is now 50 (C). 1 MB is correct (C).** | S6 (April 2026): "You can now add 50 custom skills". S1/S5/S8/S11: 50. The 20 figure is the per-plugin-package skill cap (ASKILL-M002) and also the companion-file cap per skill. |

---

## Q1. Automations
**Answer.** "Automations" is the Cowork page that lists two kinds of user-owned unattended tasks:
1. Scheduled prompts (recurring), capped at 25 per user.
2. Event-driven tasks. These fire on a matching email, a Teams chat or channel message, or an @mention. Since October 2026 they can also fire on events from non-Microsoft systems through plugins and Power Platform connectors.

Users create both in natural language in chat, through **Automations > Create** (S1), or through **Automations > Add > Create with Cowork** (S13). Cowork proposes a "Set up trigger?" card with three parts, **When / Run in / What it does**, plus the permissions it needs. Tasks are draft-and-approve by default.

ISVs don't define automations. They define **trigger events**: "Supported triggers are defined by partners and can be found within the documentation for each plugin or connector" (S13). There's no documented manifest field that lets a plugin or skill ship a ready-made automation; the user always arms it. Billing is usage-based. The docs give no automation-specific rate.

- C: 25 scheduled prompts. States are Active / Draft / Paused. Run history is kept. Tasks run as the creating user, with rate limits, loop protection, the audit log and Purview (S1, S7).
- C: The October 2026 whats-new entry is "Non-Microsoft event-driven triggers... such as when a new case is added in a CRM" (S6). The legal example given is "When a new contract lands in our [document system] legal intake folder, review it against our playbook" (S13).
- C: Usage-based billing counts "model responses, tool and skill calls, image generation, and browser tasks" (S5, S7).
- I: Each automated run uses Copilot Credits the same way an interactive task does.
- I: Triggers in partner connectors most likely use the Power Platform connector trigger model.
- U: How an MCP-only plugin declares a trigger. No authoring doc exists.
- U: Any per-user cap on event-driven tasks (the docs only mention "rate limits").
- U: Whether a SKILL.md can pre-define a schedule. It can only instruct Cowork to *propose* one.

Confidence: High on what automations are and how they're created. Low on how ISVs author triggers.

## Q5. Plugin and skill counts
**Answer.**
- **Custom skills per user: 50** (S1, S5, S8, S11, S19). Each SKILL.md is ≤1 MB, with ≤20 companion files per skill at ≤5 MB each and ≤10 MB in total (S1, S4, S8).
- **Skill upload archive:** ≤10 MB compressed, ≤50 MB uncompressed, ≤100 files (S2). This conflicts with the 20-companion rule; design to 20.
- **Per plugin package:** ≤20 skills (ASKILL-M002) and ≤10 connectors (S4 FAQ).
- **Plugins per user or per tenant:** no documented limit (U).
- **Do plugin skills (including Microsoft's Legal for Cowork, with 20 skills) count toward the 50?** Not documented (U). They probably don't (I): S4 says plugin skills carry source `"package"`, while the 50 limit is worded as skills *you create*.
- Plugins can't be scoped by country or region; admins use security groups instead (S9).
- In Information Barriers tenants, uploading and sharing plugins or skills is blocked (S4, S9).

Confidence: High on 50 / 1 MB / 20-per-package. Unknown on per-user plugin limits.

## Q6. How Cowork picks a skill
**Answer.** Cowork matches on descriptions, using progressive disclosure:
- Every skill's `name` and `description` (~100 tokens) loads at startup.
- The body loads when the skill triggers.
- `references/` loads on demand (S4).

Users can also invoke a skill explicitly with **`/skill-name`**. Microsoft's own examples include `/compare-key-terms`, `/create-playbook`, `/app` and `/cost` (S13, S14). Users can also pick skills through the **+ menu > Skills** (on mobile, S5) or the conversation **Sources** picker (S2). A plugin can be switched on or off for each conversation in **Sources & Skills**; that setting is saved per device (S3, S9).

On name clashes the docs conflict:
- S3: "If a plugin skill has the same name as a built-in skill, the agent determines which skill to use."
- S4 FAQ: plugin skills "can't override built-in skills of the same name".

To reduce overlap, Cowork's automatic evaluation runs a **conflict scan**. When two skills compete for the same requests, it recommends "an explicit handoff between them" (S1).

- C: The description limit is 1–1024 characters (S4). A description that's too long means the skill "never loads", and one with no trigger phrases "never fires" (S1).
- I: For a daily-email plugin next to a legal-review plugin, use narrow trigger phrases and explicit "do not use for X — use /other-skill" handoff lines. Teach attorneys the `/` invocation.

Confidence: High.

## Q7. What a skill folder can contain, and the sandbox
**Answer.** Only these rules are documented:
- `SKILL.md` must sit at the skill root, with frontmatter `name` (kebab-case, 1–64 characters, equal to the folder name) and `description`. Optional fields such as `license` and `metadata` appear in Microsoft's example (S4).
- Any other files count as companion files. Paths must be relative, with no `..`, no hidden files and no reserved names. Allowed characters are alphanumerics, `-`, `_`, `.`, spaces and `!` (S4).
- Learn names `references/` and `scripts/`. `assets/` isn't named but is allowed (I).
- Only `scripts/` is described as "executed".

The Cowork docs **don't name the interpreter, the Python version, the preinstalled packages, the OS, network egress for scripts, or time limits** (U).

What is documented about the environment (S7):
- Tasks run in a "temporary, isolated environment inside the Microsoft 365 service boundary".
- The environment is removed when the task ends, and users can't access it.
- Memory is scoped to the conversation (S8).
- Files persist only in OneDrive or SharePoint (the output folder `OneDrive/Cowork`, S1).
- Companion files must download within 15 seconds (S4).

Comparison point: for declarative-agent or Agent Builder skills (S15), the sandbox runs `.py .js .mjs .cjs .ts .mts .sh .bash`, with **no network, no package installs, and "Don't depend on a package unless its availability is confirmed."**

- I: Python is the one language Microsoft shows for Cowork scripts. Treat stdlib-only as the safe baseline. Probe for `python-docx` or `openpyxl` before relying on them.
- I: The web search the agent uses is Bing grounding (S17), not egress from scripts. Cowork's network endpoints doc (S12) lists only client-side endpoints.

Confidence: High on the folder rules. Unknown on runtime specifics.

## Q9. Adaptive Cards vs MCP Apps vs SPFx Copilot UX components

**Adaptive Cards in Cowork**
- A built-in **Adaptive Cards** skill "generates interactive card-based responses with structured layouts, buttons, and data displays in the conversation" (S1, S8).
- A custom skill produces one by telling Cowork to present results as an Adaptive Card (I).
- There's no documented contract for a skill to supply card JSON or templates (U).
- Action.Submit round-trip behaviour isn't documented. "Buttons" are mentioned but their semantics aren't (U).
- Renders in the Cowork conversation. Teams messages Cowork posts can carry formatted cards (S6, April 2026).
- No install is needed beyond the skill.

**MCP Apps**
- A plugin's remote MCP connector declares `_meta.ui.resourceUri` and serves `text/html;profile=mcp-app`. Cowork renders it inline or fullscreen in a sandboxed iframe (S10).
- Supported: `tools/call` from the widget, `ui/message`, and `open-link` (with a confirmation).
- Not supported: `ui/update-model-context`, PiP, and streaming.
- Limits: 64 KiB inline result; 200–720 px wide by ≤640 px tall; 60 widget calls per minute per conversation; `ui/initialize` must complete within 10 s; CSP domains must be listed in `validDomains`.
- A user can upload the plugin and share it without admin rights (S2, S9). A hosted HTTPS MCP server is still required.
- Also renders in M365 Copilot Chat for declarative agents with an MCP plugin (prior note, plugin-mcp-apps 2026-09-30).

**SPFx Copilot UX components (GA 2026-10-28)**
- Need an admin to deploy them to the tenant app catalog, and each user needs the M365 Copilot add-on licence.
- Render in M365 Copilot web/desktop and Teams. **Not Cowork yet** (prior note).

**When to use which** (I):
- Adaptive Card: a read-only summary or dashboard in Cowork with zero infrastructure.
- MCP App: interactive, data-backed UI in Cowork (and Chat) from an ISV backend.
- SPFx: a Chat/Teams experience the customer's IT installs and hosts in SharePoint.

Confidence: High for MCP Apps and SPFx. Medium-low for how Adaptive Cards are authored.

## Q11. How scripts in a skill run
**Answer.**
- **Who invokes them:** the agent decides, guided by what SKILL.md says. Scripts are "Executed, not loaded into context" (S4). They run in the background without visible steps (S1).
- **Input/output:** not documented (U). JSON on stdout is a sensible convention, not a contract (I).
- **Size:** ≤5 MB per file, ≤10 MB per skill (S4).
- **Safety checks:** Cowork's evaluation statically scans bundled code and never executes it during static checks (S1).

The same package format runs in other hosts with **different script runtimes**:
- **Agent Builder / declarative agents (Copilot Chat):** scripts run in a no-network sandbox (S15). Limits are 8 skills per agent and SKILL.md under 20,000 characters.
- **Copilot in Excel (preview):** runs `scripts/*.js` as Office.js in Excel's own runtime. Microsoft's guidance tells skill authors to *not* run these scripts when the skill is invoked outside Excel (S16).
- **SharePoint site skills:** can't run code (prior note, copilot-in-sharepoint-skills 2026-09-28).
- **OneDrive custom skills appearing in Copilot Chat:** not documented (U).

Confidence: Medium.

## Q12. Other code file types
**Answer.**
- **Bundling:** any safe-named file can be bundled as a companion (S4). Cowork also accepts code files as attachments and previews them (S1).
- **Execution in Cowork:** only `scripts/*.py` appears in Microsoft's Cowork examples.
- **`.ts/.js/.sh` in Cowork:** U. They are executable in the declarative-agent sandbox (S15), and `.js` runs in Excel skills (S16).
- **`.cs` and `.ps1`:** not on any Microsoft executable list. Assume they don't run (I).
- **Claude-plugin conversion:** `bin/` executables are "Not applicable" and `hooks/` are "Not yet supported" (S4).

Confidence: Medium on "Python yes", low on the rest.

---

## Corrections to our assumptions
1. The custom-skill cap is **50**, not 20 (S6, April 2026). The MVP's 20 is stale, or it's being confused with the 20-skills-per-plugin-package cap (ASKILL-M002). It's also the 20-companion-files-per-skill cap.
2. Installing by copying a folder into OneDrive `/Documents/Cowork/skills/<name>/` **is** a supported path, alongside Customize > Upload (.md/.zip/.skill) and plugin upload. Microsoft's APM tool installs this way (S18).
   - Discovery happens at session start.
   - Folder copies can't be shared through the Share dialog the way uploaded or created skills can (I).
   - Custom skills don't work on mobile (S1).
3. Explicit `/skill-name` invocation exists and is how Microsoft demos plugin skills. Our accelerators should give skills short, memorable kebab names.
4. Don't rely on parallel sub-agents. Microsoft's documentation describes sequential execution, and Claude `agents/` aren't supported.
5. The Cowork sandbox (OS, Python version, packages, egress, time limits) is **undocumented**. The `/opt/...` and `/mnt/...` paths are unofficial. Accelerators should:
   - stick to stdlib-only Python;
   - degrade gracefully when a script can't run;
   - avoid script-side network calls (the declarative-agent sandbox has none);
   - fetch web content through the agent, not through scripts.
6. The docs conflict on built-in vs plugin skill name clashes (S3 vs S4). Never reuse built-in names (`word`, `email`, `calendar-management`, and so on).
7. ISVs can supply **trigger events** through connectors and plugins (October 2026). They can't supply ready-made automations. The user arms each one.
8. Adaptive Cards in Cowork are produced by a built-in skill. There's no documented card-JSON contract. For interactive, data-backed UI in Cowork, MCP Apps are the documented route. SPFx UX components don't render in Cowork.

## Reference links
- https://learn.microsoft.com/en-us/copilot/cowork/use-cowork#create-custom-skills (the MVP's source; it redirects from `/microsoft-365/copilot/cowork/...`)
- https://learn.microsoft.com/en-us/copilot/cowork/cowork-plugin-development (package format, ASKILL rules, companion limits, progressive loading)
- https://learn.microsoft.com/en-us/copilot/cowork/cowork-customize (upload limits, sharing)
- https://learn.microsoft.com/en-us/copilot/cowork/mcp-apps-support
- https://learn.microsoft.com/en-us/copilot/cowork/cowork-admin-governance#automated-tasks
- https://techcommunity.microsoft.com/blog/microsoft-copilot-blog/what%E2%80%99s-new-in-cowork-expanded-plugins-and-role-based-skills/4566221 (triggers from non-Microsoft systems; `/skill` examples)
- https://techcommunity.microsoft.com/blog/microsoft-copilot-blog/cowork-in-progress/4511672 (running release log)
- https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/declarative-agent-skills (the only documented sandbox spec, for declarative agents)
- https://learn.microsoft.com/en-us/office/dev/add-ins/excel/excel-skills (Office.js scripts in skills)
- https://microsoft.github.io/apm/integrations/copilot-cowork/ (the OneDrive copy-install path)
- https://github.com/microsoft/copilot-camp/tree/main/src/cowork and https://microsoft.github.io/copilot-camp/pages/copilot-cowork/01-cowork-skills/
- https://microsoft.github.io/wiqd/

## The MVP's source
I couldn't find a public original. A video titled "Copilot Cowork: Enhanced SKILLS setup - Legal Redlining as the showcase!" appears only on a low-quality aggregator (new.ccum.net, which was unreachable). It names no creator, and I found no GitHub repo or blog. Searches for "legal-redliner" and "Advance Skill topology" returned nothing.

## Recommended follow-ups (empirical; the docs are silent)
1. In a test tenant, run a probe skill whose `scripts/probe.py` prints `sys.version`, `platform.platform()`, attempts to import `docx`, `openpyxl` and `pandas`, and attempts an HTTPS GET. Compare the result with the MVP's paths.
2. Check whether a skill installed by copying a folder into OneDrive gets a Skill Report evaluation and shows up in "Your skills".
3. Check whether plugin skills count toward the 50 custom-skill limit (install Legal for Cowork and then add 50 custom skills).
4. Check whether Adaptive Card buttons (Action.Submit / Action.Execute) feed back into the conversation.

---

## Addendum 2026-11-05: re-check of documented script execution (no change)
**Question**: Does Learn still document `scripts/` as executed in Cowork skills, and did anything change since 10-30?
**Findings**: No change. cowork-plugin-development (ms.date 2026-10-02, updated 2026-11-03) still lists "Scripts (`scripts/`) | Executed, not loaded into context" and `scripts/extract-clauses.py`; still no interpreter, packages, egress or time-limit text. use-cowork (ms.date 2026-11-03, updated 2026-11-04) adds only "Some background operations, like script execution, run without displaying individual steps" (already known) and repeats 50 skills / 1 MB SKILL.md / 20 companion files / 10 MB per skill. whats-new (Nov 2026) has only the Planner plugin; no skill or script entries. cowork-faq repeats 50 skills; admin pages (agent365 manage-plugins-skills-mcp-servers, ms.date 2026-10-14) say nothing on scripts. No Cowork MC/roadmap item in 10-15..11-05 touches scripts (MC1493223 "Skills in Copilot Chat" is 2026-10-09, says skills may include "supporting files or scripts", nothing on runtime). Caveat: MC check was via mc.merill.net title listing only (bodies not read; 200 of 4,059 shown).
**Sources**: learn.microsoft.com/en-us/copilot/cowork/{use-cowork,cowork-plugin-development,cowork-faq,whats-new,cowork-admin-governance}; learn.microsoft.com/en-us/microsoft-agent-365/admin/manage-plugins-skills-mcp-servers; mc.merill.net/message/MC1493223
**Open questions**: Runtime still undocumented; our tenant evidence (Python 3.12.15, lxml 6.1.3, python-docx 1.2.0) remains the only source for versions/packages. Note: the old `/microsoft-365/copilot/cowork/` URL for cowork-plugin-development now 404s; use `/copilot/cowork/`.
