---
description: Save working state before compaction or session end for reliable recovery
tags: [context, compaction, handoff, state, recovery, checkpoint]
techStack: [all]
appliesTo: ["save progress", "context handoff", "before compaction", "checkpoint"]
alwaysApply: false
exemplar: none-too-volatile
last-reviewed: 2026-05-16
---

# context-handoff

> **Category**: Operations
> **Last Reviewed**: 2026-05-16
> **Reviewed By**: ai-procedure-quality-r1 (Phase 2b Wave 2b-A)
> **Exemplar rationale**: Handoffs are per-session ephemeral state — no canonical reference holds. The contract is "after this skill runs, `current-task.md` alone enables continuation."
>
> **Audit note**: The audit flagged a duplicate `## Quick Recovery (READ THIS FIRST)` heading at line 113 + line 176. Investigation: line 176's instance is INSIDE a markdown code fence (`Example:`) — it renders as literal text, not as a heading. The grep counted it as an H2 line because it starts with `## `. This is a grep false positive, not a structural defect. The skill structure is correct as-is.

---

## Purpose

**Ensure reliable context recovery across compaction and session boundaries.**

This skill creates a checkpoint of working state that enables another Claude instance (or the same instance post-compaction) to continue work without loss. It addresses the critical gap where automatic or manual compaction can occur without state being persisted.

