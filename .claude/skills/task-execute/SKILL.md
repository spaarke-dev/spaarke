---
description: Execute a POML task file with proper context loading and verification
tags: [tasks, execution, context, knowledge]
techStack: [all]
appliesTo: ["execute task", "run task", "start task", "work on task"]
alwaysApply: false
---

# task-execute

> **Category**: Project Lifecycle
> **Last Updated**: December 2025

---

## Purpose

Execute a single POML task file with **mandatory context loading** and **persistent state tracking**. This skill ensures Claude Code reads all required knowledge files, follows ADRs, applies skills before implementation, and maintains recoverable state across compaction.

**Critical**: This skill prevents the common failure mode where Claude Code starts implementing without loading the knowledge files referenced in the task.

**Context Persistence**: All progress is tracked in `current-task.md` so work can continue after compaction or new sessions.

---

## When to Use

- User says "execute task 013" or "work on task 013"
- User provides a task file path
- Continuing work on a project task
- Resuming after compaction or new session
- **Always use this protocol** when working on any POML task

---

## Execution Protocol (MANDATORY STEPS)

### Step 0: Context Recovery Check

```
IF resuming work (not fresh start):
  READ projects/{project-name}/current-task.md

  IF current-task.md exists AND status == "in-progress":
    → This is a continuation
    → EXTRACT: completed_steps, files_modified, next_step
    → SKIP to the step indicated in next_step
    → REPORT: "Resuming task {id} from step {N}"

  IF current-task.md shows different task:
    → WARN user: "current-task.md shows task {X}, you requested task {Y}"
    → ASK: "Switch to task {Y}? This will update current-task.md"
```

### Step 1: Load Task File

```
READ the task .poml file
EXTRACT:
  - <metadata><tags> for context focus
  - <knowledge><files> for required reading
  - <constraints> for rules to follow
  - <steps> for execution sequence
  - <metadata><title> for display
  - <metadata><phase> for context
```

### Step 2: Initialize/Update current-task.md

**This step ensures state persistence for context recovery.**

```
LOAD projects/{project-name}/current-task.md

IF file doesn't exist:
  CREATE from template: .claude/templates/current-task.template.md

UPDATE current-task.md:
  - Task ID: {extracted from filename}
  - Task File: {relative path to .poml}
  - Title: {from metadata}
  - Phase: {from metadata}
  - Status: "in-progress"
  - Started: {current timestamp}
  - Clear previous completed_steps (new task)
  - Clear previous files_modified (new task)

COMMIT mentally: This file is now the source of truth for recovery
```

### Step 3: Context Budget Check

```
CHECK current context usage

IF > 70%:
  → UPDATE current-task.md with full state (see "Handoff Protocol" below)
  → REPORT: "Context at {X}%. Handoff saved to current-task.md."
  → Request new session
  → STOP until fresh context available

IF > 85%:
  → EMERGENCY: Immediately update current-task.md
  → STOP work
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

**Based on task tags, load the appropriate constraint files:**

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

#### 4b. Load Patterns by Tag (RECOMMENDED)

**Based on task tags, load relevant pattern files:**

| Task Tags | Load Pattern Files |
|-----------|-------------------|
| `bff-api`, `api` | `.claude/patterns/api/endpoint-definition.md`, `.claude/patterns/api/endpoint-filters.md` |
| `pcf`, `react` | `.claude/patterns/pcf/control-initialization.md`, `.claude/patterns/pcf/theme-management.md` |
| `auth`, `oauth` | `.claude/patterns/auth/obo-flow.md`, `.claude/patterns/auth/oauth-scopes.md` |
| `dataverse`, `plugin` | `.claude/patterns/dataverse/plugin-structure.md` |
| `cache` | `.claude/patterns/caching/distributed-cache.md` |
| `testing` | `.claude/patterns/testing/unit-test-structure.md`, `.claude/patterns/testing/mocking-patterns.md` |

#### 4c. Common Knowledge Files by Tag

```
ADDITIONAL MODULE-SPECIFIC FILES:
  - pcf tags → READ src/client/pcf/CLAUDE.md
  - pcf tags → READ docs/guides/PCF-V9-PACKAGING.md
  - bff-api tags → READ src/server/api/CLAUDE.md (if exists)
  - dataverse tags → READ .claude/skills/dataverse-deploy/SKILL.md
  - deploy tags → READ .claude/skills/dataverse-deploy/SKILL.md

