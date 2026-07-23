---
description: Decompose a project plan into numbered POML task files for systematic AI-assisted execution
tags: [tasks, planning, project-structure, poml]
techStack: [all]
appliesTo: ["projects/*/plan.md", "create tasks", "decompose plan"]
alwaysApply: false
exemplar: projects/ai-procedure-quality-r1/tasks/
last-reviewed: 2026-07-16
---

# task-create

> **Last Reviewed**: 2026-05-17
> **Reviewed By**: ai-procedure-quality-r1 (Phase 2b Wave 2c — `leave-alone-justified` on body length per dereferencing-reliability concern; light extract: 3 worked Examples moved to `examples/examples.md`)
> **Exemplar rationale**: `projects/ai-procedure-quality-r1/tasks/` is a recent multi-task project (32 tasks across 7 phases) produced by this skill — a live reference output.
> **Justified length**: procedural Workflow body kept inline. Task POML callers cite Step 3.5.5, 3.65, 3.8 — these step numbers are external API and stay in body. Examples moved to `examples/` because they're reference content, not procedure.

## Purpose

Transforms `plan.md` work breakdown structure (WBS) into individual, executable task files in the `tasks/` directory. Each task is a **valid POML/XML document** (`.poml` extension) optimized for AI agent execution. This skill bridges planning (project-init) and implementation (task-execute).

## When to Use

- After `project-init` has created plan.md with WBS phases
- User says "create tasks", "decompose plan", or "generate task files"
- Explicitly invoked with `/task-create {project-name}`
- Plan.md exists but tasks/ directory is empty

## Inputs Required

| Input | Required | Source |
|-------|----------|--------|
| Project path | Yes | Path to project in `projects/` folder |
| plan.md | Yes | Auto-loaded from `projects/{project-name}/plan.md` |
| Task granularity | No | Default: "medium" (2-4 hours of work per task) |

## Workflow

### Step 1: Load Project Context
```
EXTRACT project-name from provided path
LOAD: projects/{project-name}/plan.md
LOAD: projects/{project-name}/README.md  # For context
LOAD: .claude/templates/task-execution.template.md  # POML SKELETON (pointer) — this skill's Steps 3.5.x–3.85 + Step 4 are the AUTHORITATIVE field set; the template mirrors them for copy-paste

EXTRACT from plan.md:
  - WBS phases (Section 5)
  - Dependencies (Section 6 if present)
  - Risks/constraints (Section 7)
  - Acceptance criteria (Section 8)
```

### Step 2: Validate Plan Readiness
```
CHECK plan.md has:
  ✓ At least one WBS phase defined
  ✓ Each phase has deliverables or outcomes listed
  ✓ No "TBD" in critical sections

IF validation fails:
  → List missing elements
  → Offer to help complete plan.md first
  → STOP until plan is ready
```

### Step 3: Decompose Phases into Tasks
```
FOR each WBS phase:
  IDENTIFY discrete work items:
    - Each item should be completable in one session (2-4 hours)
    - Each item should have a verifiable output
    - Dependencies should be explicit
  
  APPLY numbering scheme:
    - Phase 1 tasks: 001, 002, 003...
    - Phase 2 tasks: 010, 011, 012...
    - Phase 3 tasks: 020, 021, 022...
    (10-gap allows inserting tasks later)
```

### Step 3.4: Discover Related Resources (REQUIRED)

**Every task MUST have a `<knowledge>` section with relevant files based on its tags.**

#### Tag-to-Knowledge Mapping (MANDATORY)

When a task has these tags, ALWAYS include these knowledge files:

| Tag | Constraints | Patterns | Additional Files |
|-----|-------------|----------|------------------|
| `pcf`, `react`, `fluent-ui`, `frontend`, `e2e-test` | `.claude/constraints/pcf.md` | `.claude/patterns/pcf/control-initialization.md`, `.claude/patterns/pcf/theme-management.md` | `src/client/pcf/CLAUDE.md`, `docs/guides/PCF-DEPLOYMENT-GUIDE.md`, `.claude/skills/ui-test/SKILL.md` |
| `bff-api`, `api`, `minimal-api`, `endpoints` | `.claude/constraints/api.md` | `.claude/patterns/api/endpoint-definition.md`, `.claude/patterns/api/endpoint-filters.md` | `src/server/api/CLAUDE.md` (if exists) |
| `dataverse`, `solution`, `fields`, `plugin` | `.claude/constraints/plugins.md` | `.claude/patterns/dataverse/plugin-structure.md` | `.claude/skills/dataverse-deploy/SKILL.md` |
| `auth`, `oauth`, `authorization` | `.claude/constraints/auth.md` | `.claude/patterns/auth/spaarke-sso-binding.md` (canonical v2), `.claude/patterns/auth/obo-flow.md`, `.claude/patterns/auth/oauth-scopes.md` | `.claude/adr/ADR-028-spaarke-auth-architecture.md`, `docs/guides/auth-deployment-setup.md` |
| `cache`, `redis`, `caching` | `.claude/constraints/data.md` | `.claude/patterns/caching/distributed-cache.md` | — |
| `ai`, `azure-openai`, `document-intelligence` | `.claude/constraints/ai.md` | `.claude/patterns/ai/streaming-endpoints.md` | — |
| `deploy` | — | — | `.claude/skills/dataverse-deploy/SKILL.md`, `docs/guides/PCF-DEPLOYMENT-GUIDE.md` |
| `testing`, `unit-test`, `integration-test` | `.claude/constraints/testing.md` | `.claude/patterns/testing/unit-test-structure.md`, `.claude/patterns/testing/mocking-patterns.md` | — |
| `worker`, `job`, `background` | `.claude/constraints/jobs.md` | — | — |

**Critical: PCF tasks MUST reference PCF-DEPLOYMENT-GUIDE.md**

The `PCF-DEPLOYMENT-GUIDE.md` guide contains **mandatory version bumping instructions**:
- Version must be updated in 4 locations before deployment
- Footer must show version number
- Failure to follow this results in stale deployments

```
FOR each task:
  EXTRACT tags from <metadata><tags>
  FOR each tag:
    LOOKUP in Tag-to-Knowledge Mapping
    ADD matching constraint file to <knowledge><files>
    ADD matching pattern files to <knowledge><files>
    ADD matching additional files to <knowledge><files>

  ENSURE <knowledge> section is NOT empty
  IF no matches found:
    ADD at minimum: relevant CLAUDE.md for the module being modified
```