**Key Principle**: After this skill runs, `current-task.md` + the project `CLAUDE.md` contain everything needed to continue work. `current-task.md` holds **current state only** — see [State, not history](#state-not-history-binding--added-2026-10-06) below.

---

## State, not history (BINDING — added 2026-10-06)

**Every checkpoint REWRITES `current-task.md` to describe the present. Never prepend a new block on top of old ones, and never keep a superseded block "for reference".**

**Why this rule exists.** Before this rule, this skill said "don't overwrite history" and its template ended in `[... rest of current-task.md content ...]`, so each checkpoint added a "supersedes everything below" block and kept the rest. In long projects the file reached 150–480 KB (`unified-access-control-r2`: 5 KB on 2026-08-21 → 483 KB on 2026-10-06, 27 sessions stacked). That cost accuracy as well as tokens:
- `task-execute` reads the file at Step 0 and Step 2 of **every** task, so a 480 KB file is ~120k tokens before any work starts. That fills the context, forces compaction, and recovery re-reads the same file.
- Superseded blocks still carry imperative instructions ("Running — do NOT relaunch" for runs long finished) that a recovering agent can act on.
- The items that genuinely outlive a session (owner directives, environment gotchas) end up buried thousands of lines down, where recovery never reaches them.

**Budget.** Target ≤ 10 KB. If the file is over **20 KB** when you checkpoint, you are carrying history: move it out (table below) before writing the new state.

**What belongs in `current-task.md`** — only what is true now:
- active task(s) or parallel lanes, their step and status (finished lanes are removed, not struck through);
- running background work (workflow run IDs, monitors) that is still running;
- explicit next actions, in order;
- open blockers, pending owner questions, manual gates;
- uncommitted or unpushed state.

**Where everything else goes:**

| Content | Destination |
|---|---|
| Owner directives and standing rules that outlive a task | Project `CLAUDE.md` → `## Standing directives & gotchas` (one bullet each, with the date) |
| Environment gotchas ("do not re-learn this") | Same section; if it applies beyond this project, `.claude/FAILURE-MODES.md` |
| Decisions with rationale | The task's notes file (`notes/task-NNN-*.md`) or `notes/decisions.md` |
| What happened in a session | The checkpoint **commit message** — git log is the journal |
| A finished checkpoint you still want verbatim | Append to `notes/handoff-history/YYYY-MM.md` — never loaded on recovery; grep it only for a specific past detail |

**Accuracy check before writing.** Anything you delete from `current-task.md` must be (a) no longer true, or (b) now in one of the destinations above. When unsure whether a standing item is still in force, keep it in the project `CLAUDE.md` section and mark it `(verify)`; don't drop it.

---

## When to Use

### Manual Triggers
- User says "save my progress" or "save state"
- User is about to run `/compact`
- User is ending a session mid-task
- User requests `/context-handoff` or `/checkpoint` (alias)

### Proactive Triggers (Claude Should Self-Invoke)
- Context usage approaches 70% (check with `/context`)
- Before a large operation that might push context over limits
- After completing significant work that should be checkpointed
- When switching between projects

### Automatic Detection (Claude Should Monitor)
- Long-running tasks (> 30 minutes of work)
- After every 3-5 completed task steps
- After creating or modifying many files

---

## Workflow

### Step 1: Identify Current Work Context

```
DETERMINE active project:
  - Check if in a project worktree
  - Check git branch name (feature/{project-name})
  - Check recent file modifications under projects/
  - If ambiguous: Ask user

IF no active project:
  → "No active project detected. Nothing to checkpoint."
  → STOP

LOCATE current-task.md:
  - Path: projects/{project-name}/current-task.md
  - IF missing: Create from template
```

### Step 2: Capture Critical State

**This is the minimum state required for recovery. Be concise.**

```
CAPTURE (in memory first):

1. TASK IDENTIFICATION
   - Task ID (from current work or current-task.md)
   - Task file path
   - Task title
   - Current phase

2. PROGRESS STATE
   - Completed steps (numbered list)
   - Current step number and description
   - Step progress (e.g., "Step 4 of 7, sub-step 2 of 3")

3. FILES MODIFIED (this session only)
   - List all files created or modified
   - Brief purpose for each
   - Mark any uncommitted changes

4. DECISIONS MADE (this session only)
   - Key implementation choices
   - Why each decision was made
   - Any alternatives considered

5. NEXT ACTION (CRITICAL - must be explicit)
   - Exact next step to take
   - Any preconditions
   - Files to reference
```

### Step 3: Rewrite current-task.md

**First, move out anything no longer current** (see [State, not history](#state-not-history-binding--added-2026-10-06)). Then **rewrite** the file. Don't prepend; the result is one state, with Quick Recovery at the top:

```markdown
# Current Task State - {Project Name}

> **Last Updated**: {YYYY-MM-DD HH:MM} (by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | {NNN} - {Title} |
| **Step** | {N} of {Total}: {Step description} |
| **Status** | {in-progress / blocked} |
| **Next Action** | {Explicit next action to take} |

### Files Modified This Session
- `{path}` - {purpose}
- `{path}` - {purpose}

### Critical Context
{1-3 sentences of essential context for continuation}

---

## Active Task / Lanes
{status of each active task or lane — remove finished ones}

## Next Actions
{ordered list}

## Open Owner Questions / Blockers
{only those still open}
```

There is no "previous checkpoints" section. Each checkpoint replaces the whole file.

### Step 4: Verify and Report

```
VERIFY current-task.md is complete:
  - Quick Recovery section has all fields
  - Next Action is explicit (not vague)
  - Files Modified matches actual changes
  - Timestamp is updated

OPTIONAL: Commit the checkpoint
  IF uncommitted changes exist:
    → "Do you want me to commit the state checkpoint? [y/n]"
    IF yes:
      git add projects/{project-name}/current-task.md
      git commit -m "checkpoint: save state for {task-id}"

REPORT to user:
  "✅ Context checkpoint saved to current-task.md

   Task: {task-id} - {title}
   Step: {N} of {total}
   Next: {next action}

   Ready for /compact or session end.
   To resume: 'continue task' or 'where was I?'"
```

---

## Quick Recovery Format

**The "Quick Recovery" section must answer these questions in < 30 seconds:**

1. **What task am I on?** → Task ID, title, phase
2. **Where in the task?** → Step N of M
3. **What files did I touch?** → Files modified list
4. **What do I do next?** → Explicit next action

Example:
```markdown
## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 045 - Deploy to Production |
| **Step** | 4 of 7: Configure Azure credentials |
| **Status** | in-progress |
| **Next Action** | Test deployment with: `az webapp deploy --slot staging` |

### Files Modified This Session
- `infrastructure/bicep/customer.bicep` - Added AI Search config
- `src/server/api/Sprk.Bff.Api/Program.cs` - Registered RagService

### Critical Context
OIDC federated identity configured. Staging deployment succeeded.
Need to verify health checks before promoting to production.
```

---

## Integration with Other Skills

### Before Compaction Flow
```
User: "I need to compact"
     ↓
Claude: [Invokes context-handoff]
     ↓
Claude: "✅ State saved. Ready for /compact."
     ↓
User: /compact
     ↓
[Compaction occurs]
     ↓
User: "Where was I?"
     ↓
Claude: [Invokes project-continue]
     ↓
Claude: Reads Quick Recovery section, continues work
```

### During Long Tasks
```
[Claude working on task]
     ↓
[After completing step 5 of 8]
     ↓
Claude: [Proactively invokes context-handoff]
     ↓
Claude: "Checkpoint saved. Continuing with step 6..."
```

### With project-continue
```
context-handoff: Creates checkpoint → current-task.md
project-continue: Reads checkpoint → restores context
```

---

## Proactive Checkpoint Guidelines

**When to self-checkpoint (Claude should do this automatically):**

| Condition | Action |
|-----------|--------|
| Completed a task step | Update current-task.md completed steps |
| Modified 5+ files | Run full context-handoff |
| Made significant decision | Log in current-task.md decisions |
| 30+ minutes of work | Run full context-handoff |
| Before large operation | Run full context-handoff |
| Context feels "heavy" | Check /context, consider checkpoint |

---

## Error Handling

| Situation | Response |
|-----------|----------|
| No active project | "No project context to save. Working on ad-hoc tasks?" |
| current-task.md missing | Create from template, populate with current state |
| Can't determine current task | Ask user: "Which task are you working on?" |
| Conflicting state in file | Ask user: "current-task.md shows task X, but you seem to be on Y. Which is correct?" |
| Git commit fails | "Checkpoint saved but not committed. Commit manually if needed." |

---

## Examples

### Example 1: Manual Pre-Compaction

```
User: "I need to compact, save my progress first"

Claude: [Runs context-handoff]

Output:
  "✅ Context checkpoint saved to current-task.md

   Task: 032 - Implement Email Export
   Step: 3 of 5: Create EmailExportService
   Next: Implement SendEmailAsync method using Graph API

   Files modified:
   - Services/Ai/Export/IEmailExportService.cs - Created interface
   - Services/Ai/Export/EmailExportService.cs - Started implementation

   Ready for /compact. To resume: 'continue task' or 'where was I?'"
```

### Example 2: Proactive Checkpoint

```
[Claude just completed step 5 of 8]

Claude: "Step 5 complete. Let me checkpoint progress..."

[Runs context-handoff silently]

Claude: "✅ Progress saved. Continuing with Step 6: Add unit tests..."
```

### Example 3: End of Session

```
User: "I'm done for today"

Claude: [Runs context-handoff]

Output:
  "✅ Session state saved.

   Task 013 at Step 4 of 6.
   Next session: Say 'continue task 013' or 'where was I?'

   Good night!"
```

---

## Related Skills

| Skill | Relationship |
|-------|--------------|
| `project-continue` | Reads what context-handoff writes |
| `task-execute` | Should call context-handoff after steps |
| `push-to-github` | Can commit checkpoint as part of workflow |

---

## Operator Notes

- **Be proactive** - Don't wait for user to ask; checkpoint after significant work
- **Keep Quick Recovery minimal** - Recovery should take < 30 seconds to read
- **Next Action must be explicit** - "Continue working" is NOT explicit; "Run `dotnet test`" IS explicit
- **Timestamp is critical** - Always update Last Updated when checkpointing
- **Rewrite, don't accumulate** - `current-task.md` is current state. Move durable items to the project `CLAUDE.md`, decisions to notes, and the session narrative to the commit message (see [State, not history](#state-not-history-binding--added-2026-10-06))
- **Files Modified is session-scoped** - Reset when task changes, not accumulate forever
- **Verify before reporting** - Actually read back current-task.md to confirm save worked

---

## Post-Compaction Recovery

When Claude Code resumes after compaction:

1. **User says anything** → Check if there's active project context
2. **Find current-task.md** → Read Quick Recovery section
3. **Load minimal context** → Just enough to continue
4. **Report state** → "Recovered: Task X, Step Y, Next: Z"
5. **Load full context** → Via project-continue if needed

This can be automatic if the user's first message is work-related, or explicit via "where was I?".

---

## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| Post-compaction agent has stale `current-task.md` but believes it's current | Checkpoint was skipped or interrupted; `Last Updated:` timestamp wasn't refreshed | Always update the `Last Updated:` timestamp during checkpoint. On resume, compare it against `git log -1 --format=%ci current-task.md` — large gap = handoff was incomplete. |
| Quick Recovery says step N but the work was actually on step N+2 | Checkpoint ran early in a step; agent did 2 more steps before context ran out | Checkpoint after EACH completed step (per root CLAUDE.md proactive checkpointing rules), not just before compaction. Background work between checkpoints is at-risk. |
| `current-task.md` updated but the relevant project files weren't committed | Checkpoint saved the state file; uncommitted code changes lost across compaction | If "Files Modified This Session" list has uncommitted changes, prompt user to commit BEFORE compaction. Don't silently lose work. |
| `current-task.md` grows to hundreds of KB; recovery is slow, compaction comes early, and an agent obeys a stale "do not relaunch" | Each checkpoint prepended a "supersedes everything below" block and kept the old ones (the pre-2026-10-06 wording of this skill told agents to keep history) | Rewrite per [State, not history](#state-not-history-binding--added-2026-10-06). One-time cleanup: copy the file verbatim to `notes/handoff-history/current-task-archive-{date}.md`, move standing items to the project `CLAUDE.md`, and rewrite the file from the newest checkpoint. |
| Recovery loads project-continue but `current-task.md` is for a different project | Multiple worktrees, agent loaded wrong project's state | Verify branch name matches expected project before trusting `current-task.md`. If mismatch, the user opened the wrong working directory. |

---

*This skill ensures no work is lost across context boundaries.*

---

## Portfolio Hook (added 2026-06-23 by spaarke-devops-project-tracking-r1 task 034 · FR-20) — **HIGHEST VALUE**

**At end of skill** (after handoff document written): invoke `/devops-project-sync`.

Per spec §6.2: this is the **highest-value hook** — compaction checkpoints (every 3 steps, >60% context, 5+ files modified) coincide with portfolio checkpoints so the GitHub board is never more than 3 task steps stale.

Silent on success. Failure degrades to ⚠️ warn; does NOT block handoff write.

See: [`.claude/skills/devops-project-sync/SKILL.md`](../devops-project-sync/SKILL.md).
