# CLAUDE.md - Spaarke Repository Instructions

> **Last Updated**: January 6, 2026
>
> **Purpose**: This file provides repository-wide context and instructions for Claude Code when working in this codebase.

---

## Development Environment

### Claude Code Extended Context Settings

This monorepo uses extended context settings for multi-phase feature development. These environment variables enable Claude Code to handle complex project pipelines with deep context requirements:

```bash
MAX_THINKING_TOKENS=50000
CLAUDE_CODE_MAX_OUTPUT_TOKENS=64000
```

**Why Extended Context?**
- **Multi-phase projects**: AI Document Intelligence R1 has 100+ tasks across 8 phases
- **Deep resource discovery**: Skills load ADRs, knowledge docs, patterns, and existing code
- **Context-rich task execution**: Each task includes full project history, applicable constraints
- **Pipeline orchestration**: project-pipeline skill chains multiple component skills

**When to Use**:
- Running `/project-pipeline` for new projects with extensive specs (>2000 words)
- Executing complex tasks that touch multiple system areas (API + PCF + Dataverse)
- Working on AI features that require ADR-013/014/015/016 context loading
- Phase wrap-up tasks that synthesize learnings across many prior tasks

**Setting in Windows**:
```cmd
setx MAX_THINKING_TOKENS "50000"
setx CLAUDE_CODE_MAX_OUTPUT_TOKENS "64000"
```

**Verifying settings**:
```powershell
# In new terminal session
echo $env:MAX_THINKING_TOKENS
echo $env:CLAUDE_CODE_MAX_OUTPUT_TOKENS
```

### Claude Code Model and Permission Settings

This repository is configured for **Opus 4.5** with **auto-accept edits** via `.claude/settings.json`:

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
- **Default**: `opus` (Claude Opus 4.5 - claude-opus-4-5-20251101)
- **Check current**: `/model` or `/status` during session
- **Override**: `claude --model sonnet` for faster, simpler tasks

**Permission Modes** (cycle with **Shift+Tab**):

| Mode | Indicator | Use Case |
|------|-----------|----------|
| **Auto-Accept** | `⏵⏵ accept edits on` | Task implementation (default) |
| **Plan Mode** | `⏸ plan mode on` | Code analysis, planning, exploration |
| **Ask Mode** | (none) | Explicit approval for each change |

**Recommended Workflow**:

1. **Planning Phase** (`/project-pipeline`, `/design-to-spec`):
   - Press **Shift+Tab** twice to enter **Plan Mode**
   - Claude analyzes, reads, plans without making changes
   - Review the plan before implementation

2. **Implementation Phase** (task execution):
   - Press **Shift+Tab** to return to **Auto-Accept Mode**
   - Claude implements changes automatically
   - Quality gates (code-review, adr-check) run at Step 9.5

**Starting in Specific Mode**:
```bash
# Start in plan mode for analysis
claude --permission-mode plan

# Start in auto-accept for implementation
claude --permission-mode acceptEdits

# Use Opus for planning, Sonnet for execution (hybrid)
claude --model opusplan
```

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
- **Power Platform PCF Controls** (Frontend) - TypeScript/React components for Dataverse model-driven apps
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
│   ├── pcf/                   # PCF Controls (TypeScript/React)
│   ├── office-addins/         # Office Add-ins
│   └── shared/                # Shared UI components library
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
| ADR-006 | PCF over webresources | **No new legacy JS webresources** |
| ADR-007 | SpeFileStore facade | **No Graph SDK types leak above facade** |
| ADR-008 | Endpoint filters for auth | **No global auth middleware; use endpoint filters** |
| ADR-009 | Redis-first caching | **No hybrid L1 cache unless profiling proves need** |
| ADR-010 | DI minimalism | **≤15 non-framework DI registrations** |
| ADR-012 | Shared component library | **Reuse `@spaarke/ui-components` across modules** |
| ADR-013 | AI Architecture | **AI Tool Framework; extend BFF, not separate service** |
| ADR-021 | Fluent UI v9 Design System | **All UI must use Fluent v9; no hard-coded colors; dark mode required** |
| ADR-022 | PCF Platform Libraries | **React 16 APIs only; unmanaged solutions only** |

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

### TypeScript/PCF (Frontend)

```typescript
// ✅ DO: Import from shared component library
import { DataGrid, StatusBadge } from "@spaarke/ui-components";

// ✅ DO: Use Fluent UI v9 exclusively
import { Button, Input } from "@fluentui/react-components";

// ✅ DO: Type all props and state
interface IMyComponentProps {
  items: IDataItem[];
  onSelect: (item: IDataItem) => void;
}

// ❌ DON'T: Create new legacy webresources
// ❌ DON'T: Mix Fluent UI versions (v8 and v9)
// ❌ DON'T: Hard-code Dataverse entity schemas
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

*Last updated: January 6, 2026*
