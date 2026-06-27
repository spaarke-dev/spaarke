# Spaarke DevOps Project Tracking (r1) — Design

> **Status**: DRAFT — initial design, awaiting user feedback on open questions (§9) before `/design-to-spec`.
> **Created**: 2026-06-23
> **Owner**: Spaarke Engineering
> **Companion docs (to be created)**: SPEC.md, decisions.md, plan.md, tasks/

---

## 1. Problem & Goals

### 1.1 What we're solving

We currently run **~30+ active and ~100+ historical projects** as git worktrees against [c:/code_files/spaarke](file:///c:/code_files/spaarke). Each project follows a structured spec-driven process (`design.md` → `SPEC.md` → `plan.md` → `tasks/*.poml`), and worktrees live at `c:/code_files/spaarke-wt-{name}-r{n}`. There is **no portfolio-level view** showing:

- Which Epics exist and what they contain
- Which projects are active, planned, on hold, completed, cancelled
- How projects roll up into thematic groups (e.g. "AI Platform", "Auth", "Document Intelligence")
- Aggregate task counts, durations, and timeline
- Where each worktree maps to which Epic

We want a **single, GitHub-native, low-overhead** tool that gives us this view without adding a parallel system to maintain.

### 1.2 Goals (in scope for r1)

| # | Goal |
|---|---|
| G1 | **Portfolio-level visibility** — see all Epics with their projects, status, and rollup metrics in one view |
| G2 | **Project-level visibility** — for each worktree, see name, type, summary, dates, duration, task count, status |
| G3 | **Single source of truth** — GitHub Issues + Projects v2; no parallel spreadsheet or Notion/Linear |
| G4 | **Low-friction maintenance** — Claude Code skills populate and sync fields; humans don't hand-edit GitHub UI fields |
| G5 | **Authoritative POML compatibility** — POML task files remain the source of truth for task content. GitHub tracks aggregate counts + status, not duplicated task bodies |
| G6 | **Reuse existing infrastructure** — extend GitHub Project #2 (Spaarke Core) rather than build new |

### 1.3 Non-goals (deferred or out of scope)

| # | Non-goal | Why |
|---|---|---|
| N1 | Replacing POML task files with GitHub Issues | POML is mature; duplication doubles maintenance |
| N2 | Real-time sync (file-watcher style) | On-demand `/devops-project-sync` is sufficient |
| N3 | Time tracking / billable hours | Not needed for engineering portfolio view |
| N4 | Story points / velocity charts | Out of scope for v1 — revisit after baseline |
| N5 | Backfilling all 133 historical projects in one pass | Open question — see Q1 |
| N6 | A custom web dashboard outside GitHub | GitHub Projects v2 views are sufficient and free |

---

## 2. Current state

### 2.1 What exists

| Artifact | Where | Notes |
|---|---|---|
| Project folders | [projects/](../../projects/) — 133 of them, including `_backlog/` | Standard files: README, design, SPEC, decisions, plan, tasks/, CLAUDE.md, current-task.md |
| Git worktrees | `c:/code_files/spaarke-wt-{name}-r{n}` | One worktree per active project; `worktree-setup`/`worktree-sync` skills manage |
| Task files | `projects/{name}/tasks/*.poml` | Authoritative task definitions; `task-execute` is the binding execution protocol |
| Task index | `projects/{name}/tasks/TASK-INDEX.md` | Status checkboxes (🔲 / ✅) per task |
| Current state | `projects/{name}/current-task.md` | Active task tracker, updated by `task-execute` |
| GitHub repo | `spaarke-dev/spaarke` | Single repo for all project work |
| GitHub Projects v2 | [@spaarke-dev project #2 "Spaarke Core"](https://github.com/users/spaarke-dev/projects/2) | 22 items today; **20 fields already configured** (see §2.2) |
| Project pipeline skills | `design-to-spec`, `project-pipeline`, `project-setup`, `task-create`, `task-execute`, `project-continue`, `worktree-setup`, `worktree-sync` | The spec-driven lifecycle is already toolchain-supported |

### 2.2 GitHub Project #2 — existing fields

The Spaarke Core project board already has these fields, several of which map directly to what we want:

| Field | Type | Options / Notes | Direct fit for our goals? |
|---|---|---|---|
| `Title` | Text | — | ✅ |
| `Status` | Single-select | Todo, In Progress, Done | ⚠️ Needs extension (Planned, On Hold, Cancelled) |
| `Type` | Single-select | Idea, Epic, **Story**, Task, Bug, Spike | ✅ Epic exists; "Story" can map to "Project" or we add new option |
| `Priority` | Single-select | P0–P3 | ✅ |
| `Sprint` | Iteration | — | Optional |
| `Start Date` | Text | — | ✅ |
| `Target Date` | Text | — | ✅ Needs "Actual End Date" companion |
| `Area` | Single-select | Matter, UI/UX, Tooling, Documentation, Components, Document, Invoice, AI, Project, Event, Workflow | ❌ Domain-tags, not the engineering-type-tags user wants |
| `Release` | Text | — | Optional |
| `Parent issue` | Built-in | — | ✅ Sub-issue hierarchy already available |
| `Sub-issues progress` | Built-in | — | ✅ Auto-rollup for free |
| `Created` / `Updated` / `Closed` | Built-in | — | ✅ |

**Conclusion**: 60% of what we need is already there. We extend, not rebuild.

---

## 3. Recommended approach (high level)

### 3.1 Three-layer hierarchy in GitHub Issues

```
Epic (Issue, Type=Epic)
  ├── Project / Worktree (Issue, Type=Project — NEW Type option)
  │     └── (tasks remain in projects/{name}/tasks/*.poml; counts surfaced on the issue)
  ├── Project / Worktree
  └── Project / Worktree
```

- **Epic** = thematic grouping that spans multiple worktrees (e.g. "AI Platform Unification", "Smart Todo", "BFF Hygiene")
- **Project / Worktree** = one worktree, one spec-driven engagement (e.g. `spaarke-wt-spaarke-ai-platform-unification-r6`)
- **Tasks** = remain POML files. The Project Issue carries `taskCount` and `tasksCompleted` as custom fields; we **do not** mirror each POML into a sub-issue (see §6.2 alternative).

### 3.2 Why GitHub Projects v2 (and not Linear / Jira / Notion / a custom dashboard)

| Criterion | GitHub Projects v2 | Linear / Jira | Notion / Sheet | Custom dashboard |
|---|---|---|---|---|
| Already adopted | ✅ #2 exists with 20 fields | ❌ New tool | ❌ Parallel system | ❌ Build & maintain |
| Free for our usage | ✅ | ⚠️ Paid tiers | ✅ | ❌ Hosting + dev time |
| Lives with code + PRs + CI | ✅ Native | ⚠️ Bridges | ❌ | ⚠️ |
| Roadmap / board / table views | ✅ | ✅ | ⚠️ Manual | ❌ |
| Sub-issue rollup | ✅ Built-in | ✅ | ❌ | ❌ |
| `gh` CLI scriptable | ✅ | ⚠️ via APIs | ❌ | n/a |
| Custom fields | ✅ 20+ types | ✅ | ✅ | ✅ |
| API for skills to populate | ✅ GraphQL | ✅ | ⚠️ | n/a |

**Decision (proposed)**: Single GitHub Project (extend existing #2) is the lowest-friction, highest-leverage path. Decision recorded as **D-01** below in §5.

### 3.3 Why not "sprint = worktree"

The user proposed mapping sprints to projects. **Recommend against**: GitHub iterations are time-boxed (fixed cadence, e.g. 2-week sprints) but Spaarke projects vary wildly — some 3 days (`trivy-cve-cleanup-r1`), some 8+ weeks (`ai-spaarke-insights-engine-r1`). Modeling project=sprint creates impedance and forces awkward partitioning of long projects across multiple sprints.

**Recommended model instead**: Epic = thematic grouping; Project = worktree (variable-length); Sprint iteration field remains available for *optional* time-windowing if/when we want team-cadence reporting later.

---

## 4. Detailed design

### 4.1 New custom fields to add to project #2

The existing 20 fields cover most needs. We add **5 new fields** scoped to Project-Issues:

| Field | Type | Options / Format | Purpose |
|---|---|---|---|
| `Project Type` | Single-select | `Module`, `UI`, `Infrastructure`, `Cleanup`, `Data`, `Process`, `AI`, `Mixed` | The "type tag" the user requested. Distinct from existing `Area` (which is domain-oriented). |
| `Worktree Path` | Text | `spaarke-wt-{name}-r{n}` | Maps issue ↔ filesystem worktree |
| `Project Folder` | Text | `projects/{name}/` | Maps issue ↔ project docs folder |
| `Task Count` | Number | integer | Total POML tasks in `tasks/` |
| `Tasks Completed` | Number | integer | Count of ✅ in `TASK-INDEX.md` |
| `Project Status` | Single-select | `Planned`, `In Progress`, `On Hold`, `Completed`, `Cancelled`, `Abandoned` | Distinct from item-level `Status`; richer state the user asked for. We **keep** the existing `Status` (Todo/In Progress/Done) for grain-level GitHub Issue state; `Project Status` is the portfolio-level rollup. |

**Computed in views (not stored)**: Duration = `Target Date − Start Date` (or actual end if `Project Status=Completed`). GitHub Projects supports computed columns in roadmap and table views.

### 4.2 New `Type` option for Issues

Add `Project` to the `Type` field (currently: Idea / Epic / Story / Task / Bug / Spike).

| Type | Use |
|---|---|
| `Epic` | Top-level thematic grouping |
| `Project` (NEW) | One worktree |
| `Story`, `Task`, `Bug`, `Spike`, `Idea` | Existing — used for issue-level work tracking (PR-linked) |

### 4.3 Labels (for filtering and at-a-glance grouping)

Labels are repo-scoped (cheap, easy to add). Use these on Epic + Project issues:

| Label | Purpose |
|---|---|
| `epic` | Marks an Epic issue (redundant with `Type=Epic` but enables GitHub-native label filtering) |
| `project` | Marks a Project issue |
| `worktree:active` / `worktree:archived` | Filesystem state flag — useful for cleanup |
| `area:ai`, `area:bff`, `area:pcf`, `area:auth`, etc. | Cross-cutting; complements `Area` field |

### 4.4 Views on project #2

| View name | Type | Filter | Purpose |
|---|---|---|---|
| **Portfolio Roadmap** | Roadmap (by `Start Date` / `Target Date`) | `Type IN (Epic, Project)` | Top-level Gantt-style timeline |
| **Epics overview** | Table | `Type = Epic` | One row per Epic, with `Sub-issues progress` rollup |
| **Active projects** | Table | `Type = Project AND Project Status = In Progress` | The "what's running now" view |
| **Project backlog** | Table | `Type = Project AND Project Status = Planned` | Pipeline of upcoming worktrees |
| **On hold / Cancelled** | Table | `Type = Project AND Project Status IN (On Hold, Cancelled, Abandoned)` | History + parked work |
| **By type tag** | Board (group by `Project Type`) | `Type = Project` | "Show me all UI projects" / "all Infrastructure projects" |
| **Issue-level work** | Existing default views | `Type IN (Story, Task, Bug, Spike)` | Unchanged — day-to-day work |

### 4.5 Issue templates

Two new templates in [.github/ISSUE_TEMPLATE/](../../.github/ISSUE_TEMPLATE/):

- **`epic.yml`** — fields: title, objectives/focus, scope, success criteria, projected timeframe
- **`project.yml`** — fields: title, project folder slug, worktree slug (defaults from title), proposed `Project Type`, parent Epic (reference), summary, projected Start/Target Date

Templates emit issue bodies in a stable, parseable format so skills can read/update them.

### 4.5a Epic ↔ Project association (GitHub mechanics)

**GitHub-native term: parent-issue / sub-issue.** Since 2024 GA, any GitHub Issue can have any number of sub-issues; the parent shows a `Sub-issues progress` bar. This is a true relational link stored on the issues — not a parsed checkbox list in the body.

| Our terminology | GitHub mechanics |
|---|---|
| Epic | Issue with `Type=Epic`, label `epic`. Has 1+ sub-issues. |
| Project / Worktree | Issue with `Type=Project`, label `project`. **`Parent issue` field = its Epic.** Visible in the Epic's "Sub-issues" pane. |
| Tasks (POML) | NOT sub-issues. Tracked as `Task Count` / `Tasks Completed` fields on the Project Issue (per D-08). |

**Mechanics:**
- Project #2 already exposes the `Parent issue` field — visible in §2.2
- Setting parent: Project Issue → right sidebar → `Parent issue` → select Epic. Or from Epic: "Add sub-issue" → pick existing or create new.
- One sub-issue can only have one parent → clean tree (no DAG complications)
- Re-parenting supported: skills accept `--parent-epic <#N>` to move a Project between Epics

**`Sub-issues progress` ≠ `Project Status` rollup.** The auto-computed bar uses GitHub's item-level `Status` (Done count), not our `Project Status`. We define the mapping in SPEC:
- `Project Status = Completed` ⇒ Issue closed AND item-`Status = Done` (so it counts as a win)
- `Project Status = Cancelled` ⇒ Issue closed AND label `cancelled` (does NOT count as a win)
- `Project Status = On Hold` ⇒ Issue stays open; `Status` stays at `In Progress`; explicit label `on-hold`

### 4.5b Idea lifecycle: capture → promotion → packaging (N-to-1)

This answers: how do raw ideas in `projects/_backlog/` become real Projects, and what about packaging multiple ideas into one?

**Stage 1 — Idea capture (GitHub-only; no local folder)**

Ideas are captured as **GitHub Issues with `Type=Idea`, label `backlog`**. No project folder, no worktree.

- Skill: **`/devops-idea-create`** — opens an Idea Issue from a one-line prompt; fields: title, summary, why-it-matters, tentative Epic, originating source/discussion
- This replaces / supplements the current `projects/_backlog/needs-a-project.md` file (we can leave that file as the "draft pad" before promotion to an Idea Issue, or eliminate it)
- Backlog view: project #2 filtered by `Type=Idea`

**Stage 2 — Idea promotion (Idea → Project)**

Two paths:

**Path A: 1 Idea → 1 Project**
1. Open Idea Issue → change `Type` to `Project`, set `Parent issue` to its Epic, populate `Project Type` field
2. Locally: run `/devops-project-start --from-issue #N`
3. Skill scaffolds `projects/{slug}-r1/`, creates worktree, drafts `design.md` from the Issue body, opens VS Code

**Path B: N Ideas → 1 Project (packaging)**
1. Create a new Project Issue (`Type=Project`) — title + summary + parent Epic + `Project Type`
2. Attach the Idea Issues to it — preferred mechanism: **add Ideas as sub-issues of the Project** (so the Project's `Sub-issues progress` reflects which source-ideas have been addressed). Alternative: link via "Closes #X" in PRs.
3. Close each Idea with comment "Absorbed into Project #M" (or leave open as sub-issues if you want progress tracking)
4. Locally: `/devops-project-start --from-issue #M --absorbs #X #Y #Z`
5. Skill scaffolds the worktree; the auto-generated `design.md` includes a "Source ideas" section listing absorbed Idea Issues with original framings preserved

**Skill: `/devops-idea-promote`** handles both paths. Flag `--package #X #Y #Z` triggers Path B.

**Why is the local-scaffolding step always required?** Because creating a local git worktree on your machine isn't something GitHub Actions can do from the cloud — see §6.6 for the automation boundary.

### 4.5c Practical walkthrough — zero to a running project

This is the concrete sequence with **(M) = manual in GitHub UI**, **(S) = skill in Claude Code (explicit run)**, **(A) = automatic via skill-hook into an existing skill (no explicit run)**.

**Why you can't see `Type=Project` or set `Parent issue` today** — because Phase 1 of *this very project* hasn't run yet. The `Type` field on project #2 currently has only `Idea / Epic / Story / Task / Bug / Spike`. We add `Project` as a new option in Phase 1 step 1 below. The `Parent issue` field is a built-in GitHub field that's always there, but it only becomes useful once we have Epic + Project Issues to link.

**Phase 1 — one-time portfolio setup**

| # | Step | M/S/A | What happens |
|---|---|---|---|
| 1 | Add `Project` option to the `Type` single-select on project #2 | **(S)** `/devops-portfolio-setup` | Runs `gh api graphql` mutation to extend the existing field. Could also be done manually in GitHub Settings UI |
| 2 | Add 5 new custom fields (`Project Type`, `Worktree Path`, `Project Folder`, `Task Count`, `Tasks Completed`, `Project Status`) | **(S)** same skill | Same skill batch-creates fields with the option lists from D-15 / D-16 |
| 3 | Create `.github/ISSUE_TEMPLATE/{epic,project,idea}.yml` and PR | **(S)** same skill | Templates standardize Issue body so other skills can parse them |
| 4 | Add labels (`epic`, `project`, `backlog`, `worktree:active`, `worktree:archived`, `on-hold`, `cancelled`) | **(S)** same skill | `gh label create …` |
| 5 | Create initial Epics from §4.6 strawman | **(S)** `/devops-epic-create` (one run per Epic) | Creates Epic Issues, sets Type=Epic, applies label, adds to project #2 |

**Recurring workflow — every new project**

| # | Step | M/S/A | What happens |
|---|---|---|---|
| 6 | Capture a rough Idea | **(M)** in GitHub UI **OR** **(S)** `/devops-idea-create` | Creates Issue with `Type=Idea`, label `backlog`. No local folder. |
| 7 | Triage Ideas — pick one (or N) to promote | **(M)** in GitHub UI | Judgment call — which Ideas are real work? Any that should be packaged together? |
| 8 | **Set `Parent issue`** on the Project Issue (this is where I think you got stuck) | **(M)** in GitHub UI **OR (S)** done as part of promote skill | Two ways in GitHub UI: **(a)** open the Epic → "Sub-issues" section → "Add sub-issue" → select the Project Issue, OR **(b)** open the Project Issue → right sidebar → "Parent issue" → select the Epic. Skill `/devops-idea-promote` sets this automatically when promoting. |
| 9a | Promote 1 Idea → 1 Project | **(S)** `/devops-idea-promote --to-project --epic #E` | Flips Idea's `Type` from `Idea` to `Project`, sets Parent issue, populates Project Type / Project Status=Planned, applies `project` label |
| 9b | Or: package N Ideas → 1 Project | **(S)** `/devops-idea-promote --package #X #Y #Z --epic #E` | Creates a NEW Project Issue; attaches Idea Issues as **sub-issues of the Project Issue** (per D-20, kept open so per-Idea progress is visible); sets parent Epic |
| 10 | Scaffold local folder + worktree + design.md from the Project Issue | **(S)** `/devops-project-start --from-issue #N` | Reads Issue → creates `projects/{slug}-r1/` + worktree at `c:/code_files/spaarke-wt-{slug}-r1` + drafts design.md skeleton + writes back `Worktree Path` / `Project Folder` fields + adds "GitHub Issue: #N" pointer to local README |
| 11 | Author SPEC from design | local: `/design-to-spec` | **(A)** end of skill calls `/devops-project-sync` → updates Issue body summary, ensures `Project Status=In Progress` |
| 12 | Create tasks | local: `/task-create` | **(A)** sets `Task Count` field on Project Issue |
| 13 | Execute tasks | local: `/task-execute` (per task) | **(A)** Step 9 of `task-execute` increments `Tasks Completed`; if last task, prompts for `Project Status=Completed` candidate |
| 14 | Checkpoint during long work | local: `/context-handoff` (>60% context) | **(A)** calls `/devops-project-sync` — your compaction checkpoint *is* your portfolio checkpoint |
| 15 | Sync worktree with master | local: `/worktree-sync` | **(A)** calls `/devops-project-sync` at end |
| 16 | PR / merge | local: `/merge-to-master`; GitHub PR | **(A)** post-merge hook updates Project Issue with merged PR #; if completion criteria met, prompts for archive |
| 17 | Project done (or cancelled) | local: `/devops-project-archive` (explicit gate) | Sets `Project Status`, deletes worktree per D-18, leaves project folder with `.archived` marker |
| 18 | Stakeholder snapshot | local: `/devops-portfolio-status` on demand | Writes `docs/portfolio/snapshot-{date}.md` |

**The point**: steps 11–16 ride on the existing development skills you already use. **You never type a `/devops-*` command in the middle of work** — the portfolio updates happen as side-effects. The only explicit `/devops-*` triggers are at intentional gates: setup (once), promote (when ideas become real), archive (at close), and snapshot (for stakeholders).

### 4.6 Epic taxonomy (initial proposal)

Derived from a scan of existing project names. Final list to be confirmed during `/design-to-spec`. Strawman:

| Epic | Sample projects (existing folders) |
|---|---|
| AI Platform & Chat | `ai-spaarke-platform-enhancments-r3`, `ai-sprk-chat-*`, `spaarke-ai-platform-unification-r{4,6}` |
| Insights Engine | `ai-spaarke-insights-engine-r{1,2,3}`, `ai-spaarke-insights-engine-widgets-r1` |
| Smart Todo | `smart-todo-r4`, `smart-todo-decoupling-r3` |
| Document Intelligence | `ai-document-intelligence-r5`, `ai-document-relationship-visuals` |
| BFF & Test Hygiene | `bff-ai-architecture-audit-r1`, `sdap-bff.api-test-suite-repair`, `sdap-bff-api-remediation-fix` |
| Auth & SSO | `auth-sso-and-email-wizard-2026-05` |
| Code Quality | `code-quality-and-assurance-r{1,2,3}` |
| Procedures & Knowledge | `ai-procedure-quality-r1`, `ai-procedure-refactoring-r{1,2}`, `agent-framework-knowledge-r1` |
| CI/CD & Tooling | `ci-cd-github-enhancement`, **this project (`spaarke-devops-project-tracking-r1`)** |
| Insights / Widgets / Search | `ai-spaarke-ai-workspace-UI-r1`, `ai-search-indexing-fix`, `ai-semantic-search-optimization-r1` |
| Communications | `email-communication-solution-r3`, `daily-update-service-r2` |
| Multi-tenant / Multi-container | `spaarke-multi-container-multi-index-r1` |

---

## 5. Decisions (provisional — to be ratified in decisions.md)

| ID | Decision | Rationale |
|---|---|---|
| **D-01** | Use GitHub Projects v2 (extend existing project #2) as the single tracking surface. | 60% of fields already exist; tool already adopted; native to git/PR flow; free. |
| **D-02** | Three-layer hierarchy: Epic → Project (worktree) → POML tasks. Tasks are NOT mirrored to GitHub Issues. | POML is mature, ADR-aware, AI-execution-binding. Mirroring doubles maintenance. |
| **D-03** | Project = variable-duration unit of work (worktree-shaped). Do NOT model project as a fixed-cadence sprint. | Spaarke projects span 3 days to 8+ weeks — sprint impedance is real. |
| **D-04** | Keep existing item-level `Status` (Todo/In Progress/Done). Add separate `Project Status` (Planned/In Progress/On Hold/Completed/Cancelled/Abandoned) field. | Item-level status applies to small issues; portfolio status is richer. |
| **D-05** | Project Type (Module/UI/Infrastructure/Cleanup/Data/Process/AI/Mixed) is a NEW field distinct from existing `Area`. | Area is domain-axis; Project Type is engineering-axis. Both valuable, neither subsumes the other. |
| **D-06** | Claude Code skills are the primary write path. Humans rarely edit GitHub UI fields directly. | Reduces drift, enforces format, leverages POML/file state as source of truth. |
| **D-07** | **Backfill scope: active + in-flight projects only (~20–30).** Completed/abandoned projects remain as folders + git history; historical backfill is explicitly out of scope for r1. | User decision 2026-06-23. Phase 5 removed from plan; phasing collapses to Phases 0–4 + 6. |
| **D-08** | **POML tasks are NOT mirrored as GitHub sub-issues.** Project Issues track `Task Count` + `Tasks Completed` only, populated from `TASK-INDEX.md` by skills. | User decision 2026-06-23. Avoids duplication; preserves POML as authoritative task surface. |
| **D-09** | **Extend existing GitHub Project #2 ("Spaarke Core")** with new fields + `Type=Project` option + portfolio-filtered views. No new GitHub Project created. | User decision 2026-06-23. Single source of truth wins. Day-to-day issue work continues on existing views; portfolio work uses new `Type IN (Epic, Project)` filtered views. |
| **D-10** | **Audience = three tiers**: (a) internal engineering management (primary), (b) other Spaarke engineers, (c) stakeholders / leadership. NOT customer-facing in r1. | User decision 2026-06-23. Drives the design polish bar (see §6.3). |
| **D-11** | **Epic ↔ Project association uses GitHub native sub-issues** (Epic = parent issue, Project = sub-issue with `Parent issue` field set). Tasks (POML) are NOT sub-issues; they remain in `tasks/*.poml` per D-08. | Native GitHub mechanic; auto-rolls up via `Sub-issues progress`. See §4.5a for mechanics and Project Status ↔ item Status mapping. |
| **D-12** | **Ideas captured as GitHub Issues with `Type=Idea`** (no local folder). Promotion to Project supports **both 1→1 and N→1 packaging** via `/devops-idea-promote` skill. Promotion always requires running `/devops-project-start --from-issue` locally to create the worktree. | Ideas are first-class portfolio citizens; packaging is explicit (preserves source-idea framing). Local scaffolding intentionally requires a local skill run — GitHub Actions can't create worktrees on user machines. See §4.5b. |
| **D-13** | **GitHub ↔ Claude Code automation boundary**: GitHub is portfolio/capture/PR surface; Claude Code on local machine is scaffolding/execution surface. **`/devops-project-start --from-issue #N` is the one blessed handoff**. GitHub Actions may post pre-filled prompts but cannot trigger local skills. **GitHub Copilot Workspace / Coding Agent is NOT in this workflow** for r1. | Avoids the "GitHub tried to SSH into my laptop" anti-pattern. Keeps Spaarke's POML/`task-execute` discipline intact (Copilot Agent is free-form; we are structured). See §6.5 for full diagram. |
| **D-14** | **Epic taxonomy** — every Project MUST have an Epic parent (even single-project Epics like "CI/CD & Tooling"). Strawman in §4.6 ratified pending §4.6 review pass. | Keeps the rollup clean; no orphans on the portfolio board. |
| **D-15** | **`Project Type` options**: `Module / UI / Infrastructure / Cleanup / Data / Process / AI / Mixed`. | Single-select field. Adding options later is safe; removing is not. |
| **D-16** | **`Project Status` options**: `Planned / In Progress / On Hold / Completed / Cancelled`. **`Abandoned` folded into `Cancelled`** — informal-death projects take the same label. | Simpler enum; semantic loss is negligible. |
| **D-17** | **Project Issue body is auto-generated** from `projects/{name}/README.md` + `design.md` summary by `/devops-project-sync`, with a "<!-- DO NOT EDIT — synced from README.md -->" header. | Engineers / stakeholders see consistent content (per D-10); drift is one-directional (local → GitHub). |
| **D-18** | **Worktree archival policy**: on `/devops-project-archive`, **delete the worktree** (git branch + PR history retains the work). **Keep the `projects/{name}/` folder** with a `.archived` marker file. | Reclaims disk + IDE clutter; preserves design/spec/task documentation for posterity. |
| **D-19** | **`/devops-project-sync` is user-triggered (and hook-driven) for r1**. A scheduled GitHub Action (`workflow_dispatch` + cron) is a Phase 5 follow-up *if* drift is observed in practice. | Avoid pre-mature automation. |
| **D-20** | **N→1 packaging — Idea Issues stay open** as sub-issues of the Project Issue. They close on the specific PR that addresses each Idea (or at Project completion). | `Sub-issues progress` shows real per-Idea delivery; design.md "Source ideas" section provides written traceability. |
| **D-21** | **`/devops-project-start` does NOT auto-open VS Code**. Opt-in flag `--open-editor` (default: print the path + suggested next command). | Avoid surprising side effects. |
| **D-22** | **Action that auto-comments `/devops-project-start --from-issue #N`** when `Type=Project` is set: **shipped in Phase 5 (polish), not r1 acceptance**. | Saves typing; adds a `.github/workflows/` file; nice-to-have. |
| **D-23** | **Documentation lives in extensions of existing docs**: extend [`docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md`](../../docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md) (portfolio integration section) + [`docs/procedures/AI-CODING-PROCEDURES-GUIDE.md`](../../docs/procedures/AI-CODING-PROCEDURES-GUIDE.md) (lifecycle scenarios). Update root [`CLAUDE.md`](../../CLAUDE.md) §16 Pointers with portfolio row. **No new top-level guide file.** Per-project README gets an auto-written portfolio pointer block. See §6.7. | Existing docs already cover the territory; adding a third would fragment discovery. Per-project README pointer makes folder ↔ portfolio link bidirectional. |

---

## 6. Claude Code skills design

### 6.1 New skills (9 total — none currently exist; all introduced by this project)

**Clarification**: None of these are existing Spaarke skills. The whole `/devops-*` family is what this r1 project ships. "Existing skills" elsewhere in this doc refers to **Spaarke's already-existing skills** (`/design-to-spec`, `/task-execute`, `/context-handoff`, `/worktree-setup`, `/worktree-sync`, `/repo-cleanup`, `/merge-to-master`) which get *extended* with portfolio-update hooks — see §6.2.

| # | Skill | Purpose | Frequency | Trigger |
|---|---|---|---|---|
| 1 | `/devops-portfolio-setup` | One-time: add `Project` Type option, 5 new fields, labels, issue templates to project #2 | **Once** (Phase 1) | Explicit |
| 2 | `/devops-epic-create` | Create an Epic Issue with template fields, add to project #2, set `Type=Epic` | **Per Epic** (~10 total) | Explicit |
| 3 | `/devops-idea-create` | Capture an Idea as GitHub Issue (`Type=Idea`, label `backlog`); no local folder | **Per Idea** (frequent) | Explicit — but can also do manually in GitHub UI |
| 4 | `/devops-idea-promote` | Promote Idea(s) → Project Issue. Supports 1→1 and N→1 (`--package #X #Y --epic #E`). Sets Parent issue, populates Project Type. Does NOT create local worktree (that's #5). | **Per promotion** (intentional gate) | Explicit |
| 5 | `/devops-project-start --from-issue #N` | **THE BLESSED HANDOFF.** Reads a Project Issue → scaffolds `projects/{slug}-r1/` + worktree + drafts `design.md` skeleton + writes back `Worktree Path` / `Project Folder` fields + adds `GitHub Issue: #N` pointer to local README | **Per project start** | Explicit (suggested via GitHub Action comment per D-22 polish) |
| 6 | `/devops-project-register --from-folder` | Backfill an *existing* worktree/folder onto the portfolio (creates Issue, sets fields). For Phase 3. Inverse direction of #5. | **One-time during Phase 3 backfill** | Explicit |
| 7 | `/devops-project-sync` | Re-read local state (TASK-INDEX, current-task, worktree presence) → update GitHub Project Issue fields | **Frequent** — but **auto-triggered** via §6.2 hooks; rarely explicit | (A) hook-driven |
| 8 | `/devops-portfolio-status` | Print a concise dashboard. Optionally write `docs/portfolio/snapshot-{date}.md` for stakeholders (per D-10) | **On demand** | Explicit |
| 9 | `/devops-project-archive` | Mark Project Issue Completed / Cancelled, capture final counts, apply worktree retention (D-18) | **At project close** | Explicit |

**Of the 9, only 6 are ever explicitly typed by you**: portfolio-setup (once), epic-create (per Epic), idea-create (or do in UI), idea-promote (per promotion), project-start (per new project), project-archive (per close), portfolio-status (on demand). The other two (`-register`, `-sync`) are either backfill-only or hook-driven.

### 6.2 Skill integration hooks — automation built into existing skills

This is the **load-bearing automation strategy**: portfolio updates ride on the existing development flow as side-effects. You should not have to remember to run a `/devops-*` skill in the middle of normal work. Per-skill hooks below.

| Existing skill | Hook injected | When in the skill's flow | Operator visibility |
|---|---|---|---|
| **`/design-to-spec`** | If a Project Issue exists for this folder → update its body summary from spec.md + ensure `Project Status=In Progress` | After spec.md is written | "✅ Portfolio synced" one-liner |
| **`/project-pipeline`** | If no Project Issue exists for this folder → call `/devops-project-register --from-folder`; else `/devops-project-sync` | At start | "✅ Portfolio sync: #N" |
| **`/task-create`** | Set `Task Count` field on Project Issue from POML count | After tasks scaffolded | "✅ Portfolio: Task Count=N" |
| **`/task-execute`** (Step 9 completion) | Increment `Tasks Completed`; if last task → prompt "Promote Project Status to Completed?" | On task completion | "✅ Portfolio: Tasks Completed=N/M" |
| **`/context-handoff`** | Call `/devops-project-sync` — your compaction checkpoint *is* your portfolio checkpoint | Always at end | "✅ Portfolio sync" |
| **`/worktree-setup`** | After scaffolding worktree: if Issue exists, link via `/devops-project-register`; else prompt "Register project on portfolio now?" | After worktree created | Prompt-once UX |
| **`/worktree-sync`** | Call `/devops-project-sync` | At end | "✅ Portfolio sync" |
| **`/repo-cleanup`** | For archive candidates, call `/devops-project-archive` | When detecting archive candidates | Confirmation prompt |
| **`/merge-to-master`** | Update Project Issue with merged PR #; if completion criteria → prompt for archive | After merge succeeds | "✅ Portfolio: PR #M linked" |

**Why `/context-handoff` is a particularly important hook**: per root CLAUDE.md §5, `/context-handoff` runs every 3 task steps, after 5+ file edits, after deployments, and at >60% context. That's a natural high-frequency checkpoint cadence — so the portfolio is always ≤3 task-steps stale without any extra ceremony.

**Skills NOT hooked (intentionally)**:
- `/push-to-github` — too high frequency; not enough signal to justify the API call
- `/pull-from-github` — read-only; no portfolio state change
- `/repo-cleanup` non-archive paths — read-only inventory

### 6.7 Documentation deliverables — extend existing docs, don't create new

Two existing docs already cover the territory; we extend rather than create a third. **No new top-level doc** in `docs/guides/` or `docs/procedures/`.

**Doc 1: [`docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md`](../../docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md)** (423 lines today — front-door guide for "I want to start a project")

| Existing scope | What we add |
|---|---|
| 2-step flow (`/design-to-spec` → `/project-pipeline`) | New "Step 0: Capture idea on the portfolio" (`/devops-idea-create` or manual GitHub Issue) |
| Components reference | Add `/devops-*` skill family + portfolio link to project #2 |
| Detailed process | New section: **"Portfolio integration"** covering: (a) Epic association mechanics with screenshots of `Parent issue` and `Sub-issues` UI; (b) Idea → Project promotion (1→1 and N→1); (c) `/devops-project-start --from-issue` handoff; (d) auto-hooks that update fields during normal work |
| What gets created | Append: "GitHub Project Issue #N created/updated, fields populated" |
| Command reference | Add the 9 `/devops-*` skills with examples |
| Troubleshooting | Add: missing `Type=Project` (Phase 1 not run); orphan worktree (no Issue link); drift between local/portfolio |

**Doc 2: [`docs/procedures/AI-CODING-PROCEDURES-GUIDE.md`](../../docs/procedures/AI-CODING-PROCEDURES-GUIDE.md)** (525 lines today — scenario-based reference)

This is the home for **"update status" and "close project"** workflows. Add new scenarios:

| Scenario | What it covers |
|---|---|
| **I want to capture an idea before it's a real project** | `/devops-idea-create`; manual GitHub Issue option; what fields matter |
| **I want to promote ideas into a project (with packaging)** | Path A (1→1) and Path B (N→1); explicit walk-through with `--epic` and `--package` flags |
| **I want to update a project's status** | (a) auto-status from `/task-execute` completion; (b) manual `/devops-project-sync`; (c) UI override; (d) on-hold workflow (label `on-hold` + comment with restart criterion); (e) status semantics table |
| **I want to close (complete or cancel) a project** | `/devops-project-archive`; what gets retained (folder + design.md) vs deleted (worktree); how to handle in-flight PRs at close; how `Sub-issues progress` updates the Epic rollup |
| **I want to see what's running across all projects** | `/devops-portfolio-status`; the Portfolio Roadmap view URL; the per-Epic table view; monthly snapshot export |
| **I want to package multiple ideas into one project** | Detailed walk-through of N→1 packaging; how `Sub-issues progress` shows per-Idea delivery (D-20) |
| **I'm a stakeholder — where do I look?** | Direct link to project #2; views to use; how to read `Project Status`; what to ignore (item-level `Status` is for granular issues, not project state) |

**Doc 3: existing root [`CLAUDE.md`](../../CLAUDE.md) §16 "Pointers"** (lightweight cross-reference update)

Add a row to §16 Pointers:

| Topic | Pointer |
|---|---|
| **Portfolio tracking + DevOps procedures** | `docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md` (initiation + portfolio integration) · `docs/procedures/AI-CODING-PROCEDURES-GUIDE.md` (lifecycle scenarios) · [project #2](https://github.com/users/spaarke-dev/projects/2) (board) |

**Doc 4: per-project pointer (auto-written by `/devops-project-start`)**

Each project's local `README.md` gets a header block written by the skill:

```markdown
> **Portfolio**: GitHub Issue [#N](https://github.com/spaarke-dev/spaarke/issues/N) · Epic: [#E](https://github.com/spaarke-dev/spaarke/issues/E) · Project Status: In Progress · [Portfolio board view](https://github.com/users/spaarke-dev/projects/2/views/X)
```

This makes the folder ↔ portfolio link discoverable from either direction.

**What we explicitly do NOT create:**
- A separate `PROJECT-PORTFOLIO-MANAGEMENT.md` guide — the two existing docs cover it
- A separate stakeholder-facing doc — the polished snapshot (`docs/portfolio/snapshot-{date}.md` per §6.3) is the stakeholder artifact
- Duplicate of the design.md content (this file) in `docs/` — the design lives with the project; reference from CLAUDE.md §16

### 6.3 Polish bar driven by D-10 (3-tier audience)

Because the audience includes stakeholders and other engineers (not just the primary owner), the skills MUST meet these polish requirements:

| Requirement | Driven by | Concrete impact |
|---|---|---|
| Project Issue body is human-readable and self-contained | "Other engineers" + "Stakeholders" | Auto-generated body from `README.md` summary + key dates + status badges; engineers landing on the issue understand the project without opening the folder |
| `/devops-portfolio-status` produces a shareable markdown snapshot | "Stakeholders / leadership" | Skill writes a monthly snapshot to `docs/portfolio/snapshot-{YYYY-MM-DD}.md` formatted for human consumption (Epic-by-Epic narrative, not raw field dump) |
| Each project folder's `README.md` has a "GitHub Issue: #N" pointer | "Other engineers" | `/devops-project-register` writes the pointer back to the local README, so engineers can navigate from folder → portfolio |
| Per-Epic "elevator pitch" field on the Epic issue | "Stakeholders / leadership" | The `description/objectives/focus` block in the Epic issue body is treated as a load-bearing field; not a nice-to-have |
| Naming consistency on labels and field options | All audiences | Lowercase, hyphenated, predictable — no surprise capitalization in views |

### 6.4 Skill ↔ GitHub Project mechanics

All skills use the `gh` CLI + `gh api graphql` for Projects v2 mutations. Authentication is already configured (`gho_…` with `repo`, `project`, `read:org` scopes — verified during this design pass).

Key GraphQL mutations the skills need:
- `addProjectV2ItemById` — add issue to project board
- `updateProjectV2ItemFieldValue` — set custom field values
- `updateProjectV2DraftIssue` — for draft entries (not generally used)

Issue creation uses `gh issue create --label epic --label area:* --title "..." --body "$(cat <<EOF ...)"`.

### 6.5 GitHub ↔ Claude Code automation boundary (the "can GitHub start a session" question)

The honest answer: **partial automation — and that's by design.** GitHub is the *portfolio + capture* surface; Claude Code on your laptop is the *scaffolding + execution* surface. The handoff is a single blessed skill.

**What GitHub CAN do (via Actions / built-in features):**

| Capability | Mechanism | In r1? |
|---|---|---|
| Create Issues with structured bodies | Issue templates + `workflow_dispatch` | ✅ |
| Validate Issue body format on create | Action with schema check | Q10 — recommended for Phase 5 |
| Update Project Issue fields on label/status change | Action + `gh api graphql` | Q10 — optional |
| Server-side sync of Task Count / Tasks Completed | Scheduled Action reading `TASK-INDEX.md` from a PR/commit | Q10 — *not* r1 (user-triggered only) |
| Open branches and PRs | `gh` CLI from Action | Existing CI |
| Comment on Project Issue with "next: run `/devops-project-start --from-issue #N`" | Action triggered on `Type=Project` set | ✅ Polish item, Phase 5 |
| **Create local git worktrees on your laptop** | — | ❌ Worktrees are local-only constructs |
| **Start Claude Code / VS Code on your laptop** | — | ❌ GitHub can't reach into your machine |

**Where GitHub Copilot fits (and doesn't):**

| Tool | What it does | Spaarke fit? |
|---|---|---|
| **GitHub Copilot (autocomplete)** | Inline suggestions in editor | Independent of this workflow |
| **GitHub Copilot Chat** | Conversational chat in VS Code / GitHub | Independent — used per-developer preference |
| **GitHub Copilot Workspace / Coding Agent** | Takes an Issue, produces a draft PR in **GitHub-hosted Codespaces** (cloud VM, not your laptop) | ❌ Not in this workflow. Spaarke's POML / `task-execute` / ADR-aware process is far more structured than Copilot Agent's free-form runtime. We may use it for narrow one-shot bug fixes later, but not for project scaffolding. |
| **Codespaces** | Browser/VS-Code-Web access to a cloud-hosted dev env tied to the repo | Optional read-only stakeholder view of a Project Issue's worktree state — polish item, not r1 |

**Recommended workflow (the skills enforce this):**

```
GitHub (portfolio + capture)         Local (Claude Code execution)
────────────────────────────         ────────────────────────────
1. Idea Issue created                  
   (Type=Idea, label=backlog)          
                                        
2. Idea triaged → promoted to          
   Project Issue (Path A or B)
   (Type=Project, parent=Epic,
    Project Type set)                  
                                        
3. ───────────────────────────────→   /devops-project-start --from-issue #M
   (Action posts pre-filled              (skill reads Issue, scaffolds folder
    command in Issue comment)             + worktree + initial design.md skeleton,
                                          opens VS Code in worktree)
                                        
4. Worktree path + folder slug     ←   Skill writes back: Project Issue gets
   stored back on Project Issue          Worktree Path + Project Folder fields;
                                         local README gets "GitHub Issue: #M" pointer
                                        
5. Spec → tasks → execution:           /design-to-spec
                                        /task-create
                                        /task-execute (per task — auto-bumps
                                          Tasks Completed on Issue per §6.2)
                                        /worktree-sync
                                        
6. Field updates flow on each step      /devops-project-sync at checkpoints
   - Tasks Completed ↑                 (task milestone, daily start, etc.)
   - Project Status changes              
                                        
7. PR opened with "Closes #M"     ←   Standard PR flow; CI runs as normal
                                        
8. PR merged → archive                ─→ /devops-project-archive #M
   (Project Status=Completed,            (sets fields, applies worktree retention
    Status=Done, Issue closed)           policy per Q9)
```

**Bottom line:**
- **GitHub = portfolio + capture + Issue tracking + CI/CD + PRs** (where stakeholders look)
- **Claude Code + skills on your laptop = project scaffolding + spec/task/execution loop** (where work happens)
- **`/devops-project-start --from-issue #N`** is the one and only blessed handoff
- GitHub Actions can *prompt* you to run that skill (post a comment, label the Issue) but cannot *trigger* it on your machine
- Copilot Workspace / Coding Agent is a different agent model and not part of this workflow in r1

### 6.6 What the skills read from local state

| Source | Field(s) it populates |
|---|---|
| `projects/{name}/README.md` (title, summary) | Issue title, body summary |
| `projects/{name}/design.md` (sections 1–3 typically) | Issue body — objectives, scope |
| `projects/{name}/tasks/TASK-INDEX.md` | Task Count, Tasks Completed |
| `projects/{name}/current-task.md` | Project Status (heuristic — has active task → In Progress) |
| Worktree presence (`c:/code_files/spaarke-wt-{name}-r{n}`) | Worktree Path field, worktree:active label |
| Git log on worktree branch | Start Date (first commit), Target Date (manual or projection) |
| Project type tag inference | Project Type field — heuristic from folder name keywords or asked once at registration |

---

## 7. Phasing (proposed — to be detailed in plan.md)

| Phase | Scope | Output |
|---|---|---|
| **Phase 0** (this design + spec phase) | design.md, SPEC.md, decisions.md, plan.md | Approved spec |
| **Phase 1 — Foundation** | Add custom fields + `Project` Type option to GitHub Project #2; create issue templates; create 1 Epic + 1 Project manually as end-to-end smoke | Project board updated, smoke issue live |
| **Phase 2 — Skills** | Implement `/devops-epic-create`, `/devops-project-register`, `/devops-project-sync` skills | Working CLI flow for new projects |
| **Phase 3 — Active backfill** | Backfill all *active and in-flight* projects (~20-30) per D-07 | All current work visible in portfolio |
| **Phase 4 — Existing skill integration** | Hook into `worktree-setup`, `task-execute`, `worktree-sync`, `repo-cleanup` | Auto-registration on creation, auto-updates on task completion |
| **Phase 5 — Polish for shared audience** | Views, saved filters, README → Issue pointers, `/devops-portfolio-status` snapshot export to `docs/portfolio/` per D-10. **Optionally**: Action that auto-comments project-start command (per D-22); scheduled `workflow_dispatch` sync (per D-19) | Roadmap usable for stakeholders; engineers can navigate folder ↔ portfolio |
| **Phase 6 — Documentation deliverables** | Extend [`docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md`](../../docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md) with portfolio integration section. Extend [`docs/procedures/AI-CODING-PROCEDURES-GUIDE.md`](../../docs/procedures/AI-CODING-PROCEDURES-GUIDE.md) with 7 new scenarios (capture idea, promote, update status, close, see portfolio, package N→1, stakeholder view). Update root CLAUDE.md §16 Pointers. Per §6.7 / D-23. | Engineers and stakeholders have a single discoverable place for every lifecycle action |

---

## 8. Alternatives considered

| Alternative | Pros | Cons | Verdict |
|---|---|---|---|
| **Linear** | Best-in-class UX, hierarchy, automation | Paid; new tool; fragments DevOps surface | ❌ Tool fragmentation, cost |
| **Jira** | Enterprise-grade, all features | Heavy; new tool; license cost | ❌ Overkill |
| **Notion database** | Quick to spin up, custom views | Parallel system; manual sync from code/state | ❌ Sync burden |
| **Markdown registry in repo** (e.g. `docs/portfolio/INDEX.md`) | No external dependency | No views; no filtering; manual aggregation | ❌ Discoverability poor |
| **Custom dashboard (web UI)** | Tailored exactly to needs | Build + host + maintain | ❌ Not worth it for internal tooling |
| **Multi-Project layout** (one GitHub Project per Epic) | Cleaner per-Epic boards | Fragmented; harder cross-Epic rollups; more maintenance | ❌ One-project model wins |
| **Mirror POML tasks as GitHub sub-issues** (per project) | Granular GitHub visibility; PR-linkable per task | Doubles maintenance; POML format ≠ issue body; high drift risk | ❌ Counts + status rollup is sufficient |

---

## 9. Open questions / clarifications needed

### 9.1 Resolved (2026-06-23 user input)

| # | Question | Resolution | Recorded as |
|---|---|---|---|
| ~~Q1~~ | Backfill scope | **Active + in-flight only (~20-30)** | D-07 |
| ~~Q2~~ | Task-as-sub-issue policy | **POML stays authoritative; Project Issue tracks counts only** | D-08 |
| ~~Q3~~ | Single board vs new board | **Extend existing Project #2** | D-09 |
| ~~Q4~~ | Audience | **3-tier: engineering owner + engineers + stakeholders/leadership** | D-10 |
| ~~Q5~~ | Epic taxonomy | **Every Project requires an Epic parent** | D-14 |
| ~~Q6~~ | Project Type options | **Module / UI / Infrastructure / Cleanup / Data / Process / AI / Mixed** | D-15 |
| ~~Q7~~ | Project Status set | **Fold Abandoned into Cancelled** → 5-option set | D-16 |
| ~~Q8~~ | Issue body format | **Auto-generated with sync-from-README header** | D-17 |
| ~~Q9~~ | Worktree archival | **Delete worktree on Completed; keep folder with `.archived` marker** | D-18 |
| ~~Q10~~ | CI scheduled sync | **User-triggered + hook-driven for r1; scheduled is Phase 5** | D-19 |
| ~~Q11~~ | N→1 Idea handling | **Keep Idea Issues open as sub-issues** | D-20 |
| ~~Q12~~ | `/devops-project-start` editor | **Opt-in `--open-editor` flag** | D-21 |
| ~~Q13~~ | Action auto-comment | **Phase 5 polish, not r1** | D-22 |

**All open questions resolved.** Design is ready for `/design-to-spec`.

### 9.2 Future questions surfaced for the SPEC phase (not blocking)

| # | Question | Where it'll be addressed |
|---|---|---|
| **F1** | Exact GraphQL mutations + payloads for `/devops-portfolio-setup` (field creation, type extension) | SPEC §3 — functional requirements |
| **F2** | Skill-hook UX: silent-on-success vs always-visible one-liner | SPEC NFR (consistency with existing Spaarke skill conventions) |
| **F3** | Auto-archive trigger: do we auto-archive a project where all tasks `✅` + PR merged, or always wait for explicit `/devops-project-archive`? | SPEC §3 — recommend explicit gate (safer) |
| **F4** | Backfill ordering: should Phase 3 backfill by Epic, by most-recent activity, or alphabetical? | Plan.md task ordering |
| **F5** | Phase 5 polish exact scope — which Actions to ship, which snapshot format, README pointer placement | Plan.md Phase 5 task definition |

---

## 10. Success criteria

The implementation is "done for r1" when:

1. ✅ One Epic exists for each strawman category in §4.6 (or revised list per Q4)
2. ✅ Every **active** worktree has a matching `Type=Project` GitHub Issue, registered via `/devops-project-register`
3. ✅ The **Portfolio Roadmap** view shows all Epics + their Projects with Start/Target dates
4. ✅ `/devops-project-sync` updates Task Count, Tasks Completed, and Project Status with no manual edits
5. ✅ `worktree-setup` (or `project-setup`) offers to register the project, and on yes, the new Project Issue appears on the board with correct fields
6. ✅ `task-execute` updates Tasks Completed on task completion (Step 9)
7. ✅ A user can answer in <30 seconds: "What Epics are active? How many projects are in each? What's the rough portfolio status?" — from one GitHub page
8. ✅ A new engineer can find, in `docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md`, a complete worked example of starting a new project including portfolio integration — without needing this design.md
9. ✅ A user can find, in `docs/procedures/AI-CODING-PROCEDURES-GUIDE.md`, scenario-based steps for capturing an idea, promoting it, updating status, and closing a project
10. ✅ Each project's local `README.md` has a portfolio pointer header linking to its GitHub Issue, parent Epic, and the portfolio board view

---

## 11. Risks

| Risk | Mitigation |
|---|---|
| **Drift between local state and GitHub Project fields** | Skills are write-only from local→GitHub; never the reverse. `/devops-project-sync` is idempotent. |
| **GitHub Project field schema changes are global** — adding a `Project Type` option affects all 22 existing items. | Phase 1 includes a one-time migration pass; existing items get a default of `Mixed` or null. |
| **`gh` API rate limits** during backfill | Batch in groups of 20; respect 5000/hr REST limit; use GraphQL mutations where supported. |
| **POML task counts and `TASK-INDEX.md` ✅ checkboxes drift** | `task-execute` is the only sanctioned task completion path; checkbox + counts updated together. |
| **Folder name ≠ worktree name** in some legacy projects | `Worktree Path` field is independent of `Project Folder`; both stored. |
| **Epics evolve** — projects may be re-parented | Sub-issue re-parenting is supported by GitHub; skill includes `--parent-epic` flag for re-assign |
| **Two GitHub Projects existing (current #2 + #3 "Content pipeline")** — make sure we don't conflict | We extend #2 only; #3 is unrelated and untouched |

---

## 12. References

- GitHub Project #2 (Spaarke Core): https://github.com/users/spaarke-dev/projects/2 — current state inspected via `gh project field-list 2`
- [.claude/skills/INDEX.md](../../.claude/skills/INDEX.md) — existing skill registry
- [.claude/skills/worktree-setup/SKILL.md](../../.claude/skills/worktree-setup/SKILL.md), [worktree-sync](../../.claude/skills/worktree-sync/SKILL.md), [project-pipeline](../../.claude/skills/project-pipeline/SKILL.md), [task-execute](../../.claude/skills/task-execute/SKILL.md)
- Existing exemplar project structure: [projects/ai-spaarke-insights-engine-r1/](../../projects/ai-spaarke-insights-engine-r1/)
- GitHub Projects v2 docs: https://docs.github.com/en/issues/planning-and-tracking-with-projects/learning-about-projects/about-projects
- Root [CLAUDE.md](../../CLAUDE.md) §13 (Context Layer Hierarchy), §16 (Pointers)

---

## 13. Next steps

1. **User reviews this design.md** — answer Q1–Q10 in §9
2. Run `/design-to-spec` to transform into SPEC.md (FRs, NFRs, phased deliverables)
3. Run `/task-create` to decompose SPEC into POML task files
4. Set up worktree at `c:/code_files/spaarke-wt-spaarke-devops-project-tracking-r1` via `/worktree-setup` (recursive — first dogfood of this very project's flow)
5. Phase 1 (foundation) execution
