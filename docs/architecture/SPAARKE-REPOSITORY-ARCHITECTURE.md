# Spaarke Repository Architecture

> **Version**: 1.2
> **Date**: December 30, 2025
> **Purpose**: Comprehensive guide to the Spaarke repository structure for developers and AI coding agents

---

## Repository Overview

The Spaarke repository contains the **Spaarke Legal Operations Intelligence Platform**—an enterprise solution for legal departments integrating document management (SDAP), AI-powered document analysis, workflow automation, and operational intelligence built on Microsoft Dataverse and SharePoint Embedded (SPE).

### Platform Components

| Component | Description | Status |
|-----------|-------------|--------|
| **SDAP** (SharePoint Document Access Platform) | Document storage, retrieval, and management via SPE | Production |
| **Email-to-Document Automation** | Automatic email archival to .eml documents with webhooks | Phase 2 Complete |
| **AI Document Intelligence** | AI-powered summarization, metadata extraction, and analysis | In Development |
| **Legal Workflow Automation** | Matter management, deadline tracking, task automation | Planned |
| **Operational Analytics** | Dashboards, reporting, and insights | Planned |

### High-Level Architecture Diagram

```
spaarke/
├─────────────────────────────────────────────────────────────────────────────────┐
│  RUNTIME CODE                                                                    │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────────────────────┐  │
│  │  src/client/    │  │  src/server/    │  │  src/dataverse/                 │  │
│  │  ├─ pcf/        │  │  └─ api/        │  │  ├─ plugins/                    │  │
│  │  ├─ office-     │  │     └─ Sprk.    │  │  ├─ solutions/                  │  │
│  │  │   addins/    │  │        Bff.Api/ │  │  └─ webresources/               │  │
│  │  └─ webres/     │  │                 │  │                                 │  │
│  └─────────────────┘  └─────────────────┘  └─────────────────────────────────┘  │
├─────────────────────────────────────────────────────────────────────────────────┤
│  INFRASTRUCTURE & CONFIG                                                         │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────────────────────┐  │
│  │  infrastructure/│  │  config/        │  │  .github/workflows/             │  │
│  │  └─ bicep/      │  │  (local secrets)│  │  (CI/CD pipelines)              │  │
│  └─────────────────┘  └─────────────────┘  └─────────────────────────────────┘  │
├─────────────────────────────────────────────────────────────────────────────────┤
│  DOCUMENTATION                                                                   │
│  ┌─────────────────┐  ┌─────────────────────────────────────────────────────┐   │
│  │  docs/          │  │  ADRs, guides, standards, AI knowledge base         │   │
│  │  ai-knowledge/  │  │  Architecture decisions and procedures              │   │
│  └─────────────────┘  └─────────────────────────────────────────────────────┘   │
├─────────────────────────────────────────────────────────────────────────────────┤
│  TESTING & QUALITY                                                               │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────────────────────┐  │
│  │  tests/unit/    │  │  tests/         │  │  tests/e2e/                     │  │
│  │                 │  │  integration/   │  │  tests/manual/                  │  │
│  └─────────────────┘  └─────────────────┘  └─────────────────────────────────┘  │
├─────────────────────────────────────────────────────────────────────────────────┤
│  ACTIVE WORK                                                                     │
│  ┌───────────────────────────────────────────────────────────────────────────┐  │
│  │  projects/{project-name}/  — Active development with spec, plan, tasks   │  │
│  └───────────────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────────┘
```

---

## Main Directory Structure

### Root Level

| Directory/File | Purpose | AI Agent Notes |
|----------------|---------|----------------|
| `src/` | **All runtime source code** | Primary coding location |
| `tests/` | **All test code** | Mirror structure of `src/` |
| `docs/` | **Documentation** | `ai-knowledge/` for coding, `reference/` for deep dives |
| `infrastructure/` | **Azure Bicep IaC** | Deployment templates |
| `projects/` | **Active development work** | Current feature/task work |
| `scripts/` | **PowerShell utilities** | Deployment and maintenance scripts |
| `config/` | **Local configuration** | `.local.json` files (gitignored secrets) |
| `.github/` | **CI/CD workflows** | GitHub Actions pipelines |
| `CLAUDE.md` | **AI agent instructions** | Read first before any work |
| `Spaarke.sln` | **.NET solution file** | All .NET projects referenced here |

