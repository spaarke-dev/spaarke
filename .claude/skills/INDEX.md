# Skills Index

> **Purpose**: Central registry of all Claude Code skills — the **single source of truth** for what skills exist, their triggers, and how to create new ones.
>
> **Last Updated**: June 18, 2026 (added prototype-harness-setup, prototype-harness-extend, prototype-experiment-init for the spaarke-prototype UI iteration framework)

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
| [dataverse-create-schema](dataverse-create-schema/SKILL.md) | Create/update Dataverse entities, attributes, relationships via Web API. Use MCP `describe_table` for schema discovery first. | No | "create entity", "add column", "dataverse schema" |
| [dataverse-deploy](dataverse-deploy/SKILL.md) | Deploy solutions, plugins, web resources to Dataverse. Use MCP `describe_table` for post-deployment verification. | No | "deploy to dataverse", "deploy solution" |
| [pcf-deploy](pcf-deploy/SKILL.md) | Build, pack, and deploy PCF controls via solution ZIP import | No | "deploy pcf", "build and deploy pcf", "pcf solution import" |
| [code-page-deploy](code-page-deploy/SKILL.md) | Build and deploy React Code Page web resources to Dataverse | No | "deploy code page", "deploy web resource", "build webresource" |
| [power-page-deploy](power-page-deploy/SKILL.md) | Build and deploy Vite/React SPA to Dataverse as a Power Pages web resource | No | `/power-page-deploy`, "deploy power pages", "deploy spa", "deploy external workspace" |
| [master-deploy](master-deploy/SKILL.md) | **End-to-end unified-master deploy** — all 19 web resources + BFF API from one master HEAD. Encodes today's lessons (build-script fallbacks, Reporting workaround, BFF restore bug). Use after multiple PRs merge. | No | `/master-deploy`, "master deploy", "deploy from master", "deploy everything from master", "unified deploy" |
| [design-to-spec](design-to-spec/SKILL.md) | Transform human design documents into AI-optimized spec.md | No | `/design-to-spec`, "design to spec" |
| [pull-from-github](pull-from-github/SKILL.md) | Pull latest changes from GitHub | No | `/pull-from-github`, "pull from github" |
| [push-to-github](push-to-github/SKILL.md) | Commit changes and push to GitHub | No | `/push-to-github`, "push to github" |
| [project-pipeline](project-pipeline/SKILL.md) | **🚀 RECOMMENDED**: Full automated pipeline SPEC.md → ready tasks + branch | No | `/project-pipeline`, "start project" |
| [project-setup](project-setup/SKILL.md) | Generate project artifacts (README, PLAN, CLAUDE.md) only | No | `/project-setup`, "create artifacts" |
| [repo-cleanup](repo-cleanup/SKILL.md) | Repository hygiene audit and ephemeral file cleanup | No | `/repo-cleanup`, "clean up repo" |
| [test-diet](test-diet/SKILL.md) | Project-close test reconciliation — classifies tests touched during the project as scaffolding (delete) vs maintain (keep) per ADR-038 §7 (17-ban B1-B17). Read-only: emits `git rm`/`git mv` commands for reviewer judgment. **Binding** for every project's 090 wrap-up per spec FR-B09. | No | `/test-diet`, "test diet", "project close test review", "reconcile build vs maintain tests" |
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
| [worktree-sync](worktree-sync/SKILL.md) | Guarantee worktree is fully synchronized — committed, pushed, merged, updated | No | `/worktree-sync`, "sync worktree", "full sync", "update worktree from master" |
| [deploy-new-release](deploy-new-release/SKILL.md) | Interactive production release — pre-flight, env selection, deploy, validate, tag | No | `/deploy-new-release`, "deploy release", "new release", "production release" |
| [jps-action-create](jps-action-create/SKILL.md) | Create a new JPS definition for an Analysis Action | No | "create JPS action", "new JPS definition", "new playbook action" |
| [jps-playbook-audit](jps-playbook-audit/SKILL.md) | Audit existing playbooks against current scope catalog and standards | No | "audit playbooks", "review playbooks", "check playbook compliance" |
| [jps-playbook-design](jps-playbook-design/SKILL.md) | End-to-end AI playbook: design → scope/model selection → deploy to Dataverse → verify | No | "design playbook", "create playbook", "new AI playbook" |
| [jps-scope-refresh](jps-scope-refresh/SKILL.md) | Refresh scope-model-index.json from Dataverse state | No | "refresh scope index", "update scope catalog", "sync scopes" |
| [jps-validate](jps-validate/SKILL.md) | Validate JPS JSON against schema and test rendering | No | "validate JPS", "check JPS", "test JPS definition" |
| [add-reference-to-index](add-reference-to-index/SKILL.md) | Index golden reference documents into AI Search for RAG retrieval | No | "add reference to index", "index reference document", "add golden reference" |
| [doc-drift-audit](doc-drift-audit/SKILL.md) | Detect documentation drift — stale refs in docs and .claude/ that no longer match code (compact diff-based audit) | No | `/doc-drift-audit`, "audit doc drift", "check for stale docs", "project transition audit", "find stale references" |
| [mcp-tool-handler](mcp-tool-handler/SKILL.md) | Implement or modify an MCP tool handler for the Spaarke MCP server (reads `knowledge/mcp-apps/`, `knowledge/foundry-agent-service/`) | No | "MCP tool handler", "add tool to Spaarke MCP", "IAiToolHandler" |
| [declarative-agent](declarative-agent/SKILL.md) | Author or modify a declarative agent — manifest, knowledge sources, action plugins (reads `knowledge/declarative-agents/`, `knowledge/m365-copilot/`) | No | "declarative agent", "DA manifest", "Copilot agent", "Spaarke declarative agent" |
| [foundry-agent](foundry-agent/SKILL.md) | Design Foundry Agent Service workflow or Agent Framework loop, choose runtime, wire Foundry IQ KB (reads `knowledge/foundry-agent-service/`, `knowledge/foundry-iq/`, `knowledge/agent-framework/`) | No | "Foundry agent", "Foundry workflow", "durable workflow", "HITL gate", "Foundry IQ knowledge base" |
| [dataverse-mcp-usage](dataverse-mcp-usage/SKILL.md) | Use Dataverse MCP — built-in tools, Business Skills authoring, App MCP custom tools (reads `knowledge/dataverse-mcp/`) | No | "Dataverse MCP", "Business Skill", "App MCP", "custom MCP tool for Dataverse" |
| [spe-integration](spe-integration/SKILL.md) | Integrate with SharePoint Embedded — containers, permissions, agent grounding, webUrl opens (reads `knowledge/sharepoint-embedded/`) | No | "SharePoint Embedded", "SPE container", "container type", "webUrl document open" |
| [widget-design](widget-design/SKILL.md) | Design MCP App widget — inline or side-by-side, Fluent v9, sandboxed iframe constraints (reads `knowledge/mcp-apps/`) | No | "MCP App widget", "Copilot widget", "side-by-side widget", "inline widget" |
| [fluent-v9-component](fluent-v9-component/SKILL.md) | Author/modify any Fluent UI v9 React component across Spaarke surfaces — loads `.claude/patterns/{ui,pcf}/fluent-v9-*.md` + drills into `knowledge/fluent-ui-v9/` | No | "Fluent UI", "Fluent v9", "build component", "theming", "FluentProvider", "Griffel", "makeStyles", "Popover/Tooltip/Dialog/Menu/Toast" |
| [prototype-harness-setup](prototype-harness-setup/SKILL.md) | Scaffold a Mode 2 production component harness in `spaarke-prototype` for sub-second visual iteration on a worktree component | No | `/prototype-harness-setup`, "set up prototype harness", "create UAT harness", "stand up local dev for X widget", "iterate on UI visually" |
| [prototype-harness-extend](prototype-harness-extend/SKILL.md) | Add a new Dataverse entity factory + preset to `spaarke-prototype/_infra/seed/` for harness consumption | No | `/prototype-harness-extend`, "add entity to prototype seed", "create factory for sprk_X", "extend the harness with X entity" |
| [prototype-experiment-init](prototype-experiment-init/SKILL.md) | Scaffold a Mode 1 standalone UX experiment for greenfield design work (no production code yet) | No | `/prototype-experiment-init`, "start UX experiment", "design new prototype for X", "greenfield design for Y" |
| [devops-portfolio-setup](devops-portfolio-setup/SKILL.md) | One-shot idempotent bootstrap of Project #2 portfolio schema (Type=Project, 6 fields, 7 labels, 3 issue templates). Snapshot → mutate → reconcile pattern. | No | `/devops-portfolio-setup`, "bootstrap portfolio", "setup portfolio schema" |
| [devops-epic-create](devops-epic-create/SKILL.md) | Create an Epic Issue on Project #2 with Type=Epic + label epic + populated fields | No | `/devops-epic-create`, "create epic", "new portfolio epic" |
| [devops-idea-create](devops-idea-create/SKILL.md) | Capture idea as GitHub Issue (Type=Idea, label backlog). NO local folder/worktree side-effects. | No | `/devops-idea-create`, "capture idea", "add to backlog" |
| [devops-idea-promote](devops-idea-promote/SKILL.md) | Promote Ideas → Project. Path A (1→1: flip type+labels) or Path B (N→1: package as sub-issues). | No | `/devops-idea-promote`, "promote idea", "package ideas" |
| [devops-project-start](devops-project-start/SKILL.md) | **THE BLESSED HANDOFF** — Issue → folder + worktree + design.md skeleton + field round-trip. The one canonical bridge from portfolio to local. | No | `/devops-project-start`, "start project from issue", "blessed handoff" |
| [devops-project-register](devops-project-register/SKILL.md) | Inverse of project-start — existing worktree → Project Issue + fields. Used for Phase 3 backfill. | No | `/devops-project-register`, "register project", "backfill on portfolio" |
| [devops-project-sync](devops-project-sync/SKILL.md) | Workhorse — re-read local state + idempotently update Issue fields. Called by 5 hook tasks. | No | `/devops-project-sync`, "sync portfolio", "update project fields" |
| [devops-portfolio-status](devops-portfolio-status/SKILL.md) | Portfolio dashboard (terminal); `--snapshot` writes stakeholder narrative to docs/portfolio/ | No | `/devops-portfolio-status`, "portfolio dashboard", "what's running" |
| [devops-project-archive](devops-project-archive/SKILL.md) | **DESTRUCTIVE** — set Project Status, close Issue, **DELETE worktree**, retain folder + .archived marker | No | `/devops-project-archive`, "archive project", "close project" |

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
- **doc-drift-audit** - Diff-based documentation drift detection (stale refs, broken paths)

