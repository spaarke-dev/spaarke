# Skill Interaction Guide

> **Purpose**: Comprehensive guide to Spaarke skill usage procedures, interaction patterns, and workflows.
>
> **Audience**: Claude Code (AI agent) and human operators
>
> **Last Updated**: December 20, 2025

---

## Table of Contents

1. [Overview](#overview)
2. [Extended Context Configuration](#extended-context-configuration)
3. [Skill Categories](#skill-categories)
4. [Primary Workflows](#primary-workflows)
5. [Skill Interaction Patterns](#skill-interaction-patterns)
6. [Decision Trees](#decision-trees)
7. [Invocation Rules](#invocation-rules)
8. [Common Patterns](#common-patterns)

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

## Extended Context Configuration

### Prerequisites for Project Pipeline Skills

**CRITICAL**: Skills involved in project initialization require extended context settings:

```bash
MAX_THINKING_TOKENS=50000
CLAUDE_CODE_MAX_OUTPUT_TOKENS=64000
```

**Why Extended Context is Required**:
- **Multi-phase projects**: Projects like AI Document Intelligence R1 have 100+ tasks across 8 phases
- **Deep resource discovery**: Pipeline loads ADRs, knowledge docs, patterns, and existing code
- **Context-rich task execution**: Each task includes full project history and applicable constraints
- **Pipeline orchestration**: `project-pipeline` chains multiple component skills sequentially
- **Large spec documents**: Design specs are typically 1500-5000 words

**Real-World Example**:
For the AI Document Intelligence R1 project:
- spec.md: 2,306 words
- 4 ADRs loaded (ADR-013, ADR-014, ADR-015, ADR-016)
- 8 knowledge docs discovered
- 178 tasks generated with full context

**Setting in Windows**:
```cmd
setx MAX_THINKING_TOKENS "50000"
setx CLAUDE_CODE_MAX_OUTPUT_TOKENS "64000"
```

**Verification**:
```powershell
echo $env:MAX_THINKING_TOKENS
echo $env:CLAUDE_CODE_MAX_OUTPUT_TOKENS
# Should output: 50000 and 64000
```

### Skills Requiring Extended Context

| Skill | Context Need | Reason |
|-------|--------------|--------|
| **design-to-spec** | High | Ingests 2000-5000 word design docs, preliminary resource discovery |
| **project-pipeline** | Critical | Orchestrates multiple skills, comprehensive resource discovery |
| **project-setup** | Medium | Processes 1500-3000 word specs, generates comprehensive artifacts |
| **task-create** | Medium | Creates 50-200+ task files with tag-to-knowledge mapping |

**If not set**, pipeline skills may fail or produce incomplete results.

---

## Skill Categories

### 1. Project Lifecycle Skills

**Purpose**: Manage project creation, task decomposition, and execution

| Skill | Tier | Purpose | Developer-Facing | AI Internal |
|-------|------|---------|------------------|-------------|
| **design-to-spec** | 1 (Component) | Transform human design → AI-optimized spec.md | ✅ Yes | ❌ No |
| **project-pipeline** | 2 (Orchestrator) | Spec → Ready Tasks (full automation) | ✅ **RECOMMENDED** | ❌ No |
| **project-setup** | 1 (Component) | Generate artifacts (README, PLAN, CLAUDE.md) | ❌ No | ✅ Yes (called by pipeline) |
| **task-create** | 1 (Component) | Decompose PLAN.md → task files | ❌ No | ✅ Yes (called by pipeline) |
| **task-execute** | 2 (Orchestrator) | Execute a single task with full context | ✅ Yes (natural language) | ❌ No |
| **repo-cleanup** | 3 (Operational) | Validate structure, remove ephemeral files | ✅ Yes (after completion) | ❌ No |

**Developer Workflow** (2 Steps):
```
Step 1: design-to-spec (if starting from human design doc)
         ↓
Step 2: project-pipeline (full automation: artifacts + tasks + branch)
         ↓
Step 3: task-execute (natural language: "work on task 001")
```

**AI Internal Skills** (called by orchestrators, NOT by developers):
- `project-setup` - Called by `project-pipeline` Step 2
- `task-create` - Called by `project-pipeline` Step 3

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
  IF task has tag="deploy":
    🔧 CALLS: dataverse-deploy
      → Follow deployment procedure

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
Have human design doc? → YES → /design-to-spec projects/{name}
                              → Then: /project-pipeline projects/{name} ⭐

Have spec.md already?  → YES → /project-pipeline projects/{name} ⭐

Have nothing yet?      → Create design.md or spec.md first
                         → Option A: /design-to-spec (if design.md)
                         → Option B: /project-pipeline (if spec.md)
```

### When Executing Tasks

```
Have task file? → YES → Natural language: "work on task 002"
                      → OR explicit: /task-execute projects/{name}/tasks/002-*.poml
                → NO  → Work directly (always-apply skills active)
```

### When You Need

| Need | Command | Developer-Facing | AI Internal |
|------|---------|------------------|-------------|
| Transform design doc to spec | `/design-to-spec projects/{name}` | ✅ Yes | ❌ No |
| Full project setup | `/project-pipeline projects/{name}` ⭐ | ✅ Yes | ❌ No |
| Just artifacts (advanced) | `/project-setup projects/{name}` | ⚠️ Advanced | ✅ Yes (called by pipeline) |
| Just tasks (advanced) | `/task-create projects/{name}` | ⚠️ Advanced | ✅ Yes (called by pipeline) |
| Execute a task | `work on task {NNN}` | ✅ Yes | ❌ No |
| Review code | `/code-review` | ✅ Yes | ❌ No |
| Check ADRs | `/adr-check` | ✅ Yes | ❌ No |
| Deploy PCF/solution | `/dataverse-deploy` | ✅ Yes | ❌ No |
| Edit ribbon | `/ribbon-edit` | ✅ Yes | ❌ No |
| Pull changes | `/pull-from-github` | ✅ Yes | ❌ No |
| Push changes | `/push-to-github` | ✅ Yes | ❌ No |
| Cleanup repo | `/repo-cleanup` | ✅ Yes | ❌ No |

### Skill Dependency Chain

```
design-to-spec (Developer-Facing)
  └─→ Preliminary resource discovery (constraints only)
        └─→ Generates: spec.md
              │
              └─→ Handoff to project-pipeline

project-pipeline (Developer-Facing)
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
        └─→ code-review (after code)
        └─→ adr-check (after code)
        └─→ dataverse-deploy (if tagged)
        └─→ ribbon-edit (if ribbon task)

task-execute (Developer-Facing - Natural Language)
  💬 Invoked by: "work on task 002" OR "continue with next task"
  └─→ (Same dependencies as above)
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

**Next Steps After Reading This Guide**:
1. Review individual skill files for detailed procedures
2. See `.claude/skills/INDEX.md` for complete skill registry
3. Reference this guide when uncertain about skill interactions
4. Update this guide when adding new skills or interaction patterns

---

*This guide is the authoritative source for skill interaction patterns in the Spaarke codebase.*
