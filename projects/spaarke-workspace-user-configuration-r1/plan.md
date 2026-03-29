# Project Plan: Configurable Workspace — User-Personalized Dashboard Layouts

> **Last Updated**: 2026-03-29
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)

---

## 1. Executive Summary

**Purpose**: Enable users to personalize their workspace dashboard layout by choosing templates, selecting sections, and arranging them via drag-and-drop. Stored per-user in Dataverse with BFF API, replacing the hardcoded configuration.

**Scope**:
- Section Registry + standard section contract
- BFF CRUD endpoints (8 endpoints)
- Layout Wizard Code Page (3-step)
- Workspace Header with switcher
- Dynamic config builder
- useFeedTodoSync independence fix

**Estimated Effort**: ~80-100 hours across 7 phases

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-001**: Minimal API for all BFF endpoints; return ProblemDetails for errors
- **ADR-006**: Layout Wizard is a Code Page, not PCF; standalone dialog via navigateTo
- **ADR-008**: Endpoint authorization via filters, not global middleware
- **ADR-010**: DI minimalism — <=15 non-framework registrations
- **ADR-012**: Shared components via `@spaarke/ui-components`; Fluent v9 only; no PCF-specific APIs
- **ADR-021**: Fluent UI v9 exclusively; makeStyles; dark mode + high contrast; WCAG 2.1 AA
- **ADR-026**: Code Page standard — Vite + vite-plugin-singlefile; React 19 createRoot; single HTML output

**From Spec**:
- Store only section IDs and grid positions in Dataverse — no serialized JSX
- Gracefully handle missing section IDs (skip with console warning)
- Enforce max 10 user workspaces per user
- System workspaces are read-only and non-deletable
- Cache last-used layout in sessionStorage, invalidate on wizard save

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Section Registry as code-side array | Single source of truth; adding sections = 1 registration object | All section discovery flows from registry |
| SectionFactoryContext standard contract | Sections own their behavior; no bespoke parent wiring | Each section factory is self-contained |
| Dynamic config builder merges JSON + registry | Clean separation of stored layout from rendering logic | WorkspaceShell unchanged |
| System workspace defined in code, not Dataverse | Always available, cannot be broken by user | BFF returns alongside user layouts |
| Schema versioning in JSON | Forward compatibility for layout changes | Config builder checks version |

### Discovered Resources

**Applicable ADRs**:
- `.claude/adr/ADR-001-minimal-api.md` — Endpoint patterns
- `.claude/adr/ADR-006-pcf-over-webresources.md` — Code Page vs PCF decision
- `.claude/adr/ADR-008-endpoint-filters.md` — Authorization filter pattern
- `.claude/adr/ADR-010-di-minimalism.md` — DI registration limits
- `.claude/adr/ADR-012-shared-components.md` — Shared library rules
- `.claude/adr/ADR-021-fluent-design-system.md` — Fluent v9, dark mode, WCAG
- `.claude/adr/ADR-026-full-page-custom-page-standard.md` — Vite single-file build

**Applicable Constraints**:
- `.claude/constraints/api.md` — BFF API MUST/MUST NOT rules
- `.claude/constraints/webresource.md` — Web resource rules
- `.claude/constraints/react-versioning.md` — React 16 (PCF) vs React 19 (Code Pages)
- `.claude/constraints/testing.md` — Testing standards

**Applicable Patterns**:
- `.claude/patterns/api/endpoint-definition.md` — Canonical endpoint structure
- `.claude/patterns/api/service-registration.md` — DI registration pattern
- `.claude/patterns/api/endpoint-filters.md` — Authorization filter pattern
- `.claude/patterns/webresource/full-page-custom-page.md` — Code Page project structure

**Reusable Code**:
- `src/solutions/LegalWorkspace/src/workspaceConfig.tsx` — Current hardcoded config (to be replaced)
- `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/` — Shell component + types
- `src/server/api/Sprk.Bff.Api/Api/Documents/DocumentsEndpoints.cs` — Endpoint pattern reference
- `src/server/api/Sprk.Bff.Api/Api/Filters/DocumentAuthorizationFilter.cs` — Filter pattern reference

**Applicable Scripts**:
- `scripts/Deploy-CorporateWorkspace.ps1` — Deploys corporate workspace web resource
- `scripts/Deploy-BffApi.ps1` — Deploys BFF API to Azure

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Foundation & Types (serial)
└─ Section Registry types, SectionFactoryContext, layout template definitions

Phase 2: Section Migrations (parallel)
└─ Migrate 5 existing sections to registry pattern + useFeedTodoSync fix

Phase 3: Backend API (parallel)
└─ 8 BFF endpoints + Dataverse entity definition

Phase 4: Dynamic Config & Header (serial/parallel)
└─ Dynamic config builder, workspace header, workspace loading logic

