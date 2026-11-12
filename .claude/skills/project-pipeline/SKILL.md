---
name: project-pipeline
description: Automated pipeline from SPEC.md to ready-to-execute tasks — runs autonomously by default with parallel task execution
tags: [project-pipeline, orchestration, automation]
techStack: [all]
appliesTo: ["projects/*/", "start project", "initialize project"]
alwaysApply: false
exemplar: none-too-volatile
last-reviewed: 2026-11-03
---

# project-pipeline

> **Category**: Project-orchestration
> **Last Reviewed**: 2026-11-03
> **Exemplar**: none — pipeline runs are project-specific. The step structure below (Steps 0–5) is the contract; other skills, root `CLAUDE.md` and `.claude/constraints/agent-cost.md` cite its step numbers, so never renumber them.

## When to Use

- The user says "start project", "initialize project from spec", or "run project pipeline", or invokes `/project-pipeline {project-path}`.
- `projects/{project-name}/spec.md` exists. If only a design document exists, run `/design-to-spec` first.

## What It Produces

Spec validation → resource discovery → project artifacts (`project-setup`) → POML task files (`task-create`) → project branch and initial commit → GitHub registration → task execution in parallel waves (`task-execute`). The pipeline calls the component skills; it does not re-implement them. Where a component skill is named below, that skill is authoritative.

## Execution Mode, Model and Settings

- **Autonomous by default.** Run every step end to end; report at each milestone without waiting. The user can interrupt at any time.
- **Interactive on request.** If the user says "run interactively", "with approvals" or similar, read `references/interactive-mode.md` before Step 0. It lists the confirmation gate at each step.
- **Safety gates stop in both modes**: pre-flight (0.3), an incomplete spec (1), the Completeness Lint (3), registration (4.2), and the build check between waves (5). They are not confirmation gates. The user fixes the cause; the pipeline does not auto-fix it.
- **Model and effort**: run Steps 0–3 (planning) on opus, or fable for the hardest projects, at effort `high` (`.claude/constraints/agent-cost.md`). Task execution uses each POML's `<model-tier>`/`<effort>` (Step 5).
- **Output length**: this skill writes long artifacts. If one comes back truncated, set `CLAUDE_CODE_MAX_OUTPUT_TOKENS=64000` and restart the session. Do not set `MAX_THINKING_TOKENS`; current models ignore it.

## Constraints (MUST / MUST NOT)

- **MUST** run Step 2 resource discovery every time. It connects the spec to existing ADRs, skills, patterns and code; there is no shortcut.
- **MUST** generate artifacts through `project-setup` and tasks through `task-create`, never from memory. Hand-authored CLAUDE.md files carried stale skills and wrong paths.
- **MUST** emit the full canonical POML field set and pass the Completeness Lint before Step 4 (Step 3).
- **MUST** register the project on GitHub before any task runs (Step 4.2).
- **MUST** verify the build between waves, and stop on a failure (Step 5).
- **MUST NOT** dispatch a task that touches `.claude/` to a sub-agent: sub-agents cannot write there (root `CLAUDE.md` §3).
- **MUST NOT** retry a failed task whose cause has not been named (Step 5 retry rule).
- **MUST NOT** use `git stash` to clear a dirty tree. The stash stack is shared by every worktree; commit work in progress instead.

## Pipeline Steps

### Step 0: Plan Mode Confirmation

Plan Mode enforces "plan before implement" for the analysis steps.

- **Session with an operator present** (autonomous or interactive mode): ask the user to confirm Plan Mode is on (the ⏸ indicator; Shift+Tab cycles modes). If it is not on, STOP until it is.
- In Plan Mode, run the read-only work: Steps 0.3–1.7 and Step 2 Part 1 (discovery). Present the discovery summary and planned artifacts as the plan. Before the first file write (Step 2 Part 2), ask the user to leave Plan Mode for Accept Edits. Steps 2 Part 2 to 5 write files and git state.
- **Non-interactive session** (headless / `-p`, nobody to press Shift+Tab): state that Plan Mode cannot be confirmed. Run Steps 0–1.7 read-only and stop with a report. Steps 2–3 create files, so run them in an interactive session, or only when the invoking prompt explicitly authorizes writes.

### Step 0.3: Pre-Flight Checks

Ensure a clean baseline. Report the results; STOP on a failed critical check (both modes, no auto-fix).

