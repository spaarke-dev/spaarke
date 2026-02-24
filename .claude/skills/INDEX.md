# Skills Index

> **Purpose**: Central registry of all Claude Code skills — the **single source of truth** for what skills exist, their triggers, and how to create new ones.
>
> **Last Updated**: February 24, 2026 (added code-page-deploy skill, enhanced bff-deploy and pcf-deploy with path maps)

---

## How This File Is Used

**This file is actively referenced during Claude Code sessions:**

1. **From root `CLAUDE.md`**: The instruction "check `.claude/skills/INDEX.md` for applicable workflows" directs Claude here before starting project work.

2. **Skill Discovery**: When Claude needs to determine which skill applies to a task, it consults this index to find:
   - Available skills and their triggers
   - Which skills are always-apply vs explicit invocation
   - Skill categories and tiers

3. **Skill Creation**: When adding new skills, this file provides the authoritative template and metadata requirements.

**Related Files**:
| File | Role | When to Use |
|------|------|-------------|
| **This file (INDEX.md)** | Skill registry | Look up what skills exist, their triggers |
| [SKILL-INTERACTION-GUIDE.md](SKILL-INTERACTION-GUIDE.md) | Interaction playbook | Understand how skills work together, decision trees |
| `.claude/skills/{name}/SKILL.md` | Individual skill | Execute a specific skill's procedure |
| Root `CLAUDE.md` | Entry point | References this index in "AI Agent Skills" section |

---

## Available Skills

| Skill | Description | Always Apply | Trigger |
|-------|-------------|--------------|---------|
| [adr-aware](adr-aware/SKILL.md) | Proactively load ADRs when creating resources | **Yes** | Auto-applied |
| [ai-procedure-maintenance](ai-procedure-maintenance/SKILL.md) | Maintain AI procedures when adding ADRs, patterns, skills | No | "update AI procedures", "add new ADR" |
| [script-aware](script-aware/SKILL.md) | Discover and reuse scripts from library before writing new code | **Yes** | Auto-applied |
| [adr-check](adr-check/SKILL.md) | Validate code against Architecture Decision Records | No | `/adr-check`, "check ADRs" |
| [azure-deploy](azure-deploy/SKILL.md) | Deploy Azure infrastructure, BFF API, and configure App Service | No | "deploy to azure", "deploy api", "azure deployment" |
| [bff-deploy](bff-deploy/SKILL.md) | Deploy BFF API to Azure App Service using deployment script | No | "deploy bff", "publish bff", "bff deploy", "update bff api" |
| [ci-cd](ci-cd/SKILL.md) | GitHub Actions CI/CD pipeline status and workflow management | No | `/ci-cd`, "check CI", "build status", "workflow failed" |
| [code-review](code-review/SKILL.md) | Comprehensive code review (security, performance, style) | No | `/code-review`, "review code" |
| [conflict-check](conflict-check/SKILL.md) | Detect file conflicts between active PRs and current work | No | `/conflict-check`, "check conflicts", "file overlap" |
| [dataverse-create-schema](dataverse-create-schema/SKILL.md) | Create/update Dataverse entities, attributes, relationships via Web API | No | "create entity", "add column", "dataverse schema" |
| [dataverse-deploy](dataverse-deploy/SKILL.md) | Deploy solutions, plugins, web resources to Dataverse | No | "deploy to dataverse", "deploy solution" |
| [pcf-deploy](pcf-deploy/SKILL.md) | Build, pack, and deploy PCF controls via solution ZIP import | No | "deploy pcf", "build and deploy pcf", "pcf solution import" |
| [code-page-deploy](code-page-deploy/SKILL.md) | Build and deploy React Code Page web resources to Dataverse | No | "deploy code page", "deploy web resource", "build webresource" |
| [design-to-spec](design-to-spec/SKILL.md) | Transform human design documents into AI-optimized spec.md | No | `/design-to-spec`, "design to spec" |
| [pull-from-github](pull-from-github/SKILL.md) | Pull latest changes from GitHub | No | `/pull-from-github`, "pull from github" |
| [push-to-github](push-to-github/SKILL.md) | Commit changes and push to GitHub | No | `/push-to-github`, "push to github" |
| [project-pipeline](project-pipeline/SKILL.md) | **🚀 RECOMMENDED**: Full automated pipeline SPEC.md → ready tasks + branch | No | `/project-pipeline`, "start project" |
| [project-setup](project-setup/SKILL.md) | Generate project artifacts (README, PLAN, CLAUDE.md) only | No | `/project-setup`, "create artifacts" |
| [repo-cleanup](repo-cleanup/SKILL.md) | Repository hygiene audit and ephemeral file cleanup | No | `/repo-cleanup`, "clean up repo" |
| [spaarke-conventions](spaarke-conventions/SKILL.md) | Coding standards and naming conventions | **Yes** | Auto-applied |
| [task-create](task-create/SKILL.md) | Decompose plan.md into POML task files | No | `/task-create`, "create tasks" |
| [task-execute](task-execute/SKILL.md) | Execute POML task with mandatory knowledge loading | No | "execute task", "run task", "work on task" |
| [ui-test](ui-test/SKILL.md) | Browser-based UI testing for PCF/frontend using Chrome | No | `/ui-test`, "test in browser", "visual test" |
| [project-continue](project-continue/SKILL.md) | Continue project after PR merge or new session | No | `/project-continue`, "continue project", "resume project" |
| [context-handoff](context-handoff/SKILL.md) | Save working state before compaction or session end | No | `/checkpoint`, `/context-handoff`, "save progress" |
| [ribbon-edit](ribbon-edit/SKILL.md) | Edit Dataverse ribbon via solution export/import | No | "edit ribbon", "add ribbon button" |
| [worktree-setup](worktree-setup/SKILL.md) | Create and manage git worktrees for parallel development | No | `/worktree-setup`, "create worktree", "new project worktree" |
| [dev-cleanup](dev-cleanup/SKILL.md) | Clean up dev environment caches (Azure CLI, NuGet, npm, Git) | No | `/dev-cleanup`, "clean up dev", "fix auth issues", "clear caches" |
| [merge-to-master](merge-to-master/SKILL.md) | Merge completed branch work into master with safety checks | No | `/merge-to-master`, "merge to master", "check unmerged branches", "reconcile branches" |

