# Project Plan: UI Create Wizard Enhancements R1

> **Last Updated**: 2026-03-23
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)

---

## 1. Executive Summary

**Purpose**: Deliver 18 post-UAT enhancements to the create wizard and workspace ecosystem — wizard flow improvements, MSAL auth standardization, shared component extraction, Code Page consolidation, theme/color token compliance, and runtime bug fixes.

**Scope**:
- Associate To step with Dataverse lookup side pane
- MSAL auth standardization across all Code Pages
- WorkspaceShell extraction to shared library
- PlaybookLibrary + AnalysisBuilder consolidation
- BFF API analysis create endpoint
- Theme cascade fix + hard-coded color replacement
- React 19 upgrade for all Code Pages
- Runtime bug fixes (overdue badge, SprkChat URL)

**Estimated Effort**: ~80-100 hours across 6 phases

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-001**: New `POST /api/ai/analysis/create` uses Minimal API pattern
- **ADR-006**: All wizard UI as Code Pages (not custom pages + PCF wrappers)
- **ADR-008**: New BFF endpoint uses endpoint filter for authorization
- **ADR-010**: DI registration via feature module; concrete types unless seam needed
- **ADR-012**: All shared components in `@spaarke/ui-components`; IDataService/INavigationService abstractions
- **ADR-013**: AI features extend BFF, not separate service; analysis via BFF API
- **ADR-021**: Fluent v9 exclusively; semantic tokens only; dark mode required; no hard-coded colors
- **ADR-022**: Code Pages bundle React 19; PCF stays React 16/17 platform-provided

**From Spec**:
- MSAL as single canonical auth path (no postMessage relay fallback)
- WorkspaceShell is Code Page only (React 19) — no PCF consumption needed
- Business unit `sprk_containerid` for document creation in Summarize flow
- Single-select document picker for MVP (not multi-select)
- No OS `prefers-color-scheme` in theme cascade — default to light

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Add `openLookup()` to INavigationService | Xrm.Utility.lookupObjects needs abstraction for testability | New method on interface + 3 adapters |
| Move analysis N:N associations to BFF | Xrm.WebApi.execute not available in Code Page iframe | New BFF endpoint + frontend service rewrite |
| Extract WorkspaceShell (Code Pages only) | Responsive grid with square-aspect cards is reusable | New shared component, LegalWorkspace refactor |
| Consolidate PlaybookLibrary + AnalysisBuilder | Both serve same purpose with slightly different params | One Code Page, one web resource |
| Remove OS prefers-color-scheme fallback | App-level theme is the only source of truth | Theme cascade simplification |
| Replace all hard-coded colors | ADR-021 compliance; dark mode breaks with hard-coded values | Systematic codebase sweep |

### Discovered Resources

**Applicable ADRs**:
- `.claude/adr/ADR-001-minimal-api.md` — Minimal API endpoint pattern
- `.claude/adr/ADR-006-pcf-over-webresources.md` — Code Page as default surface
- `.claude/adr/ADR-008-endpoint-filters.md` — Authorization via endpoint filters
- `.claude/adr/ADR-010-di-minimalism.md` — Concrete DI registrations
- `.claude/adr/ADR-012-shared-components.md` — Shared component library, IDataService pattern
- `.claude/adr/ADR-013-ai-architecture.md` — AI architecture, BFF extension pattern
- `.claude/adr/ADR-021-fluent-design-system.md` — Fluent v9, tokens, dark mode
- `.claude/adr/ADR-022-pcf-platform-libraries.md` — PCF React 16/17, Code Page React 19

**Applicable Patterns**:
- `.claude/patterns/api/endpoint-definition.md` — BFF endpoint creation
- `.claude/patterns/api/endpoint-filters.md` — Authorization filter implementation
- `.claude/patterns/api/service-registration.md` — DI feature module pattern
- `.claude/patterns/auth/msal-client.md` — MSAL auth in Code Pages
- `.claude/patterns/pcf/dialog-patterns.md` — Dialog opening via navigateTo
- `.claude/patterns/pcf/theme-management.md` — Theme detection and management
- `.claude/patterns/ai/analysis-scopes.md` — N:N analysis scope associations
- `.claude/patterns/webresource/custom-dialogs-in-dataverse.md` — Dialog sizing

