# Project Plan: Power BI Embedded Reporting R1

> **Last Updated**: 2026-03-31
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)

---

## 1. Executive Summary

**Purpose**: Embed Power BI reports into Spaarke's MDA via a "Reporting" Code Page with App Owns Data pattern, service principal profiles for multi-tenant isolation, Redis-cached embed tokens, BU RLS, and in-browser report authoring — all without per-user Power BI licensing.

**Scope**:
- BFF Reporting API endpoints (embed token generation, report catalog, export, profile management)
- `sprk_reporting` Code Page (React 19 + Vite + powerbi-client-react)
- `sprk_report` Dataverse entity and security role
- Module gating and 4-layer security
- 5 standard product reports with deployment pipeline
- Customer onboarding automation

**Estimated Effort**: ~120-160 hours across 5 phases

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-001**: BFF Minimal API pattern — `/api/reporting/*` endpoints, no Azure Functions
- **ADR-006**: Code Page for full-page Reporting UI — not PCF, not legacy webresource
- **ADR-008**: Endpoint filters for per-endpoint authorization — no global auth middleware
- **ADR-009**: Redis-first caching for embed tokens — `IDistributedCache`, no hybrid L1
- **ADR-010**: DI minimalism — `ReportingEmbedService` + `ReportingProfileManager` (≤2 registrations)
- **ADR-012**: Shared components from `@spaarke/ui-components` for UI elements
- **ADR-021**: Fluent UI v9 exclusively, dark mode required, design tokens only
- **ADR-026**: Vite 5 + `vite-plugin-singlefile` for Code Page build

**From Spec**:
- MUST use "App Owns Data" with service principal profiles
- MUST use Import mode (not DirectQuery or Direct Lake)
- MUST use "Reporting" naming exclusively (not "Analytics")
- MUST NOT require end-user Power BI licenses
- MUST cache embed tokens in Redis with 80% TTL auto-refresh

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Service principal profiles | Multi-tenant isolation without multiple app registrations | One SP, per-customer profiles |
| Import mode with scheduled refresh | Lower cost, simpler setup, 4x daily freshness sufficient | No real-time data |
| `powerbi-client-react` 2.0.2 | React 18+ compatible, official MS package | Code Page only (not PCF) |
| EffectiveIdentity for BU RLS | Standard PBI row-level security pattern | DAX USERNAME() filter in .pbix |
| Transparent PBI background | Dark mode support without custom CSS | `BackgroundType.Transparent` config |

### Discovered Resources

**Applicable ADRs**:
- `.claude/adr/ADR-001-minimal-api.md` — BFF endpoint pattern
- `.claude/adr/ADR-006-pcf-over-webresources.md` — Code Page default for UI
- `.claude/adr/ADR-008-endpoint-filters.md` — Per-endpoint authorization filters
- `.claude/adr/ADR-009-redis-caching.md` — Redis-first caching
- `.claude/adr/ADR-010-di-minimalism.md` — DI registration limits
- `.claude/adr/ADR-012-shared-components.md` — Shared UI components
- `.claude/adr/ADR-021-fluent-design-system.md` — Fluent v9, dark mode
- `.claude/adr/ADR-026-full-page-custom-page-standard.md` — Vite single-file build

**Applicable Skills**:
- `.claude/skills/code-page-deploy/` — Build and deploy Code Page web resource
- `.claude/skills/dataverse-deploy/` — Deploy entity/solution to Dataverse
- `.claude/skills/adr-aware/` — Auto-load ADRs during implementation
- `.claude/skills/script-aware/` — Discover deployment scripts

