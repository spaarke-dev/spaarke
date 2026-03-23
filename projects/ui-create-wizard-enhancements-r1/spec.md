# Create Wizard Enhancements R1 - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-03-23
> **Source**: design.md (16 enhancements from UAT feedback + runtime bug fixes)

## Executive Summary

Following the UI Dialog & Shell Standardization project (completed 2026-03-19), user acceptance testing revealed UX gaps, broken AI pipelines, responsive layout issues, and Code Page consolidation opportunities across the create wizard and workspace ecosystem. This project delivers 16 enhancements spanning wizard flow improvements, auth standardization, shared component extraction, and runtime bug fixes.

## Scope

### In Scope

**Wizard UX Enhancements (E-01, E-04, E-05, E-06, E-07, E-10)**
- Add "Associate To" first step to CreateMatter and CreateProject wizards
- Replace inline lookup dropdowns with Dataverse side pane (`Xrm.Utility.lookupObjects`)
- Rework follow-on steps: "Assign Work" (replaces "Assign Resources"), "Create Event" (replaces "AI Summary"), "Send Notification Email" (renamed from "Send Email to Client")
- Move Secure Project section to top of CreateProject "Enter Info" step

**Auth & API Pipeline Fixes (E-02, E-08, E-16)**
- Fix AI pre-fill pipeline: MSAL auth, bffBaseUrl propagation, error/retry UI
- Move analysis creation + N:N scope association to BFF API (Xrm.WebApi.execute unavailable in Code Page iframe)
- Investigate and resolve remaining AI pipeline issues (streaming, summary generation)

**Platform Standardization (E-03, E-09, E-11, E-14)**
- Standardize dialog sizing to 60%×70% across all wizard navigateTo calls
- Extract shared `WorkspaceShell` component into `@spaarke/ui-components` — responsive grid, square-aspect cards, configurable sections
- Remove duplicate title bars from SummarizeFiles wizard and Assign Work follow-on step
- Consolidate PlaybookLibrary + AnalysisBuilder into single Code Page (`sprk_playbooklibrary`)

**Summarize → Analysis Flow (E-15)**
- Create `sprk_document` records after summary step, pass to PlaybookLibrary
- Add document selector (single-select MVP) when multiple documents are passed

**Runtime Bug Fixes (E-12, E-13)**
- Fix `sprk_duedate` field name error on Quick Summary overdue badge (400 Bad Request)
- Fix double `/api/api/` prefix in SprkChat context-mappings URL (404)

**React 19 Upgrade (Code Pages only)**
- Upgrade all Code Page solutions from React 18 to React 19 (Fluent UI v9 supports `react <20.0.0`)
- PCF controls remain on React 16/17 (platform-provided, per ADR-022)

### Out of Scope

- CreateEvent, CreateTodo, CreateWorkAssignment wizard changes (separate project)
- FindSimilar enhancements
- Power Pages SPA wizard integration (separate project)
- New wizard creation (no new entity wizards)
- Ribbon/command bar button changes
- Event/Todo application updates (separate project)
- Multi-select document picker (future iteration of E-15)

### Affected Areas

- `src/client/shared/Spaarke.UI.Components/` — shared component library (WorkspaceShell, wizard components, analysisService, adapters)
- `src/solutions/LegalWorkspace/` — workspace refactor to consume WorkspaceShell
- `src/solutions/CreateMatterWizard/` — Code Page wrapper auth fix
- `src/solutions/CreateProjectWizard/` — Code Page wrapper auth fix
- `src/solutions/SummarizeFilesWizard/` — hideTitle + document creation on follow-on
- `src/solutions/PlaybookLibrary/` — merge AnalysisBuilder params, add document selector
- `src/solutions/AnalysisBuilder/` — RETIRE after migration
- `src/client/webresources/js/sprk_wizard_commands.js` — dialog sizing, bffBaseUrl, launch point updates
- `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` — new analysis create endpoint
- `src/server/shared/Spaarke.Dataverse/IAnalysisDataverseService.cs` — add scope association method
- SprkChat pane BFF client — fix double `/api/` prefix

## Requirements

### Functional Requirements

