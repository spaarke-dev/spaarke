# Skill Interaction Guide

> **Purpose**: Comprehensive guide to Spaarke skill usage procedures, interaction patterns, and workflows.
>
> **Audience**: Claude Code (AI agent) and human operators
>
> **Last Updated**: December 18, 2024

---

## Table of Contents

1. [Overview](#overview)
2. [Skill Categories](#skill-categories)
3. [Primary Workflows](#primary-workflows)
4. [Skill Interaction Patterns](#skill-interaction-patterns)
5. [Decision Trees](#decision-trees)
6. [Invocation Rules](#invocation-rules)
7. [Common Patterns](#common-patterns)

---

## Overview

### What Are Skills?

Skills are **structured procedures** that Claude Code follows when performing specific tasks. Each skill:
- Has a clear, focused purpose
- Defines when it should be invoked
- Specifies what other skills it calls
- Documents its inputs, outputs, and side effects

### Skill Architecture Principles

```
┌─────────────────────────────────────────────────────────┐
│                  SKILL DESIGN PRINCIPLES                │
├─────────────────────────────────────────────────────────┤
│                                                         │
│  1. SINGLE RESPONSIBILITY                              │
│     Each skill does ONE thing well                     │
│                                                         │
│  2. CLEAR BOUNDARIES                                   │
│     No overlapping functionality                       │
│                                                         │
│  3. COMPOSABILITY                                      │
│     Skills can call other skills                       │
│                                                         │
│  4. EXPLICIT INVOCATION                                │
│     Clear triggers and commands                        │
│                                                         │
│  5. DOCUMENTED INTERACTIONS                            │
│     Dependencies and call patterns explicit            │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

### Skill Tiers

Skills are organized in three tiers by complexity and scope:

| Tier | Type | Example | Calls Other Skills |
|------|------|---------|-------------------|
| **Tier 1** | Component | project-setup, task-create | ❌ No (pure operations) |
| **Tier 2** | Orchestrator | project-pipeline, task-execute | ✅ Yes (compose Tier 1) |
| **Tier 3** | Operational | dataverse-deploy, ribbon-edit | ❌ No (domain-specific) |
| **Tier 0** | Always-Apply | adr-aware, spaarke-conventions | N/A (automatic) |

---

## Skill Categories

### 1. Project Lifecycle Skills

**Purpose**: Manage project creation, task decomposition, and execution

| Skill | Tier | Purpose | User-Facing |
|-------|------|---------|-------------|
| **project-pipeline** | 2 (Orchestrator) | Spec → Ready Tasks (full automation) | ✅ **RECOMMENDED** |
| **project-setup** | 1 (Component) | Generate artifacts (README, PLAN, CLAUDE.md) | ⚠️ Advanced users only |
| **task-create** | 1 (Component) | Decompose PLAN.md → task files | ⚠️ Manual workflow |
| **task-execute** | 2 (Orchestrator) | Execute a single task with full context | ✅ Primary |
| **repo-cleanup** | 3 (Operational) | Validate structure, remove ephemeral files | ✅ After completion |

**Primary Workflow**:
```
User → project-pipeline → project-setup → task-create → task-execute (per task) → repo-cleanup
```

---

### 2. Always-Apply Skills (Tier 0)

**Purpose**: Automatically enforce standards and architecture compliance

| Skill | Applied When | Purpose |
|-------|--------------|---------|
| **adr-aware** | Before writing any code | Proactively load relevant ADRs based on resource type |
| **spaarke-conventions** | During all code writing | Apply naming conventions, patterns, standards |

**Invocation**: Automatic (implicit) - no explicit call needed

**Example Flow**:
```
task-execute starts
  → (implicit) Load adr-aware for API endpoint
  → (implicit) Apply spaarke-conventions during coding
  → (explicit) Run code-review after coding
  → (explicit) Run adr-check for validation
```

---

### 3. Quality & Validation Skills

**Purpose**: Post-hoc validation and quality gates

| Skill | Tier | When Invoked | Purpose |
|-------|------|--------------|---------|
| **code-review** | 3 (Operational) | After implementing code in task | Security, performance, style review |
| **adr-check** | 3 (Operational) | After code changes or on demand | Validate all ADR compliance |

**Invocation**: Explicit call by orchestrator skills (e.g., task-execute) or manual

---

### 4. Platform Operations Skills

**Purpose**: Domain-specific operations for Dataverse/Power Platform

| Skill | Tier | When Invoked | Purpose |
|-------|------|--------------|---------|
| **dataverse-deploy** | 3 (Operational) | Deploy-tagged tasks or manual | Deploy solutions, PCF, web resources via PAC CLI |
| **ribbon-edit** | 3 (Operational) | Ribbon customization tasks or manual | Solution export → edit ribbon XML → import |

**Invocation**: Explicit via task tags or manual command

---

### 5. Git Operations Skills

**Purpose**: Repository management and GitHub integration

| Skill | Tier | When Invoked | Purpose |
|-------|------|--------------|---------|
| **pull-from-github** | 3 (Operational) | Manual or before starting work | Fetch + merge with conflict resolution |
| **push-to-github** | 3 (Operational) | Manual or after completing work | Commit + push following conventions, create PR |

**Invocation**: Manual only (never auto-invoked by other skills)

---

## Primary Workflows

### Workflow 1: New Project from Spec (RECOMMENDED)

**Scenario**: You have a design specification and want to initialize a complete project.

```
┌─────────────────────────────────────────────────────────────┐
│              WORKFLOW 1: NEW PROJECT FROM SPEC              │
└─────────────────────────────────────────────────────────────┘

Step 1: Create Project Folder & Spec
  📁 projects/{project-name}/
  📄 projects/{project-name}/spec.md (design specification)

Step 2: Invoke Orchestrator
  💬 User: "/project-pipeline projects/{project-name}"
  🤖 Claude: Loads project-pipeline skill

Step 3: Validation (project-pipeline Step 1)
  🔍 Validate spec.md exists and has required sections
  ✅ Output: "SPEC.md validated - ready for planning"
  ⏸️  Wait for user: 'y' to proceed

Step 4: Resource Discovery (project-pipeline Step 2)
  🔍 Extract keywords from spec.md
  📚 Search .claude/skills/ for applicable skills
  📖 Search docs/ai-knowledge/ for guides
  📜 Load applicable ADRs via adr-aware
  ✅ Output: "Discovered X ADRs, Y skills, Z guides"

Step 5: Generate Artifacts (project-pipeline Step 2)
  🔧 CALLS: project-setup
    → Creates README.md (project overview)
    → Creates PLAN.md (implementation plan)
    → Creates CLAUDE.md (AI context file)
    → Creates tasks/ folder
    → Creates notes/ folder structure
  ✅ Output: "Artifacts generated"
  ⏸️  Wait for user: 'y' to proceed

Step 6: Create Task Files (project-pipeline Step 3)
  🔧 CALLS: task-create (or inline)
    → Decomposes PLAN.md phases into tasks
    → Creates tasks/NNN-{slug}.poml files
    → Creates tasks/TASK-INDEX.md
    → Applies tag-to-knowledge mapping
    → Adds deployment tasks (if applicable)
    → Adds wrap-up task (090-project-wrap-up.poml)
  ✅ Output: "X tasks created"
  ⏸️  Wait for user: 'y' to proceed

Step 7: Create Feature Branch (project-pipeline Step 3.5)
  🔧 Git operations:
    → git checkout -b feature/{project-name}
    → git add projects/{project-name}/
    → git commit -m "feat: initialize {project-name} project"
    → git push -u origin feature/{project-name}
  ✅ Output: "Feature branch created and pushed"

Step 8: Optional Auto-Start (project-pipeline Step 4)
  ⏸️  Wait for user: 'y' to start task 001
  IF 'y':
    🔧 CALLS: task-execute projects/{project-name}/tasks/001-*.poml
    → Loads task file
    → Loads knowledge files (from <knowledge> section)
    → Loads ADRs (via adr-aware)
    → Executes task steps
  ELSE:
    ✅ Output: "Project ready! Run 'execute task 001' when ready."

Step 9: Execute Remaining Tasks (Manual Loop)
  FOR each task in TASK-INDEX.md:
    💬 User: "execute task {NNN}"
    🔧 CALLS: task-execute
      → (See Workflow 2 for task execution details)

Step 10: Project Wrap-up (Final Task)
  💬 User: "execute task 090" (or final task number)
  🔧 CALLS: task-execute → repo-cleanup
    → Validate repository structure
    → Remove ephemeral files from notes/
    → Update README status to "Complete"
    → Create lessons-learned.md
```

**Key Decision Points**:
- After Step 3: User can stop to refine spec.md
- After Step 5: User can review/edit artifacts
- After Step 6: User can review/modify tasks
- Step 8: User decides whether to start immediately or later

---

### Workflow 2: Execute Single Task

**Scenario**: Execute one task file with full context loading.

```
┌─────────────────────────────────────────────────────────────┐
│                WORKFLOW 2: EXECUTE SINGLE TASK              │
└─────────────────────────────────────────────────────────────┘

Step 1: Invoke Task Execution
  💬 User: "execute task 001" OR "work on task 001"
  🤖 Claude: Loads task-execute skill

Step 2: Locate Task File
  🔍 Search for: projects/{project}/tasks/001-*.poml
  ✅ Found: projects/{project}/tasks/001-setup-environment.poml

Step 3: Load Task Context (task-execute)
  📄 Parse task file (POML/XML)
  📋 Extract: metadata, prompt, steps, acceptance criteria

Step 4: Load Knowledge Files (task-execute)
  📚 Read <knowledge><files> section from task
  📖 Load each file listed (ADRs, guides, references)

  Example from task file:
  <knowledge>
    <files>
      docs/reference/adr/ADR-001-minimal-api-and-workers.md
      docs/ai-knowledge/guides/SPAARKE-ARCHITECTURE.md
      src/server/api/CLAUDE.md
    </files>
  </knowledge>

Step 5: Load ADRs (adr-aware - Always-Apply)
  🏛️  Based on <metadata><tags>:
    - If tag="api" → Load ADR-001, ADR-007, ADR-008, ADR-010
    - If tag="pcf" → Load ADR-006, ADR-011, ADR-012
    - If tag="plugin" → Load ADR-002
    - (See adr-aware skill for full mapping)

Step 6: Execute Task Steps (task-execute)
  📋 Follow <steps> section sequentially
  🔧 Use <tools> guidance for Claude Code capabilities
  ✅ Generate <outputs> as specified

  During execution:
    🛡️  (implicit) Apply spaarke-conventions
    🛡️  (implicit) Reference loaded ADRs for constraints

Step 7: Validate Outputs (task-execute)
  ✅ Check all <outputs> were created
  ✅ Run <acceptance-criteria> verification steps

Step 8: Quality Gates (task-execute)
  IF code was written:
    🔧 CALLS: code-review
      → Security review
      → Performance review
      → Style compliance

    🔧 CALLS: adr-check
      → Validate ADR compliance
      → Report violations if any

  IF quality issues found:
    ⚠️  Fix issues before marking complete

Step 9: Update Task Status (task-execute)
  📝 Mark task as completed in TASK-INDEX.md
  ✅ Output: "Task 001 complete. Next: execute task 002"

Step 10: Special Task Types
  IF task has tag="deploy":
    🔧 CALLS: dataverse-deploy
      → Follow deployment procedure

  IF task involves ribbon:
    🔧 CALLS: ribbon-edit
      → Follow ribbon edit procedure
```

---

### Workflow 3: Manual Project Setup (Advanced)

**Scenario**: Need more control, create artifacts manually without full pipeline.

```
┌─────────────────────────────────────────────────────────────┐
│            WORKFLOW 3: MANUAL PROJECT SETUP                 │
└─────────────────────────────────────────────────────────────┘

Step 1: Create Artifacts Only
  💬 User: "/project-setup projects/{project-name}"
  🤖 Claude: Loads project-setup skill (Tier 1 - Component)

  Generates:
    ✅ README.md
    ✅ PLAN.md
    ✅ CLAUDE.md
    ✅ tasks/ folder
    ✅ notes/ folder structure

  Does NOT:
    ❌ Discover resources (ADRs, skills)
    ❌ Create task files
    ❌ Create feature branch

Step 2: Manual Task Creation
  💬 User: "/task-create projects/{project-name}"
  🤖 Claude: Loads task-create skill (Tier 1 - Component)

  Generates:
    ✅ tasks/NNN-{slug}.poml files
    ✅ tasks/TASK-INDEX.md
    ✅ Tag-to-knowledge mapping applied

Step 3: Manual Branch & Commit
  💬 User: (Manually via bash or push-to-github skill)

  Commands:
    git checkout -b feature/{project-name}
    git add projects/{project-name}/
    git commit -m "feat: initialize {project-name}"
    git push -u origin feature/{project-name}

Step 4: Execute Tasks
  (Same as Workflow 2 - Execute Single Task)
```

**When to Use Manual Workflow**:
- Need to review/modify artifacts before creating tasks
- Want to customize PLAN.md extensively
- Project structure already partially exists
- Learning how the pipeline works

---

## Skill Interaction Patterns

### Pattern 1: Orchestrator Calls Component (Composition)

**Definition**: Tier 2 (Orchestrator) skills call Tier 1 (Component) skills to compose functionality.

**Example**:
```
project-pipeline (Tier 2 - Orchestrator)
  ├─→ CALLS project-setup (Tier 1 - Component)
  │     └─→ Returns: Artifacts created
  ├─→ CALLS task-create (Tier 1 - Component)
  │     └─→ Returns: Task files created
  └─→ CALLS task-execute (Tier 2 - Orchestrator)
        └─→ Returns: Task completed

Result: Full project initialization with human-in-loop
```

**Rules**:
- Orchestrators coordinate multiple components
- Components do NOT call other components
- Orchestrators handle human interaction and decision points

---

### Pattern 2: Always-Apply Skills (Implicit Invocation)

**Definition**: Tier 0 skills are automatically applied during relevant operations.

**Example**:
```
task-execute starts executing task 005 (API endpoint)
  ↓
  (Automatic) adr-aware detects tag="api"
    → Loads ADR-001 (Minimal API)
    → Loads ADR-007 (SpeFileStore facade)
    → Loads ADR-008 (Endpoint filters for auth)
    → Loads ADR-010 (DI minimalism)
  ↓
  Claude writes API endpoint code
  ↓
  (Automatic) spaarke-conventions applied
    → PascalCase for C# files
    → Concrete types not interfaces (per ADR-010)
    → Endpoint filters for auth (per ADR-008)
  ↓
  task-execute completes
```

**Rules**:
- Always-Apply skills NEVER need explicit invocation
- They activate based on context (tags, file types, operations)
- They are implicit dependencies of orchestrator skills

---

### Pattern 3: Quality Gate Pattern (Sequential Validation)

**Definition**: After producing code, run validation skills in sequence.

**Example**:
```
task-execute implements code
  ↓
  Check: Was code written?
  ↓ (YES)
  ├─→ CALL code-review
  │     ├─→ Security check
  │     ├─→ Performance check
  │     └─→ Style check
  ↓
  ├─→ CALL adr-check
  │     └─→ Validate ADR compliance
  ↓
  IF issues found:
    ├─→ Fix issues
    └─→ Re-run checks
  ↓
  ELSE:
    └─→ Mark task complete
```

**Rules**:
- Quality gates run AFTER implementation, not before
- Multiple validation skills can run in sequence
- Failed validation blocks task completion

---

### Pattern 4: Domain-Specific Operations (Explicit Invocation)

**Definition**: Tier 3 operational skills invoked explicitly by orchestrators or manually.

**Example: Deployment Task**
```
task-execute loads task 010-deploy-pcf.poml
  ↓
  Read <metadata><tags>: ["deploy", "pcf"]
  ↓
  Execute <steps> section
    ↓
    Step mentions "deploy PCF control"
    ↓
    EXPLICIT CALL: dataverse-deploy
      ├─→ Detect PCF control type
      ├─→ Run: pac pcf push
      ├─→ Verify deployment
      └─→ Return: Success/Failure
  ↓
  Continue remaining steps
  ↓
  task-execute completes
```

**Rules**:
- Domain skills are NOT always-apply
- Must be explicitly called by orchestrator or user
- Usually triggered by task tags or keywords

---

### Pattern 5: Manual Operations (No Auto-Invocation)

**Definition**: Some skills are NEVER auto-invoked, only manual.

**Example: Git Operations**
```
User completes several tasks
  ↓
  User decides to commit and push
  ↓
  💬 User: "/push-to-github"
  ↓
  Claude: Loads push-to-github skill
    ├─→ Run git status
    ├─→ Run git diff
    ├─→ Draft commit message
    ├─→ Stage files
    ├─→ Commit with Spaarke conventions
    └─→ Push to remote

NOTE: This is NEVER automatically called by task-execute or other skills
```

**Skills with Manual-Only Pattern**:
- pull-from-github
- push-to-github
- repo-cleanup (except in wrap-up task)

**Rules**:
- These skills affect repository state globally
- Require explicit user intent
- Should NOT be auto-invoked by task execution

---

## Decision Trees

### Decision Tree 1: How Should I Start This Project?

```
START: I have a project to work on
  │
  ├─ Do I have a design spec (spec.md)?
  │   │
  │   ├─ YES
  │   │   │
  │   │   ├─ Do I want fully automated setup?
  │   │   │   │
  │   │   │   ├─ YES → Use project-pipeline ⭐ RECOMMENDED
  │   │   │   │        /project-pipeline projects/{name}
  │   │   │   │
  │   │   │   └─ NO (want manual control)
  │   │   │        ├─ Generate artifacts only
  │   │   │        │   /project-setup projects/{name}
  │   │   │        │
  │   │   │        ├─ Review/edit artifacts
  │   │   │        │
  │   │   │        └─ Create tasks manually
  │   │   │            /task-create projects/{name}
  │   │   │
  │   │   └─ Do artifacts already exist (README, PLAN)?
  │   │       │
  │   │       ├─ YES → Just create tasks
  │   │       │        /task-create projects/{name}
  │   │       │
  │   │       └─ NO → Start with project-pipeline
  │   │
  │   └─ NO (no spec.md)
  │       │
  │       └─ Create spec.md first
  │           ├─ Create folder: projects/{name}/
  │           ├─ Write spec.md with:
  │           │   - Problem statement
  │           │   - Solution approach
  │           │   - Scope
  │           │   - Acceptance criteria
  │           └─ Then use project-pipeline
  │
  └─ Is this just a small task (no full project)?
      │
      └─ YES → Work directly without project structure
          Just start coding with always-apply skills active
```

---

### Decision Tree 2: Which Skill Should Execute This Task?

```
START: I need to work on something
  │
  ├─ Is there a task file (.poml)?
  │   │
  │   ├─ YES → Use task-execute
  │   │        "execute task {NNN}"
  │   │
  │   │        task-execute will automatically:
  │   │        ├─ Load knowledge files
  │   │        ├─ Load ADRs (adr-aware)
  │   │        ├─ Apply conventions
  │   │        └─ Run quality gates
  │   │
  │   └─ NO → Is this a known operation?
  │       │
  │       ├─ Deploy to Dataverse → dataverse-deploy
  │       │
  │       ├─ Edit ribbon → ribbon-edit
  │       │
  │       ├─ Code review → code-review
  │       │
  │       ├─ Check ADRs → adr-check
  │       │
  │       ├─ Git operations → pull-from-github or push-to-github
  │       │
  │       └─ Just coding
  │           → Work directly, always-apply skills active
  │
  └─ Is this project wrap-up/cleanup?
      │
      └─ YES → repo-cleanup
```

---

### Decision Tree 3: When Should I Invoke a Skill Explicitly vs. Rely on Always-Apply?

```
START: I'm about to write code
  │
  ├─ Do I need to load specific ADRs first?
  │   │
  │   ├─ NO → adr-aware handles this automatically
  │   │        (based on resource type: API, PCF, Plugin, etc.)
  │   │
  │   └─ YES (unusual/specific ADR need)
  │       → Manually load the ADR file
  │          (Read the ADR before coding)
  │
  ├─ Do I need to apply naming conventions?
  │   │
  │   └─ NO explicit action needed
  │       → spaarke-conventions applies automatically
  │          (PascalCase, camelCase, file naming, etc.)
  │
  ├─ Do I need to validate code after writing?
  │   │
  │   ├─ Part of task-execute? → Automatic
  │   │
  │   └─ Manual coding session → Explicitly invoke
  │       ├─ /code-review
  │       └─ /adr-check
  │
  └─ Do I need to deploy or do platform operations?
      │
      └─ Explicitly invoke domain skill
          ├─ /dataverse-deploy
          └─ /ribbon-edit
```

---

## Invocation Rules

### Rule 1: Orchestrators Own Human Interaction

**Principle**: Only Tier 2 (Orchestrator) skills should wait for user input or present choices.

**Examples**:
- ✅ project-pipeline waits after each step: "Y to proceed / stop to exit"
- ✅ task-execute may ask user to clarify ambiguous requirements
- ❌ project-setup should NOT prompt user (pure generation)
- ❌ task-create should NOT wait for confirmation (called by orchestrator)

**Reasoning**: Avoids nested confirmation prompts and unclear interaction flows.

---

### Rule 2: Components Are Pure Operations

**Principle**: Tier 1 (Component) skills should be deterministic and side-effect-free where possible.

**Examples**:
- ✅ project-setup: Input (spec.md) → Output (README, PLAN, CLAUDE.md)
- ✅ task-create: Input (PLAN.md) → Output (task/*.poml files)
- ❌ Component skills should NOT make git commits
- ❌ Component skills should NOT deploy to external services

**Reasoning**: Makes components reusable, testable, and predictable.

---

### Rule 3: Always-Apply Skills Never Block

**Principle**: Tier 0 (Always-Apply) skills must never require user input or halt execution.

**Examples**:
- ✅ adr-aware silently loads ADRs based on context
- ✅ spaarke-conventions applies patterns without confirmation
- ❌ Always-Apply skills should NOT ask "Which ADR should I load?"
- ❌ Always-Apply skills should NOT wait for approval

**Reasoning**: They are implicit dependencies; blocking would break all workflows.

---

### Rule 4: Domain Skills Are Self-Contained

**Principle**: Tier 3 (Operational) domain skills should NOT call other skills.

**Examples**:
- ✅ dataverse-deploy completes deployment independently
- ✅ ribbon-edit handles full ribbon edit cycle
- ❌ dataverse-deploy should NOT call push-to-github
- ❌ ribbon-edit should NOT call code-review

**Reasoning**: Keeps domain skills focused and avoids circular dependencies.

---

### Rule 5: Manual Skills Require Explicit User Intent

**Principle**: Skills that affect global repository state must be manually invoked.

**Examples**:
- ✅ User explicitly runs: /push-to-github
- ✅ User explicitly runs: /pull-from-github
- ❌ task-execute should NOT auto-commit after each task
- ❌ project-pipeline should NOT auto-push to remote without user confirmation

**Reasoning**: Prevents unintended commits, pushes, or destructive operations.

---

## Common Patterns

### Pattern: Progressive Automation

Start manual, automate as confidence grows:

1. **Learning Phase**: Use manual workflow (project-setup → task-create → task-execute)
2. **Confidence Phase**: Use project-pipeline but stop before task execution
3. **Full Automation**: Use project-pipeline with auto-start task 001

### Pattern: Checkpoint Pattern

Orchestrators should provide checkpoints for user review:

```
project-pipeline
  Step 1: Validate spec → ⏸️ Checkpoint
  Step 2: Generate artifacts → ⏸️ Checkpoint
  Step 3: Create tasks → ⏸️ Checkpoint
  Step 4: Auto-start (optional) → ⏸️ Checkpoint
```

### Pattern: Context Loading Chain

Skills load progressively more specific context:

```
project-pipeline (broad)
  → Loads: spec.md, ADR index, skill index

  → Calls: project-setup (focused)
      → Loads: Templates, spec sections

  → Calls: task-execute (specific)
      → Loads: Task file, knowledge files, specific ADRs
```

### Pattern: Fail-Fast Validation

Validate early in the workflow to avoid wasted work:

```
project-pipeline Step 1: Validate spec.md
  ├─ Check file exists
  ├─ Check required sections present
  ├─ Check minimum word count
  └─ IF validation fails → STOP (don't proceed to generation)
```

### Pattern: Tag-Based Dispatch

Use task tags to determine which domain skills to invoke:

```
task-execute loads task file
  → Read <metadata><tags>

  IF "deploy" in tags:
    → Call dataverse-deploy

  IF "ribbon" in tags:
    → Call ribbon-edit

  IF "api" in tags:
    → adr-aware loads API-related ADRs
```

---

## Summary: Quick Reference

### When Starting a New Project

```
Have spec.md? → YES → /project-pipeline projects/{name} ⭐
              → NO  → Create spec.md first, then /project-pipeline
```

### When Executing Tasks

```
Have task file? → YES → execute task {NNN}
                → NO  → Work directly (always-apply active)
```

### When You Need

| Need | Command |
|------|---------|
| Full project setup | `/project-pipeline projects/{name}` |
| Just artifacts | `/project-setup projects/{name}` |
| Just tasks | `/task-create projects/{name}` |
| Execute a task | `execute task {NNN}` |
| Review code | `/code-review` |
| Check ADRs | `/adr-check` |
| Deploy PCF/solution | `/dataverse-deploy` |
| Edit ribbon | `/ribbon-edit` |
| Pull changes | `/pull-from-github` |
| Push changes | `/push-to-github` |
| Cleanup repo | `/repo-cleanup` |

### Skill Dependency Chain

```
project-pipeline
  └─→ project-setup
        └─→ (no dependencies)
  └─→ task-create
        └─→ adr-aware (implicit)
  └─→ task-execute
        └─→ adr-aware (implicit)
        └─→ spaarke-conventions (implicit)
        └─→ code-review (after code)
        └─→ adr-check (after code)
        └─→ dataverse-deploy (if tagged)
        └─→ ribbon-edit (if ribbon task)
```

---

**Next Steps After Reading This Guide**:
1. Review individual skill files for detailed procedures
2. See `.claude/skills/INDEX.md` for complete skill registry
3. Reference this guide when uncertain about skill interactions
4. Update this guide when adding new skills or interaction patterns

---

*This guide is the authoritative source for skill interaction patterns in the Spaarke codebase.*
