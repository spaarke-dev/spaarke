# Repository Navigation Guide

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified
> **Purpose**: Quick navigation reference for developers and AI coding agents. For deep architecture, see `docs/architecture/`.

---

## Overview

**Spaarke** is an enterprise platform for legal and corporate operations built on Microsoft Dataverse and SharePoint Embedded (SPE). It combines document management (SDAP), AI-powered analysis, workflow automation, and operational intelligence.

- **AI instructions**: See root `CLAUDE.md`
- **Architectural overview**: See `docs/architecture/sdap-overview.md`
- **Architecture index**: See `docs/architecture/INDEX.md`

---

## Top-Level Directory Map

| Directory | Purpose | See Also |
|-----------|---------|----------|
| `src/` | All runtime source code | `docs/architecture/sdap-overview.md` |
| `tests/` | Unit, integration, e2e, manual tests | `docs/procedures/` |
| `docs/` | Documentation (architecture, guides, ADRs, standards) | This guide |
| `infrastructure/` | Azure Bicep IaC | `infrastructure/bicep/` |
| `projects/` | Active development work (spec, plan, tasks) | Root `CLAUDE.md` |
| `scripts/` | PowerShell deployment/utility scripts | — |
| `config/` | Local configuration (`.local.json` gitignored) | — |
| `.github/` | CI/CD workflows | `docs/architecture/ci-cd-architecture.md` |
| `.claude/` | AI agent context (skills, patterns, ADRs, constraints) | `.claude/skills/INDEX.md` |
| `Spaarke.sln` | .NET solution file | — |

---

## Source Code Layout (`src/`)

### Frontend (`src/client/`)

| Subdirectory | Tech | See Also |
|--------------|------|----------|
| `pcf/` | TypeScript, React 16/17, Fluent UI v9 | `docs/architecture/sdap-pcf-patterns.md` |
| `code-pages/` | TypeScript, React 18, Fluent UI v9 | `docs/architecture/code-pages-architecture.md` |
| `office-addins/` | TypeScript, React 18 | `docs/architecture/office-outlook-teams-integration-architecture.md` |
| `shared/` | TypeScript — `@spaarke/ui-components`, `@spaarke/auth` | `docs/guides/SHARED-UI-COMPONENTS-GUIDE.md` |
| `webresources/` | Legacy JS web resources | (ADR-006: no new legacy webresources) |

**PCF Controls** (field-bound form controls — React 16/17 platform libs): 14 controls including `AIMetadataExtractor`, `AssociationResolver`, `DocumentRelationshipViewer`, `DrillThroughWorkspace`, `EmailProcessingMonitor`, `RelatedDocumentCount`, `ScopeConfigEditor`, `SemanticSearchControl`, `SpaarkeGridCustomizer`, `ThemeEnforcer`, `UniversalDatasetGrid`, `UniversalQuickCreate`, `UpdateRelatedButton`, `VisualHost`.

Run `ls src/client/pcf/` for the authoritative list.

**Code Pages** (standalone React 18 SPAs): `AnalysisWorkspace`, `DocumentRelationshipViewer`, `PlaybookBuilder`, `SemanticSearch`.

### Backend (`src/server/`)

| Subdirectory | Tech | See Also |
|--------------|------|----------|
| `api/Sprk.Bff.Api/` | .NET 8 Minimal API | `docs/architecture/sdap-bff-api-patterns.md` |
| `shared/` | .NET 8 shared libraries | `docs/architecture/INDEX.md` |

BFF API entry point: `src/server/api/Sprk.Bff.Api/Program.cs` (all endpoint registration, DI, middleware).

### Solutions (`src/solutions/`)

Dataverse solution projects (each may include React 18 SPAs bundled as web resources). Current solutions include: `AllDocuments`, `CalendarSidePane`, `CopilotAgent`, `CreateEventWizard`, `CreateMatterWizard`, `CreateProjectWizard`, `CreateTodoWizard`, `CreateWorkAssignmentWizard`, `DailyBriefing`, `DocumentUploadWizard`, `EventsPage`, `FindSimilarCodePage`, `LegalWorkspace`, `PlaybookLibrary`, `Reporting`, `SmartTodo`, `SpaarkeCore`, `SpeAdminApp`, `SummarizeFilesWizard`, `WorkspaceLayoutWizard`, plus side panes and supporting wizards.

Run `ls src/solutions/` for the authoritative list.

---

## Documentation Layout (`docs/`)

| Subdirectory | Purpose | Index |
|--------------|---------|-------|
| `adr/` | Full Architecture Decision Records (history + rationale) | `docs/adr/` |
| `architecture/` | Architecture decisions and patterns (decisions-only) | `docs/architecture/INDEX.md` |
| `guides/` | Operational and how-to guides (deploy, configure, troubleshoot) | `docs/guides/INDEX.md` |
| `standards/` | Coding and authentication standards | — |
| `data-model/` | Dataverse entity schemas | — |
| `procedures/` | Process documentation (testing, code quality) | — |
| `enhancements/` | Enhancement proposals | — |
| `product-documentation/` | User-facing docs | — |

