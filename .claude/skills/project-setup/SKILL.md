---
description: Generate project artifacts (README, PLAN, CLAUDE.md) from a design specification
tags: [project-setup, artifacts, scaffolding, component]
techStack: [all]
appliesTo: ["projects/*/", "create artifacts", "generate project files"]
alwaysApply: false
exemplar: none-too-volatile
last-reviewed: 2026-05-16
---

# project-setup

> **Last Reviewed**: 2026-05-16
> **Reviewed By**: ai-procedure-quality-r1 (Phase 2b Wave 2b-B — flipped frontmatter above H1; extracted CLAUDE.md template to references/)
> **Exemplar rationale**: Project artifacts are per-project; the template patterns are what's reusable. See `references/claudemd-template.md`.
> **Developer note**: This is a COMPONENT skill — typically called BY `project-pipeline`. For most users, invoke `/project-pipeline` instead.

## Prerequisites

### Claude Code Extended Context Configuration

**IMPORTANT**: Before running this skill, ensure Claude Code is configured with extended context settings:

```bash
MAX_THINKING_TOKENS=50000
CLAUDE_CODE_MAX_OUTPUT_TOKENS=64000
```

**Why Extended Context is Required**:
- Loads and processes detailed `spec.md` files (typically 1500-3000 words)
- Generates comprehensive `plan.md` with WBS (Work Breakdown Structure)
- Creates context-rich `CLAUDE.md` with technical constraints
- Often called by `project-pipeline` which performs additional resource discovery

**Verify settings before proceeding**:
```bash
# Windows PowerShell
echo $env:MAX_THINKING_TOKENS
echo $env:CLAUDE_CODE_MAX_OUTPUT_TOKENS

# Should output: 50000 and 64000
```