```
a. Branch:     git branch --show-current
               on master while creating a project → WARN; the project belongs on work/{project-name}
b. Clean tree: git status --porcelain        dirty → STOP; ask the user to commit first (not stash)
c. Current:    git fetch origin; git rev-list --count HEAD..origin/master    > 0 → STOP; pull origin/master first
d. Build:      dotnet build src/server/api/Sprk.Bff.Api/                     fails → STOP; a broken baseline cannot start a project
e. Worktree:   git rev-parse --git-common-dir — in a worktree whose main repo master != origin/master → WARN, suggest /worktree-sync
f. Lessons:    newest projects/*/notes/lessons-learned.md → remind the user to review it (informational)
```

Report: branch, tree state, commits behind, build result (errors, warnings), main-repo sync, and the latest lessons-learned path.

### Step 0.5: Master Staleness Check

Run `merge-to-master` in **audit mode**: `git fetch origin`; for each `origin/work/*` branch, count commits not on `origin/master`.

- Branches with unmerged commits → WARN: "{N} branches have {M} unmerged commits; a new project created from master will miss this work. Recommended: `/merge-to-master` first." List each branch and its count.
- None → log "✅ Master is current — all branch work merged."
- **Autonomous**: log the warning and continue. **Interactive**: ask whether to merge first or continue.

### Step 1: Validate SPEC.md

```
LOAD projects/{project-name}/spec.md
REQUIRE sections (design-to-spec template headings): Executive Summary · Scope · Requirements · Technical Constraints · Success Criteria
                  · ADR Tensions (root CLAUDE.md §6.5 — MUST exist, even if it says "no tensions surfaced")
REQUIRE substance: under ~500 words prompts a check that each required section is real, not a placeholder.
                   A short spec for a small project is fine if every section is substantive.
IF a required section is missing or a placeholder → STOP; list what is missing; offer to help complete it
IF short but complete → proceed, and say so in the report
```

If `spec.md` does not exist, read `references/error-recovery.md` ("spec.md missing") before continuing.

Report: word count, sections present, ready for planning.

### Step 1.5: Overlap Detection (Parallel Sessions)

Detect likely file conflicts with active PRs before investing in setup (`conflict-check` covers the same ground per task).

```
gh pr list --state open --json number,title,headRefName,files
Infer likely touched areas from spec.md:
  PCF controls → src/client/pcf/ · API endpoints, record create/update paths and invariants → src/server/api/
  (BFF write path; no plugins — ADR-002) · shared libraries → src/*/shared/ · docs → docs/, .claude/
FOR each open PR: overlap = likely areas ∩ PR files → collect non-empty overlaps
git worktree list → note other worktrees' branches with active projects
```

- No overlap → continue silently.
- Overlap → WARN with the PR number, title, branch and overlapping areas, plus options: coordinate scope, designate file ownership, or merge that PR first and rebase. **Autonomous**: log and proceed (informational). **Interactive**: wait for the user.

### Step 1.7: Process ADR Tensions (per CLAUDE.md §6.5)

Surface anticipated ADR conflicts before Step 2, so resolution paths are chosen on purpose rather than emerging as silent violations at code review. design-to-spec writes the tensions; this step validates them at handoff; code-review enforces them at PR time.

```
READ spec.md "ADR Tensions"
IF "No ADR tensions surfaced" → log ✅ ADR tensions: none declared; continue
ELSE FOR EACH tension row:
  SHOW: ADR + rule challenged · conflict · declared path (A exception / B amendment / C comply) · rationale
  VALIDATE the path is concrete:
    A → rationale cites a specific project constraint (not boilerplate)
    B → the amendment scope is identified (which ADR sections change)
    C → the compliant alternative is specific
  hollow or boilerplate rationale → FLAG: "§6.5 requires concrete reasoning. Strengthen the rationale or downgrade to Path C."
  Path B → REMIND: "The ADR amendment must merge before or with this project's dependent code."
SUMMARIZE "N tensions (X A, Y B, Z C) on the record" and CONTINUE. Do not block: accepted declarations are authoritative.
```

### Step 2: Comprehensive Resource Discovery & Artifact Generation

**Pre-step: hot-path overlap.** Read `projects/INDEX.md` (the active-project registry). From spec.md / design.md, detect which hot-path surfaces the project touches: BFF, SpaarkeAi, ci-workflows, skill-directives, root CLAUDE.md. For each one, find other active projects that declare the same surface.
- Overlap → **HARD WARNING** in the Step 2 output, naming the projects and a coordination recommendation. Do not block; the user decides the sequencing. (Purpose: no silent collisions on shared files when many worktrees touch one surface; in one count, 13 of 17 worktrees touched the BFF.)
- None → log "✅ Hot-path surfaces unique among active worktrees". The project's own row is appended in Step 4.

