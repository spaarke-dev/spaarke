---
description: Automated pipeline from SPEC.md to ready-to-execute tasks — runs autonomously by default with parallel task execution
tags: [project-pipeline, orchestration, automation]
techStack: [all]
appliesTo: ["projects/*/", "start project", "initialize project"]
alwaysApply: false
exemplar: none-too-volatile
last-reviewed: 2026-05-17
---

# project-pipeline

> **Last Reviewed**: 2026-05-17
> **Reviewed By**: ai-procedure-quality-r1 (Phase 2b Wave 2c — `leave-alone-justified` on body length; **fixed AP-1: stale `MAX_THINKING_TOKENS=50000` prescription** — IGNORED on Opus 4.6+ per root CLAUDE.md adaptive-thinking model)
> **Exemplar rationale**: Pipeline runs are project-specific; no canonical snapshot holds. The skill's 12-step structure is the contract.
> **Justified length** (943 lines): operationally dense orchestrator chaining 12 component-skill invocations. Splitting to references/ would risk dereference reliability (per Phase 2b Wave 2c decision 2026-05-17): an agent executing this pipeline needs procedural detail inline, not gestured-at.

## Prerequisites

### Claude Code Effort & Output Configuration

**CRITICAL — Updated 2026-05-17**: This orchestrator runs best with **adaptive thinking on high or max effort** + extended output tokens. The previous prescription of `MAX_THINKING_TOKENS=50000` is **OBSOLETE on Opus 4.6+** — Anthropic moved to adaptive thinking where the model decides depth dynamically. See root [`CLAUDE.md`](../../../CLAUDE.md) for current configuration guidance.

**Current prerequisites:**

```bash
# Output tokens (still load-bearing; project-pipeline emits a lot)
CLAUDE_CODE_MAX_OUTPUT_TOKENS=64000

# MAX_THINKING_TOKENS — IGNORED on Opus 4.6+ (adaptive thinking applies)
# If running on older models, the previous prescription was 50000
```

**Effort guidance**: invoke `/project-pipeline` on Opus with effort `high` or `max` — multi-phase, deep resource discovery, 100+ task files. Subagents the pipeline spawns (general-purpose, Explore) can run on `low` or `medium`.

**Why Output Tokens is Critical**:
- **Resource Discovery (Step 2)**: Loads ADRs, skills, knowledge docs, and existing code patterns
- **Artifact Generation (Step 2)**: Calls `project-setup` which generates README, PLAN, CLAUDE.md
- **Task Decomposition (Step 3)**: Creates 50-200+ task files with tag-to-knowledge mapping
- **Context Enrichment**: Each task file includes applicable ADRs and knowledge docs
- **Pipeline Orchestration**: Chains multiple component skills sequentially

**Example Context Load**:
For AI Document Intelligence R1 project:
- spec.md: 2,306 words
- 4 ADRs loaded (ADR-013, ADR-014, ADR-015, ADR-016)
- 8 knowledge docs discovered
- 178 tasks generated with full context

**Verify settings before proceeding**:
```bash
# Windows PowerShell
echo $env:MAX_THINKING_TOKENS
echo $env:CLAUDE_CODE_MAX_OUTPUT_TOKENS

# Should output: 50000 and 64000
```

**If output tokens not set**, the pipeline may produce truncated artifacts. See root [`CLAUDE.md`](../../../CLAUDE.md) for full guidance. (The legacy `MAX_THINKING_TOKENS` env var is IGNORED on Opus 4.6+ — adaptive thinking applies; invoke this skill on Opus with effort `high` or `max`.)

### Permission Mode: Plan Mode (REQUIRED for Steps 0-3)

**This skill MUST run in Plan Mode for Steps 0-3. Claude Code must confirm Plan Mode before proceeding past Step 0.**

```
⏸ PLAN MODE REQUIRED — ENFORCED

Before starting this skill, Claude MUST:
  1. Verify Plan Mode is active (look for "⏸ plan mode on" indicator in UI)
  2. If NOT in Plan Mode → STOP and ask user to press Shift+Tab twice to enter Plan Mode
  3. Do NOT proceed past Step 0 until Plan Mode is confirmed

WHY: Steps 0-3 analyze spec.md, discover resources, and generate planning artifacts.
     Plan Mode ensures Claude reads and plans before making any file changes.
     This is the single most effective enforcement of the "plan before implement" discipline.

WHEN TO SWITCH TO ACCEPT EDITS MODE:
  - After Step 3 completes (all planning artifacts reviewed)
  - Before Step 4 (feature branch creation — requires git write)
  - User must Shift+Tab to Accept Edits mode explicitly
  - Skill reports: "Ready for implementation phase — please switch to Accept Edits mode"
```

---

## Purpose

**Tier 2 Orchestrator Skill (RECOMMENDED)** - Streamlined end-to-end project initialization pipeline that chains: SPEC.md validation → Resource discovery → Artifact generation → Task decomposition → Feature branch → Ready to execute Task 001.

**Key Features**:
- **Autonomous by default** — pipeline runs end-to-end without approval gates
- Automatic resource discovery (ADRs, skills, knowledge docs)
- Calls component skills (project-setup, task-create)
- Creates feature branch for isolation
- **Parallel task execution** — tasks are grouped for concurrent execution via Claude Code task agents
- Auto-starts task 001 after pipeline completes