If not set, see root [CLAUDE.md](../../../CLAUDE.md#development-environment) for setup instructions.

---

## Purpose

**Tier 1 Component Skill (AI INTERNAL USE)** - Generates foundational project artifact files (README.md, PLAN.md, CLAUDE.md) and folder structure from a design specification. This is a pure artifact generator that does NOT perform resource discovery, branching, or task creation.

**Design Philosophy**:
- Single responsibility: artifact generation only
- No side effects (no git operations, no external calls)
- Deterministic: same input → same output
- Composable: can be called by orchestrator skills

## ⚠️ Developer Note

**This skill is for AI internal use only.** It is called BY `project-pipeline`, not invoked directly by developers.

### When This Skill Is Used

**Called By**: `project-pipeline` (Step 2 - Artifact Generation)

**Direct Developer Use**: ❌ **NOT RECOMMENDED**

### If You're a Developer

**✅ Use this instead**:
```bash
/project-pipeline projects/{project-name}
```

This orchestrates the full setup:
- Comprehensive resource discovery (ADRs, skills, patterns, knowledge docs)
- Artifact generation ← **This skill is called here**
- Task decomposition
- Feature branch creation

### Advanced Use Cases ONLY

Call this skill directly only if:
- ✅ You need to regenerate artifacts (README, PLAN, CLAUDE.md) without full pipeline
- ✅ You're debugging artifact generation logic
- ✅ You want manual control over each initialization step

**Do NOT use if**:
- ❌ You want full automated setup → Use **project-pipeline** instead
- ❌ You need task files created → Use **project-pipeline** (it calls task-create)
- ❌ You want resource discovery → Use **project-pipeline** (it performs comprehensive discovery)

## Inputs Required

| Input | Required | Source |
|-------|----------|--------|
| Project path | Yes | Path to `projects/{project-name}/` folder |
| Design specification | Yes | Must exist at `projects/{project-name}/spec.md` |

### Design Spec Location

The design specification must live at:
```
projects/{project-name}/spec.md
```

**Prerequisites (must be done before invoking this skill)**:
1. Create folder: `projects/{descriptive-project-name}/`
2. Place spec: `projects/{project-name}/spec.md`
3. Invoke: `/project-setup projects/{project-name}`

## Workflow

### Step 1: Validate Inputs

```
IF project path provided:
  EXTRACT project-name from folder name
ELSE:
  ASK user for project path

IF projects/{project-name}/spec.md does NOT exist:
  → STOP - "spec.md not found. Please create projects/{project-name}/spec.md first."

IF projects/{project-name}/README.md already exists:
  → WARN - "Project already initialized. Continue anyway?"
  → Offer to view existing project or re-initialize
```

### Step 2: Load Templates and Context

```
LOAD templates:
  - .claude/templates/project-README.template.md
  - .claude/templates/project-plan.template.md

LOAD projects/{project-name}/spec.md

EXTRACT from spec:
  - Problem statement
  - Proposed solution
  - Scope (in-scope / out-of-scope)
  - Success criteria / acceptance criteria
  - Technical constraints
  - Key requirements
```

### Step 3: Create Folder Structure

```
projects/{project-name}/
├── spec.md            # Design specification (input - already exists)
├── README.md          # Project overview (generated)
├── plan.md            # Implementation plan (generated)
├── CLAUDE.md          # AI context file for this project (generated)
├── current-task.md    # Active task state tracker (generated - for context recovery)
├── tasks/             # Task files go here
│   └── .gitkeep
└── notes/             # Ephemeral working files
    ├── .gitkeep
    ├── debug/         # Debugging session artifacts
    ├── spikes/        # Exploratory code/research
    ├── drafts/        # Work-in-progress content
    └── handoffs/      # Context reset summaries
```

**Notes directory purpose**: Store temporary artifacts during development (debug logs, spike code, drafts, handoff summaries). Contents are ephemeral and may be removed after project completion by repo-cleanup skill.

**current-task.md purpose**: Tracks active task state for context recovery across compaction. See [Context Recovery Protocol](../../../docs/procedures/context-recovery.md).

### Step 4: Generate README.md

Use `project-README.template.md` structure:

**Required Sections**:
- **Title**: Project name in Title Case (derived from folder name)
- **Quick Links**: Pre-fill with relative paths:
  - `plan.md`
  - `tasks/TASK-INDEX.md`
  - `spec.md`
- **Overview**: High-level summary (2-3 sentences extracted from spec)
- **Problem Statement**: Direct copy from design spec "Problem" or "Background" section
- **Proposed Solution**: High-level approach from spec "Solution" or "Approach" section
- **Scope**:
  - In-scope items (bulleted list from spec)
  - Out-of-scope items (bulleted list from spec)
- **Graduation Criteria**: Success criteria from spec as measurable checklist

**Template Guidance**:
- Keep README readable in under 2 minutes
- Use present tense ("This project provides...")
- Make graduation criteria measurable (not vague like "improve performance")

### Step 5: Generate plan.md

Use `project-plan.template.md` structure:

**Required Sections**:

1. **Executive Summary**
   - Purpose (what and why)
   - Scope (boundaries)
   - Timeline estimate (based on complexity)

2. **Architecture Context**
   - Key architectural constraints from spec
   - Technology stack
   - Integration points

3. **Implementation Approach**
   - Phase structure overview
   - Critical path
   - Dependencies

4. **WBS (Work Breakdown Structure)**
   - Create phases based on spec sections:

   | Spec Section | Generates Phase |
   |--------------|-----------------|
   | Data Model | Phase: Data Model & Schema Setup |
   | API Endpoints | Phase: API Development |
   | UI Components | Phase: Frontend Development |
   | PCF Controls | Phase: PCF Control Implementation |
   | Integration | Phase: Integration & Wiring |
   | Security | Phase: Authentication & Authorization |
   | Testing | Phase: Testing & Validation |
   | Deployment | Phase: Deployment & Go-Live |

   - Each phase should have:
     - Phase number and title
     - Objectives (what will be achieved)
     - Key deliverables (concrete outputs)
     - Inputs (what's needed to start)
     - Outputs (what's produced)
     - Dependencies (other phases or external factors)

5. **Dependencies**
   - External dependencies (services, APIs, resources)
   - Internal dependencies (shared libraries, components)

6. **Testing Strategy**
   - Unit testing approach
   - Integration testing approach
   - Acceptance testing approach

7. **Acceptance Criteria**
   - Copy from graduation criteria in README
   - Add verification steps

8. **Risk Register**
   - Extract risks from spec constraints
   - Add mitigation strategies

9. **Next Steps**
   - Immediate next actions
   - Reference to task creation

**Estimation Guidance** (for timeline in Executive Summary):
- Base estimate on WBS phases and spec complexity
- Don't include specific dates (user controls timeline)
- Express as effort range (e.g., "Estimated effort: 15-20 days")

### Step 6: Generate CLAUDE.md

Create project-specific AI context file. **The canonical template** lives at [`references/claudemd-template.md`](references/claudemd-template.md) — copy that template, then replace `{placeholders}` (project name, dates, ADR list, key constraints) with project-specific values.

Key sections the generated CLAUDE.md MUST contain (from the template):

1. **Project Status** — Phase / Last Updated / Current Task / Next Action
2. **Quick Reference** — Key Files + Project Metadata
3. **Context Loading Rules** — what to load on session start
4. **🚨 MANDATORY: Task Execution Protocol** — bind every "work on task X" trigger to the `task-execute` skill
5. **Multi-File Work Decomposition** — when to parallelize vs serialize
6. **Key Technical Constraints** — extracted from `spec.md` (ADRs, tech stack rules)
7. **Decisions Made** — empty initially; updated as project progresses
8. **Implementation Notes** — gotchas, workarounds, learnings
9. **Resources** — applicable ADRs, related projects, external docs

See `references/claudemd-template.md` for the full template body to paste in.

### Step 7: Generate current-task.md

Create initial task state tracker from template:

```
COPY template: .claude/templates/current-task.template.md
  → projects/{project-name}/current-task.md

UPDATE placeholders:
  - Project: {project-name}
  - Task ID: none
  - Status: none
  - All other fields: initial/empty state

PURPOSE:
  - Enables context recovery after compaction or new sessions
  - Task-execute skill will update this file during task work
  - See: docs/procedures/context-recovery.md
```

### Step 8: Output Summary

```
✅ Project artifacts created: projects/{project-name}/

Files generated:
  ✅ README.md - Project overview and graduation criteria
  ✅ plan.md - Implementation plan with WBS
  ✅ CLAUDE.md - AI context file
  ✅ current-task.md - Active task state tracker (context recovery)
  ✅ tasks/.gitkeep - Task folder (empty, ready for task-create)
  ✅ notes/.gitkeep - Notes folder with subdirectories
  ✅ notes/debug/.gitkeep
  ✅ notes/spikes/.gitkeep
  ✅ notes/drafts/.gitkeep
  ✅ notes/handoffs/.gitkeep

Existing files (not modified):
  📄 spec.md - Design specification (input)

Next steps:
  1. Review README.md and plan.md for accuracy
  2. Run /task-create to decompose plan into executable task files
  3. Or use /project-pipeline to automate the full pipeline

Note: This skill does NOT create:
  ❌ Task files (use task-create or project-pipeline)
  ❌ Feature branch (use project-pipeline or manual git commands)
  ❌ Resource discovery (use project-pipeline for ADR/skill/knowledge loading)
```

## What This Skill Does NOT Do

To maintain single responsibility, this skill explicitly does NOT:

- ❌ **Resource Discovery**: Does not search for related ADRs, skills, or knowledge docs (use project-pipeline for this)
- ❌ **Task Creation**: Does not create task files (use task-create or project-pipeline)
- ❌ **Feature Branching**: Does not create git branches (use project-pipeline or manual git)
- ❌ **Git Commits**: Does not commit files (handled by orchestrators or manual)
- ❌ **Task Execution**: Does not execute tasks (use task-execute)

These responsibilities belong to orchestrator skills (like project-pipeline) or should be done manually.

## Conventions

### Naming
- Project folder: `kebab-case` (e.g., `sdap-refactor`, `ai-document-intelligence-r1`)
- Files: lowercase with hyphens
- No abbreviations in project names unless well-known (e.g., `sdap`, `spe`, `ai`)

### Content Standards
- README.md: Readable in under 2 minutes
- plan.md: WBS phases map to logical milestones
- Each phase: 3-7 deliverables (decompose further if more)
- CLAUDE.md: Keep updated as project progresses

### Graduation Criteria Requirements
Every project must have measurable graduation criteria:
- ✅ At least one functional requirement (feature works as specified)
- ✅ At least one quality requirement (tests pass, no regressions)
- ✅ Optional: performance, security, documentation requirements
- ❌ NO vague criteria like "improve performance" or "better UX"

## Resources

### Templates (Auto-loaded)
- `.claude/templates/project-README.template.md`
- `.claude/templates/project-plan.template.md`

### Related Skills
- **project-pipeline**: Orchestrator that calls this skill (RECOMMENDED for most users)
- **task-create**: Creates task files from plan.md (run after this skill)
- **task-execute**: Executes individual tasks (run after task-create)

## Integration with Other Skills

### Called By (Upstream)
- **project-pipeline** - Calls this skill at Step 2 after resource discovery

### Calls (Downstream)
- None (this is a component skill with no dependencies)

### Complements
- **task-create** - Natural next step after using this skill
- **repo-cleanup** - Validates structure created by this skill at project end

## Examples

### Example 1: Standalone Use (Advanced)

**Trigger**: `/project-setup projects/sdap-refactor`

**Prerequisites**:
```
projects/sdap-refactor/
└── spec.md  (created by user, 2000 words, has all required sections)
```

**Result**:
```
projects/sdap-refactor/
├── spec.md             # Input (existed before)
├── README.md           # Generated - project overview
├── plan.md             # Generated - 6 phases, WBS structure
├── CLAUDE.md           # Generated - AI context
├── tasks/.gitkeep
└── notes/
    ├── .gitkeep
    ├── debug/
    ├── spikes/
    ├── drafts/
    └── handoffs/
```

**Next Action**: User manually runs `/task-create projects/sdap-refactor`

---

### Example 2: Called by Orchestrator

**Trigger**: User runs `/project-pipeline projects/ai-doc-summary`

**Process**:
```
project-pipeline Step 1: Validate spec.md ✅
project-pipeline Step 2: Resource discovery ✅
  → Found 4 ADRs, 2 skills, 3 guides

project-pipeline Step 2 (continued): Generate artifacts
  → CALLS: project-setup projects/ai-doc-summary
    ✅ README.md created
    ✅ plan.md created
    ✅ CLAUDE.md created
  → project-setup returns

project-pipeline Step 3: Create tasks...
```

---

### Example 3: Missing Spec Error

**Trigger**: `/project-setup projects/new-feature`

**Result** (if spec.md doesn't exist):
```
❌ Cannot initialize: projects/new-feature/spec.md not found.

Please create the spec file first:
  1. Create folder: projects/new-feature/
  2. Add design spec: projects/new-feature/spec.md
     Required sections:
     - Executive Summary / Purpose
     - Problem Statement
     - Proposed Solution
     - Scope (in-scope / out-of-scope)
     - Success Criteria / Acceptance Criteria
  3. Re-run: /project-setup projects/new-feature

Or use /project-pipeline for full automated setup.
```

---

### Example 4: Already Initialized

**Trigger**: `/project-setup projects/existing-project`

**Result** (if README.md already exists):
```
⚠️  Warning: projects/existing-project/README.md already exists.

Options:
  1. 'continue' - Regenerate all files (will overwrite existing README, PLAN, CLAUDE.md)
  2. 'view' - Show existing README.md
  3. 'stop' - Cancel operation

[Your choice: continue / view / stop]
```

## Validation Checklist

Before completing project-setup, verify:

- [ ] spec.md exists and was read successfully
- [ ] Project name derived from folder name correctly
- [ ] README.md has problem statement from spec
- [ ] README.md has measurable graduation criteria (not vague)
- [ ] plan.md has at least one WBS phase with deliverables
- [ ] plan.md phases map logically to spec sections
- [ ] CLAUDE.md references spec.md as source
- [ ] CLAUDE.md has project metadata filled in
- [ ] Folder structure created (tasks/, notes/ with subdirs)
- [ ] No PII or secrets in any generated file
- [ ] All file paths use forward slashes (cross-platform)

---

## Summary

**project-setup** is a **Tier 1 Component Skill** that generates project artifacts from a design specification. It is:
- ✅ Focused (artifact generation only)
- ✅ Deterministic (same input → same output)
- ✅ Reusable (called by orchestrators or standalone)
- ✅ Side-effect-free (no git operations, no external calls)

For most users, **use project-pipeline instead** - it orchestrates this skill along with resource discovery, task creation, and branching.

---

## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| Generated CLAUDE.md has unfilled `{placeholders}` left in body | Author copy-pasted the template but forgot to fill in project name, dates, or ADR list | Always grep generated CLAUDE.md for `{[a-z]` patterns before completing project setup — they signal unfilled placeholders. |
| Skill invoked directly when user actually needed `/project-pipeline` | User typed "create project artifacts" instead of "start project" | The Developer Note explicitly redirects to project-pipeline. If you find yourself doing resource discovery or task creation inside this skill, you've drifted out of scope — stop and switch to project-pipeline. |
| Project artifacts created but folder structure incomplete (missing `tasks/`, `notes/`, etc.) | Skill ran but template enforcement was loose | Always create the full directory tree (per Conventions section) — empty directories should have `.gitkeep` placeholders. |
| Two `## Resources` sections appeared in generated CLAUDE.md | Old SKILL.md duplication propagated through generation | Fixed in Wave 2b-B (2026-05-16) by extracting the canonical template to `references/claudemd-template.md`. The template has ONE `## Resources` section; the duplicate has been removed. |
| Generated CLAUDE.md references stale skill names | Template references a skill that has been renamed (e.g., `design-to-project` → `design-to-spec`) | Template references skills indirectly ("the task-execute skill") so renames don't break it. But if a renamed skill is referenced by its old name, fix the template at `references/claudemd-template.md`. |

---

*For Claude Code: This is a component skill. If a user requests full project setup, recommend /project-pipeline instead of this skill directly.*
