# CLAUDE.md - Spaarke Repository Instructions

> **Last Updated**: February 11, 2026
>
> **Purpose**: This file provides repository-wide context and instructions for Claude Code when working in this codebase.

---

## Development Environment

### Adaptive Thinking & Effort Control (Opus 4.6)

Claude Opus 4.6 introduces **adaptive thinking** — the model dynamically decides when and how much to think based on task complexity. This replaces the fixed `MAX_THINKING_TOKENS` budget from Opus 4.5.

**How It Works**: Instead of a fixed thinking token budget, Claude evaluates each request's complexity and allocates thinking accordingly. The **effort parameter** provides soft guidance:

| Effort Level | Thinking Behavior | Use Case |
|-------------|-------------------|----------|
| `max` | Always thinks deeply, no constraints (Opus 4.6 only) | Architectural decisions, complex debugging, multi-system refactors |
| `high` (default) | Always thinks deeply | Complex reasoning, difficult coding, agentic task execution |
| `medium` | Moderate thinking, may skip for simple queries | Balanced speed/quality for standard tasks, subagent coordination |
| `low` | Minimal thinking, skips for simple tasks | Simple lookups, classification, high-volume operations, fast subagents |

**Toggle Thinking in Session**: Press **Alt+T** (or **Option+T** on Mac) to toggle thinking on/off. Press **Ctrl+O** for verbose mode to view reasoning.

**Environment Variables**:
```bash
# Opus 4.6: MAX_THINKING_TOKENS is IGNORED (adaptive thinking controls depth)
# Setting to 0 still disables thinking entirely on any model
CLAUDE_CODE_MAX_OUTPUT_TOKENS=64000
```

**Setting in Windows**:
```cmd
setx CLAUDE_CODE_MAX_OUTPUT_TOKENS "64000"
```

**Task-Specific Effort Guidance**:

| Task Type | Recommended Effort | Why |
|-----------|-------------------|-----|
| `/project-pipeline` (full pipeline) | `high` or `max` | Multi-phase, deep resource discovery, 100+ tasks |
| Task execution (FULL rigor) | `high` | Code implementation needs thorough reasoning |
| Task execution (STANDARD) | `medium` | Tests and constrained tasks need balance |
| Task execution (MINIMAL) | `low` | Documentation, inventory — speed over depth |
| Subagent research/exploration | `low` or `medium` | Quick focused workers benefit from speed |
| Agent team teammates | `medium` | Independent workers need efficiency at scale |
| Code review / ADR check | `high` | Quality gates need thorough analysis |

**Cost Impact**: Lower effort = fewer tokens = lower cost + lower latency. Use `low` for subagents and simple tasks to avoid unnecessary token spend.

### Claude Code Model and Permission Settings

This repository is configured for **Opus 4.6** with **auto-accept edits** via `.claude/settings.json`:

```json
{
  "model": "opus",
  "permissions": {
    "defaultMode": "acceptEdits",
    "allow": ["Read(**)", "Glob(**)", "Grep(**)", "Skill(*)", "Bash(git:*)", ...]
  }
}
```

**Model Selection**:
- **Default**: `opus` (Claude Opus 4.6 - claude-opus-4-6)
- **Check current**: `/model` or `/status` during session
- **Override**: `claude --model sonnet` for faster, simpler tasks (Sonnet 4.5)

**Permission Modes** (cycle with **Shift+Tab**):

| Mode | Indicator | Use Case |
|------|-----------|----------|
| **Auto-Accept** | `⏵⏵ accept edits on` | Task implementation (default) |
| **Plan Mode** | `⏸ plan mode on` | Code analysis, planning, exploration |
| **Delegate Mode** | (agent teams only) | Lead coordinates, teammates implement |
| **Don't Ask** | Auto-denies unless pre-approved | Strict mode using only `/permissions` allow list |
| **Bypass Permissions** | All prompts skipped | Fully autonomous (containers/VMs only) |
| **Ask Mode** | (none) | Explicit approval for each change |

**Skip All Confirmations** (fully autonomous execution):

```bash
# OPTION 1: Bypass all permissions (use in isolated environments only)
claude --dangerously-skip-permissions

# OPTION 2: Set in settings.json for persistent bypass
# In .claude/settings.json:
{
  "permissions": {
    "defaultMode": "bypassPermissions"
  }
}

# OPTION 3: Pre-approve specific tools to minimize prompts (RECOMMENDED)
# In .claude/settings.json - allow common operations:
{
  "permissions": {
    "defaultMode": "acceptEdits",
    "allow": [
      "Read(**)", "Glob(**)", "Grep(**)",
      "Bash(git *)", "Bash(dotnet *)", "Bash(npm *)",
      "Bash(az *)", "Bash(pac *)", "Bash(gh *)",
      "Skill(*)", "Task(*)"
    ]
  }
}
```

> **WARNING**: `--dangerously-skip-permissions` disables ALL safety checks. Only use in containers, VMs, or disposable environments. For daily development, use **acceptEdits** mode with a broad `allow` list instead.

**Recommended Workflow**:

1. **Planning Phase** (`/project-pipeline`, `/design-to-spec`):
   - Press **Shift+Tab** to cycle to **Plan Mode**
   - Claude analyzes, reads, plans without making changes
   - Review the plan before implementation

2. **Implementation Phase** (task execution):
   - Press **Shift+Tab** to return to **Auto-Accept Mode**
   - Claude implements changes automatically
   - Quality gates (code-review, adr-check) run at Step 9.5

3. **Fully Autonomous Phase** (CI/CD, batch tasks):
   - Use `claude -p "task" --dangerously-skip-permissions --output-format json`
   - Headless mode for scripted pipelines

