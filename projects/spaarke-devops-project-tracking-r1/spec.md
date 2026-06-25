# Spaarke DevOps Project Tracking (r1) — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-06-23
> **Source**: [design.md](design.md) (639 lines, 23 ratified decisions, 6 phases)

---

## Executive Summary

Spaarke runs ~30+ active and ~100+ historical projects as git worktrees following a structured spec-driven process, but has **no portfolio-level view** of which Epics exist, which projects are active/planned/on-hold/completed, or how they roll up. This project extends the existing GitHub Project #2 ("Spaarke Core") with new fields, a `Type=Project` option, GitHub-native sub-issue hierarchy (Epic → Project), 9 new `/devops-*` Claude Code skills, automation hooks into 9 existing Spaarke skills, and documentation extensions to two existing guides — delivering a single, GitHub-native, low-friction portfolio tracking surface where the day-to-day development workflow itself keeps the portfolio current.

---

## Scope

### In Scope

- **GitHub Project #2 extension**: add `Project` Type option; 5 new custom fields (`Project Type`, `Worktree Path`, `Project Folder`, `Task Count`, `Tasks Completed`, `Project Status`); 7 labels; 3 issue templates (`epic.yml`, `project.yml`, `idea.yml`)
- **9 new Claude Code skills** in the `/devops-*` family (portfolio-setup, epic-create, idea-create, idea-promote, project-start, project-register, project-sync, portfolio-status, project-archive)
- **Automation hooks** injected into 9 existing Spaarke skills so portfolio updates ride on normal workflow with no explicit `/devops-*` typing during day-to-day work
- **Active-project backfill** (~20-30 in-flight projects) onto the portfolio board
- **Portfolio views**: Roadmap (timeline), Active projects, Backlog, On-hold/Cancelled, By type tag
- **Documentation extensions** to `docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md` and `docs/procedures/AI-CODING-PROCEDURES-GUIDE.md`; one-line addition to root `CLAUDE.md` §16 Pointers
- **Per-project README portfolio pointer block** auto-written by `/devops-project-start` (folder ↔ portfolio bidirectional link)
- **Initial Epic taxonomy** (~10-12 Epics) per design.md §4.6

### Out of Scope

- Historical backfill of all ~133 projects (D-07 — only active/in-flight backfilled)
- Mirroring POML tasks as GitHub sub-issues (D-08 — POML stays authoritative)
- A new dedicated GitHub Project board separate from #2 (D-09)
- Customer-facing portfolio view (D-10 — audience is internal eng + engineers + stakeholders)
- A new top-level `docs/guides/PROJECT-PORTFOLIO-MANAGEMENT.md` (D-23 — extend existing docs)
- Replacing POML task files with GitHub Issues
- Real-time file-watcher sync (on-demand and hook-triggered only)
- Time tracking / billable hours / story-points / velocity
- A custom web dashboard outside GitHub
- GitHub Copilot Workspace / Coding Agent integration in r1
- Story-point estimation or sprint-based fixed-cadence modeling (D-03)
- Auto-trigger of `/devops-project-archive` on PR merge (F3 default: explicit gate)

### Affected Areas

