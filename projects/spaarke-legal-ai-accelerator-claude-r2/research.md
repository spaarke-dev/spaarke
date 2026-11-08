# Research: a Claude edition of the Legal AI Accelerator (r2)

> **Created** 2026-11-07, from the r1 session's review of the owner's Microsoft 365 app-package research.
> **Status:** research complete (2026-11-07; findings in §8, reports in `notes/research/`), pre-design. Next step:
> `design.md` (then `/design-to-spec` → `/project-pipeline`), after the owner answers §8.6.
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

- **Word (Microsoft side).** Microsoft's own Legal plugin works in Word; it is largely built for Word and contract
  review. What doesn't run in Word is **third-party** plugins: r1 found Spaarke's plugin is *listed* in Word but never
  *runs* there (L21, L25), although third-party plugins do run in Cowork. So on Microsoft, Spaarke's Word path stays
  playbook-file based (four-column Word copy, design §5.3). On Claude, whether Claude for Word runs third-party
  plugins and skills is a separate question (see §7).
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
- Word doesn't run third-party plugins such as Spaarke's (L21, L25); Microsoft's own Legal plugin does run there.
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

## 7. Owner direction (2026-11-07)

1. **One package, adjusted only for platform differences.** The Claude and Microsoft editions share content and
   shape.
