---
name: task-execute
description: Execute a POML task file with proper context loading, ADR awareness, proactive checkpointing, and rigor-level decision tree — the load-bearing task-execution protocol for every project
tags: [tasks, execution, context, knowledge, rigor-levels, protocol]
techStack: [all]
appliesTo: ["execute task", "run task", "start task", "work on task", "continue", "next task", "resume task", "pick up where I left off"]
alwaysApply: false
exemplar: none-too-volatile
last-reviewed: 2026-11-03
length-justification: Every step inline here (rigor tree, Steps 0–11, the Step 9.5 finding-triage rules) runs on every project task and must be read whole; branches that apply only sometimes are in references/.
---

# task-execute

> **Read this file in full.** It is long on purpose: every section below runs on every project task, and a step skipped because it sat out of view is the failure this skill exists to prevent. Branches that apply only sometimes are in `references/`, each behind an "If …, read …" line at the exact point it applies — when you reach one whose condition holds, open the file before continuing.

> **Category**: Project Lifecycle
> **Last Reviewed**: 2026-11-03
> **Exemplar rationale**: Task execution is per-task and per-project; no canonical snapshot holds. The step protocol + rigor decision tree IS the contract.

## Contents

| Section | What it covers | When it applies |
|---|---|---|
| Purpose / When to Use | What this skill guarantees; triggers | Always |
| Step 0.3 | Multi-task (wave) detection | Every invocation; the wave procedure only for multi-task runs → `references/parallel-waves.md`, `/goal` → `references/goal-wave-loop.md` |
| Step 0.5 | Hot-path check, rigor level, model tier/effort, step mode, declaration | Every task, before Step 0 |
| Step 0 | Size guard, registration guard, resume after compaction | Every task |
| Steps 1–3 | Load POML, initialize `current-task.md`, context budget | Every task |
| Steps 4–7 | Knowledge, constraints, patterns, ADRs, skills, scripts, module CLAUDE.md | Every task, depth by rigor; surface checklists → `references/surface-checklists.md` |
| Step 8 / 8.0 | Execute steps with tracking; delegation decision | Every task; delegation procedure → `references/multi-file-decomposition.md` |
| Step 8.5 | Checkpoint triggers | Every task (FULL/STANDARD) |
| Step 9 / 9.5 | Acceptance criteria; quality gates; **finding triage and review scope** | Every task; 9.5 at FULL and for any test-modifying task |
| Step 9.6 | Portfolio sync | Every task completion |
| Step 9.7 | UI testing | PCF/frontend tasks → `references/ui-testing.md` |
| Steps 10–11 | Status + drift check; script library; master sync; transition; test-diet gate; final merge prompt | Every task; script maintenance → `references/script-library-maintenance.md` |
| Gotchas | Failure modes seen in practice | Always |
| Related | Other skills and docs | Lookup |

Worked examples (task run, checkpoint flow, recovery): `examples/worked-examples.md`.

---

## Purpose

Execute a single POML task file with **mandatory context loading** and **persistent state tracking**: read every knowledge file the task names, follow the ADRs, apply skills before implementation, and keep `current-task.md` recoverable across compaction.

- **Critical**: this skill prevents the common failure where implementation starts before the knowledge files referenced in the task are loaded.
- **Context persistence**: progress is tracked in `projects/{project}/current-task.md` so work continues after compaction or a new session.
- **Proactive checkpointing**: Claude MUST checkpoint at the Step 8.5 triggers using the `context-handoff` skill.

## When to Use

- User says "execute task 013", "work on task 013", "continue", "next task", "resume task X", "pick up where I left off"
- User provides a task file path
- Continuing work on a project task, or resuming after compaction or a new session
- **Always use this protocol** when working on any POML task (root CLAUDE.md §4)

---

## Execution Protocol (MANDATORY STEPS)

Run the steps in this order: 0.3 → 0.5 → 0 → 1 … 11. Step ids are cited by root CLAUDE.md, other skills and POMLs — never renumber them.

### Step 0.3: Parallel Execution Detection

Runs only when task-execute is invoked for **more than one task at once** (e.g. "work on Phase 2 tasks", or from `project-pipeline` Step 5). For a single task, go to Step 0.5.

**If invoked for more than one task, read `references/parallel-waves.md` before continuing.** It holds the wave procedure, the concurrency cap, the mandatory build verification between waves, and failure isolation. Rules that hold regardless:
- Parallel-safe tasks in one wave are dispatched in ONE message with several Agent calls; a task marked `<parallel-safe>false</parallel-safe>` (e.g. it touches `.claude/`) runs sequentially in the main session (root CLAUDE.md §3).
- A failed build after a wave stops the run: report it and do not start the next wave.

**If the operator has run `/goal` for this wave (a `goal-eligible` wave from `task-create` Step 3.85), read `references/goal-wave-loop.md` before continuing.** Goal achievement never replaces Step 9.5.

---

### Step 0.5: Determine Required Rigor Level (MANDATORY)

**Pre-step — Hot-Path Auto-Invoke check (binding for every task):**

1. Read the task POML's `<inputs>`, `<outputs>`, and `<tags>` sections.
2. Cross-reference against the Hot-Path Watchlist in [`.claude/skills/conflict-check/SKILL.md`](../conflict-check/SKILL.md) (BFF, BFF entry/DI, SpaarkeAi, ci-workflows, skill-directives, root-CLAUDE.md).
3. **If ANY watchlist trigger fires** (a POML output/input file matches a watchlist glob, OR task tags include `bff-api`/`spaarke-ai`/`ci-workflows`/`skill-directive`/`root-claude`), invoke `/conflict-check` immediately and read its output before the rigor decision below.
4. If `/conflict-check` reports a hard warn (file overlap with another active worktree), pause and escalate to the user. Soft warns and silent passes proceed normally.
5. **`projects/INDEX.md` maintenance (root CLAUDE.md §10)**: if this task is the project's FIRST touch of a hot-path surface (BFF / SpaarkeAi / ci-workflows / skill-directives / root-CLAUDE) and the project's row in [`projects/INDEX.md`](../../../projects/INDEX.md) does not yet show it, update the row (main-session-only; the registry is maintained here and by `project-pipeline` Step 2, no cron).