**Starting in Specific Mode**:
```bash
# Start in plan mode for analysis
claude --permission-mode plan

# Start in auto-accept for implementation
claude --permission-mode acceptEdits

# Fully autonomous (containers only)
claude --dangerously-skip-permissions

# Headless mode for CI/CD pipelines
claude -p "run dotnet test" --output-format json
```

---

## New Claude Code Features (February 2026)

### Hooks System

Hooks fire custom shell commands at specific points during a Claude Code session. Configure in `.claude/settings.json` or `~/.claude/settings.json`:

```json
{
  "hooks": {
    "PreToolUse": [
      {
        "matcher": "Bash",
        "command": "scripts/validate-bash-command.sh"
      }
    ],
    "PostToolUse": [
      {
        "matcher": "Edit",
        "command": "scripts/post-edit-lint.sh"
      }
    ],
    "TaskCompleted": [
      {
        "command": "scripts/run-quality-gate.sh"
      }
    ]
  }
}
```

**Available hook events**: `PreToolUse`, `PostToolUse`, `PostToolUseFailure`, `PermissionRequest`, `TeammateIdle`, `TaskCompleted`, `Stop`

**Spaarke use cases**:
- Run `dotnet format` after file edits
- Validate ADR compliance on code changes
- Auto-run tests after implementation steps
- Enforce quality gates on task completion

### Headless / Non-Interactive Mode

Run Claude Code programmatically without a terminal UI:

```bash
# Single query, get JSON output
claude -p "run dotnet test and report results" --output-format json

# Pipe input
echo "explain this error" | claude -p --output-format json

# Combine with skip-permissions for CI/CD
claude -p "build and test" --dangerously-skip-permissions --output-format json
```

### MCP Server Integration

