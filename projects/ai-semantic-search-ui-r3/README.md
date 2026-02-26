# AI Semantic Search UI R3

> **Status**: Complete (pending manual Dataverse deployment)
> **Branch**: `work/ai-semantic-search-ui-r3`
> **Started**: 2026-02-24
> **Completed**: 2026-02-25
> **Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Tasks**: [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md)

---

## Overview

Full-page **Semantic Search code page** (Dataverse HTML web resource) providing system-wide, multi-entity AI-powered search across Documents, Matters, Projects, and Invoices. Combines the search/filter UX from the PCF SemanticSearchControl (R2) with metadata-driven graph clustering visualization, Universal DatasetGrid for tabular results, and saved search favorites.

## Key Deliverables

- **SemanticSearch Code Page** — React 19 single-file HTML web resource with graph + grid views
- **BFF API Enhancements** — `scope=all` for document search, new `POST /api/ai/search/records` endpoint
- **DocumentRelationshipViewer Grid Migration** — Migrate to Universal DatasetGrid
- **Saved Searches** — Filter strategy + field selection favorites via `sprk_gridconfiguration`
- **Sitemap & Navigation** — Sitemap entry + command bar button for code page access

## Architecture

```
┌─────────────────────────────────────────────────────┐
│  SemanticSearch Code Page (React 19, single HTML)   │
│  ┌──────────┐  ┌──────────────────────────────────┐ │
│  │ Search   │  │ Domain Tabs (Docs|Matters|...)   │ │
│  │ Filter   │  │ ┌────────────────────────────────┐│ │
│  │ Pane     │  │ │ Grid View (UniversalDatasetGrid)│ │
│  │          │  │ │ Graph View (@xyflow/react)     ││ │
│  │          │  │ └────────────────────────────────┘│ │
│  └──────────┘  └──────────────────────────────────┘ │
│  ┌──────────────────────────────────────────────────┐│
│  │ Command Bar (selection + entity-type aware)     ││
│  └──────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────┘
         │                          │
         ▼                          ▼
    POST /api/ai/search       POST /api/ai/search/records
    (knowledge-index)         (spaarke-records-index)
```

## Technology Stack

| Component | Technology | Version |
|-----------|-----------|---------|
| UI Framework | React | 19 |
| Component Library | Fluent UI v9 | 9.54+ |
| Graph Visualization | @xyflow/react | 12.x |
| Graph Layout | d3-force | 3.x |
| Data Grid | Universal DatasetGrid | Shared lib |
| Authentication | @azure/msal-browser | 3.x |
| Build | webpack | 5.x |
| Backend | .NET 8 Minimal API | — |

## Graduation Criteria

1. [x] User can search across Documents, Matters, Projects, Invoices from full-page interface
2. [x] Domain tabs change filters and grid columns per entity type
3. [x] Grid view uses Universal DatasetGrid with domain-specific columns
4. [x] Graph view clusters by Matter Type, Practice Area, Document Type, Organization, Person/Contact
5. [x] Saved searches store and restore filter strategy + field selections
6. [x] Command bar shows context-appropriate actions per entity type
7. [x] Dark mode works correctly (Fluent UI v9 tokens only)
8. [x] Single self-contained HTML file deploys to Dataverse
9. [x] BFF API supports `scope=all` and entity records search
10. [x] DocumentRelationshipViewer grid migrated (ADR-021 fix; GridView incompatible — see Task 051 notes)
11. [x] All tests pass, bundle size within limits (1.13 MiB; Fluent umbrella is main contributor)

## Quick Commands

```bash
# Build code page
cd src/client/code-pages/SemanticSearch && npm run build

# Build + inline HTML
cd src/client/code-pages/SemanticSearch && npm run build && pwsh build-webresource.ps1

# Build BFF API
dotnet build src/server/api/Sprk.Bff.Api/

# Run tests
dotnet test

# Deploy code page
/code-page-deploy

# Deploy BFF API
/bff-deploy
```

## Key Decisions

| Decision | Rationale |
|----------|-----------|
| Single-domain search (one tab at a time) | Simpler UX and implementation; query text preserved across switches |
| `sprk_gridconfiguration` for saved searches | Existing custom table with more schema control; already used by Events page |
| React 19 (bundled) | Code Page pattern per ADR-006/021/022; not platform-provided |
| webpack (not Vite) | Matches existing DocumentRelationshipViewer pattern |
| DocumentRelationshipViewer grid migration included | Ships visual consistency together |
| Inline graph drill-down | Matches DocRelViewer expand pattern |

---

*Project context for Claude Code. See root CLAUDE.md for repository-wide rules.*