### 🤖 AI / JPS Playbook Authoring
- **jps-action-create** - Component: Create a new JPS definition for an Analysis Action (Tier 1)
- **jps-playbook-audit** - Orchestrator: Audit existing playbooks against current scope catalog and standards (Tier 2)
- **jps-playbook-design** - Orchestrator: End-to-end playbook creation — design, scope/model selection, deploy, verify (Tier 2)
- **jps-scope-refresh** - Operational: Refresh scope-model-index.json from Dataverse (Tier 3)
- **jps-validate** - Component: Validate JPS JSON against schema and test rendering (Tier 1)
- **add-reference-to-index** - Operational: Index golden reference documents into AI Search for L1 knowledge retrieval

### 🎨 UI Prototyping (sub-second iteration via `spaarke-prototype` framework)
- **prototype-harness-setup** - Scaffold Mode 2 production harness (aliases worktree source, mocks Xrm/auth, seeded data, HMR)
- **prototype-harness-extend** - Add new entity factory + preset to shared `_infra/seed/`
- **prototype-experiment-init** - Scaffold Mode 1 standalone UX experiment (greenfield, no production source yet)

### 🔧 Maintenance
- **ai-procedure-maintenance** - Propagate updates when adding ADRs, constraints, patterns, skills

### 🧠 Knowledge Base (Microsoft platform — reads `knowledge/`)
- **mcp-tool-handler** - Implement MCP tool handler in Spaarke BFF (reads `knowledge/mcp-apps/`, `knowledge/foundry-agent-service/`)
- **declarative-agent** - Author declarative agent manifests (reads `knowledge/declarative-agents/`, `knowledge/m365-copilot/`)
- **foundry-agent** - Server-side agent runtime choice + Foundry IQ (reads `knowledge/foundry-agent-service/`, `knowledge/foundry-iq/`, `knowledge/agent-framework/`)
- **dataverse-mcp-usage** - Dataverse MCP, Business Skills, App MCP (reads `knowledge/dataverse-mcp/`)
- **spe-integration** - SharePoint Embedded ops (reads `knowledge/sharepoint-embedded/`)
- **widget-design** - MCP App widgets, inline/side-by-side, Fluent v9 (reads `knowledge/mcp-apps/`)