The check takes seconds; it prevents concurrent edits on the same hot files.

**Then determine rigor level.** FIRST read the POML's authored `<rigor>` field (canonical name; accept the deprecated `<rigor-hint>` as an alias) — it is `task-create` Step 3.5.5's considered hint. Validate it against the tree below. Use the authored `<rigor>` unless the tree clearly warrants a HIGHER level (e.g. the POML says STANDARD but the task modifies `.cs`/`.ts`/`.tsx` or has `bff-api`/`auth` tags → override UP to FULL and say so). Never silently override DOWN. If the POML has no `<rigor>`, compute from the tree.

```
FULL — if the task has ANY of these (all protocol steps mandatory; report after EACH step):
  - Tags include: bff-api, api, pcf, plugin, auth
  - Will modify code files (.cs, .ts, .tsx)
  - Has 6+ steps in task definition
  - Resuming after compaction/new session
  - Task description includes: "implement", "refactor"
  - Dependencies on 3+ other tasks

ELSE STANDARD — if it has ANY of these (skip pattern loading; report major steps):
  - Tags include: testing, integration-test
  - Will create new files
  - Has constraints or ADRs listed
  - Phase 2.x or higher (integration phase)

ELSE MINIMAL — documentation/inventory tasks only.
```

#### Rigor Level Protocol Requirements

| Protocol Step | FULL | STANDARD | MINIMAL |
|--------------|------|----------|---------|
| Step 0: Context Recovery | ✅ Required | ✅ Required | ✅ Required |
| Step 1: Load Task File | ✅ Required | ✅ Required | ✅ Required |
| Step 2: Initialize current-task.md | ✅ Required | ✅ Required | ✅ Required |
| Step 3: Context Budget Check | ✅ Required | ✅ Required | ✅ Required |
| Step 4: Load Knowledge Files | ✅ All files | ✅ Explicit only | ⏭️ Skip |
| Step 4a: Load Constraints by Tag | ✅ Required | ✅ Required | ⏭️ Skip |
| Step 4b: Load Patterns by Tag | ✅ Required | ⏭️ Skip | ⏭️ Skip |
| Step 5: Load ADR Constraints | ✅ Required | ✅ If listed | ⏭️ Skip |
| Step 6: Apply Always-Apply Skills | ✅ Required | ⏭️ Optional | ⏭️ Skip |
| Step 6.5: Load Script Context | ✅ Required | ✅ If deploy/test | ⏭️ Skip |
| Step 7: Review CLAUDE.md Files | ✅ Required | ⏭️ Skip | ⏭️ Skip |
| Step 8: Execute Steps | ✅ Track all | ✅ Track major | ✅ Execute |
| Step 8.5: Checkpointing | ✅ Every 3 steps | ✅ After a deploy, before a risky step, or when context is reported high | ⏭️ Skip |
| Step 9: Verify Acceptance | ✅ Required | ✅ Required | ✅ Required |
| Step 9.5: Quality Gates | ✅ Required | ⏭️ Skip † | ⏭️ Skip † |
| Step 10: Update Task Status | ✅ Required | ✅ Required | ✅ Required |
| **Reporting Frequency** | After each step | After major steps | Start + end only |

† **Except** a task that modifies `tests/**` or is tagged `testing`, `test-reset`, `deletion` or `integration-test`: Step 9.5 is required at every level (root CLAUDE.md §8; Step 9.5 "Binding override").
Steps not in the table (9.6, 9.7, 10.5, 10.6, 11) run at every level whenever their own condition holds.

#### MANDATORY RIGOR LEVEL DECLARATION

At task start, output:

```
🔒 RIGOR LEVEL: [FULL | STANDARD | MINIMAL]
📋 REASON: [Why this level was chosen based on decision tree]
🔧 MODEL TIER: [sonnet | opus | fable] @ effort [low|medium|high|xhigh] (from POML <model-tier> + <effort>; defaults sonnet @ high)
🧭 STEP MODE: [directional | prescriptive] (from POML <steps mode="…">)

📖 PROTOCOL STEPS TO EXECUTE:
  [List all steps that will be executed for this rigor level]

Proceeding with Step 0...
```

This declaration is **non-negotiable** and makes shortcuts visible.

#### Model Tier + Effort

Execution defaults to the tier and effort in root CLAUDE.md §8.5 (Sonnet @ `high`). Each POML carries a `<model-tier>` (`sonnet` default; `opus`/`fable` for high-blast-radius / architectural / ADR-migration / security tasks) and an `<effort>` (`high` default; `xhigh` only for brownfield/root-cause or complex-but-fully-specified work — `task-create` Step 3.5.5b). **Honor the declared `<effort>`; do not run every task at `xhigh`** — at `xhigh` a Sonnet run costs close to Opus.

- **Parallel execution** (`project-pipeline` Step 5): the dispatcher spawns each task's subagent with `model = <model-tier>`. Effort comes from the agent definition (e.g. `implementer`) or a workflow `agent()` option, not the call (`.claude/constraints/agent-cost.md`, "How effort is set"). Declare the effort you actually run at, not the POML's, when they differ.
- **Serial / main-session execution**: honor `<effort>` for the session. If the **current session model is lower** than the task's `<model-tier>`, STOP and surface: *"🔔 This task is flagged `<model-tier>` — switch the session model (/model) or run it as an Opus/Fable subagent before proceeding."* Do not silently execute a top-tier task on a lower model.
- Keep the FULL-rigor quality gates (Step 9.5) **unconditional** — they are the safety net for subtle ADR slips a lower-tier model is likelier to miss.