**Applicable Scripts**:
- `scripts/Deploy-PCFWebResources.ps1` — PCF deployment
- `scripts/Test-SdapBffApi.ps1` — BFF API validation
- `scripts/Deploy-SpeAdminApp.ps1` — Code Page deployment pattern

**Existing Code References**:
- `src/solutions/DocumentUploadWizard/` — Canonical MSAL auth pattern
- `src/solutions/DocumentUploadWizard/AssociateToStep.tsx` — Associate To reference
- `src/client/shared/Spaarke.UI.Components/src/components/CreateWorkAssignment/` — Follow-on step pattern
- `src/client/shared/Spaarke.UI.Components/src/components/Playbook/analysisService.ts` — N:N scope associations
- `src/client/shared/Spaarke.UI.Components/src/utils/codePageTheme.ts` — Theme utilities

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Foundation & Bug Fixes (Tasks 001-009)
├── Theme cascade fix (E-17)
├── Hard-coded color replacement (E-18)
├── Runtime bug fixes (E-12, E-13)
├── Dialog sizing standardization (E-03)
├── React 19 upgrade for all Code Pages
└── Send Notification Email rename (E-07)

Phase 2: Shared Library Extensions (Tasks 010-019)
├── INavigationService.openLookup() + adapters (E-04)
├── AssociateToStep shared component (E-01)
├── WorkspaceShell extraction (E-09)
├── Duplicate title bar fixes (E-11)
└── Secure Project position (E-10)

Phase 3: BFF API & Auth (Tasks 020-029)
├── POST /api/ai/analysis/create endpoint (E-16)
├── analysisService.ts rewrite for BFF (E-08)
├── MSAL auth standardization (E-02)
└── bffBaseUrl propagation

Phase 4: Wizard Flow Enhancements (Tasks 030-039)
├── CreateMatter Associate To integration (E-01)
├── CreateProject Associate To integration (E-01)
├── Assign Work follow-on (E-05)
├── Create Event follow-on (E-06)
└── Lookup side pane integration (E-04)

Phase 5: Consolidation & Document Flow (Tasks 040-049)
├── PlaybookLibrary + AnalysisBuilder merge (E-14)
├── Summarize → Analysis document creation (E-15)
├── Document selector in PlaybookLibrary (E-15)
├── LegalWorkspace WorkspaceShell consumption (E-09)
└── Launch point updates

