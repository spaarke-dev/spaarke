---
description: Decompose a project plan into numbered POML task files for systematic AI-assisted execution
tags: [tasks, planning, project-structure, poml]
techStack: [all]
appliesTo: ["projects/*/plan.md", "create tasks", "decompose plan"]
alwaysApply: false
exemplar: projects/ai-procedure-quality-r1/tasks/
last-reviewed: 2026-11-03
length-justification: Every run sets eight per-task POML fields from their own decision rules and writes the authoritative POML skeleton, and all of it applies to every task in every project.
---

# task-create

> **Read this file in full.** It is long on purpose: every run applies all of Steps 3.4–3.85 to every task and writes the Step 4 skeleton, and this file is the authoritative definition of the task POML that the template, the validator and other skills point back to. Only the `<ui-tests>` structure, the `/goal` condition template and the component-justification detail are conditional, and they live in `references/`.

> **Category**: Project-orchestration
> **Last Reviewed**: 2026-11-03
> **Exemplar**: `projects/ai-procedure-quality-r1/tasks/` — a 32-task, 7-phase project this skill produced.

## Contents

| Section | What it covers | When it applies |
|---|---|---|
| Purpose · When to Use | Role of the skill; the step-number contract; inputs | Every run |
| Steps 1–3 | Load context, validate plan.md, decompose + number + size tasks | Every run |
| Step 3.4 | Standard Tag Vocabulary; Tag-to-Knowledge Mapping → `<knowledge>` | Every task |
| Step 3.5 | ADR mapping → `<constraints>` + concise ADR files | Every task |
| Step 3.5.5 | Rigor decision tree → `<rigor>` | Every task |
| Step 3.5.5b | Model tier, effort rubric, authoring for literal execution | Every task |
| Step 3.5.5c | Step mode; `<escalation>` triggers | Every task |
| Step 3.5.6 | Component Justification Gate → `<justification>` | New-surface tasks (detail: `references/component-justification.md`) |
| Step 3.6 | Deployment tasks per deliverable type | Every project with deployables |
| Step 3.65 | Visual direction + `<ui-tests>` | `pcf` / `frontend` / `fluent-ui` / `e2e-test` tasks (structure: `references/ui-tests.md`) |
| Step 3.8 | Parallel groups, auto-demotion rules, wave plan | Every task |
| Step 3.85 | `/goal` eligibility per wave | Every wave (condition: `references/goal-condition.md`) |
| Step 3.7 | Mandatory wrap-up task | Every project |
| Step 4 | Authoritative POML skeleton + POML Tag Requirements | Every task |
| Step 5 | TASK-INDEX.md, status token table, drift check, parallel groups section | Every run |
| Step 6 | Output summary | Every run |
| Validation Checklist · Completeness Lint | Gates before declaring done | Every run |
| Portfolio Hook | `/devops-project-sync` Task Count | Every run, last |
| Gotchas · References | Failure modes; related files | As needed |

## Purpose

Turns the `plan.md` work breakdown structure (WBS) into executable task files in `tasks/`. Each task is a **valid POML/XML document** (`.poml`) written for a literal AI executor. This skill sits between planning (`project-setup` / `project-pipeline` Step 2) and implementation (`task-execute`).

**This skill is the authoritative definition of the task POML field set.** `.claude/templates/task-execution.template.md` mirrors Step 4 for copy-paste; `scripts/Validate-TaskPoml.ps1` checks it.

**Step numbers are an external contract.** POMLs, root `CLAUDE.md` (§8.5 → Step 3.5.5b, §11 → Step 3.5.6), the template, `project-pipeline`, `code-review` and `task-execute` cite Steps 3.4, 3.5, 3.5.5, 3.5.5b, 3.5.5c, 3.5.6, 3.6, 3.65, 3.7, 3.8, 3.85, 4 and 5 by number. Never renumber them; grep the repo (`task-create.*Step 3`) before changing one.

## When to Use

- `plan.md` has WBS phases and `tasks/` is empty — usually `project-pipeline` Step 3.
- The user says "create tasks", "decompose plan", "generate task files", or runs `/task-create {project-name}`.

| Input | Required | Source |
|-------|----------|--------|
| Project path | Yes | `projects/{project-name}/` |
| plan.md | Yes | Auto-loaded from `projects/{project-name}/plan.md` |
| Task granularity | No | Default `medium` (Step 3 sizing table) |

## Workflow

Steps 3.4–3.85 decide each task's fields; Step 4 writes them. The step order below is deliberate (3.8 and 3.85 come before 3.7). Do not reorder.

### Step 1: Load Project Context

```
LOAD: projects/{project-name}/plan.md
LOAD: projects/{project-name}/README.md                  # context
LOAD: .claude/templates/task-execution.template.md       # POML skeleton (pointer); this skill is authoritative
EXTRACT from plan.md: WBS phases (§5), dependencies (§6 if present), risks/constraints (§7), acceptance criteria (§8)
```

### Step 2: Validate Plan Readiness

```
CHECK plan.md has: ≥1 WBS phase · deliverables or outcomes per phase · no "TBD" in critical sections
IF validation fails: list the missing elements → offer to help complete plan.md → STOP until it is ready
```

### Step 3: Decompose Phases into Tasks

```
FOR each WBS phase:
  IDENTIFY discrete work items — each completable in one session, with a verifiable output and explicit dependencies
  NUMBER: Phase 1 → 001, 002…; Phase 2 → 010, 011…; Phase 3 → 020, 021…   (the 10-gap leaves room to insert tasks)
  NAME:   tasks/{NNN}-{task-slug}.poml — 3-digit zero-padded id, 3–5-word kebab-case slug (e.g. 012-setup-redis-connection)
```