#### Step Mode

Honor the POML `<steps mode="…">`:
- **`directional`** (default): `<steps>` are guidance; the binding contract is `<goal>` + `<acceptance-criteria>` + `<constraints>`. You MAY adapt the sequence to the real codebase state — if step 3 is wrong for what you find, do the right thing and note the deviation. Do NOT robotically execute a step you can see is wrong.
- **`prescriptive`**: the exact sequence is binding (migrations, deployments, anything irreversible). Follow it exactly; a needed deviation is an **escalation** (root CLAUDE.md §6), not a silent improvisation.

If the POML carries an `<escalation><trigger>`, firing that trigger is a first-class outcome: STOP and escalate per root CLAUDE.md §6 (inside a `/goal` wave loop, write `BLOCKED.md` per `references/goal-wave-loop.md`). Without the trigger, the executor will push toward *a* completion; the trigger is what makes stopping legitimate.

#### Execution hygiene

Do not pad execution with ritual: skip "double-check everything after every step" self-talk and forced interim progress summaries. **Keep** the verification that produces an artifact or a gate decision: Step 9 acceptance checks, Step 9.5 quality gates, and the test/build runs the task names. Rule: keep verification that yields an artifact or a gate decision; drop verification that only yields reassurance.

#### User Override

- **"Execute with FULL protocol"** → forces all steps regardless of task type
- **"Execute with MINIMAL protocol"** → use carefully, only for documentation
- **Default:** auto-detect using the decision tree above

#### Audit Trail

Log rigor level and reason in current-task.md:

```markdown
**Rigor Level:** FULL
**Reason:** Task tags include 'bff-api' (code implementation)
**Protocol Steps Executed:**
- [x] Step 0.5: Determined rigor level
- [x] Step 1: Load Task File
[... etc]
```

---

### Step 0: Context Recovery Check

```
SIZE GUARD (every start, before reading):
  IF current-task.md > 20 KB:
    → It is carrying history, not state. Do NOT load it whole.
    → Read only its newest block, then clean it up per context-handoff
      "State, not history" (archive verbatim to notes/handoff-history/,
      move standing items to project CLAUDE.md, rewrite as current state)
      BEFORE starting the task.

REGISTRATION GUARD (every start):
  IF projects/{project-name}/README.md has no `> **Portfolio**:` pointer block:
    → The project is not registered on GitHub (Board #2). STOP before the task.
    → Run project-pipeline Step 4.2 (register: /devops-project-register --epic <#E>,
      project label, verify), commit the pointer block, then continue the task.
    → Ask the operator only for the Epic, if design.md/spec.md does not name it.

IF resuming work (new session, post-compaction, "where was I?"):
  READ projects/{project-name}/current-task.md (Quick Recovery section first)
  READ the project CLAUDE.md standing-rule sections (if present): "Standing directives & gotchas",
    or the template's "Binding rules", "Owner directives and standing decisions", "Gotchas"
  VERIFY the branch / worktree matches the project before trusting current-task.md

  IF current-task.md exists AND status == "in-progress" for this task:
    → This is a continuation
    → EXTRACT: completed_steps, files_modified, next_step
    → RELOAD context before resuming — compaction dropped it: Step 1 (POML), then
      Steps 4–7 at this task's rigor level (knowledge, constraints, ADRs, skills)
    → RESUME at next_step; do not redo completed steps
    → REPORT: "✅ Context recovered — Task {id}: {title}. Completed: Steps 1-{N}.
               Resuming: Step {N+1} - {description}."

  IF current-task.md shows a different task:
    → WARN user: "current-task.md shows task {X}, you requested task {Y}"
    → ASK: "Switch to task {Y}? This will update current-task.md"
```

Full recovery procedure (multiple worktrees, missing state): `docs/procedures/context-recovery.md`; the `context-handoff` skill's "Post-Compaction Recovery" owns the checkpoint format.

### Step 1: Load Task File

```
READ the task .poml file
VERIFY each <knowledge><files> path resolves; a broken path is a bad POML —
  fix the POML (or report it), do not silently skip the file
EXTRACT:
  - <metadata><tags> for context focus
  - <knowledge><files> for required reading
  - <constraints> for rules to follow
  - <steps> for execution sequence
  - <metadata><title> for display
  - <metadata><phase> for context
```

### Step 2: Initialize/Update current-task.md

```
LOAD projects/{project-name}/current-task.md

IF file doesn't exist:
  CREATE from template: .claude/templates/current-task.template.md

UPDATE current-task.md:
  - Task ID: {extracted from filename}
  - Task File: {relative path to .poml}
  - Title / Phase: {from metadata}
  - Status: "in-progress"
  - Started: {current timestamp}
  - Clear previous completed_steps and files_modified (new task)

This file is now the source of truth for recovery.
```

### Step 3: Context Budget Check

Claude cannot measure its own context usage (root CLAUDE.md §5), so this check keys on events:

```
IF the user or harness has reported that context is high, or a compaction notice has appeared:
  → RUN context-handoff (it rewrites current-task.md with full state)
  → REPORT: "Context reported high. State saved to current-task.md — ready for /compact."
  → Continue after compaction from current-task.md (the SessionStart hook re-injects it)

IF this task will load a large file set (many knowledge files, big logs, a wide diff):
  → Checkpoint BEFORE loading it
```

### Step 4: Load Knowledge Files (MANDATORY)

**This step is NOT optional. Do NOT skip to implementation.**

```
FOR each file in <knowledge><files>:
  READ the file completely
  EXTRACT key rules, patterns, constraints

FOR each pattern in <knowledge><patterns>:
  READ the referenced file
  UNDERSTAND the pattern to follow
```