**Execution Mode**: The pipeline runs autonomously — it proceeds through all steps without waiting for user confirmation. Status updates are reported at each milestone but do not block progress. The user can interrupt at any point if needed.

**Interactive Override**: If the user explicitly says "run pipeline interactively" or "with approvals", switch to interactive mode with confirmation gates after each step.

## When to Use

- User says "start project", "initialize project from spec", or "run project pipeline"
- Explicitly invoked with `/project-pipeline {project-path}`
- A `spec.md` file exists at `projects/{project-name}/spec.md`

## Pipeline Steps

### Step 0: Plan Mode Confirmation

**Purpose**: Enforce Plan Mode before any planning operations.

**Action**:
```
ASK user: "Please confirm Plan Mode is active (look for ⏸ indicator)"
IF not confirmed:
  → STOP — request user to press Shift+Tab twice
  → Do not proceed until confirmed
```

---

### Step 0.3: Pre-Flight Checks

**Purpose**: Ensure clean baseline before starting a new project. Catches common "stale start" problems.

**Checks (all must pass)**:

```
a. Main repo (or current worktree) on correct branch
   git branch --show-current
   IF on master AND creating feature project → WARN, suggest feature branch

b. Working tree clean
   git status --porcelain
   IF dirty → STOP — ask user to commit/stash first

c. Master is current with origin
   git fetch origin
   git rev-list --count HEAD..origin/master
   IF > 0 → STOP — user must pull origin/master first

d. Build succeeds on current baseline
   dotnet build src/server/api/Sprk.Bff.Api/
   IF build fails → STOP — broken baseline cannot start new project

e. If in a worktree: main repo is synced
   DETECT: git rev-parse --git-common-dir
   IF worktree AND main repo master != origin/master → WARN, suggest worktree-sync

f. Previous project lessons-learned read (if exists)
   IF projects/*/notes/lessons-learned.md exists (sorted by mtime):
     → Remind user: "Latest lessons-learned: {path} — have you reviewed it?"
     → Non-blocking, informational only
```

**Output**:
```
✅ Pre-flight checks passed:
   - On branch: {branch}
   - Working tree: clean
   - Master: current (0 commits behind)
   - Build: passing (0 errors, {N} warnings)
   - Main repo sync: {status}

📋 Recent lessons-learned: {path} (please review if not already)
```

**Autonomous mode**: Reports results, STOPS if any critical check fails. No auto-fix (the user must resolve).
**Interactive mode**: Same behavior — these are safety gates, not confirmation gates.

---

### Step 0.5: Master Staleness Check

**Purpose:** Ensure master has all completed branch work before creating a new project. Prevents new projects from starting on stale code.

**Action:**
```
RUN merge-to-master in AUDIT mode:
  git fetch origin
  FOR EACH branch in origin/work/*:
    count unmerged commits vs origin/master

IF any branches have unmerged commits:
  ⚠️  WARNING: Master may be stale!

  {N} branches have {M} total unmerged commits:
    - {branch}: {count} commits
    - ...

  New projects created from master will be missing this work.

  Recommended: Run `/merge-to-master` before starting this project.
  Continue anyway? [y/n]

IF no unmerged branches:
  ✅ Master is current — all branch work merged.
  Proceeding with project initialization...
```

**Autonomous mode** (default): If stale, LOG warning and continue. User can address after pipeline completes.
**Interactive mode**: Wait for user to choose merge first or continue.

---

### Step 1: Validate SPEC.md

**Action:**
```
LOAD: projects/{project-name}/spec.md

VALIDATE:
✓ File exists and is readable
✓ Contains required sections:
  - Executive Summary / Purpose
  - Scope definition
  - Technical approach
  - Success criteria
✓ Minimum 500 words (meaningful content)

IF validation fails:
  → STOP - List missing elements
  → Offer to help complete spec.md
```

**Output to User:**
```
✅ SPEC.md validated:
   - 2,306 words
   - All required sections present
   - Ready for planning

📋 Next Step: Generate PLAN.md from spec

[Y to proceed / refine to make changes / stop to exit]
```

**Autonomous mode** (default): Report validation results and proceed immediately.
**Interactive mode**: Wait for user confirmation.

---

### Step 1.5: Overlap Detection (Parallel Sessions)

**Purpose:** Detect potential file conflicts with active PRs before investing time in project setup.

**Action:**
```
CHECK for active PRs:
  gh pr list --state open --json number,title,headRefName,files

IDENTIFY likely files from spec.md:
  - Parse spec.md for mentioned components:
    • PCF controls → src/client/pcf/
    • API endpoints → src/server/api/
    • Dataverse plugins → src/solutions/
    • Shared libraries → src/*/shared/
    • Documentation → docs/, .claude/
  - List directories/files likely to be modified

COMPARE with active PRs:
  FOR EACH active PR:
    overlap = intersection(likely_project_files, pr_files)
    IF overlap is not empty:
      ADD to potential_conflicts list

CHECK other worktrees:
  git worktree list
  FOR EACH worktree (excluding current):
    CHECK branch name for active project
```

