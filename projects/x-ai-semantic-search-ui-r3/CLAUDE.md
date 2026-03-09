# CLAUDE.md - AI Semantic Search UI R3

> **Project**: AI Semantic Search UI R3
> **Last Updated**: 2026-02-24
> **Purpose**: Project-specific context for Claude Code

---

## Project Overview

Build a full-page **React 19 Code Page** (Dataverse HTML web resource) for system-wide, multi-entity semantic search across Documents, Matters, Projects, and Invoices. Includes graph clustering visualization, Universal DatasetGrid, saved search favorites, and new BFF API endpoints.

**Key Spec Reference**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

---

## Critical Constraints

### ADR Compliance (MANDATORY)

| ADR | Requirement | Enforcement |
|-----|-------------|-------------|
| **ADR-006** | Code Page (web resource), not custom page | Single self-contained HTML file |
| **ADR-021** | Fluent UI v9 exclusively | All UI from `@fluentui/react-components`; tokens for colors |
| **ADR-022** | React 19 for Code Pages (bundled) | `createRoot()` from `react-dom/client` |
| **ADR-026** | Full-page code page standard | Single HTML output, theme detection, Xrm frame-walk |
| **ADR-012** | Shared component library | Import `UniversalDatasetGrid`, `ViewSelector` from `@spaarke/ui-components` |
| **ADR-001** | Minimal API for endpoints | No Azure Functions |
| **ADR-008** | Endpoint filters for auth | `SemanticSearchAuthorizationFilter` |
| **ADR-009** | Redis-first caching | `IDistributedCache` for search results |
| **ADR-010** | DI minimalism | ≤15 non-framework registrations |
| **ADR-013** | AI architecture | Extend BFF, not separate service; no direct AI calls from frontend |
| **ADR-014** | AI caching policy | Tenant-scoped cache keys, versioned |
| **ADR-016** | Rate limiting | `ai-batch` policy on search endpoints |
| **ADR-019** | ProblemDetails errors | RFC 7807 for all HTTP failures |

### React 19 Code Page (CRITICAL)

```typescript
// CORRECT - React 19 Code Page pattern
import { createRoot } from "react-dom/client";
import { FluentProvider, webLightTheme, webDarkTheme } from "@fluentui/react-components";

const params = new URLSearchParams(window.location.search);
createRoot(document.getElementById("root")!).render(
    <FluentProvider theme={isDark ? webDarkTheme : webLightTheme}>
        <App {...appProps} />
    </FluentProvider>
);
```

**This is a Code Page, NOT a PCF control. React 19 is bundled, not platform-provided.**

### Fluent UI v9 Exclusively

```typescript
// CORRECT - Fluent v9
import { Button, Input, Dropdown, TabList, Tab } from "@fluentui/react-components";
import { makeStyles, tokens } from "@fluentui/react-components";
import { DocumentRegular, SearchRegular } from "@fluentui/react-icons";

// PROHIBITED - Fluent v8
import { Button } from "@fluentui/react";  // DON'T USE
```

---

## MUST Rules

- **MUST** use React 19 with `createRoot()` (Code Page, bundled)
- **MUST** use `@fluentui/react-components` (Fluent v9) exclusively
- **MUST** wrap all UI in `FluentProvider` with theme
- **MUST** use Fluent design tokens for all colors, spacing, typography
- **MUST** support light, dark, and high-contrast modes
- **MUST** use `makeStyles` (Griffel) for custom styling
- **MUST** import icons from `@fluentui/react-icons`
- **MUST** deploy as single self-contained HTML (webpack → build-webresource.ps1)
- **MUST** read URL parameters from `URLSearchParams`
- **MUST** use endpoint filters for API authorization
- **MUST** return ProblemDetails for API errors
- **MUST** import shared components from `@spaarke/ui-components`
- **MUST** use `Xrm.Navigation.navigateTo` for record/entity navigation
- **MUST** use `sessionStorage` for MSAL token cache

## MUST NOT Rules