| Granularity | Hours/task | Tasks/phase |
|-------------|------------|-------------|
| fine | 1–2 | 5–10 |
| **medium (default)** | 2–4 | 3–7 |
| coarse | 4–8 | 2–4 |

A task is one reviewable change: target ≤ 15 KB of POML (see "Task size" in Step 3.5.5b).

### Step 3.4: Discover Related Resources (REQUIRED)

**Every task MUST have a non-empty `<knowledge>` section, driven by its tags.**

**1. Choose tags** from the Standard Tag Vocabulary. Tags drive context loading (`task-execute` Step 4 loads constraints and patterns by tag), so a precise set saves tokens and an off-vocabulary tag loads nothing.

| Category | Tags |
|----------|------|
| API/Backend | `bff-api`, `api`, `backend`, `minimal-api`, `endpoints` |
| Frontend/PCF | `pcf`, `react`, `typescript`, `frontend`, `fluent-ui` |
| Dataverse | `dataverse`, `solution`, `fields`, `plugin`, `ribbon` |
| Azure | `azure`, `app-service`, `azure-ai`, `azure-search`, `bicep` |
| AI/ML | `azure-openai`, `ai`, `embeddings`, `document-intelligence` |
| Operations | `deploy`, `ci-cd`, `devops`, `infrastructure` |
| Quality | `testing`, `unit-test`, `integration-test`, `e2e-test` |
| Refactoring | `refactoring`, `rename`, `restructure`, `migration` |
| Configuration | `config`, `options`, `di`, `settings` |

Usage: `<tags>bff-api, refactoring, services</tags>` · `<tags>pcf, react, frontend, fluent-ui</tags>` · `<tags>deploy, dataverse, pcf</tags>`.

**2. Apply the Tag-to-Knowledge Mapping (MANDATORY).** When a task has these tags, ALWAYS include these files:

| Tag | Constraints | Patterns | Additional Files |
|-----|-------------|----------|------------------|
| `pcf`, `react`, `fluent-ui`, `frontend`, `e2e-test` | `.claude/constraints/pcf.md` | `.claude/patterns/pcf/control-initialization.md`, `.claude/patterns/pcf/theme-management.md` | `src/client/pcf/CLAUDE.md`, `docs/guides/PCF-DEPLOYMENT-GUIDE.md`, `.claude/skills/ui-test/SKILL.md` |
| `bff-api`, `api`, `minimal-api`, `endpoints` | `.claude/constraints/api.md` | `.claude/patterns/api/endpoint-definition.md`, `.claude/patterns/api/endpoint-filters.md` | `src/server/api/Sprk.Bff.Api/CLAUDE.md` |
| `dataverse`, `solution`, `fields`, `plugin` | `.claude/constraints/plugins.md` | `.claude/patterns/dataverse/plugin-structure.md` | `.claude/skills/dataverse-deploy/SKILL.md` |
| `auth`, `oauth`, `authorization` | `.claude/constraints/auth.md` | `.claude/patterns/auth/spaarke-sso-binding.md` (canonical v2), `.claude/patterns/auth/obo-flow.md`, `.claude/patterns/auth/oauth-scopes.md` | `.claude/adr/ADR-028-spaarke-auth-architecture.md`, `docs/guides/auth-deployment-setup.md` |
| `cache`, `redis`, `caching` | `.claude/constraints/data.md` | `.claude/patterns/caching/distributed-cache.md` | — |
| `ai`, `azure-openai`, `document-intelligence` | `.claude/constraints/ai.md` | `.claude/patterns/ai/streaming-endpoints.md` | — |
| `deploy` | — | — | `.claude/skills/dataverse-deploy/SKILL.md`, `docs/guides/PCF-DEPLOYMENT-GUIDE.md` |
| `testing`, `unit-test`, `integration-test` | `.claude/constraints/testing.md` | `.claude/patterns/testing/unit-test-structure.md`, `.claude/patterns/testing/mocking-patterns.md` | — |
| `worker`, `job`, `background` | `.claude/constraints/jobs.md` | `.claude/patterns/api/background-workers.md` (queue), `.claude/patterns/api/scheduled-jobs.md` (schedule) | `.claude/adr/ADR-052-workload-placement.md` (where it runs) |

**PCF tasks MUST reference `docs/guides/PCF-DEPLOYMENT-GUIDE.md`.** It holds the mandatory version bump: every location in the `pcf-deploy` skill's version table, plus the footer version. Skipping it ships stale deployments.

```
FOR each task:
  FOR each tag in <metadata><tags>: ADD the mapped constraint, pattern and additional files to <knowledge><files>
  SCAN projects/{name}/notes/lessons-learned.md and .claude/FAILURE-MODES.md; reference any lesson relevant to this task
  IF no tag matched: ADD at minimum the CLAUDE.md of the module being modified
  ENSURE <knowledge> is NOT empty
```

### Step 3.5: Map ADRs to Tasks (REQUIRED)

```
FOR each task, by the resource types it creates or modifies:
  API Endpoint → ADR-001, ADR-008, ADR-010        Authorization → ADR-003, ADR-008, ADR-028
  Caching → ADR-009                                Dataverse Plugin → ADR-002
  Graph/SPE Integration → ADR-007                  PCF Control → ADR-006, ADR-011, ADR-012, ADR-021
  DI Registration → ADR-010                        AI Features → ADR-013, ADR-014, ADR-015, ADR-016
  Background Worker → ADR-052 (where it runs), ADR-004 (queue) / ADR-036 (schedule)
  Testing → ADR-038 (no concise copy — use docs/adr/ADR-038-testing-strategy.md)

  ADD <constraint source="ADR-XXX"> for each applicable ADR
  ADD to <knowledge><files> the CONCISE ADR: .claude/adr/ADR-XXX-*.md (100–150 lines, AI-optimised)
      — NOT docs/adr/ADR-XXX-*.md (full history; load only if needed)
FULL mapping table: the adr-aware skill.
```