**Part 1: Resource discovery.** This is the comprehensive pass: design-to-spec Step 3 loaded ADR constraints and ran one reuse scan (`notes/reuse-scan.md`) to enrich the spec, while this step loads everything implementation needs.

```
1. IDENTIFY resource types from spec.md: technologies, operations, components (API, PCF, storage, AI, jobs)
2. LOAD applicable ADRs via adr-aware (concise `.claude/adr/` versions; full `docs/adr/` only for rationale) (e.g. PCF control → ADR-006, ADR-011, ADR-012, ADR-021)
3. SEARCH .claude/skills/INDEX.md and skill frontmatter (tags, techStack) for applicable skills
4. SEARCH docs/guides/ and .claude/patterns/ for procedures and code-pattern pointers
5. FIND canonical implementations in the codebase to copy. For each new surface the spec introduces
   (service, endpoint, DI registration, package, Dataverse column, file surface), record the existing
   component it overlaps with: the root CLAUDE.md §11 "Existing" answer that task-create Step 3.5.6
   requires per task. Flag project-level new surface the spec/design leaves unjustified.
6. DISCOVER scripts via script-aware (scripts/README.md): e.g. PCF build → Invoke-PcfBuildProd.ps1
   (deploy via pcf-deploy), API testing → Test-SdapBffApi.ps1, custom pages → Deploy-CustomPage.ps1,
   ribbon → Export-EntityRibbon.ps1
7. IF spec.md names Dataverse entities (sprk_* tables, lookups, option sets): with the Dataverse MCP tools
   (mcp__dataverse__*, read-only), list tables and describe each referenced entity; flag gaps
   ("sprk_X lacks field Y"), conflicts ("field Y has a different type") and missing entities
   ("sprk_Z does not exist → schema creation task needed"). Otherwise skip.
```

If you fan discovery out to sub-agents, read `references/discovery-fanout.md` before launching them. It has the return schema and the model choice.

Output: counts and lists of ADRs, skills, knowledge docs, canonical implementations, scripts, schema findings, §11 flags, and the hot-path result.

**Part 2: Generate artifacts**

```
INVOKE project-setup projects/{project-name} with spec.md + the discovery summary
  → README.md, plan.md, CLAUDE.md, current-task.md, tasks/ and notes/ folders (project-setup is authoritative)
THEN ENHANCE:
  plan.md   — fill "Discovered Resources" (ADRs with summaries, skills with purpose, knowledge docs, reusable
              code) and the References section
  CLAUDE.md — "Applicable ADRs" (§7 Key documents) and §2 "ADR tensions approved" from Step 1.7; resource paths
```

If `project-setup` fails or plan.md comes out without a usable Phase Breakdown, read `references/error-recovery.md` ("Artifact generation failed") before continuing.

Report: discovered resources, then the files created.

### Step 3: Generate Task Files

Run `task-create` for `projects/{project-name}`: invoke the skill, or follow its Workflow (Steps 1–6) and Completeness Lint in this session. **task-create is authoritative** for decomposition and numbering (2–4 hour tasks; 001…, 010…, 020…), the POML structure (its Step 4; `.claude/templates/task-execution.template.md` is only a skeleton pointer), tag-to-knowledge mapping (3.4), ADR mapping (3.5), rigor (3.5.5), model tier and effort (3.5.5b), step mode and escalation (3.5.5c), §11 justification (3.5.6), deployment tasks (3.6), UI tests (3.65), the wrap-up task (3.7), parallel groups (3.8), `/goal` eligibility (3.85), and TASK-INDEX.md (its Step 5).

The pipeline holds task-create to these on top, because Step 5 consumes them:

```
EVERY POML carries the FULL canonical field set — not the minimal skeleton. Root <task id="…" project="…">:
  <metadata>  title, phase, gate, status, tags,
              <rigor> + <rigor-reason>                      (3.5.5)
              <model-tier> + <model-tier-reason>, <effort>  (3.5.5b — REQUIRED; Step 5 dispatches on these)
              <parallel-group> + <parallel-safe>            (3.8 — every task, no exceptions)
  <prompt>/<role>/<goal> explicit and literal (Sonnet executes literally; it does not infer intent)
  <context><relevant-files> exact files + the canonical reference implementation to copy
  <constraints> with an ADR source attribute AND a scope clause on each
  <knowledge><files> non-empty; PCF tasks include docs/guides/PCF-DEPLOYMENT-GUIDE.md + src/client/pcf/CLAUDE.md
  <steps mode="directional|prescriptive">                   (3.5.5c)
  <escalation><trigger>  for tasks with a judgment boundary (3.5.5c)
  <justification>        for tasks adding new surface      (3.5.6 / root CLAUDE.md §11)
  <ui-tests>             for pcf / frontend / fluent-ui / e2e-test tags (3.65; run by task-execute Step 9.7)
  <outputs> exact paths + type;  <acceptance-criteria> a CLOSED set incl. negative/authorization cases
  A POML missing <model-tier>/<effort> silently falls back to defaults at dispatch and under-specifies the wave.

PLACEMENT JUSTIFICATION (root CLAUDE.md §10/§11): a task adding to src/server/api/Sprk.Bff.Api/ (or to
  Spaarke.Core / Spaarke.Dataverse consumed by the BFF) MUST require, in <constraints>/<notes>, that the
  implementer states the Placement Justification in the PR (cite .claude/constraints/bff-extensions.md)
  and verifies publish size ≤ 60 MB.

HOT-PATH DECLARATION: IF spec.md/design.md shows a BFF (src/server/api/Sprk.Bff.Api/**) or SpaarkeAi
  (src/solutions/SpaarkeAi/**) touch, design.md MUST contain a <hot-path-declaration> block (BFF, SpaarkeAi,
  ci-workflows, skill-directives, root-CLAUDE.md: Y/N each). Absent → HARD WARNING and flag the project for a
  design-doc update. Informational: task generation continues, and the warning is repeated in the pipeline
  summary so the operator sees it before Phase 1.

ALSO REQUIRED: deployment tasks after each phase with deployable output (tag deploy) · the mandatory wrap-up task ·
  TASK-INDEX.md with dependencies, critical path, high-risk items, Parallel Execution Groups and each wave's
  goal-eligible flag + goal-condition.

COMPLETENESS LINT (gate): pwsh scripts/Validate-TaskPoml.ps1 projects/{project-name}/tasks
  Any POML missing a required field → FAIL the step. Do not proceed to Step 4 with under-specified tasks.

BOARD FIELD "Tasks Planned": set once, when the tasks are first created, to the number of task files. Never on
  later runs or syncs; never overwrite a non-empty value (Task Count tracks the current number).
  The board item usually does not exist yet (Step 4.2 creates it). Then skip it here; after registration and
  the commit of TASK-INDEX.md, run: python scripts/ai-cost/backfill-tasks-planned.py {project-name} --apply
```

If task generation fails or produces no task files, read `references/error-recovery.md` ("Task decomposition failed") before continuing.

Report: task count, parallel groups, deployment tasks, wrap-up task, lint result, any hot-path warning.

### Step 4: Create Feature Branch

```
BRANCH: the project branch is work/{project-name} (the worktree-setup / devops-project-start convention; hooks
        infer the project from it). Already on it (normal in a project worktree) → use it.
        Otherwise → git checkout -b work/{project-name}
COMMIT: git add projects/{project-name}/
        Append the project's row to projects/INDEX.md (name, branch, worktree path, hot-path declaration,
        last-touched date) and stage it.
        git commit — conventional format: "feat({scope}): initialize {project-name} project", with a body listing
        the artifacts and the task count, ending with the attribution lines the session requires
PUSH:   git push -u origin work/{project-name}
OPTIONAL draft PR for visibility: gh pr create --draft
```

If you open the draft PR, take its title and body from `references/draft-pr-template.md`. If the commit or push is rejected, read `references/error-recovery.md` ("Commit rejected") before retrying.

Report: branch, commit, push, draft PR link if any. Then go to Step 4.2.

### Step 4.2: Register the project on GitHub (MUST)

Unregistered projects are invisible on Board #2, where findings are triaged and projects find each other. On 2026-10-15, 16 project folders had no Project Issue (owner direction, 2026-10-15). No prompt; autonomous and interactive alike.

