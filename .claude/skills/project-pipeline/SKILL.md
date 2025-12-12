# project-pipeline

---
description: Automated pipeline from SPEC.md to ready-to-execute tasks with human-in-loop confirmation
tags: [project-pipeline, orchestration, automation]
techStack: [all]
appliesTo: ["projects/*/", "start project", "initialize project"]
alwaysApply: false
---

## Purpose

**Streamlined project initialization pipeline** that automatically chains: SPEC.md validation → PLAN.md generation → Task decomposition → Ready to execute Task 001.

**Human-in-Loop**: After each step, present results and ask for confirmation before proceeding. Default to "proceed" (user just says 'y').

## When to Use

- User says "start project", "initialize project from spec", or "run project pipeline"
- Explicitly invoked with `/project-pipeline {project-path}`
- A `spec.md` file exists at `projects/{project-name}/spec.md`

## Pipeline Steps

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

**Wait for User**: `y` (proceed) | `refine {instructions}` | `stop`

---

### Step 2: Generate PLAN.md

**Action:**
```
LOAD templates:
  - docs/ai-knowledge/templates/project-plan.template.md

LOAD context:
  - projects/{project-name}/spec.md
  - Applicable ADRs (via adr-aware skill)
  - Existing patterns from src/

DISCOVER RESOURCES (Step 2.5 from project-init):
  1. Extract keywords from spec.md
  2. Search .claude/skills/INDEX.md for applicable skills
  3. Search docs/ai-knowledge/ for relevant guides
  4. Load referenced ADRs
  5. Output: "Discovered Resources" section in PLAN.md

GENERATE: projects/{project-name}/PLAN.md
  Using template structure:
  1. Executive Summary (purpose, scope, timeline)
  2. Architecture Context (constraints, decisions, discovered resources)
  3. Implementation Approach (phase structure, critical path)
  4. Phase Breakdown (objectives, deliverables, inputs, outputs)
  5. Dependencies (external, internal)
  6. Testing Strategy
  7. Acceptance Criteria
  8. Risk Register
  9. Next Steps

CREATE: projects/{project-name}/README.md
  - Project overview
  - Quick start guide
  - Status tracking
  - Success criteria
```

**Output to User:**
```
✅ PLAN.md generated (487 lines):
   - 5 phases identified
   - 178 estimated tasks
   - 625 hours estimated effort
   - 10 week timeline

📄 Files created:
   - projects/ai-document-intelligence-r1/PLAN.md
   - projects/ai-document-intelligence-r1/README.md

📋 Next Step: Decompose PLAN.md into executable task files

[Y to proceed / review to view plan / refine {section} to edit / stop to exit]
```

**Wait for User**: `y` (proceed) | `review` (open PLAN.md) | `refine {instructions}` | `stop`

---

### Step 3: Generate Task Files

**Action:**
```
LOAD:
  - projects/{project-name}/plan.md (Phase Breakdown section)
  - docs/ai-knowledge/templates/task-execution.template.md (POML format)
  - Tag-to-knowledge mapping (from task-create skill)

REQUIREMENTS (from task-create):
  - Each task file MUST follow the task-execution.template.md structure (root <task id="..." project="...">)
  - Each task MUST include <knowledge><files> and it MUST NOT be empty
  - PCF tasks MUST include docs/ai-knowledge/guides/PCF-V9-PACKAGING.md and src/client/pcf/CLAUDE.md
  - Applicable ADRs MUST be included via docs/reference/adr/*.md (see task-create Step 3.5)

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

ADD deployment tasks (per task-create Step 3.6):
  - After each phase that produces deployable artifacts
  - Tag: deploy

ADD wrap-up task (mandatory per task-create Step 3.7):
  - Final task: 090-project-wrap-up.poml (or next available)
  - Updates README status to Complete
  - Creates lessons-learned.md
  - Archives project artifacts

CREATE: projects/{project-name}/tasks/TASK-INDEX.md
  - Registry of all tasks with status
  - Dependencies graph
  - Critical path
  - High-risk items
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

**Wait for User**: `y` (start task 001) | `review {task-number}` | `stop`

---

### Step 4: Execute Task 001 (Optional Auto-Start)

**Action:**
```
IF user said 'y':
  → INVOKE task-execute skill with projects/{project-name}/tasks/001-*.poml
  → This loads:
    - Task file (POML)
    - Knowledge files (from <knowledge> section)
    - ADRs (via adr-aware based on tags)
    - Context from PLAN.md and README.md

IF user said 'stop':
  → OUTPUT:
    "✅ Project initialized and ready!
     
     When ready to start:
     - Execute: `work on task 001`
     - Or: `execute task 001`
     
     Task-execute will load all necessary context automatically."
```

---

## Error Handling

**If spec.md missing:**
```
❌ SPEC.md not found at: projects/{project-name}/spec.md

To use project-pipeline:
1. Create project folder: projects/{descriptive-name}/
2. Write spec.md with:
   - Executive Summary / Purpose
   - Scope definition
   - Technical approach
   - Success criteria
3. Run: /project-pipeline projects/{descriptive-name}

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

### Example 1: Standard Flow
```
User: "start project ai-document-intelligence-r1"

Agent: ✅ SPEC.md validated (2,306 words)
        📋 Next Step: Generate PLAN.md
        [Y to proceed]

User: "y"

Agent: ✅ PLAN.md generated (487 lines, 5 phases)
        📋 Next Step: Generate task files
        [Y to proceed]

User: "y"

Agent: ✅ 178 tasks generated
        ✨ Project ready!
        📋 Next Step: Execute Task 001
        [Y to start]

User: "y"

Agent: [Executes task 001 with full context loaded]
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

This skill **orchestrates** but **doesn't replace** existing skills:

- **adr-aware**: Auto-invoked during PLAN.md generation for ADR loading
- **project-init**: Concepts integrated but not called (inline for better UX)
- **task-create**: Concepts integrated but not called (inline for better UX)
- **task-execute**: **INVOKED** at Step 4 if user confirms

**Why inline instead of calling skills?**
- Better error handling and recovery
- Clearer progress feedback to user
- Single confirmation point per step (not nested confirmations)
- Easier to refine individual steps

---

## Success Criteria

Pipeline successful when:
- [ ] SPEC.md validated
- [ ] PLAN.md created with all template sections
- [ ] README.md created
- [ ] All task .poml files created
- [ ] TASK-INDEX.md created
- [ ] Deployment tasks added
- [ ] Wrap-up task added
- [ ] User confirmedready to execute Task 001

---

*For Claude Code: This is the recommended entry point for new projects with existing spec.md. Provides streamlined UX with human-in-loop confirmation.*
