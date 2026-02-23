# Skill Interaction Guide

> **Purpose**: Playbook for how skills work together — decision trees, interaction patterns, and detailed workflows.
>
> **Last Updated**: January 6, 2026 (added ui-test skill and Step 9.7 integration)

---

## How This File Is Used

**This file is loaded when Claude needs to understand skill composition and workflows:**

1. **Complex Decision Making**: When determining which skill to invoke for a multi-step task, Claude consults the decision trees here.

2. **Skill Orchestration**: When an orchestrator skill (like `project-pipeline`) needs to call component skills, this file defines the interaction patterns.

3. **Workflow Execution**: Detailed step-by-step workflows for common scenarios (new project, task execution, etc.).

**This file is NOT for:**
- Looking up what skills exist (see [INDEX.md](INDEX.md))
- Extended context configuration (see root `CLAUDE.md`)
- Individual skill procedures (see `.claude/skills/{name}/SKILL.md`)

**Related Files**:
| File | Role | When to Use |
|------|------|-------------|
| [INDEX.md](INDEX.md) | Skill registry | Look up what skills exist, their triggers |
| **This file (GUIDE)** | Interaction playbook | Understand how skills work together |
| `.claude/skills/{name}/SKILL.md` | Individual skill | Execute a specific skill's procedure |
| Root `CLAUDE.md` | Entry point | Extended context config, skill trigger phrases |

---

## Table of Contents

