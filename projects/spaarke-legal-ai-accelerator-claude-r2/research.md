# Research: a Claude edition of the Legal AI Accelerator (r2)

> **Created** 2026-11-07, from the r1 session's review of the owner's Microsoft 365 app-package research.
> **Status:** research only, pre-design. Next step: `design.md` (then `/design-to-spec` → `/project-pipeline`).
> **Relationship:** a separate project, coordinated with `spaarke-legal-ai-accelerator-r1` (Microsoft: Word + Cowork).
> r1 owns the shared source (`src/accelerators/`) until it merges. This project must not fork it (see §5).

---

## 1. Why this project exists

r1 builds the Legal AI Accelerator for Microsoft 365: Copilot **Cowork** (skills + Python engines) and **Word AI for
Legal** (a playbook `.docx`). The owner wants a **parallel accelerator for Claude** (Anthropic's Claude apps and Claude
Code plugins), tracked separately but built from the same content: the same playbooks, topic catalogue and skills.

Reading the owner's research on the Microsoft 365 app package (§2) showed that most of the work is packaging and
proof, not new content. r1's build already writes a Claude-format plugin from the same source as the Microsoft one.

## 2. Source research (owner, received 2026-11-07, verbatim in substance)

**What an M365 app package is.** It is the zip format Microsoft uses for Teams apps, Copilot agents and Office
add-ins. Cowork plugins reuse it, so admins manage them with the usual Microsoft 365 tools.

```text
spaarke-legal.zip
├── manifest.json      # the package's descriptor file
├── color.png          # 192×192 icon
├── outline.png        # 32×32 icon
└── skills/
    ├── review-contract/
    │   ├── SKILL.md
    │   └── references/   # playbooks, templates, scripts
    └── issues-list/
        └── SKILL.md
```

- `manifest.json` holds the app ID, version, developer details (including privacy and terms URLs), name, description
  and icons.
- `agentSkills[]` lists skill folders (at most 20 per package).
- `agentConnectors[]` lists remote MCP servers, each with an HTTPS URL and a sign-in setting, normally OAuth (at most
  10 per package).
- The format is strict: an unrecognised field makes the upload fail.

**Distribution:**
- share with specific people using the preview plugin CLI (wiqd);
- publish to the whole organization: an admin uses M365 admin center › Manage apps › Upload custom app, and the
  plugin then appears under Discover in Cowork;
- publish publicly through Partner Center to the Microsoft 365 App Store.

**Claude conversion.**
- Uploading a Claude plugin zip in Cowork's Customize › Plugins page converts it automatically, skills and
  connectors included.
- For a repeatable build, the CLI route is
  `atk import openplugin --path ./spaarke-claude-plugin --output ./spaarke-m365 --privacy-url … --terms-url …`,
  then `atk package --manifest-file ./appPackage/manifest.json …`.

| Claude plugin part | Microsoft result |
|---|---|
| `.claude-plugin/plugin.json` | Becomes `manifest.json` (name, description and developer details mapped; app ID generated) |
| `skills/*/SKILL.md` | Copied unchanged; Microsoft says the format is identical |
| `.mcp.json` servers | Become `agentConnectors[]` entries, with the sign-in type detected automatically |
| `commands/`, `agents/`, `hooks/` | Not supported yet |
| `CLAUDE.md` (practice profile) | Microsoft doesn't say whether it is kept |
| API-key sign-in | Not available in Cowork yet; use OAuth |

After converting, the placeholder OAuth ID must be replaced with a real Entra app registration before publishing.

**What the research concludes for Spaarke:**
- Write once, package twice: skills plus a remote OAuth MCP server convert cleanly.
- Keep Claude-only features (scheduled agents, hooks, slash commands) in an optional layer, and give scheduled jobs
  another home on the Microsoft side.
- Keep the practice profile out of `CLAUDE.md`; serve it from Dataverse through the MCP server.
- Respect the limits: 20 skills, 10 connectors, companion files ≤ 5 MB each and ≤ 10 MB per skill.
- wiqd is in preview, and Microsoft's own import can't yet package or share a converted Claude plugin. Use
  `atk import openplugin` for anything to be published.