```
IF README.md already has the `> **Portfolio**:` pointer block:
  → invoke /devops-project-sync (silent; a sync failure warns and does not block) and go to VERIFY
ELSE:
  EPIC: required (devops-project-register D-12). Take it from design.md/spec.md if named; otherwise ask the
        operator this one question (in autonomous mode too: it cannot be guessed)
  → /devops-project-register --epic <#E>
  → python scripts/ai-cost/backfill-tasks-planned.py {project-name} --apply   (after TASK-INDEX.md is committed)
ALWAYS (idempotent):
  gh label create {project-name} --force --description "Issues filed by or for projects/{project-name}"
    (the label /project-defer-issue-tracking and finding triage use to find this project's issues)
  Commit and push the README pointer block if it changed.
VERIFY (all must hold):
  ✓ README.md has the `> **Portfolio**:` pointer block, committed and pushed
  ✓ gh issue view <#N> --json projectItems → lists project "Spaarke Core" (Board #2)
  ✓ the Project Issue's parent is the Epic
  ✓ board fields set: Status, Type = Project, Project Type, Worktree Path, Project Folder, Task Count, Tasks Planned
  ✓ gh label list --search {project-name} → the label exists
ANY check fails (including gh unavailable) → STOP. Do NOT start Step 5. Report ❌ with the failed check and the
  command that fixes it. A project that is not registered is not ready to execute.
```

Report: "✅ Registered on GitHub: Project Issue #{N} under Epic #{E} · Board #2 · label {project-name}"

### Step 4.5: Projected Target Date

- **Autonomous**: skip. Target Date stays blank until the operator sets it.
- **Interactive**: read `references/interactive-mode.md` ("Step 4.5") and run the Target Date prompt. It is placed here because only now does the plan exist (task count, phases, effort range, critical path), so the date is an informed projection and drift (Closed − Target) means something.
- The project has no portfolio pointer block → this cannot happen after Step 4.2; return to Step 4.2.

### Step 5: Execute Tasks (Auto-Start with Parallel Execution)

Start task execution after Step 4.2 passes. Follow the parallel groups in TASK-INDEX.md until all tasks are complete, an escalation is raised, or a compaction notice appears (then checkpoint, compact and resume from TASK-INDEX.md). Interactive mode runs one task at a time (`references/interactive-mode.md`).

```
FOR each wave (a parallel group whose dependencies are all satisfied):
  → ONE message with MULTIPLE Agent tool calls, one sub-agent per task; each runs task-execute for its task
  → wait for all; update TASK-INDEX.md (🔲 [open] → ✅ [done]); move to the next satisfied group
Serial tasks (no group, or <parallel-safe>false</parallel-safe>) → task-execute from the main session
```

**Dispatch rules**
- A wave's tasks have their dependencies satisfied and do not modify the same files (check `<relevant-files>` and `<parallel-safe>`).
- **Model and effort**: launch each task's sub-agent with `model = <model-tier>` from its POML (default `sonnet`). Every launch names a model; a `PreToolUse` hook refuses one that does not. Effort comes from the agent definition (e.g. `implementer`) or a workflow `agent()` option, because the Agent tool call takes `model` only. A POML `<effort>` that differs from the definition's runs at the definition's or session's effort until a workflow or matching definition carries it (`.claude/constraints/agent-cost.md`, "How effort is set"). Planning Steps 0–3 stay on the operator's session model.
- **Concurrency**: at most **6 agents per wave**. This is an API-overload guard; tune it only with evidence. The harness also caps concurrent sub-agents per session (`CLAUDE_CODE_MAX_CONCURRENT_SUBAGENTS` in `.claude/settings.json`).
- **`.claude/` boundary**: tasks touching `.claude/` are sequential, main-session only. task-create marks them `parallel-safe: false`. A sub-agent dispatched to one fails with "Edit denied". That is expected: the main session picks the task up (root CLAUDE.md §3).
- **`/goal` wave loop**: for a wave TASK-INDEX.md marks goal-eligible (task-create 3.85), the operator may run it under `/goal "<goal-condition>"`. Each task still runs task-execute with its own Step 9.5 gates. The evaluator only checks a stopping condition and is **not a quality gate**: never mark tasks complete because the goal was met. Non-eligible waves dispatch normally. Detail: `.claude/skills/task-execute/references/goal-wave-loop.md`.
- Track parallel tasks in current-task.md ("Parallel Execution" section).

