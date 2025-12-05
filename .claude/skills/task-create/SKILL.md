# task-create

---
description: Decompose a project plan into numbered POML task files for systematic AI-assisted execution
alwaysApply: false
---

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
LOAD: docs/ai-knowledge/templates/task-execution.template.md  # POML format

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

### Step 3.5: Map ADRs to Tasks (REQUIRED)

```
FOR each task identified:
  DETERMINE resource types being created/modified:
    - API Endpoint → ADR-001, ADR-008, ADR-010
    - Authorization → ADR-003, ADR-008
    - Caching → ADR-009
    - Dataverse Plugin → ADR-002
    - Graph/SPE Integration → ADR-007
    - PCF Control → ADR-006, ADR-011, ADR-012
    - Background Worker → ADR-001, ADR-004
    - DI Registration → ADR-010
  
  ADD to task:
    - <constraints> with source="ADR-XXX" for each applicable ADR
    - <knowledge><files> including relevant ADR paths

REFERENCE: See adr-aware skill for full mapping table
```

### Step 4: Generate Task Files

For each task, create `tasks/{NNN}-{task-slug}.poml` as a **valid XML document**:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<task id="{NNN}" project="{project-name}">
  <metadata>
    <title>{Task Title}</title>
    <phase>{Phase Number}: {Phase Name}</phase>
    <status>not-started</status>
    <estimated-hours>{2-4}</estimated-hours>
    <dependencies>{comma-separated task IDs or "none"}</dependencies>
    <blocks>{comma-separated task IDs or "none"}</blocks>
  </metadata>

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

  <steps>
    <step order="1">{First concrete action}</step>
    <step order="2">{Second concrete action}</step>
    <step order="3">{Continue until task is complete}</step>
    <step order="N-2">Run tests and verify all pass</step>
    <step order="N-1">Update TASK-INDEX.md: change this task's status to ✅ completed</step>
    <step order="N">If any deviations from plan, document in projects/{project-name}/notes/</step>
  </steps>

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
</task>
```

### Step 5: Update Project Files
```
UPDATE projects/{project-name}/CLAUDE.md:
  - Add task count and phase mapping
  - Update "Next Action" to reference first task

CREATE tasks/TASK-INDEX.md:
  | ID | Title | Phase | Status | Dependencies |
  |----|-------|-------|--------|--------------|
  | 001 | ... | 1 | not-started | none |
  | 002 | ... | 1 | not-started | 001 |
  ...
```

### Step 6: Output Summary
```
✅ Tasks created for: projects/{project-name}/

Task breakdown:
  Phase 1: {n} tasks (001-00{n})
  Phase 2: {m} tasks (010-01{m})
  ...
  Total: {total} tasks

Files created:
  - tasks/TASK-INDEX.md
  - tasks/001-{slug}.md
  - tasks/002-{slug}.md
  ...

Execution order recommendation:
  1. Start with task 001 (no dependencies)
  2. ...

Run /task-execute 001 to begin implementation.
```

## Conventions

### File Naming
- Format: `{NNN}-{task-slug}.poml`
- Extension: `.poml` (valid XML document)
- Slug: 3-5 words, kebab-case (e.g., `setup-redis-connection`)
- Numbers: 3 digits, zero-padded (001, 010, 100)

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
- `<metadata>` - id, title, phase, status, estimated-hours, dependencies, blocks
- `<prompt>` - Natural language task instruction for AI agent
- `<role>` - Persona/expertise for the AI to adopt
- `<goal>` - Clear definition of done
- `<context>` - Background and relevant files
- `<constraints>` - With explicit ADR source attributes
- `<steps>` - Ordered steps with order attribute
- `<outputs>` - Exact file paths with type attribute
- `<acceptance-criteria>` - Testable criteria with testable="true" attribute

Recommended sections:
- `<knowledge>` - ADRs, patterns, and reference files
- `<tools>` - Available tools for execution
- `<notes>` - Implementation hints

### Status Values
- `not-started` - Initial state
- `in-progress` - Currently being worked
- `blocked` - Waiting on dependency or external input
- `completed` - All deliverables and criteria met
- `deferred` - Postponed (with reason in notes)

## Resources

### Templates (Auto-loaded)
- `docs/ai-knowledge/templates/task-execution.template.md`

### Related Skills
- **project-init**: Creates plan.md that this skill consumes
- **task-execute**: Runs individual tasks (not yet created)
- **design-to-project**: Orchestrates init → create → execute

## Examples

### Example 1: Decompose SDAP Refactor Plan
**Trigger**: "/task-create sdap-refactor"

**Input plan.md phases**:
```
Phase 1: Assessment (2 tasks)
Phase 2: API Restructure (5 tasks)
Phase 3: Worker Migration (4 tasks)
Phase 4: Testing (3 tasks)
```

**Output**:
```
tasks/
├── TASK-INDEX.md
├── 001-inventory-endpoints.poml
├── 002-document-dependencies.poml
├── 010-create-minimal-api-structure.poml
├── 011-migrate-policy-endpoints.poml
├── 012-migrate-document-endpoints.poml
├── 013-migrate-sync-endpoints.poml
├── 014-implement-auth-filters.poml
├── 020-extract-sync-worker.poml
├── 021-extract-notification-worker.poml
├── 022-implement-job-queue.poml
├── 023-configure-worker-hosting.poml
├── 030-unit-tests.poml
├── 031-integration-tests.poml
└── 032-e2e-validation.poml
```

### Example 2: Fine-Grained Decomposition
**Trigger**: "Create tasks for redis-caching project with fine granularity"

**Result**: 15 tasks instead of 8, each ~1-2 hours

### Example 3: Handle Missing Dependencies
**Trigger**: "/task-create my-project"

**If plan.md lacks WBS**:
```
⚠️ Cannot create tasks: plan.md missing work breakdown structure

Required in plan.md Section 5:
  - At least one phase with name and description
  - Deliverables or outcomes for each phase

Would you like me to help complete the plan first?
```

## Validation Checklist

Before completing task-create, verify:
- [ ] TASK-INDEX.md created with all tasks listed
- [ ] Each task file has all required POML sections
- [ ] Task numbers follow gap convention (001, 010, 020...)
- [ ] Dependencies form a valid DAG (no circular refs)
- [ ] First task(s) have no unmet dependencies
- [ ] Acceptance criteria are copy/referenced from plan.md
- [ ] Project CLAUDE.md updated with task summary
