# Skills Index

> **Purpose**: Central registry of Claude Code skills for Spaarke development.

## Available Skills

| Skill | Description | Always Apply | Trigger |
|-------|-------------|--------------|---------|
| [adr-aware](adr-aware/SKILL.md) | Proactively load ADRs when creating resources | **Yes** | Auto-applied |
| [adr-check](adr-check/SKILL.md) | Validate code against Architecture Decision Records | No | `/adr-check`, "check ADRs" |
| [code-review](code-review/SKILL.md) | Comprehensive code review (security, performance, style) | No | `/code-review`, "review code" |
| [dataverse-deploy](dataverse-deploy/SKILL.md) | Deploy solutions, PCF controls, web resources to Dataverse | No | "deploy to dataverse", "pac pcf push" |
| [design-to-project](design-to-project/SKILL.md) | Full design spec to implementation pipeline | No | `/design-to-project`, "implement spec" |
| [pull-from-github](pull-from-github/SKILL.md) | Pull latest changes from GitHub | No | `/pull-from-github`, "pull from github" |
| [push-to-github](push-to-github/SKILL.md) | Commit changes and push to GitHub | No | `/push-to-github`, "push to github" |
| [project-init](project-init/SKILL.md) | Initialize project folder with README, plan, tasks | No | `/project-init`, "create project" |
| [repo-cleanup](repo-cleanup/SKILL.md) | Repository hygiene audit and ephemeral file cleanup | No | `/repo-cleanup`, "clean up repo" |
| [spaarke-conventions](spaarke-conventions/SKILL.md) | Coding standards and naming conventions | **Yes** | Auto-applied |
| [task-create](task-create/SKILL.md) | Decompose plan.md into POML task files | No | `/task-create`, "create tasks" |
| [ribbon-edit](ribbon-edit/SKILL.md) | Edit Dataverse ribbon via solution export/import | No | "edit ribbon", "add ribbon button" |

## Skill Categories

### 📐 Standards (Always-Apply)
- **adr-aware** - Proactive ADR loading based on resource type
- **spaarke-conventions** - Naming, patterns, file organization

### 🚀 Project Lifecycle
- **design-to-project** - Start here for new features from design specs
- **project-init** - Create project folder structure
- **task-create** - Break plan into executable tasks
- **repo-cleanup** - Clean up after project completion

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
Design Spec
    │
    ▼
┌─────────────────────┐
│  design-to-project  │  ← Full pipeline orchestrator
└─────────────────────┘
    │ calls ▼
┌─────────────────────┐
│    project-init     │  ← Creates folder structure
└─────────────────────┘
    │ then ▼
┌─────────────────────┐
│    task-create      │  ← Decomposes into tasks
└─────────────────────┘
    │ during implementation ▼
┌─────────────────────┐
│     adr-aware       │  ← BEFORE: Load relevant ADRs (always-apply)
│ spaarke-conventions │  ← DURING: Apply coding standards (always-apply)
│     adr-check       │  ← AFTER: Validate architecture
│    code-review      │  ← AFTER: Quality review
└─────────────────────┘
    │ on completion ▼
┌─────────────────────┐
│    repo-cleanup     │  ← WRAP-UP: Validate structure, remove ephemeral files
└─────────────────────┘
```

## ADR Awareness Flow

```
┌──────────────────────────────────────────────────────────────┐
│                   ADR COMPLIANCE LIFECYCLE                   │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  PLANNING              IMPLEMENTATION           VALIDATION   │
│  ───────              ──────────────           ──────────   │
│                                                              │
│  design-to-project    adr-aware (proactive)   adr-check     │
│  ↓                    ↓                       ↓             │
│  Identifies ADRs      Loads ADRs before       Validates all │
│  in Phase 2          writing code            12 ADRs        │
│                                                              │
│  task-create          Prevents violations     Reports        │
│  ↓                    before they happen     violations     │
│  Includes ADR refs                                          │
│  in constraints                                              │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

## Creating New Skills

1. Copy `_templates/skill-starter/` to `.claude/skills/{skill-name}/`
2. Edit `SKILL.md` following the template structure
3. Add references, scripts, assets as needed
4. Update this INDEX.md

Template location: `_templates/SKILL-TEMPLATE.md`

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
├── design-to-project/
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

*Last updated: December 8, 2025*