- **MUST NOT** use Fluent v8 (`@fluentui/react`)
- **MUST NOT** hard-code colors (hex, rgb, named)
- **MUST NOT** import from granular `@fluentui/react-*` packages
- **MUST NOT** call Azure AI services directly from the code page
- **MUST NOT** expose API keys to the client
- **MUST NOT** create a separate AI microservice
- **MUST NOT** use global auth middleware
- **MUST NOT** use `localStorage` for tokens (use `sessionStorage`)
- **MUST NOT** log document content, prompts, or model responses

---

## Applicable ADRs (Full Reference)

| ADR | File | Key Constraint |
|-----|------|----------------|
| ADR-001 | `.claude/adr/ADR-001-minimal-api.md` | Minimal API pattern for all endpoints |
| ADR-006 | `.claude/adr/ADR-006-pcf-over-webresources.md` | Code Page for standalone dialogs |
| ADR-008 | `.claude/adr/ADR-008-endpoint-filters.md` | Endpoint filters, not middleware |
| ADR-009 | `.claude/adr/ADR-009-redis-caching.md` | Redis-first, versioned cache keys |
| ADR-010 | `.claude/adr/ADR-010-di-minimalism.md` | ≤15 DI registrations |
| ADR-011 | `.claude/adr/ADR-011-dataset-pcf.md` | Code Page for standalone grids |
| ADR-012 | `.claude/adr/ADR-012-shared-components.md` | @spaarke/ui-components |
| ADR-013 | `.claude/adr/ADR-013-ai-architecture.md` | Extend BFF for AI |
| ADR-014 | `.claude/adr/ADR-014-ai-caching.md` | Tenant-scoped AI cache keys |
| ADR-016 | `.claude/adr/ADR-016-ai-rate-limits.md` | Rate limiting on AI endpoints |
| ADR-019 | `.claude/adr/ADR-019-problemdetails.md` | ProblemDetails for errors |
| ADR-021 | `.claude/adr/ADR-021-fluent-design-system.md` | Fluent v9 exclusively |
| ADR-022 | `.claude/adr/ADR-022-pcf-platform-libraries.md` | React 19 for Code Pages |
| ADR-026 | `.claude/adr/ADR-026-full-page-custom-page-standard.md` | Single HTML output |

---

## Reference Implementations

| Pattern | Reference Location |
|---------|-------------------|
| Code Page entry point | `src/client/code-pages/DocumentRelationshipViewer/src/index.tsx` |
| Code Page webpack config | `src/client/code-pages/DocumentRelationshipViewer/webpack.config.js` |
| Code Page build script | `src/client/code-pages/DocumentRelationshipViewer/build-webresource.ps1` |
| MSAL auth (Code Page) | `src/client/code-pages/DocumentRelationshipViewer/src/services/auth/MsalAuthProvider.ts` |
| Graph visualization (@xyflow/react v12) | `src/client/code-pages/DocumentRelationshipViewer/src/components/DocumentGraph.tsx` |
| d3-force layout hook | `src/client/code-pages/DocumentRelationshipViewer/src/hooks/useForceLayout.ts` |
| Theme detection (4-level) | `src/solutions/EventsPage/src/providers/ThemeProvider.ts` |
| Semantic search API service | `src/client/pcf/SemanticSearchControl/SemanticSearchControl/services/SemanticSearchApiService.ts` |
| Filter components (adapt R16→R19) | `src/client/pcf/SemanticSearchControl/SemanticSearchControl/components/FilterDropdown.tsx` |
| Date range filter (adapt R16→R19) | `src/client/pcf/SemanticSearchControl/SemanticSearchControl/components/DateRangeFilter.tsx` |
| Search hooks (adapt) | `src/client/pcf/SemanticSearchControl/SemanticSearchControl/hooks/useSemanticSearch.ts` |
| Universal DatasetGrid | `src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/` |
| ViewSelector (saved views) | `src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/ViewSelector.tsx` |
| ViewService (grid configs) | `src/client/shared/Spaarke.UI.Components/src/services/ViewService.ts` |
| BFF search endpoints | `src/server/api/Sprk.Bff.Api/Api/Ai/SemanticSearchEndpoints.cs` |
| Search models | `src/server/api/Sprk.Bff.Api/Models/Ai/SemanticSearch/` |
| Search service | `src/server/api/Sprk.Bff.Api/Services/Ai/SemanticSearch/SemanticSearchService.cs` |
| Endpoint definition pattern | `.claude/patterns/api/endpoint-definition.md` |
| Endpoint filters pattern | `.claude/patterns/api/endpoint-filters.md` |
| MSAL client pattern | `.claude/patterns/auth/msal-client.md` |
| sprk_gridconfiguration schema | `src/solutions/SpaarkeCore/entities/sprk_gridconfiguration/entity-schema.md` |