---

## Source Code (`src/`)

### `src/client/` — Frontend Components

| Subdirectory | Technology | Purpose |
|--------------|------------|---------|
| `pcf/` | TypeScript, React, Fluent UI | PCF controls for Dataverse model-driven apps |
| `office-addins/` | TypeScript | Office Add-ins (Word, Excel, Outlook) |
| `webresources/` | JavaScript/HTML | Dataverse web resources |
| `shared/` | TypeScript | Shared UI component library |
| `assets/` | Static files | Icons, images, stylesheets |

**PCF Controls (`src/client/pcf/`):**

| Control | Purpose |
|---------|---------|
| `AIMetadataExtractor/` | AI-powered document metadata extraction |
| `SpeFileViewer/` | Document preview and Office Online editing |
| `ThemeEnforcer/` | Dark mode theme enforcement |
| `UniversalDatasetGrid/` | Generic dataset grid control |
| `UniversalQuickCreate/` | Document upload with metadata |

### `src/server/` — Backend Services

| Subdirectory | Technology | Purpose |
|--------------|------------|---------|
| `api/Sprk.Bff.Api/` | .NET 8, Minimal API | Backend-for-Frontend, Graph/Dataverse integration |
| `shared/` | .NET 8 | Shared .NET libraries |

**BFF API Structure (`src/server/api/Sprk.Bff.Api/`):**

```
Sprk.Bff.Api/
├── Api/                    # Endpoint definitions (Minimal API groups)
│   ├── DocumentsEndpoints.cs    # Document CRUD operations
│   ├── EmailEndpoints.cs        # Email-to-document automation
│   └── ...
├── Configuration/          # Strongly-typed configuration classes
├── Infrastructure/         # Cross-cutting concerns
│   ├── Auth/               # Authentication middleware
│   ├── Authorization/      # Policy-based authorization
│   ├── Dataverse/          # Dataverse Web API client
│   ├── DI/                 # Dependency injection modules
│   ├── Errors/             # ProblemDetails helpers
│   ├── Graph/              # Microsoft Graph operations
│   ├── Resilience/         # Polly retry policies
│   └── Validation/         # Request validation
├── Models/                 # DTOs and request/response models
├── Services/               # Business logic services
│   ├── Email/              # Email-to-document services
│   │   ├── IEmailToEmlConverter.cs
│   │   ├── IEmailFilterService.cs
│   │   └── EmailRuleSeedService.cs
│   └── Jobs/               # Async job processing
│       ├── Handlers/       # Job type handlers
│       │   └── EmailToDocumentJobHandler.cs
│       └── EmailPollingBackupService.cs
├── Telemetry/              # OpenTelemetry metrics
│   ├── AiTelemetry.cs
│   └── EmailTelemetry.cs   # Email processing metrics
└── Program.cs              # Application entry point
```

### `src/dataverse/` — Dataverse Platform Components

| Subdirectory | Technology | Purpose |
|--------------|------------|---------|
| `plugins/` | C# | Dataverse plugins (thin validation/projection) |
| `solutions/` | Dataverse solution | Solution XML, entity definitions |
| `webresources/` | JavaScript | Dataverse form scripts |

---

## Tests (`tests/`)

| Subdirectory | Purpose | When to Update |
|--------------|---------|----------------|
| `unit/` | Unit tests (isolated, fast) | Any code change |
| `integration/` | Integration tests (real services) | API/service changes |
| `e2e/` | End-to-end tests (full stack) | User flow changes |
| `manual/` | Manual test scripts | Complex scenarios |
| `Spaarke.ArchTests/` | Architecture fitness tests | Structure changes |
| `code review/` | Code review analysis documents | After reviews |

**Test Naming Convention:**
- Unit tests: `{ProjectName}.Tests/` mirrors `src/` structure
- Example: `tests/unit/Sprk.Bff.Api.Tests/` tests `src/server/api/Sprk.Bff.Api/`