Phase 6: Integration & Wrap-up (Tasks 050-059)
├── End-to-end integration testing
├── Cross-wizard regression testing
├── Deployment verification
└── Project wrap-up (090)
```

### Critical Path

**Blocking Dependencies:**
- Phase 1 (theme/color/React 19) MUST complete before Phase 2-5 (all UI work builds on clean token base)
- Phase 2 (INavigationService.openLookup) BLOCKS Phase 4 (wizard lookup integration)
- Phase 3 (BFF endpoint) BLOCKS Phase 5 (analysis flow from frontend)
- Phase 2 (AssociateToStep) BLOCKS Phase 4 (wizard integration)
- Phase 2 (WorkspaceShell) BLOCKS Phase 5 (LegalWorkspace refactor)

**High-Risk Items:**
- MSAL silent flow in Code Page iframe — Mitigation: DocumentUploadWizard reference implementation
- React 19 upgrade across all Code Pages — Mitigation: Fluent v9 supports `react <20.0.0`
- Hard-coded color sweep completeness — Mitigation: grep-based acceptance criterion

---

## 4. Phase Breakdown

### Phase 1: Foundation & Bug Fixes

**Objectives:**
1. Fix theme cascade to remove OS prefers-color-scheme fallback (E-17)
2. Replace all hard-coded colors with Fluent v9 tokens (E-18)
3. Consolidate 6 duplicated ThemeProvider files
4. Fix runtime bugs (overdue badge E-12, SprkChat URL E-13)
5. Standardize dialog sizing to 60%x70% (E-03)
6. Upgrade all Code Pages to React 19
7. Rename "Send Email to Client" to "Send Notification Email" (E-07)

**Deliverables:**
- [ ] `resolveCodePageTheme()` and `getEffectiveDarkMode()` updated (no OS fallback)
- [ ] All hard-coded colors replaced with `tokens.*`
- [ ] 6 ThemeProvider files replaced with shared utility imports
- [ ] Overdue badge field name fixed
- [ ] SprkChat double /api/ prefix fixed
- [ ] All dialog navigateTo calls use 60%x70%
- [ ] All Code Page package.json files updated to React 19
- [ ] Email label renamed in 3 locations

**Inputs**: Existing Code Page solutions, shared library theme utils, sprk_wizard_commands.js
**Outputs**: Updated theme utilities, token-based styles, React 19 builds, bug fixes

### Phase 2: Shared Library Extensions

**Objectives:**
1. Add `openLookup()` to INavigationService interface + all adapters
2. Create AssociateToStep shared component
3. Extract WorkspaceShell from LegalWorkspace to shared library
4. Add hideTitle support to SummarizeFiles and Assign Work
5. Move Secure Project section to top in CreateProjectStep

**Deliverables:**
- [ ] `INavigationService.openLookup()` + Xrm, BFF, mock adapters
- [ ] `@spaarke/ui-components/components/AssociateToStep/`
- [ ] `@spaarke/ui-components/components/WorkspaceShell/` (WorkspaceShell, ActionCardRow, ActionCard, MetricCardRow, MetricCard, SectionPanel, types)
- [ ] hideTitle applied to WizardShell in SummarizeFiles and Assign Work
- [ ] SecureProjectSection reordered in CreateProjectStep

**Inputs**: LegalWorkspace source, DocumentUploadWizard AssociateToStep reference
**Outputs**: New shared components, updated service interfaces

### Phase 3: BFF API & Auth

**Objectives:**
1. Create `POST /api/ai/analysis/create` BFF endpoint
2. Add scope N:N association method to IAnalysisDataverseService
3. Rewrite analysisService.ts to call BFF API via authenticatedFetch
4. Standardize MSAL auth across all wizard Code Pages
5. Ensure bffBaseUrl propagation from all launch points

**Deliverables:**
- [ ] `AnalysisEndpoints.cs` with POST endpoint + endpoint filter
- [ ] `IAnalysisDataverseService` scope association method
- [ ] Updated `analysisService.ts` using BFF API
- [ ] All Code Page main.tsx files using MSAL authenticatedFetch
- [ ] sprk_wizard_commands.js passing bffBaseUrl in data params

**Inputs**: Existing AnalysisEndpoints.cs, analysisService.ts, DocumentUploadWizard auth pattern
**Outputs**: New BFF endpoint, updated frontend services, MSAL auth in all Code Pages

### Phase 4: Wizard Flow Enhancements

**Objectives:**
1. Integrate AssociateToStep into CreateMatter and CreateProject wizards
2. Implement Assign Work follow-on step (replaces Assign Resources)
3. Implement Create Event follow-on step (replaces AI Summary)
4. Integrate Dataverse lookup side pane for Matter Type, Practice Area, Project Type
5. Update step sequences and sidebar labels

**Deliverables:**
- [ ] CreateMatter wizard with Associate To step 1
- [ ] CreateProject wizard with Associate To step 1
- [ ] Assign Work follow-on creating `sprk_workassignment` with relationships
- [ ] Create Event follow-on creating `sprk_event` with relationships
- [ ] Lookup fields opening Dataverse side pane

**Inputs**: AssociateToStep shared component, INavigationService.openLookup, existing wizard components
**Outputs**: Enhanced wizard flows with new steps and follow-ons

### Phase 5: Consolidation & Document Flow

**Objectives:**
1. Merge PlaybookLibrary + AnalysisBuilder into single Code Page
2. Implement Summarize → Analysis document creation flow
3. Add document selector to PlaybookLibrary
4. Refactor LegalWorkspace to consume WorkspaceShell
5. Update all launch points and retire AnalysisBuilder

**Deliverables:**
- [ ] Single `sprk_playbooklibrary` Code Page handling all contexts
- [ ] `sprk_analysisbuilder` web resource deleted
- [ ] Document creation in Summarize "Work on Analysis" follow-on
- [ ] Document selector UI (single-select) in PlaybookLibrary
- [ ] LegalWorkspace consuming WorkspaceShell shared component
- [ ] All launch points updated

**Inputs**: Existing PlaybookLibrary, AnalysisBuilder, LegalWorkspace, Summarize wizard
**Outputs**: Consolidated Code Pages, document flow, workspace refactor

### Phase 6: Integration & Wrap-up

**Objectives:**
1. End-to-end testing of all enhanced wizard flows
2. Cross-wizard regression testing
3. Deployment verification in Dataverse
4. Documentation updates

**Deliverables:**
- [ ] All 19 success criteria verified
- [ ] No console errors from Spaarke code
- [ ] All Code Pages deployed and functional
- [ ] Project completion documentation

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Fluent UI v9 + React 19 | GA | Low | peerDependencies confirmed |
| MSAL.js v2+ in iframe | GA | Low | Reference impl exists |
| Xrm.Utility.lookupObjects | GA | Low | Standard Dataverse API |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| WizardShell component | `src/client/shared/Spaarke.UI.Components/` | Production |
| CreateRecordWizard | `src/client/shared/Spaarke.UI.Components/` | Production |
| IDataService / INavigationService | `src/client/shared/Spaarke.UI.Components/src/types/` | Production |
| DocumentUploadWizard (MSAL ref) | `src/solutions/DocumentUploadWizard/` | Production |
| AnalysisEndpoints.cs | `src/server/api/Sprk.Bff.Api/Api/Ai/` | Production |
| codePageTheme.ts | `src/client/shared/Spaarke.UI.Components/src/utils/` | Production |

---

## 6. Testing Strategy

**Unit Tests** (90%+ coverage for shared components):
- WorkspaceShell responsive behavior
- AssociateToStep state management
- INavigationService.openLookup() mock adapter
- analysisService BFF API calls
- Theme resolution without OS fallback

**Integration Tests**:
- BFF analysis create endpoint with scope associations
- MSAL auth flow in Code Page context
- Document creation in Summarize flow

**E2E / Manual Tests**:
- Full wizard flows (CreateMatter, CreateProject, SummarizeFiles)
- Associate To + lookup side pane interaction
- Follow-on steps (Assign Work, Create Event, Send Email)
- PlaybookLibrary from all launch contexts
- Dark mode rendering across all changed components
- Responsive WorkspaceShell from 768px to 2560px

---

## 7. Acceptance Criteria

See [spec.md](spec.md) Success Criteria section (19 items).

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | React 19 upgrade breaks Code Page rendering | Low | High | Fluent v9 supports react <20.0.0; test incrementally |
| R2 | MSAL silent flow fails in Dataverse iframe | Medium | High | Reference impl in DocumentUploadWizard; error banner + retry |
| R3 | Hard-coded color grep misses patterns | Low | Medium | Systematic search; acceptance criterion is zero matches |
| R4 | Xrm.Utility.lookupObjects UX differs by Dataverse version | Low | Medium | Test in target environment; fall back to inline if needed |
| R5 | WorkspaceShell responsive breaks at edge viewport widths | Medium | Medium | CSS Grid + aspect-ratio well-supported; test 768-2560px range |
| R6 | AnalysisBuilder retirement breaks undocumented launch points | Low | Medium | Search codebase for all references before deletion |

---

## 9. Next Steps

1. **Generate task files** from this plan
2. **Begin Phase 1** — theme/color foundation work
3. **Continue through phases** sequentially (with parallel tasks within phases)

---

**Status**: Ready for Tasks
**Next Action**: Run task-create to generate POML task files

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