**Applicable Patterns**:
- `.claude/patterns/api/endpoint-definition.md` — Minimal API endpoint pattern
- `.claude/patterns/api/endpoint-filters.md` — Authorization filter implementation
- `.claude/patterns/api/service-registration.md` — Feature module DI pattern
- `.claude/patterns/auth/service-principal.md` — MSAL ConfidentialClientApplication
- `.claude/patterns/auth/oauth-scopes.md` — Power BI scope: `https://analysis.windows.net/.default`
- `.claude/patterns/auth/spaarke-auth-initialization.md` — Code Page bootstrap
- `.claude/patterns/caching/distributed-cache.md` — Redis embed token caching
- `.claude/patterns/webresource/full-page-custom-page.md` — Code Page boilerplate

**Reference Implementations**:
- `src/solutions/EventsPage/` — Canonical Code Page (React 19, Vite, single-file)
- `src/solutions/PlaybookLibrary/` — Production Code Page with URL params
- `src/server/api/Sprk.Bff.Api/Api/ScorecardCalculatorEndpoints.cs` — MapGroup endpoint pattern
- `scripts/Deploy-ExternalWorkspaceSpa.ps1` — Web resource deployment template

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Foundation & BFF API (Tasks 001-009)
├─ NuGet package setup (Microsoft.PowerBI.Api)
├─ ReportingEmbedService (SP auth, embed tokens, Redis caching)
├─ ReportingProfileManager (SP profile CRUD)
├─ ReportingEndpoints.cs (embed, catalog, export)
├─ ReportingAuthorizationFilter (security role check)
└─ Module gating via environment variable

Phase 2: Code Page & Embedding (Tasks 010-019)
├─ sprk_reporting Code Page scaffold (Vite + React 19)
├─ Power BI embed component (powerbi-client-react)
├─ Report dropdown (category-grouped catalog)
├─ Token auto-refresh (80% TTL, setAccessToken)
├─ Dark mode (transparent background + Fluent v9)
└─ Module gate UI (disabled state)

Phase 3: Authoring & Export (Tasks 020-029)
├─ Edit mode toggle (view → edit → save)
├─ Create new report flow
├─ Save / Save As with catalog update
├─ Export to PDF/PPTX
└─ Author/Admin privilege UI controls

Phase 4: Dataverse & Reports (Tasks 030-039)
├─ sprk_report entity definition
├─ sprk_ReportingAccess security role
├─ sprk_ReportingModuleEnabled environment variable
├─ 5 standard .pbix report templates
├─ Deploy-ReportingReports.ps1 script
└─ Report versioning in source control

