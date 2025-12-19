# Skills Index

> **Purpose**: Central registry of Claude Code skills for Spaarke development.

## Available Skills

| Skill | Description | Always Apply | Trigger |
|-------|-------------|--------------|---------|
| [adr-aware](adr-aware/SKILL.md) | Proactively load ADRs when creating resources | **Yes** | Auto-applied |
| [adr-check](adr-check/SKILL.md) | Validate code against Architecture Decision Records | No | `/adr-check`, "check ADRs" |
| [code-review](code-review/SKILL.md) | Comprehensive code review (security, performance, style) | No | `/code-review`, "review code" |
| [dataverse-deploy](dataverse-deploy/SKILL.md) | Deploy solutions, PCF controls, web resources to Dataverse | No | "deploy to dataverse", "pac pcf push" |
| [design-to-spec](design-to-spec/SKILL.md) | Transform human design documents into AI-optimized spec.md | No | "design to spec", "transform spec", "create AI spec" |
| [~~design-to-project~~](design-to-project/SKILL.md) | ~~Full design spec to implementation pipeline~~ **ARCHIVED** - Use project-pipeline | No | ~~Use `/project-pipeline` instead~~ |
| [pull-from-github](pull-from-github/SKILL.md) | Pull latest changes from GitHub | No | `/pull-from-github`, "pull from github" |
| [push-to-github](push-to-github/SKILL.md) | Commit changes and push to GitHub | No | `/push-to-github`, "push to github" |
| [project-pipeline](project-pipeline/SKILL.md) | **🚀 RECOMMENDED**: Full automated pipeline SPEC.md → ready tasks + branch | No | `/project-pipeline`, "start project" |
| [project-setup](project-setup/SKILL.md) | Generate project artifacts (README, PLAN, CLAUDE.md) only | No | `/project-setup`, "create artifacts" |
| [repo-cleanup](repo-cleanup/SKILL.md) | Repository hygiene audit and ephemeral file cleanup | No | `/repo-cleanup`, "clean up repo" |
| [spaarke-conventions](spaarke-conventions/SKILL.md) | Coding standards and naming conventions | **Yes** | Auto-applied |
| [task-create](task-create/SKILL.md) | Decompose plan.md into POML task files | No | `/task-create`, "create tasks" |
| [task-execute](task-execute/SKILL.md) | Execute POML task with mandatory knowledge loading | No | "execute task", "run task", "work on task" |
| [ribbon-edit](ribbon-edit/SKILL.md) | Edit Dataverse ribbon via solution export/import | No | "edit ribbon", "add ribbon button" |

## Skill Categories

### 📐 Standards (Always-Apply)
- **adr-aware** - Proactive ADR loading based on resource type
- **spaarke-conventions** - Naming, patterns, file organization

### 🚀 Project Lifecycle
- **design-to-spec** - Component: Transform human design docs into AI-optimized spec.md (Tier 1)
- **project-pipeline** - **⭐ RECOMMENDED**: Full orchestrator - spec.md → ready tasks + branch (Tier 2)
- **project-setup** - Component: Generate artifacts only (README, PLAN, CLAUDE.md) (Tier 1)
- **task-create** - Component: Decompose plan into task files (Tier 1)
- **task-execute** - Orchestrator: Execute individual task with context loading (Tier 2)
- **repo-cleanup** - Operational: Validate structure and clean up after completion (Tier 3)
- ~~**design-to-project**~~ - **ARCHIVED** - Use project-pipeline instead

### ✅ Quality Assurance
- **code-review** - General code quality review
- **adr-check** - Architecture compliance validation (post-hoc)
- **repo-cleanup** - Repository structure validation and hygiene

### ⚙️ Dataverse/Platform
- **dataverse-deploy** - Deploy solutions, PCF controls, web resources via PAC CLI
- **ribbon-edit** - Automate ribbon customization via solution export/import

### 🔄 Operations
- **pull-from-github** - Pull latest changes from GitHub
- **push-to-github** - Commit changes and push to GitHub

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
```

**Skill Tiers**:
- **Tier 0**: Always-Apply (adr-aware, spaarke-conventions)
- **Tier 1**: Components (design-to-spec, project-setup, task-create)
- **Tier 2**: Orchestrators (project-pipeline, task-execute)
- **Tier 3**: Operational (code-review, adr-check, dataverse-deploy, etc.)

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
├── adr-check/
│   ├── SKILL.md
│   └── references/
│       └── adr-validation-rules.md
├── code-review/
│   ├── SKILL.md
│   └── references/
│       └── review-checklist.md
├── dataverse-deploy/             ← Dataverse deployment operations
│   └── SKILL.md
├── design-to-spec/               ← Transform design docs to AI-ready spec.md
│   └── SKILL.md
├── design-to-project/            ← ARCHIVED (use project-pipeline)
│   ├── SKILL.md
│   └── references/
├── project-init/
│   ├── SKILL.md
│   └── assets/
├── repo-cleanup/               ← Repository hygiene
│   └── SKILL.md
├── ribbon-edit/                ← Dataverse ribbon customization
│   └── SKILL.md
├── spaarke-conventions/
│   ├── SKILL.md
│   └── references/
└── task-create/
    ├── SKILL.md
    └── references/
```

---

*Last updated: December 19, 2025*