---

## Documentation (`docs/`)

### Documentation Hierarchy

```
docs/
├── CLAUDE.md                     # Traffic controller for AI agents
├── ai-knowledge/                 # ✅ REFERENCE FREELY for coding
│   ├── architecture/             # System patterns, boundaries
│   ├── guides/                   # How-to procedures
│   ├── standards/                # Coding standards, auth patterns
│   └── templates/                # Project/task scaffolding
├── reference/                    # ⚠️ ASK BEFORE LOADING
│   ├── adr/                      # Architecture Decision Records
│   ├── architecture/             # Deep architecture docs
│   ├── articles/                 # Full guides (verbose)
│   ├── procedures/               # Process documentation
│   ├── protocols/                # AI behavior protocols
│   └── research/                 # KM-* knowledge articles
└── product-documentation/        # End-user documentation
```

**AI Agent Rule:** Always read `docs/ai-knowledge/` first. Only load `docs/reference/` when explicitly directed or debugging requires historical context.

---

## Infrastructure (`infrastructure/`)

| Subdirectory | Purpose |
|--------------|---------|
| `bicep/modules/` | Reusable Bicep modules (Key Vault, Service Bus, etc.) |
| `bicep/stacks/` | Environment-specific deployments (Model 1, Model 2) |
| `bicep/parameters/` | Parameter files for each environment |
| `dataverse/` | Dataverse deployment scripts |
| `scripts/` | Infrastructure automation scripts |

---

## Projects (`projects/`)

Active development work lives here. Each project follows this structure:

```
projects/{project-name}/
├── spec.md                 # Design specification (input from product)
├── README.md               # Project overview (AI-generated)
├── plan.md                 # Implementation plan (AI-generated)
├── CLAUDE.md               # Project-specific AI context
├── tasks/                  # Task files (POML format)
│   ├── TASK-INDEX.md       # Task status tracking
│   └── task-*.md           # Individual task definitions
└── notes/                  # Ephemeral working files
    └── handoffs/           # Context handoff files
```

**AI Agent Rule:** When starting work on a project, read the project's `CLAUDE.md` and `plan.md` first.

---

## AI Agent Repository Traversal Logic

### Starting a New Task

```
1. Read root /CLAUDE.md
2. Read /docs/CLAUDE.md (traffic controller)
3. Read /docs/ai-knowledge/CLAUDE.md (index)
4. If project work:
   └── Read /projects/{project}/CLAUDE.md
   └── Read /projects/{project}/plan.md
5. Load relevant /docs/ai-knowledge/{topic}/ documents
6. Navigate to code location based on task type
```

### Task Type → Directory Mapping

| Task Type | Primary Directory | Secondary |
|-----------|-------------------|-----------|
| PCF control work | `src/client/pcf/{control}/` | `tests/unit/` |
| BFF API work | `src/server/api/Sprk.Bff.Api/` | `tests/unit/Sprk.Bff.Api.Tests/` |
| Dataverse plugin | `src/dataverse/plugins/` | `tests/unit/` |
| Infrastructure | `infrastructure/bicep/` | `docs/reference/architecture/` |
| Documentation | `docs/ai-knowledge/` | `docs/reference/` |

### Finding Related Files

| If Editing... | Also Check... |
|---------------|---------------|
| `src/server/api/Sprk.Bff.Api/Api/*.cs` | `tests/unit/Sprk.Bff.Api.Tests/` |
| `src/client/pcf/{control}/` | `src/client/pcf/CLAUDE.md` |
| `infrastructure/bicep/modules/*.bicep` | `infrastructure/bicep/stacks/*.bicep` |
| Any `appsettings.json` | `config/*.local.json` (local overrides) |

---

## Creating New Directories

### When to Create New Directories

| Scenario | Location | Naming Convention |
|----------|----------|-------------------|
| New PCF control | `src/client/pcf/{ControlName}/` | PascalCase, descriptive |
| New BFF endpoint group | `src/server/api/Sprk.Bff.Api/Api/` | File only, not directory |
| New shared library | `src/server/shared/{LibraryName}/` | PascalCase with `.` separator |
| New infrastructure module | `infrastructure/bicep/modules/` | kebab-case |
| New project | `projects/{project-name}/` | kebab-case |
| New AI knowledge topic | `docs/ai-knowledge/{topic}/` | kebab-case |