## 3. Assessment against r1 (r1 session, 2026-11-07)

### 3.1 What r1 already does (it confirms the approach)

| Research claim | r1 today (branch `work/spaarke-legal-ai-accelerator-r1`) |
|---|---|
| M365 package: `manifest.json`, icons, `skills/`, `agentSkills[]` | The primary output `spaarke-nda-mvp.zip` (manifest 1.30, stable GUID). Uploaded and run in Cowork (spike S1, T-01–T-03) |
| Strict manifest | Build gates validate the manifest on every build (wiqd 0.24.0 baseline) |
| ≤ 20 skills; files ≤ 5 MB; ≤ 10 MB per skill | Enforced in `src/accelerators/build/gates.py` (`MAX_PLUGIN_SKILLS`, `MAX_FILE`, `MAX_TOTAL`). r1 also found a limit the research omits: **20 companion files per skill** (`MAX_COMPANIONS`, gap-log #1; `nda-review` is at 20) |
| Write once, package twice | `src/accelerators/build/build_accelerator.py` writes the M365 zip **and** a Claude-format plugin (`t0/claude-plugin/<pkg>/.claude-plugin/plugin.json` and `t0/<pkg>-claude-format.zip`) from one source. The Claude output is a conversion test, not yet a first-class deliverable |
| Commands, agents, hooks don't carry over | r1 uses skills only, by design |
| `agentConnectors[]` for an OAuth MCP server | The v2 connector, owned by `spaarkeai-mcp-bff-api-r1` (`/mcp/legal-operations`, Entra client "Spaarke Copilot Connector – {stamp}") |

### 3.2 Where the research conflicts with r1 evidence

1. **"Converts cleanly."** In r1's tenant test (S1), Cowork converted the Claude-format zip, but the plugin **lost
   its name and version**. r1 therefore writes the M365 manifest itself. `atk import openplugin` is **untested**; at
   most it is a cross-check, not the shipping path.
2. **Admin publishing route.** The research says Manage apps › Upload custom app, then Discover. r1's design and its
   2026-11-02 research say admin center › Agents. r1's T-14 (admin deploy) settles it.

### 3.3 What the research leaves out

- **Word.** Packaging doesn't make anything run in Word AI for Legal. r1 tested this: the plugin is *listed* in Word
  but never *runs* there (L21, L25). Word stays playbook-file based (four-column Word copy, design §5.3). "Package
  twice" covers Cowork and Claude only.
- **The profile.** r1 keeps the profile as a user-owned file outside the package
  (`Documents/Cowork/Profile/legal-profile.md`), read fresh on each run (T-06). That is already platform-neutral, with
  no MCP needed. Serving it from Dataverse through MCP is a v2 option, not a prerequisite. (A Claude plugin has no
  `CLAUDE.md` component anyway; plugins hold skills, commands, agents, hooks and `.mcp.json`.)
- **Scripts.** r1's skills ship Python engines (stdlib + lxml + python-docx; no network, no LLM calls). They run in
  Cowork (Python 3.12.15, verified). The Claude edition must prove the same engines run in Claude's code-execution
  environment, and must check whether that environment can reach the user's files.

### 3.4 Recommendations (r1 session)

1. **Reuse `src/accelerators/` and its build; don't fork.** Skills, Markdown playbooks, `topics.yaml` and engines are
   platform-neutral; only packaging differs. A fork means two playbook copies, which is the drift problem r1 just
   solved (Act 5, design §5.3).
2. **Make the Claude output first class:** the same gates, a Claude-side trial run, and identical skill names
   (`spaarke-{area}-{capability}`, e.g. `spaarke-nda-review`).
3. **Package:** r1's individual-user zip (backlog X12) can carry both plugins under `Accelerators/`. The
   `Legal Knowledge/` tree, playbooks and quick cards are shared.
4. **To verify** (a researcher task):
   - `atk import openplugin`: does it keep name and version?
   - "Upload custom app → Discover" (with r1 T-14);
   - can a converted plugin be shared with specific people?
   - the Claude-side runtime for scripts and file access.

## 4. Relevant r1 context (read on branch `work/spaarke-legal-ai-accelerator-r1`, PR #2528, until merged)

| What | Where (r1 branch) |
|---|---|
| Design v0.6: terminology (§2.3, ontology D-209), surfaces (§5.4), component protocol (§5.10), onboarding and the individual-user zip (§8.1a), POC results and positioning (§8.2), spikes (§12) | `projects/spaarke-legal-ai-accelerator-r1/design.md` |
| Package backlog by act; X12 (package zip), X13 (renderer as a setup script) | `projects/spaarke-legal-ai-accelerator-r1/notes/poc-e2e/package-backlog.md` |
| POC results and gaps #1–#29 | `notes/poc-e2e/results.md`, `notes/poc-e2e/gap-log.md` |
| Build, gates, engines, skills, playbooks | `src/accelerators/` (`build/`, `core/`, `practices/nda/`) |
| Four-column Word copy renderer and checker | `src/accelerators/build/render_word_playbook.py` (`render_table`, `check_table`) |
| Component protocol v0.2 | `projects/spaarke-legal-ai-accelerator-r1/notes/component-protocol.md` |
| Microsoft Legal for Cowork 1.0.10 skill texts (internal reference only; never shipped) | `projects/spaarke-legal-ai-accelerator-r1/notes/legal-for-cowork-skills/` |
| Naming convention (plugin, skills, MCP tools) agreed with mcp-bff-api-r1 | `projects/spaarkeai-mcp-bff-api-r1/notes/coordination/2026-11-06-copilot-naming-and-ownership.md` (its branch) |
| Researcher notes: Word vs Cowork plugin kinds, Harvey and Legora, Word saved playbooks | `.claude/agent-memory/researcher/*-2026-11-0*.md` |
| Prior art for onboarding: `anthropics/claude-for-legal` (`cold-start-interview`, `legal-builder-hub`, Apache-2.0) | r1 design §8.1a |

**Tested facts to carry over:**
- Cowork runs uploaded plugin skills and their Python scripts (S1, S2, T-05).
- A Claude-format zip converts but loses name and version (S1).
- Word never runs plugins (L21, L25).
- Skill selection works by description match or `/skill` (S9).
- `nda-library-review` scored 375/384 on 16 NDAs for 185 credits (POC Act 6c).

## 5. Coordination with r1

- **Shared source.** `src/accelerators/` is owned by r1 until PR #2528 merges. Until then, r2 proposes changes to
  shared parts (skills, engines, playbooks, gates) to the r1 session rather than editing them. Claude-only packaging
  (a new build target, Claude manifests, Claude trial runs) can live in r2.
- **Naming.** Skill and plugin names stay identical on both platforms, following the convention agreed with
  mcp-bff-api-r1 (pending owner OD-20 / OD-42 in r1).
- **Terminology.** "Playbook" is one concept in several formats (ontology §3, D-209). Name the format where it matters.
- **Standing directives that carry over from r1:**
  - never commit secrets;
  - skills and scripts embed no secrets and make no LLM or network calls;
  - keep real client NDAs out of the repo;
  - Claude never types passwords or sends email;
  - every Agent launch states a model.

## 6. Open questions for the owner

1. **Which Claude surfaces?** Claude desktop or claude.ai skills, Claude Code plugins, or both. And does "Claude
   Cowork" mean an Anthropic product, or Claude models inside Microsoft Cowork (already covered by r1 S10)?
2. **Timing:** start now on r1's branch content, or after PR #2528 merges?
3. **Where the Claude edition's playbook folder lives** (OneDrive, local folder or a project), and how skills reach it
   on the Claude side.
4. **Scheduled work** (renewal watching, obligation tracking): in scope for r2, and on which platform?