2. **Build on the vendors' legal capabilities.** Anthropic's Claude for Legal (and Microsoft's Legal plugin) are what
   make frontier-model vendors a compelling alternative to Harvey or Legora. Spaarke adds to them; it doesn't replace
   them.
3. **Use each platform's strengths.** Elements may differ where Claude allows a better technical approach than
   Microsoft, or the reverse.
4. **Word correction:** see §3.3. Third-party plugins don't run in Microsoft Word; Microsoft's own Legal plugin does.
5. **Research next:** Claude for Legal — what it provides, individual vs team setup, per-user and per-team practice
   configuration, extension (MCP, other plugins), and visuals (MCP Apps, React artifacts, widgets). Reports go to
   `notes/research/`.

**v1 scope: a basic "get started" kit, "email a zip file with everything that's needed".** It is not an enterprise
solution. The design doc v0.3 (`Legal AI Solution Accelerator for Claude.md`) is useful background for a later,
more sophisticated release (governance map, bypass controls, admin baseline, compliance collector, inference hooks,
automation runner), but those are out of v1 scope.

## 8. Research findings (2026-11-07)

Labels follow the reports: [C] cited, [I] inferred, [T]/[U] needs a live test.

| Report (`notes/research/`) | Covers |
|---|---|
| `claude-install-and-runtime.md` | Install paths (individual, Team), zip rules, component × surface matrix, script runtime, Word, profile options, data terms, design-doc v0.3 claims checked, the "email a zip" recipe |
| `claude-for-legal-synopsis.md` | Anthropic's Claude for Legal (`anthropics/claude-for-legal` @ `4a6c651`): contents, profile model, extension, licence, fit for v1 |
| `anthropic-legal-catalogue-and-m365-connector.md` | The two Anthropic legal offerings (the generalist `legal` plugin vs the 12 practice plugins), bundling vs referencing, Anthropic's Microsoft 365 connector |
| `claude-for-word-sign-in-modes.md` | Claude for Microsoft 365 add-in: Claude-account vs cloud-provider (Azure AI Foundry) sign-in |
| `claude-visual-outputs.md` | Seven visual channels on Claude, what needs a server, v1 vs v2 per output, cross-platform with Microsoft |

### 8.1 Packaging and install

- **One plugin zip covers chat, Cowork and Claude Code.** Upload at Customize › Plugins › Add › Upload plugin; it is
  saved to the account and syncs to Claude Code. Rules: one `.claude-plugin/plugin.json` (root or one top-level
  folder), nothing beside that folder, **no top-level `bin/`**, ≤ 200 MB and 5,000 files [C]. So the emailed
  attachment should itself be the plugin zip, built by script and gated with `claude plugin validate`.
- **Team rollout:** an Owner uploads the same zip at Organization settings › Plugins & skills and sets *Installed by
  default* or *Required*; updates by *Upload new version*, with rollback [C]. Without an Owner, one user uploads and
  *Shares* it [C]. Zip installs have no auto-update [I].
- **Component reach:** skills and commands load everywhere; agents and hooks only in Cowork and Claude Code; local
  stdio MCP only on the desktop; `userConfig` and plugin `CLAUDE.md` are Claude Code only (the latter not even there)
  [C]. This confirms §3.4's "skills only" shape: it is the shape that works on every surface and maps 1:1 onto the M365
  package.
- **Mail risk:** gateways often quarantine zips containing `.py`; also publish the zip at a download URL [I].

### 8.2 Runtime: where r1's Python engines can run

- Chat and cloud Cowork run scripts in an Anthropic sandbox; local Cowork in an on-device VM; Claude Code on the user's
  machine [C]. The API sandbox is **Python 3.11** with python-docx (hence lxml) preinstalled; claude.ai's image is not
  published [C/I]. r1 verified the engines on 3.12.15 (Microsoft Cowork), so **test on 3.11 and avoid 3.12-only
  syntax**.
- **File access:** chat sees uploads only (30 MB each); cloud Cowork reaches connected folders only while the Desktop
  app is open, as per-file copies; Claude Code reads the disk [C]. The 16-NDA library review therefore works in Cowork
  and Claude Code, and in chat only by upload.
- **Correction to design doc v0.3:** "Cowork (local) = primary surface" is outdated. Pro, Max and Ultra run new Cowork
  tasks in the cloud since 2026-10-06; Team defaults to cloud, Enterprise to local [C]. v0.3 §9.5 "guardrail hooks apply
  in all modes" is not possible: hooks are ignored in chat and Word [C/I]. Other v0.3 §4/§8 claims were confirmed
  (`claude-install-and-runtime.md` §8).

### 8.3 Claude for Word

- One add-in, **Claude for Microsoft 365**; there is no separate "Claude for Legal" Word add-in [C]. It has built-in
  legal behaviour (counterparty redline review, tracked-changes mode, comment replies, defined terms) [C].
- **Claude-account sign-in:** enabled skills, including plugin skills, are available under `/` [C]. Whether skill
  **scripts** run there is undocumented [U]. Design Word-facing skills as **instruction-only**, playbook in Markdown
  references, which matches r1's Word approach. This is a Claude advantage over Microsoft, where Spaarke's plugin does
  not run in Word (L21, L25), but it needs a live test (§8.5).
- **Cloud-provider sign-in (Azure AI Foundry):** keeps prompts inside the customer's Azure contract, no Claude seats;
  keyless with `gateway_auth_source=entra`; skills come from user upload or an admin **bootstrap endpoint** (≤ 20),
  not from plugins [C]. It needs an admin, so it is a v2 path (the BFF could serve the bootstrap endpoint), not "email a
  zip".

### 8.4 Building on Anthropic's legal capabilities (owner direction §7.2)

- Anthropic has **two** legal offerings [C]: the generalist **Legal** plugin (`knowledge-work-plugins/legal`, 9 skills
  incl. `review-contract`, `triage-nda`; positions in `legal.local.md`) and **Claude for Legal** (12 practice plugins,
  ~151 skills, setup interviews; positions in a profile under `~/.claude/plugins/config/…`). Both are **method only by
  design**; positions come from the customer.
- **Reference, don't bundle.** The README tells users to add Anthropic's plugins from the Directory or marketplace;
  Spaarke ships its own `spaarke-legal` plugin (name must not start with `claude-`) [C/I]. Forking would drift and lose
  Anthropic's updates and the verified badge. A Spaarke `marketplace.json` pinning Anthropic plugins by `sha` is a
  later option [T].
- **Spaarke's cheapest high-value contribution is the positions:** a pre-filled `legal.local.md` and a claude-for-legal
  profile seed carrying Spaarke's NDA playbook (the `NDA triage positions` section that `nda-review` requires and its
  template omits), marked for attorney sign-off [I]. Plus Spaarke-only skills (`spaarke-nda-review`,
  `spaarke-nda-library-review` with r1's engines) that hand off to Anthropic's skills by name.
- Claude for Legal's profile path is **broken in Cowork** (issues #55, #94) and absent in chat; r1's user-owned profile
  file avoids it [C].
- Reuse Claude for Legal's guardrail vocabulary (`[review]`, `[model knowledge — verify]`, 🔴/🟠/🟡/🟢) so chained
  outputs read as one product [I].

### 8.5 Practice configuration, connectors and visuals

- **Profile, two layers [I]:** team defaults as a reference file inside the plugin (or a shared claude.ai Project on
  Team); the user's own `legal-profile.md` in a folder they connect to Cowork, or a claude.ai Project for chat-only
  users. `spaarke-start` writes it (Cowork) or produces it as a download (chat). This is r1's T-06 pattern; no "plugin
  settings" store exists outside Claude Code [C].
- **Anthropic's Microsoft 365 connector** (`microsoft365.mcp.claude.com`) gives v1 document access with no Spaarke
  server: OneDrive/SharePoint contracts and Outlook in chat, Cowork and Word; Team/Enterprise need a work account [C]. A
  team playbook in a SharePoint folder becomes readable by everyone. It doesn't reach SPE containers or Dataverse
  matters; that remains Spaarke's v2 connector. Keep write tools on *Needs approval* (standing directive). Whether
  `read_resource` returns `.docx` bytes the engines can use is [T].
- **Visuals:** only generated **files** and **Word in-document output** need no server and work on both platforms.
  v1: a Markdown table in every reply, `.docx` memo and `.xlsx` issues list, a self-contained HTML library dashboard,
  `.docx` with real tracked changes for redlines, and Claude-only artifacts from an `assets/` template as a "nice view".
  v2: one `ui://` MCP App widget served by the BFF for both Claude and M365 Copilot. Build one self-contained HTML view
  per output that reads either embedded JSON or the ext-apps bridge, so v1 and v2 share it [I].
- **Data terms (README notice):** Free/Pro/Max/Ultra are consumer terms (training on unless turned off); Team and
  Enterprise are Commercial Terms; Word add-in data is deleted within 30 days on every plan [C/I].

### 8.6 Status of the §6 questions

| # | Question | Status |
|---|---|---|
| 1 | Which Claude surfaces? | **Answered by research + §7:** "Cowork" is Anthropic's own Claude Cowork. v1 targets chat, Cowork and Claude for Word (Claude-account sign-in) from one zip; Claude Code comes free via sync. Foundry-mode Word is v2 |
| 2 | Start now or after r1 PR #2528 merges? | **Open — owner** |
| 3 | Where the playbook/profile folder lives | **Proposed:** two-layer profile (§8.5); team playbook optionally in SharePoint via Anthropic's M365 connector. Owner to confirm |
| 4 | Scheduled work | **Out of v1** (§7 v1 scope; agents have no schedule key and are ignored in chat) |

New questions for the owner before `design.md`:
1. **Positions as the lead deliverable?** Ship Spaarke's NDA playbook as a `legal.local.md` + Claude for Legal profile
   seed (works with zero Spaarke code), with `spaarke-legal` skills as the second layer — or lead with the skills?
2. **Which Anthropic plugin to build on:** the generalist **Legal** plugin, **Claude for Legal** `commercial-legal`, or
   both (two seeds to maintain)?
3. **Minimum plan:** Pro and up (consumer terms, needs the training-off notice) or Team and up only?
4. **Azure AI Foundry Word mode:** v1 or v2? Recommended v2 (it needs an admin), or a one-off test against a Spaarke
   dev Foundry deployment (`gateway_auth_source=entra`).
5. **Which Claude plans are available for live tests L1–L7** (§8.7)? The owner's own account has Anthropic's Legal
   plugin and the Microsoft 365 connector (work account) connected; Team-only tests (L5's org upload, local Cowork)
   need a Team org.

### 8.7 Live tests before the design is frozen

Consolidated from the reports (each report has the detail):

| # | Test | Decides |
|---|---|---|
| L1 | Upload a test `spaarke-legal` zip in chat and Cowork; run a skill whose Python engine reads a connected-folder `.docx` and writes one back; record Python version and `import docx, lxml` | Whether r1's engines ship in the Claude zip, and on which surfaces |
| L2 | Claude for Word (Claude sign-in): is a plugin skill listed under `/`, does it run, do `scripts/` run, does tracked-changes output give one revision + one comment per clause | The Claude Word path (§8.3) |
| L3 | Does Anthropic's Legal plugin find `legal.local.md` in a Cowork connected folder, and does `commercial-legal:review` use a seeded profile held outside `~/.claude`? | Whether the positions overlay works without Anthropic's broken path |
| L4 | Is `claude-for-legal` in Cowork Discover by default, or must it be added as a marketplace? | README install steps |
| L5 | Re-upload of a self-uploaded plugin: update in place or duplicate; state kept? | Update procedure for zip users |
| L6 | M365 connector: does `read_resource` return `.docx` bytes; does SharePoint search cover a team playbook folder? | Team-playbook-in-SharePoint option |
| L7 | HTML dashboard file with inline script: does it render in Claude Desktop's and Microsoft Cowork's previews? | Library-dashboard format |

Watch: the unified Chat + Cowork Desktop rollout (managed devices from 2026-11-10) may shift surfaces again; re-check
before the design is frozen.