### Step 3.5: Map ADRs to Tasks (REQUIRED)

```
FOR each task identified:
  DETERMINE resource types being created/modified:
    - API Endpoint → ADR-001, ADR-008, ADR-010
    - Authorization → ADR-003, ADR-008
    - Caching → ADR-009
    - Dataverse Plugin → ADR-002
    - Graph/SPE Integration → ADR-007
    - PCF Control → ADR-006, ADR-011, ADR-012, ADR-021
    - Background Worker → ADR-001, ADR-004
    - DI Registration → ADR-010
    - AI Features → ADR-013, ADR-014, ADR-015, ADR-016
    - Testing → ADR-022

  ADD to task:
    - <constraints> with source="ADR-XXX" for each applicable ADR
    - <knowledge><files> include CONCISE ADR paths:
      - Use .claude/adr/ADR-XXX-*.md (100-150 lines, AI-optimized)
      - NOT docs/adr/ADR-XXX-*.md (full version, load only if needed)

REFERENCE: See adr-aware skill for full mapping table
```

### Step 3.5.5: Determine Task Rigor Level (REQUIRED)

```
FOR each task identified:
  DETERMINE rigor level using the same decision tree from task-execute skill:

  RIGOR LEVEL = FULL IF task has ANY of:
    - Tags include: bff-api, api, pcf, plugin, auth
    - Will modify code files (.cs, .ts, .tsx) - check <relevant-files>
    - Has 6+ steps in <steps> section
    - Task description includes: "implement", "refactor", "create service"
    - Dependencies on 3+ other tasks

  RIGOR LEVEL = STANDARD IF task has ANY of:
    - Tags include: testing, integration-test
    - Will create new files (check <outputs> for new paths)
    - Has explicit <constraints> or ADRs listed
    - Phase 2.x or higher (integration/deployment phases)

  RIGOR LEVEL = MINIMAL OTHERWISE:
    - Documentation tasks
    - Inventory/checklist creation
    - Simple configuration updates

  ADD to task <metadata> (canonical field is <rigor>; <rigor-hint> is a deprecated alias — do NOT emit it):
    <rigor>{FULL | STANDARD | MINIMAL}</rigor>
    <rigor-reason>{Why this level - reference specific trigger from decision tree}</rigor-reason>

  EXAMPLE rigor values:
    <rigor>FULL</rigor>
    <rigor-reason>Task tags include 'bff-api' (code implementation)</rigor-reason>

    <rigor>STANDARD</rigor>
    <rigor-reason>Task tags include 'testing', 'integration-test'</rigor-reason>

    <rigor>MINIMAL</rigor>
    <rigor-reason>Documentation task (no code implementation)</rigor-reason>

  PURPOSE:
    - Makes rigor level explicit in task file (documented, not inferred)
    - task-execute skill uses this hint but can override based on actual characteristics
    - User can override by editing task file before execution
    - Audit trail shows why rigor level was chosen

REFERENCE: See .claude/skills/task-execute/SKILL.md Step 0.5 for full decision tree
```

### Step 3.5.5b: Assign Execution Model Tier + Effort (REQUIRED — added 2026-07-08 for Sonnet-5 execution)

**Model strategy (binding):** planning phases (`design-to-spec`, `project-pipeline` Steps 0–3) run on **Opus 4.8 / Fable 5**; task **execution defaults to Sonnet 5** (near-Opus coding quality at lower cost). `project-pipeline`/`task-create` flags the minority of tasks that genuinely need top-tier reasoning back up to Opus/Fable via a per-task `<model-tier>`, and sets an explicit `<effort>` per task (see the effort rubric below).

```
FOR each task identified, assign a model tier:

  MODEL TIER = opus (or fable) IF the task has ANY of:
    - Cross-cutting refactor / migration touching 3+ existing files or consumers (high blast radius)
    - Architectural change, new pattern/abstraction authoring, or ADR-migration / ADR-compliance work
    - Security/auth-sensitive code (tags include: auth, security)
    - Ambiguous or underspecified goal requiring significant judgment, or novel algorithm design
    - FULL rigor AND (dependencies on 3+ tasks OR flagged high-risk in the plan Risk Register)
    → fable ONLY for the very hardest of these (whole-subsystem migration, novel architecture);
      opus for everything else in this bucket.

  MODEL TIER = sonnet OTHERWISE (default):
    - Standard single-component code implementation with an explicit canonical reference to follow
    - Test authoring, wiring, configuration, deployment steps
    - Documentation / inventory tasks

  ADD to task <metadata>:
    <model-tier>{sonnet | opus | fable}</model-tier>
    <model-tier-reason>{Why - reference the specific trigger}</model-tier-reason>

  EXAMPLES:
    <model-tier>opus</model-tier>
    <model-tier-reason>Cross-cutting migration of 4 wizard families onto a shared module (high blast radius)</model-tier-reason>

    <model-tier>opus</model-tier>
    <model-tier-reason>ADR-024 resolver-compliance migration of an existing non-compliant wizard</model-tier-reason>

    <model-tier>sonnet</model-tier>
    <model-tier-reason>New single-component wizard following the canonical workAssignmentService.ts reference</model-tier-reason>

  PURPOSE:
    - project-pipeline Step 5 dispatches each task's subagent with model = <model-tier>
    - task-execute declares the tier and escalates if the current session model is lower
    - Keeps the expensive top tier scoped to the tasks that actually need it (cost control)
```

**Effort rubric (REQUIRED — set `<effort>` per task).** Sonnet 5 respects effort strictly: `high` is the default; `xhigh` is for the hardest coding/agentic work. Blanket `xhigh` is NOT free — at `xhigh` Sonnet-5 cost approaches Opus 4.8, so reserve it. Route on how hard the *reasoning* is, given a complete spec:

```
  EFFORT = xhigh IF (model-tier is sonnet AND) the task is:
    - Brownfield debugging / root-cause / race-condition-class work (Sonnet 5 is documented
      as particularly strong here — some tasks once tagged "Opus-required" belong here instead)
    - Complex multi-file change with a COMPLETE spec (hard depth, low ambiguity)

  EFFORT = high OTHERWISE (default):
    - Routine, well-patterned work with a clear reference (CRUD endpoints, PCF wiring,
      mechanical refactors, test authoring, config)

  EFFORT = medium/low: reserve for short, scoped, latency-sensitive mechanical steps only.

  opus/fable tasks: set <effort>high</effort> unless the task is a genuine whole-subsystem
  reasoning problem (then xhigh). Do NOT stack fable + xhigh reflexively.

  ADD to task <metadata>:
    <effort>{low | medium | high | xhigh}</effort>

  Cost guardrail: if a SONNET task genuinely needs sustained xhigh, re-check whether it should
  be <model-tier>opus</model-tier> instead — xhigh is for tasks where Sonnet's follow-through
  profile fits but depth is needed, not a substitute for correct Opus routing.
```