Phase 5: Layout Wizard Code Page (serial)
└─ 3-step wizard: template selection, section checklist, DnD arrangement

Phase 6: Integration & Polish (parallel)
└─ URL deep-linking, sessionStorage caching, loading states, system workspace

Phase 7: Testing, Deployment & Wrap-up (serial)
└─ Integration tests, deployment, code review, ADR check
```

### Critical Path

**Blocking Dependencies:**
- Phase 2 BLOCKED BY Phase 1 (types must exist before section migrations)
- Phase 4 BLOCKED BY Phase 1 + Phase 2 (config builder needs registry + migrated sections)
- Phase 5 BLOCKED BY Phase 1 + Phase 3 (wizard needs types + API endpoints)
- Phase 6 BLOCKED BY Phase 4 (integration needs dynamic config working)
- Phase 7 BLOCKED BY all prior phases

**High-Risk Items:**
- Drag-and-drop in wizard Step 3 — complex interaction, may need iteration
- Section migration — changing behavior of 5 existing sections requires careful testing
- useFeedTodoSync no-op — must not break existing ActivityFeed+SmartToDo sync

---

## 4. Phase Breakdown

### Phase 1: Foundation & Types

**Objectives:**
1. Define `SectionRegistration` and `SectionFactoryContext` interfaces in shared library
2. Define layout template configurations (6 templates)
3. Define `WorkspaceLayoutDto` and API DTOs
4. Define Dataverse entity schema for `sprk_workspacelayout`

**Deliverables:**
- [ ] `SectionRegistration` interface in `@spaarke/ui-components`
- [ ] `SectionFactoryContext` interface in `@spaarke/ui-components`
- [ ] Layout template type definitions and constants
- [ ] `WorkspaceLayoutDto` and related API types
- [ ] Dataverse entity definition (sprk_workspacelayout)

**Inputs**: spec.md, WorkspaceShell types.ts
**Outputs**: Type definitions, entity schema

### Phase 2: Section Migrations (PARALLEL)

**Objectives:**
1. Migrate 5 existing sections to `SectionRegistration` pattern
2. Fix `useFeedTodoSync` to return no-op stubs
3. Create Section Registry array

**Deliverables:**
- [ ] `getStartedRegistration` — Get Started section factory
- [ ] `quickSummaryRegistration` — Quick Summary section factory
- [ ] `latestUpdatesRegistration` — Latest Updates section factory
- [ ] `todoRegistration` — My To Do List section factory
- [ ] `documentsRegistration` — My Documents section factory
- [ ] `useFeedTodoSync` no-op fix
- [ ] `SECTION_REGISTRY` array

**Inputs**: workspaceConfig.tsx (current factories), SectionRegistration types
**Outputs**: 5 registration files, updated useFeedTodoSync, sectionRegistry.ts

### Phase 3: Backend API (PARALLEL)

**Objectives:**
1. Create Dataverse entity `sprk_workspacelayout`
2. Implement 8 BFF endpoints for workspace layouts
3. Add authorization filter for layout ownership

**Deliverables:**
- [ ] `GET /api/workspace/layouts` — list user layouts
- [ ] `GET /api/workspace/layouts/{id}` — get specific layout
- [ ] `GET /api/workspace/layouts/default` — get user default
- [ ] `POST /api/workspace/layouts` — create (max 10 enforced)
- [ ] `PUT /api/workspace/layouts/{id}` — update (user-only)
- [ ] `DELETE /api/workspace/layouts/{id}` — delete (user-only)
- [ ] `GET /api/workspace/sections` — list registry sections
- [ ] `GET /api/workspace/templates` — list layout templates
- [ ] Workspace layout authorization filter
- [ ] DI registration for workspace services

**Inputs**: spec.md API section, endpoint-definition.md pattern
**Outputs**: Endpoint files, service files, filter files

### Phase 4: Dynamic Config & Header

**Objectives:**
1. Build dynamic config builder (layout JSON + registry -> WorkspaceConfig)
2. Build Workspace Header component with dropdown switcher
3. Integrate with LegalWorkspace to replace hardcoded config

**Deliverables:**
- [ ] `buildDynamicWorkspaceConfig()` function
- [ ] `WorkspaceHeader` component (dropdown + settings button)
- [ ] LegalWorkspace integration (replace buildWorkspaceConfig)
- [ ] System workspace definition (Corporate Workspace)

**Inputs**: SectionRegistry, WorkspaceShell types, BFF endpoints
**Outputs**: Dynamic config builder, header component, updated LegalWorkspace

### Phase 5: Layout Wizard Code Page

**Objectives:**
1. Scaffold wizard Code Page project (Vite + React 19 + single-file)
2. Build Step 1: Layout template selection (6 visual thumbnails)
3. Build Step 2: Section selection (grouped checklist from registry)
4. Build Step 3: Section arrangement (drag-and-drop) + naming + default checkbox
5. Wire save flow to BFF API

**Deliverables:**
- [ ] `src/solutions/WorkspaceLayoutWizard/` project scaffold
- [ ] Wizard Step 1: Template selection
- [ ] Wizard Step 2: Section selection
- [ ] Wizard Step 3: Arrange + name + default
- [ ] API integration (POST/PUT)
- [ ] Vite build producing single HTML (`sprk_workspacelayoutwizard`)

**Inputs**: WizardDialog component, BFF endpoints, section registry data
**Outputs**: Code Page project, single HTML web resource

### Phase 6: Integration & Polish (PARALLEL)

**Objectives:**
1. URL deep-linking via `workspaceId` parameter
2. sessionStorage caching with wizard-save invalidation
3. Loading states (skeleton, first-time banner, error fallback)
4. System workspace Save As flow
5. Slot overflow/underflow handling

**Deliverables:**
- [ ] URL parameter parsing and workspace routing
- [ ] sessionStorage cache layer
- [ ] Loading skeleton + "Personalize" banner + error toast
- [ ] Save As mode in wizard (system workspace -> user copy)
- [ ] Overflow row auto-append logic

**Inputs**: Dynamic config, header, wizard
**Outputs**: Polished workspace experience

### Phase 7: Testing, Deployment & Wrap-up

**Objectives:**
1. Integration tests for BFF endpoints
2. Deploy wizard Code Page and BFF API
3. Run quality gates (code-review + adr-check)
4. Update project documentation

**Deliverables:**
- [ ] BFF endpoint integration tests
- [ ] Wizard Code Page deployed to Dataverse
- [ ] BFF API deployed to Azure
- [ ] Code review passed
- [ ] ADR compliance verified

**Inputs**: All implemented code
**Outputs**: Deployed feature, passing tests, clean code review

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Dataverse environment | Available | Low | Use dev environment |
| BFF API infrastructure | Available | Low | Existing deployment |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| WorkspaceShell | `src/client/shared/.../WorkspaceShell/` | Production |
| WizardDialog | `src/client/shared/.../WizardShell/` | Production |
| WorkspaceConfig types | `src/client/shared/.../WorkspaceShell/types.ts` | Production |
| workspaceConfig.tsx | `src/solutions/LegalWorkspace/src/workspaceConfig.tsx` | Production (to replace) |
| useFeedTodoSync | `src/solutions/LegalWorkspace/src/hooks/useFeedTodoSync.ts` | Production (to fix) |

---

## 6. Testing Strategy

**Unit Tests** (90% coverage target):
- Section registry completeness and factory output
- Dynamic config builder edge cases (missing sections, overflow, schema version)
- Layout template validation

**Integration Tests**:
- BFF endpoint CRUD operations
- Max 10 workspace enforcement
- System workspace immutability
- Default workspace toggle logic

**Manual Tests**:
- Wizard end-to-end flow (create, edit, Save As)
- Drag-and-drop in wizard Step 3
- Workspace switching via header dropdown
- Dark mode across wizard and header
- First-time user experience (banner)

---

## 7. Acceptance Criteria

### Phase 1:
- [ ] SectionRegistration and SectionFactoryContext interfaces exported from shared library
- [ ] Layout template constants defined

### Phase 2:
- [ ] All 5 sections render identically via registry factories
- [ ] SmartToDo works without ActivityFeed present

### Phase 3:
- [ ] All 8 BFF endpoints return correct responses
- [ ] Max 10 enforcement on POST
- [ ] System layouts read-only

### Phase 4:
- [ ] Dynamic config builder produces valid WorkspaceConfig from JSON + registry
- [ ] Header dropdown switches workspaces

### Phase 5:
- [ ] Wizard creates/edits workspaces end-to-end

### Phase 6:
- [ ] URL deep-linking works
- [ ] sessionStorage provides instant render

### Phase 7:
- [ ] All tests passing
- [ ] Deployed to dev environment

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|-------------|--------|------------|
| R1 | Section migration breaks existing behavior | Medium | High | Test each section individually; keep old factory as fallback |
| R2 | DnD library adds bundle size to wizard | Low | Medium | Use lightweight library (dnd-kit); single-file build keeps it contained |
| R3 | useFeedTodoSync fix causes regression in sync | Low | High | Test both scenarios: with and without ActivityFeed |
| R4 | Dataverse entity schema changes needed mid-project | Low | Medium | Schema versioning allows forward migration |

---

## 9. Next Steps

1. **Generate task files** from this plan (50-100+ tasks)
2. **Execute Phase 1** tasks (foundation types)
3. **Execute Phase 2-3** tasks in parallel (sections + API)
4. **Continue** through remaining phases

---

**Status**: Ready for Tasks
**Next Action**: Run task-create to generate POML task files

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