Phase 5: Integration & Onboarding (Tasks 040-049)
├─ Customer onboarding script
├─ BU RLS verification tests
├─ Multi-deployment model testing
├─ Integration tests
├─ End-to-end smoke tests
└─ Documentation
```

### Critical Path

**Blocking Dependencies:**
- Phase 2 BLOCKED BY Phase 1 (BFF endpoints needed for embedding)
- Phase 3 BLOCKED BY Phase 2 (embed component needed for authoring)
- Phase 4 tasks (entity, security role) can run PARALLEL with Phase 1-2
- Phase 5 BLOCKED BY Phases 1-4

**High-Risk Items:**
- Service principal profile API — first use in codebase; Mitigation: spike with PBI REST API early
- `powerbi-client-react` integration — first use; Mitigation: prototype embed in Phase 2 task 010
- BU RLS with EffectiveIdentity — complex; Mitigation: test with 2+ BU users in Phase 5

---

## 4. Phase Breakdown

### Phase 1: Foundation & BFF API

**Objectives:**
1. Set up Power BI NuGet dependencies and configuration
2. Implement service principal authentication for Power BI REST API
3. Build embed token generation with Redis caching
4. Create reporting API endpoints with authorization filters
5. Implement module gating

**Deliverables:**
- [ ] `Microsoft.PowerBI.Api` NuGet package added
- [ ] `ReportingEmbedService.cs` — SP auth, embed token generation, Redis caching
- [ ] `ReportingProfileManager.cs` — SP profile management
- [ ] `ReportingEndpoints.cs` — `/api/reporting/*` endpoints
- [ ] `ReportingAuthorizationFilter.cs` — security role + module gate check
- [ ] `ReportingModule.cs` — DI registration
- [ ] Environment variable configuration for PBI settings

**Critical Tasks:**
- SP authentication and token generation MUST BE FIRST — blocks all embedding
- Module gating endpoint filter — blocks UI work

**Inputs**: spec.md, ADR-001, ADR-008, ADR-009, ADR-010, service-principal pattern, endpoint-definition pattern

**Outputs**: Working `/api/reporting/embed-token`, `/api/reporting/reports`, `/api/reporting/export` endpoints

### Phase 2: Code Page & Embedding

**Objectives:**
1. Scaffold `sprk_reporting` Code Page with Vite + React 19
2. Embed Power BI report viewer using `powerbi-client-react`
3. Build report catalog dropdown with category grouping
4. Implement token auto-refresh mechanism
5. Add dark mode support with transparent PBI background

**Deliverables:**
- [ ] `src/solutions/Reporting/` — Code Page project (package.json, vite.config.ts, etc.)
- [ ] `ReportViewer.tsx` — Power BI embed component
- [ ] `ReportDropdown.tsx` — Category-grouped report selector
- [ ] Token auto-refresh hook (`usePowerBiEmbed.ts`)
- [ ] Dark mode with transparent background
- [ ] Module disabled state UI

**Critical Tasks:**
- Code Page scaffold and basic embed — validates full stack connectivity
- Token refresh — critical for production reliability

**Inputs**: Phase 1 endpoints, ADR-006, ADR-021, ADR-026, full-page-custom-page pattern, EventsPage reference

**Outputs**: Working embedded report viewer with dropdown and auto-refresh

### Phase 3: Authoring & Export

**Objectives:**
1. Enable embedded edit mode for Authors
2. Implement create new report flow
3. Add save / save-as functionality
4. Build export to PDF/PPTX
5. Implement role-based UI controls (Viewer/Author/Admin)

**Deliverables:**
- [ ] Edit mode toggle component
- [ ] New report creation flow (blank report bound to semantic model)
- [ ] Save/Save As with `sprk_report` catalog update
- [ ] Export endpoint and UI (PDF/PPTX)
- [ ] Role-based button visibility (view only / edit+create / edit+create+delete)

**Inputs**: Phase 2 embed component, PBI REST API docs, FR-06 through FR-13

**Outputs**: Full authoring experience with role-based access

### Phase 4: Dataverse & Reports

**Objectives:**
1. Define `sprk_report` entity schema in Dataverse
2. Create `sprk_ReportingAccess` security role (Viewer/Author/Admin privileges)
3. Create `sprk_ReportingModuleEnabled` environment variable
4. Create 5 standard product report .pbix templates
5. Build deployment pipeline script

**Deliverables:**
- [ ] `sprk_report` entity with attributes (name, category, workspace_id, report_id, is_custom)
- [ ] `sprk_ReportingAccess` security role with privilege tiers
- [ ] `sprk_ReportingModuleEnabled` environment variable definition
- [ ] 5 .pbix report templates in `reports/v1.0.0/`
- [ ] `Deploy-ReportingReports.ps1` deployment script
- [ ] `reports/CHANGELOG.md` version tracking

**Critical Tasks:**
- Entity and security role can start in parallel with Phase 1-2
- .pbix templates require PBI Desktop (manual creation — human task)

**Inputs**: spec.md entity definition, existing Deploy-*.ps1 scripts as templates

**Outputs**: Dataverse schema ready, reports deployable

### Phase 5: Integration & Onboarding

**Objectives:**
1. Build customer onboarding automation
2. Verify BU RLS across business units
3. Test all 3 deployment models
4. Write integration and E2E tests
5. Create operational documentation

**Deliverables:**
- [ ] Customer onboarding script (workspace + SP profile + report deployment)
- [ ] BU RLS verification test cases
- [ ] Multi-deployment model test results
- [ ] Integration tests for reporting endpoints
- [ ] User documentation and admin guide

**Inputs**: All Phase 1-4 outputs, test environments

**Outputs**: Production-ready module with documentation

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Power BI REST API | GA | Low | Standard MS service |
| `powerbi-client-react` 2.0.2 | GA | Low | Pin version |
| `Microsoft.PowerBI.Api` NuGet | GA | Low | Pin version |
| F-SKU capacity (F2+ dev) | Pending | High | Document prerequisites; fail gracefully |
| Entra ID app registration | Pending | Medium | Onboarding checklist |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| BFF API host | `src/server/api/Sprk.Bff.Api/` | Production |
| Redis cache | Infrastructure | Production |
| `@spaarke/ui-components` | `src/client/shared/Spaarke.UI.Components/` | Production |
| `@spaarke/auth` | `src/client/shared/Spaarke.Auth/` | Production |
| Vite build pipeline | Existing Code Pages | Production |

---

## 6. Testing Strategy

**Unit Tests** (80% coverage target):
- `ReportingEmbedService` — token generation, caching, profile selection
- `ReportingProfileManager` — profile CRUD operations
- `ReportingAuthorizationFilter` — role checks, module gate
- `ReportingEndpoints` — handler logic, error responses

**Integration Tests**:
- Embed token generation end-to-end (BFF → PBI REST API)
- Redis cache hit/miss/expiry scenarios
- Module gating (enabled vs disabled)
- Security role enforcement (unauthorized access blocked)

**E2E Tests**:
- Report renders in Code Page with correct data
- Report dropdown loads catalog
- Token auto-refresh (simulate near-expiry)
- Edit mode → save → verify catalog update
- Export to PDF download
- Dark mode visual check

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase 1:**
- [ ] `/api/reporting/embed-token` returns valid PBI embed token in < 500ms (cached)
- [ ] `/api/reporting/reports` returns report catalog from Dataverse
- [ ] Endpoints return 404 when module disabled, 403 when unauthorized

**Phase 2:**
- [ ] Code Page renders embedded PBI report within 3 seconds
- [ ] Report dropdown shows catalog grouped by category
- [ ] Token auto-refreshes at 80% TTL without page reload
- [ ] Dark mode renders with transparent PBI background

**Phase 3:**
- [ ] Author can create, edit, save, and save-as reports
- [ ] Export produces valid PDF/PPTX files
- [ ] UI buttons respect Viewer/Author/Admin roles

**Phase 4:**
- [ ] `sprk_report` entity created with all attributes
- [ ] Security role has 3 privilege tiers
- [ ] Deployment script imports .pbix and seeds catalog

**Phase 5:**
- [ ] BU RLS verified with 2+ BUs seeing different data
- [ ] Onboarding script provisions workspace end-to-end
- [ ] All 3 deployment models tested

### Business Acceptance

- [ ] No end-user requires Power BI Pro/PPU license
- [ ] Reports load within 3 seconds for standard data volumes
- [ ] Module can be enabled/disabled per customer without code changes

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | F-SKU capacity not provisioned | Medium | High | Clear prerequisites doc; graceful error page |
| R2 | SP profile API behavior differs from docs | Medium | High | Early spike; fallback to workspace-per-customer |
| R3 | `powerbi-client-react` incompatibility | Low | Medium | Pin version; test early in Phase 2 |
| R4 | BU RLS EffectiveIdentity complexity | Medium | Medium | Prototype early; document DAX pattern |
| R5 | Dark mode PBI background rendering | Medium | Low | Test `BackgroundType.Transparent` early |
| R6 | Large semantic model performance | Low | Medium | Monitor; plan Direct Lake for R2 |

---

## 9. Next Steps

1. **Generate task files** from this plan
2. **Begin Phase 1** — BFF API foundation
3. **Parallel**: Start Phase 4 Dataverse schema (independent of API work)

---

**Status**: Ready for Tasks
**Next Action**: Run task decomposition to generate POML task files

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