1. **FR-01** (E-01): CreateMatter and CreateProject wizards include an optional "Associate To" first step with record type dropdown and Dataverse lookup side pane. Users can skip association. — Acceptance: Wizard creates record and links via `sprk_Project_Matter_nn` when association selected; creates without link when skipped.

2. **FR-02** (E-02): All wizard Code Pages use MSAL (`@spaarke/auth`) as the canonical auth strategy for BFF API calls. `authenticatedFetch` acquires Bearer tokens via MSAL silent flow. `bffBaseUrl` is passed via navigateTo data params from all launch points. — Acceptance: AI pre-fill triggers after file upload; fields populated with AI badges; visible error banner with "Retry" if auth fails.

3. **FR-03** (E-03): All wizard navigateTo calls use `width: 60%, height: 70%` dialog dimensions. — Acceptance: CreateMatter, CreateProject, SummarizeFiles, PlaybookLibrary all open at 60%×70%.

4. **FR-04** (E-04): Matter Type, Practice Area, and Project Type fields use `Xrm.Utility.lookupObjects` side pane instead of inline search-as-you-type dropdowns. `INavigationService` extended with `openLookup()` method; Xrm adapter, mock adapter, and BFF adapter implement it. — Acceptance: Selecting a lookup field opens Dataverse side pane with search + recent records + "New" button.

5. **FR-05** (E-05): "Assign Resources" follow-on step replaced with "Assign Work" using full work assignment fields (Name, Description, Matter Type, Practice Area, Priority, Response Due Date, Assigned Attorney, Assigned Paralegal, Assigned Outside Counsel). Creates `sprk_workassignment` record linked via `sprk_workassignment_RegardingMatter_sprk_matter_n1` or `sprk_workassignment_RegardingProject_sprk_project_n1`. Priority defaults to "Normal"; Matter Type/Practice Area auto-filled from parent record. — Acceptance: Follow-on creates work assignment with correct relationship to parent Matter/Project.

6. **FR-06** (E-06): "AI Summary" follow-on card replaced with "Create Event". Opens inline event creation form (same fields as CreateEvent wizard). Creates `sprk_event` linked via matter/project relationship. — Acceptance: Follow-on creates event with correct relationship.

7. **FR-07** (E-07): "Send Email to Client" renamed to "Send Notification Email" in Next Steps card label, step sidebar label, and step content title. — Acceptance: Label reads "Send Notification Email" in all three locations.

8. **FR-08** (E-09): Workspace layout components extracted from `src/solutions/LegalWorkspace/` into `@spaarke/ui-components/components/WorkspaceShell/`. Components: WorkspaceShell, ActionCardRow, ActionCard, MetricCardRow, MetricCard, SectionPanel, types. Cards use CSS Grid with `aspect-ratio: 1` maintaining square proportions. `WorkspaceShell` accepts declarative config (action cards, metric cards, sections). LegalWorkspace refactored to consume WorkspaceShell. — Acceptance: Cards maintain square aspect ratio at all viewport widths; cards wrap to additional rows instead of stretching; LegalWorkspace renders identically using shared components.

9. **FR-09** (E-10): SecureProjectSection moved from below the form grid to above it in CreateProjectStep. — Acceptance: Secure Project toggle appears at the top of "Enter Info" step.

10. **FR-10** (E-11): SummarizeFiles wizard and Assign Work follow-on step render without their own title bar when opened as Dataverse dialogs (dialog chrome provides title). Set `hideTitle={true}` on WizardShell. — Acceptance: No duplicate title bar in any wizard dialog.

11. **FR-11** (E-12): Quick Summary overdue badge queries correct field name on `sprk_workassignment` (likely `sprk_responseduedatetime`). — Acceptance: OData query returns 200; overdue count displays correctly.

12. **FR-12** (E-13): SprkChat context-mappings URL uses single `/api/` prefix. — Acceptance: Request to `{bffBaseUrl}/ai/chat/context-mappings` returns 200 (not 404).

13. **FR-13** (E-14): PlaybookLibrary and AnalysisBuilder consolidated into single Code Page (`sprk_playbooklibrary`). Accepts union of parameters: `intent`, `documentId`/`documentIds`, `documentName`, `apiBaseUrl`, `entityType`/`entityId`. Close behavior auto-detected. `src/solutions/AnalysisBuilder/` deleted. All launch points updated. — Acceptance: Single Code Page handles all playbook/analysis launch contexts; `sprk_analysisbuilder` web resource no longer exists.