Claude Code can connect to [MCP servers](https://code.claude.com/docs/en/mcp) for external tool access. Configure in settings:

```json
{
  "mcpServers": {
    "dataverse": {
      "command": "node",
      "args": ["mcp-servers/dataverse-server.js"],
      "env": {
        "DATAVERSE_URL": "https://spaarkedev1.crm.dynamics.com"
      }
    }
  }
}
```

MCP tools appear as regular tools and work with permission rules: `mcp__dataverse__query`.

### Session Management

| Command | Purpose |
|---------|---------|
| `/resume` | Resume a previous session |
| `/rewind` | Rewind to an earlier point in the session |
| `/teleport` | Resume and configure remote sessions (claude.ai subscribers) |
| `/compact` | Compress conversation to free context |
| `/clear` | Wipe conversation and start fresh |

### Cost & Token Awareness

- **Effort parameter** controls token spend per-request (see Adaptive Thinking section above)
- Lower effort for subagents and simple tasks = significant cost savings
- Agent teams multiply token usage per teammate — use judiciously
- Monitor with `/status` during sessions

---

## Agent Teams (Experimental)

Agent teams coordinate multiple Claude Code instances working in parallel. One session acts as **team lead** (orchestrator), spawning **teammates** that work independently with their own context windows and communicate directly with each other.

### Enabling Agent Teams

```json
// In .claude/settings.json
{
  "env": {
    "CLAUDE_CODE_EXPERIMENTAL_AGENT_TEAMS": "1"
  }
}
```

Or set the environment variable:
```cmd
setx CLAUDE_CODE_EXPERIMENTAL_AGENT_TEAMS "1"
```

### When to Use Agent Teams vs. Subagents

| Capability | Subagents (`Task` tool) | Agent Teams |
|-----------|------------------------|-------------|
| **Context** | Own window, results return to caller | Own window, fully independent |
| **Communication** | Report back to main agent only | Teammates message each other directly |
| **Coordination** | Main agent manages all work | Shared task list with self-coordination |
| **Token cost** | Lower (results summarized) | Higher (each is a separate Claude instance) |
| **Best for** | Focused research, exploration, quick checks | Parallel implementation, competing hypotheses, cross-layer work |

**Use subagents for**: Quick research, code exploration, focused verification tasks
**Use agent teams for**: Parallel feature implementation, multi-perspective code review, debugging with competing theories, cross-layer changes (API + PCF + tests)

### Agent Teams for Spaarke Projects

**Parallel code review** (3 reviewers):
```
Create an agent team to review PR #142. Spawn three reviewers:
- One focused on security implications
- One checking performance against ADRs
- One validating test coverage
```

**Parallel feature implementation** (requires separate file ownership):
```
Create a team for the financial-intelligence module:
- Teammate 1: BFF API endpoints (src/server/api/)
- Teammate 2: PCF control (src/client/pcf/)
- Teammate 3: Integration tests (tests/)
Use Sonnet for each teammate. Require plan approval before changes.
```

**Investigation/debugging**:
```
Users report slow document uploads. Spawn 3 teammates to investigate:
- One checking the SpeFileStore facade performance
- One analyzing the Redis caching layer
- One profiling the AI pipeline
Have them debate findings and converge on root cause.
```

### Display Modes

| Mode | Setting | Description |
|------|---------|-------------|
| **In-process** (default) | `"teammateMode": "in-process"` | All teammates in main terminal. Use Shift+Up/Down to navigate. |
| **Split panes** | `"teammateMode": "tmux"` | Each teammate in own pane. Requires tmux or iTerm2. |

```bash
# Force in-process for a single session
claude --teammate-mode in-process
```

### Delegate Mode

When agent teams are active, press **Shift+Tab** to cycle into **Delegate Mode** — restricts the lead to coordination-only tools (spawning, messaging, task management). All implementation happens through teammates.

### Key Rules for Spaarke Agent Teams

- Each teammate loads `CLAUDE.md`, MCP servers, and skills automatically
- Teammates start with the lead's permission settings
- **Avoid file conflicts**: break work so each teammate owns different files/directories
- Use `/conflict-check` before starting parallel work
- Teammates cannot spawn their own teams (no nesting)
- One team per session — clean up before starting a new team

### Quality Gates with Hooks

Use `TeammateIdle` and `TaskCompleted` hooks to enforce quality:
- `TeammateIdle`: Run code-review when a teammate finishes
- `TaskCompleted`: Run adr-check before marking task complete

---

## 🚀 Project Initialization: Developer Workflow

**Standard 2-Step Process for New Projects:**

### Step 1: Create AI-Optimized Specification

**If you have a human design document** (Word doc, design.md, rough notes):
```bash
/design-to-spec projects/{project-name}
```
- Transforms narrative design → structured spec.md
- Adds preliminary ADR references (constraints only)
- Flags ambiguities for clarification
- **Output**: `projects/{project-name}/spec.md` (AI-ready specification)

**OR manually write** `projects/{project-name}/spec.md` with:
- Executive Summary, Scope, Requirements, Success Criteria, Technical Approach

**→ Review spec.md before proceeding to Step 2**

---

### Step 2: Initialize Project (Full Pipeline)

```bash
/project-pipeline projects/{project-name}
```

This orchestrates the complete setup (internal pipeline steps a-f):
- a. ✅ Validates spec.md
- b. ✅ **Comprehensive resource discovery** (ADRs, skills, patterns, knowledge docs)
- c. ✅ Generates artifacts (README.md, PLAN.md, CLAUDE.md, folder structure)
- d. ✅ Creates 50-200+ task files with full context
- e. ✅ Creates feature branch and initial commit
- f. ✅ Optionally starts task 001

**→ Human-in-loop confirmations after each major step**

---

### Step 3: Execute Tasks

**If project-pipeline auto-started task 001** (you said 'y' in Step 2):
- Already executing task 001 in same session
- Continue until task complete

**If resuming in new session or moving to next task** (task 001 is typically auto-started by pipeline step f):
```bash
work on task 002
# OR
continue with next task
# OR
resume task 005
```

**What happens**: Natural language phrases automatically invoke the `task-execute` skill, which loads:
- Task file (POML format)
- Applicable ADRs and constraints
- Knowledge docs and patterns
- Project context (README, PLAN, CLAUDE.md)

**Explicit invocation** (alternative):
```bash
/task-execute projects/{project-name}/tasks/002-*.poml
```

---

### 🚨 MANDATORY: Task Execution Protocol for Claude Code

**ABSOLUTE RULE**: When executing project tasks, Claude Code MUST invoke the `task-execute` skill. DO NOT read POML files directly and implement manually.

#### Why This Matters

The task-execute skill ensures:
- ✅ Knowledge files are loaded (ADRs, constraints, patterns)
- ✅ Context is properly tracked in current-task.md
- ✅ Proactive checkpointing occurs every 3 steps
- ✅ Quality gates run (code-review + adr-check) at Step 9.5
- ✅ Progress is recoverable after compaction
- ✅ PCF version bumping follows protocol
- ✅ Deployment follows dataverse-deploy skill

**Bypassing this skill leads to**:
- ❌ Missing ADR constraints
- ❌ No checkpointing - lost progress after compaction
- ❌ Skipped quality gates
- ❌ Manual errors (forgotten version bumps, etc.)

#### Auto-Detection Rules (Trigger Phrases)

When Claude Code detects these phrases, it MUST invoke task-execute skill:

| User Says | Required Action | Implementation |
|-----------|-----------------|----------------|
| "work on task X" | Execute task X | Invoke task-execute with task X POML file |
| "continue" | Execute next pending task | Check TASK-INDEX.md for next 🔲, invoke task-execute |
| "continue with task X" | Execute task X | Invoke task-execute with task X POML file |
| "next task" | Execute next pending task | Check TASK-INDEX.md for next 🔲, invoke task-execute |
| "keep going" | Execute next pending task | Check TASK-INDEX.md for next 🔲, invoke task-execute |
| "resume task X" | Execute task X | Invoke task-execute with task X POML file |
| "pick up where we left off" | Execute task from current-task.md | Load current-task.md, invoke task-execute |

**Example - User says "continue"**:
1. Read `projects/{project-name}/tasks/TASK-INDEX.md`
2. Find first task with status 🔲 (pending)
3. Invoke Skill tool with skill="task-execute" and task file path
4. Let task-execute orchestrate the full protocol

#### Parallel Task Execution

When tasks can run in parallel (no dependencies between them), each task MUST still use task-execute:

**CORRECT Approach**:
- Single message with multiple Skill tool invocations
- Each invocation calls task-execute with a different task file
- Example: Tasks 020, 021, 022 can run in parallel → Send one message with three Skill tool calls

**INCORRECT Approach**:
- Reading multiple POML files directly
- Manually implementing from multiple tasks
- Bypassing task-execute for "efficiency"

See task-execute SKILL.md Step 8 for the complete execution protocol with checkpointing rules.

#### Enforcement Checklist for Claude Code

Before implementing ANY task:
- [ ] Did user request task work? (any trigger phrase above)
- [ ] Did I invoke the task-execute skill?
- [ ] Did I load the task POML file directly? (❌ WRONG)
- [ ] Did I implement changes without task-execute? (❌ WRONG)

If you answered "no" to the second question or "yes" to questions 3-4, STOP and invoke task-execute instead.

---

### ⚠️ Component Skills (AI Internal Use Only)

These skills are **called BY orchestrators** and should NOT be invoked directly by developers:

| Skill | Purpose | Called By |
|-------|---------|-----------|
| `project-setup` | Generate artifacts only (README, PLAN, CLAUDE.md) | `project-pipeline` (pipeline step c) |
| `task-create` | Decompose plan.md into task files | `project-pipeline` (pipeline step d) |
| `adr-aware` | Load applicable ADRs based on resource types | Multiple skills (auto) |
| `script-aware` | Discover and reuse scripts from library | Multiple skills (auto) |

**Exception**: `task-execute` is also a skill, but it **IS developer-facing** for daily task work:
- Used in Step 3 above: `work on task 001` invokes `task-execute`
- Ensures proper context loading (knowledge files, ADRs, patterns)
- Primary workflow for implementing individual tasks

**When in doubt → Use `/project-pipeline`** (it orchestrates everything)

---

## 🚨 AI Execution Rules (Critical)

### Context Management

| Usage | Action |
|-------|--------|
| < 60% | ✅ Proceed normally |
| 60-70% | ⚠️ Run `/checkpoint` - proactive save, then continue |
| > 70% | 🛑 STOP - Run `/checkpoint`, request `/compact` |
| > 85% | 🚨 EMERGENCY - Immediately run `/checkpoint` and stop |

**Commands**: `/context` (check) · `/checkpoint` (save state) · `/compact` (compress) · `/clear` (wipe)

### Proactive Checkpointing (MANDATORY)

**Claude MUST checkpoint frequently during task execution. These rules are NOT optional.**

| Condition | Action |
|-----------|--------|
| After every 3 completed task steps | Run `context-handoff` (silent: "✅ Checkpoint.") |
| After modifying 5+ files in session | Run `context-handoff` |
| After any deployment operation | Run `context-handoff` |
| Before starting a complex step | Run `context-handoff` |
| Context > 60% | Run `context-handoff` (verbose report) |
| Context > 70% | Run `context-handoff` + STOP + request `/compact` |

**Checkpoint behavior**:
- Update `current-task.md` Quick Recovery section
- Report briefly: "✅ Checkpoint saved. Continuing..."
- Continue working (don't wait for user unless context > 70%)

### Context Persistence

**All work state must be recoverable from files alone.**

| File | Purpose | Updated By |
|------|---------|------------|
| `projects/{project-name}/current-task.md` | Active task state, completed steps, files modified | `task-execute` skill |
| `projects/{project-name}/CLAUDE.md` | Project context, decisions, constraints | Manual or skills |
| `projects/{project-name}/tasks/TASK-INDEX.md` | Task status overview | `task-execute` skill |

**Resuming Work in a New Session**:

| What You Want | What to Say |
|---------------|-------------|
| Resume where you left off | "Where was I?" or "Continue" |
| Resume specific task | "Continue task 013" |
| Resume specific project | "Continue work on {project-name}" |
| Check all project status | "/project-status" |
| Save progress before stopping | "Save my progress" (invokes `context-handoff`) |

**Full Protocol**: [Context Recovery Procedure](docs/procedures/context-recovery.md)

### Human Escalation Triggers

**MUST request human input for**:
- Ambiguous or conflicting requirements
- Security-sensitive code (auth, secrets, encryption)
- ADR conflicts or violations
- Breaking changes (API contracts, DB schema)
- Scope expansion beyond task boundaries

**Format**: Use 🔔 **Human Input Required** block with situation, options, recommendation.

### Task Completion and Transition

After completing any task:
1. Update task `.poml` file status to "completed"
2. Update `TASK-INDEX.md` status: 🔲 → ✅
3. **Reset `current-task.md`** for next task (clears steps, files, decisions)
4. Set `current-task.md` to next pending task (or "none" if project complete)
5. Report completion and ask if ready for next task

**Important**: `current-task.md` tracks only the **active task**, not history. Task history is preserved in TASK-INDEX.md and individual .poml files.

**Full protocols**: `.claude/protocols/` (AIP-001, AIP-002, AIP-003)

---

## Task Execution Rigor Levels

All tasks are executed via `task-execute` skill with one of three rigor levels determined automatically by decision tree.

### Rigor Level Overview

| Level | When Applied | Protocol Steps | Reporting Frequency | Quality Gates |
|-------|--------------|----------------|---------------------|---------------|
| **FULL** | Code implementation, architecture changes, post-compaction recovery | All 11 steps mandatory | After each step | ✅ code-review + adr-check |
| **STANDARD** | Tests, new file creation, tasks with constraints | 8 of 11 steps required | After major steps | ⏭️ Skipped |
| **MINIMAL** | Documentation, inventory, simple updates | 4 of 11 steps required | Start + completion only | ⏭️ Skipped |

### Automatic Detection (Decision Tree)

**FULL Protocol Required If Task Has ANY:**
- Tags: `bff-api`, `api`, `pcf`, `plugin`, `auth`
- Will modify code files (`.cs`, `.ts`, `.tsx`)
- Has 6+ steps in task definition
- Resuming after compaction or new session
- Description includes: "implement", "refactor", "create service"
- Dependencies on 3+ other tasks

**STANDARD Protocol Required If Task Has ANY:**
- Tags: `testing`, `integration-test`
- Will create new files
- Has explicit `<constraints>` or ADRs listed
- Phase 2.x or higher (integration/deployment phases)

**MINIMAL Protocol Otherwise:**
- Documentation tasks
- Inventory/checklist creation
- Simple configuration updates

### Mandatory Rigor Level Declaration

**At task start, Claude Code MUST output:**

```
🔒 RIGOR LEVEL: [FULL | STANDARD | MINIMAL]
📋 REASON: [Why this level was chosen based on decision tree]

📖 PROTOCOL STEPS TO EXECUTE:
  ✅ Step 0.5: Determine rigor level
  ✅ Step 1: Load Task File
  ✅ Step 2: Initialize current-task.md
  [... list continues based on rigor level ...]

Proceeding with Step 0...
```

This declaration is **non-negotiable** and makes protocol shortcuts visible.

### User Override

You can override automatic detection:
- **"Execute with FULL protocol"** → Forces all steps regardless of task type
- **"Execute with MINIMAL protocol"** → Use carefully, only for documentation
- **Default:** Auto-detect using decision tree

### Examples by Task Type

| Task Type | Example | Auto-Detected Level |
|-----------|---------|-------------------|
| Implement API endpoint | "Create authorization service" | **FULL** (bff-api tag) |
| Update PCF component | "Add dark mode to control" | **FULL** (pcf tag, .tsx files) |
| Write integration tests | "Test Document Profile endpoint" | **STANDARD** (testing tag) |
| Create deployment checklist | "Identify forms using control" | **MINIMAL** (documentation) |
| Refactor existing code | "Extract helper method" | **FULL** (code modification) |
| Update documentation | "Add deployment guide" | **MINIMAL** (docs) |

### Audit Trail in current-task.md

Rigor level and execution are logged for recovery:

```markdown
### Task XXX Details

**Rigor Level:** FULL
**Reason:** Task tags include 'bff-api' (code implementation)
**Protocol Steps Executed:**
- [x] Step 0.5: Determined rigor level
- [x] Step 1: Load Task File
- [x] Step 2: Initialize current-task.md
- [x] Step 4a: Load Constraints (api.md, testing.md)
- [x] Step 4b: Load Patterns (endpoint-definition.md)
[... etc]
```

**Full Details:** See `.claude/skills/task-execute/SKILL.md` Step 0.5 for complete decision tree and protocol requirements.

---

## 🛠️ AI Agent Skills (MANDATORY)

**Skills are structured procedures that MUST be followed when triggered.**

### Skill Discovery

**BEFORE starting any project-related work**, check `.claude/skills/INDEX.md` for applicable workflows.

**Skill Location**: `.claude/skills/{skill-name}/SKILL.md`

### Trigger Phrases → Required Skills

When these phrases are detected, **STOP** and load the corresponding skill:

| Trigger Phrase | Skill | Action |
|----------------|-------|--------|
| "design to spec", "transform spec", "create AI spec" | `design-to-spec` | Load `.claude/skills/design-to-spec/SKILL.md` - Transform human design docs to AI-ready spec.md |
| "start project", "initialize project from spec", "run project pipeline" | `project-pipeline` ⭐ | Load `.claude/skills/project-pipeline/SKILL.md` and run full pipeline (RECOMMENDED) |
| "create project artifacts", "generate artifacts", "project setup" | `project-setup` | Load `.claude/skills/project-setup/SKILL.md` (advanced users only) |
| "create tasks", "decompose plan", "generate tasks" | `task-create` | Load `.claude/skills/task-create/SKILL.md` and follow procedure |
| "review code", "code review" | `code-review` | Load `.claude/skills/code-review/SKILL.md` and follow checklist |
| "check ADRs", "validate architecture" | `adr-check` | Load `.claude/skills/adr-check/SKILL.md` and validate |
| "deploy to azure", "deploy api", "azure deployment", "deploy infrastructure" | `azure-deploy` | Load `.claude/skills/azure-deploy/SKILL.md` and follow procedure |
| "deploy to dataverse", "pac pcf push", "solution import", "deploy control", "publish customizations" | `dataverse-deploy` | Load `.claude/skills/dataverse-deploy/SKILL.md` and follow procedure |
| "edit ribbon", "add ribbon button", "ribbon customization", "command bar button" | `ribbon-edit` | Load `.claude/skills/ribbon-edit/SKILL.md` and follow procedure |
| "pull from github", "update from remote", "sync with github", "git pull", "get latest" | `pull-from-github` | Load `.claude/skills/pull-from-github/SKILL.md` and follow procedure |
| "push to github", "create PR", "commit and push", "ready to merge", "submit changes" | `push-to-github` | Load `.claude/skills/push-to-github/SKILL.md` and follow procedure |
| "update AI procedures", "add new ADR", "propagate changes", "maintain procedures" | `ai-procedure-maintenance` | Load `.claude/skills/ai-procedure-maintenance/SKILL.md` and follow checklists |
| "create worktree", "setup worktree", "new project worktree", "worktree for project" | `worktree-setup` | Load `.claude/skills/worktree-setup/SKILL.md` and follow procedure |
| "continue project", "resume project", "where was I", "pick up where I left off" | `project-continue` | Load `.claude/skills/project-continue/SKILL.md` and sync + load context |
| "save progress", "save my state", "context handoff", "checkpoint", "checkpoint before compaction" | `context-handoff` | Load `.claude/skills/context-handoff/SKILL.md` and save state for recovery |
| "clean up dev", "clear caches", "fix auth issues", "azure cli issues", "dev environment cleanup" | `dev-cleanup` | Load `.claude/skills/dev-cleanup/SKILL.md` and run cleanup script |
| "merge to master", "check unmerged branches", "reconcile branches", "sync master", "audit branches" | `merge-to-master` | Load `.claude/skills/merge-to-master/SKILL.md` and follow procedure |

### Auto-Detection Rules

| Condition | Required Skill |
|-----------|---------------|
| `projects/{project-name}/design.md` (or .docx, .pdf) exists but `spec.md` doesn't | Run `design-to-spec` to transform |
| `projects/{project-name}/spec.md` exists but `README.md` doesn't | Run `project-pipeline` (or `project-setup` if user requests minimal) |
| `projects/{project-name}/plan.md` exists but `tasks/` is empty | Run `task-create` |
| Creating API endpoint, PCF control, or plugin | Apply `adr-aware` (always-apply) |
| Writing any code | Apply `spaarke-conventions` (always-apply) |
| Running `pac` commands, deploying to Dataverse | Load `dataverse-deploy` skill first |
| Running `az` commands, deploying to Azure | Load `azure-deploy` skill first |
| Modifying ribbon XML, `RibbonDiffXml`, or command bar | Load `ribbon-edit` skill first |
| Resuming work on existing project (has tasks/, CLAUDE.md) | Run `project-continue` to sync and load context |
| Starting new project from master | Run `merge-to-master` in audit mode to check for stranded branches |
| Completing final task in a project | Prompt user to run `merge-to-master` to merge branch into master |

### Always-Apply Skills

These skills are **automatically active** during all coding work:

| Skill | Purpose |
|-------|---------|
| `adr-aware` | Proactively load relevant ADRs before creating resources |
| `script-aware` | Discover and reuse scripts from library before writing new automation |
| `spaarke-conventions` | Apply naming conventions and code patterns |

### Slash Commands

Use these commands to explicitly invoke skills:

| Command | Purpose |
|---------|----------|
| `/design-to-spec {path}` | Transform human design doc to AI-optimized spec.md |
| `/project-status [name]` | Check project status and get next action |
| `/project-pipeline {path}` | **⭐ RECOMMENDED**: Full pipeline - spec → ready tasks + branch |
| `/project-setup {path}` | Generate artifacts only (advanced users) |
| `/task-create {path}` | Decompose plan into task files |
| `/repo-cleanup` | Repository hygiene audit and ephemeral file cleanup |
| `/code-review` | Review recent changes |
| `/adr-check` | Validate ADR compliance |
| `/azure-deploy` | Deploy Azure infrastructure, BFF API, or configure App Service |
| `/dataverse-deploy` | Deploy PCF, solutions, or web resources to Dataverse |
| `/ribbon-edit` | Edit Dataverse ribbon via solution export/import |
| `/pull-from-github` | Pull latest changes from GitHub |
| `/push-to-github` | Commit changes and push to GitHub |
| `/ai-procedure-maintenance` | Propagate updates when adding ADRs, patterns, constraints, skills |
| `/worktree-setup` | Create and manage git worktrees for parallel project development |
| `/project-continue {name}` | Continue project after PR merge or new session with full context |
| `/context-handoff` | Save working state before compaction for reliable recovery |
| `/checkpoint` | Alias for `/context-handoff` - quick state save |
| `/dev-cleanup` | Clean up local dev environment caches (Azure CLI, NuGet, npm, Git credentials) |
| `/merge-to-master` | Merge completed branch work into master (audit, single merge, or full reconciliation) |

---

## Documentation

### AI-Optimized Context (Load First)

`.claude/` contains **concise, AI-optimized** content for efficient context loading:

```
.claude/
├── adr/                      # Concise ADRs (~100-150 lines each)
├── constraints/              # MUST/MUST NOT rules by topic
├── patterns/                 # Code patterns and examples
├── protocols/                # AI behavior protocols
├── skills/                   # Skill definitions and workflows
└── templates/                # Project/task templates
```

### Full Reference Documentation (Load When Needed)

`docs/` contains **complete documentation** for deep dives:

```
docs/
├── adr/                      # Full ADRs with history and rationale
├── architecture/             # System architecture docs
├── guides/                   # How-to guides and procedures
├── procedures/               # Process documentation
├── standards/                # Coding and auth standards
└── product-documentation/    # User-facing docs
```

### Loading Strategy

| Need | Location | Action |
|------|----------|--------|
| Constraints, patterns | `.claude/` | ✅ Load by default |
| Full rationale, history | `docs/` | ⚠️ Load when deep dive needed |
| ADR constraints | `.claude/adr/ADR-XXX.md` | ✅ Use for implementation |
| ADR full context | `docs/adr/ADR-XXX-*.md` | ⚠️ Use for architectural decisions |

---

## Azure Infrastructure Resources

**Avoid discovery queries** — resource names and endpoints are pre-documented below.
**Operational commands are permitted** (deployments, secret management, configuration).

- ❌ Don't run: `az resource list`, `az webapp show`, `az cognitiveservices account show`
- ✅ Do run: `az webapp deploy`, `az keyvault secret set`, `az deployment group create`

### Quick Endpoints (Dev Environment)

| Service | Endpoint |
|---------|----------|
| BFF API | `https://spe-api-dev-67e2xz.azurewebsites.net` |
| Azure OpenAI | `https://spaarke-openai-dev.openai.azure.com/` |
| Document Intelligence | `https://westus2.api.cognitive.microsoft.com/` |
| Azure AI Search | `https://spaarke-search-dev.search.windows.net/` |

### Resource Documentation

| Need | Location | Content |
|------|----------|---------|
| AI resources (OpenAI, Doc Intel, AI Search, AI Foundry) | [`docs/architecture/auth-AI-azure-resources.md`](docs/architecture/auth-AI-azure-resources.md) | Endpoints, models, CLI commands |
| All Azure resources | [`docs/architecture/auth-azure-resources.md`](docs/architecture/auth-azure-resources.md) | Full Azure inventory |
| AI Foundry infrastructure | [`infrastructure/ai-foundry/README.md`](infrastructure/ai-foundry/README.md) | Hub, Project, Prompt Flows |
| Resource naming conventions | [`docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md`](docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md) | Naming patterns |

### Key Resource Names

| Resource Type | Dev Environment |
|--------------|-----------------|
| Resource Group | `spe-infrastructure-westus2` |
| App Service | `spe-api-dev-67e2xz` |
| Azure OpenAI | `spaarke-openai-dev` |
| Document Intelligence | `spaarke-docintel-dev` |
| AI Search | `spaarke-search-dev` |
| AI Foundry Hub | `sprkspaarkedev-aif-hub` |
| AI Foundry Project | `sprkspaarkedev-aif-proj` |
| Key Vault | `spaarke-spekvcert` |

### Dataverse Environments

| Environment | URL | Purpose |
|-------------|-----|---------|
| Dev | `https://spaarkedev1.crm.dynamics.com` | Development/testing |

---

## Project Overview

**Spaarke** is a SharePoint Document Access Platform (SDAP) built with:
- **.NET 8 Minimal API** (Backend) - SharePoint Embedded integration via Microsoft Graph
- **Power Platform PCF Controls** (Frontend) - Field-bound TypeScript/React controls for Dataverse forms
- **React Code Pages** (Frontend) - Standalone React 18 dialogs and pages (document viewers, wizards, panels)
- **Dataverse Plugins** (Platform) - Thin validation/projection plugins

## Repository Structure

```
projects/                      # Active development projects
├── {project-name}/            # Each project has its own folder
│   ├── spec.md                # Design specification (input)
│   ├── README.md              # Project overview (generated)
│   ├── plan.md                # Implementation plan (generated)
│   ├── CLAUDE.md              # Project-specific AI context
│   ├── tasks/                 # Task files (POML format)
│   └── notes/                 # Ephemeral working files

src/
├── client/                    # Frontend components
│   ├── pcf/                   # PCF Controls (React 16/17, field-bound form controls)
│   ├── code-pages/            # React Code Pages (React 18, standalone dialogs & pages)
│   ├── office-addins/         # Office Add-ins (React 18)
│   └── shared/                # Shared UI components library (React 18-compatible)
├── server/                    # Backend services
│   ├── api/                   # .NET 8 Minimal API (Sprk.Bff.Api)
│   └── shared/                # Shared .NET libraries
└── solutions/                 # Dataverse solution projects

tests/                         # Unit and integration tests
docs/                          # Documentation (see above)
infrastructure/                # Azure Bicep templates
```

## Architecture Decision Records (ADRs)

ADRs are in `.claude/adr/` (concise) and `docs/adr/` (full). The key constraints are summarized here—**reference ADRs only if you need historical context** for why a decision was made.

| ADR | Summary | Key Constraint |
|-----|---------|----------------|
| ADR-001 | Minimal API + BackgroundService | **No Azure Functions** |
| ADR-002 | Thin Dataverse plugins | **No HTTP/Graph calls in plugins; <50ms p95** |
| ADR-006 | Anti-legacy-JS: PCF for form controls, React Code Pages for dialogs | **No legacy JS webresources; field-bound → PCF; standalone dialog → Code Page** |
| ADR-007 | SpeFileStore facade | **No Graph SDK types leak above facade** |
| ADR-008 | Endpoint filters for auth | **No global auth middleware; use endpoint filters** |
| ADR-009 | Redis-first caching | **No hybrid L1 cache unless profiling proves need** |
| ADR-010 | DI minimalism | **≤15 non-framework DI registrations** |
| ADR-012 | Shared component library | **Reuse `@spaarke/ui-components` across modules** |
| ADR-013 | AI Architecture | **AI Tool Framework; extend BFF, not separate service** |
| ADR-021 | Fluent UI v9 Design System | **All UI must use Fluent v9; no hard-coded colors; dark mode required** |
| ADR-022 | PCF Platform Libraries (field-bound controls only) | **PCF: React 16 APIs, platform-provided; Code Pages: React 18, bundled** |

## AI Architecture

AI features are built using the **AI Tool Framework** - see `docs/guides/SPAARKE-AI-ARCHITECTURE.md`.

| Component | Purpose |
|-----------|---------|
| `AiToolEndpoints.cs` | Streaming + enqueue endpoints |
| `AiToolService` | Tool orchestrator |
| `IAiToolHandler` | Interface for tool implementations |
| `AiToolAgent` PCF | Embedded streaming AI UI component |

Key pattern: **Dual Pipeline** - SPE storage + AI processing execute in parallel.

## Coding Standards

### .NET (Backend)

```csharp
// ✅ DO: Use concrete types unless a seam is required
services.AddSingleton<SpeFileStore>();
services.AddSingleton<AuthorizationService>();

// ✅ DO: Use endpoint filters for authorization
app.MapGet("/api/documents/{id}", GetDocument)
   .AddEndpointFilter<DocumentAuthorizationFilter>();

// ✅ DO: Keep services focused and small
public class SpeFileStore { /* Only SPE operations */ }

// ❌ DON'T: Inject Graph SDK types into controllers
public class BadController(GraphServiceClient graph) { } // WRONG

// ❌ DON'T: Use global middleware for resource authorization
app.UseMiddleware<AuthorizationMiddleware>(); // WRONG
```

### TypeScript/PCF (Field-Bound Form Controls — React 16/17)

```typescript
// ✅ DO: Import from shared component library
import { DataGrid, StatusBadge } from "@spaarke/ui-components";

// ✅ DO: Use Fluent UI v9 exclusively
import { Button, Input } from "@fluentui/react-components";

// ✅ DO: Use React 16 APIs (platform-provided, not bundled)
// ReactControl returns element; ReactDOM.render() for StandardControl
public updateView(context): React.ReactElement {
    return React.createElement(FluentProvider, { theme }, React.createElement(MyComponent, { context }));
}

// ❌ DON'T: Create new legacy webresources (no-framework JS, jQuery)
// ❌ DON'T: Use React 18 createRoot() in PCF — platform only provides React 16/17
// ❌ DON'T: Mix Fluent UI versions (v8 and v9)
// ❌ DON'T: Hard-code Dataverse entity schemas
// ❌ DON'T: Use custom page + PCF wrapper for standalone dialogs — use a Code Page instead
```

### TypeScript/React Code Pages (Standalone Dialogs & Pages — React 18)

```typescript
// ✅ DO: Use React 18 createRoot (bundled, not platform-provided)
import { createRoot } from "react-dom/client";
const params = new URLSearchParams(window.location.search);
createRoot(document.getElementById("root")!).render(
    <FluentProvider theme={webLightTheme}><App documentId={params.get("documentId") ?? ""} /></FluentProvider>
);

// ✅ DO: Use WizardDialog / SidePanel from shared library for common layouts
import { WizardDialog, SidePanel } from "@spaarke/ui-components";

// ✅ DO: Open Code Page dialogs via navigateTo webresource (no custom page needed)
Xrm.Navigation.navigateTo(
    { pageType: "webresource", webresourceName: "sprk_mypagename", data: `documentId=${id}` },
    { target: 2, width: { value: 85, unit: "%" }, height: { value: 85, unit: "%" } }
);
```

### Dataverse Plugins

```csharp
// ✅ DO: Keep plugins thin (<200 LoC, <50ms execution)
public class ValidationPlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        // Validation only - no HTTP calls
        ValidateRequiredFields(context);
    }
}

// ❌ DON'T: Make HTTP/Graph calls from plugins
// ❌ DON'T: Implement orchestration logic in plugins
```

## Commands

| Action | Command |
|--------|---------|
| Build all | `dotnet build` |
| Build API | `dotnet build src/server/api/Sprk.Bff.Api/` |
| Build PCF | `cd src/client/pcf && npm run build` |
| Test all | `dotnet test` |
| Test with coverage | `dotnet test --collect:"XPlat Code Coverage" --settings config/coverlet.runsettings` |
| Run API | `dotnet run --project src/server/api/Sprk.Bff.Api/` |
| Format code | `dotnet format` |

**API Endpoints**: `https://localhost:5001` · Health check: `GET /healthz`

## File Naming Conventions

| Type | Convention | Example |
|------|------------|---------|
| C# files | PascalCase | `AuthorizationService.cs` |
| TypeScript files | PascalCase for components | `DataGrid.tsx` |
| TypeScript files | camelCase for utilities | `formatters.ts` |
| Test files | `{ClassName}.Tests.cs` | `AuthorizationService.Tests.cs` |
| ADRs | `ADR-{NNN}-{slug}.md` | `ADR-001-minimal-api-and-workers.md` |

## Error Handling

### API Responses
- Use `ProblemDetails` for all error responses
- Include correlation IDs for tracing
- Return user-friendly messages (not stack traces)

### PCF Controls
- Use try/catch with user-friendly error messages
- Log errors with context for debugging
- Show inline error states in UI

## Security Considerations

- **Never** commit secrets to the repository
- Use `config/*.local.json` for local secrets (gitignored)
- Use Azure Key Vault for production secrets
- All API endpoints require authentication (except `/healthz`, `/ping`)

## Development Lifecycle

All coding projects follow this process:

| Phase | Description | Artifacts |
|-------|-------------|-----------|
| 1. **Product Feature Request** | Business need or user story | Feature request document |
| 2. **Solution Assessment** | Evaluate approaches, identify risks | Assessment document |
| 3. **Detailed Design Specification** | Technical design, data models, APIs | Design spec (.docx or .md) |
| 4. **Project Plan** | Timeline, milestones, dependencies | `plan.md` in project folder |
| 5. **Tasks** | Granular work items | `tasks/*.poml` in project folder |
| 6. **Product Feature Documentation** | User-facing docs, admin guides | Docs in `docs/guides/` |
| 7. **Complete Project** | Code complete, tested, deployed | Working feature |
| 8. **Project Wrap-up** | Cleanup, archive, retrospective | Run `/repo-cleanup` |

### 🤖 AI-Assisted Development

For new projects, use `/design-to-spec` then `/project-pipeline`. See [Project Initialization: Developer Workflow](#-project-initialization-developer-workflow) for the complete workflow.

### Before Starting Work

1. **Check for skills** - Review `.claude/skills/INDEX.md` for applicable workflows
2. **Identify the phase** - What lifecycle phase is this work in?
3. **Check for existing artifacts** - Look for design specs, assessments
4. **Follow the workflow** - If a design spec exists, run `/design-to-spec` then `/project-pipeline`

### Working Checklist

**Before making changes:**
- Check ADRs align with your approach
- Review `.claude/skills/INDEX.md` for applicable workflows

**After completing work:**
- Run tests (`dotnet test`)
- Update task status in TASK-INDEX.md
- Update docs if behavior changed
- Run `/repo-cleanup` to validate structure
- Use small, focused commits

## Module-Specific Instructions

See `CLAUDE.md` files in subdirectories for module-specific guidance:
- `src/server/api/Sprk.Bff.Api/CLAUDE.md` - BFF API specifics
- `src/client/pcf/CLAUDE.md` - PCF control development
- `src/server/shared/CLAUDE.md` - Shared .NET libraries

---

*Last updated: February 11, 2026*