UPDATE current-task.md:
  - Add "Knowledge Files Loaded" section with paths
  - Add "Constraints Loaded" section with constraint file names
  - Add "Patterns Loaded" section with pattern file names
```

### Step 5: Load ADR Constraints (Two-Tier)

```
FOR each <constraint source="ADR-XXX">:

  TIER 1 (Default - Concise):
    READ .claude/adr/ADR-XXX-*.md
    - These are 100-150 line AI-optimized versions
    - Contain MUST/MUST NOT rules
    - Sufficient for most implementation tasks

  TIER 2 (If Needed - Full Context):
    IF constraint is unclear or need historical rationale:
      READ docs/adr/ADR-XXX-*.md
      - Full version with history, alternatives considered, consequences

UPDATE current-task.md:
  - Add "Applicable ADRs" section with ADR numbers and relevance
```

### Step 6: Apply Always-Apply Skills

```
LOAD .claude/skills/adr-aware/SKILL.md
LOAD .claude/skills/spaarke-conventions/SKILL.md
LOAD .claude/skills/script-aware/SKILL.md
```

### Step 6.5: Load Script Context (for deployment/testing tasks)

```
IF task tags include: deploy, test, validate, automation, setup
  OR task steps mention: deployment, testing, validation, health check

THEN:
  READ scripts/README.md
  IDENTIFY relevant scripts for this task
  NOTE scripts to use instead of writing new automation

COMMON SCRIPT MATCHES:
  - PCF deployment → Deploy-PCFWebResources.ps1
  - API testing → Test-SdapBffApi.ps1
  - Health checks → test-sdap-api-health.js
  - Custom page deploy → Deploy-CustomPage.ps1
  - Ribbon export → Export-EntityRibbon.ps1

UPDATE current-task.md:
  - Add "Available Scripts" section with matched scripts