See `docs/CLAUDE.md` for the documentation traffic-controller guide (what to load when).

---

## Tests (`tests/`)

| Subdirectory | Purpose |
|--------------|---------|
| `unit/` | Fast isolated tests; mirrors `src/` structure (e.g., `tests/unit/Sprk.Bff.Api.Tests/`) |
| `integration/` | Tests against real services |
| `e2e/` | End-to-end user flows |
| `manual/` | Manual test scripts |
| `Spaarke.ArchTests/` | Architecture fitness tests |

---

## Naming Conventions

| Scenario | Location | Naming |
|----------|----------|--------|
| New PCF control (field-bound) | `src/client/pcf/{ControlName}/` | PascalCase |
| New Code Page (standalone dialog) | `src/solutions/{PageName}/` or `src/client/code-pages/{PageName}/` | PascalCase |
| New BFF endpoint group | `src/server/api/Sprk.Bff.Api/Api/{Feature}Endpoints.cs` | PascalCase file |
| New BFF service | `src/server/api/Sprk.Bff.Api/Services/{Area}/` | PascalCase folder |
| New shared .NET library | `src/server/shared/Sprk.{LibraryName}/` | `Sprk.`-prefixed PascalCase |
| New Dataverse solution | `src/solutions/{SolutionName}/` | PascalCase |
| New Bicep module | `infrastructure/bicep/modules/` | kebab-case |
| New project | `projects/{project-name}/` | kebab-case |
| New ADR | `docs/adr/ADR-NNN-{slug}.md` | Numbered, kebab-case slug |

**Decision rule (ADR-006)**:
- Field-bound on a Dataverse form → **PCF control** (React 16/17, platform libraries)
- Standalone dialog or full-page experience → **React Code Page** (React 18, bundled)
- No new legacy JS web resources

---

## New Component Procedures

### New .NET Project

```powershell
# 1. Create project
dotnet new classlib -n Sprk.NewLibrary -o src/server/shared/Sprk.NewLibrary

# 2. Add to solution
dotnet sln Spaarke.sln add src/server/shared/Sprk.NewLibrary/Sprk.NewLibrary.csproj

# 3. Create test project
dotnet new xunit -n Sprk.NewLibrary.Tests -o tests/unit/Sprk.NewLibrary.Tests
dotnet sln Spaarke.sln add tests/unit/Sprk.NewLibrary.Tests/Sprk.NewLibrary.Tests.csproj
```

### New PCF Control

```powershell
cd src/client/pcf
pac pcf init --namespace Sprk --name NewControl --template dataset
# Add control to solution and update pcfconfig.json as needed
```

See `.claude/skills/dataverse-deploy/SKILL.md` for deployment.

### New Project (AI-assisted)

```bash
# 1. Place spec.md in projects/{name}/
# 2. Run full pipeline
/project-pipeline projects/{name}
```

See root `CLAUDE.md` → "Project Initialization: Developer Workflow".

---

## Quick Reference: Where to Find X

| Looking For | Location |
|-------------|----------|
| PCF control source | `src/client/pcf/{ControlName}/` |
| Code Page SPA | `src/solutions/{PageName}/` or `src/client/code-pages/{PageName}/` |
| BFF API endpoint groups | `src/server/api/Sprk.Bff.Api/Api/` |
| BFF infrastructure (auth, DI, Graph, Dataverse) | `src/server/api/Sprk.Bff.Api/Infrastructure/` |
| BFF business services | `src/server/api/Sprk.Bff.Api/Services/` |
| Shared .NET libraries | `src/server/shared/` |
| Shared UI components | `src/client/shared/Spaarke.UI.Components/` |
| Unit tests | `tests/unit/{ProjectName}.Tests/` |
| ADRs (concise, for AI) | `.claude/adr/` |
| ADRs (full history) | `docs/adr/` |
| Architecture docs | `docs/architecture/` |
| Operational guides | `docs/guides/` |
| Bicep modules | `infrastructure/bicep/modules/` |
| Deployment scripts | `scripts/` |
| GitHub workflows | `.github/workflows/` |
| Active project work | `projects/{project-name}/` |
| AI skills and patterns | `.claude/skills/`, `.claude/patterns/` |

---

## See Also

- Root `/CLAUDE.md` — Repository-wide AI instructions and architecture discovery
- `docs/CLAUDE.md` — Documentation traffic controller (what to load when)
- `docs/architecture/INDEX.md` — Architecture decisions index
- `docs/guides/INDEX.md` — Operational guides index
- `docs/adr/` — Full Architecture Decision Records
- `.claude/skills/INDEX.md` — AI skill registry