**Decision Tree:**
```
IF no active PRs with overlapping files:
  → Continue normally (no warning)

IF overlap detected:
  ⚠️ WARN user:
  "Potential file overlap detected with active PRs:

   PR #{number}: {title}
     Branch: {branch}
     Overlapping areas:
       - src/client/pcf/ (both projects touch PCF controls)
       - .claude/skills/ (both modify skills)

   Recommendations:
   1. Coordinate scope to avoid same-file edits
   2. Designate file ownership (Session A owns file X, Session B owns file Y)
   3. Plan to merge PR #{number} first, then rebase this project

   Proceed anyway? [Y to continue / stop to exit]"

  WAIT for user confirmation before continuing
```

**Output to User (if overlaps found):**
```
⚠️ Potential Overlap Detected

Your project (from spec.md) appears to touch:
  - src/client/pcf/ (new PCF control)
  - .claude/skills/ (skill updates)

Active PRs with overlapping files:
──────────────────────────────────
PR #98: chore: project planning updates
  Branch: work/project-planning-and-documentation
  Overlapping: .claude/skills/

Recommendations:
1. If PR #98 is close to merge → Wait for it, then start
2. If both sessions are yours → Coordinate file ownership
3. If proceeding → Plan to rebase after PR #98 merges

[Y to proceed with awareness / stop to wait]
```

**Autonomous mode** (default): Log overlaps as warnings and proceed. Overlaps are informational — they don't block the pipeline.
**Interactive mode**: Present overlaps and wait for user confirmation.

---

### Step 2: Comprehensive Resource Discovery & Artifact Generation

**Purpose:** Load ALL implementation context (ADRs, skills, patterns, knowledge docs, pattern file pointers) for task creation.

> **Pre-step (added 2026-06-26 by `ci-cd-unit-test-remediation-r1` task CICD-061 per spec FR-C04)**: Before resource discovery, read **`projects/INDEX.md`** (the active-project registry maintained by this skill + task-execute). For the new project being initialized:
>
> 1. Parse the new project's spec.md / design.md to detect hot-path touches (BFF / SpaarkeAi / ci-workflows / skill-directives / root-CLAUDE).
> 2. For each detected hot-path, query INDEX.md for OTHER active projects with the same hot-path declared (YES column for the same surface).
> 3. **If overlap detected**: emit a HARD WARNING in Step 2's output naming the overlapping projects + coordination recommendation. Do NOT block the pipeline — the user decides how to sequence.
> 4. **If no overlap**: silent log `✅ Hot-path surfaces unique among active worktrees`.
> 5. After the new project is initialized, append its row to `projects/INDEX.md` (project name, branch, worktree path, hot-path declaration, last-touched date).
>
> The hot-path overlap warning is informational, not blocking. Its purpose: prevent silent collisions on shared files when 5+ worktrees touch the same surface (per CICD-030 finding: 13 of 17 worktrees touch BFF).

**Action Part 1: Comprehensive Resource Discovery**
```
LOAD context:
  - projects/{project-name}/spec.md

DISCOVER RESOURCES (Comprehensive):
  1. IDENTIFY resource types from spec.md
     - Extract keywords (technologies, operations, feature types)
     - Identify components (API, PCF, plugins, storage, AI, jobs)

  2. LOAD applicable ADRs via adr-aware
     - Based on resource types in spec (API, PCF, Plugin, etc.)
     - Example: PCF control → ADR-006, ADR-011, ADR-012, ADR-021
     - Load FULL ADR content (not just constraints)

  3. SEARCH for applicable skills
     - Search .claude/skills/INDEX.md
     - Match tags, techStack in skill frontmatter
     - Example: "deploy to Dataverse" → dataverse-deploy skill

  4. SEARCH for knowledge docs and patterns
     - Search docs/guides/ for relevant procedures
     - Search .claude/patterns/ for code patterns
     - Match technology names, patterns
     - Example: "Azure OpenAI" → openai, embeddings, streaming patterns

  5. FIND existing code examples
     - Search codebase for similar implementations
     - Identify canonical implementations to follow
     - Example: Existing PCF controls for reference

  6. DISCOVER applicable scripts (via script-aware)
     - READ scripts/README.md for script registry
     - Match spec keywords to script purposes:
       - PCF deployment → Deploy-PCFWebResources.ps1
       - API testing → Test-SdapBffApi.ps1
       - Custom pages → Deploy-CustomPage.ps1
       - Ribbon work → Export-EntityRibbon.ps1
     - Note scripts for inclusion in task files

  7. VALIDATE Dataverse schema (via MCP tools — if project touches Dataverse entities)
     - Parse spec.md for Dataverse entity names (sprk_* tables, lookups, option sets)
     - IF entities referenced in spec:
       a. Use mcp__dataverse__list_tables() to enumerate existing tables
       b. Use mcp__dataverse__describe_table() for each referenced entity
       c. Compare spec's field/relationship requirements against actual schema
       d. Flag gaps: "Entity sprk_X exists but missing field Y"
       e. Flag conflicts: "Field Y exists but type differs from spec"
       f. Flag missing: "Entity sprk_Z does not exist — schema creation task needed"
     - IF no Dataverse entities in spec: SKIP this step
     - NOTE: MCP tools are read-only here — schema changes happen in task execution

OUTPUT: Comprehensive resource discovery summary
  - X ADRs loaded (with full content)
  - Y skills applicable (with file paths)
  - Z knowledge docs found (guides + patterns)
  - N canonical implementations identified (from codebase search)
  - M scripts available (for deployment/testing steps)
  - S schema validations (Dataverse entities checked via MCP, gaps flagged)

⚠️ **DIFFERENCE from design-to-spec Step 3**:
- design-to-spec: Preliminary (ADR constraints only for spec enrichment)
- project-pipeline: Comprehensive (full ADRs, patterns, pattern pointers to canonical implementations)
```

