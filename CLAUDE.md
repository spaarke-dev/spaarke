# CLAUDE.md - Spaarke Repository Instructions

> **Last Updated**: December 24, 2025
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
```bash
# In new terminal session
echo $env:MAX_THINKING_TOKENS
echo $env:CLAUDE_CODE_MAX_OUTPUT_TOKENS
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
- **Output**: `projects/{name}/spec.md` (AI-ready specification)

**OR manually write** `projects/{project-name}/spec.md` with:
- Executive Summary, Scope, Requirements, Success Criteria, Technical Approach

**→ Review spec.md before proceeding to Step 2**

---

### Step 2: Initialize Project (Full Pipeline)

```bash
/project-pipeline projects/{project-name}
```

This orchestrates the complete setup:
1. ✅ Validates spec.md
2. ✅ **Comprehensive resource discovery** (ADRs, skills, patterns, knowledge docs)
3. ✅ Generates artifacts (README.md, PLAN.md, CLAUDE.md, folder structure)
4. ✅ Creates 50-200+ task files with full context
5. ✅ Creates feature branch and initial commit
6. ✅ Optionally starts task 001

**→ Human-in-loop confirmations after each major step**

---

### Step 3: Execute Tasks

**If project-pipeline auto-started task 001** (you said 'y' in Step 2):
- Already executing task 001 in same session
- Continue until task complete

**If resuming in new session or moving to next task**:
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
/task-execute projects/{name}/tasks/002-*.poml
```

---

### ⚠️ Component Skills (AI Internal Use Only)

These skills are **called BY orchestrators** and should NOT be invoked directly by developers:

| Skill | Purpose | Called By |
|-------|---------|-----------|
| `project-setup` | Generate artifacts only (README, PLAN, CLAUDE.md) | `project-pipeline` (Step 2) |
| `task-create` | Decompose plan.md into task files | `project-pipeline` (Step 3) |
| `adr-aware` | Load applicable ADRs based on resource types | Multiple skills (auto) |

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
| < 70% | ✅ Proceed normally |
| > 70% | 🛑 STOP - Update `current-task.md`, create handoff, request new session |
| > 85% | 🚨 EMERGENCY - Immediately update `current-task.md` and stop |

**Commands**: `/context` (check) · `/clear` (wipe) · `/compact` (compress)

### Context Persistence

**All work state must be recoverable from files alone.**

| File | Purpose | Updated By |
|------|---------|------------|
| `projects/{name}/current-task.md` | Active task state, completed steps, files modified | `task-execute` skill |
| `projects/{name}/CLAUDE.md` | Project context, decisions, constraints | Manual or skills |
| `projects/{name}/tasks/TASK-INDEX.md` | Task status overview | `task-execute` skill |

**Resuming Work in a New Session**:

| What You Want | What to Say |
|---------------|-------------|
| Resume where you left off | "Where was I?" or "Continue" |
| Resume specific task | "Continue task 013" |
| Resume specific project | "Continue work on {project-name}" |
| Check all project status | "/project-status" |
| Save progress before stopping | "Save my progress" |

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

**Full protocols**: `docs/reference/protocols/` (AIP-001, AIP-002, AIP-003)

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
| "deploy to dataverse", "pac pcf push", "solution import", "deploy control", "publish customizations" | `dataverse-deploy` | Load `.claude/skills/dataverse-deploy/SKILL.md` and follow procedure |
| "edit ribbon", "add ribbon button", "ribbon customization", "command bar button" | `ribbon-edit` | Load `.claude/skills/ribbon-edit/SKILL.md` and follow procedure |
| "pull from github", "update from remote", "sync with github", "git pull", "get latest" | `pull-from-github` | Load `.claude/skills/pull-from-github/SKILL.md` and follow procedure |
| "push to github", "create PR", "commit and push", "ready to merge", "submit changes" | `push-to-github` | Load `.claude/skills/push-to-github/SKILL.md` and follow procedure |

### Auto-Detection Rules

| Condition | Required Skill |
|-----------|---------------|
| `projects/{name}/design.md` (or .docx, .pdf) exists but `spec.md` doesn't | Run `design-to-spec` to transform |
| `projects/{name}/spec.md` exists but `README.md` doesn't | Run `project-pipeline` (or `project-setup` if user requests minimal) |
| `projects/{name}/plan.md` exists but `tasks/` is empty | Run `task-create` |
| Creating API endpoint, PCF control, or plugin | Apply `adr-aware` (always-apply) |
| Writing any code | Apply `spaarke-conventions` (always-apply) |
| Running `pac` commands, deploying to Dataverse | Load `dataverse-deploy` skill first |
| Modifying ribbon XML, `RibbonDiffXml`, or command bar | Load `ribbon-edit` skill first |