## Skill Categories

### 📐 Standards (Always-Apply)
- **adr-aware** - Proactive ADR loading based on resource type
- **script-aware** - Script library discovery and reuse before writing new automation
- **spaarke-conventions** - Naming, patterns, file organization

### 🚀 Project Lifecycle
- **design-to-spec** - Component: Transform human design docs into AI-optimized spec.md (Tier 1)
- **project-pipeline** - **⭐ RECOMMENDED**: Full orchestrator - spec.md → ready tasks + branch (Tier 2)
- **project-continue** - Orchestrator: Resume project after PR merge or new session (Tier 2)
- **project-setup** - Component (AI-internal): Generate artifacts only (Tier 1)
- **task-create** - Component (AI-internal): Decompose plan into task files (Tier 1)
- **task-execute** - Orchestrator: Execute individual task with context loading (Tier 2)
- **repo-cleanup** - Operational: Validate structure and clean up after completion (Tier 3)

### ✅ Quality Assurance
- **code-review** - General code quality review
- **adr-check** - Architecture compliance validation (post-hoc)
- **ui-test** - Browser-based UI testing for PCF/frontend (requires Chrome)
- **repo-cleanup** - Repository structure validation and hygiene

### 🔧 Maintenance
- **ai-procedure-maintenance** - Propagate updates when adding ADRs, constraints, patterns, skills

### ☁️ Azure/Infrastructure
- **azure-deploy** - Deploy Azure infrastructure, BFF API, App Service configuration
- **bff-deploy** - Deploy BFF API to Azure App Service (focused procedure with packaging safeguards)

### ⚙️ Dataverse/Platform
- **dataverse-create-schema** - Create/update Dataverse entities, attributes, relationships via Web API
- **dataverse-deploy** - Deploy solutions, plugins, web resources via PAC CLI
- **pcf-deploy** - Build, pack, and deploy PCF controls via solution ZIP import (PCF-specific)
- **code-page-deploy** - Build and deploy React Code Page web resources (two-step: webpack + inline HTML)
- **ribbon-edit** - Automate ribbon customization via solution export/import

### 🔄 Operations
- **pull-from-github** - Pull latest changes from GitHub
- **push-to-github** - Commit changes and push to GitHub
- **ci-cd** - GitHub Actions CI/CD pipeline status, troubleshooting, and workflow management
- **worktree-setup** - Create and manage git worktrees for parallel project development
- **conflict-check** - Detect file overlap between active PRs (parallel session awareness)
- **context-handoff** - Save working state before compaction or session end for recovery
- **dev-cleanup** - Clean up local dev environment caches (Azure CLI, NuGet, npm, Git credentials)
- **merge-to-master** - Merge completed branch work into master with safety checks and build verification

## Skill Flow

