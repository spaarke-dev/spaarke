# task-create

---
description: Decompose a project plan into numbered POML task files for systematic AI-assisted execution
tags: [tasks, planning, project-structure, poml]
techStack: [all]
appliesTo: ["projects/*/plan.md", "create tasks", "decompose plan"]
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

### Step 3.4: Discover Related Resources (REQUIRED)

**Every task MUST have a `<knowledge>` section with relevant files based on its tags.**

#### Tag-to-Knowledge Mapping (MANDATORY)

When a task has these tags, ALWAYS include these knowledge files:

| Tag | Constraints | Patterns | Additional Files |
|-----|-------------|----------|------------------|
| `pcf`, `react`, `fluent-ui`, `frontend`, `e2e-test` | `.claude/constraints/pcf.md` | `.claude/patterns/pcf/control-initialization.md`, `.claude/patterns/pcf/theme-management.md` | `src/client/pcf/CLAUDE.md`, `docs/guides/PCF-V9-PACKAGING.md`, `.claude/skills/ui-test/SKILL.md` |
| `bff-api`, `api`, `minimal-api`, `endpoints` | `.claude/constraints/api.md` | `.claude/patterns/api/endpoint-definition.md`, `.claude/patterns/api/endpoint-filters.md` | `src/server/api/CLAUDE.md` (if exists) |
| `dataverse`, `solution`, `fields`, `plugin` | `.claude/constraints/plugins.md` | `.claude/patterns/dataverse/plugin-structure.md` | `.claude/skills/dataverse-deploy/SKILL.md` |
| `auth`, `oauth`, `authorization` | `.claude/constraints/auth.md` | `.claude/patterns/auth/obo-flow.md`, `.claude/patterns/auth/oauth-scopes.md` | — |
| `cache`, `redis`, `caching` | `.claude/constraints/data.md` | `.claude/patterns/caching/distributed-cache.md` | — |
| `ai`, `azure-openai`, `document-intelligence` | `.claude/constraints/ai.md` | `.claude/patterns/ai/streaming-endpoints.md` | — |
| `deploy` | — | — | `.claude/skills/dataverse-deploy/SKILL.md`, `docs/guides/PCF-V9-PACKAGING.md` |
| `testing`, `unit-test`, `integration-test` | `.claude/constraints/testing.md` | `.claude/patterns/testing/unit-test-structure.md`, `.claude/patterns/testing/mocking-patterns.md` | — |
| `worker`, `job`, `background` | `.claude/constraints/jobs.md` | — | — |

**Critical: PCF tasks MUST reference PCF-V9-PACKAGING.md**

The `PCF-V9-PACKAGING.md` guide contains **mandatory version bumping instructions**:
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

  ADD to task <metadata>:
    <rigor-hint>{FULL | STANDARD | MINIMAL}</rigor-hint>
    <rigor-reason>{Why this level - reference specific trigger from decision tree}</rigor-reason>

  EXAMPLE rigor hints:
    <rigor-hint>FULL</rigor-hint>
    <rigor-reason>Task tags include 'bff-api' (code implementation)</rigor-reason>

    <rigor-hint>STANDARD</rigor-hint>
    <rigor-reason>Task tags include 'testing', 'integration-test'</rigor-reason>

    <rigor-hint>MINIMAL</rigor-hint>
    <rigor-reason>Documentation task (no code implementation)</rigor-reason>

  PURPOSE:
    - Makes rigor level explicit in task file (documented, not inferred)
    - task-execute skill uses this hint but can override based on actual characteristics
    - User can override by editing task file before execution
    - Audit trail shows why rigor level was chosen

REFERENCE: See .claude/skills/task-execute/SKILL.md Step 0.5 for full decision tree
```

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
     - Create notes/lessons-learned.md if notable insights

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
    <phase>{Phase Number}: {Phase Name}</phase>
    <status>not-started</status>
    <estimated-hours>{2-4}</estimated-hours>
    <dependencies>{comma-separated task IDs or "none"}</dependencies>
    <blocks>{comma-separated task IDs or "none"}</blocks>
    <tags>{context tags for Claude Code focus - see Standard Tag Vocabulary}</tags>
    <rigor-hint>{FULL | STANDARD | MINIMAL}</rigor-hint>
    <rigor-reason>{Why this level - from Step 3.5.5 decision tree}</rigor-reason>
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
- `<metadata>` - id, title, phase, status, estimated-hours, dependencies, blocks, **tags**
- `<prompt>` - Natural language task instruction for AI agent
- `<role>` - Persona/expertise for the AI to adopt
- `<goal>` - Clear definition of done
- `<context>` - Background and relevant files
- `<constraints>` - With explicit ADR source attributes
- `<steps>` - Ordered steps with order attribute
- `<outputs>` - Exact file paths with type attribute
- `<acceptance-criteria>` - Testable criteria with testable="true" attribute

Recommended sections:
- `<knowledge>` - ADRs, patterns, and reference files (REQUIRED if task has tags - see Tag-to-Knowledge Mapping)
- `<tools>` - Available tools for execution
- `<notes>` - Implementation hints
- `<execution>` - Reference to task-execute skill and pre-execution protocol
- `<ui-tests>` - Browser-based UI tests for PCF/frontend tasks (REQUIRED if tags include: pcf, frontend, fluent-ui, e2e-test)

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
- **task-execute**: Runs individual tasks (calls ui-test in Step 9.7)
- **ui-test**: Executes browser-based UI tests defined in task `<ui-tests>` sections
- **project-pipeline**: Orchestrates spec → setup → create → execute

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
- [ ] PCF/frontend tasks have `<ui-tests>` sections (Step 3.65)
- [ ] UI tests include dark mode compliance check for Fluent UI tasks (ADR-021)
- [ ] Each task has `<rigor-hint>` and `<rigor-reason>` in metadata (Step 3.5.5)
- [ ] Rigor levels match task characteristics (FULL for code, STANDARD for tests, MINIMAL for docs)