| Path | Purpose | Touched in |
|---|---|---|
| `https://github.com/users/spaarke-dev/projects/2` (Project #2) | Extend fields, add Type option, configure views | Phase 1, 5 |
| `.github/ISSUE_TEMPLATE/{epic,project,idea}.yml` | New issue templates | Phase 1 |
| `.github/workflows/*.yml` (new files) | Optional auto-comment Action (Phase 5) and optional scheduled sync (Phase 5) | Phase 5 |
| `.claude/skills/devops-*/SKILL.md` | 9 new skill folders + SKILL.md per skill | Phase 2 |
| `.claude/skills/{design-to-spec,project-pipeline,task-create,task-execute,context-handoff,worktree-setup,worktree-sync,repo-cleanup,merge-to-master}/SKILL.md` | Inject portfolio hooks into 9 existing skills | Phase 4 |
| `docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md` | Add portfolio integration section | Phase 6 |
| `docs/procedures/AI-CODING-PROCEDURES-GUIDE.md` | Add 7 lifecycle scenarios | Phase 6 |
| `CLAUDE.md` (root) §16 Pointers | Add portfolio row | Phase 6 |
| `docs/portfolio/snapshot-{date}.md` (new directory) | Stakeholder snapshot output of `/devops-portfolio-status` | Phase 5 |
| `projects/{name}/README.md` (all projects) | Auto-written portfolio pointer header block | Phase 4 (skill side-effect on registration) |

---

## Requirements

### Functional Requirements

#### Phase 1 — Foundation

1. **FR-01**: Extend Project #2 `Type` single-select field with new option `Project`. Existing options (`Idea`, `Epic`, `Story`, `Task`, `Bug`, `Spike`) preserved unchanged. — **Acceptance**: `gh project field-list 2 --owner spaarke-dev --format json` includes `"name":"Project"` in the `Type` field options.

2. **FR-02**: Add 6 new custom fields to Project #2 with these exact schemas:
   - `Project Type` — single-select: `Module`, `UI`, `Infrastructure`, `Cleanup`, `Data`, `Process`, `AI`, `Mixed` (per D-15)
   - `Worktree Path` — text
   - `Project Folder` — text
   - `Task Count` — number
   - `Tasks Completed` — number
   - `Project Status` — single-select: `Planned`, `In Progress`, `On Hold`, `Completed`, `Cancelled` (per D-16, `Abandoned` folded into `Cancelled`)
   — **Acceptance**: `gh project field-list 2 --owner spaarke-dev --format json` shows all 6 fields with exact option lists; existing 20 fields untouched.

3. **FR-03**: Create 7 repository labels: `epic`, `project`, `backlog`, `worktree:active`, `worktree:archived`, `on-hold`, `cancelled`. — **Acceptance**: `gh label list` includes all 7 with consistent lowercase-hyphenated naming.

4. **FR-04**: Create 3 issue templates at `.github/ISSUE_TEMPLATE/{epic,project,idea}.yml` with structured body fields parseable by `/devops-*` skills. Epic template fields: title, objectives/focus, scope, success criteria, projected timeframe. Project template fields: title, project folder slug, worktree slug, proposed `Project Type`, parent Epic reference, summary, projected Start/Target Date. Idea template fields: title, summary, why-it-matters, tentative Epic, originating source. — **Acceptance**: Opening the GitHub "New issue" picker shows the 3 templates; submitting each creates an Issue with expected body structure.

5. **FR-05**: Create initial Epic Issues per the §4.6 taxonomy strawman (~10-12 Epics): AI Platform & Chat, Insights Engine, Smart Todo, Document Intelligence, BFF & Test Hygiene, Auth & SSO, Code Quality, Procedures & Knowledge, CI/CD & Tooling, Insights/Widgets/Search, Communications, Multi-tenant. — **Acceptance**: All Epic Issues exist on Project #2 with `Type=Epic`, label `epic`, populated description/objectives field, and are visible in the Epics-overview portfolio view.

#### Phase 2 — Skills

6. **FR-06**: Implement `/devops-portfolio-setup` skill performing one-time setup: extends Type field (FR-01), adds 6 custom fields (FR-02), creates 7 labels (FR-03), commits 3 issue templates (FR-04). Idempotent re-run safe. — **Acceptance**: Running on a fresh project produces FR-01..FR-04 acceptance; running a second time produces no errors and no duplicate creations.

7. **FR-07**: Implement `/devops-epic-create` skill: takes title + objectives/focus + scope, creates an Epic Issue on Project #2 with `Type=Epic`, label `epic`, populated fields. — **Acceptance**: One invocation creates one Epic visible on the portfolio board with all template fields populated; sub-issues panel ready to accept Project sub-issues.

8. **FR-08**: Implement `/devops-idea-create` skill: takes one-line summary + (optional) tentative Epic, creates an Idea Issue with `Type=Idea`, label `backlog`. **No local folder or worktree created**. — **Acceptance**: Idea Issue appears in the Idea backlog view; no `projects/{name}/` folder created.

9. **FR-09**: Implement `/devops-idea-promote` skill supporting **two paths per D-12**:
   - Path A `--to-project --epic #E`: flips Idea's `Type` from `Idea` to `Project`, sets `Parent issue` to Epic #E, populates `Project Type`, applies `project` label
   - Path B `--package #X #Y #Z --epic #E`: creates a NEW Project Issue with `Type=Project`, sets `Parent issue` to Epic #E, attaches Idea Issues #X #Y #Z as **sub-issues of the Project Issue (kept open per D-20)**
   — Does NOT create local worktree (FR-10's job). — **Acceptance**: Path A: original Idea Issue now has `Type=Project` and parent Epic set. Path B: new Project Issue exists with 3 Idea sub-issues showing in `Sub-issues progress`; Ideas remain open.

10. **FR-10**: Implement `/devops-project-start --from-issue #N` skill — **THE BLESSED HANDOFF (per D-13)**. Reads Project Issue #N body and fields. Scaffolds `projects/{slug}-r1/` folder; creates git worktree at `c:/code_files/spaarke-wt-{slug}-r1` from master with feature branch; drafts `design.md` skeleton populated from Issue body; writes back `Worktree Path` + `Project Folder` field values to Issue; writes `> **Portfolio**: GitHub Issue #N · Epic: #E · Project Status: Planned · [board view]` header block to the new local `README.md`. Supports `--open-editor` flag (default off, per D-21). For Path B promotions, supports `--absorbs #X #Y #Z` to include "Source Ideas" section in drafted design.md preserving original framings. — **Acceptance**: After invocation, both the local worktree and the Issue's portfolio fields are populated; local README contains the portfolio pointer block; design.md skeleton is ready for `/design-to-spec`.

11. **FR-11**: Implement `/devops-project-register --from-folder` skill: inverse direction of FR-10 — for an *existing* worktree/folder without a Project Issue, creates the Project Issue and populates fields from local state. Used in Phase 3 backfill. — **Acceptance**: Run against an existing `projects/{name}-r1/` folder produces a Project Issue with `Worktree Path`, `Project Folder`, `Task Count`, `Tasks Completed`, and computed `Project Status` matching local state.

12. **FR-12**: Implement `/devops-project-sync` skill: re-reads local state (`tasks/TASK-INDEX.md`, `current-task.md`, worktree presence) and updates GitHub Project Issue custom fields. Idempotent. Partial-success tolerant (per resolved F7 below): report individual field failures, continue updating others. — **Acceptance**: Run against a project with N completed POML tasks produces `Tasks Completed=N` on the Issue; running twice in a row produces no GitHub API mutations on the second run (idempotent verification).

13. **FR-13**: Implement `/devops-portfolio-status` skill: prints concise dashboard (active Epics → their Projects → rollup metrics) to terminal; optional `--snapshot` writes `docs/portfolio/snapshot-{YYYY-MM-DD}.md` formatted for stakeholders per D-10 (Epic-by-Epic narrative, not raw field dump). — **Acceptance**: Terminal output answers in <30 seconds: "What Epics are active? How many projects in each? Rough portfolio status?". `--snapshot` produces a markdown file with no raw field IDs leaking.

14. **FR-14**: Implement `/devops-project-archive` skill: sets `Project Status=Completed` or `Cancelled`, captures final `Task Count`/`Tasks Completed`, closes the Issue with `Status=Done` (Completed) or label `cancelled` (Cancelled), **deletes the local git worktree per D-18** (leaves `projects/{name}/` folder with new `.archived` marker file containing archive date + final status + closing PR reference). — **Acceptance**: Worktree at `c:/code_files/spaarke-wt-{slug}-r1` is removed (`git worktree list` confirms); `projects/{name}/` retains all files plus `.archived`; Project Issue closed with correct Status/label.

#### Phase 3 — Active backfill

15. **FR-15**: Backfill the ~20-30 currently active/in-flight projects onto Project #2 via `/devops-project-register --from-folder`. "Active/in-flight" defined per resolved F6 below. Issue ordering: by Epic, then by most-recent commit activity on the worktree branch. — **Acceptance**: All active/in-flight projects identified by the F6 criteria have corresponding Project Issues; each has correct Parent Epic, populated fields, and is visible in the Active projects portfolio view.

#### Phase 4 — Automation hooks into existing skills

16. **FR-16**: Inject portfolio-update hook into `/design-to-spec`: at end of skill (after spec.md is written), if a Project Issue exists for the folder, call `/devops-project-sync` and ensure `Project Status=In Progress`. — **Acceptance**: Running `/design-to-spec` on a project with an existing Issue updates `Project Status` to `In Progress` automatically.

17. **FR-17**: Inject hook into `/project-pipeline`: at start, if no Project Issue exists for the folder, prompt "register on portfolio?" → calls `/devops-project-register`; otherwise call `/devops-project-sync`. — **Acceptance**: First `/project-pipeline` run on a non-registered project produces a Project Issue (after user confirms); subsequent runs sync silently.

18. **FR-18**: Inject hook into `/task-create`: after tasks scaffolded, set `Task Count` field on the Project Issue from POML file count. — **Acceptance**: After `/task-create`, the Issue's `Task Count` equals `ls projects/{name}/tasks/*.poml | wc -l`.

19. **FR-19**: Inject hook into `/task-execute` Step 9 (task completion): increment `Tasks Completed` field; if `Tasks Completed == Task Count`, prompt "Promote Project Status to Completed candidate?" — **Acceptance**: Each task completion via `task-execute` increments `Tasks Completed` by exactly 1; last task completion shows the prompt.

20. **FR-20**: Inject hook into `/context-handoff`: always call `/devops-project-sync` at end of handoff. **Per design.md §6.2 this is the highest-value hook** — compaction checkpoints (every 3 steps, >60% context, 5+ files modified) coincide with portfolio checkpoints so the board is never >3 task steps stale. — **Acceptance**: Any `/context-handoff` invocation in a project with an Issue produces a successful `/devops-project-sync` call visible in handoff output.

21. **FR-21**: Inject hook into `/worktree-setup`: after worktree scaffolded, if matching Issue exists, link it via `/devops-project-register`; else prompt "Register project on portfolio now?". — **Acceptance**: `/worktree-setup` for a project with a pre-existing Issue produces correct Worktree Path linkage; for new projects, prompt fires once.

22. **FR-22**: Inject hook into `/worktree-sync`: at end of sync, call `/devops-project-sync`. — **Acceptance**: `/worktree-sync` end-of-output includes a successful sync confirmation line.

23. **FR-23**: Inject hook into `/repo-cleanup`: for projects identified as archive candidates (e.g., merged + no recent activity), call `/devops-project-archive` after explicit user confirmation. — **Acceptance**: When `/repo-cleanup` detects archive candidates, it prompts; on confirmation, the archive skill runs and produces FR-14 acceptance state.

24. **FR-24**: Inject hook into `/merge-to-master`: after merge succeeds, update Project Issue with merged PR # in body and a comment; if all POML tasks are complete + PR merged, prompt for archive. — **Acceptance**: Post-merge, the Project Issue has a comment "Merged via PR #M" or equivalent; archive prompt fires only when both conditions met.

#### Phase 5 — Polish for shared audience

25. **FR-25**: Configure portfolio views on Project #2: **Portfolio Roadmap** (Roadmap by Start Date/Target Date, filter `Type IN (Epic, Project)`), **Epics overview** (Table, `Type=Epic`), **Active projects** (Table, `Type=Project AND Project Status=In Progress`), **Project backlog** (Table, `Type=Project AND Project Status=Planned`), **On hold/Cancelled** (Table, `Project Status IN (On Hold, Cancelled)`), **By type tag** (Board grouped by `Project Type`, `Type=Project`). The 7th existing default Issue-level work view is preserved unchanged. — **Acceptance**: Each view exists in Project #2 with the specified filter; clicking the Portfolio Roadmap shows all Epics + Projects on a timeline.

26. **FR-26**: Per-project README portfolio pointer block: `/devops-project-start` (FR-10) and `/devops-project-register` (FR-11) both write a header to the local `README.md`:
   ```
   > **Portfolio**: GitHub Issue [#N](url) · Epic: [#E](url) · Project Status: {status} · [Portfolio board view](url)
   ```
   — **Acceptance**: Every active project's README has the block at the top (visual inspection of 5+ projects in Phase 3 backfill output).

27. **FR-27** *(optional, Phase 5)*: GitHub Action that auto-comments the pre-filled `/devops-project-start --from-issue #N` command on a Project Issue when its `Type` changes to `Project`. Per D-22 this is polish, not r1 acceptance. — **Acceptance** (if shipped): Creating a Project Issue triggers an Action that posts a comment within 60 seconds containing the exact command.

28. **FR-28** *(optional, Phase 5)*: Scheduled `workflow_dispatch` Action that runs server-side equivalent of `/devops-project-sync` nightly across all active projects. Per D-19 this is opt-in if drift becomes a problem. — **Acceptance** (if shipped): Action runs at the scheduled cron time; drift report posted as an Issue comment when any field differs from local state.

#### Phase 6 — Documentation

29. **FR-29**: Extend [`docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md`](../../docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md) (423 lines today). Add **Step 0** for idea capture (`/devops-idea-create` or manual GitHub Issue). Add **Portfolio Integration** section covering: Epic↔Project mechanics with screenshots of `Parent issue` and `Sub-issues` UI; Idea → Project promotion (Path A 1→1 and Path B N→1); `/devops-project-start --from-issue` handoff walkthrough; auto-hook behaviors per FR-16..FR-24. Add the 9 `/devops-*` skills to the command reference. Add portfolio-specific troubleshooting (missing Type=Project, orphan worktree, drift). — **Acceptance**: The doc covers the full lifecycle from idea to archive without requiring the reader to open `design.md` of this project.

30. **FR-30**: Extend [`docs/procedures/AI-CODING-PROCEDURES-GUIDE.md`](../../docs/procedures/AI-CODING-PROCEDURES-GUIDE.md) (525 lines today). Add 7 new scenarios: (1) "Capture an idea before it's a real project"; (2) "Promote ideas into a project (with packaging)"; (3) "Update a project's status"; (4) "Close (complete or cancel) a project"; (5) "See what's running across all projects"; (6) "Package multiple ideas into one project"; (7) "I'm a stakeholder — where do I look?". Each scenario follows existing doc's "what to say / what happens automatically / what to check" pattern. — **Acceptance**: All 7 scenarios present with code blocks for the skills/commands and explanations of automatic behavior; reader can find each by Ctrl-F on a key phrase.

31. **FR-31**: Update root [`CLAUDE.md`](../../CLAUDE.md) §16 Pointers with one new row:
   ```
   | **Portfolio tracking + DevOps procedures** | `docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md` (initiation + portfolio integration) · `docs/procedures/AI-CODING-PROCEDURES-GUIDE.md` (lifecycle scenarios) · [project #2](url) (board) |
   ```
   And entry in `.claude/CHANGELOG.md`. — **Acceptance**: The pointer row exists in CLAUDE.md §16; changelog has a 2026-06-XX entry referencing this project.

### Non-Functional Requirements

- **NFR-01**: Single GitHub Project surface (#2) is the only portfolio tracking system. No parallel spreadsheet, Notion database, Linear board, or external dashboard introduced. (Per D-01, D-09.)

- **NFR-02**: POML task files in `projects/{name}/tasks/*.poml` remain the **authoritative** source for task content. GitHub Project Issues track only `Task Count` and `Tasks Completed` aggregates; no per-task GitHub sub-issues created. (Per D-08.)

- **NFR-03**: Automation hooks from FR-16..FR-24 are **silent on success or emit a single confirmation line** (e.g. `✅ Portfolio synced: #N`). Hooks MUST NOT block the host skill on failure; failures degrade to a single warning line and the host skill continues.

- **NFR-04**: `/devops-project-sync` is **idempotent** — repeated runs against unchanged local state produce zero GitHub API mutations. Verified via a no-op dry-run mode.

- **NFR-05**: GitHub API operations respect rate limits: ≤5000/hr REST, ≤5000 points/hr GraphQL. Backfill (FR-15) batches mutations in groups of 20 with built-in exponential backoff on 429.

- **NFR-06**: Auto-generated Project Issue body (per D-17) begins with the comment `<!-- DO NOT EDIT — synced from README.md by /devops-project-sync -->` so manual edits are explicitly discouraged.

- **NFR-07**: All 9 new skills follow Spaarke skill convention: `.claude/skills/{name}/SKILL.md` with YAML frontmatter (`description`, `tags`, `techStack`, `appliesTo`, `alwaysApply: false`, `last-reviewed: {date}`) plus Prerequisites/Purpose/Steps/Failure-Modes sections per existing skill structure.

- **NFR-08**: Documentation extensions (FR-29, FR-30) **preserve existing structure** of the host docs. No section renumbering, no breaking format changes to existing examples, no rearrangement of existing scenarios.

- **NFR-09**: Worktree retention policy on archive (per D-18): worktree deleted (`git worktree remove`), local `projects/{name}/` folder retained with new `.archived` marker file. Branch history preserved on remote.

- **NFR-10**: All skills assume `gh` CLI v2.40+ is authenticated with token scopes `repo`, `project`, `read:org` (already in place per current session check). No new tokens or credentials introduced.

---

## Technical Constraints

### Applicable ADRs

This project's domain (DevOps tooling, skill authoring, GitHub Project configuration, documentation) **does not have direct mandatory ADR coverage** in the Spaarke ADR catalog. The Spaarke ADR set focuses on code/auth/AI architecture concerns. Preliminary discovery surfaces:

- **No mandatory ADRs apply** to this project — confirmed during design-to-spec Step 3 discovery
- **ADR-010 (DI Minimalism)** — *informational only*; would apply if any of the 9 new skills introduces .NET service code, which is not anticipated (skills are markdown + `gh` CLI invocations)
- **Researcher subagent** (`.claude/agents/researcher.md`) may be invoked if GitHub Projects v2 GraphQL API behavior needs current Microsoft/GitHub-side documentation lookup

Confirmation that this is expected: design.md §11 "Risks" notes the sparse ADR surface. Comprehensive ADR/pattern discovery happens in `/project-pipeline` Step 2 (after this spec is approved) — if any additional ADR applies, it will surface then and be added.

### MUST Rules

- ✅ **MUST** use `gh` CLI (or `gh api graphql`) for all GitHub Project API mutations. Do NOT introduce a custom Octokit / REST-client wrapper.
- ✅ **MUST** preserve all 20 existing fields on Project #2 unchanged. Adding fields is allowed; removing or renaming is forbidden in r1.
- ✅ **MUST** keep all 9 new skills idempotent. Re-running any skill against unchanged state produces no API mutations.
- ❌ **MUST NOT** commit any GitHub tokens, PATs, or credentials. Use the pre-existing authenticated `gh` session.
- ❌ **MUST NOT** modify any existing Spaarke skill in a way that breaks its current contract. Hook injection is additive only.
- ❌ **MUST NOT** introduce a parallel portfolio tracking system (spreadsheet, Notion, Linear). NFR-01 binding.
- ❌ **MUST NOT** mirror POML tasks as GitHub sub-issues. NFR-02 binding (per D-08).

### Existing Patterns to Follow

- **Skill convention**: `.claude/skills/{skill-name}/SKILL.md` with YAML frontmatter. Reference exemplars: [`.claude/skills/task-execute/SKILL.md`](../../.claude/skills/task-execute/SKILL.md), [`.claude/skills/worktree-setup/SKILL.md`](../../.claude/skills/worktree-setup/SKILL.md), [`.claude/skills/design-to-spec/SKILL.md`](../../.claude/skills/design-to-spec/SKILL.md).
- **Hook-injection pattern**: see how `task-execute` invokes `code-review` and `adr-check` at Step 9.5 — the calling skill mentions the hooked skill in its `Steps` section. Replicate for FR-16..FR-24.
- **Issue template YAML schema**: see existing public examples like `.github/ISSUE_TEMPLATE/bug_report.yml` patterns from any well-maintained OSS repo; structure uses `name`, `description`, `title`, `labels`, `body` (with form-style fields).
- **gh CLI Projects v2 mutations**: reference `gh project --help`, `gh api graphql -f query=...` for `addProjectV2ItemById`, `updateProjectV2ItemFieldValue`, `updateProjectV2Field` mutations.
- **Doc extension structure**: see how `docs/procedures/AI-CODING-PROCEDURES-GUIDE.md` structures scenarios already; replicate the "what to say / what happens / what to check" pattern.
- **Spaarke `CHANGELOG.md` entry style**: see `.claude/CHANGELOG.md` for existing entry format.

---

## Success Criteria

1. [ ] **Phase 1 complete**: Project #2 has `Type=Project` option, 6 new fields, 7 labels, 3 issue templates. Verify: `gh project field-list 2 --owner spaarke-dev --format json | grep -c "Project Type"` returns 1.

2. [ ] **All 12 initial Epics created**: visible in Epics-overview view. Verify: open Project #2 Epics view, count rows.

3. [ ] **All 9 `/devops-*` skills implemented and discoverable**: listed in `.claude/skills/INDEX.md`. Verify: `ls .claude/skills/devops-* | wc -l` returns 9.

4. [ ] **Active-project backfill complete**: every active/in-flight worktree (per F6 definition) has a matching `Type=Project` Issue with all 6 new fields populated. Verify: count of `Project Status=In Progress OR Planned` Issues matches the F6-derived active project count.

5. [ ] **`/devops-project-sync` is idempotent**: dry-run after a clean sync produces zero proposed mutations. Verify: run skill twice; second run reports "no changes."

6. [ ] **Hooks active in 9 existing skills**: each of the 9 hooked skills mentions `/devops-project-sync` (or equivalent) in its Steps section. Verify: `grep -l "devops-project-sync\|devops-project-register" .claude/skills/{design-to-spec,project-pipeline,task-create,task-execute,context-handoff,worktree-setup,worktree-sync,repo-cleanup,merge-to-master}/SKILL.md` returns 9 lines.

7. [ ] **Portfolio Roadmap view usable**: a user (not the implementer) can answer in <30 seconds: "What Epics are active? How many projects in each? Rough portfolio status?" — from a single GitHub page.

8. [ ] **`/devops-project-start` round-trip works end-to-end**: from a Project Issue, the skill creates folder + worktree + design.md skeleton + writes back fields + README pointer. Verify: complete one end-to-end run on a smoke-test Idea.

9. [ ] **Documentation extensions land**: `docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md` has portfolio integration section; `docs/procedures/AI-CODING-PROCEDURES-GUIDE.md` has all 7 new scenarios. Verify: grep for new section headers; visual review of scenario count.

10. [ ] **Per-project README pointer present** in all active projects after Phase 3+4: each `projects/{name}/README.md` has the `> **Portfolio**: GitHub Issue …` header block. Verify: `grep -L "> \*\*Portfolio\*\*" projects/*/README.md` returns no active projects.

11. [ ] **CLAUDE.md §16 updated** with portfolio row. Verify: `grep "Portfolio tracking" CLAUDE.md` returns 1 match.

12. [ ] **No regression in existing skills**: all hooked existing skills still pass their own success criteria (per their respective SKILL.md). Verify: dry-run each hooked skill on a known-good project; observe no errors.

---

## Dependencies

### Prerequisites

- `gh` CLI v2.40+ installed and authenticated as `spaarke-dev` (already in place — verified `gh auth status` during design-to-spec analysis)
- GitHub PAT/OAuth token scopes: `repo`, `project`, `read:org`, `gist`, `workflow` (already in place)
- Existing Spaarke skill infrastructure: `.claude/skills/INDEX.md`, frontmatter format, Skill tool invocation pattern
- Existing GitHub Project #2 "Spaarke Core" reachable at `https://github.com/users/spaarke-dev/projects/2`
- All 9 existing Spaarke skills to be hooked are present and in their current form: `design-to-spec`, `project-pipeline`, `task-create`, `task-execute`, `context-handoff`, `worktree-setup`, `worktree-sync`, `repo-cleanup`, `merge-to-master`

### External Dependencies

- **GitHub Projects v2 GraphQL API** — `addProjectV2ItemById`, `updateProjectV2ItemFieldValue`, `updateProjectV2Field`, `createProjectV2Field` mutations
- **GitHub Issues API** — Issue creation, labeling, sub-issue parent linking
- **GitHub Issue Templates** — `.github/ISSUE_TEMPLATE/*.yml` YAML schema (form-based templates)
- No external services beyond GitHub itself; no new Azure resources; no new third-party API keys

---

## Owner Clarifications

Captured from design phase Q&A (Q1–Q13) and ratified as decisions D-01..D-23. The full list of 23 decisions is in [design.md §5](design.md). The most implementation-impactful clarifications are summarized below:

| Topic | Question Asked | Owner's Answer | Implementation Impact |
|---|---|---|---|
| Backfill scope | Backfill all 133 projects, only in-flight, or only new? | Active + in-flight only (~20-30) | FR-15 scope; no historical backfill in r1; Phase 5 not introduced as a separate backfill phase |
| Tasks as sub-issues | Mirror every POML task as a GitHub sub-issue? | No — POML stays authoritative; track counts only | NFR-02; FR-18/FR-19 update aggregate fields only; no per-task Issues |
| Single board vs new | Extend existing Project #2 or create dedicated Portfolio board? | Extend existing #2 | NFR-01; all new fields land on #2; views filter by `Type IN (Epic, Project)` |
| Audience | Who views the portfolio besides you? | Engineering owner + engineers + stakeholders/leadership (not customer-facing) | Polish bar set: human-readable Issue body (FR-26), shareable snapshot (FR-13), README ↔ Issue pointers (FR-26) |
| Epic-Project mechanics | How exactly are Epics and Projects linked in GitHub? | Native sub-issues (`Parent issue` field) | FR-09 sets Parent issue automatically; FR-29 doc covers the manual GitHub UI variant |
| Idea capture vs promotion | How do raw ideas become projects, and what about packaging? | Ideas = GitHub Issues; `/devops-idea-promote` supports both 1→1 and N→1; local scaffolding only via `/devops-project-start` | FR-08, FR-09, FR-10 — three distinct skills with crisp roles; D-12 binding |
| Automation boundary | Can GitHub trigger Claude Code to start a project? | No — local worktree creation requires `/devops-project-start` on user's machine. Copilot Workspace NOT in workflow for r1 | FR-10 is the one blessed handoff; FR-27 (Action auto-comment) is polish only |
| Epic taxonomy | Are some projects standalone (no Epic parent)? | No — every Project must have an Epic parent (even single-Project Epics) | Phase 1 FR-05 creates ~12 Epics; FR-09/FR-10 enforce `--epic` parameter |
| Project Type set | Confirm `Module/UI/Infrastructure/Cleanup/Data/Process/AI/Mixed`? | Yes | FR-02 schema |
| Status set | Distinguish `Abandoned` from `Cancelled`? | Fold to Cancelled only | FR-02 Project Status options |
| Issue body | Auto-generated or hand-written? | Auto-generated with "DO NOT EDIT — synced from README" header | NFR-06; FR-12 writes the body |
| Worktree on archive | Keep worktree or delete? | Delete worktree; keep folder with `.archived` | FR-14, NFR-09 |
| CI scheduled sync | Schedule a nightly Action? | User-triggered + hook-driven for r1; scheduled is Phase 5 optional | FR-28 optional |
| N→1 Idea handling | Close Ideas on absorption, or keep open as sub-issues? | Keep open as sub-issues | FR-09 Path B behavior; D-20 binding |
| `/devops-project-start` editor | Auto-open VS Code, or just print path? | Opt-in `--open-editor` flag | FR-10 flag handling |
| Action auto-comment | Ship the auto-comment Action in r1? | Phase 5 polish, not r1 acceptance | FR-27 optional |
| Documentation placement | Create new doc, or extend existing? | Extend existing: HOW-TO-INITIATE-NEW-PROJECT.md + AI-CODING-PROCEDURES-GUIDE.md + CLAUDE.md §16. No new top-level guide. | FR-29, FR-30, FR-31; D-23 binding |
| "Active/in-flight" project definition (F6) | What qualifies a project for FR-15 backfill? | Worktree exists at `c:/code_files/spaarke-wt-*` AND (`current-task.md` indicates active task OR open PR references the branch OR commits in last 30 days) | FR-15 enumeration step; backfill scope locked |
| `/devops-project-sync` error handling (F7) | On API failure mid-sync, hard fail / retry / partial-success? | Partial-success allowed; failures collected and reported; next idempotent run heals | NFR-04 contract; FR-12 behavior |
| `projects/_backlog/needs-a-project.md` disposition (F8) | Keep, deprecate, or migrate? | Keep as a "draft pad" before formalizing into Idea Issues — no auto-migration of contents | FR-08 doc surface; FR-29 troubleshooting section |
| Project slug derivation on `/devops-project-start` (F9) | Where does the worktree slug come from? | Derived from Project Issue title (kebab-cased, suffixed `-r1`; bumped to `-r2`+ if folder exists). `--slug` flag overrides. | FR-10 deterministic-output contract |

For the complete 23-decision table, see [design.md §5](design.md).

---

## Assumptions

These items were not explicitly specified by the owner; the spec proceeds with these stated assumptions. Owner may correct any before implementation begins:

- **`gh` CLI as canonical interface**: All skills invoke `gh` (or `gh api graphql`). No Octokit/REST library wrapper introduced.
- **Single-user model**: The operator (project owner) is the only user running `/devops-*` skills locally in r1. No multi-user collision handling needed.
- **Single repo**: All Projects sit on `spaarke-dev/spaarke`. No cross-repo Issue linking needed.
- **POML format stability**: The POML task file format does not change during this project's lifetime. Hooks that parse `TASK-INDEX.md` checkbox state assume current format.
- **GitHub Projects v2 GraphQL API stability**: No breaking changes to the field-mutation surface during r1 execution.
- **Skill execution context**: All skills run in the user's local Claude Code session against the user's local repo + worktrees. No server-side skill execution in r1.
- **Backfill timing**: Phase 3 backfill runs after Phase 2 skills are stable and tested on a 1-2 project smoke test. If skills are unstable, backfill scope can be deferred without blocking r1.
- **Hook silence threshold**: Successful hook outputs default to ≤1 line per FR-NFR-03; verbose mode available only with `--verbose` flag.

---

## Unresolved Questions

These items are NOT blocking for spec → pipeline transition, but should be resolved by the implementer during Phase 2 skill design or by user input during Phase 3 backfill:

- [ ] **F1 — GraphQL mutation payloads**: Exact JSON payloads for `createProjectV2Field` (single-select with option list), `updateProjectV2ItemFieldValue` for each field type. **Blocks**: FR-06 implementation detail. **Resolution path**: Discovered during Phase 2 skill code; reference [Researcher subagent](../../.claude/agents/researcher.md) if GitHub API docs are unclear.

- [ ] **F2 — Skill-hook UX (silent vs visible)**: Should hook output be silent on success, or always emit a single ✅ line? Per NFR-03 we land on "single confirmation line"; F2 is whether that's truly always-on or only on state change. **Recommendation**: Always-on single line — keeps the hook discoverable to humans. **Blocks**: nothing critical; cosmetic.

- [ ] **F3 — Auto-archive trigger**: Should `/merge-to-master` (FR-24) auto-archive when all conditions met, or always require explicit `/devops-project-archive`? **Recommendation**: Explicit gate (safer, reversible). **Blocks**: FR-24 prompt-vs-auto behavior.

- [ ] **F4 — Phase 3 backfill ordering**: By Epic, by most-recent activity, alphabetical, or owner-chosen sequence? **Recommendation**: By Epic, then by most-recent commit activity within Epic. **Blocks**: FR-15 task ordering; resolved in `plan.md`.

- [ ] **F5 — Phase 5 polish exact scope**: Which optional FR-27 (Action auto-comment) and FR-28 (scheduled sync) ship in r1's Phase 5 vs defer to a follow-up? **Recommendation**: Defer both — r1 acceptance excludes optional FRs; ship in a polish round if drift becomes visible. **Blocks**: nothing in r1 acceptance; Phase 5 task list.

- [x] ~~**F6 — "Active/in-flight" project definition**~~ — **Resolved 2026-06-23**: Owner accepted recommendation. Moved to Owner Clarifications.

- [x] ~~**F7 — `/devops-project-sync` error handling**~~ — **Resolved 2026-06-23**: Owner accepted recommendation (partial-success). Moved to Owner Clarifications.

- [x] ~~**F8 — `projects/_backlog/needs-a-project.md` disposition**~~ — **Resolved 2026-06-23**: Owner accepted recommendation (keep as draft pad). Moved to Owner Clarifications.

- [x] ~~**F9 — Project slug derivation**~~ — **Resolved 2026-06-23**: Owner accepted recommendation (derived from Issue title, `--slug` overrides). Moved to Owner Clarifications.

---

## References

- **Design**: [design.md](design.md) — full design document with 23 ratified decisions, 6 phases, alternatives considered
- **Plan**: [plan.md] — to be generated by `/project-pipeline` Step 4
- **Existing skills** to be hooked (Phase 4):
  - [`.claude/skills/design-to-spec/SKILL.md`](../../.claude/skills/design-to-spec/SKILL.md)
  - [`.claude/skills/project-pipeline/SKILL.md`](../../.claude/skills/project-pipeline/SKILL.md)
  - [`.claude/skills/task-create/SKILL.md`](../../.claude/skills/task-create/SKILL.md)
  - [`.claude/skills/task-execute/SKILL.md`](../../.claude/skills/task-execute/SKILL.md)
  - [`.claude/skills/context-handoff/SKILL.md`](../../.claude/skills/context-handoff/SKILL.md)
  - [`.claude/skills/worktree-setup/SKILL.md`](../../.claude/skills/worktree-setup/SKILL.md)
  - [`.claude/skills/worktree-sync/SKILL.md`](../../.claude/skills/worktree-sync/SKILL.md)
  - [`.claude/skills/repo-cleanup/SKILL.md`](../../.claude/skills/repo-cleanup/SKILL.md)
  - [`.claude/skills/merge-to-master/SKILL.md`](../../.claude/skills/merge-to-master/SKILL.md)
- **Documentation to extend** (Phase 6):
  - [`docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md`](../../docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md) (423 lines)
  - [`docs/procedures/AI-CODING-PROCEDURES-GUIDE.md`](../../docs/procedures/AI-CODING-PROCEDURES-GUIDE.md) (525 lines)
  - [`CLAUDE.md`](../../CLAUDE.md) §16 Pointers
- **GitHub Project #2**: https://github.com/users/spaarke-dev/projects/2
- **Exemplar spec**: [`projects/ai-procedure-quality-r1/spec.md`](../ai-procedure-quality-r1/spec.md)
- **ADR catalog**: [`.claude/adr/INDEX.md`](../../.claude/adr/INDEX.md)
- **Spaarke skill convention**: [`.claude/skills/INDEX.md`](../../.claude/skills/INDEX.md)
- **CLAUDE.md root**: [`CLAUDE.md`](../../CLAUDE.md) — see §16 Pointers for portfolio integration; §10 BFF Hygiene unaffected by this project

---

*AI-optimized specification. Original design: [design.md](design.md). Generated by `/design-to-spec` skill on 2026-06-23.*