```
Human Design Document (design.md, .docx, .pdf, or notes)
    │
    ▼
┌─────────────────────┐
│  design-to-spec     │  ← Tier 1 Component (Optional)
│  Transform verbose  │     Extracts requirements, adds ADR refs,
│  docs → AI-ready    │     flags ambiguities
└─────────────────────┘
    │
    ▼
AI-Optimized Spec (spec.md)
    │
    ▼
┌─────────────────────┐
│  project-pipeline   │  ← Tier 2 Orchestrator (RECOMMENDED)
│  Human-in-loop      │     Confirmations at each step
└─────────────────────┘
    │
    ├─→ Step 1: Validate spec.md
    │
    ├─→ Step 2: Resource discovery + artifact generation
    │      │
    │      └─→ CALLS ▼
    │   ┌──────────────────┐
    │   │  project-setup   │  ← Tier 1 Component
    │   │  README, PLAN,   │     Artifact generation only
    │   │  CLAUDE.md       │
    │   └──────────────────┘
    │
    ├─→ Step 3: Task decomposition
    │      │
    │      └─→ CALLS ▼
    │   ┌──────────────────┐
    │   │  task-create     │  ← Tier 1 Component
    │   │  tasks/*.poml    │     Task file generation only
    │   └──────────────────┘
    │
    ├─→ Step 4: Feature branch + commit
    │
    └─→ Step 5: Optional auto-start task 001
           │
           └─→ CALLS ▼
        ┌──────────────────┐
        │  task-execute    │  ← Tier 2 Orchestrator (per task)
        │  Load + execute  │     With full context
        └──────────────────┘
            │
            ├─→ adr-aware (Tier 0 - implicit)
            ├─→ script-aware (Tier 0 - implicit)
            ├─→ spaarke-conventions (Tier 0 - implicit)
            ├─→ Execute task steps
            ├─→ code-review (Tier 3 - quality gate)
            ├─→ adr-check (Tier 3 - validation)
            └─→ dataverse-deploy/ribbon-edit (Tier 3 - conditional)
               │
               ▼
        User executes remaining tasks (repeat task-execute)
               │
               ▼
        ┌──────────────────┐
        │  repo-cleanup    │  ← Tier 3 Operational (final step)
        │  Validate +      │     Cleanup ephemeral files
        │  cleanup         │
        └──────────────────┘
               │
               ▼
        ┌──────────────────┐
        │  merge-to-master │  ← Tier 3 Operational (branch → master)
        │  Merge branch    │     Ensures master stays current
        │  work to master  │
        └──────────────────┘

─────────────────────────────────────────────────────────────────
                    SESSION/PR CONTINUATION
─────────────────────────────────────────────────────────────────

After PR merge, new session, or context compaction:

┌───────────────────────┐
│  project-continue     │  ← Tier 2 Orchestrator (resumption)
│  Sync + context load  │     Full project context recovery
└───────────────────────┘
    │
    ├─→ Step 1: Identify project/branch
    ├─→ Step 2: Sync with master (uses pull-from-github patterns)
    ├─→ Step 3: Check PR status
    ├─→ Step 4: Load ALL project files (CLAUDE.md, plan.md, etc.)
    ├─→ Step 5: Load ADRs via adr-aware
    ├─→ Step 6: Determine resume point from current-task.md
    └─→ Step 7: Hand off to task-execute
           │
           ▼
    ┌──────────────────┐
    │  task-execute    │  ← Continues from correct step
    └──────────────────┘

─────────────────────────────────────────────────────────────────
                    CONTEXT PRESERVATION
─────────────────────────────────────────────────────────────────

Before compaction (manual or proactive):

┌───────────────────────┐
│  context-handoff      │  ← Tier 3 Operational (state save)
│  Save state for       │     Creates recovery checkpoint
│  reliable recovery    │
└───────────────────────┘
    │
    ├─→ Step 1: Identify current project
    ├─→ Step 2: Capture critical state (task, step, files, decisions)
    ├─→ Step 3: Update current-task.md with Quick Recovery section
    └─→ Step 4: Verify and report
           │
           ▼
    Ready for /compact or session end
           │
           ▼ (next session)
    ┌──────────────────┐
    │  project-continue │  ← Reads Quick Recovery section
    └──────────────────┘
```

**Skill Tiers**:
- **Tier 0**: Always-Apply (adr-aware, script-aware, spaarke-conventions)
- **Tier 1**: Components (design-to-spec, project-setup, task-create)
- **Tier 2**: Orchestrators (project-pipeline, project-continue, task-execute)
- **Tier 3**: Operational (code-review, adr-check, dataverse-deploy, context-handoff, etc.)

## ADR Awareness Flow

