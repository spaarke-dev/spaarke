# CLAUDE.md - Spaarke Repository Instructions

> **Last Updated**: December 4, 2025
>
> **Purpose**: This file provides repository-wide context and instructions for Claude Code when working in this codebase.

---

## 🚨 AI Execution Rules (Critical)

### Context Management

| Usage | Action |
|-------|--------|
| < 70% | ✅ Proceed normally |
| > 70% | 🛑 STOP - Create handoff at `notes/handoffs/`, request new session |
| > 85% | 🚨 EMERGENCY - Immediately create handoff |

**Commands**: `/context` (check) · `/clear` (wipe) · `/compact` (compress)

### Human Escalation Triggers

**MUST request human input for**:
- Ambiguous or conflicting requirements
- Security-sensitive code (auth, secrets, encryption)
- ADR conflicts or violations
- Breaking changes (API contracts, DB schema)
- Scope expansion beyond task boundaries

**Format**: Use 🔔 **Human Input Required** block with situation, options, recommendation.

### Task Completion

After completing any task:
1. Update `TASK-INDEX.md` status: 🔲 → ✅
2. Document deviations in `notes/`
3. Report completion to user

**Full protocols**: `docs/reference/protocols/` (AIP-001, AIP-002, AIP-003)

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
│   ├── api/                   # .NET 8 Minimal API (Spe.Bff.Api)
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
dotnet build src/server/api/Spe.Bff.Api/

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
dotnet test tests/unit/Spe.Bff.Api.Tests/
```

### Running Locally

```bash
# Start API
dotnet run --project src/server/api/Spe.Bff.Api/

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
| 5. **Tasks** | Granular work items | `tasks.md` in project folder |
| 6. **Product Feature Documentation** | User-facing docs, admin guides | Docs in `docs/guides/` |
| 7. **Complete Project** | Code complete, tested, deployed | Working feature |

### 🤖 AI-Assisted Development

**When given a design spec, follow the AI Agent Playbook:**
- **Location**: `docs/templates/AI-AGENT-PLAYBOOK.md`
- **Purpose**: Step-by-step instructions to process design specs into project artifacts

The playbook guides you through:
1. **Ingest** - Extract key info from the design spec
2. **Context** - Gather ADRs, existing code, knowledge base
3. **Generate** - Create README, plan, tasks using templates
4. **Validate** - Cross-reference checklist before coding
5. **Implement** - Follow patterns and update tasks

### Before Starting Work

1. **Identify the phase** - What lifecycle phase is this work in?
2. **Check for existing artifacts** - Look for design specs, assessments
3. **Follow the spec** - If a design spec exists, implement accordingly
4. **Use the playbook** - If starting from a design spec, follow `docs/templates/AI-AGENT-PLAYBOOK.md`

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
