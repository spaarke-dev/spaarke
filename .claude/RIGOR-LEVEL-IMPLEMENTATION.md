# Rigor Level System Implementation

> **Date:** 2026-01-07
> **Purpose:** Enforce deterministic protocol compliance in task execution
> **Status:** ✅ Implemented

---

## Problem Statement

Claude Code was inconsistently following the task-execute protocol, using "judgment" to decide when to take shortcuts. This led to:
- Skipped constraint loading (risk of ADR violations)
- Skipped pattern loading (risk of reinventing patterns incorrectly)
- Missing step-by-step tracking (risk of context loss after compaction)
- Inconsistent quality gates (risk of bugs in production)

**Root Cause:** LLMs are pattern-matchers, not procedure executors. Without deterministic rules, protocol compliance was unreliable.

---

## Solution Implemented

### 1. Deterministic Decision Tree

Added Step 0.5 to task-execute skill with boolean decision tree:

```
FULL Protocol IF Task Has ANY:
  ├─ Tags: bff-api, api, pcf, plugin, auth
  ├─ Will modify code files (.cs, .ts, .tsx)
  ├─ Has 6+ steps in task definition
  ├─ Resuming after compaction or new session
  ├─ Description includes: "implement", "refactor"
  └─ Dependencies on 3+ other tasks

STANDARD Protocol IF Task Has ANY:
  ├─ Tags: testing, integration-test
  ├─ Will create new files
  ├─ Has explicit constraints or ADRs listed
  └─ Phase 2.x or higher

MINIMAL Protocol OTHERWISE:
  └─ Documentation, inventory, simple updates
```

### 2. Three Rigor Levels

| Level | When | Steps Required | Reporting | Quality Gates |
|-------|------|----------------|-----------|---------------|
| **FULL** | Code implementation | 11/11 steps | After each step | ✅ code-review + adr-check |
| **STANDARD** | Tests, new files | 8/11 steps | After major steps | ⏭️ Skipped |
| **MINIMAL** | Documentation | 4/11 steps | Start + end only | ⏭️ Skipped |

### 3. Mandatory Visible Reporting

At task start, Claude Code MUST output:

```
🔒 RIGOR LEVEL: FULL
📋 REASON: Task tags include 'bff-api' (code implementation)

📖 PROTOCOL STEPS TO EXECUTE:
  ✅ Step 0.5: Determine rigor level
  ✅ Step 1: Load Task File
  ✅ Step 2: Initialize current-task.md
  [... complete list ...]

Proceeding with Step 0...
```

**Key:** This makes shortcuts impossible to hide.

### 4. Audit Trail

Rigor level logged in current-task.md for recovery:

```markdown
### Task XXX Details

**Rigor Level:** FULL
**Reason:** Task tags include 'bff-api' (code implementation)
**Protocol Steps Executed:**
- [x] Step 0.5: Determined rigor level
- [x] Step 1: Load Task File
[... etc]
```

### 5. User Override

User can override automatic detection:
- "Execute with FULL protocol" → Forces all steps
- "Execute with MINIMAL protocol" → Forces minimal (use carefully)
- Default: Auto-detect using decision tree

---

## Files Modified

### 1. `.claude/skills/task-execute/SKILL.md`

**Added:**
- Step 0.5: Determine Required Rigor Level (MANDATORY)
- Decision tree with boolean conditions
- Rigor Level Protocol Requirements table
- Mandatory declaration template
- User override instructions
- Audit trail format

**Location:** After "When to Use" section, before "Step 0: Context Recovery Check"

### 2. `CLAUDE.md`

**Added:**
- "Task Execution Rigor Levels" section
- Rigor Level Overview table
- Automatic Detection (Decision Tree) section
- Mandatory Rigor Level Declaration format
- User Override instructions
- Examples by Task Type table
- Audit Trail in current-task.md format
- Reference link to task-execute skill

**Location:** After "Task Completion and Transition", before "AI Agent Skills (MANDATORY)"

### 3. `.claude/skills/task-create/SKILL.md`

**Added:**
- Step 3.5.5: Determine Task Rigor Level (REQUIRED)
- Same decision tree as task-execute for consistency
- Auto-detection logic based on task characteristics
- `<rigor-hint>` and `<rigor-reason>` in POML metadata template
- Updated Step 6 output summary to report rigor level distribution
- Updated validation checklist to verify rigor-hint presence

**Purpose:**
- Makes rigor level explicit in task files (documented, not inferred)
- task-execute can read hint but override based on actual characteristics
- User can override by editing task file before execution
- Creates audit trail from task creation through execution

**Location:** After Step 3.5 (Map ADRs to Tasks), before Step 3.6 (Add Deployment Tasks)

---

## Benefits

