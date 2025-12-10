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

Execute a single POML task file with **mandatory context loading**. This skill ensures Claude Code reads all required knowledge files, follows ADRs, and applies skills before implementation begins.

**Critical**: This skill prevents the common failure mode where Claude Code starts implementing without loading the knowledge files referenced in the task.

---

## When to Use

- User says "execute task 013" or "work on task 013"
- User provides a task file path
- Continuing work on a project task
- **Always use this protocol** when working on any POML task

---

## Execution Protocol (MANDATORY STEPS)

### Step 0: Load Task File
```
READ the task .poml file
EXTRACT:
  - <metadata><tags> for context focus
  - <knowledge><files> for required reading
  - <constraints> for rules to follow
  - <steps> for execution sequence
```

### Step 1: Context Budget Check
```
CHECK current context usage
IF > 70%:
  → Create handoff summary
  → Request new session
  → STOP until fresh context available
```

### Step 2: Load Knowledge Files (MANDATORY)

**This step is NOT optional. Do NOT skip to implementation.**

```
FOR each file in <knowledge><files>:
  READ the file completely
  EXTRACT key rules, patterns, constraints
  
FOR each pattern in <knowledge><patterns>:
  READ the referenced file
  UNDERSTAND the pattern to follow

COMMON KNOWLEDGE FILES BY TAG:
  - pcf tags → READ src/client/pcf/CLAUDE.md
  - pcf tags → READ docs/ai-knowledge/guides/PCF-V9-PACKAGING.md
  - bff-api tags → READ src/server/api/CLAUDE.md (if exists)
  - dataverse tags → READ .claude/skills/dataverse-deploy/SKILL.md
  - deploy tags → READ .claude/skills/dataverse-deploy/SKILL.md
```

### Step 3: Load ADR Constraints
```
FOR each <constraint source="ADR-XXX">:
  READ docs/reference/adr/ADR-XXX-*.md
  NOTE specific requirements that apply
```

### Step 4: Apply Always-Apply Skills
```
LOAD .claude/skills/adr-aware/SKILL.md
LOAD .claude/skills/spaarke-conventions/SKILL.md
```

### Step 5: Review Relevant CLAUDE.md Files
```
BASED on <context><relevant-files>:
  IDENTIFY module paths (src/client/pcf/*, src/server/api/*, etc.)
  FOR each module:
    IF {module}/CLAUDE.md exists:
      READ for module-specific conventions
```

### Step 6: Execute Task Steps
```
FOR each <step> in <steps>:
  EXECUTE the step
  
  IF step involves code changes:
    APPLY patterns from <knowledge>
    VERIFY against <constraints>
    
  IF step involves PCF:
    FOLLOW PCF-V9-PACKAGING.md version bumping rules
    UPDATE version in 4 locations
    
  IF step involves deployment:
    FOLLOW dataverse-deploy skill
```

### Step 7: Verify Acceptance Criteria
```
FOR each <criterion> in <acceptance-criteria>:
  VERIFY the criterion is met
  IF not met:
    FIX before proceeding
```

### Step 8: Update Task Status
```
UPDATE task file <metadata><status> to "completed"
ADD <notes> section with completion summary
UPDATE TASK-INDEX.md with ✅ completed status
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

---

## BFF API-Specific Checklist

When task has `bff-api`, `api`, or `minimal-api` tags:

- [ ] Read `src/server/api/CLAUDE.md` (if exists)
- [ ] Follow ADR-001 Minimal API patterns
- [ ] Follow ADR-008 endpoint filter patterns
- [ ] Build succeeds: `dotnet build`
- [ ] Tests pass: `dotnet test`

---

## Deployment-Specific Checklist

When task has `deploy` tag:

- [ ] Read `.claude/skills/dataverse-deploy/SKILL.md`
- [ ] Read `docs/ai-knowledge/guides/PCF-V9-PACKAGING.md`
- [ ] Fresh build (delete bin/obj first)
- [ ] Version numbers correct
- [ ] PAC CLI authenticated
- [ ] Deployment verified

---

## Example Execution

**User**: "Execute task 013"

**Agent**:
```
1. READ tasks/013-update-aisummarypanel.poml
2. EXTRACT tags: pcf, react, fluent-ui, frontend
3. LOAD KNOWLEDGE:
   - READ src/client/pcf/CLAUDE.md
   - READ docs/ai-knowledge/guides/PCF-V9-PACKAGING.md
   - READ .claude/skills/dataverse-deploy/SKILL.md
4. LOAD CONSTRAINTS:
   - ADR-006: PCF pattern
5. LOAD ALWAYS-APPLY:
   - adr-aware skill
   - spaarke-conventions skill
6. EXECUTE STEPS 1-8 from task
7. VERIFY acceptance criteria
8. UPDATE task status to completed
```

---

## Failure Modes to Avoid

| Failure | Cause | Prevention |
|---------|-------|------------|
| Stale PCF deployment | Didn't bump version | ALWAYS read PCF-V9-PACKAGING.md |
| ADR violation | Didn't read ADRs | ALWAYS read ADRs in constraints |
| Wrong patterns | Didn't read CLAUDE.md | ALWAYS read module CLAUDE.md |
| Missing context | Skipped knowledge step | NEVER skip Step 2 |

---

## Related Skills

- **task-create**: Creates the task files this skill executes
- **adr-aware**: Proactive ADR loading (always-apply)
- **dataverse-deploy**: Deployment operations
- **code-review**: Post-implementation review