---

## Key Patterns

### Code Page API Data Adapter

The Universal DatasetGrid typically expects FetchXML/WebApi data. For search results from BFF API, use the `headlessConfig` prop with a custom data adapter that maps search results to the grid's expected format.

### Domain → API Service Routing

| Domain | Service | Index |
|--------|---------|-------|
| Documents | SemanticSearchApiService → `POST /api/ai/search` | `knowledge-index` |
| Matters | RecordSearchApiService → `POST /api/ai/search/records` | `spaarke-records-index` |
| Projects | RecordSearchApiService → `POST /api/ai/search/records` | `spaarke-records-index` |
| Invoices | RecordSearchApiService → `POST /api/ai/search/records` | `spaarke-records-index` |

### Domain → Filter Visibility

| Domain | Visible Filters |
|--------|----------------|
| Documents | Document Type, File Type, Matter Type, Date Range, Threshold, Mode |
| Matters | Matter Type, Date Range, Threshold, Mode |
| Projects | Date Range, Threshold, Mode |
| Invoices | Date Range, Threshold, Mode |

---

## Task Execution Protocol

### 🚨 MANDATORY: Task Execution Protocol for Claude Code

**ABSOLUTE RULE**: When executing project tasks, Claude Code MUST invoke the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Rigor Level: FULL

This project involves code implementation (Code Page, BFF API, TypeScript, C#). All tasks use FULL rigor:
- All 11 task-execute steps mandatory
- Checkpoint every 3 steps
- Quality gates (code-review + adr-check) at Step 9.5

### Multi-File Work Decomposition

When implementing tasks that modify 4+ files:
1. DECOMPOSE into independent sub-tasks
2. IDENTIFY parallel-safe work (no shared state)
3. DELEGATE to subagents (Task tool with subagent_type="general-purpose")
4. COORDINATE results

---

## Key Decisions Log

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-02-24 | Single-domain search (one tab at a time) | Simpler UX and implementation; query preserved across switches |
| 2026-02-24 | `sprk_gridconfiguration` for saved searches | More schema control; already used by Events ViewSelector |
| 2026-02-24 | React 19 bundled (Code Page) | ADR-021/022 mandate; DocumentRelationshipViewer precedent |
| 2026-02-24 | webpack (not Vite) | Matches existing DocRelViewer build pipeline |
| 2026-02-24 | Include DocRelViewer grid migration | Visual consistency ships together |
| 2026-02-24 | Inline graph drill-down | Matches DocRelViewer expand pattern |
| 2026-02-24 | Records index needs investigation | Coverage unknown — spike task before implementation |

---

## Quick Commands

```bash
# Build code page
cd src/client/code-pages/SemanticSearch && npm run build

# Build + inline HTML
cd src/client/code-pages/SemanticSearch && npm run build && pwsh build-webresource.ps1

# Build BFF API
dotnet build src/server/api/Sprk.Bff.Api/

# Run all tests
dotnet test

# Deploy code page to Dataverse
/code-page-deploy

# Deploy BFF API
/bff-deploy
```

---

*Project-specific context for Claude Code. See root CLAUDE.md for repository-wide rules.*