**Action Part 2: Generate Artifacts with Discovered Resources**
```
PREPARE context for project-setup:
  - spec.md content
  - Discovered resources summary (ADRs, skills, knowledge docs)

INVOKE: project-setup projects/{project-name}

This component skill generates:
  ✅ README.md (project overview, graduation criteria from spec)
  ✅ PLAN.md (implementation plan with WBS from spec)
  ✅ CLAUDE.md (AI context file from spec)
  ✅ current-task.md (active task state tracker - for context recovery)
  ✅ Folder structure (tasks/, notes/ with subdirectories)

AFTER project-setup completes:
  ENHANCE PLAN.md with discovered resources:
    - Insert "Discovered Resources" section in Architecture Context
    - List applicable ADRs with summaries
    - List relevant skills with purpose
    - List knowledge docs with topics
    - Update References section with all resource links

ENHANCE CLAUDE.md with discovered resources:
    - Populate "Applicable ADRs" section
    - Add resource file paths for quick reference
    - VERIFY "🚨 MANDATORY: Task Execution Protocol" section exists
      (should be auto-included from project-setup template)
    - If missing, add the mandatory task execution protocol section
```

**Output to User:**
```
✅ Resources discovered:
   - 4 ADRs identified (ADR-001, ADR-007, ADR-008, ADR-010)
   - 2 skills applicable (dataverse-deploy, adr-aware)
   - 3 knowledge docs found (SPAARKE-ARCHITECTURE.md, ...)
   - 2 scripts available (Deploy-PCFWebResources.ps1, Test-SdapBffApi.ps1)

✅ Artifacts generated:
   - README.md (project overview, graduation criteria)
   - PLAN.md (5 phases, WBS structure)
   - CLAUDE.md (AI context file)
   - Folder structure created

📄 Files created:
   - projects/{project-name}/README.md
   - projects/{project-name}/plan.md
   - projects/{project-name}/CLAUDE.md
   - projects/{project-name}/current-task.md (context recovery)
   - projects/{project-name}/tasks/ (empty, ready for task files)
   - projects/{project-name}/notes/ (with subdirectories)

📋 Next Step: Decompose PLAN.md into executable task files

[Y to proceed / review to view artifacts / refine {file} to edit / stop to exit]
```

**Autonomous mode** (default): Report discovery/generation results and proceed immediately to task decomposition.
**Interactive mode**: Wait for user confirmation.

---

### Step 3: Generate Task Files