```

### Step 7: Review Relevant CLAUDE.md Files

```
BASED on <context><relevant-files>:
  IDENTIFY module paths (src/client/pcf/*, src/server/api/*, etc.)
  FOR each module:
    IF {module}/CLAUDE.md exists:
      READ for module-specific conventions
```

### Step 8: Execute Task Steps (with Progress Tracking)

```
FOR each <step> in <steps>:

  BEFORE starting step:
    UPDATE current-task.md:
      - Current Step: Step {N} - {description}
      - Next Action: {what this step will do}

  EXECUTE the step

  IF step involves code changes:
    APPLY patterns from <knowledge>
    VERIFY against <constraints>

    UPDATE current-task.md → Files Modified:
      - Add each file touched with purpose

  IF step involves PCF:
    FOLLOW PCF-V9-PACKAGING.md version bumping rules
    UPDATE version in 4 locations

  IF step involves deployment:
    FOLLOW dataverse-deploy skill

  IF decision made:
    UPDATE current-task.md → Decisions Made:
      - {timestamp}: {decision} — Reason: {why}

  AFTER completing step:
    UPDATE current-task.md → Completed Steps:
      - [x] Step {N}: {description} ({timestamp})

    UPDATE Next Step to Step {N+1}
```

### Step 9: Verify Acceptance Criteria

```
FOR each <criterion> in <acceptance-criteria>:
  VERIFY the criterion is met
  IF not met:
    FIX before proceeding
```

### Step 10: Update Task Status (Completion)

```
UPDATE task file <metadata><status> to "completed"
ADD <notes> section with completion summary

UPDATE TASK-INDEX.md with ✅ completed status
```

### Step 10.5: Script Library Maintenance

```
AFTER task completion, EVALUATE script library updates:

IF task USED existing scripts:
  VERIFY scripts still work as expected
  IF script needed modifications:
    UPDATE script file
    UPDATE scripts/README.md (Last Used, any behavior changes)

IF task CREATED reusable automation (commands used 3+ times):
  EVALUATE: Should this become a script?

  IF yes (repeatable, complex, multi-step):
    1. CREATE new script following naming convention
    2. ADD inline documentation (synopsis, parameters, examples)
    3. ADD entry to scripts/README.md with:
       - Purpose, Usage frequency, Lifecycle status
       - Dependencies, When to use, Command example
    4. Place in: scripts/ (general) or projects/{name}/scripts/ (project-specific)

IF task DEPRECATED script functionality:
  UPDATE scripts/README.md to mark as ⚠️ Deprecated
  NOTE replacement approach
```

### Step 11: Transition to Next Task

**Important**: `current-task.md` tracks only the ACTIVE task, not task history. When a task completes, it resets for the next task.

```
TRANSITION current-task.md:

1. ARCHIVE completed task info (optional - for session notes):
   - Add to "Session Notes > Key Learnings" if significant discoveries
   - Add to "Handoff Notes" if important context for future tasks

2. RESET for next task:
   - Clear "Completed Steps" section
   - Clear "Files Modified" section
   - Clear "Decisions Made" section
   - Clear "Current Step" section

3. DETERMINE next task:
   - Check TASK-INDEX.md for next pending task (🔲 status)
   - If dependencies: Find first task with all dependencies satisfied

4. UPDATE current-task.md with next task:
   IF next task found:
     - Task ID: {next task number}
     - Task File: tasks/{NNN}-{slug}.poml
     - Title: {from next task metadata}
     - Phase: {from next task metadata}
     - Status: "not-started"
     - Started: "—"
     - Next Action: "Begin Step 1 of task {NNN}"

   IF no more tasks (project complete):
     - Task ID: "none"
     - Status: "none"
     - Next Action: "Project complete. Run /repo-cleanup"

5. REPORT to user:
   "✅ Task {completed_id} complete.

    Next task: {next_id} - {next_title}
    Ready to begin? [Y/N]"
```

**Why reset instead of accumulate?**
- `current-task.md` is for **context recovery**, not task history
- Task history is preserved in:
  - `TASK-INDEX.md` (status of all tasks)
  - Individual `.poml` files (status + notes sections)
  - Git commits (what changed when)
- Keeping it focused prevents file bloat and faster recovery

---

## Handoff Protocol (Pre-Compaction)

When context usage is high or session ending:

```
UPDATE current-task.md completely:

1. Completed Steps: Mark all finished steps with timestamps

2. Current Step: Document exactly where you are:
   - What was being done
   - What's left to do on this step

3. Files Modified: Complete list with purposes

4. Decisions Made: All implementation choices with rationale

5. Session Notes → Handoff Notes:
   - Key context not captured elsewhere
   - Gotchas discovered
   - Important warnings
   - "Another Claude should know..."

6. Next Action: Clear, specific next step

VERIFY: Another Claude instance could continue from current-task.md alone

REPORT to user:
  "✅ State saved to current-task.md
   Ready for /compact or new session.
   Run 'continue task' or 'where was I?' to resume."
```

---

## Context Recovery Protocol

When resuming work (new session or post-compaction):

```
1. READ projects/{project-name}/current-task.md
2. EXTRACT active task info
3. LOAD the task .poml file
4. LOAD knowledge files from task
5. LOAD ADRs from constraints
6. RESUME from "Current Step"

REPORT:
  "✅ Context recovered for {project-name}

   Task: {task_id} - {title}
   Completed: Steps 1-{N}
   Resuming: Step {N+1} - {description}

   Ready to continue. Proceed?"
```

---

## PCF-Specific Checklist

When task has `pcf`, `react`, or `fluent-ui` tags:

- [ ] Read `src/client/pcf/CLAUDE.md`
- [ ] Read `docs/ai-knowledge/guides/PCF-V9-PACKAGING.md`
- [ ] Version bumped in ControlManifest.Input.xml
- [ ] Version bumped in Solution.xml
- [ ] Version bumped in extracted ControlManifest.xml
- [ ] Version shown in UI footer
- [ ] Build succeeds: `npm run build`
- [ ] If deploying: Use `pac pcf push` or solution import
- [ ] current-task.md updated with files modified

---

## BFF API-Specific Checklist

When task has `bff-api`, `api`, or `minimal-api` tags:

- [ ] Read `src/server/api/CLAUDE.md` (if exists)
- [ ] Follow ADR-001 Minimal API patterns
- [ ] Follow ADR-008 endpoint filter patterns
- [ ] Build succeeds: `dotnet build`
- [ ] Tests pass: `dotnet test`
- [ ] current-task.md updated with files modified

---

## Deployment-Specific Checklist

When task has `deploy` tag:

- [ ] Read `.claude/skills/dataverse-deploy/SKILL.md`
- [ ] Read `docs/ai-knowledge/guides/PCF-V9-PACKAGING.md`
- [ ] Fresh build (delete bin/obj first)
- [ ] Version numbers correct
- [ ] PAC CLI authenticated
- [ ] Deployment verified
- [ ] current-task.md updated with deployment status

---

## Example Execution

**User**: "Execute task 013"

**Agent**:
```
1. CHECK current-task.md for recovery state
2. READ tasks/013-update-aisummarypanel.poml
3. UPDATE current-task.md: task 013, status: in-progress
4. EXTRACT tags: pcf, react, fluent-ui, frontend
5. LOAD KNOWLEDGE:
   - READ src/client/pcf/CLAUDE.md
   - READ docs/ai-knowledge/guides/PCF-V9-PACKAGING.md
   - UPDATE current-task.md with knowledge files loaded
6. LOAD CONSTRAINTS:
   - ADR-006, ADR-021: PCF patterns
   - UPDATE current-task.md with applicable ADRs
7. LOAD ALWAYS-APPLY:
   - adr-aware skill
   - spaarke-conventions skill
8. EXECUTE STEPS 1-8 from task:
   - UPDATE current-task.md after each step
   - Track files modified
   - Log decisions made
9. VERIFY acceptance criteria
10. UPDATE task status to completed
11. UPDATE current-task.md: status completed, set next task
```

---

## Example Recovery

**User**: "Where was I?" (after new session)

**Agent**:
```
1. READ projects/ai-doc-summary/current-task.md
2. FOUND: Task 013, status: in-progress, step 5 of 8 complete
3. LOAD tasks/013-update-aisummarypanel.poml
4. LOAD knowledge files per task
5. REPORT:
   "✅ Recovered context for ai-doc-summary

   Task: 013 - Update AISummaryPanel component
   Completed: Steps 1-5
   Files modified: src/client/pcf/AISummaryPanel/...

   Next: Step 6 - Add dark mode support

   Ready to continue?"
```

---

## Failure Modes to Avoid

| Failure | Cause | Prevention |
|---------|-------|------------|
| Lost progress after compaction | Didn't update current-task.md | ALWAYS update after each step |
| Stale PCF deployment | Didn't bump version | ALWAYS read PCF-V9-PACKAGING.md |
| ADR violation | Didn't read ADRs | ALWAYS read ADRs in constraints |
| Wrong patterns | Didn't read CLAUDE.md | ALWAYS read module CLAUDE.md |
| Missing context | Skipped knowledge step | NEVER skip Step 4 |
| Can't resume | current-task.md incomplete | Update ALL sections during work |

---

## Related Skills

- **task-create**: Creates the task files this skill executes
- **adr-aware**: Proactive ADR loading (always-apply)
- **script-aware**: Script library discovery and maintenance (always-apply)
- **dataverse-deploy**: Deployment operations
- **code-review**: Post-implementation review
- **project-pipeline**: Initializes current-task.md for projects

## Related Protocols

- **[Context Recovery Protocol](../../../docs/procedures/context-recovery.md)**: Full recovery procedure

---

*This skill ensures Claude Code maintains recoverable state across all context boundaries.*