#### 4a. Load Constraints by Tag (MANDATORY)

| Task Tags | Load Constraints File |
|-----------|----------------------|
| `bff-api`, `api`, `minimal-api`, `endpoints` | `.claude/constraints/api.md` |
| `pcf`, `react`, `fluent-ui`, `frontend` | `.claude/constraints/pcf.md` |
| `dataverse`, `plugin`, `solution` | `.claude/constraints/plugins.md` |
| `auth`, `oauth`, `authorization` | `.claude/constraints/auth.md` |
| `cache`, `redis`, `data` | `.claude/constraints/data.md` |
| `ai`, `azure-openai`, `document-intelligence` | `.claude/constraints/ai.md` |
| `worker`, `job`, `background` | `.claude/constraints/jobs.md` |
| `testing`, `unit-test`, `integration-test` | `.claude/constraints/testing.md` |
| `config`, `feature-flag` | `.claude/constraints/config.md` |
| `provisioning`, `provisioning-run`, `l2-controlplane`, `provisioning-handler`, `customer-provisioning` | `.claude/constraints/provisioning.md` |

#### 4b. Load Patterns by Tag (RECOMMENDED; required at FULL)

| Task Tags | Load Pattern Files |
|-----------|-------------------|
| `bff-api`, `api` | `.claude/patterns/api/endpoint-definition.md`, `.claude/patterns/api/endpoint-filters.md` |
| `pcf`, `react` | `.claude/patterns/pcf/control-initialization.md`, `.claude/patterns/pcf/theme-management.md` |
| `auth`, `oauth` | `.claude/patterns/auth/spaarke-sso-binding.md` (canonical v2), `.claude/patterns/auth/obo-flow.md`, `.claude/patterns/auth/oauth-scopes.md` + ADR-028 + `docs/guides/auth-deployment-setup.md` |
| `dataverse`, `plugin` | `.claude/patterns/dataverse/plugin-structure.md` |
| `cache` | `.claude/patterns/caching/distributed-cache.md` |
| `testing` | `.claude/patterns/testing/unit-test-structure.md`, `.claude/patterns/testing/mocking-patterns.md` |
| `provisioning`, `provisioning-run`, `l2-controlplane`, `provisioning-handler` | `.claude/patterns/provisioning/INDEX.md` (dispatches to the provisioning pattern files) |

#### 4c. Common Knowledge Files by Tag

```
ADDITIONAL MODULE-SPECIFIC FILES:
  - pcf tags → READ src/client/pcf/CLAUDE.md
  - pcf tags → READ docs/guides/PCF-DEPLOYMENT-GUIDE.md
  - bff-api tags → READ src/server/api/Sprk.Bff.Api/CLAUDE.md
  - dataverse tags → READ .claude/skills/dataverse-deploy/SKILL.md
  - deploy tags → READ the matching deploy skill: pcf-deploy, bff-deploy, code-page-deploy or dataverse-deploy

UPDATE current-task.md:
  - "Knowledge Files Loaded", "Constraints Loaded", "Patterns Loaded" sections with paths
```

**If the task's tags include `pcf`, `react`, `fluent-ui`, `bff-api`, `api`, `minimal-api` or `deploy`, read `references/surface-checklists.md` before continuing — at every rigor level, even where the rigor table skips the rest of Step 4.** It adds the surface's required reads and the completion checks you tick at Step 9 (PCF version bump and `build:prod`; BFF placement justification, publish-size delta and CVE check; deployment verification).

### Step 5: Load ADR Constraints (Two-Tier)

```
FOR each <constraint source="ADR-XXX">:
  TIER 1 (default): READ .claude/adr/ADR-XXX-*.md
    - Concise AI-optimized versions with the MUST/MUST NOT rules; enough for most tasks
  TIER 2 (if a rule is unclear or you need the rationale): READ docs/adr/ADR-XXX-*.md

UPDATE current-task.md:
  - "Applicable ADRs" section with ADR numbers and relevance
```

### Step 6: Apply Always-Apply Skills

```
LOAD .claude/skills/adr-aware/SKILL.md
LOAD .claude/skills/spaarke-conventions/SKILL.md
LOAD .claude/skills/script-aware/SKILL.md
```

### Step 6.5: Load Script Context (deployment/testing tasks)

```
IF task tags include: deploy, test, validate, automation, setup
  OR task steps mention: deployment, testing, validation, health check
THEN:
  READ scripts/README.md
  IDENTIFY relevant scripts; use them instead of writing new automation
  UPDATE current-task.md: "Available Scripts" section

COMMON SCRIPT MATCHES:
  - PCF build → Invoke-PcfBuildProd.ps1 (build:prod + fails on a failed build); deploy via the pcf-deploy skill
  - API testing → Test-SdapBffApi.ps1;  Health checks → test-sdap-api-health.js
  - Custom page deploy → Deploy-CustomPage.ps1
  - LegalWorkspace / Console deploy → Deploy-SpaarkeAi.ps1 (LegalWorkspace ships inside the Console bundle)
  - Events page deploy → Deploy-EventsPage.ps1
  - BFF API deploy → Deploy-BffApi.ps1 (via the bff-deploy skill)
  - Ribbon export → Export-EntityRibbon.ps1
```

### Step 7: Review Relevant CLAUDE.md Files

```
BASED on <context><relevant-files>:
  IDENTIFY module paths (src/client/pcf/*, src/server/api/*, etc.)
  FOR each module: IF {module}/CLAUDE.md exists → READ for module-specific conventions
```

---

### Step 8: Execute Task Steps (with Progress Tracking)

#### Step 8.0: Multi-File Work Decomposition (judgment call)