1. [Overview](#overview)
2. [Skill Tiers](#skill-tiers)
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
| **Tier 3** | Operational | azure-deploy, dataverse-deploy, ribbon-edit | ❌ No (domain-specific) |
| **Tier 0** | Always-Apply | adr-aware, spaarke-conventions | N/A (automatic) |

---

## Primary Workflows

> **Note**: For the complete list of available skills, their triggers, and categories, see [INDEX.md](INDEX.md).

### Workflow 1: New Project from Design Document (RECOMMENDED)

**Scenario**: You have a human-written design document and want to initialize a complete project.

```
┌─────────────────────────────────────────────────────────────┐
│       WORKFLOW 1: NEW PROJECT FROM DESIGN DOCUMENT          │
└─────────────────────────────────────────────────────────────┘

PHASE A: DESIGN TRANSFORMATION (Optional - if starting from human design doc)

Step A1: Create Project Folder & Design Document
  📁 projects/{project-name}/
  📄 projects/{project-name}/design.md (or .docx, .pdf)

Step A2: Transform to AI-Optimized Spec
  💬 User: "/design-to-spec projects/{project-name}"
  🤖 Claude: Loads design-to-spec skill

Step A3: Extract Core Elements
  🔍 Extract: Purpose, scope, requirements, success criteria
  📋 Flag missing/unclear elements for user clarification
  ⏸️  Wait for user: clarify gaps or proceed

Step A4: Preliminary Technical Context Discovery
  🔍 Identify resource types from design content
  📜 Load applicable ADRs for CONSTRAINTS ONLY
     - API endpoints → ADR-001, ADR-008, ADR-010 constraints
     - PCF controls → ADR-006, ADR-011, ADR-012 constraints
     - Plugins → ADR-002 constraints
  ⚠️  SCOPE: Preliminary only (for spec enrichment)
       ❌ DO NOT: Load full code patterns, detailed guides
       ✅ FULL discovery happens in project-pipeline Step 2

Step A5: Generate spec.md
  ✅ Creates: projects/{project-name}/spec.md (AI-optimized)
  📋 Includes: Structured requirements, ADR constraints, file paths
  ⏸️  Wait for user: Review spec.md before proceeding

Step A6: Handoff to Pipeline
  ⏸️  User choice: 'y' to proceed to project-pipeline | 'done' to stop
  IF 'y': → Continue to PHASE B

---

PHASE B: PROJECT INITIALIZATION (Full Automation)

Step B1: Invoke Orchestrator
  💬 User: "/project-pipeline projects/{project-name}"
  🤖 Claude: Loads project-pipeline skill

Step B2: Validation (project-pipeline Step 1)
  🔍 Validate spec.md exists and has required sections
  ✅ Output: "SPEC.md validated - ready for planning"
  ⏸️  Wait for user: 'y' to proceed

Step B3: Comprehensive Resource Discovery (project-pipeline Step 2)
  🔍 Extract keywords from spec.md
  📜 Load FULL ADRs (not just constraints)
     - Complete ADR content with decision rationale
  📚 Search .claude/skills/ for applicable skills
  📖 Search docs/ai-knowledge/ for guides and patterns
  💻 Find existing code examples
  ⚠️  SCOPE: Comprehensive (for task creation and implementation)
       ✅ Full ADR content, patterns, code examples
  ✅ Output: "Discovered X ADRs, Y skills, Z guides, N code examples"

Step B4: Generate Artifacts (project-pipeline Step 2 continued)
  🔧 CALLS: project-setup (AI Internal)
    → Creates README.md (project overview)
    → Creates PLAN.md (implementation plan)
    → Creates CLAUDE.md (AI context file)
    → Creates tasks/ folder
    → Creates notes/ folder structure
  🔧 ENHANCE artifacts with discovered resources
    → Insert "Discovered Resources" section in PLAN.md
    → Populate "Applicable ADRs" section in CLAUDE.md
  ✅ Output: "Artifacts generated and enriched"
  ⏸️  Wait for user: 'y' to proceed

Step B5: Create Task Files (project-pipeline Step 3)
  🔧 CALLS: task-create (AI Internal)
    → Decomposes PLAN.md phases into tasks
    → Creates tasks/NNN-{slug}.poml files (50-200+ tasks)
    → Creates tasks/TASK-INDEX.md
    → Applies tag-to-knowledge mapping
    → Embeds discovered resources in each task
    → Adds deployment tasks (if applicable)
    → Adds wrap-up task (090-project-wrap-up.poml)
  ✅ Output: "X tasks created with full context"
  ⏸️  Wait for user: 'y' to proceed

Step B6: Create Feature Branch (project-pipeline Step 4)
  🔧 Git operations:
    → git checkout -b feature/{project-name}
    → git add projects/{project-name}/
    → git commit -m "feat: initialize {project-name} project"
    → git push -u origin feature/{project-name}
  ✅ Output: "Feature branch created and pushed"

Step B7: Optional Auto-Start (project-pipeline Step 5)
  ⏸️  Wait for user: 'y' to start task 001 | 'done' to exit
  IF 'y':
    🔧 CALLS: task-execute projects/{project-name}/tasks/001-*.poml
    → Loads task file
    → Loads knowledge files (from <knowledge> section)
    → Loads ADRs (via adr-aware)
    → Executes task steps
    → (Session continues with task 001 execution)
  ELSE:
    ✅ Output: "Project ready! Say 'work on task 001' when ready."

---

PHASE C: TASK EXECUTION (Ongoing)

Step C1: Execute Tasks (Natural Language)
  💬 User says: "work on task 002" OR "continue with next task"
  🤖 Claude: Automatically invokes task-execute skill
      → (See Workflow 2 for task execution details)

  Alternative: Explicit invocation
  💬 User: "/task-execute projects/{project-name}/tasks/002-*.poml"

Step C2: Project Wrap-up (Final Task)
  💬 User: "work on task 090" (or final task number)
  🔧 CALLS: task-execute → repo-cleanup
    → Validate repository structure
    → Remove ephemeral files from notes/
    → Update README status to "Complete"
    → Create lessons-learned.md
```

**Key Decision Points**:
- After Step A5: User can refine spec.md before proceeding
- After Step B2: User can stop to refine spec.md further
- After Step B4: User can review/edit artifacts
- After Step B5: User can review/modify tasks
- Step B7: User decides whether to start task 001 immediately or later

**Resource Discovery Distinction**:
- **Preliminary (design-to-spec)**: ADR constraints only for spec enrichment
- **Comprehensive (project-pipeline)**: Full ADRs, patterns, code examples for implementation

---

### Workflow 2: Execute Single Task

**Scenario**: Execute one task file with full context loading.

```
┌─────────────────────────────────────────────────────────────┐
│                WORKFLOW 2: EXECUTE SINGLE TASK              │
└─────────────────────────────────────────────────────────────┘

Step 1: Invoke Task Execution (Natural Language)
  💬 User says: "work on task 001" OR "continue with next task"
  🤖 Claude: Automatically invokes task-execute skill

  Alternative (Explicit):
  💬 User: "/task-execute projects/{project}/tasks/001-*.poml"
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
  IF task has tag="azure" or "infrastructure":
    🔧 CALLS: azure-deploy
      → Follow Azure deployment procedure

  IF task has tag="deploy" + "pcf":
    🔧 CALLS: pcf-deploy
      → Follow PCF build/pack/import procedure

  IF task has tag="deploy" or "dataverse" (non-PCF):
    🔧 CALLS: dataverse-deploy
      → Follow Dataverse deployment procedure

  IF task involves ribbon:
    🔧 CALLS: ribbon-edit
      → Follow ribbon edit procedure
```

---

### Workflow 3: Manual Project Setup (Advanced - NOT RECOMMENDED)

**Scenario**: Advanced users who need direct control over artifact generation without full pipeline orchestration.

⚠️ **WARNING**: This workflow uses AI-internal component skills directly. Most developers should use Workflow 1 (project-pipeline) instead.

```
┌─────────────────────────────────────────────────────────────┐
│   WORKFLOW 3: MANUAL PROJECT SETUP (Advanced Users Only)   │
└─────────────────────────────────────────────────────────────┘

Step 1: Create Artifacts Only (AI Internal Skill)
  💬 User: "/project-setup projects/{project-name}"
  🤖 Claude: Loads project-setup skill (Tier 1 - Component)
  ⚠️  NOTE: This is an AI-internal skill normally called by project-pipeline

  Generates:
    ✅ README.md
    ✅ PLAN.md
    ✅ CLAUDE.md
    ✅ tasks/ folder
    ✅ notes/ folder structure

  Does NOT:
    ❌ Discover resources (ADRs, skills, patterns)
    ❌ Create task files
    ❌ Create feature branch
    ❌ Enrich artifacts with discovered context

Step 2: Manual Task Creation (AI Internal Skill)
  💬 User: "/task-create projects/{project-name}"
  🤖 Claude: Loads task-create skill (Tier 1 - Component)
  ⚠️  NOTE: This is an AI-internal skill normally called by project-pipeline

  Generates:
    ✅ tasks/NNN-{slug}.poml files
    ✅ tasks/TASK-INDEX.md
    ✅ Tag-to-knowledge mapping applied

  Missing:
    ❌ Resource discovery context (no comprehensive ADR loading)
    ❌ Code examples and patterns

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

**When to Use Manual Workflow (RARE)**:
- ✅ Debugging artifact generation logic
- ✅ Regenerating artifacts without full pipeline
- ✅ Learning how component skills work internally

**Do NOT Use If**:
- ❌ Starting a new project → Use project-pipeline (Workflow 1)
- ❌ Need comprehensive resource discovery → Use project-pipeline
- ❌ Want automated branching and task creation → Use project-pipeline

---

## Skill Interaction Patterns

### Pattern 1: Orchestrator Calls Component (Composition)

**Definition**: Tier 2 (Orchestrator) skills call Tier 1 (Component) skills to compose functionality.

**Example: project-pipeline orchestrates project initialization**:
```
project-pipeline (Tier 2 - Orchestrator, Developer-Facing)
  │
  ├─→ Step 2: CALLS project-setup (Tier 1 - Component, AI Internal)
  │     └─→ Returns: README.md, PLAN.md, CLAUDE.md, folder structure
  │
  ├─→ Step 3: CALLS task-create (Tier 1 - Component, AI Internal)
  │     └─→ Returns: 50-200+ task files with full context
  │
  └─→ Step 5: CALLS task-execute (Tier 2 - Orchestrator, Developer-Facing)
        └─→ Returns: Task 001 completed (optional auto-start)

Result: Full project initialization with human checkpoints
```

**Example: design-to-spec feeds into project-pipeline**:
```
design-to-spec (Tier 1 - Component, Developer-Facing)
  │
  ├─→ Step 3: Preliminary resource discovery (constraints only)
  │     └─→ Returns: spec.md enriched with ADR constraints
  │
  └─→ Handoff to project-pipeline (User confirms 'y')
        │
        └─→ project-pipeline Step 2: Comprehensive resource discovery
              └─→ Returns: Full ADRs, patterns, code examples
```

**Rules**:
- Orchestrators coordinate multiple components
- Components do NOT call other components (except design-to-spec optionally invoking project-pipeline)
- Orchestrators handle human interaction and decision points
- AI-internal components (project-setup, task-create) should NOT be directly invoked by developers

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
- conflict-check (on-demand overlap detection)

**Rules**:
- These skills affect repository state globally
- Require explicit user intent
- Should NOT be auto-invoked by task execution

---

### Pattern 6: Conflict Detection Pattern (Parallel Sessions)

**Definition**: Proactive detection of file overlap when running multiple Claude Code sessions simultaneously.

**When to Use**:
- Running 3-5 Claude Code sessions in parallel via git worktrees
- Before starting a new project (check for overlapping active PRs)
- Before merging a PR (detect potential conflicts)
- At end of task (sync check with master)

**Example: Project Planning Overlap Check (project-pipeline Step 1.5)**:
```
project-pipeline validates spec.md
  ↓
  Step 1.5: Overlap Detection
    ├─→ Identify likely files from spec.md
    │   (PCF controls → src/client/pcf/)
    │   (API endpoints → src/server/api/)
    ├─→ Check active PRs for overlapping files
    │   gh pr list --json number,title,files
    ├─→ Compare file lists
    ↓
  IF overlap detected:
    ⚠️ WARN user:
      "PR #98 also modifies .claude/skills/"
      "Recommendation: Coordinate file ownership"
    ⏸️  Wait for user: 'y' to proceed with awareness
  ↓
  Step 2: Continue to resource discovery
```

**Example: End-of-Task Sync Check (task-execute Step 10.6)**:
```
task-execute completes implementation
  ↓
  Step 10.6: Conflict Sync Check
    ├─→ git fetch origin master
    ├─→ Check for new master commits
    │   git log HEAD..origin/master --oneline
    ├─→ Compare master changes with task files
    ↓
  IF master changed files you modified:
    ⚠️ WARN: "Master has changes to files you modified"
    RECOMMEND: "Rebase before pushing"
  ↓
  Step 10.7: Transition to next task
```

**Example: On-Demand Conflict Check (/conflict-check)**:
```
💬 User: "/conflict-check"
  ↓
  Claude: Loads conflict-check skill
    ├─→ Get current branch files
    │   git diff --name-only origin/master...HEAD
    ├─→ Get active PR files
    │   gh pr list --json number,files
    ├─→ Calculate overlaps
    ↓
  IF overlaps found:
    Output: "⚠️ PR #101 overlaps on: src/client/pcf/"
    Recommendations:
      1. Merge PR #101 first
      2. Designate file ownership
      3. Frequent rebase pattern
  ELSE:
    Output: "✅ No conflicts detected"
```

**Integration Points**:
| Skill | When | Purpose |
|-------|------|---------|
| project-pipeline | Step 1.5 | Detect overlap before project setup |
| task-execute | Step 10.6 | End-of-task master sync check |
| push-to-github | Pre-push | Detect overlap before PR update |
| conflict-check | On-demand | Manual overlap check anytime |

**Parallel Session Setup** (uses worktree-setup skill):
```
Main repo: C:\code_files\spaarke\ [master]
  ↓
  git worktree add ../spaarke-wt-feature-a -b feature/feature-a
  git worktree add ../spaarke-wt-feature-b -b feature/feature-b
  ↓
Result:
  Session 1: C:\code_files\spaarke-wt-feature-a\
  Session 2: C:\code_files\spaarke-wt-feature-b\
  (Each VS Code window has isolated branch)
```

**Merge Order Strategy**:
```
Session 1 done → Rebase → Merge PR #101
  ↓
Session 2: git fetch && git rebase origin/master
  ↓
Session 2 done → Merge PR #102
```

**Rules**:
- Overlap detection is informational, not blocking
- User decides whether to proceed with awareness
- Coordinate file ownership for same-file work
- Use sequential merge pattern for conflict-free merges

---

## Decision Trees

### Decision Tree 1: How Should I Start This Project?

```
START: I have a project to work on
  │
  ├─ Do I have a design spec or design document?
  │   │
  │   ├─ I have a HUMAN DESIGN DOC (design.md, .docx, .pdf)
  │   │   │
  │   │   └─ Step 1: Transform to AI-optimized spec
  │   │       /design-to-spec projects/{name}
  │   │       │
  │   │       └─ This creates spec.md with:
  │   │           - Structured requirements
  │   │           - ADR constraints (preliminary)
  │   │           - File paths and context
  │   │           │
  │   │           └─ Step 2: Proceed to project-pipeline
  │   │               /project-pipeline projects/{name} ⭐ RECOMMENDED
  │   │
  │   ├─ I have an AI-OPTIMIZED SPEC (spec.md already exists)
  │   │   │
  │   │   ├─ Do I want fully automated setup?
  │   │   │   │
  │   │   │   ├─ YES → Use project-pipeline ⭐ RECOMMENDED
  │   │   │   │        /project-pipeline projects/{name}
  │   │   │   │        - Comprehensive resource discovery
  │   │   │   │        - Artifact generation
  │   │   │   │        - 50-200+ task files
  │   │   │   │        - Feature branch creation
  │   │   │   │        - Optional auto-start task 001
  │   │   │   │
  │   │   │   └─ NO (want manual control) ⚠️ ADVANCED ONLY
  │   │   │        ├─ Generate artifacts only (AI Internal Skill)
  │   │   │        │   /project-setup projects/{name}
  │   │   │        │   ⚠️ Missing: Resource discovery
  │   │   │        │
  │   │   │        ├─ Review/edit artifacts
  │   │   │        │
  │   │   │        └─ Create tasks manually (AI Internal Skill)
  │   │   │            /task-create projects/{name}
  │   │   │            ⚠️ Missing: Comprehensive context
  │   │   │
  │   │   └─ Do artifacts already exist (README, PLAN)?
  │   │       │
  │   │       ├─ YES but NO tasks → Use task-create
  │   │       │        /task-create projects/{name}
  │   │       │        ⚠️ AI Internal - normally called by pipeline
  │   │       │
  │   │       └─ NO artifacts → Start with project-pipeline
  │   │
  │   └─ NO (no spec.md or design doc)
  │       │
  │       └─ Create design document or spec.md first
  │           │
  │           ├─ Option A: Write human design doc
  │           │   - Create: projects/{name}/design.md
  │           │   - Include: problem, solution, scope, criteria
  │           │   - Then: /design-to-spec projects/{name}
  │           │
  │           └─ Option B: Write AI spec directly
  │               - Create: projects/{name}/spec.md
  │               - Use template: docs/ai-knowledge/templates/spec.template.md
  │               - Then: /project-pipeline projects/{name}
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
  │   ├─ YES → Use task-execute (Natural Language)
  │   │        💬 User says: "work on task 002" OR "continue with next task"
  │   │        🤖 Claude: Automatically invokes task-execute skill
  │   │
  │   │        Alternative (Explicit):
  │   │        "/task-execute projects/{name}/tasks/002-*.poml"
  │   │
  │   │        task-execute will automatically:
  │   │        ├─ Load knowledge files (from task <knowledge> section)
  │   │        ├─ Load ADRs (adr-aware based on tags)
  │   │        ├─ Apply conventions (spaarke-conventions)
  │   │        └─ Run quality gates (code-review, adr-check)
  │   │
  │   └─ NO → Is this a known operation?
  │       │
  │       ├─ Deploy to Azure → azure-deploy
  │       │
  │       ├─ Deploy PCF control → pcf-deploy
  │       │
  │       ├─ Deploy to Dataverse (non-PCF) → dataverse-deploy
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
          ├─ /azure-deploy (Azure infrastructure, BFF API)
          ├─ /dataverse-deploy (Dataverse, PCF, solutions)
          └─ /ribbon-edit (ribbon customizations)
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
- ✅ azure-deploy handles Azure infrastructure independently
- ✅ dataverse-deploy completes deployment independently
- ✅ ribbon-edit handles full ribbon edit cycle
- ❌ azure-deploy should NOT call dataverse-deploy
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

  IF "azure" or "infrastructure" in tags:
    → Call azure-deploy

  IF "deploy" or "dataverse" in tags:
    → Call dataverse-deploy

  IF "ribbon" in tags:
    → Call ribbon-edit

  IF "api" in tags:
    → adr-aware loads API-related ADRs
```

---

## Summary

### Quick Decision Trees

**Starting a New Project**:
```
Have human design doc? → /design-to-spec → /project-pipeline ⭐
Have spec.md already?  → /project-pipeline ⭐
Have nothing?          → Create design.md or spec.md first
```

**Executing Tasks**:
```
Have task file? → Natural language: "work on task 002"
No task file?   → Work directly (always-apply skills active)
```

> **For command reference**: See [INDEX.md](INDEX.md) for the complete list of skills and their triggers.

### Skill Dependency Chain

```
design-to-spec (Developer-Facing)
  └─→ Preliminary resource discovery (constraints only)
        └─→ Generates: spec.md
              │
              └─→ Handoff to project-pipeline

project-pipeline (Developer-Facing)
  ├─→ Step 1.5: conflict-check (overlap detection - informational)
  │     └─→ Warns about active PRs touching same files
  │
  ├─→ Comprehensive resource discovery (full ADRs, patterns, code)
  │
  ├─→ CALLS: project-setup (AI Internal)
  │     └─→ No dependencies
  │
  ├─→ CALLS: task-create (AI Internal)
  │     └─→ adr-aware (implicit)
  │
  └─→ CALLS: task-execute (Developer-Facing, optional auto-start)
        └─→ adr-aware (implicit)
        └─→ spaarke-conventions (implicit)
        └─→ code-review (Step 9.5 - after code)
        └─→ adr-check (Step 9.5 - after code)
        └─→ ui-test (Step 9.7 - if pcf/frontend, requires --chrome)
        └─→ azure-deploy (if azure/infrastructure tagged)
        └─→ pcf-deploy (if pcf + deploy tagged)
        └─→ dataverse-deploy (if deploy/dataverse tagged, non-PCF)
        └─→ ribbon-edit (if ribbon task)
        └─→ conflict-check (Step 10.6 - sync check, parallel sessions)

task-execute (Developer-Facing - Natural Language)
  💬 Invoked by: "work on task 002" OR "continue with next task"
  └─→ (Same dependencies as above)

worktree-setup (Developer-Facing - Parallel Sessions)
  💬 Invoked by: "create worktree", "setup worktree for project"
  └─→ Creates isolated worktree for parallel development
  └─→ Enables running multiple Claude Code sessions simultaneously
```

### Resource Discovery Levels

| Skill | Discovery Type | Scope | Purpose |
|-------|---------------|-------|---------|
| **design-to-spec** | Preliminary | ADR constraints only | Enrich spec.md with architecture boundaries |
| **project-pipeline** | Comprehensive | Full ADRs, patterns, code examples | Support task creation and implementation |

**Key Distinction**:
- Preliminary = "What are the rules?" (constraints for spec)
- Comprehensive = "How do I implement this?" (full context for tasks)

---

**Next Steps**:
1. **Look up a skill**: See [INDEX.md](INDEX.md) for the complete registry
2. **Execute a skill**: See `.claude/skills/{name}/SKILL.md` for the procedure
3. **Understand interactions**: Reference this guide for how skills work together

---

*Last updated: January 6, 2026*
