# SDAP SPE Admin App — Implementation Plan

> **Created**: 2026-03-13
> **Source**: spec.md
> **Phases**: 3 (MVP → Full Features → Advanced)

---

## Architecture Context

### Component Overview

| Component | Type | Location |
|-----------|------|----------|
| SPE Admin Code Page | React 18 + Vite | `src/solutions/SpeAdminApp/` |
| BFF API Endpoints | .NET 8 Minimal API | `src/server/api/Sprk.Bff.Api/Endpoints/SpeAdmin/` |
| BFF Services | .NET 8 Services | `src/server/api/Sprk.Bff.Api/Services/SpeAdmin/` |
| DI Module | Feature module | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/SpeAdminModule.cs` |
| Dataverse Tables | Solution components | Dataverse environment |

### Discovered Resources

#### Applicable ADRs

| ADR | Topic | Relevance |
|-----|-------|-----------|
| ADR-001 | Minimal API + BackgroundService | BFF endpoints, dashboard sync service |
| ADR-006 | Code Pages for standalone UIs | SPE Admin is a Code Page (not PCF, not legacy JS) |
| ADR-008 | Endpoint filters for auth | SpeAdminAuthorizationFilter on route group |
| ADR-010 | DI minimalism | AddSpeAdminModule() feature module, ≤15 registrations |
| ADR-012 | Shared component library | Reuse @spaarke/ui-components heavily |
| ADR-019 | ProblemDetails errors | All API error responses |
| ADR-021 | Fluent UI v9 design system | Exclusive UI framework, dark mode required |
| ADR-022 | PCF platform libraries | Code Page bundles React 18 (not platform-provided) |

#### Reference Patterns

| Pattern | File | Purpose |
|---------|------|---------|
| Code Page entry | `src/solutions/LegalWorkspace/src/main.tsx` | createRoot() pattern |
| Vite config | `src/solutions/LegalWorkspace/vite.config.ts` | viteSingleFile + shared lib aliases |
| Endpoint group | `src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs` | MapGroup + endpoint filters |
| Auth filter | `src/server/api/Sprk.Bff.Api/Api/Filters/DocumentAuthorizationFilter.cs` | IEndpointFilter pattern |
| BackgroundService | `src/server/api/Sprk.Bff.Api/Services/Communication/DailySendCountResetService.cs` | Periodic sync pattern |
| Graph factory | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` | ForApp() + ForUserAsync() |
| DI module | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/CommunicationModule.cs` | Feature module registration |
| Shared UI exports | `src/client/shared/Spaarke.UI.Components/src/index.ts` | Barrel exports |

#### Knowledge Docs & Patterns

| Resource | Path | Tags |
|----------|------|------|
| Code Page template | `.claude/patterns/webresource/full-page-custom-page.md` | code-page, vite |
| Endpoint definition | `.claude/patterns/api/endpoint-definition.md` | bff-api |
| Endpoint filters | `.claude/patterns/api/endpoint-filters.md` | bff-api, auth |
| Error handling | `.claude/patterns/api/error-handling.md` | bff-api |
| Service registration | `.claude/patterns/api/service-registration.md` | bff-api, di |
| Background workers | `.claude/patterns/api/background-workers.md` | bff-api, background |
| OAuth scopes | `.claude/patterns/auth/oauth-scopes.md` | auth |
| OBO flow | `.claude/patterns/auth/obo-flow.md` | auth |
| MSAL client | `.claude/patterns/auth/msal-client.md` | auth, code-page |
| Graph SDK v5 | `.claude/patterns/auth/graph-sdk-v5.md` | auth, graph |
| Graph endpoints | `.claude/patterns/auth/graph-endpoints-catalog.md` | graph |
| Dataverse Web API | `.claude/patterns/dataverse/web-api-client.md` | dataverse |
| API constraints | `.claude/constraints/api.md` | bff-api |
| Auth constraints | `.claude/constraints/auth.md` | auth |
| Web resource constraints | `.claude/constraints/webresource.md` | code-page |
| Shared UI guide | `docs/guides/SHARED-UI-COMPONENTS-GUIDE.md` | frontend |
| Token cache | `.claude/patterns/caching/token-cache.md` | auth, caching |

#### Scripts

| Script | Purpose |
|--------|---------|
| `scripts/Deploy-BffApi.ps1` | Deploy BFF API to Azure App Service |
| `scripts/Test-SdapBffApi.ps1` | Test BFF API endpoints |

---

## Phase Breakdown

### Phase 1: MVP — Foundation & Core Features

**Goal**: Working admin app with BU-scoped container and file management.

#### 1.1 Dataverse Tables & Configuration

| # | Deliverable | Estimate |
|---|------------|----------|
| 001 | Create `sprk_speenvironment` table schema and solution component | 2h |
| 002 | Create `sprk_specontainertypeconfig` table schema with BU lookup | 3h |
| 003 | Create `sprk_speauditlog` table schema with lookups | 2h |
| 004 | Add sitemap entry for SPE Admin in model-driven app | 1h |

#### 1.2 BFF API — Infrastructure

| # | Deliverable | Estimate |
|---|------------|----------|
| 005 | Create `SpeAdminModule.cs` DI feature module | 2h |
| 006 | Create `SpeAdminGraphService.cs` — multi-config Graph client resolution | 4h |
| 007 | Create `SpeAuditService.cs` — audit logging to Dataverse | 2h |
| 008 | Create `SpeAdminAuthorizationFilter.cs` — endpoint authorization filter | 2h |
| 009 | Create `SpeAdminEndpoints.cs` — route group registration | 1h |

#### 1.3 BFF API — Configuration Endpoints

| # | Deliverable | Estimate |
|---|------------|----------|
| 010 | Environment CRUD endpoints (GET/POST/PUT/DELETE `/api/spe/environments`) | 3h |
| 011 | Container type config CRUD endpoints (GET/POST/PUT/DELETE `/api/spe/configs`) | 3h |
| 012 | Business Unit list endpoint (GET `/api/spe/businessunits`) | 1h |

#### 1.4 BFF API — Container Endpoints

| # | Deliverable | Estimate |
|---|------------|----------|
| 013 | List/Get containers (GET `/api/spe/containers`) | 2h |
| 014 | Create container (POST `/api/spe/containers`) | 2h |
| 015 | Update/Activate/Lock/Unlock container endpoints | 3h |
| 016 | Container permission CRUD endpoints | 3h |

#### 1.5 BFF API — File Endpoints

| # | Deliverable | Estimate |
|---|------------|----------|
| 017 | List items in folder (GET `/api/spe/containers/{id}/items`) | 2h |
| 018 | Upload file endpoint (POST with multipart) | 3h |
| 019 | Download, preview, delete file endpoints | 2h |
| 020 | File versions, thumbnails, sharing link endpoints | 2h |
| 021 | Create folder endpoint | 1h |

#### 1.6 BFF API — Dashboard & Audit

| # | Deliverable | Estimate |
|---|------------|----------|
| 022 | Dashboard metrics endpoint + `SpeDashboardSyncService` BackgroundService | 3h |
| 023 | Audit log query endpoint with filters | 2h |

#### 1.7 Code Page — Project Scaffolding

| # | Deliverable | Estimate |
|---|------------|----------|
| 024 | Scaffold `src/solutions/SpeAdminApp/` (Vite, package.json, tsconfig, index.html) | 2h |
| 025 | Create `main.tsx` entry point with createRoot and theme detection | 1h |
| 026 | Create TypeScript types (`types/spe.ts`) from spec interfaces | 2h |
| 027 | Create `speApiClient.ts` — authenticatedFetch wrapper for `/api/spe/*` | 2h |
| 028 | Create `BuContext.tsx` — BU + config selection state management | 2h |

#### 1.8 Code Page — Layout & Navigation

| # | Deliverable | Estimate |
|---|------------|----------|
| 029 | Create `AppShell.tsx` — navigation panel + content area layout | 3h |
| 030 | Create `BuContextPicker.tsx` — BU → CT Config → Environment cascade | 3h |
| 031 | Create `NavigationPanel.tsx` — left nav with section icons | 2h |

#### 1.9 Code Page — Dashboard

| # | Deliverable | Estimate |
|---|------------|----------|
| 032 | Create `DashboardPage.tsx` — metrics cards + recent activity grid | 3h |

#### 1.10 Code Page — Container Management

| # | Deliverable | Estimate |
|---|------------|----------|
| 033 | Create `ContainersPage.tsx` — container list with UniversalDatasetGrid | 3h |
| 034 | Create `ContainerDetail.tsx` — detail side panel with tabs | 3h |
| 035 | Create `PermissionPanel.tsx` — add/edit/remove permissions | 3h |

#### 1.11 Code Page — File Browser

| # | Deliverable | Estimate |
|---|------------|----------|
| 036 | Create `FileBrowserPage.tsx` — file/folder grid with breadcrumb nav | 4h |
| 037 | Create `FileDetailPanel.tsx` — file metadata, versions, sharing | 3h |

#### 1.12 Code Page — Settings

| # | Deliverable | Estimate |
|---|------------|----------|
| 038 | Create `SettingsPage.tsx` — environment + config management | 3h |
| 039 | Create `EnvironmentConfig.tsx` — environment CRUD form | 2h |
| 040 | Create `ContainerTypeConfig.tsx` — CT config CRUD form | 3h |

#### 1.13 Code Page — Audit Log

| # | Deliverable | Estimate |
|---|------------|----------|
| 041 | Create `AuditLogPage.tsx` — filterable audit log grid | 3h |

#### 1.14 Phase 1 Integration & Deploy

| # | Deliverable | Estimate |
|---|------------|----------|
| 042 | Build Code Page (npm run build → dist/speadmin.html) and verify | 2h |
| 043 | Deploy BFF API with new `/api/spe/*` endpoints | 2h |
| 044 | Deploy Code Page to Dataverse as `sprk_speadmin` web resource | 2h |
| 045 | Create Dataverse tables in environment | 2h |
| 046 | End-to-end integration testing (BU picker → containers → files) | 4h |

### Phase 2: Full Features

**Goal**: Complete SPE admin coverage with container types, search, security.

#### 2.1 BFF API — Container Type Endpoints

| # | Deliverable | Estimate |
|---|------------|----------|
| 050 | List/Get container types (GET `/api/spe/containertypes`) | 2h |
| 051 | Create container type (POST `/api/spe/containertypes`) | 2h |
| 052 | Update container type settings | 2h |
| 053 | Register container type on consuming tenant | 3h |
| 054 | List container type app permissions | 2h |

#### 2.2 BFF API — Additional Endpoints

| # | Deliverable | Estimate |
|---|------------|----------|
| 055 | Container column CRUD endpoints | 2h |
| 056 | Container custom property endpoints | 2h |
| 057 | Search containers endpoint (POST `/api/spe/search/containers`) | 3h |
| 058 | Search items endpoint (POST `/api/spe/search/items`) | 3h |
| 059 | Recycle bin endpoints (list/restore/permanent delete) | 2h |
| 060 | Security alerts and secure score endpoints | 2h |

#### 2.3 Code Page — Container Types UI

| # | Deliverable | Estimate |
|---|------------|----------|
| 061 | Create `ContainerTypesPage.tsx` — container type grid | 3h |
| 062 | Create `ContainerTypeDetail.tsx` — settings editor panel | 3h |
| 063 | Create `RegisterWizard.tsx` — CT registration flow with permission selection | 4h |

#### 2.4 Code Page — Container Enhancements

| # | Deliverable | Estimate |
|---|------------|----------|
| 064 | Create `ColumnEditor.tsx` — column definitions management | 3h |
| 065 | Create `CustomPropertyEditor.tsx` — custom properties management | 3h |

#### 2.5 Code Page — Search

| # | Deliverable | Estimate |
|---|------------|----------|
| 066 | Create `SearchPage.tsx` — search interface | 3h |
| 067 | Create `ContainerResultsGrid.tsx` — actionable container results (bulk delete, export) | 3h |
| 068 | Create `ItemResultsGrid.tsx` — actionable item results (delete, permissions, download) | 3h |

#### 2.6 Code Page — Recycle Bin & Security

| # | Deliverable | Estimate |
|---|------------|----------|
| 069 | Create `RecycleBinPage.tsx` — restore/permanent delete grid | 2h |
| 070 | Create `SecurityPage.tsx` — alerts grid + secure score card | 3h |

#### 2.7 Phase 2 Integration & Deploy

| # | Deliverable | Estimate |
|---|------------|----------|
| 071 | Phase 2 integration testing | 3h |
| 072 | Deploy updated BFF and Code Page | 2h |

### Phase 3: Advanced Features

**Goal**: eDiscovery, retention, multi-tenant, bulk ops.

#### 3.1 Advanced Features

| # | Deliverable | Estimate |
|---|------------|----------|
| 080 | eDiscovery read-only dashboard (cases, custodians, data sources) | 4h |
| 081 | Retention label management | 3h |
| 082 | Multi-tenant consuming tenant management | 4h |
| 083 | Bulk operations (batch delete, batch permission assignment) | 4h |
| 084 | Multi-app registration support (different owning app per BU) | 4h |

#### 3.2 Phase 3 Deploy & Wrap-up

| # | Deliverable | Estimate |
|---|------------|----------|
| 085 | Phase 3 integration testing and deployment | 3h |
| 090 | Project wrap-up — update README status, create lessons-learned.md | 2h |

---

## Task Summary

| Phase | Tasks | Estimate |
|-------|-------|----------|
| Phase 1 (MVP) | 001–046 | ~105h |
| Phase 2 (Full) | 050–072 | ~55h |
| Phase 3 (Advanced) | 080–090 | ~24h |
| **Total** | **~60 tasks** | **~184h** |

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| A | 001, 002, 003 | None | Independent Dataverse table definitions |
| B | 010, 011, 012 | 005, 009 | Independent config endpoints |
| C | 013–016 | 006, 009 | Container endpoints (sequential within) |
| D | 017–021 | 006, 009 | File endpoints (sequential within) |
| E | 024–028 | None | Code Page scaffolding (independent of BFF) |
| F | 033–035 | 029, 030, 031, 027 | Container UI pages |
| G | 036–037 | 029, 027 | File browser UI pages |
| H | 038–040 | 029, 027 | Settings UI pages |
| I | 050–054 | 006 | Container type endpoints |
| J | 055–060 | 006 | Additional Phase 2 endpoints |
| K | 061–065 | 029, 050–054 | Container type + enhanced container UI |
| L | 066–068 | 057, 058, 029 | Search UI |

## Critical Path

```
005 (DI Module) → 006 (GraphService) → 009 (Endpoints) → 013 (Containers API) → 024 (Scaffold) → 029 (AppShell) → 033 (ContainersPage) → 046 (E2E Test)
```

## Risk Items

| Risk | Impact | Mitigation |
|------|--------|------------|
| Graph API rate limiting during dev/test | Medium | Implement throttle-aware retry; cache responses |
| Multi-config credential caching complexity | Medium | Start with single config; add caching incrementally |
| Key Vault access from dev machine | Low | Use local secrets for dev; Key Vault for deployed |
| Large file upload through BFF proxy | Medium | Implement chunked upload; set appropriate timeouts |
| Container type registration requires SharePoint admin | High | Document prerequisites; test with trial types first |

---

## References

- [spec.md](spec.md) — Full specification
- [design.md](design.md) — Original design document
- [ADR-001](.claude/adr/ADR-001.md) — Minimal API + BackgroundService
- [ADR-006](.claude/adr/ADR-006.md) — Code Pages
- [ADR-008](.claude/adr/ADR-008.md) — Endpoint filters
- [ADR-010](.claude/adr/ADR-010.md) — DI minimalism
- [ADR-012](.claude/adr/ADR-012.md) — Shared component library
- [ADR-021](.claude/adr/ADR-021.md) — Fluent UI v9
- [ADR-022](.claude/adr/ADR-022.md) — PCF platform libraries
- [Microsoft Graph SPE API](https://learn.microsoft.com/en-us/graph/api/resources/filestoragecontainer)