Before implementing, decide whether to delegate. Delegate only when the work splits into **independent, substantial** pieces (4+ files in independent modules, no shared state or imports between them); coupled or small changes (1–3 files, create-then-use sequences) stay in the main session, and so does anything touching `.claude/` (root CLAUDE.md §3). If context has been reported high, checkpoint first. Record the choice (delegate or not, and why) in one line in `current-task.md`.

**If you decide to delegate, read `references/multi-file-decomposition.md` before launching agents.** Every Agent launch states its model and effort (`.claude/constraints/agent-cost.md`; the `PreToolUse` hook refuses a launch with none).

#### Step loop

```
FOR each <step> in <steps>:

  BEFORE starting step:
    UPDATE current-task.md: Current Step: Step {N} - {description}; Next Action: {what this step will do}

  EXECUTE the step

  IF step involves code changes:
    APPLY patterns from <knowledge>; VERIFY against <constraints>
    UPDATE current-task.md → Files Modified: each file touched, with purpose

  IF step involves PCF:
    FOLLOW the pcf-deploy skill's version-bump table (every location it lists)

  IF step involves deployment:
    FOLLOW the matching deploy skill (dataverse-deploy, pcf-deploy, bff-deploy, code-page-deploy)

  IF decision made:
    UPDATE current-task.md → Decisions Made: {timestamp}: {decision} — Reason: {why}

  AFTER completing step:
    UPDATE current-task.md → Completed Steps: [x] Step {N}: {description} ({timestamp})
    UPDATE Next Step to Step {N+1}

    CHECKPOINT CHECK (MANDATORY after EVERY step): evaluate the Step 8.5 triggers.
    IF one fires → run context-handoff, report "✅ Checkpoint saved. Continuing...", continue.
    IF a compaction notice has appeared, or the user asks to compact →
      STOP after the checkpoint and report "State saved. Ready for /compact."
```

### Step 8.5: Proactive Checkpointing Rules (MANDATORY)

**Checkpoint at these triggers. They are NOT optional.** A checkpoint means invoking the `context-handoff` skill, which owns the `current-task.md` format and its "State, not history" rule (REWRITE the file; never prepend a new block). User phrases "save my progress", "/checkpoint", "/context-handoff", "I need to compact" also invoke it.

| Condition | Action | Rationale |
|-----------|--------|-----------|
| After every 3 completed steps | Run context-handoff | Regular checkpoints prevent data loss |
| After modifying 5+ files (this session) | Run context-handoff | Significant work should be preserved |
| After any deployment or live change | Run context-handoff (verbose) | Deployment state is critical |
| Before starting a complex or risky step | Run context-handoff | Preserve clean state before risky work |
| User or harness reports context is high | Run context-handoff (verbose) | Claude cannot see its own usage (root §5) |
| Compaction notice, or user asks to compact | Run context-handoff, then stop for /compact | Save state before it is summarized |
| After a significant decision | Update Decisions section (and checkpoint) | Document rationale for recovery |

A checkpoint updates, at minimum: Quick Recovery (Task, Step N of Total, Status, an EXPLICIT Next Action), Files Modified This Session, and Critical Context (1–3 sentences). Then continue working — do not wait for the user.

| Checkpoint Type | When | User Notification |
|-----------------|------|-------------------|
| **Silent** | After steps 3, 6, 9… | Brief: "✅ Checkpoint." |
| **Verbose** | Context reported high; after a deployment | Full report with state summary |
| **Blocking** | Compaction notice, or user asks to compact | STOP and request /compact |

---

### Step 9: Verify Acceptance Criteria

```
FOR each <criterion> in <acceptance-criteria>:
  VERIFY the criterion is met
  IF not met: FIX before proceeding
```

If the task's tags include `pcf`, `react`, `fluent-ui`, `bff-api`, `api`, `minimal-api` or `deploy`, open `references/surface-checklists.md` now (whether or not you read it at Step 4c) and tick every item in its section(s) for this task.

### Step 9.5: Quality Gates (MANDATORY)

Run code review and ADR validation after implementation, before marking the task complete.

```
AFTER all implementation steps and acceptance criteria verified:

1. RUN code-review on files modified in this task:
   → Get list from current-task.md "Files Modified" section (include files subagents changed)
   → Execute /code-review {file-list}

   TRIAGE every finding (Critical, Warning and Suggestion alike) using
   "Finding triage and review scope" below. Act on the class, not the severity label:
     → FIX-NOW classes (F1-F4): fix in this task. When a defect is outside this
       task's scope (pre-existing, another project's code, found in passing),
       file it AND report it to the operator. Never drop a real defect.
     → KNOWN-LIMIT classes (K1-K4): one line each in task notes + PR, no fix round
     → A genuine product or policy choice: ask the user (batch the questions)

   AFTER fixing: RE-RUN code-review on the FIX SCOPE: the fix diff plus the code that
   directly calls or is called by the changed lines (not the whole task surface).
   RE-RUN the affected test suites.
   REPEAT until no F-class finding remains. There is no round cap on fixing;
   the limits below are on review ceremony.

2. RUN adr-check on modified files:
   → Execute /adr-check {file-list}

   IF ADR violations found:
     → DO NOT default to silent fix-until-clean (the failure root CLAUDE.md §6.5 prevents)
     → Apply the root CLAUDE.md §6.5 ADR conflict resolution:
       Path C — Pivot to comply (default): the violation is a genuine mistake or a clean
         compliant alternative exists → FIX and re-run adr-check.
       Path A — Project-scoped exception: strict compliance gives a worse outcome AND the
         deviation is narrow → ESCALATE in the §6.5 format. If approved: document in
         design.md/spec.md "ADR Tensions" + PR description; never bypass silently.
       Path B — ADR amendment: the rule itself is no longer right → ESCALATE; if approved,
         the amendment becomes its own deliverable.
     → Silent violation is forbidden. If unsure, escalate (A or B) rather than choosing
       C reflexively.
     → Once a path is chosen and acted on, RE-RUN adr-check to verify the documented
       state (clean, OR violation present + cited exception).

3. VERIFY linting (if applicable):
   → TypeScript/PCF: npm run lint
   → C#: dotnet build --warnaserror
   IF lint errors: FIX, then RE-RUN lint

SKIP quality gates ONLY IF (and not for a test-modifying task — see below):
  - Task is documentation-only (no code changes)
  - Task is configuration-only (no logic changes)
  - User explicitly requests skip (record the reason)

UPDATE current-task.md — "Quality Gates" section:
  - Code Review: ✅ Passed (or issues found/fixed)
  - ADR Check: ✅ Passed (or violations found/fixed)
  - Lint: ✅ Passed (or N/A)
```