**Authoring for literal (Sonnet-5) execution (applies to EVERY task, all tiers).** Sonnet 5 follows instructions literally and does NOT generalize intent or infer unstated requests. Under-specified tasks degrade Sonnet-5 output more than Opus. Author each POML so a literal executor cannot go wrong:

- **Explicit scope everywhere.** List the **exact files** to touch (not "the follow-on components"); **point at the canonical reference implementation to copy** (e.g. `workAssignmentService.ts`) rather than describe it; state the **exact contract** (e.g. the ADR-024 resolver's 5 fields + mutual-exclusion). A `<constraint>` without an explicit scope clause is a defect — write "Every endpoint added or modified in this task applies `DocumentAuthFilter`," not "use endpoint filters for auth."
- **Acceptance criteria are a CLOSED SET.** The executor treats every listed `<criterion>` as mandatory and **anything not listed as out of scope**. Criteria must be **exhaustive, not illustrative** — include the negative/authorization cases (the 401, the empty-input, the unauthorized-user path), not just the happy path.
- **"Above and beyond" must be requested.** If a task should include opportunistic improvements (e.g. "also fix adjacent lint violations in touched files"), say so explicitly. Do NOT rely on the model to infer it — and do NOT add anti-laziness scaffolding ("be exhaustive", "double-check everything"); Sonnet 5 over-triggers on those and burns effort on ritual verification.
- **Step mode (see Step 3.5.5c) and escalation triggers (see the `<escalation>` element)** are the other two literal-execution levers — set them deliberately per task.
- **Frontend tasks need concrete visual direction** (see Step 3.65): anchor the look in a `<knowledge>` pattern reference to an existing Fluent v9 component or an explicit spec. "Clean and modern" is not a spec — Sonnet 5 will settle into a fixed default house style.
- **Knowledge curation (token discipline).** The 1M context window is headroom for genuinely cross-cutting tasks, NOT license to load the full ADR corpus by default. The Sonnet-5 tokenizer produces ~30% more tokens for the same text, so padding is materially more expensive — load what the task needs via the Tag-to-Knowledge mapping, reference the rest by path.

### Step 3.5.5c: Choose Step Mode + Escalation Triggers (REQUIRED — added 2026-07-08 for Sonnet-5 execution)

A literal, agentic executor treats granular ordered `<steps>` as a **binding contract** — it will do exactly the six steps written even if step 3 is wrong for the actual codebase state. Choose a step mode per task:

```
  <steps mode="directional">  ← DEFAULT for standard implementation tasks.
      Steps are guidance; the binding contract is <goal> + <acceptance-criteria> + <constraints>.
      The executor MAY adapt the sequence to the real codebase state.

  <steps mode="prescriptive">  ← for migrations, deployment-touching work, anything under
      azure-deployment.md constraints, or any irreversible/ordered procedure.
      The exact sequence is binding; deviations require escalation (not silent improvisation).
```

**Escalation triggers.** A literal model will not infer *when* to invoke human escalation (root CLAUDE.md §6 / §6.5), and Sonnet 5's follow-through tendency makes it more likely to push to *a* completion than to stop at a judgment boundary. For any task with a known failure mode or judgment boundary, add an `<escalation>` element naming the concrete trip-wire:

```xml
<escalation>
  <trigger>If the Graph API contract differs from the spec, STOP and escalate (root CLAUDE.md §6) rather than adapting the implementation.</trigger>
</escalation>
```

This element is also load-bearing for the `/goal` wave loop (Step 3.8): inside a goal loop, firing an `<escalation>` trigger is the *correct* way out — the worker writes `BLOCKED.md` and the loop ends on the escalation branch instead of improvising.

### Step 3.5.6: Component Justification Gate (REQUIRED per CLAUDE.md §11)

**This step enforces the universal rule from root CLAUDE.md §11 "Component Justification — Default to Reuse."**

For every task that introduces a NEW component (service, abstraction, interface, endpoint, route, DI registration, package dependency, Dataverse column, file path under `src/`), add a `<justification>` element answering the three-question template:

```xml
<justification>
  <existing>Closest existing neighbor — cite file:line from Grep evidence (or "none found" with grep command shown)</existing>
  <extension>Yes/No + reason in ≤2 sentences. "Cleaner separation" is NOT a reason.</extension>
  <cost-of-doing-nothing>Concrete behavior or contract that fails without this. NOT "scalability" / "abstraction layer" / "future flexibility."</cost-of-doing-nothing>
</justification>
```

**Decision logic during decomposition**:

```
FOR each new-component task:
  EVALUATE the three answers:

  IF <extension> is "Yes → can extend existing":
    → REWRITE the task to "Extend `<existing>` with …" instead of "Build new `<new-component>`"
    → Continue with the rewritten task

  IF <cost-of-doing-nothing> cannot name a concrete behavior or contract that fails:
    → DEMOTE the task (mark as "deferred — needs concrete failure mode") OR DROP

  IF all three answers are concrete + extension genuinely impossible:
    → PROCEED with new-component task

  Reviewer flag: hollow / boilerplate answers ("for separation of concerns", "for testability", "for future flexibility") fail this gate.
```

**Scope of this gate** — applies to tasks that ADD surface:
- New `.cs` / `.ts` / `.tsx` file
- New endpoint route / handler
- New DI registration
- New package reference (`<PackageReference>` / `dependencies` entry)
- New Dataverse column / entity / alternate key
- New skill / agent / pattern / constraint file

**NOT required for**: tasks that ONLY modify existing files (edit, refactor, fix bug, add tests for existing surface, rename, format). The rule applies to NEW surface, not modification.

**Audit trail**: tasks with hollow or missing `<justification>` are blocked from code review per CLAUDE.md §11. The `code-review` skill Step 6.6 verifies justification concreteness at PR time.

### Step 3.6: Add Deployment Tasks (REQUIRED)

```
FOR each deliverable type in the project, CREATE deployment task(s):

DEPLOYMENT TASK MAPPING:
┌──────────────────────┬────────────────────────────────────────────────────────────┐
│ Deliverable Type     │ Required Deployment Task(s)                                │
├──────────────────────┼────────────────────────────────────────────────────────────┤
│ BFF API Endpoints    │ - Deploy to Azure App Service                              │
│                      │ - Tags: [deploy, azure, bff-api]                           │
│                      │ - Skill: (azure deployment commands)                       │
├──────────────────────┼────────────────────────────────────────────────────────────┤
│ PCF Controls         │ - Deploy PCF to Dataverse (pac pcf push)                   │
│                      │ - Tags: [deploy, dataverse, pcf]                           │
│                      │ - Skill: dataverse-deploy                                  │
├──────────────────────┼────────────────────────────────────────────────────────────┤
│ Dataverse Fields     │ - Deploy solution to Dataverse                             │
│                      │ - Configure Relevance Search (if applicable)               │
│                      │ - Tags: [deploy, dataverse, solution]                      │
│                      │ - Skill: dataverse-deploy                                  │
├──────────────────────┼────────────────────────────────────────────────────────────┤
│ Azure AI Resources   │ - Provision Azure AI Search/OpenAI resources               │
│                      │ - Configure connection strings in App Service              │
│                      │ - Tags: [deploy, azure, azure-ai]                          │
│                      │ - Skill: (bicep/infrastructure scripts)                    │
├──────────────────────┼────────────────────────────────────────────────────────────┤
│ Background Workers   │ - Deploy worker to Azure Container Apps                    │
│                      │ - Tags: [deploy, azure, worker]                            │
└──────────────────────┴────────────────────────────────────────────────────────────┘

PLACEMENT:
  - Deployment tasks should be at the END of each phase (after implementation + testing)
  - OR create a dedicated "Deployment" phase if multiple deployments needed
  - Final integration testing task should come AFTER all deployment tasks

EXAMPLE Phase Structure:
  Phase 1: Implementation
    001-010: Build features
    009: Unit tests
  Phase 1-Deploy:
    015: Deploy BFF API to Azure
    016: Deploy PCF to Dataverse
    017: Integration tests (post-deployment)
  Phase 2: Next feature set...
  ...
  090: Project wrap-up
```

### Step 3.65: Add UI Test Definitions for PCF/Frontend Tasks (REQUIRED)

**Concrete visual direction (REQUIRED for frontend tasks — added 2026-07-08 for Sonnet-5).** Before writing UI tests, verify the task anchors its visual direction concretely. Sonnet 5 settles into a fixed default house style on open-ended frontend briefs, and generic adjustments ("cleaner", "more modern") just shift it to a *different* fixed palette rather than matching intent. Every `pcf`/`frontend`/`fluent-ui` task MUST anchor the look via ONE of: a `<knowledge><patterns>` reference to an existing Fluent v9 component in the repo to mirror, an explicit spec (tokens/spacing/layout), or a referenced design artifact. Reject "clean and modern" as a spec — if the plan gives only that, add the concrete anchor from an existing component (e.g. mirror `RecordHeaderShell`) or flag the task as needing design input.

```
FOR each task with tags: pcf, frontend, fluent-ui, e2e-test:

  ADD <ui-tests> section to task POML with:
    - Test name and description
    - Target URL (use {org} placeholder for environment)
    - Step-by-step test actions
    - Expected outcomes
    - ADR-021 dark mode checks (for Fluent UI components)

  EXAMPLE <ui-tests> structure:
    <ui-tests>
      <test name="Component Renders">
        <url>https://{org}.crm.dynamics.com/main.aspx?appid={app-id}&amp;pagetype=entityrecord&amp;etn=account</url>
        <steps>
          <step>Navigate to Account form</step>
          <step>Verify {component-name} control is visible</step>
          <step>Check console for JavaScript errors</step>
        </steps>
        <expected>Control renders without console errors</expected>
      </test>

      <test name="Dark Mode Compliance (ADR-021)">
        <steps>
          <step>Toggle dark mode in D365 settings</step>
          <step>Verify background colors adapt (no white in dark mode)</step>
          <step>Verify text colors adapt (no black in dark mode)</step>
          <step>Verify icons use currentColor</step>
        </steps>
        <expected>All colors use Fluent UI v9 semantic tokens per ADR-021</expected>
      </test>

      <test name="User Interaction">
        <steps>
          <step>Click primary action button</step>
          <step>Verify loading indicator appears</step>
          <step>Verify response displays correctly</step>
        </steps>
        <expected>Interaction completes within 3 seconds</expected>
      </test>
    </ui-tests>

  WHY: UI tests are executed by task-execute Step 9.7 via ui-test skill
       when Claude Code has Chrome integration enabled (--chrome flag)

  REQUIREMENTS:
    - Tests must be specific to the component being built
    - Include dark mode test if task involves Fluent UI
    - Include console error check for all PCF controls
    - Use {placeholder} syntax for environment-specific values
```

### Step 3.8: Identify Parallel Task Groups (REQUIRED)

**Purpose:** Enable efficient parallel execution by grouping independent tasks.

```
FOR each phase:
  ANALYZE task dependencies to identify parallel groups:

  PARALLEL GROUP = Tasks where:
    - All have the SAME prerequisite task(s) (or none)
    - NO task depends on another in the same group
    - Tasks do NOT modify the same files (check <relevant-files>)
    - Tasks are in the same phase or adjacent phases

  EXAMPLE dependency analysis:
    Task 020: dependencies="010"
    Task 021: dependencies="010"
    Task 022: dependencies="010"
    → These three CAN run in parallel (Group A)

    Task 023: dependencies="020,021"
    → This MUST wait for both 020 and 021 (serial)

  CREATE Parallel Groups list:
    Group A: [020, 021, 022] - prereq: 010
    Group B: [031, 032] - prereq: 030

  FOR each task in a parallel group:
    ADD to <metadata>:
      <parallel-group>{group-letter}</parallel-group>
      <parallel-safe>true</parallel-safe>

  FOR tasks NOT safe to parallelize:
    ADD to <metadata>:
      <parallel-safe>false</parallel-safe>
      <parallel-reason>{why: shared files, sequential logic, etc.}</parallel-reason>

  **AUTO-DEMOTION RULE — `.claude/` permission boundary:**
    IF any file in <relevant-files> starts with `.claude/`:
      → FORCE <parallel-safe>false</parallel-safe>
      → SET <parallel-reason>touches .claude/ — main-session-only (permission boundary)</parallel-reason>
      → This is NON-NEGOTIABLE. Sub-agents cannot write to .claude/.
      → See CLAUDE.md "Sub-Agent Write Boundary" section.

  **AUTO-DEMOTION RULE — file overlap check:**
    WITHIN each proposed parallel group:
      FOR each pair of tasks (A, B) in the group:
        IF intersection(A.relevant-files, B.relevant-files) is non-empty:
          → SPLIT: move one task to a different group
          → OR mark both <parallel-safe>false</parallel-safe>
          → Add <parallel-reason>file-overlap with task {other-id}</parallel-reason>

  DOCUMENT in TASK-INDEX.md "Parallel Execution Groups" section with wave structure:
    ```
    ## Parallel Execution Plan

    Phase 1:
      Wave 1 (parallel, 4 agents): 010, 011, 012, 013
      Wave 2 (parallel, 3 agents): 020, 021, 022 — prereq: Wave 1
      Wave 3 (sequential): 030 — touches .claude/, main-session-only
    ```

WHY THIS MATTERS:
  - The project-pipeline executes parallel groups CONCURRENTLY by default
  - Claude Code spawns one task agent per task in a group simultaneously
  - Independent tasks complete faster when run in parallel
  - Dependencies prevent race conditions and merge conflicts
  - Explicit grouping prevents accidental parallel execution of conflicting tasks
  - EVERY task MUST have parallel-group and parallel-safe metadata — no exceptions

DESIGN FOR PARALLELISM:
  - When decomposing work, PREFER creating independent tasks over sequential chains
  - Split by file/component ownership (e.g., one task per endpoint, per component)
  - Avoid tasks that touch the same files — this prevents parallelization
  - A project with no parallel groups will execute slowly (one task at a time)
  - Aim for at least 2-3 parallel groups per project phase
```

### Step 3.85: Assign `/goal` Wave-Completion Eligibility (REQUIRED — added 2026-07-08)

`/goal` (Claude Code v2.1.139+) keeps a session working turn-after-turn until a **separate transcript-only evaluator** (Haiku by default — it reads the conversation, runs NO commands, NO file reads) judges a completion condition met. It is the per-**wave** completion loop that removes the operator "continue" press between tasks. It is NOT a per-task mechanism and NOT a quality gate — Step 9.5 + the orchestrator remain the arbiter of whether work is *good*.

**Assign eligibility per wave (from the Step 3.8 wave structure), analogous to `<model-tier>`:**

```
FOR each wave in the Parallel Execution Plan:

  GOAL-ELIGIBLE = true IFF ALL of:
    (1) Machine-verifiable end-state exists — the wave's tasks prove done via transcript-visible
        output: tests pass / build exits 0 / lint clean / git status clean. (The evaluator can
        only see what the worker surfaces — no verifiable signal ⇒ not eligible.)
    (2) ≥3 tasks batched in the wave (enough sequential handoffs to be worth automating).
    (3) Tasks are well-specified — exhaustive <acceptance-criteria>, explicit <constraints>.
    (4) Low architectural-judgment / low-ambiguity (these should stop for human input, not loop).

  GOAL-ELIGIBLE = false IF ANY of:
    - Wave touches security / auth / secrets (tags: auth, security)
    - Wave is deployment-touching or otherwise irreversible (tags: deploy; azure-deployment.md scope)
    - Wave has likely breaking changes / expected ADR conflicts (escalation expected)
    - Exploratory / design / research tasks with no crisp end-state
    - Single-task or two-task wave (no batching benefit)

  IF GOAL-ELIGIBLE:
    COMPILE a by-reference condition (stay under the 4,000-char /goal cap — do NOT enumerate every
    criterion). Store it in TASK-INDEX.md next to the wave. Template:

      All of the following hold in this session:
      (1) Every task in Wave {N} ({id-list}) shows its acceptance criteria passing via transcript
          output — {the wave's verify command, e.g. "dotnet test exits 0" / "npm test passes"};
      (2) Each task's Step 9.5 gates (code-review + adr-check) have been RUN and their full findings
          surfaced in the transcript;
      (3) git status shows only the waves' expected file changes.
      OR: a BLOCKED.md exists under projects/{name}/ documenting a root-CLAUDE.md §6 escalation, shown in transcript.
      Stop after {N_tasks × 6} turns if neither state is reached.

  RECORD in TASK-INDEX.md Parallel Execution Plan:
    Wave 2 (parallel, 3 agents): 020, 021, 022 — prereq: Wave 1 — goal-eligible: YES
      goal-condition: "{compiled condition}"
    Wave 3 (sequential): 030 — touches .claude/ — goal-eligible: NO (single-task, .claude/ boundary)
```

**Why by-reference + capped:** the 4,000-char limit and the transcript-only evaluator mean the condition proves completion through the worker's *surfaced* test runs and gate outputs, not by re-listing criteria. The turn cap is the runaway guard. Escalation (a fired `<escalation>` trigger → `BLOCKED.md`) is a legitimate loop exit, never an error.

### Step 3.7: Add Mandatory Project Wrap-up Task (REQUIRED)

```
ALWAYS create a final "Project Wrap-up" task as the LAST task in the project.

Task ID: Use highest phase number + 90 (e.g., if last phase is 050, wrap-up is 090)
         This ensures wrap-up is always at the end regardless of task additions.

This task is MANDATORY for all projects and must include these steps:

  1. Run final quality gates:
     - /code-review on all project code (identifies remaining issues)
     - /adr-check on all project code (validates architecture compliance)
     - Fix any critical issues before proceeding

  2. Run repository cleanup:
     - /repo-cleanup projects/{project-name} (audits and cleans ephemeral files)
     - Review cleanup report
     - Approve removals (notes/debug/, notes/spikes/, notes/drafts/)
     - Archive handoffs if any (notes/handoffs/ → .archive/)

  3. Update README.md:
     - Set Status to "Complete"
     - Update Last Updated date
     - Set Phase to "Complete" and Progress to "100%"
     - Add Completed Date
     - Check all Graduation Criteria checkboxes
     - Add completion entry to Changelog

  4. Update plan.md:
     - Set Status to "Complete"
     - Update all milestone statuses to ✅

  5. Document lessons learned:
     - Append to notes/lessons-learned.md (the existing per-project convention — 51+ projects use it).
       One lesson per entry, one-line summary, record BOTH corrections and confirmed non-obvious
       approaches + why they mattered. Do NOT create a new central lessons store — durable,
       cross-project lessons are promoted into .claude/FAILURE-MODES.md or an ADR by
       doc-drift-audit / ai-procedure-maintenance at project close.
     - When authoring NEW tasks (Step 3.4), scan the project's notes/lessons-learned.md and
       .claude/FAILURE-MODES.md and reference any relevant lesson from the task's <knowledge>.

  6. Final verification:
     - All task files marked completed in TASK-INDEX.md
     - All documentation is current
     - No critical code-review issues remaining
     - Repository cleanup completed
```

### Step 4: Generate Task Files

For each task, create `tasks/{NNN}-{task-slug}.poml` as a **valid XML document**:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<task id="{NNN}" project="{project-name}">
  <metadata>
    <title>{Task Title}</title>
    <phase>{N} {Phase Name}</phase>
    <gate>startable | blocked | {dep-condition}</gate>       <!-- readiness -->
    <status>not-started</status>
    <rigor>{FULL | STANDARD | MINIMAL}</rigor>                <!-- Step 3.5.5 (author hint; task-execute may override) -->
    <rigor-reason>{Why this level - from Step 3.5.5 decision tree}</rigor-reason>
    <model-tier>{sonnet | opus | fable}</model-tier>         <!-- Step 3.5.5b; sonnet default -->
    <model-tier-reason>{Why this tier - from Step 3.5.5b}</model-tier-reason>
    <effort>{low | medium | high | xhigh}</effort>           <!-- from Step 3.5.5b effort rubric; default high -->
    <parallel-group>{A/B/C/... or "none" - from Step 3.8}</parallel-group>
    <parallel-safe>{true/false - can this run in parallel?}</parallel-safe>
    <parallel-reason>{why, when parallel-safe is false}</parallel-reason>
    <deps>{comma-separated task IDs or "none"}</deps>
    <tags>{context tags for Claude Code focus - see Standard Tag Vocabulary}</tags>
    <estimated-effort>{optional, e.g. 2-4 hours}</estimated-effort>
  </metadata>

  <!-- CANONICAL METADATA FIELD NAMES (reconciled 2026-07-16 to match live practice + the shared skeleton in
       .claude/templates/task-execution.template.md). Deprecated aliases accepted for back-compat but NOT emitted:
       <rigor-hint> → <rigor>; <dependencies> (metadata sibling) → <deps>. The <dependencies> element remains
       valid INSIDE <context> to describe prerequisite tasks. <blocks> is dropped — the DAG lives in TASK-INDEX.md. -->

  <prompt>
    {Natural language instruction for AI agent. 1-3 sentences describing 
    what needs to be accomplished and why. This is the "executable" part
    the operator hands to the AI.}
  </prompt>

  <role>
    SPAARKE platform developer. Follow ADRs strictly. {Add specific expertise
    needed: e.g., "Expert in ASP.NET Core Minimal APIs and Microsoft Graph SDK."}
  </role>

  <goal>
    {Clear, measurable definition of done. What artifact(s) will exist when complete?}
  </goal>

  <context>
    <background>
      {Why this task exists - business context from plan.md/spec.md}
    </background>
    <relevant-files>
      <file>{path to file to create/modify}</file>
      <file>{path to related file}</file>
    </relevant-files>
  </context>

  <constraints>
    <constraint source="ADR-{NNN}">{Constraint text}</constraint>
    <constraint source="ADR-{NNN}">{Another constraint}</constraint>
    <constraint source="project">{Project-specific constraint}</constraint>
  </constraints>

  <knowledge>
    <files>
      <file>docs/adr/ADR-{NNN}-{name}.md</file>
      <file>{path to relevant knowledge article}</file>
    </files>
    <patterns>
      <pattern name="{pattern name}" location="{file path}">
        {Brief description of pattern to follow}
      </pattern>
    </patterns>
  </knowledge>

  <steps mode="directional">  <!-- directional (default) = goal+criteria+constraints are binding, executor may adapt sequence;
                                     prescriptive = exact sequence binding (migrations/deploys/irreversible) — see Step 3.5.5c -->
    <step order="1">{First concrete action}</step>
    <step order="2">{Second concrete action}</step>
    <step order="3">{Continue until task is complete}</step>
    <step order="N-2">Run tests and verify all pass</step>
    <step order="N-1">Update TASK-INDEX.md: change this task's status to ✅ completed</step>
    <step order="N">If any deviations from plan, document in projects/{project-name}/notes/</step>
  </steps>

  <escalation>  <!-- OPTIONAL but REQUIRED for tasks with a known failure mode / judgment boundary (Step 3.5.5c) -->
    <trigger>{Concrete trip-wire: "If X differs from the spec, STOP and escalate per CLAUDE.md §6 rather than adapting."}</trigger>
  </escalation>

  <tools>
    <tool name="dotnet">Build and test .NET projects</tool>
    <tool name="npm">Build TypeScript/PCF projects</tool>
    <tool name="terminal">Run shell commands</tool>
  </tools>

  <outputs>
    <output type="code">{exact path to code file}</output>
    <output type="test">{exact path to test file}</output>
    <output type="docs">{exact path to doc file if any}</output>
  </outputs>

  <acceptance-criteria>
    <criterion testable="true">
      Given {precondition}, when {action}, then {expected result}.
    </criterion>
    <criterion testable="true">
      {Another testable criterion}
    </criterion>
    <criterion testable="true">All unit tests pass.</criterion>
  </acceptance-criteria>

  <notes>
    {Implementation hints, gotchas to avoid, references to spec.md sections}
  </notes>

  <execution>
    <skill>.claude/skills/task-execute/SKILL.md</skill>
    <protocol>
      Before starting this task, load all files listed in <knowledge><files>.
      Follow the task-execute skill for mandatory pre-execution checklist.
    </protocol>
  </execution>
</task>
```

### Step 5: Update Project Files
```
UPDATE projects/{project-name}/CLAUDE.md:
  - Add task count and phase mapping
  - Update "Next Action" to reference first task

CREATE tasks/TASK-INDEX.md:
  | ID | Title | Phase | Status | Dependencies | Parallel |
  |----|-------|-------|--------|--------------|----------|
  | 001 | ... | 1 | 🔲 | none | — |
  | 002 | ... | 1 | 🔲 | 001 | — |
  | 020 | ... | 2 | 🔲 | 010 | Group A |
  | 021 | ... | 2 | 🔲 | 010 | Group A |
  ...

  ADD "Parallel Execution Groups" section:
  ```markdown
  ## Parallel Execution Groups

  Tasks in the same group can run simultaneously once prerequisites are met.

  | Group | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
  |-------|-------|--------------|---------------|---------------------|
  | A | 020, 021, 022 | 010 ✅ | Separate endpoints | ✅ Yes |
  | B | 031, 032 | 030 ✅ | Separate components | ✅ Yes |

  **How to Execute Parallel Groups:**
  1. Check all prerequisites are complete (✅ in Status)
  2. Invoke Task tool with multiple subagents in ONE message
  3. Each subagent runs task-execute for one task
  4. Wait for all to complete before next group
  ```
```

### Step 6: Output Summary
```
✅ Tasks created for: projects/{project-name}/

Task breakdown:
  Phase 1: {n} tasks (001-00{n})
  Phase 2: {m} tasks (010-01{m})
  ...
  Wrap-up: 1 task (090-project-wrap-up)
  Total: {total} tasks

Rigor level distribution:
  FULL: {count} tasks (code implementation, architecture changes)
  STANDARD: {count} tasks (tests, new files, constraints)
  MINIMAL: {count} tasks (documentation, inventory)

Task list with rigor levels:
  001 - {title} (FULL - {reason})
  002 - {title} (STANDARD - {reason})
  003 - {title} (MINIMAL - {reason})
  ...
  090 - Project Wrap-up (FULL - code-review + adr-check + repo-cleanup)

Files created:
  - tasks/TASK-INDEX.md
  - tasks/001-{slug}.poml
  - tasks/002-{slug}.poml
  ...
  - tasks/090-project-wrap-up.poml  # MANDATORY final task

Execution order recommendation:
  1. Start with task 001 (no dependencies)
  2. ...
  N. End with task 090 (project-wrap-up) - updates README, plan, documents completion

Run /task-execute 001 to begin implementation.
```

## Conventions

### File Naming
- Format: `{NNN}-{task-slug}.poml`
- Extension: `.poml` (valid XML document)
- Slug: 3-5 words, kebab-case (e.g., `setup-redis-connection`)
- Numbers: 3 digits, zero-padded (001, 010, 100)

### Standard Tag Vocabulary (for `<tags>` element)

Use these standardized tags in the `<metadata><tags>` element to help Claude Code focus context:

| Category | Tags | Purpose |
|----------|------|---------|
| **API/Backend** | `bff-api`, `api`, `backend`, `minimal-api`, `endpoints` | BFF API development |
| **Frontend/PCF** | `pcf`, `react`, `typescript`, `frontend`, `fluent-ui` | PCF control development |
| **Dataverse** | `dataverse`, `solution`, `fields`, `plugin`, `ribbon` | Dataverse customization |
| **Azure** | `azure`, `app-service`, `azure-ai`, `azure-search`, `bicep` | Azure infrastructure |
| **AI/ML** | `azure-openai`, `ai`, `embeddings`, `document-intelligence` | AI features |
| **Operations** | `deploy`, `ci-cd`, `devops`, `infrastructure` | Deployment tasks |
| **Quality** | `testing`, `unit-test`, `integration-test`, `e2e-test` | Testing tasks |
| **Refactoring** | `refactoring`, `rename`, `restructure`, `migration` | Code restructuring |
| **Configuration** | `config`, `options`, `di`, `settings` | Configuration changes |

**Usage in POML:**
```xml
<metadata>
  <tags>bff-api, refactoring, services</tags>  <!-- Task 001: rename service -->
  <tags>pcf, react, frontend, fluent-ui</tags>  <!-- Task 013: update panel -->
  <tags>deploy, dataverse, pcf</tags>           <!-- Task 015: deploy PCF -->
</metadata>
```

**Context Loading Benefit:**
When Claude Code starts a task with `<tags>bff-api, services</tags>`, it can:
1. Load `src/server/api/CLAUDE.md` for BFF context
2. Reference `dataverse-deploy` skill for deployment
3. Skip loading PCF-specific context (saving tokens)

### Task Sizing
| Granularity | Hours/Task | Tasks/Phase |
|-------------|------------|-------------|
| fine | 1-2 | 5-10 |
| medium | 2-4 | 3-7 |
| coarse | 4-8 | 2-4 |

Default to "medium" unless user specifies otherwise.

### POML Tag Requirements
Every task file MUST have these POML sections (valid XML):
- `<task>` - Root element with id and project attributes
- `<metadata>` - title, phase, gate, status, **rigor** (+ rigor-reason), **deps**, **tags** (canonical names; `<deps>` not `<dependencies>`)
- `<prompt>` - Natural language task instruction for AI agent
- `<role>` - Persona/expertise for the AI to adopt
- `<goal>` - Clear definition of done
- `<context>` - Background and relevant files
- `<constraints>` - With explicit ADR source attributes
- `<steps>` - Ordered steps with order attribute
- `<outputs>` - Exact file paths with type attribute
- `<acceptance-criteria>` - Testable criteria with testable="true" attribute

Required metadata (added 2026-07-08 for Sonnet-5 execution):
- `<rigor>` + `<rigor-reason>` - rigor level (Step 3.5.5; canonical name — `<rigor-hint>` deprecated)
- `<model-tier>` + `<model-tier-reason>` - execution tier (Step 3.5.5b)
- `<effort>` - low/medium/high/xhigh (Step 3.5.5b effort rubric; default `high`)
- `<parallel-group>` + `<parallel-safe>` - wave grouping (Step 3.8; EVERY task)
- `<steps mode="...">` - `directional` (default) or `prescriptive` (Step 3.5.5c)

Recommended sections:
- `<knowledge>` - ADRs, patterns, and reference files (REQUIRED if task has tags - see Tag-to-Knowledge Mapping)
- `<tools>` - Available tools for execution
- `<notes>` - Implementation hints
- `<execution>` - Reference to task-execute skill and pre-execution protocol
- `<escalation>` - Concrete stop-and-escalate trigger (REQUIRED for tasks with a known failure mode / judgment boundary — Step 3.5.5c)
- `<ui-tests>` - Browser-based UI tests for PCF/frontend tasks (REQUIRED if tags include: pcf, frontend, fluent-ui, e2e-test)

### Status Values
- `not-started` - Initial state
- `in-progress` - Currently being worked
- `blocked` - Waiting on dependency or external input
- `completed` - All deliverables and criteria met
- `deferred` - Postponed (with reason in notes)

## Resources

### Templates (Auto-loaded)
- `.claude/templates/task-execution.template.md` — **POML skeleton (pointer only)**. It mirrors this skill's Step 4 block for copy-paste; the AUTHORITATIVE field set + per-field decision logic is THIS skill (Steps 3.5.5 / 3.5.5b / 3.5.5c / 3.5.6 / 3.8 / 3.65 / 3.85). Keep the two in sync (`ai-procedure-maintenance` Checklist F).

### Related Skills
- **project-init**: Creates plan.md that this skill consumes
- **task-execute**: Runs individual tasks (calls ui-test in Step 9.7)
- **ui-test**: Executes browser-based UI tests defined in task `<ui-tests>` sections
- **project-pipeline**: Orchestrates spec → setup → create → execute

## Examples

See [`examples/examples.md`](examples/examples.md) for 3 worked examples:
- **Example 1**: Decomposing the SDAP Refactor plan into 14 POML files across 4 phases
- **Example 2**: Fine-grained decomposition (15 tasks vs 8, each ~1-2 hours)
- **Example 3**: Handling missing WBS in plan.md (graceful failure with recovery prompt)

---

## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| Generated POML files have empty `<knowledge><files>` blocks | Tag-to-knowledge mapping (Step 3.5.5) didn't match the task's tags | Verify the mapping table covers the task's tags. If a tag has no mapping, add one OR change the task to use a known tag. The mapping is the connective tissue between tasks and ADRs/constraints. |
| TASK-INDEX.md missing parallel-group annotations | Step 3.8 (parallel grouping) skipped or incomplete | Always run Step 3.8 — it identifies independent tasks that can run in parallel, which is critical for efficient execution (per project-pipeline orchestration). |
| Task POMLs lack `<ui-tests>` for PCF/frontend tasks | Step 3.65 (UI test annotation) skipped | PCF/frontend tasks MUST include `<ui-tests>` sections so `task-execute` Step 9.7 can invoke `ui-test` skill against them. The skill body Step 3.65 covers the format. |
| Generated task numbering collides with existing tasks | Author re-ran task-create over an existing tasks/ directory without merge logic | task-create is idempotent ON FIRST RUN but doesn't merge. If re-running, delete existing `tasks/` first OR manually merge. Don't overwrite without confirmation. |
| External callers (e.g., `task-execute` referencing Step 3.5.5) silently break | Step numbers in Workflow body got renumbered during refinement | **Step number scaffolding is external API** (callers cite by number). NEVER renumber Step 3.5.5, 3.65, 3.8, etc. without coordinating with all callers (verified post-edit via `grep -rn "Step 3.5"`). |

## Validation Checklist

Before completing task-create, verify:
- [ ] TASK-INDEX.md created with all tasks listed
- [ ] Each task file has all required POML sections
- [ ] Task numbers follow gap convention (001, 010, 020...)
- [ ] Dependencies form a valid DAG (no circular refs)
- [ ] First task(s) have no unmet dependencies
- [ ] Acceptance criteria are copy/referenced from plan.md
- [ ] Project CLAUDE.md updated with task summary
- [ ] PCF/frontend tasks have `<ui-tests>` sections (Step 3.65)
- [ ] UI tests include dark mode compliance check for Fluent UI tasks (ADR-021)
- [ ] Each task has `<rigor>` and `<rigor-reason>` in metadata (Step 3.5.5) — canonical field is `<rigor>`, not the deprecated `<rigor-hint>`
- [ ] Rigor levels match task characteristics (FULL for code, STANDARD for tests, MINIMAL for docs)
- [ ] Each task has `<model-tier>` + `<model-tier-reason>` + `<effort>` in metadata (Step 3.5.5b); `xhigh` only where the effort rubric justifies it (not blanket)
- [ ] Each task's `<steps>` has an explicit `mode` (directional default; prescriptive for migrations/deploys) (Step 3.5.5c)
- [ ] Tasks with a known failure mode / judgment boundary carry an `<escalation><trigger>` (Step 3.5.5c)
- [ ] Constraints are scoped explicitly and acceptance criteria are a closed set incl. negative/authorization cases (Authoring for literal execution)
- [ ] Frontend tasks anchor visual direction concretely — pattern ref or explicit spec, not "clean and modern" (Step 3.65)
- [ ] Parallel groups identified and documented (Step 3.8)
- [ ] Each task has `<parallel-group>` and `<parallel-safe>` in metadata
- [ ] TASK-INDEX.md includes "Parallel Execution Groups" section
- [ ] No tasks in same parallel group modify the same files

### Completeness Lint (REQUIRED — added 2026-07-16 to prevent silent metadata drift)

**Run this mechanical check on every generated POML before declaring task-create complete.** It is the forcing
function the 2026-07-16 template-drift finding recommended (rec C): the omissions it catches are silent otherwise —
a well-formed POML missing `<model-tier>` just falls back to a default at dispatch, and nothing flags it.

```
FOR each tasks/*.poml:
  REQUIRE present + non-empty: <model-tier>, <effort>, <rigor>, <parallel-group>, <parallel-safe>
  REQUIRE <steps> carries an explicit mode="directional|prescriptive"
  IF the task adds NEW surface (new .cs/.ts/.tsx file, endpoint, DI registration, package,
     Dataverse column, or a <relevant-files> entry with role="new"):
     REQUIRE a non-hollow <justification> (existing / extension / cost-of-doing-nothing) — Step 3.5.6
  IF tags include pcf|frontend|fluent-ui|e2e-test: REQUIRE <ui-tests>
  REJECT the deprecated <rigor-hint> / metadata-sibling <dependencies> field names (use <rigor> / <deps>)
FAIL the checklist (do not report task-create complete) if any POML is missing a required field.
```

- Automatable via `scripts/Validate-TaskPoml.ps1` (run `pwsh scripts/Validate-TaskPoml.ps1 projects/{name}/tasks`).
- The same gate runs again at PR time in `code-review` (Step 6.7 — POML completeness) so a hand-edited task can't
  slip an incomplete POML past review.

---

## Portfolio Hook (added 2026-06-23 by spaarke-devops-project-tracking-r1 task 032 · FR-18)

**At end of skill** (after all task POMLs scaffolded): invoke `/devops-project-sync` to set the `Task Count` field on the Project Issue.

`Task Count = ls projects/{name}/tasks/*.poml | wc -l` (computed by /devops-project-sync from local state).

Silent on success. Hook is a no-op if `projects/{name}/README.md` lacks the portfolio pointer block (project not registered).

See: [`.claude/skills/devops-project-sync/SKILL.md`](../devops-project-sync/SKILL.md).