### ☁️ Azure/Infrastructure
- **azure-deploy** - Deploy Azure infrastructure, BFF API, App Service configuration
- **bff-deploy** - Deploy BFF API to Azure App Service (focused procedure with packaging safeguards)

### ⚙️ Dataverse/Platform
- **dataverse-create-schema** - Create/update Dataverse entities, attributes, relationships via Web API
- **dataverse-deploy** - Deploy solutions, plugins, web resources via PAC CLI
- **pcf-deploy** - Build, pack, and deploy PCF controls via solution ZIP import (PCF-specific)
- **code-page-deploy** - Build and deploy React Code Page web resources (two-step: webpack + inline HTML)
- **ribbon-edit** - Automate ribbon customization via solution export/import

### 🚢 Release
- **deploy-new-release** - Interactive production release orchestrator — pre-flight, env selection, deploy, validate, tag

### 🔄 Operations
- **pull-from-github** - Pull latest changes from GitHub
- **push-to-github** - Commit changes and push to GitHub
- **ci-cd** - GitHub Actions CI/CD pipeline status, troubleshooting, and workflow management
- **worktree-setup** - Create and manage git worktrees for parallel project development
- **conflict-check** - Detect file overlap between active PRs (parallel session awareness)
- **context-handoff** - Save working state before compaction or session end for recovery
- **dev-cleanup** - Clean up local dev environment caches (Azure CLI, NuGet, npm, Git credentials)
- **merge-to-master** - Merge completed branch work into master with safety checks and build verification
- **worktree-sync** - Guarantee worktree is fully synchronized — committed, pushed, merged to master, updated from master

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
├── merge-to-master/            ← Merge branch work into master
│   └── SKILL.md
└── worktree-sync/              ← Bidirectional worktree synchronization
    └── SKILL.md
```

---

*Last updated: May 14, 2026 (added 6 knowledge-base skills: mcp-tool-handler, declarative-agent, foundry-agent, dataverse-mcp-usage, spe-integration, widget-design — see `knowledge/` tree)*