**Binding override — test-modifying tasks ALWAYS run quality gates** (root CLAUDE.md §8, no end date; introduced 2026-06-26 with ADR-038):
- IF the task modifies any file under `tests/**` OR its POML `<tags>` include `testing`, `test-reset`, `deletion`, `integration-test` → code-review + adr-check are MANDATORY regardless of rigor level; the SKIP block above does NOT apply. Reason: prevents wiring-test antipatterns and KEEP-path violations (ADR-038).
- Path-check enforcement: any deletion under a KEEP path — `tests/integration/{auth,regression,data-mutation,tenant,contract,seam}/**`, `tests/unit/domain/**`, `tests/Spaarke.ArchTests/**` (ADR-038 §2) — requires a same-PR replacement covering the same scenario, or — for an orphaned test whose subject was deleted — the evidence ADR-038 Amendment A3 lists, which code-review verifies in the diff.

#### Finding triage and review scope (BINDING)

**Why this exists.** `code-review` is coverage-first: it reports every finding, including low-confidence ones, and leaves filtering to this step. When this step had no filter, every re-run reviewed the whole surface again and surfaced new findings; in `unified-access-control-r2` tasks reached fix round `-r6`, owner decision rounds passed 50, and the project ran ~10× budget on "static-guard arms races and edge-case machinery … without real-world benefit". The classes below are the owner's rule from that project, made repo-wide.

**🔴 Owner correction (BINDING).** The limits below cut CEREMONY: unnecessary review passes, re-reviews of untouched code, speculative findings and pseudo-fixes. They **never** limit FIXING. An earlier version capped work at "2 fix rounds", which let a confirmed security defect wait on a round count; that reading is wrong and withdrawn. Owner: *"we should never allow known broken code to not be fixed just because it takes more than 2 rounds."*