14. **FR-14** (E-15): Summarize wizard "Work on Analysis" follow-on creates `sprk_document` records from uploaded files (using business unit's `sprk_containerid`), then opens PlaybookLibrary with document IDs. PlaybookLibrary displays document selector at top when multiple documents passed. Single-select for MVP. — Acceptance: User can summarize files, click "Work on Analysis", select one document, choose a playbook, and analysis runs without error.

15. **FR-15** (E-16): Analysis creation and N:N scope association (skills, knowledge, tools) moved to BFF API endpoint `POST /api/ai/analysis/create`. Frontend `analysisService.ts` calls BFF via `authenticatedFetch` instead of `Xrm.WebApi`. — Acceptance: Analysis runs from Code Page with correct scope associations; no "Xrm.WebApi.online.execute is not available" error.

16. **FR-16** (React 19): All Code Page solutions upgraded from React 18 to React 19. — Acceptance: All Code Pages build and render correctly with React 19; no runtime errors.

17. **FR-17** (E-17): Fix theme cascade in `resolveCodePageTheme()` and `getEffectiveDarkMode()` — remove OS `prefers-color-scheme` as the final fallback (level 4). When no explicit Spaarke theme preference is set (localStorage `spaarke-theme` = `'auto'` or absent), default to light mode. Theme resolution cascade becomes: (1) localStorage explicit preference, (2) URL flags, (3) Navbar DOM detection, (4) default light. The `setupCodePageThemeListener` and `setupThemeListener` should also stop listening to `prefers-color-scheme` media query changes. — Acceptance: Code Pages render in light mode by default; dark mode only when Spaarke theme menu is explicitly set to dark; OS dark mode setting has no effect.

18. **FR-18** (E-18): Replace all hard-coded colors with Fluent UI v9 semantic tokens across the codebase. Consolidate 6 duplicated `ThemeProvider.ts` files (in LegalWorkspace, EventsPage, CalendarSidePane, SpeAdminApp, EventDetailSidePane, TodoDetailSidePane) to use the shared `codePageTheme.ts`/`themeStorage.ts` utilities instead. Specific replacements:
    - `rgba(0,0,0,0.12)` → `tokens.colorNeutralStroke1` (borders/dividers)
    - `rgba(0,0,0,0.3)` / `rgba(0,0,0,0.5)` → `tokens.colorBackgroundOverlay` (overlays)
    - `#292929` / `#ffffff` body backgrounds → `tokens.colorNeutralBackground1`
    - `#d13438` / `#a4262c` error reds → `tokens.colorPaletteRedForeground1`
    - `#fde7e9` error backgrounds → `tokens.colorPaletteRedBackground1`
    - `#666` muted text → `tokens.colorNeutralForeground3`
    - `#195ABD` / `#2987E6` brand blues → `tokens.colorBrandForeground1` / `tokens.colorBrandForeground2`
    - `#444` / `#ddd` graph dots → theme-aware values via `tokens.colorNeutralStroke2`
    — Acceptance: `grep -r` for hex colors and `rgba(` in `.ts`/`.tsx` files returns zero results (excluding navbar detection constants, test fixtures, and type definition comments). All 6 duplicated ThemeProvider files replaced with shared utility imports.

### Non-Functional Requirements

- **NFR-01**: All UI must respect app-level theme only (not OS `prefers-color-scheme`). Default is light mode. Dark mode only when Dataverse app theme is set to dark (ADR-021).
- **NFR-02**: No console errors in browser DevTools from Spaarke code (Microsoft platform noise excluded).
- **NFR-03**: WorkspaceShell must be responsive from 768px to 2560px viewport width.
- **NFR-04**: MSAL token acquisition must complete within 2 seconds (silent flow); error banner shown if auth fails.
- **NFR-05**: Document creation on "Work on Analysis" follow-on must complete within 3 seconds for up to 10 files.

## Technical Constraints

### Applicable ADRs

- **ADR-001**: Minimal API pattern — new `POST /api/ai/analysis/create` endpoint follows minimal API conventions
- **ADR-006**: Code Pages for standalone dialogs — all wizard work uses Code Page pattern (not custom pages + PCF wrappers)
- **ADR-008**: Endpoint filters for authorization — new analysis create endpoint uses endpoint filters
- **ADR-012**: Shared component library — WorkspaceShell, AssociateToStep, wizard components in `@spaarke/ui-components`
- **ADR-013**: AI Architecture — pre-fill pipeline and analysis creation extend BFF, not separate service
- **ADR-021**: Fluent UI v9 — all UI must use Fluent v9; no hard-coded colors; dark mode required
- **ADR-022**: PCF React 16 platform libs — Code Pages bundle their own React (now 19); PCF stays on platform-provided React 16/17

### MUST Rules

- ✅ MUST use MSAL as the single canonical auth path for all Code Page → BFF API calls (no postMessage relay fallback)
- ✅ MUST use `@spaarke/ui-components` for all shared wizard and workspace components
- ✅ MUST use Fluent UI v9 exclusively (no v8 mixing)
- ✅ MUST support dark mode for all new/changed components
- ✅ MUST use endpoint filters for authorization on new BFF endpoints
- ✅ MUST use concrete types for DI registrations (ADR-010)
- ❌ MUST NOT use `Xrm.WebApi.online.execute` from Code Page iframes (not available)
- ❌ MUST NOT use `fetch.bind(window)` for authenticated BFF calls (no credentials in iframe context)
- ❌ MUST NOT use React 18 `createRoot` API changes that break React 19 compatibility
- ❌ MUST NOT bundle React in PCF controls (platform-provided only)
- ❌ MUST NOT use OS `prefers-color-scheme` as theme fallback — Spaarke theme menu (`localStorage spaarke-theme`) is the only user preference source; default to light when unset
- ❌ MUST NOT use hard-coded hex colors (`#xxx`), `rgb()`, or `rgba()` in component styles — use Fluent v9 `tokens.*` exclusively

### Existing Patterns to Follow

- `src/solutions/DocumentUploadWizard/` — canonical Code Page auth pattern (MSAL + authenticatedFetch)
- `src/solutions/DocumentUploadWizard/AssociateToStep.tsx` — Associate To step with Xrm frame-walking
- `src/client/shared/Spaarke.UI.Components/src/components/CreateWorkAssignment/` — follow-on step pattern (Create Event, Assign Work)
- `src/client/shared/Spaarke.UI.Components/src/components/Playbook/analysisService.ts` — N:N association pattern (adapt for BFF)
- `.claude/patterns/` — endpoint, service, and component patterns

## Architecture

```
Layer 1: @spaarke/ui-components (shared library)
├── components/WorkspaceShell/       → NEW: extracted workspace layout (E-09)
│   ├── WorkspaceShell.tsx           → Responsive grid layout
│   ├── ActionCardRow.tsx            → Square action cards (Get Started)
│   ├── MetricCardRow.tsx            → Square metric cards (Quick Summary)
│   ├── SectionPanel.tsx             → Titled section with toolbar/badge
│   └── types.ts                     → Config interfaces
├── components/AssociateToStep/      → NEW: shared associate step (E-01)
├── components/CreateMatterWizard/   → Add AssociateToStep, rework follow-ons
├── components/CreateProjectWizard/  → Same + Secure Project moved to top (E-10)
├── components/SummarizeFilesWizard/ → hideTitle on WizardShell (E-11)
├── components/Playbook/             → analysisService → BFF API calls (E-16)
├── types/serviceInterfaces.ts       → Add openLookup to INavigationService
├── utils/adapters/                  → Add openLookup to Xrm adapter

Layer 2: Code Page wrappers (auth fix + consolidation + React 19)
├── CreateMatterWizard/main.tsx      → MSAL auth + bffBaseUrl
├── CreateProjectWizard/main.tsx     → MSAL auth + bffBaseUrl
├── SummarizeFilesWizard/main.tsx    → Document creation on follow-on (E-15)
├── PlaybookLibrary/main.tsx         → Merge AnalysisBuilder params + doc selector (E-14)
├── AnalysisBuilder/                 → RETIRE (E-14)
├── All Code Pages                   → React 18 → React 19 upgrade

Layer 3: Consumers (sizing + workspace refactor)
├── LegalWorkspace/                  → Consume WorkspaceShell (E-09)
├── sprk_wizard_commands.js          → Dialog size + bffBaseUrl + launch point updates
├── ActionCardHandlers.ts            → Dialog size
├── SprkChat pane                    → Fix double /api/ prefix (E-13)

Layer 4: BFF API (new endpoint)
├── AnalysisEndpoints.cs             → POST /api/ai/analysis/create (E-16)
├── IAnalysisDataverseService.cs     → Add scope association method
```

## Wizard Step Sequences (After Enhancement)

### CreateMatter Wizard

| Step | Title | Content | Changes |
|------|-------|---------|---------|
| 1 | Associate To | Record type dropdown + Dataverse lookup | **NEW** (E-01) |
| 2 | Add Files | Drag-and-drop upload + AI pre-fill | Auth fix (E-02) |
| 3 | Enter Info | Matter form fields | Lookups use side pane (E-04) |
| 4 | Next Steps | Follow-on action cards | Cards: Create Event, Assign Work, Send Notification Email |
| 4a | Create Event | Event creation form (conditional) | **NEW** — replaces AI Summary (E-06) |
| 4b | Assign Work | Work assignment form (conditional) | **REWORKED** — replaces Assign Resources (E-05) |
| 4c | Send Notification Email | Email composition (conditional) | Renamed (E-07) |

### CreateProject Wizard

Same sequence with:
- Step 3: Secure Project section at TOP of form (E-10)
- Associate To: entity types = Account, Matter (not Account, Project)

### Summarize → Analysis Flow

| Step | Action | System Behavior |
|------|--------|-----------------|
| 1 | User uploads files | Files stored in SPE container (business unit's `sprk_containerid`) |
| 2 | Summarize runs | Aggregate AI summary across all files |
| 3 | User clicks "Work on Analysis" | System creates `sprk_document` records (inherits business unit `sprk_containerid`) |
| 4 | PlaybookLibrary opens | Shows "Choose Document to analyze" (single-select MVP) |
| 5 | User selects document + playbook | Analysis runs via BFF API (E-16) using existing 1:N pipeline |

## Relationship Schema Reference

| Relationship | Type | From | To | Schema Name |
|-------------|------|------|-----|-------------|
| Matter ↔ Project | N:N | sprk_matter | sprk_project | `sprk_Project_Matter_nn` |
| WorkAssignment → Matter | N:1 | sprk_workassignment | sprk_matter | `sprk_workassignment_RegardingMatter_sprk_matter_n1` |
| WorkAssignment → Project | N:1 | sprk_workassignment | sprk_project | `sprk_workassignment_RegardingProject_sprk_project_n1` |
| Event → Matter | N:1 | sprk_event | sprk_matter | (lookup field on sprk_event) |
| Event → Project | N:1 | sprk_event | sprk_project | (lookup field on sprk_event) |
| Analysis → Document | N:1 | sprk_analysis | sprk_document | `sprk_documentid` lookup |
| Analysis ↔ Skill | N:N | sprk_analysis | sprk_analysisskill | `sprk_analysis_skill` |
| Analysis ↔ Knowledge | N:N | sprk_analysis | sprk_analysisknowledge | `sprk_analysis_knowledge` |
| Analysis ↔ Tool | N:N | sprk_analysis | sprk_analysistool | `sprk_analysis_tool` |

## Success Criteria

1. [ ] "Associate To" step works with Dataverse lookup side pane — Verify: create Matter with and without association
2. [ ] AI pre-fill triggers after file upload with MSAL auth — Verify: fields populated with AI badges; error banner on auth failure
3. [ ] All dialogs open at 60%×70% — Verify: measure dialog dimensions for each wizard
4. [ ] Lookup fields use Dataverse side pane — Verify: Matter Type, Practice Area, Project Type open side pane
5. [ ] "Assign Work" follow-on creates `sprk_workassignment` with correct relationship — Verify: record exists in Dataverse with link
6. [ ] "Create Event" follow-on creates `sprk_event` with correct relationship — Verify: record exists in Dataverse with link
7. [ ] "Send Notification Email" label consistent in all locations — Verify: visual inspection
8. [ ] WorkspaceShell cards maintain square aspect ratio at all viewport widths — Verify: resize browser from 768px to 2560px
9. [ ] Secure Project section at top of CreateProject "Enter Info" step — Verify: visual inspection
10. [ ] No duplicate title bars in any wizard dialog — Verify: SummarizeFiles + Assign Work
11. [ ] Quick Summary overdue badge loads without 400 error — Verify: DevTools network tab
12. [ ] SprkChat context-mappings URL has single `/api/` prefix — Verify: DevTools network tab shows 200
13. [ ] Single `sprk_playbooklibrary` Code Page handles all launch contexts — Verify: workspace cards, "+Analysis", wizard follow-ons
14. [ ] Summarize → Analysis flow: documents created, selector shown, analysis runs — Verify: end-to-end flow
15. [ ] Analysis scope associations (skills/knowledge/tools) created via BFF API — Verify: no Xrm.WebApi error; check Dataverse N:N records
16. [ ] All Code Pages build and run on React 19 — Verify: `npm run build` succeeds; no runtime errors
17. [ ] Dark mode renders correctly for all new/changed components — Verify: toggle dark mode
18. [ ] No Spaarke console errors in DevTools — Verify: clean console on workspace load and wizard flows
19. [ ] Business unit `sprk_containerid` used for document creation in Summarize flow — Verify: created documents reference correct container

## Dependencies

### Prerequisites

- UI Dialog & Shell Standardization project completed (✅ done 2026-03-19)
- All create wizards are standalone Code Pages launched via `navigateTo` (✅ done)
- `@spaarke/auth` MSAL strategy exists in DocumentUploadWizard (✅ reference implementation)
- `WizardShell` supports `hideTitle` prop (✅ already implemented)

### External Dependencies

- Fluent UI v9 compatibility with React 19 (✅ confirmed: peerDependencies `react <20.0.0`)
- MSAL.js v2+ for silent token acquisition in iframe context
- Correct Dataverse schema field name for work assignment due date (verify during E-12 implementation)

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Auth strategy | MSAL only or postMessage fallback? | MSAL as single canonical path — no redundancy unless required for solution flexibility. Two paths hide issues. | Single auth implementation; clear error on failure |
| WorkspaceShell React version | Must work in PCF (React 16) or Code Page only? | Code Page only (React 19) | Can use modern React APIs; no React 16 constraints |
| React version | Stay on React 18 or upgrade to 19? | Upgrade to React 19 — it's been stable 15+ months, Fluent UI supports it | All Code Pages upgrade; PCF stays React 16/17 per ADR-022 |
| SPE container for Summarize | Which container for orphaned documents? | User's business unit has `sprk_containerid` field; all records inherit it on create | Use business unit container; confirm inheritance in build |
| Duplicate title bars | Only SummarizeFiles affected? | Also Assign Work follow-on step | E-11 scope expanded to both |
| Work assignment fields | Same as standalone wizard or reduced? | Same fields | No field reduction for follow-on context |
| AnalysisBuilder launch points | Other launch points besides ribbon/wizard? | No other launch points currently | Clean migration to `sprk_playbooklibrary` |

## Assumptions

- `sprk_responseduedatetime` is the correct field name for work assignment due date (verify during E-12)
- Other wizard Code Pages (FindSimilar, PlaybookLibrary) may also have duplicate title bars — audit during implementation, fix if found
- Document Profile playbooks auto-trigger on `sprk_document` creation (existing behavior, not new)
- Business unit `sprk_containerid` inheritance is automatic on record create (confirm in build)

## Unresolved Questions

- [ ] Should "Associate To" default to the entity being viewed when launched from a form ribbon? — Does not block: implement without default, enhance later if needed
- [ ] Should "Create Event" auto-populate event name/date from parent matter context? — Does not block: implement with empty fields, enhance if feedback requests it
- [ ] Should "Assign Work" support creating multiple work assignments per wizard run? — Does not block: single assignment for MVP

---

*AI-optimized specification. Original design: design.md*