### Directory Creation Checklist

1. **Does it belong in existing structure?** Check if similar content exists
2. **Follow naming convention** for that area (see table above)
3. **Add CLAUDE.md** if directory contains code AI will work with
4. **Update parent CLAUDE.md** to reference new directory
5. **Add to .gitignore** if contains local/generated content

### New Code Project Procedure

When creating a new .NET project:

```powershell
# 1. Create project in appropriate location
dotnet new classlib -n Sprk.NewLibrary -o src/server/shared/Sprk.NewLibrary

# 2. Add to solution
dotnet sln Spaarke.sln add src/server/shared/Sprk.NewLibrary/Sprk.NewLibrary.csproj

# 3. Create corresponding test project
dotnet new xunit -n Sprk.NewLibrary.Tests -o tests/unit/Sprk.NewLibrary.Tests
dotnet sln Spaarke.sln add tests/unit/Sprk.NewLibrary.Tests/Sprk.NewLibrary.Tests.csproj

# 4. Add CLAUDE.md to new project directory
```

### New PCF Control Procedure

```powershell
# 1. Initialize control
cd src/client/pcf
pac pcf init --namespace Sprk --name NewControl --template dataset

# 2. Add to pcfconfig.json
# 3. Add CLAUDE.md with control-specific context
```

---

## Maintenance Procedures

### Pull Request Requirements

| Requirement | Description |
|-------------|-------------|
| **Branch naming** | `feature/{ticket}-{description}`, `fix/{ticket}-{description}` |
| **PR template** | Use `.github/pull_request_template.md` |
| **Tests required** | All code changes require corresponding test updates |
| **CLAUDE.md updates** | Update if adding new directories or changing patterns |

### Code Review Checklist (AI Agents)

Before submitting code:
1. ✅ Run `dotnet build` — no errors
2. ✅ Run `dotnet test` — tests pass
3. ✅ Check ADR compliance (see `docs/reference/adr/`)
4. ✅ Update CLAUDE.md if structure changed
5. ✅ Update TASK-INDEX.md if completing project task

### CI/CD Pipelines

| Workflow | Trigger | Purpose |
|----------|---------|---------|
| `sdap-ci.yml` | PR to main | Build, test, lint |
| `deploy-staging.yml` | Push to main | Deploy to staging |
| `deploy-to-azure.yml` | Manual | Production deployment |
| `adr-audit.yml` | PR with ADR changes | Validate ADR format |

---

## Key Configuration Files

| File | Purpose | Gitignored? |
|------|---------|-------------|
| `Spaarke.sln` | .NET solution | No |
| `Directory.Packages.props` | Central package management | No |
| `Directory.Build.props` | Shared build properties | No |
| `config/*.local.json` | Local secrets/config | Yes |
| `.env` files | Environment variables | Yes |
| `appsettings.Development.json` | Dev-specific config | No |

---

## Quick Reference: File Location by Type

| Looking For... | Location |
|----------------|----------|
| PCF control source | `src/client/pcf/{ControlName}/` |
| BFF API endpoints | `src/server/api/Sprk.Bff.Api/Api/` |
| BFF infrastructure | `src/server/api/Sprk.Bff.Api/Infrastructure/` |
| Dataverse plugins | `src/dataverse/plugins/` |
| Unit tests | `tests/unit/{ProjectName}.Tests/` |
| ADRs | `docs/reference/adr/` |
| AI coding guides | `docs/ai-knowledge/` |
| Bicep modules | `infrastructure/bicep/modules/` |
| PowerShell scripts | `scripts/` |
| GitHub workflows | `.github/workflows/` |
| Project work | `projects/{project-name}/` |

---

## See Also

- `/CLAUDE.md` — Repository-wide AI instructions
- `/docs/ai-knowledge/CLAUDE.md` — AI knowledge base index
- `/docs/reference/adr/` — Architecture Decision Records
- `/docs/reference/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` — Naming standards