```
┌──────────────────────────────────────────────────────────────┐
│                   ADR COMPLIANCE LIFECYCLE                   │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  PLANNING              IMPLEMENTATION           VALIDATION   │
│  ───────              ──────────────           ──────────   │
│                                                              │
│  project-pipeline     adr-aware (proactive)   adr-check     │
│  ↓                    ↓                       ↓             │
│  Identifies ADRs      Loads ADRs before       Validates all │
│  in Step 2           writing code            ADRs in index  │
│                                                              │
│  task-create          Prevents violations     Reports        │
│  ↓                    before they happen     violations     │
│  Includes ADR refs                                          │
│  in task metadata                                           │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

## Creating New Skills

1. Copy `_templates/skill-starter/` to `.claude/skills/{skill-name}/`
2. Edit `SKILL.md` following the template structure
3. **Add YAML frontmatter with metadata** (tags, techStack, appliesTo, alwaysApply)
4. Add references, scripts, assets as needed
5. Update this INDEX.md with skill entry and tags

Template location: `_templates/SKILL-TEMPLATE.md`

### Skill Metadata (YAML Frontmatter)

Each skill MUST include YAML frontmatter for discoverability:

```yaml
---
description: Brief phrase (5-10 words) matching natural requests
tags: [tag1, tag2, tag3]  # Keywords for discovery
techStack: [tech1, tech2]  # Technologies (aspnet-core, react, azure-openai, etc.)
appliesTo: [pattern1, pattern2]  # File patterns or scenarios
alwaysApply: false  # Only true for universal skills like conventions
---
```

**Standard Tag Vocabulary:**
- **Project:** `project-init`, `project-structure`, `tasks`, `planning`
- **Development:** `api`, `pcf`, `plugin`, `frontend`, `backend`
- **Azure/AI:** `azure`, `openai`, `ai`, `embeddings`, `semantic-kernel`
- **Dataverse:** `dataverse`, `dynamics`, `power-platform`, `crm`
- **Operations:** `deploy`, `git`, `ci-cd`, `devops`
- **Quality:** `testing`, `security`, `performance`, `code-review`
- **Architecture:** `adr`, `design`, `patterns`, `conventions`

**Standard Tech Stack Values:**
- `aspnet-core`, `csharp`, `react`, `typescript`, `powershell`
- `azure-openai`, `semantic-kernel`, `azure-ai-search`
- `dataverse`, `power-platform`, `pcf-framework`
- `sharepoint`, `microsoft-graph`

## Skill File Structure

```
.claude/skills/
├── INDEX.md                    ← This file
├── _templates/                 ← Skill creation templates
│   ├── SKILL-TEMPLATE.md
│   └── skill-starter/
│       ├── SKILL.md
│       ├── scripts/
│       ├── references/
│       └── assets/
├── adr-aware/                  ← Proactive ADR loading
│   └── SKILL.md
├── ai-procedure-maintenance/   ← Maintain AI procedures when adding new elements
│   └── SKILL.md
├── adr-check/
│   ├── SKILL.md
│   └── references/
│       └── adr-validation-rules.md
├── azure-deploy/                ← Azure infrastructure and API deployment
│   └── SKILL.md
├── ci-cd/                       ← GitHub Actions CI/CD pipeline management
│   └── SKILL.md
├── code-review/
│   ├── SKILL.md
│   └── references/
│       └── review-checklist.md
├── conflict-check/              ← Detect file overlap between active PRs
│   └── SKILL.md
├── dataverse-deploy/             ← Dataverse deployment operations (plugins, web resources, solutions)
│   └── SKILL.md
├── pcf-deploy/                   ← PCF control build, pack, and deploy (PCF-specific)
│   └── SKILL.md
├── code-page-deploy/             ← Code Page web resource build and deploy (React 18 HTML)
│   └── SKILL.md
├── design-to-spec/               ← Transform design docs to AI-ready spec.md
│   └── SKILL.md
├── project-continue/             ← Resume project after PR/session
│   └── SKILL.md
├── project-pipeline/             ← RECOMMENDED: Full orchestrator
│   └── SKILL.md
├── project-setup/                ← AI-internal: Artifact generation
│   └── SKILL.md
├── repo-cleanup/               ← Repository hygiene
│   └── SKILL.md
├── ribbon-edit/                ← Dataverse ribbon customization
│   └── SKILL.md
├── script-aware/               ← Script library discovery and reuse
│   └── SKILL.md
├── spaarke-conventions/
│   ├── SKILL.md
│   └── references/
├── task-create/
│   ├── SKILL.md
│   └── references/
├── ui-test/                   ← Browser-based UI testing (Chrome integration)
│   └── SKILL.md
├── worktree-setup/             ← Git worktree management for parallel development
│   └── SKILL.md
├── context-handoff/            ← State preservation before compaction
│   └── SKILL.md
└── merge-to-master/            ← Merge branch work into master
    └── SKILL.md
```

---

*Last updated: February 24, 2026*