**Build verification between waves (MANDATORY).** After each wave, the main session verifies the build:
- `.cs` changed → `dotnet build src/server/api/Sprk.Bff.Api/`
- `.ts`/`.tsx` changed → PCF: `npm run build:prod` through `scripts/Invoke-PcfBuildProd.ps1`, not `npm run build` (root CLAUDE.md §12, FAILURE-MODES AP-1). `pcf-scripts` exits 0 on a failed build, so judge it from the output. Non-PCF packages without a `:prod` script: `npm run build`.
- Build fails → STOP. Do not dispatch the next wave. Report the breakage with the wave id. This catches incoherent changes across parallel agents before they compound.

**Failure isolation and retry**
- One agent failing does not abort the wave. Collect every outcome (success / failure / timeout) and report "Wave X: {N} succeeded, {M} failed".
- Each task sub-agent ends with a machine-readable result:
  `{ "taskId": "0NN", "status": "success|failed|blocked", "filesChanged": ["…"], "gatesRun": { "codeReview": true, "adrCheck": true }, "escalation": null|"reason", "note": "…" }`
- `blocked` with an `escalation` reason (a fired `<escalation>` trigger → BLOCKED.md) is a legitimate outcome, not a failure. Do not retry it; surface it for the human (root CLAUDE.md §6/§6.5).
- Mark failed tasks 🔄 (needs retry), not ❌. The main session decides whether to retry sequentially or report and stop.
- **Retry rule**: retry only after the failure's cause is named and addressed (a fix, missing context supplied, or a switch to sequential execution). A failure whose cause cannot be named is escalated (a report or BLOCKED.md), not retried. Retrying an unexplained failure is a loop, not progress.

**Context management**: checkpoint after each completed group, not each task. If the user or harness reports high context, or a compaction notice appears, checkpoint, compact, then continue with the next group. Claude cannot measure its own usage (root CLAUDE.md §5). If the user asks to stop, checkpoint, stop, and report the remaining tasks.

## Success Criteria

- [ ] Pre-flight passed; staleness and overlap checks reported (Steps 0.3, 0.5, 1.5)
- [ ] SPEC.md validated, including ADR Tensions; tensions processed (Steps 1, 1.7)
- [ ] Resources discovered: ADRs, skills, knowledge docs, canonical code, scripts, schema findings, hot-path check (Step 2)
- [ ] README.md, plan.md (with Discovered Resources), CLAUDE.md, current-task.md and folders created by project-setup (Step 2)
- [ ] All task POMLs carry the full field set and pass the Completeness Lint; TASK-INDEX.md created with parallel groups (Step 3)
- [ ] Deployment tasks (if applicable), `<ui-tests>` on PCF/frontend tasks, and the wrap-up task added (Step 3)
- [ ] Project branch committed and pushed; projects/INDEX.md row added (Step 4)
- [ ] **Registered on GitHub: Project Issue on Board #2 under its Epic, fields set, project label, README pointer committed (Step 4.2, MUST)**
- [ ] Task execution started, or the user declined (Step 5)

## Gotchas

- **Skipping Step 2 discovery** gives artifacts with no ADR awareness. It shows up as Task 001 failing at its own Step 0. Re-run discovery at the planning tier (opus/fable, effort `high`); effort cannot make up for rushed discovery upstream.
- **Hand-written CLAUDE.md**: authoring it from memory carries stale skills and paths. `project-setup` uses `.claude/skills/project-setup/references/claudemd-template.md`.
- **Under-specified POMLs fail silently**: a missing `<model-tier>`/`<effort>` raises no error; dispatch falls back to defaults. The Completeness Lint is the only guard (Step 3).
- **`feature/` branches**: older versions of this skill created `feature/{project-name}`. The repo convention and the hooks use `work/{project-name}` (Step 4).
- **The Agent tool takes no per-call effort.** Do not claim a POML's `<effort>` was applied unless a definition or workflow carried it (Step 5).

## References

- Interactive runs (per-step gates, Step 4.5 prompt, one-task-at-a-time): `references/interactive-mode.md`
- Error messages and recovery: `references/error-recovery.md`
- Discovery sub-agent schema and model choice: `references/discovery-fanout.md`
- Draft PR body: `references/draft-pr-template.md`
- How this skill calls the component skills; current-task.md lifecycle; portfolio-hook history: `references/skill-integration.md`
- Worked runs and sample step reports: `examples/usage-examples.md`, `examples/step-reports.md`
- Component skills: `design-to-spec`, `merge-to-master`, `adr-aware`, `conflict-check`, `script-aware`, `project-setup`, `task-create`, `devops-project-register`, `devops-project-sync`, `task-execute`, `ui-test`