### Always-Apply Skills

These skills are **automatically active** during all coding work:

| Skill | Purpose |
|-------|---------|
| `adr-aware` | Proactively load relevant ADRs before creating resources |
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
| `/dataverse-deploy` | Deploy PCF, solutions, or web resources to Dataverse |
| `/ribbon-edit` | Edit Dataverse ribbon via solution export/import |
| `/pull-from-github` | Pull latest changes from GitHub |
| `/push-to-github` | Commit changes and push to GitHub |

---

## Documentation

Coding-relevant documentation is in `/docs/ai-knowledge/`. Reference these documents for architecture, standards, and workflow guidance.

**Do not reference `/docs/reference/` unless explicitly directed**—it contains background material not relevant to most coding tasks.

```
docs/
├── ai-knowledge/             # ✅ REFERENCE for coding tasks
│   ├── architecture/         # System patterns (SDAP, auth boundaries)
│   ├── standards/            # OAuth/OBO, Dataverse auth patterns
│   ├── guides/               # How-to procedures
│   └── templates/            # Project/task scaffolding
└── reference/                # ⚠️ DO NOT LOAD unless asked
    ├── adr/                  # Architecture Decision Records (system principles)
    ├── protocols/            # AI Protocols (AI behavior principles)
    ├── procedures/           # Full process documentation
    └── research/             # Verbose KM-* articles
```

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

ADRs are in `/docs/reference/adr/`. The key constraints are summarized here—**reference ADRs only if you need historical context** for why a decision was made.

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

## AI Architecture

AI features are built using the **AI Tool Framework** - see `docs/ai-knowledge/guides/SPAARKE-AI-ARCHITECTURE.md`.

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

## Common Tasks

### Building

```bash
# Build entire solution
dotnet build

# Build specific project
dotnet build src/server/api/Sprk.Bff.Api/

# Build PCF controls
cd src/client/pcf && npm run build
```

### Testing

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage" --settings config/coverlet.runsettings

# Run specific test project
dotnet test tests/unit/Sprk.Bff.Api.Tests/
```

### Running Locally

```bash
# Start API
dotnet run --project src/server/api/Sprk.Bff.Api/

# API available at: https://localhost:5001
# Health check: GET /healthz
```

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

**Standard 2-step workflow for new projects:**

1. **Transform design to spec**: `/design-to-spec projects/{name}`
   - Converts human design docs → AI-optimized `spec.md`

2. **Run project pipeline**: `/project-pipeline projects/{name}`
   - Validates spec → discovers resources → generates artifacts → creates tasks → ready to execute

See [Project Initialization: Developer Workflow](#-project-initialization-developer-workflow) section above for full details.

### Before Starting Work

1. **Check for skills** - Review `.claude/skills/INDEX.md` for applicable workflows
2. **Identify the phase** - What lifecycle phase is this work in?
3. **Check for existing artifacts** - Look for design specs, assessments
4. **Follow the workflow** - If a design spec exists, run `/design-to-spec` then `/project-pipeline`

### After Completing Work

1. **Run tests** - Ensure all tests pass
2. **Update task status** - Mark tasks complete in TASK-INDEX.md
3. **Run `/repo-cleanup`** - Validate repository structure
4. **Remove ephemeral files** - Clean up notes/debug, notes/spikes, notes/drafts

## When Making Changes

1. **Check ADRs** - Ensure changes align with architectural decisions
2. **Run tests** - `dotnet test` before committing
3. **Update docs** - If changing behavior, update relevant documentation
4. **Small commits** - Prefer focused, atomic commits

## Module-Specific Instructions

See `CLAUDE.md` files in subdirectories for module-specific guidance:
- `src/server/api/Spe.Bff.Api/CLAUDE.md` - BFF API specifics
- `src/client/pcf/CLAUDE.md` - PCF control development
- `src/server/shared/CLAUDE.md` - Shared .NET libraries

## Quick Reference

| Action | Command |
|--------|---------|
| Build all | `dotnet build` |
| Test all | `dotnet test` |
| Run API | `dotnet run --project src/server/api/Spe.Bff.Api/` |
| Build PCF | `cd src/client/pcf && npm run build` |
| Format code | `dotnet format` |

---

*Last updated: December 2025*