### Step 3.5.5: Determine Task Rigor Level (REQUIRED)

Use the same decision tree as `task-execute` Step 0.5 (authoritative there):

```
FULL     IF ANY: tags include bff-api, api, pcf, plugin, auth · modifies code files (.cs, .ts, .tsx — check <relevant-files>)
               · 6+ steps · description says "implement", "refactor", "create service" · depends on 3+ tasks
STANDARD IF ANY: tags include testing, integration-test · creates new files (check <outputs>)
               · has explicit <constraints> or ADRs · phase 2.x or later (integration/deployment)
MINIMAL  OTHERWISE: documentation, inventory/checklist creation, simple configuration updates

ADD to <metadata>:  <rigor>{FULL | STANDARD | MINIMAL}</rigor>
                    <rigor-reason>{the specific trigger, e.g. "Task tags include 'bff-api' (code implementation)"}</rigor-reason>
Canonical field is <rigor>. <rigor-hint> is a deprecated alias — do NOT emit it.
```

The value is a documented hint, not an inference: `task-execute` may override it from the task's real characteristics, the user may edit it before execution, and the reason is the audit trail.

### Step 3.5.5b: Assign Execution Model Tier + Effort (REQUIRED)

**Model strategy (binding):** planning (`design-to-spec`, `project-pipeline` Steps 0–3) runs on the top tier (Opus / Fable); task **execution defaults to Sonnet 5**. Flag only the minority of tasks that genuinely need top-tier reasoning back up via `<model-tier>`, and set an explicit `<effort>` on every task.

```
MODEL TIER = opus (or fable) IF ANY:
  - cross-cutting refactor / migration touching 3+ existing files or consumers (high blast radius)
  - architectural change, new pattern/abstraction authoring, ADR-migration / ADR-compliance work
  - security/auth-sensitive code (tags include auth, security)
  - ambiguous or underspecified goal needing significant judgment, or novel algorithm design
  - FULL rigor AND (depends on 3+ tasks OR flagged high-risk in the plan's Risk Register)
  → fable ONLY for the very hardest (whole-subsystem migration, novel architecture); opus for the rest of this bucket
MODEL TIER = sonnet OTHERWISE (default): single-component code with an explicit canonical reference;
  test authoring, wiring, configuration, deployment steps; documentation / inventory

ADD to <metadata>:  <model-tier>{sonnet | opus | fable}</model-tier>
                    <model-tier-reason>{trigger, e.g. "Cross-cutting migration of 4 wizard families onto a shared module"}</model-tier-reason>
More reason examples (rigor and tier): examples/examples.md Example 6.
```

`project-pipeline` Step 5 dispatches each task's agent with model = `<model-tier>`; `task-execute` declares the tier and escalates if the session model is lower. This keeps the expensive tier on the tasks that need it.

**Effort rubric (REQUIRED — `<effort>` on every task).** Sonnet 5 respects effort strictly. Route on how hard the *reasoning* is, given a complete spec. Blanket `xhigh` is not free — at `xhigh` Sonnet cost approaches Opus.

```
EFFORT = xhigh  (sonnet tasks) for brownfield debugging / root-cause / race-condition work, or a complex
                multi-file change with a COMPLETE spec (hard depth, low ambiguity)
EFFORT = high   OTHERWISE (default): routine, well-patterned work with a clear reference
EFFORT = medium/low only for short, scoped, latency-sensitive mechanical steps
opus/fable tasks: high, unless a genuine whole-subsystem reasoning problem (then xhigh). Do NOT stack fable + xhigh reflexively.
Cost guardrail: a sonnet task that needs sustained xhigh probably belongs on <model-tier>opus</model-tier>.
ADD to <metadata>:  <effort>{low | medium | high | xhigh}</effort>
```

**Authoring for literal execution (EVERY task, all tiers).** Sonnet 5 follows instructions literally and does not infer unstated intent; under-specified tasks degrade it more than Opus. Author each POML so a literal executor cannot go wrong:

- **Explicit scope everywhere.** Name the **exact files** to touch (not "the follow-on components"); **point at the canonical reference implementation to copy** (e.g. `workAssignmentService.ts`) instead of describing it; state the **exact contract** (e.g. the ADR-024 resolver's 5 fields + mutual exclusion). A `<constraint>` without a scope clause is a defect — write "Every endpoint added or modified in this task applies `DocumentAuthFilter`", not "use endpoint filters for auth".
- **Acceptance criteria are a CLOSED SET.** The executor treats every `<criterion>` as mandatory and anything unlisted as out of scope. Make them exhaustive, not illustrative — include the negative and authorization cases (the 401, the empty input, the unauthorized user), not only the happy path.
- **"Above and beyond" must be requested.** If a task should include opportunistic improvements (e.g. "also fix adjacent lint violations in touched files"), say so. Do NOT add anti-laziness scaffolding ("be exhaustive", "double-check everything"); Sonnet 5 over-triggers on it and burns effort on ritual verification.
- **Test-scope clause.** For any task that adds or modifies tests, state in `<acceptance-criteria>` which behaviours and edge cases are in scope for test coverage, as intent: *"Cover the stated contract plus the edge cases named above. Anything beyond that needs a one-line justification in task notes."* Without it, "write tests" is an open instruction inside a closed contract, and the result is padding (near-identical happy-path variants that each pass ADR-038 §7 yet verify nothing new). It also gives `/test-diet` something to check a MAINTAIN classification against. **Do NOT state a numeric test count** — a number invites satisfying the number.
- **Seeding proofs only where a silent test would be dangerous.** A seeding proof breaks a guard, confirms the named test fails, then restores the code. **Request one** for a guard whose failure would be a security, tenant-isolation, fail-open or data-loss outcome, and for an architecture/guard test whose only purpose is catching a regression — one seed per guard, not per branch. **Don't** request one for minor branches, UI behaviour or happy paths (known-limit class K3, `task-execute` Step 9.5). Why: by 2026-10, 62 of `unified-access-control-r2`'s 167 POMLs asked for seeds, each a break-run-restore-rerun cycle on a suite 6.5× larger than in March; applied to every branch, the cost outran the protection.
- **Task size.** A task is one reviewable change; target ≤ 15 KB of POML. Above ~25 KB, split it or move background prose into a design/notes file referenced by path — `<context>` points at files, it does not restate them. **Don't write fix-round history into the POML**: round results go in the task's notes file; new scope becomes a new task. Why: POMLs grew from 2–5 KB to 10–28 KB on average (up to 156 KB), mostly restated `<context>` and `<constraints>`, all reloaded at Step 1 of every execution and fix round.
- **Step mode and escalation triggers** (Step 3.5.5c) are the other two literal-execution levers — set them deliberately per task.
- **Frontend tasks need concrete visual direction** (Step 3.65). "Clean and modern" is not a spec.
- **Knowledge curation.** The 1M context window is headroom for genuinely cross-cutting tasks, not licence to load the full ADR corpus. Load what the task needs via Step 3.4; reference the rest by path (the Sonnet-5 tokenizer yields ~30% more tokens for the same text).

### Step 3.5.5c: Choose Step Mode + Escalation Triggers (REQUIRED)

A literal executor treats ordered `<steps>` as a binding contract — it will do the six written steps even if step 3 is wrong for the real codebase. Choose a mode per task:

```
<steps mode="directional">   DEFAULT for standard implementation. Binding contract = <goal> + <acceptance-criteria> + <constraints>;
                             the executor MAY adapt the sequence to the real codebase.
<steps mode="prescriptive">  Migrations, deployment-touching work, anything under .claude/constraints/azure-deployment.md,
                             any irreversible or ordered procedure. The sequence binds; deviations require escalation.
```

**Escalation triggers.** A literal model will not infer *when* to escalate (root `CLAUDE.md` §6 / §6.5), and Sonnet 5 tends to push to *a* completion rather than stop at a judgment boundary. Every task with a known failure mode or judgment boundary gets a concrete trip-wire:

```xml
<escalation>
  <trigger>If the Graph API contract differs from the spec, STOP and escalate (root CLAUDE.md §6) rather than adapting the implementation.</trigger>
</escalation>
```

Inside a `/goal` wave loop (Step 3.85), firing a trigger is the correct exit: the worker writes `BLOCKED.md` and the loop ends on the escalation branch instead of improvising.

### Step 3.5.6: Component Justification Gate (REQUIRED per CLAUDE.md §11)

Every task that adds NEW surface — a new `.cs`/`.ts`/`.tsx` file, endpoint route/handler, DI registration, package reference, Dataverse column/entity/alternate key, or skill/agent/pattern/constraint file — carries a `<justification>`. The rule applies to new surface even inside an existing file. **Not required** for tasks that only modify existing surface (edit, refactor, bug fix, tests for existing surface, rename, format). Mark new files `role="new"` in `<relevant-files>` — `Validate-TaskPoml.ps1` keys on it.

```xml
<justification>
  <existing>Closest existing neighbor. Consult `.claude/patterns/CAPABILITY-INDEX.md` first, then Grep. Cite "CAPABILITY-INDEX row N" plus file:line, or "CAPABILITY-INDEX: no row" plus the Grep command run</existing>
  <extension>Yes/No + reason in ≤2 sentences. "Cleaner separation" is NOT a reason.</extension>
  <cost-of-doing-nothing>Concrete behavior or contract that fails without this. NOT "scalability" / "abstraction layer" / "future flexibility".</cost-of-doing-nothing>
</justification>
```

```
IF <extension> is "Yes — can extend existing":  REWRITE the task to "Extend <existing> with …"
IF <cost-of-doing-nothing> names no concrete failing behavior or contract:  DEMOTE ("deferred — needs concrete failure mode") OR DROP
IF all three are concrete AND extension is genuinely impossible:  PROCEED
Hollow answers ("separation of concerns", "testability", "future flexibility") fail the gate.
```

Tasks with hollow or missing justification are blocked at review: `code-review` Step 6.6 re-checks concreteness.

A task that adds a canonical component, or changes one's path or extension point, updates its row in `.claude/patterns/CAPABILITY-INDEX.md` in the same PR.

When the project has `notes/reuse-scan.md` (design-to-spec Step 3 or project-pipeline Step 2), `<existing>` also cites that file's row for the capability, including its verdict (extend target, owner answer, or none found with the searches run).

If a task adds new surface or materially grows an existing component, read `references/component-justification.md` before writing its `<justification>` — it holds the real anti-patterns this gate catches and the component-complexity check ([`docs/standards/COMPONENT-COMPLEXITY.md`](../../../docs/standards/COMPONENT-COMPLEXITY.md): judge cohesion, not line count).

### Step 3.6: Add Deployment Tasks (REQUIRED)

For each deliverable type the project produces, create its deployment task(s):

| Deliverable type | Deployment task | Tags | Skill |
|---|---|---|---|
| BFF API endpoints | Deploy to Azure App Service | `deploy, azure, bff-api` | `bff-deploy` |
| PCF controls | Deploy via solution-ZIP import (never `pac pcf push`) | `deploy, dataverse, pcf` | `pcf-deploy` |
| Dataverse fields | Deploy the solution; configure Relevance Search if applicable | `deploy, dataverse, solution` | `dataverse-deploy` |
| Azure AI resources | Provision AI Search / OpenAI; set connection strings in App Service | `deploy, azure, azure-ai` | Bicep / infrastructure scripts (`azure-deploy`) |
| Background workers | Deploy per the ADR-052 placement decision | `deploy, azure, worker` | per placement |

Place deployment tasks at the END of each phase (after implementation + tests), or in a dedicated deploy phase when there are several. Post-deployment integration tests come AFTER all deployment tasks. Example: Phase 1 = 001–009 build + unit tests; Phase 1-Deploy = 015 deploy BFF, 016 deploy PCF, 017 integration tests; …; 090 wrap-up (full layout: `examples/examples.md` Example 4).

### Step 3.65: Add UI Test Definitions for PCF/Frontend Tasks (REQUIRED)

Applies to every task tagged `pcf`, `frontend`, `fluent-ui` or `e2e-test`.

**Concrete visual direction (MUST).** Sonnet 5 settles into a fixed default house style on open-ended frontend briefs, and generic adjustments ("cleaner", "more modern") only shift it to a *different* fixed palette. Anchor the look via ONE of: a `<knowledge><patterns>` reference to an existing Fluent v9 component to mirror, an explicit spec (tokens/spacing/layout), or a referenced design artifact. If the plan gives only "clean and modern", add a concrete anchor from an existing component (e.g. mirror `RecordHeaderShell`) or flag the task as needing design input.

**`<ui-tests>` section (MUST).** Tests specific to the component being built; a console-error check for every PCF control; an ADR-021 dark-mode test for any Fluent UI work; `{placeholder}` syntax for environment values (`{org}`, `{app-id}`). `task-execute` Step 9.7 runs them through the `ui-test` skill when Chrome integration is on.

If any task carries one of these tags, read `references/ui-tests.md` before writing its `<ui-tests>` section.

### Step 3.8: Identify Parallel Task Groups (REQUIRED)

**EVERY task gets `<parallel-group>` and `<parallel-safe>` — no exceptions.** `project-pipeline` runs parallel groups concurrently by default, one agent per task.

```
PARALLEL GROUP = tasks that: share the same prerequisite(s) (or none) · do not depend on each other ·
                 do not modify the same files (check <relevant-files>) · sit in the same or adjacent phases
  e.g. 020, 021, 022 all deps="010" → Group A.  023 deps="020,021" → serial, waits for both.

IN a group:      <parallel-group>{letter}</parallel-group>  <parallel-safe>true</parallel-safe>
NOT safe:        <parallel-safe>false</parallel-safe>  <parallel-reason>{shared files, sequential logic, …}</parallel-reason>

AUTO-DEMOTION — .claude/ permission boundary (NON-NEGOTIABLE):
  IF any <relevant-files> entry starts with .claude/ → <parallel-safe>false</parallel-safe>,
  <parallel-reason>touches .claude/ — main-session-only (permission boundary)</parallel-reason>.  Sub-agents cannot write
  to .claude/ (root CLAUDE.md §3).
AUTO-DEMOTION — file overlap:
  FOR each pair (A, B) in a proposed group: IF A.relevant-files ∩ B.relevant-files ≠ ∅ →
  move one task to another group OR mark both false with <parallel-reason>file-overlap with task {id}</parallel-reason>
```

Record the wave structure in `TASK-INDEX.md` (Step 5):

```markdown
## Parallel Execution Plan
Phase 1:
  Wave 1 (parallel, 4 agents): 010, 011, 012, 013
  Wave 2 (parallel, 3 agents): 020, 021, 022 — prereq: Wave 1
  Wave 3 (sequential): 030 — touches .claude/, main-session-only
```

**Design for parallelism.** Prefer independent tasks over sequential chains; split by file/component ownership (one task per endpoint, per component); avoid two tasks touching the same file. A project with no parallel groups runs one task at a time — aim for 2–3 groups per phase.

### Step 3.85: Assign `/goal` Wave-Completion Eligibility (REQUIRED)

`/goal` keeps a session working turn after turn until a **transcript-only** evaluator (it runs no commands and reads no files) judges a completion condition met. It is a per-**wave** loop that removes the operator's "continue" between tasks — NOT a per-task mechanism and NOT a quality gate (Step 9.5 and the orchestrator judge quality). Assign eligibility to every wave from Step 3.8:

```
GOAL-ELIGIBLE = true IFF ALL:
  (1) machine-verifiable end state the evaluator can SEE in the transcript: tests pass / build exits 0 / lint clean / git status clean
  (2) ≥3 tasks in the wave
  (3) tasks well specified — exhaustive <acceptance-criteria>, explicit <constraints>
  (4) low architectural judgment / low ambiguity
GOAL-ELIGIBLE = false IF ANY:
  wave touches security/auth/secrets (tags auth, security) · deployment-touching or irreversible (tag deploy;
  azure-deployment.md scope) · likely breaking changes or expected ADR conflicts · exploratory/design/research
  with no crisp end state · 1- or 2-task wave
RECORD next to each wave in TASK-INDEX.md:  "— goal-eligible: YES"  or  "— goal-eligible: NO ({reason})"
```

If a wave is goal-eligible, read `references/goal-condition.md` before writing its goal condition.

### Step 3.7: Add Mandatory Project Wrap-up Task (REQUIRED)

ALWAYS create a final "Project Wrap-up" task as the LAST task. ID: highest phase number + 90 (last phase 050 → wrap-up 090), so it stays last when tasks are added. It is mandatory for every project and its steps are:

1. **Final quality gates** — `/code-review` and `/adr-check` on all project code. Classify findings per `task-execute` Step 9.5 and fix every F-class finding before proceeding.
2. **Repository cleanup** — `/repo-cleanup projects/{project-name}`; review the report; approve removals (`notes/debug/`, `notes/spikes/`, `notes/drafts/`); archive handoffs (`notes/handoffs/` → `.archive/`).
3. **README.md** — Status "Complete", Last Updated date, Phase "Complete", Progress "100%", Completed Date, all Graduation Criteria checked, Changelog completion entry.
4. **plan.md** — Status "Complete"; every milestone ✅.
5. **Lessons learned** — append to `notes/lessons-learned.md` (the per-project convention): one lesson per entry with a one-line summary, recording both corrections and confirmed non-obvious approaches and why they mattered. Do NOT create a central lessons store; `doc-drift-audit` / `ai-procedure-maintenance` promote durable lessons into `.claude/FAILURE-MODES.md` or an ADR at project close.
6. **Final verification** — every task completed in TASK-INDEX.md; documentation current; no critical code-review issues; cleanup done.
7. **Archive** — `/devops-project-archive` (closes the Issue, sets board Status, writes `projects/{project-name}/.archived`). Do NOT rename or move the project folder (no `x-` prefix): links and history stay valid, and the pre-commit hook and `projects-layout-guard` workflow refuse a new `projects/x-*` folder.

`task-execute` also runs the project-close `/test-diet` gate for `090-wrapup-*` tasks (root CLAUDE.md §7).

### Step 4: Generate Task Files

For each task create `tasks/{NNN}-{task-slug}.poml` as a **valid XML document** (quote element names in prose as `&lt;name&gt;` — a bare `<name>` in prose breaks the parse):

```xml
<?xml version="1.0" encoding="UTF-8"?>
<task id="{NNN}" project="{project-name}">
  <metadata>
    <title>{Task Title}</title>
    <phase>{N} {Phase Name}</phase>
    <gate>startable | blocked | {dep-condition}</gate>       <!-- readiness -->
    <status>not-started</status>                              <!-- Step 5 status vocabulary -->
    <rigor>{FULL | STANDARD | MINIMAL}</rigor>                <!-- Step 3.5.5 (author hint; task-execute may override) -->
    <rigor-reason>{trigger from the Step 3.5.5 tree}</rigor-reason>
    <model-tier>{sonnet | opus | fable}</model-tier>         <!-- Step 3.5.5b; sonnet default -->
    <model-tier-reason>{trigger from Step 3.5.5b}</model-tier-reason>
    <effort>{low | medium | high | xhigh}</effort>           <!-- Step 3.5.5b effort rubric; default high -->
    <parallel-group>{A/B/C/… or "none"}</parallel-group>     <!-- Step 3.8 -->
    <parallel-safe>{true | false}</parallel-safe>            <!-- Step 3.8; forced false if <relevant-files> touches .claude/ -->
    <parallel-reason>{why, when parallel-safe is false}</parallel-reason>
    <deps>{comma-separated task IDs or "none"}</deps>
    <tags>{Standard Tag Vocabulary — Step 3.4}</tags>
    <estimated-effort>{optional, e.g. 2-4 hours}</estimated-effort>
  </metadata>
  <!-- Canonical names. Deprecated aliases are accepted for back-compat but NOT emitted: <rigor-hint> → <rigor>;
       metadata-sibling <dependencies> → <deps>. <dependencies> stays valid INSIDE <context>. <blocks> is dropped —
       the DAG lives in TASK-INDEX.md. -->

  <prompt>{1-3 sentences: what to accomplish and why — the executable instruction handed to the agent.}</prompt>
  <role>SPAARKE platform developer. Follow ADRs strictly. {Specific expertise, e.g. "Expert in ASP.NET Core Minimal APIs and Microsoft Graph SDK."}</role>
  <goal>{Clear, measurable definition of done — the artifact(s) that will exist.}</goal>

  <context>
    <background>{Why this task exists — business context from plan.md/spec.md}</background>
    <relevant-files>
      <file role="new | modify | canonical-reference">{exact path — name the reference implementation to copy}</file>
    </relevant-files>
    <dependencies>
      <dependency task="{NNN}" status="pending | complete">{what it provides}</dependency>
    </dependencies>
  </context>

  <constraints>                                     <!-- each with an explicit scope clause (Step 3.5.5b) -->
    <constraint source="ADR-{NNN}">{scoped constraint}</constraint>
    <constraint source="project">{project-specific constraint}</constraint>
  </constraints>

  <knowledge>                                       <!-- Steps 3.4 + 3.5; never empty -->
    <files>
      <file>.claude/adr/ADR-{NNN}-{slug}.md</file>
      <file>{path to relevant knowledge article}</file>
    </files>
    <patterns>
      <pattern name="{pattern name}" location="{file path}">{brief description of the pattern to follow}</pattern>
    </patterns>
  </knowledge>

  <steps mode="directional">  <!-- directional (default) | prescriptive (migrations/deploys/irreversible) — Step 3.5.5c -->
    <step order="1">{First concrete action}</step>
    <step order="2">{Continue until the task is complete}</step>
    <step order="N-2">Run tests and verify all pass</step>
    <step order="N-1">Update TASK-INDEX.md: change this task's status cell to `✅ [done]` — the bracketed ASCII token is mandatory, the emoji alone is not greppable (Step 5 status token table + FAILURE-MODES G-16)</step>
    <step order="N">If any deviations from plan, document in projects/{project-name}/notes/</step>
  </steps>

  <escalation>     <!-- REQUIRED for tasks with a known failure mode / judgment boundary — Step 3.5.5c -->
    <trigger>{If X differs from the spec, STOP and escalate per CLAUDE.md §6 rather than adapting.}</trigger>
  </escalation>

  <justification>  <!-- REQUIRED only for NEW-surface tasks — Step 3.5.6. Omit for modify-only tasks. -->
    <existing>{…}</existing><extension>{…}</extension><cost-of-doing-nothing>{…}</cost-of-doing-nothing>
  </justification>

  <ui-tests>       <!-- REQUIRED for tags pcf/frontend/fluent-ui/e2e-test — Step 3.65 -->
    <test name="{name}"><steps><step>{action}</step></steps><expected>{outcome}</expected></test>
  </ui-tests>

  <tools>
    <tool name="dotnet">Build and test .NET projects</tool>
    <tool name="npm">Build TypeScript/PCF projects (npm run build:prod for PCF)</tool>
    <tool name="terminal">Run shell commands</tool>
  </tools>

  <outputs>
    <output type="code">{exact path to code file}</output>
    <output type="test">{exact path to test file}</output>
    <output type="docs">{exact path to doc file if any}</output>
  </outputs>

  <acceptance-criteria>                             <!-- CLOSED SET incl. negative/authorization cases — Step 3.5.5b -->
    <criterion testable="true">Given {precondition}, when {action}, then {expected result}.</criterion>
    <criterion testable="true">Negative: {unauthorized/malformed path} returns {expected}, not an unhandled error.</criterion>
    <criterion testable="true">All unit tests pass.</criterion>
  </acceptance-criteria>

  <notes>{Implementation hints, gotchas, spec.md section references}</notes>

  <execution>
    <skill>.claude/skills/task-execute/SKILL.md</skill>
    <protocol>Before starting, load all files listed in &lt;knowledge&gt;&lt;files&gt;. Follow task-execute for the pre-execution checklist.</protocol>
  </execution>
</task>
```

**POML Tag Requirements.** Every task MUST have: `<task id project>`, `<metadata>` (title, phase, gate, status, `<rigor>` + reason, `<model-tier>` + reason, `<effort>`, `<parallel-group>` + `<parallel-safe>`, `<deps>`, `<tags>`), `<prompt>`, `<role>`, `<goal>`, `<context>`, `<constraints>` (with `source`), `<knowledge>` (non-empty), `<steps mode>` (with `order`), `<outputs>` (with `type`), `<acceptance-criteria>` (with `testable="true"`). Conditionally required: `<escalation>` (judgment boundary), `<justification>` (new surface), `<ui-tests>` (frontend tags). Recommended: `<tools>`, `<notes>`, `<execution>`.

### Step 5: Update Project Files

```
UPDATE projects/{project-name}/CLAUDE.md: task count + phase mapping; "Next Action" → the first task
CREATE tasks/TASK-INDEX.md:
  | ID  | Title | Phase | Status    | Dependencies | Parallel |
  |-----|-------|-------|-----------|--------------|----------|
  | 001 | …     | 1     | 🔲 [open] | none         | —        |
  | 020 | …     | 2     | 🔲 [open] | 010          | Group A  |
  + the Step 3.8 Parallel Execution Plan (waves + Step 3.85 goal-eligibility)
  + a "Parallel Execution Groups" section (below)
```

**Status token table.** 🔴 **The bracketed ASCII token is MANDATORY in every status cell — the emoji alone is not greppable.** Keep the emoji too (faster for a human to scan); the token is additive in the same cell.

| Token | Emoji | Meaning | POML `<status>` |
|---|---|---|---|
| `[open]` | 🔲 | not started | `not-started` (also accepted: `pending`) |
| `[wip]` | 🔄 | in progress | `in-progress` |
| `[done]` | ✅ | complete | `completed` |
| `[escalated]` | ⚠️ | complete, residue accepted | `completed-with-escalation` |
| `[blocked]` | 🟡 | shipped but blocked | `blocked-shipped` |
| `[cancelled]` | 🚫 | cut by owner decision | `cancelled` |
| `[deferred]` | ⏸️ | postponed by owner decision (reason in notes) | `deferred` |

POML `<status>blocked</status>` (waiting on a dependency or external input) is still open work: its index cell stays `[open]` or `[wip]`. `[blocked]` means *shipped* but blocked.

**Why the token.** `grep` here silently returns 0 for characters above U+FFFF; 🔲 is U+1F532, so `grep -c '🔲'` reports zero open tasks on a project with dozens — no error, a plausible wrong number ([FAILURE-MODES G-16](../../FAILURE-MODES.md#g-16-grep-silently-cannot-match-characters-above-uffff-most-colored-emoji)).

⚠️ **Status lives in TWO places** — the index cell and the POML `<status>` — and nothing structurally keeps them equal (one audit found 17 disagreements across 92 tasks). After any status write run `pwsh scripts/check-task-status-drift.ps1` (`task-execute` Step 10 requires it). When they disagree, **resolve from git evidence**, not by assuming either side is right.

```markdown
## Parallel Execution Groups

Tasks in the same group can run simultaneously once prerequisites are met.

| Group | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|-------|-------|--------------|---------------|---------------------|
| A | 020, 021, 022 | 010 ✅ | Separate endpoints | ✅ Yes |
| B | 031, 032 | 030 ✅ | Separate components | ✅ Yes |

**How to execute a group:** confirm all prerequisites are complete → invoke the Agent tool with one task-execute
subagent per task in ONE message → wait for all to finish before the next group.
```

### Step 6: Output Summary

Report: tasks created for `projects/{project-name}/`; per-phase counts with id ranges (plus `Wrap-up: 1 task (090-project-wrap-up)`) and the total; the rigor distribution (FULL / STANDARD / MINIMAL counts); one line per task as `{id} - {title} ({rigor} - {reason})`; the files created (`TASK-INDEX.md`, each `.poml`, the mandatory `090-project-wrap-up.poml`); an execution-order recommendation (start with tasks that have no dependencies, end with 090); then `Run /task-execute 001 to begin implementation.` The full layout is Example 5 in `examples/examples.md`.

## Validation Checklist

Before completing task-create, verify — fix anything that fails and re-check:
- [ ] TASK-INDEX.md created with all tasks listed, every status cell carrying its bracketed token
- [ ] Each task file has all required POML sections (Step 4 POML Tag Requirements)
- [ ] Task numbers follow the gap convention (001, 010, 020…)
- [ ] Dependencies form a valid DAG (no circular refs); first task(s) have no unmet dependencies
- [ ] Acceptance criteria are copied/referenced from plan.md
- [ ] Project CLAUDE.md updated with task summary
- [ ] Every task's `<knowledge>` is non-empty and matches its tags (Step 3.4)
- [ ] PCF/frontend tasks have `<ui-tests>` (Step 3.65), with a dark-mode check for Fluent UI tasks (ADR-021)
- [ ] Each task has `<rigor>` + `<rigor-reason>` (Step 3.5.5) — not the deprecated `<rigor-hint>` — and levels match the task (FULL for code, STANDARD for tests, MINIMAL for docs)
- [ ] Each task has `<model-tier>` + `<model-tier-reason>` + `<effort>` (Step 3.5.5b); `xhigh` only where the rubric justifies it
- [ ] Each task's `<steps>` has an explicit `mode` (Step 3.5.5c); tasks with a known failure mode / judgment boundary carry an `<escalation><trigger>`
- [ ] Constraints are scoped explicitly; acceptance criteria are a closed set incl. negative/authorization cases (Authoring for literal execution)
- [ ] Frontend tasks anchor visual direction concretely — pattern ref or explicit spec (Step 3.65)
- [ ] New-surface tasks carry a concrete `<justification>` (Step 3.5.6)
- [ ] Every task has `<parallel-group>` + `<parallel-safe>`; no two tasks in one group modify the same file; TASK-INDEX.md has the "Parallel Execution Groups" section (Step 3.8)
- [ ] Every wave has a goal-eligibility entry (Step 3.85); a deployment task exists for each deliverable type (Step 3.6); the wrap-up task is last (Step 3.7)

### Completeness Lint (REQUIRED)

Run this mechanical check on every generated POML before declaring task-create complete. The omissions it catches are otherwise silent — a well-formed POML missing `<model-tier>` just falls back to a default at dispatch.

```
pwsh scripts/Validate-TaskPoml.ps1 projects/{name}/tasks

FOR each tasks/*.poml:
  REQUIRE well-formed XML
  REQUIRE present + non-empty: <model-tier>, <effort>, <rigor>, <parallel-group>, <parallel-safe>
  REQUIRE <steps> with explicit mode="directional|prescriptive"
  IF the task adds NEW surface (new .cs/.ts/.tsx file, endpoint, DI registration, package, Dataverse column,
     or a <relevant-files> entry with role="new"): REQUIRE a non-hollow <justification> — Step 3.5.6
  IF tags include pcf|frontend|fluent-ui|e2e-test: REQUIRE <ui-tests>
  REJECT the deprecated <rigor-hint> / metadata-sibling <dependencies> (use <rigor> / <deps>)
FAIL (do not report task-create complete) if any POML is missing a required field. Fix, then re-run.
```

The script reports a missing `<justification>` and `<ui-tests>` as WARN only; treat both as failures here. The same gate runs again at PR time (`code-review` Step 6.7), so a hand-edited POML cannot slip past review.

## Portfolio Hook

**At the end of every run** (after all POMLs are written and linted): invoke `/devops-project-sync` to set the `Task Count` field on the Project Issue (it counts `projects/{name}/tasks/*.poml` from local state). Silent on success; a no-op when `projects/{name}/README.md` lacks the portfolio pointer block (project not registered). See [`devops-project-sync`](../devops-project-sync/SKILL.md).

## Gotchas

- **Empty `<knowledge><files>`.** The task's tags matched no Step 3.4 mapping row. Add a mapping row or change the task to a known tag; at minimum include the module's CLAUDE.md. The mapping is what connects a task to its ADRs and constraints.
- **TASK-INDEX.md without parallel-group annotations.** Step 3.8 was skipped; `project-pipeline` then runs everything serially. Always run it.
- **Frontend tasks without `<ui-tests>`.** Step 3.65 was skipped, so `task-execute` Step 9.7 has nothing to run.
- **Re-running over an existing `tasks/`.** task-create does not merge; numbering collides. Delete `tasks/` first or merge by hand, and never overwrite without the user's confirmation.
- **Renumbering a step breaks callers.** Steps 3.4–3.85, 4 and 5 are cited by number across POMLs, CLAUDE.md, the template and other skills. Verify with `grep -rn "task-create.*Step 3"` after any edit here.
- **Template drift.** `.claude/templates/task-execution.template.md` mirrors Step 4. Change both together (`ai-procedure-maintenance` Checklist F); this skill wins when they differ.
- **Emoji-only status cells.** They read as zero open tasks to `grep` (Step 5, G-16).

## References

- **Skeleton (pointer)**: `.claude/templates/task-execution.template.md` — mirrors Step 4; this skill is authoritative for the field set and per-field logic (Steps 3.4–3.85).
- **Scripts**: `scripts/Validate-TaskPoml.ps1` (Completeness Lint) · `scripts/check-task-status-drift.ps1` (Step 5).
- **Related skills**: `project-setup` / `project-pipeline` (produce plan.md; pipeline Step 3 calls this skill) · `task-execute` (runs tasks; Step 0.5 rigor, Step 9.5 gates, Step 9.7 UI tests) · `ui-test` · `adr-aware` (full ADR mapping) · `code-review` (Steps 6.6 / 6.7 re-check justification and completeness).
- **Conditional references**: `references/component-justification.md` (Step 3.5.6 anti-patterns + component-complexity check) · `references/ui-tests.md` (Step 3.65 `<ui-tests>` structure) · `references/goal-condition.md` (Step 3.85 condition template).
- **Examples**: [`examples/examples.md`](examples/examples.md) — decomposing a 4-phase plan into 14 POMLs; fine-grained decomposition; a plan with no WBS (graceful stop); phase layout with deployment tasks; the full Step 6 output summary.