| Benefit | How Achieved |
|---------|-------------|
| **Deterministic** | Boolean decision tree with clear conditions |
| **Visible** | Mandatory reporting at task start |
| **Auditable** | Logged in current-task.md |
| **Enforceable** | Can't skip steps without visible lie in reporting |
| **User-Controllable** | Can override with explicit instruction |
| **Self-Documenting** | Reason for rigor level is explained |
| **Recoverable** | Rigor level preserved for post-compaction recovery |

---

## Examples

### Example 1: API Implementation (FULL)

**Task:** "Implement authorization service"
**Tags:** `bff-api`, `auth`
**Auto-Detected:** FULL

**Output:**
```
🔒 RIGOR LEVEL: FULL
📋 REASON: Task tags include 'bff-api', 'auth' (code implementation)

📖 PROTOCOL STEPS TO EXECUTE:
  ✅ Step 0.5: Determine rigor level
  ✅ Step 1: Load Task File
  ✅ Step 2: Initialize current-task.md
  ✅ Step 3: Context Budget Check
  ✅ Step 4: Load Knowledge Files (ALL files listed)
  ✅ Step 4a: Load Constraints (api.md, auth.md)
  ✅ Step 4b: Load Patterns (endpoint-definition.md, obo-flow.md)
  ✅ Step 5: Load ADRs (ADR-001, ADR-008)
  ✅ Step 6: Apply Always-Apply Skills
  ✅ Step 6.5: Load Script Context
  ✅ Step 7: Review CLAUDE.md Files
  ✅ Step 8: Execute Steps (with checkpoints every 3 steps)
  ✅ Step 9: Verify Acceptance Criteria
  ✅ Step 9.5: Quality Gates (code-review, adr-check)
  ✅ Step 10: Update Task Status

Proceeding with Step 0...
```

### Example 2: Integration Tests (STANDARD)

**Task:** "Test Document Profile endpoint"
**Tags:** `testing`, `integration-test`
**Auto-Detected:** STANDARD

**Output:**
```
🔒 RIGOR LEVEL: STANDARD
📋 REASON: Task tags include 'testing', 'integration-test'

📖 PROTOCOL STEPS TO EXECUTE:
  ✅ Step 1: Load Task File
  ✅ Step 2: Initialize current-task.md
  ✅ Step 3: Context Budget Check
  ✅ Step 4: Load Knowledge Files (explicit only)
  ✅ Step 4a: Load Constraints (testing.md)
  ⏭️ Step 4b: Skip Pattern Loading
  ✅ Step 5: Load ADRs (if listed)
  ⏭️ Step 6: Skip Always-Apply Skills
  ✅ Step 8: Execute Steps
  ✅ Step 9: Verify Acceptance Criteria
  ⏭️ Step 9.5: Skip Quality Gates
  ✅ Step 10: Update Task Status

Proceeding with Step 1...
```

### Example 3: Deployment Inventory (MINIMAL)

**Task:** "Identify forms using control"
**Tags:** `dataverse`, `forms`, `deployment`
**Auto-Detected:** MINIMAL

**Output:**
```
🔒 RIGOR LEVEL: MINIMAL
📋 REASON: Documentation/inventory task (no code implementation)

📖 PROTOCOL STEPS TO EXECUTE:
  ✅ Step 1: Load Task File
  ✅ Step 2: Initialize current-task.md
  ✅ Step 3: Context Budget Check
  ✅ Step 8: Execute Steps
  ✅ Step 9: Verify Acceptance Criteria
  ✅ Step 10: Update Task Status

Proceeding with Step 1...
```

---

## Testing Plan

**For Next Project:**
1. Create a new project with diverse task types (code, tests, docs)
2. Verify rigor level auto-detection works correctly
3. Verify mandatory declaration is output at task start
4. Verify audit trail is logged in current-task.md
5. Verify user override works ("Execute with FULL protocol")

**Expected Outcomes:**
- FULL protocol used for all code implementation tasks
- STANDARD protocol used for test creation tasks
- MINIMAL protocol used for documentation tasks
- All protocol steps visible in output
- No hidden shortcuts possible

---

## Migration Notes

**Current Project (ai-summary-and-analysis-enhancements):**
- No changes needed - project almost complete
- System ready for future projects

**Future Projects:**
- Rigor level will auto-detect based on task characteristics
- Mandatory declaration will make protocol compliance visible
- Shortcuts will be impossible to hide

---

## Maintenance

**When to Update:**
- Add new tags to decision tree as coding patterns evolve
- Adjust step requirements if new protocol steps added
- Update examples as task types change

**How to Update:**
- Edit `.claude/skills/task-execute/SKILL.md` Step 0.5 decision tree
- Update `CLAUDE.md` "Task Execution Rigor Levels" section
- Update this document with new examples

---

**Implementation Date:** 2026-01-07
**Implemented By:** Claude Code with user guidance
**Status:** ✅ Production ready for future projects