**Every defect found is fixed or surfaced, whatever its origin (BINDING).** This holds whether the current work caused it directly or indirectly, or only uncovered it (pre-existing code, another project's code, configuration, data, the environment):
- **In this task's scope:** fix it in this task.
- **Outside this task's scope:** surface it to the operator. File it (`/project-defer-issue-tracking`, a GitHub issue) **and** name it in the task's completion report, with the concrete failure scenario. The operator decides when and where it is fixed.
- **Never** drop it silently, never leave it unreported because it is "not ours" or "pre-existing", and never reclassify it as a known limit to make it go away.

**Classify every finding before acting:**

| Class | Meaning | Action |
|---|---|---|
| **F1** | Runtime defect: security hole, fail-open, data loss, cross-customer exposure, broken real path | Fix (in scope) or file + report (out of scope) |
| **F2** | Maintainability defect that compounds: duplicates an existing mechanism, a comment contradicting the code, important behaviour with no test | Fix (in scope) or file + report (out of scope) |
| **F3** | Measurable performance problem | Fix (in scope) or file + report (out of scope) |
| **F4** | Unmet acceptance criterion, or required/promised functionality missing | Fix. **Never trimmed**: robustness and completeness of required functionality is not "over-engineering" |
| **K1** | Guard or static-analysis bypass reachable only by deliberately adversarial code in our own repo | Known limit: one line in notes + PR |
| **K2** | Rare edge that already fails closed | Known limit |
| **K3** | Seeding proof (mutation check) of a minor branch | Known limit |
| **K4** | Low-confidence finding with no concrete failure scenario you can state | Known limit |

A K class is for something that is **not a defect on a real path**. A confirmed defect with a stateable failure scenario on a real path is F-class, even if it is old or rare.

**Over-engineering check (applies to every fix).** A new column, job, setting or abstraction needs a realistic failure behind it, and a fix is never bigger than the problem. Prefer reusing an existing component (root CLAUDE.md §11). This check limits the SIZE of a fix, never whether a real defect gets fixed.

**Review scope (this is what is limited):**
1. **Review → fix → re-verify the fix scope → repeat until no F-class finding remains.** There is no round cap on fixing real defects.
2. **Re-verification scope is the fix diff plus its direct callers and callees,** not the task's whole surface. Including callers and callees catches a fix that breaks adjacent code. Re-running the affected test suites remains mandatory. A fresh full review after every fix is what generated an endless supply of new findings.
3. **Adversarial-verifier passes** (workflow scripts, verify-after-execute lanes) use the same classes. Allow **one** full verifier pass per task, or two for tasks tagged `auth`, `security` or `tenant-isolation`. Every later re-check is scoped to the fix diff (rule 2) and is not a new full pass. A further full pass is allowed when a fix changed the approach or entered a subsystem the scoped re-check would not cover — record the one-line reason in the task notes. This limit is on ceremony; it never stops a fix.
4. **Escalate when fixes are not converging, not because of a count.** That means the same defect returns after a fix, a fix keeps exposing new F-class defects in the same area, or the right fix needs a decision only the owner can make (root §6). Escalate with the findings and a recommendation. Never stop fixing silently, and never ship the defect.
5. **Flag diminishing returns proactively.** If a round produced only K-class findings, say so and stop. Don't continue rounds silently.
6. **Fix the class, not the instance.** When a defect's cause is a repeatable pattern (a misused contract, a wrong field read, a missing guard, a copy-pasted shape), run one Grep for the pattern before closing the task and record the result in the task notes. Then: (a) fix the matches inside the task's own files; (b) list every other match by path for the operator — the Grep output is the list; do not open or verify each one; (c) name the guard that would catch it (type, lint rule or ArchTest — root §16) and build it only when the POML asks for it or an existing guard family makes it a few lines (then with must-fire/must-not-fire controls, and a ratchet baseline if old instances remain). Fixing outside the task's files, or building a new guard, is a scope change: ask (root §6), do not expand the task. *Why: issue #975 fixed 3 error responses with the wrong content type and missed 44 more; the dead `if (!res.ok)` branch after `authenticatedFetch` was found in ~30 files and was being written again the same week.*

### Step 9.6: Portfolio Sync

After Step 9.5 and before Step 10, invoke `/devops-project-sync` to increment `Tasks Completed` on the project's GitHub Project Issue (each task completion = +1). It is silent per task. A failed increment degrades to a ⚠️ warning and does NOT block Step 10.

If, after the sync, `Tasks Completed == Task Count` (all POMLs complete), prompt: `✅ All tasks complete. Promote Project Status to "Completed candidate"? [Y/n]`. On Y set `Project Status = Completed`; this does NOT archive — the user runs `/devops-project-archive` when ready. Detail: `.claude/skills/devops-project-sync/SKILL.md`.

### Step 9.7: UI Testing (PCF/Frontend Tasks)

Applies when the task's tags include `pcf`, `frontend`, `fluent-ui` or `e2e-test`, Chrome integration is connected (`--chrome`), a deployed or local environment is running, and the task has `<ui-tests>` or UI-related acceptance criteria. It runs only with the user's confirmation.

**If the task's tags include `pcf`, `frontend`, `fluent-ui` or `e2e-test`, read `references/ui-testing.md` before continuing.** Otherwise go to Step 10.

---

### Step 10: Update Task Status (Completion)

```
UPDATE task file <metadata><status> to "completed"
ADD <notes> section with completion summary. End it with ONE rework line (format and use: scripts/ai-cost/README.md "Rework signal"),
  exactly one of these three forms, with the placeholders filled in:
    rework: none task 042
    rework: fix-round-2 task 042
    rework: escalated-to-opus task 042
  none = the first attempt passed review and verification; fix-round-<N> = N rounds of fixes after review or a failed check;
  escalated-to-<model> = the work was re-run on a higher tier. Put `task <NNN>` on the line (or keep NNN in the notes file name).
  When launching agents for the task, put `task <NNN>` in the Agent `description` so the launch can be tied to this line.
  The notes also list every out-of-scope defect filed at Step 9.5 (issue link + failure scenario).

UPDATE TASK-INDEX.md with ✅ completed status

VERIFY both writes landed (MANDATORY):
  pwsh scripts/check-task-status-drift.ps1
  → exit 0 = the POML status and the index marker agree
  → exit 1 = one of the two writes was skipped; the output names the task and which side is behind
  → FIX IT NOW, before Step 11. Do not proceed on a red check.
```

**Why.** Completion is recorded in two places — the POML `<status>` and the `TASK-INDEX.md` marker — and they drift in both directions (a 2026-09-03 audit of `unified-access-control-r2` found 17 disagreements across 92 tasks: 14 stale POMLs, 1 stale index entry). When the check goes red, resolve it from **evidence** (a git completion commit touching `projects/<name>/`), not by assuming one artifact is authoritative. The check gates **only the current project** (inferred from the `work/<name>` branch); `-All` gives a non-blocking repo-wide report.

### Step 10.5: Script Library Maintenance

**If the task used an existing script, changed one, repeated a command sequence 3+ times that could become a script, or made a script's behaviour obsolete, read `references/script-library-maintenance.md` before continuing.** Otherwise go to Step 10.6.

### Step 10.6: Conflict Sync Check (Parallel Sessions)

Before committing from a worktree, check whether master moved under you:

```
IF running in a worktree (not main repo):
  1. git fetch origin master
  2. git log HEAD..origin/master --oneline        (new commits on master?)
  3. IF master has new commits:
       git diff --name-only HEAD origin/master
       COMPARE with current-task.md "Files Modified"
       IF overlap:
         ⚠️ WARN: "Master has changes to files you modified"
         RECOMMEND rebasing before pushing:
           git fetch origin master && git rebase origin/master
           # resolve conflicts
           git push --force-with-lease
  4. IF no overlap OR the user chooses to continue: PROCEED with commit and push
```

Detecting overlap at task end is cheaper than at merge time, and small rebases conflict less.

### Step 11: Transition to Next Task

`current-task.md` tracks only the ACTIVE task, not history. When a task completes, it resets.

```
TRANSITION current-task.md:

1. MOVE OUT anything worth keeping (do NOT leave it in current-task.md):
   - A gotcha or standing directive that outlives this task → project CLAUDE.md
     standing-directive / gotcha section ("Standing directives & gotchas", or the template's
     "Owner directives and standing decisions" / "Gotchas")
   - Decisions and rationale → the task's notes file / POML <notes>
   - The narrative → the completion commit message

2. RESET for next task: clear "Completed Steps", "Files Modified", "Decisions Made", "Current Step"

3. DETERMINE next task:
   - Check TASK-INDEX.md for next pending task (🔲 status)
   - If dependencies: find the first task with all dependencies satisfied

4. UPDATE current-task.md with next task:
   IF next task found:
     Task ID / Task File (tasks/{NNN}-{slug}.poml) / Title / Phase from its metadata;
     Status: "not-started"; Started: "—"; Next Action: "Begin Step 1 of task {NNN}"

   IF no more tasks (project complete):
     Task ID: "none"; Status: "none"; Next Action: "Project complete. Run /test-diet then /repo-cleanup"

   BINDING — Test Diet Gate at project close:
   IF the just-completed task is a `090-wrapup-*` task AND `/test-diet` was NOT invoked during it:
     → 🚨 HARD WARNING: "Wrap-up task completed without running /test-diet. Test diet must pass
       before project close. Run /test-diet now to reconcile build-vs-maintain tests per ADR-038 §7."
     → If the user proceeds without it: log the warning in current-task.md Decisions and continue,
       BUT the wrap-up PR description MUST include "test-diet: SKIPPED (rationale required)".
     → notes/test-diet-report.md is the binding artifact; reviewer judgment on each
       DELETE/MAINTAIN/AMBIGUOUS classification belongs in the wrap-up PR.
   IF the wrap-up task ran /test-diet AND notes/test-diet-report.md exists:
     → Confirm: "✅ Test diet executed; report at notes/test-diet-report.md. {D} delete candidates /
       {B} ambiguous / {M} confirmed maintain."

5. REPORT to user:
   "✅ Task {completed_id} complete.
    Next task: {next_id} - {next_title}
    Ready to begin? [Y/N]"
```

**Final task merge prompt.** After the last task in the project (all tasks in TASK-INDEX.md ✅), prompt:

```
✅ All project tasks complete!
📋 Next steps:
  1. Run `/repo-cleanup` to validate structure
  2. Run `/merge-to-master` to merge this branch into master
⚠️  Branch work is NOT on master until explicitly merged.
    Pushing to origin (push-to-github) ≠ merging to master.
Merge now? [y/n]
```

On yes, invoke `merge-to-master` in Single Merge mode for the current branch.

**Why reset instead of accumulate?** `current-task.md` is for recovery, not history — history lives in `TASK-INDEX.md`, the POML status and notes, and git. Measured cost of not resetting (2026-10-06): three active projects had 150 / 445 / 483 KB files, read at Step 0 and Step 2 of every task (~120k tokens at the top end) before any work began.

---

## Gotchas

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| Lost progress after compaction; can't resume | current-task.md not updated, or sections left incomplete | Update it after every step (Step 8 loop) and checkpoint at the Step 8.5 triggers. Work between checkpoints is at risk. |
| Resumed at `next_step` and implemented without ADRs/patterns | Step 0 resume skipped context reload | On resume, reload Step 1 and Steps 4–7 before continuing (Step 0). Resuming forces FULL rigor. |
| Wrong protocol depth | Rigor tree not traversed (Step 0.5 skipped) | Step 0.5 is MANDATORY; output the declaration at task start so the choice is visible. |
| ADR violation / wrong patterns / missing context | Step 4–7 elided "because the task seems simple" | Never skip Step 4 unless rigor is MINIMAL. ADR constraints catch problems tests don't. Read the module CLAUDE.md (Step 7). |
| Knowledge files not loaded | `<knowledge><files>` paths in the POML were wrong/stale | Verify they resolve at Step 1; a broken path means task-create produced a bad mapping — fix the POML, then re-invoke. |
| Stale PCF deployment | Version not bumped in every location | `references/surface-checklists.md` PCF section; `pcf-deploy` version table. |
| Broken code merged | Step 9.5 skipped under pressure or "CI will catch it" | Step 9.5 is NON-NEGOTIABLE at FULL and for test-modifying tasks. CI catches mechanical issues; code-review catches judgment-layer issues. |
| Endless fix rounds, or a real defect left unfixed | Review scope unlimited, or a count read as a stop rule | Step 9.5 "Finding triage and review scope": limit re-review scope, never fixing. Escalate on non-convergence, not on a count. |
| External callers silently break | A step id was renumbered or its meaning changed | Step ids are an external contract. Cited ids: 0, 0.3, 0.5, 1, 2, 4a, 8, 8.0, 8.5, 9, 9.5 (incl. "Finding triage and review scope"), 9.6, 9.7, 10, 10.5, 10.6, 11. Never renumber them. |
| Parallel-safe tasks run one after another | Sequential invocation | Root CLAUDE.md §4: independent tasks run as ONE message with several task-execute invocations. |
| Skill body drifts from root CLAUDE.md | Root CLAUDE.md edited without updating this skill | Run the `doc-drift-audit` skill after changes to root CLAUDE.md §4–§8. |

## Related

- **context-handoff** — checkpoint format, "State, not history", post-compaction recovery (invoked at Step 3 / 8.5)
- **project-continue** — restore state after a PR merge or new session, then hands off here
- **task-create** — creates the POMLs this skill executes (rigor hint 3.5.5, model tier 3.5.5b, step mode 3.5.5c, `/goal` eligibility 3.85)
- **conflict-check** — hot-path watchlist (Step 0.5 pre-step)
- **adr-aware**, **spaarke-conventions**, **script-aware** — always-apply skills (Step 6)
- **code-review**, **adr-check** — Step 9.5 quality gates
- **devops-project-sync** — Step 9.6 portfolio sync
- **ui-test** — Step 9.7 browser testing (requires `--chrome`)
- **dataverse-deploy**, **pcf-deploy**, **bff-deploy**, **code-page-deploy** — deployment operations
- **test-diet**, **repo-cleanup**, **merge-to-master** — project close (Step 11)
- **project-pipeline** — initializes `current-task.md`; Step 5 dispatches waves (Step 0.3)
- `docs/procedures/context-recovery.md` — full recovery procedure
- Root `CLAUDE.md` §4–§8 — task execution, checkpoints, escalation, ADR conflicts, rigor and review limits (canonical)