**Action:**
```
LOAD:
  - projects/{project-name}/plan.md (Phase Breakdown section)
  - .claude/templates/task-execution.template.md (POML format)
  - Tag-to-knowledge mapping (from task-create skill)

REQUIREMENTS (from task-create):
  - Each task file MUST follow the task-execution.template.md structure (root <task id="..." project="...">)
  - Each task MUST include <knowledge><files> and it MUST NOT be empty
  - PCF tasks MUST include docs/guides/PCF-DEPLOYMENT-GUIDE.md and src/client/pcf/CLAUDE.md
  - Applicable ADRs MUST be included via docs/adr/*.md (see task-create Step 3.5)
  - **(Added 2026-06-26 by ci-cd-unit-test-remediation-r1 task CICD-061 per spec FR-C04)** — IF the project's spec.md or design.md indicates BFF (`src/server/api/Sprk.Bff.Api/**`) or SpaarkeAi (`src/solutions/SpaarkeAi/**`) touch, design.md MUST contain a `<hot-path-declaration>` XML block enumerating: BFF Y/N, SpaarkeAi Y/N, ci-workflows Y/N, skill-directives Y/N, root-CLAUDE.md Y/N. The pipeline EMITS A HARD WARNING and flags the project for design-doc update if this block is absent. The warning is informational (does not block task generation) but is escalated in the pipeline's Step 2 output so the operator sees it before Phase 1 begins. Reference: `projects/INDEX.md` entries dogfood this rule.

CREATE directory:
  - projects/{project-name}/tasks/

FOR each phase in PLAN.md:
  FOR each deliverable/objective:
    DECOMPOSE into discrete tasks (2-4 hour chunks)
    
    APPLY numbering:
      - Phase 1 → 001, 002, 003...
      - Phase 2 → 010, 011, 012...
      - Phase 3 → 020, 021, 022...
      - (10-gap for insertions)
    
    GENERATE .poml file:
      - Valid POML/XML format
      - <metadata> with tags, phase, estimate
      - <prompt> with goal, context, constraints
      - <knowledge> with auto-discovered files (based on tags)
      - <steps> with concrete actions
      - <tools> with Claude Code capabilities
      - <outputs> with expected artifacts
      - <acceptance-criteria> with verification steps

IDENTIFY task dependencies and parallel opportunities:
  - Mark explicit dependencies in task <metadata><dependencies>
  - Group independent tasks that CAN run in parallel
  - Flag tasks that BLOCK multiple downstream tasks (critical path)
  - See task-create Step 3.8 for parallel task grouping

ADD deployment tasks (per task-create Step 3.6):
  - After each phase that produces deployable artifacts
  - Tag: deploy

ADD UI test definitions for PCF/frontend tasks:
  - For tasks with tags: pcf, frontend, fluent-ui, e2e-test
  - Include <ui-tests> section in task POML with:
    • Test name and URL (environment placeholder if needed)
    • Step-by-step test actions (navigate, click, verify)
    • Expected outcomes
    • ADR-021 dark mode checks (for Fluent UI tasks)
  - Example UI test structure:
    <ui-tests>
      <test name="Component Renders">
        <url>https://{org}.crm.dynamics.com/main.aspx?...</url>
        <steps>
          <step>Navigate to form</step>
          <step>Verify control is visible</step>
          <step>Check console for errors</step>
        </steps>
        <expected>Control renders without console errors</expected>
      </test>
      <test name="Dark Mode Compliance">
        <steps>
          <step>Toggle dark mode in settings</step>
          <step>Verify colors adapt</step>
        </steps>
        <expected>All colors use semantic tokens per ADR-021</expected>
      </test>
    </ui-tests>
  - UI tests are executed by task-execute Step 9.7 via ui-test skill

ADD wrap-up task (mandatory per task-create Step 3.7):
  - Final task: 090-project-wrap-up.poml (or next available)
  - Updates README status to Complete
  - Creates lessons-learned.md
  - Archives project artifacts

CREATE: projects/{project-name}/tasks/TASK-INDEX.md
  - Registry of all tasks with status
  - Dependencies graph (which tasks block which)
  - Critical path (longest dependency chain)
  - High-risk items
  - **Parallel Groups**: Tasks that CAN run simultaneously
    Example format:
    ```markdown
    ## Parallel Execution Groups

    | Group | Tasks | Prerequisite | Notes |
    |-------|-------|--------------|-------|
    | A | 020, 021, 022 | 010 complete | Independent API endpoints |
    | B | 031, 032 | 030 complete | Separate UI components |
    ```
```

**Output to User:**
```
✅ Task files generated:
   - 178 tasks created in tasks/
   - TASK-INDEX.md created
   - All tasks in POML/XML format
   - Tag-to-knowledge mapping applied
   - 4 deployment tasks added (010-deploy, 020-deploy, 030-deploy, 040-deploy)
   - Wrap-up task added (090-project-wrap-up.poml)

📁 Files created:
   - projects/ai-document-intelligence-r1/tasks/TASK-INDEX.md
   - projects/ai-document-intelligence-r1/tasks/001-create-environment-variables.poml
   - ... (176 more tasks)
   - projects/ai-document-intelligence-r1/tasks/090-project-wrap-up.poml

✨ Project Ready for Execution!

📋 Next Step: Execute Task 001

To start: Just say "execute task 001" or "work on task 001"
Task-execute skill will automatically:
  - Load task file
  - Load knowledge files based on tags
  - Load applicable ADRs
  - Execute with full context

[Y to start task 001 / review {task-number} to view task / stop to exit]
```

**Autonomous mode** (default): Report task generation results and proceed immediately to branch creation.
**Interactive mode**: Wait for user confirmation.

---

### Step 4: Create Feature Branch

**Action:**
```
CREATE feature branch for this project:

BRANCH NAMING:
  feature/{project-name}  ← matches project folder name
  Example: feature/ai-document-intelligence-r1

GIT OPERATIONS:
  1. Create and checkout branch:
     git checkout -b feature/{project-name}

  2. Stage project files:
     git add projects/{project-name}/

  3. Commit with conventional commit format:
     git commit -m "feat({scope}): initialize {project-name} project

     - Created project artifacts (README, PLAN, CLAUDE.md)
     - Generated {X} task files
     - Project ready for implementation

     🤖 Generated with Claude Code"

  4. Push to remote with tracking:
     git push -u origin feature/{project-name}

  5. OPTIONAL - Create draft PR for visibility:
     gh pr create --draft \
       --title "feat({scope}): {project-name}" \
       --body "## Summary
Implementation of {project-name}

## Status
- [x] Project initialized
- [x] Tasks created ({X} tasks)
- [ ] Implementation in progress
- [ ] Code review
- [ ] Ready for merge

## Quick Links
- [Project README](projects/{project-name}/README.md)
- [Implementation Plan](projects/{project-name}/plan.md)
- [Task Index](projects/{project-name}/tasks/TASK-INDEX.md)

🤖 Generated with Claude Code"

WHY create branch at this point:
  - All artifacts and tasks are created (meaningful commit)
  - Isolates project work from master
  - Enables incremental commits during implementation
  - Draft PR provides team visibility
  - Clean merge when project completes
```

**Output to User:**
```
✅ Feature branch created:
   - Branch: feature/{project-name}
   - Initial commit: "feat({scope}): initialize {project-name} project"
   - Pushed to remote: origin/feature/{project-name}
   - Draft PR created: #{PR-number}

🔗 View PR: https://github.com/{org}/{repo}/pull/{PR-number}

📋 Next Step: Execute Task 001 (optional auto-start)

[Y to start task 001 / stop to exit]
```

**Autonomous mode** (default): Report branch creation and proceed immediately to task execution.
**Interactive mode**: Wait for user confirmation.

---

### Step 4.5: Prompt for projected Target Date (NEW — informed projection)

**Action**:
```
IF projects/{name}/README.md has a portfolio pointer block (Project Issue exists):

  COMPUTE summary the operator can use to make the projection:
    - Task count from tasks/TASK-INDEX.md
    - Phase count
    - Estimated effort range (from plan.md "Timeline" / "Estimated Effort" fields)
    - Critical path length if surfaced

  PROMPT:
    ┌─────────────────────────────────────────────────────────────────┐
    │ Plan summary:                                                   │
    │   - {N} tasks across {M} phases                                 │
    │   - Estimated effort: {range from plan.md}                      │
    │   - Critical path: {summary if available}                       │
    │                                                                 │
    │ Set a projected Target Date for this project?                   │
    │ Format: YYYY-MM-DD (e.g., 2026-08-15), or 'skip' to leave blank│
    │ >                                                               │
    └─────────────────────────────────────────────────────────────────┘

  IF operator enters valid ISO date:
    Set `Target Date` field on the Project Issue via
    updateProjectV2ItemFieldValue with value: { date: "YYYY-MM-DD" }
    REPORT: "✅ Target Date set to {date}. Drift will be tracked at archive."

  ELIF operator enters 'skip' or blank:
    REPORT: "Target Date left blank. Set later via GitHub UI or re-run /devops-project-register."

  ELIF input fails to parse:
    Re-prompt ONCE. Second invalid = treat as 'skip'.

ELSE (no portfolio pointer block — project not registered):
  SKIP this step. The /devops-project-register hook (Step 1.7) would have
  already handled this via its own Target Date prompt.
```

**Why this step is here, not earlier**:

| Lifecycle point | What's known | Projection quality |
|---|---|---|
| `/devops-idea-create` | Just a one-liner | Guess only |
| `/devops-project-start` | Folder + design.md skeleton, no plan yet | Bad guess |
| **End of `/project-pipeline`** | **Plan with WBS, task count, effort range, critical path** | **Informed projection** |

By placing the prompt here, the operator commits to a Target Date that has actual context, not a blind guess. This is what makes drift (Closed Date − Target Date) a meaningful metric later.

**Autonomous mode**: skip the prompt; Target Date remains blank until operator sets it manually.
**Interactive mode**: prompt as documented.

---

### Step 5: Execute Tasks (Auto-Start with Parallel Execution)

**Action:**
```
AUTONOMOUS MODE (default):
  → Automatically start task execution after branch creation
  → Execute tasks following the parallel group strategy from TASK-INDEX.md
  → Continue through task groups until all complete or context limit reached

TASK EXECUTION STRATEGY:
  1. READ TASK-INDEX.md for parallel groups and dependencies
  2. START with first available task(s)
  3. FOR each parallel group:
     → Spawn concurrent task agents (one per task in the group)
     → Send ONE message with MULTIPLE Agent tool calls
     → Each agent runs task-execute for its task independently
     → Wait for ALL agents in the group to complete
     → Update TASK-INDEX.md statuses (🔲 → ✅)
     → Proceed to next group whose dependencies are now satisfied
  4. FOR serial tasks (not in a parallel group):
     → Execute sequentially via task-execute
  5. AFTER each group/task completes:
     → Check TASK-INDEX.md for next available group
     → Continue until all tasks complete or context > 70%

PARALLEL EXECUTION REQUIREMENTS:
  - All tasks in a group must have dependencies satisfied
  - Tasks must NOT modify the same files (check <relevant-files> and <parallel-safe>)
  - Each task agent uses its own task-execute invocation with full context loading
  - Track parallel tasks in current-task.md "Parallel Execution" section
  - If a parallel task fails, continue other tasks in the group — report failure at group end
  - **MAX CONCURRENCY: 6 agents per wave** (hard limit — API overload guard, tune only with evidence)
  - **PERMISSION BOUNDARY: Tasks touching `.claude/` paths MUST be sequential (main-session-only)**
    - task-create auto-marks these as `parallel-safe: false`
    - If a parallel agent is accidentally dispatched to a `.claude/` task, it will fail with "Edit denied"
    - This is EXPECTED behavior — main session picks up the task and runs it sequentially
    - See root CLAUDE.md "Sub-Agent Write Boundary" section

BUILD VERIFICATION BETWEEN WAVES (MANDATORY):
  After each wave completes, main session MUST verify the codebase still builds:
  - If any `.cs` file was modified in the wave: `dotnet build src/server/api/Sprk.Bff.Api/`
  - If any `.ts`/`.tsx` file was modified: `npm run build` in the relevant package
  - If build fails: STOP. Do not dispatch next wave. Report breakage with wave identifier.
  - This catches incoherent changes across parallel agents before they compound.

FAILURE ISOLATION:
  - One agent failing does NOT abort the wave
  - Collect all agent outcomes (success/failure/timeout)
  - At wave completion, report: "Wave X: {N} succeeded, {M} failed"
  - Mark failed tasks in TASK-INDEX as 🔄 (needs retry) not ❌ (abandoned)
  - Main session decides whether to retry failed tasks sequentially or report and stop

EXAMPLE parallel execution flow:
  Group A (tasks 010, 011, 012) — prerequisite: 001 ✅
  → Spawn 3 agents simultaneously
  → All complete → Group B available
  Group B (tasks 020, 021) — prerequisite: 012 ✅
  → Spawn 2 agents simultaneously
  → Continue...

CONTEXT MANAGEMENT during parallel execution:
  - Checkpoint after each completed group (not each individual task)
  - If context > 60%: checkpoint + compact before next group
  - If context > 70%: checkpoint + STOP + report remaining tasks

INTERACTIVE MODE (when user says "run interactively" or "with approvals"):
  → Execute one task at a time
  → Ask before proceeding to next task
  → User can review results between tasks
```

**Note on current-task.md lifecycle:**
- Created by project-setup with status: "none" (no active task yet)
- When task 001 starts → status: "in-progress", steps/files/decisions tracked
- When task 001 completes → RESETS, advances to task 002 (status: "not-started")
- Continues until project complete (status: "none", next action: "run /repo-cleanup")

---

## Error Handling

**If spec.md missing:**
```
❌ SPEC.md not found at: projects/{project-name}/spec.md

Options:
1. If you have a human design document (design.md, .docx, .pdf):
   → Run: /design-to-spec projects/{project-name}
   → This transforms your design doc into an AI-optimized spec.md

2. Write spec.md manually with:
   - Executive Summary / Purpose
   - Scope definition
   - Technical approach
   - Success criteria

Then run: /project-pipeline projects/{project-name}

Need help writing spec.md? I can help with that first.
```

**If PLAN.md generation fails:**
```
⚠️ PLAN.md generation encountered issues.

Options:
1. 'retry' - Try generating again
2. 'manual' - I'll help you write PLAN.md interactively
3. 'stop' - Exit pipeline and address issues manually
```

**If task decomposition fails:**
```
⚠️ Task file generation encountered issues.

This usually means:
- PLAN.md needs more detail in Phase Breakdown section
- Deliverables not concrete enough

Options:
1. 'refine plan' - I'll help improve PLAN.md Phase Breakdown
2. 'manual tasks' - Create task files interactively
3. 'stop' - Exit and address manually
```

---

## Usage Examples

### Example 1: Autonomous Flow (Default)
```
User: "start project ai-document-intelligence-r1"

Agent: ✅ Step 0.5: Master is current — all branch work merged.
       ✅ Step 1: SPEC.md validated (2,306 words)
       ✅ Step 1.5: No active PR overlaps detected.
       ✅ Step 2: Resources discovered (4 ADRs, 2 skills, 3 knowledge docs)
                  Artifacts generated (README, PLAN, CLAUDE.md)
       ✅ Step 3: 178 tasks created, 6 parallel groups identified
       ✅ Step 4: Branch feature/ai-document-intelligence-r1 created, PR #265 opened
       🚀 Step 5: Starting task execution...

       Executing Group 0: Task 001 (serial — foundation setup)
       ✅ Task 001 complete.

       Executing Group A: Tasks 010, 011, 012 (parallel — 3 agents)
       ✅ All 3 tasks complete.

       Executing Group B: Tasks 020, 021 (parallel — 2 agents)
       [continues autonomously...]
```

### Example 2: Interactive Flow (Explicit Request)
```
User: "start project interactively"

Agent: ✅ SPEC.md validated (2,306 words)
       [Y to proceed / refine / stop]

User: "y"
[... approval gates at each step ...]
```

### Example 2: Review Before Proceeding
```
User: "initialize project from projects/new-feature/"

Agent: ✅ SPEC.md validated
        [Y to proceed]

User: "review"

Agent: [Opens SPEC.md for review]
        Ready to generate PLAN.md? [Y to proceed]

User: "y"

Agent: ✅ PLAN.md generated
        [Y to proceed / review to view plan]

User: "review"

Agent: [Opens PLAN.md]
        PLAN.md looks good? [Y to proceed / refine {section}]

User: "refine phase 2 - break down UI tasks more"

Agent: [Updates Phase 2 with more granular tasks]
        ✅ Phase 2 refined
        [Y to proceed]

User: "y"

Agent: ✅ Tasks generated
        [Y to start task 001]
```

### Example 3: Stop and Manual Control
```
User: "start project new-api-feature"

Agent: ✅ SPEC.md validated
        [Y to proceed]

User: "y"

Agent: ✅ PLAN.md generated
        [Y to proceed]

User: "stop"

Agent: ✅ Project initialized up to PLAN.md
        
        Files created:
        - projects/new-api-feature/PLAN.md
        - projects/new-api-feature/README.md
        
        When ready to continue:
        - Run: /task-create new-api-feature
        - Or: /project-pipeline new-api-feature (restart full pipeline)
```

---

## Integration with Existing Skills

This skill **orchestrates** by calling component skills:

- **design-to-spec**: **OPTIONAL PREDECESSOR** - Transforms human design docs into AI-optimized spec.md before this skill runs
- **merge-to-master**: **INTEGRATED** at Step 0.5 for master staleness check (audit mode) — prevents new projects from starting on stale code
- **adr-aware**: Auto-invoked during resource discovery (Step 2) for ADR loading
- **conflict-check**: **INTEGRATED** at Step 1.5 for PR overlap detection (parallel session awareness)
- **project-setup**: **CALLED** at Step 2 for artifact generation (README, PLAN, CLAUDE.md, folders)
- **task-create**: Concepts integrated and called at Step 3 for task decomposition
- **ui-test**: **TASK GENERATION** - Step 3 creates `<ui-tests>` sections for PCF/frontend tasks; executed by task-execute Step 9.7
- **task-execute**: **CALLED** at Step 5 if user confirms auto-start
- **push-to-github**: Concepts used at Step 4 for feature branch and commit

**Orchestration Pattern**:
```
design-to-spec (Tier 1 - Optional Predecessor)
  └─→ Transforms design.md → spec.md (if human design doc exists)
        │
        ▼
project-pipeline (Tier 2 - Orchestrator)
  ├─→ Step 2: CALLS project-setup (Tier 1 - Component)
  │     └─→ Generates artifacts
  ├─→ Step 3: CALLS task-create (Tier 1 - Component)
  │     ├─→ Generates task files
  │     └─→ Adds <ui-tests> sections for PCF/frontend tasks
  ├─→ Step 4: Feature branch creation
  └─→ Step 5: CALLS task-execute (Tier 2 - Orchestrator)
        ├─→ Executes first task
        └─→ Step 9.7: CALLS ui-test for PCF/frontend tasks

Result: Full project initialization with human confirmation at each major step
```

**Why call component skills?**
- Single responsibility: Each skill does one thing well
- Reusability: project-setup can be used standalone or by other orchestrators
- Maintainability: Changes to artifact generation happen in one place (project-setup)
- Testability: Component skills can be tested independently

---

## Success Criteria

Pipeline successful when:
- [ ] SPEC.md validated (Step 1)
- [ ] PR overlap check completed (Step 1.5 - informational)
- [ ] Resources discovered (ADRs, skills, knowledge docs) (Step 2)
- [ ] README.md created with graduation criteria (Step 2)
- [ ] PLAN.md created with all template sections and discovered resources (Step 2)
- [ ] CLAUDE.md created with project context (Step 2)
- [ ] Folder structure created (tasks/, notes/ with subdirs) (Step 2)
- [ ] All task .poml files created (Step 3)
- [ ] TASK-INDEX.md created (Step 3)
- [ ] Deployment tasks added (if applicable) (Step 3)
- [ ] UI test definitions added to PCF/frontend tasks (Step 3 - `<ui-tests>` sections)
- [ ] Wrap-up task added (090-project-wrap-up.poml) (Step 3)
- [ ] Feature branch created and pushed to remote (Step 4)
- [ ] Initial commit made with project artifacts (Step 4)
- [ ] User confirmed ready to execute Task 001 (or declined) (Step 5)

---

## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| Pipeline skipped Step 2 ADR/resource discovery — generated artifacts have no ADR awareness | Operator ran a manual mini-version OR step was elided due to time pressure | Always run Step 2 (comprehensive resource discovery) — it's the load-bearing step connecting spec to existing ADRs/skills/patterns. No shortcut available. |
| Step 4 created feature branch but no initial commit | Pre-commit hook rejected the commit | Re-run Step 4. Common cause: hook validating commit-message format. Fix the message and retry. |
| Step 3 generated 0 task files | `plan.md` lacks a phased WBS; task-create couldn't decompose | Verify `plan.md` has a "Phase Breakdown" section with deliverables. If not, return to Step 2.2 (plan generation) and re-author. |
| Pipeline "completes" but Task 001 fails at Step 0 | Pipeline ran with insufficient effort — Step 2 missed critical ADRs | Re-run pipeline on Opus with effort `max`. Adaptive thinking won't compensate for rushed upstream resource discovery. |
| Generated CLAUDE.md references stale skills or wrong paths | Author hand-edited instead of using `project-setup` (which uses canonical `references/claudemd-template.md`) | Always invoke `project-setup` as Step 2. Don't author CLAUDE.md from memory. |
| Stale `MAX_THINKING_TOKENS=50000` setting carried forward by ops scripts | Older docs prescribed this env var; it's IGNORED on Opus 4.6+ | This was an AP-1 hit fixed 2026-05-17. On current models use `/effort max` or specify effort in the slash-command invocation. |

---

*For Claude Code: This is the recommended entry point for new projects with existing spec.md. Provides streamlined UX with human-in-loop confirmation.*

---

## Portfolio Hook (added 2026-06-23 by spaarke-devops-project-tracking-r1 task 031 · FR-17)

**At start of skill** (after Step 1 spec validation, before resource discovery): check `projects/{name}/README.md` for `> **Portfolio**:` pointer block.

- **If pointer block ABSENT**: prompt "Register this project on the portfolio? [Y/n]" → on Y, invoke `/devops-project-register` (will prompt for `--epic`).
- **If pointer block PRESENT**: silently invoke `/devops-project-sync`.

Hook is silent on success (single ✅ line). Failures degrade to ⚠️ warn; do NOT block pipeline progression (NFR-03).

Register prompt fires at most ONCE per project — subsequent runs always sync silently.

See: [`.claude/skills/devops-project-register/SKILL.md`](../devops-project-register/SKILL.md), [`.claude/skills/devops-project-sync/SKILL.md`](../devops-project-sync/SKILL.md).
