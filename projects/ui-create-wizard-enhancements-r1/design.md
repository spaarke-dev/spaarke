# Create Wizard Enhancements — Design Document

> **Author**: Ralph Schroeder
> **Date**: March 20, 2026
> **Status**: Draft
> **Context**: Follows UI Dialog & Shell Standardization project (completed 2026-03-19). All create wizards are now standalone Code Pages launched via `navigateTo`. This project enhances the extracted wizards with UX improvements and fixes identified during initial deployment testing.

---

## 1. Problem Statement

Testing of the newly deployed create wizard Code Pages revealed several UX gaps and a critical integration bug:

1. **No record association step** — CreateMatter and CreateProject wizards launch without context about which parent record to associate with. The Document Upload wizard has an "Associate To" first step that should be adopted.
2. **AI pre-fill not triggering** — The "Add Files" step uploads documents but does not trigger the AI playbook that extracts key information to pre-fill form fields. Investigation reveals multiple root causes (see Section 5).
3. **Inconsistent dialog sizing** — CreateMatter/CreateProject open at 85%×85% while DocumentUpload opens at 60%×70%. All creation wizards should use consistent sizing.
4. **Inline search dropdowns** — Matter Type, Practice Area, and Project Type lookups use inline search-as-you-type dropdowns. Users expect the standard Dataverse side pane lookup (same as "Associate To" uses `Xrm.Utility.lookupObjects`).
5. **Follow-on step misalignment** — The "Assign Resources" step should become "Assign Work" with full work assignment fields (matching the standalone CreateWorkAssignment wizard). The "AI Summary" follow-on card should be replaced with "Create Event".
6. **Label inconsistency** — "Send Email to Client" should be "Send Notification Email" for clarity.

---

## 2. Scope

### In Scope

| ID | Enhancement | Affected Components |
|----|------------|-------------------|
| **E-01** | Add "Associate To" first step to CreateMatter and CreateProject wizards | CreateMatterWizard, CreateProjectWizard (shared lib) |
| **E-02** | Fix AI pre-fill: auth tokens, bffBaseUrl, error visibility | Code Page wrappers, useAiPrefill hook, CreateRecordWizard |
| **E-03** | Standardize dialog size to 60%×70% | WorkspaceGrid.tsx, sprk_wizard_commands.js |
| **E-04** | Replace inline lookup dropdowns with `Xrm.Utility.lookupObjects` side pane | CreateRecordStep (Matter), CreateProjectStep |
| **E-05** | Rename "Assign Resources" → "Assign Work", add work assignment fields, create sprk_workassignment record with relationship | CreateMatterWizard, CreateProjectWizard follow-on steps |
| **E-06** | Replace "AI Summary" follow-on card with "Create Event" (matching CreateWorkAssignment pattern) | Follow-on step config in CreateMatterWizard, CreateProjectWizard |
| **E-07** | Rename "Send Email to Client" → "Send Notification Email" | Follow-on step labels |
| **E-08** | Investigate and resolve any other AI pipeline issues (streaming, summary generation) | matterService.ts, projectService.ts, BFF endpoints |
| **E-09** | Extract shared `WorkspaceShell` component library from LegalWorkspace — standardized responsive grid with square-aspect action/metric cards, configurable sections. Fixes current responsive issues and enables future workspaces to reuse the layout | Extract from LegalWorkspace → `@spaarke/ui-components`, then refactor LegalWorkspace to consume |
| **E-10** | Move Secure Project section to top of "Enter Info" step in CreateProject wizard | CreateProjectStep.tsx |
| **E-11** | Remove duplicate title bar (title + close button) from SummarizeFiles wizard — Dataverse dialog chrome already provides this | SummarizeFilesDialog.tsx (set `hideTitle={true}` on WizardShell) |
| **E-12** | Fix `sprk_duedate` field name error — Quick Summary queries a non-existent field on `sprk_workassignment`, causing 400 Bad Request. Correct to actual schema field name (likely `sprk_responseduedatetime`) | WorkspaceGrid.tsx or QuickSummary data service |
| **E-13** | Fix double `/api/api/` prefix in SprkChat context-mappings URL — BFF base URL already includes `/api`, code prepends it again resulting in 404 | SprkChat pane BFF client configuration |
| **E-14** | Consolidate PlaybookLibrary and AnalysisBuilder into a single Code Page (`sprk_playbooklibrary`). Both are thin wrappers around the same `PlaybookLibraryShell` component — merge parameter handling, retire `sprk_analysisbuilder` | PlaybookLibrary/main.tsx, AnalysisBuilder/ (retire) |
| **E-15** | Summarize → Analysis flow: create `sprk_document` records after summary step (so GUIDs are available), pass document IDs to PlaybookLibrary. Add document selector (single-select MVP) at top of PlaybookLibrary when multiple documents are passed | SummarizeFilesDialog follow-on, PlaybookLibraryShell |

### Out of Scope

- CreateEvent, CreateTodo, CreateWorkAssignment wizard changes (separate concern)
- FindSimilar enhancements
- Power Pages SPA wizard integration (separate project handles SPA)
- New wizard creation (no new entity wizards)
- Ribbon/command bar button changes

---

## 3. Requirements

### E-01: Associate To First Step

**User Story**: As a user launching "Create Matter" or "Create Project" from the workspace, I want to optionally select a parent record so the new record is automatically linked.

**Behavior**:
- New first wizard step titled "Associate To"
- Record Type dropdown with applicable entity types:
  - For CreateMatter: Account, Project (link via `sprk_Project_Matter_nn`)
  - For CreateProject: Account, Matter (link via `sprk_Project_Matter_nn`)
- "Select Record" button opens Dataverse lookup side pane via `Xrm.Utility.lookupObjects`
- Selected record displayed as confirmation card with clear option
- "Skip" button available — association is optional (can create without parent)
- After record creation, association is made via the N:N relationship `sprk_Project_Matter_nn`
- SPE container ID resolved from selected record or business unit fallback

**Technical Pattern**: Follow `DocumentUploadWizard/AssociateToStep.tsx` pattern with Xrm frame-walking for `lookupObjects` access.

### E-02: Fix AI Pre-fill Pipeline

**Root Cause Analysis** (from investigation):

| Issue | Cause | Fix |
|-------|-------|-----|
| **No auth token** | `fetch.bind(window)` has no credentials in navigateTo iframe | Use `@spaarke/auth` MSAL flow or relay parent Xrm auth token |
| **Empty bffBaseUrl** | Not passed in navigateTo `data` param | Pass `bffBaseUrl` from workspace/ribbon callers |
| **Silent 401/403** | `useAiPrefill` logs warning but shows no UI error | Add error banner in wizard UI |
| **No retry** | `skipIfInitialized` prevents re-attempt | Make retryable after config fix |

**Fix Strategy**:
1. Workspace and ribbon command handlers must pass `bffBaseUrl` in the navigateTo data string
2. Code Page wrappers must resolve auth: either relay parent Xrm token via `postMessage`, or use the existing `@spaarke/auth` MSAL strategy that DocumentUploadWizard already uses (it works because it was originally a standalone Code Page)
3. Add a visible error/retry banner in the "Enter Info" step when pre-fill fails
4. Remove the `skipIfInitialized` single-attempt guard (or add manual retry button)

**Reference**: DocumentUploadWizard uses `authenticatedFetch` from `@spaarke/auth/strategies/MsalSilentStrategy` which acquires BFF tokens via MSAL. This same approach should be adopted by all wizard Code Pages.

### E-03: Dialog Size Standardization

**Change**: All wizard `navigateTo` calls use `width: 60%, height: 70%` (matching DocumentUpload).

**Files to update**:
- `src/solutions/LegalWorkspace/src/components/Shell/WorkspaceGrid.tsx` — all wizard navigateTo calls
- `src/client/webresources/js/sprk_wizard_commands.js` — `DIALOG_OPTIONS` constant
- `src/solutions/LegalWorkspace/src/components/GetStarted/ActionCardHandlers.ts` — playbook intent dialogs

### E-04: Lookup Fields Use Dataverse Side Pane

**Change**: Replace inline `<LookupField>` search-as-you-type for reference data with `Xrm.Utility.lookupObjects`:
- Matter Type → `Xrm.Utility.lookupObjects({ entityTypes: ["sprk_mattertype_ref"] })`
- Practice Area → `Xrm.Utility.lookupObjects({ entityTypes: ["sprk_practicearea_ref"] })`
- Project Type → `Xrm.Utility.lookupObjects({ entityTypes: ["sprk_projecttype_ref"] })`

**Rationale**: Users are accustomed to the Dataverse lookup experience (side pane with search + recent records). Inline dropdowns feel inconsistent and lack the "New" button for creating reference records on the fly.

**Technical Approach**:
- Add `openLookup(entityTypes, defaultEntityType)` method to `INavigationService`
- Xrm adapter implements via `Xrm.Utility.lookupObjects`
- Mock adapter returns test data
- BFF adapter (SPA) uses custom lookup dialog component

### E-05: "Assign Work" Step with Work Assignment Fields

**Change**: The "Assign Resources" follow-on step becomes "Assign Work":
- Title: "Assign Work"
- Fields match CreateWorkAssignment wizard: Name*, Description, Matter Type, Practice Area, Priority*, Response Due Date*, Assigned Attorney, Assigned Paralegal, Assigned Outside Counsel
- Creates `sprk_workassignment` record linked via:
  - For Matter: `sprk_workassignment_RegardingMatter_sprk_matter_n1`
  - For Project: `sprk_workassignment_RegardingProject_sprk_project_n1`
- Replaces the previous lightweight "assign attorney/paralegal/counsel" step

### E-06: Replace "AI Summary" with "Create Event"

**Change**: The follow-on Next Steps cards become:
1. ~~Draft AI Summary~~ → **Create Event** (same pattern as CreateWorkAssignment's follow-on)
2. **Assign Work** (renamed from "Assign Resources")
3. **Send Notification Email** (renamed from "Send Email")

The "Create Event" card:
- Opens an inline event creation form (same fields as CreateEvent wizard)
- Creates `sprk_event` record linked via the matter/project relationship
- Follows the pattern already implemented in CreateWorkAssignment wizard's `CreateFollowOnEventStep`

### E-07: Send Notification Email Label

**Change**: Rename "Send Email to Client" → "Send Notification Email" in:
- Next Steps card label
- Step sidebar label
- Step content title

### E-09: Extract Shared WorkspaceShell Component

**Problem**: The workspace layout (responsive grid, action cards, metric cards, titled sections) is currently hard-coded in `src/solutions/LegalWorkspace/`. When the screen size decreases, Get Started and Quick Summary cards stretch to full-width rectangles instead of maintaining square aspect ratio. More importantly, this layout is not reusable — any future workspace would need to duplicate and maintain the same grid, card, and section code.

**Current state**:
- `LegalWorkspace` builds to `corporateworkspace.html` (deployed as `sprk_corporateworkspace` web resource)
- Layout components (WorkspaceGrid, GetStartedRow, ActionCard, QuickSummaryRow, QuickSummaryMetricCard, section panels) are all local to `src/solutions/LegalWorkspace/`
- Cards use `flex: 1 1 0` with fixed `height: 120px` — no aspect ratio constraint, no consistent wrapping

**Goal**: Extract a shared `WorkspaceShell` component into `@spaarke/ui-components` that any workspace can consume with configuration. Fix the responsive behavior as part of the extraction.

**Shared component structure**:

```
@spaarke/ui-components/components/WorkspaceShell/
├── WorkspaceShell.tsx        — overall responsive grid layout
├── ActionCardRow.tsx         — row of square action cards (Get Started pattern)
├── ActionCard.tsx            — individual action card (icon + label, square aspect)
├── MetricCardRow.tsx         — row of square metric cards (Quick Summary pattern)
├── MetricCard.tsx            — individual metric card (icon + count + label + optional badge)
├── SectionPanel.tsx          — titled section with optional toolbar, count badge, content area
└── types.ts                  — IWorkspaceConfig, IActionCard, IMetricCard, ISectionConfig
```

**Key design decisions**:
- Cards use CSS Grid with `grid-template-columns: repeat(auto-fill, minmax(120px, 1fr))` and `aspect-ratio: 1` to maintain square proportions and wrap naturally
- `WorkspaceShell` accepts a declarative config: action cards, metric cards, and named sections
- Each workspace provides its own content components for sections (Latest Updates, To Do, Documents, etc.) — the shell handles layout only
- LegalWorkspace is refactored to consume `WorkspaceShell` as a thin configuration wrapper

**Consumer pattern** (how LegalWorkspace would use it):

```typescript
import { WorkspaceShell } from "@spaarke/ui-components";

<WorkspaceShell
  title="Legal Operations Workspace"
  actionCards={[
    { icon: <AddSquare />, label: "Create New Matter", onClick: handleCreateMatter },
    { icon: <ProjectAdd />, label: "Create New Project", onClick: handleCreateProject },
    // ...
  ]}
  metricCards={[
    { icon: <Scales />, count: 31, label: "My Matters", onClick: handleMatters },
    { icon: <Projects />, count: 9, label: "My Projects", onClick: handleProjects },
    { icon: <Tasks />, count: 13, label: "Open Tasks", badge: { count: 9, label: "Overdue", variant: "danger" } },
    // ...
  ]}
  sections={[
    { title: "Latest Updates", badge: 34, content: <LatestUpdatesPanel /> },
    { title: "To Do", content: <TodoPanel /> },
    { title: "Documents", content: <DocumentsPanel /> },
  ]}
  onSettingsClick={handleSettings}
  onNotificationsClick={handleNotifications}
/>
```

**Refactor approach**:
1. Extract layout components from LegalWorkspace into `@spaarke/ui-components`
2. Add responsive grid with `aspect-ratio: 1` cards (fixes current responsive issues)
3. Refactor LegalWorkspace to consume `WorkspaceShell` — section content components stay in LegalWorkspace
4. Verify responsive behavior at multiple breakpoints

### E-10: Move Secure Project to Top of Enter Info Step

**Problem**: In the CreateProject wizard, the "Secure Project" toggle section is positioned at the bottom of the "Enter Info" step, below the description textarea. Users should see and decide on project security before filling in other fields, since enabling it triggers SPE container provisioning which affects the project's document storage.

**Change**: Move `<SecureProjectSection>` from after the form grid to before it in `CreateProjectStep.tsx`. The section keeps its existing divider, lock icon, toggle, and expandable info panel — only its position changes.

**File**: `src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/CreateProjectStep.tsx`

### E-11: Remove Duplicate Title Bar from SummarizeFiles Wizard

**Problem**: The SummarizeFiles wizard renders its own title bar (title text + "X" close button) via `WizardShell`. When opened as a Dataverse dialog via `navigateTo`, the dialog chrome already provides a title bar with close button, resulting in a duplicate header.

**Change**: Pass `hideTitle={true}` to the `WizardShell` component in `SummarizeFilesDialog.tsx`. The WizardShell already supports this prop (line 172) — it conditionally renders the `titleBar` div based on `!hideTitle`.

**File**: `src/client/shared/Spaarke.UI.Components/src/components/SummarizeFilesWizard/SummarizeFilesDialog.tsx`

### E-12: Fix Work Assignment Due Date Field Name

**Problem**: The Quick Summary section queries `sprk_workassignment` with `$filter=sprk_duedate lt {now}` to count overdue items. Dataverse returns 400 Bad Request: "Could not find a property named 'sprk_duedate' on type 'Microsoft.Dynamics.CRM.sprk_workassignment'". The "9 Overdue" badge on Open Tasks may be showing incorrect data or failing silently.

**Fix**: Identify the correct schema name for the due date field (likely `sprk_responseduedatetime` based on E-05 field definitions) and update the OData query.

### E-13: Fix Double `/api/` Prefix in SprkChat Context Mappings URL

**Problem**: The SprkChat pane makes a request to `https://spe-api-dev-67e2xz.azurewebsites.net/api/api/ai/chat/context-mappings` — note the duplicate `/api/api/`. The BFF base URL (`bffBaseUrl`) already includes the `/api` path segment, and the client code prepends `/api` again when constructing the endpoint URL, resulting in a 404.

**Fix**: Remove the extra `/api` prefix from the SprkChat client's endpoint path construction so the final URL is `{bffBaseUrl}/ai/chat/context-mappings`.

### E-14: Consolidate PlaybookLibrary + AnalysisBuilder Code Pages

**Problem**: Two separate Code Pages (`sprk_playbooklibrary` and `sprk_analysisbuilder`) both wrap the same shared `PlaybookLibraryShell` component. They differ only in parameter handling and close behavior. Maintaining two thin wrappers for the same component adds deployment and testing overhead.

**Change**: Merge into a single Code Page (`sprk_playbooklibrary`) that accepts the union of both parameter sets:

| Parameter | Source | Behavior |
|-----------|--------|----------|
| `intent` | Workspace action cards | Pre-selects playbook, enters intent wizard mode |
| `documentId` / `documentIds` | "+Analysis" button, Summarize follow-on | Sets document context for analysis |
| `documentName` | "+Analysis" button | Display name for single-document context |
| `apiBaseUrl` | All launch paths | BFF API endpoint (required for analysis execution) |
| `entityType` / `entityId` | Ribbon commands, form context | Entity scoping |

**Close behavior**: Detect whether opened via `Xrm.Navigation.navigateTo` (use `navigationService.closeDialog()`) or standalone (use `window.close()`). No separate wrapper needed.

**Retire**: Delete `src/solutions/AnalysisBuilder/` after migration. Update all launch points that currently reference `sprk_analysisbuilder` to use `sprk_playbooklibrary` with the same params.

### E-15: Summarize → Analysis Flow (MVP)

**Problem**: In the Summarize wizard, the "Work on Analysis" follow-on opens the PlaybookLibrary but fails because no `sprk_document` records exist — files were uploaded to SPE but never registered as Dataverse document records.

**Solution flow**:

```
1. User uploads files          → Files stored in SPE default container (business unit)
2. Summarize wizard runs       → Aggregate AI summary across all files
3. User clicks "Work on        → System creates sprk_document records from uploaded files
   Analysis" follow-on            (GUIDs now available, Document Profile playbooks auto-trigger)
4. PlaybookLibrary opens       → Top section: "Choose Document to analyze"
   with document IDs              showing list of the just-created documents
5. User selects ONE document   → Single-select for MVP
6. User selects playbook       → Analysis runs on selected document
                                  using existing 1:N sprk_analysis pipeline
```

**Document creation timing**: Documents are created at step 3 (not during upload) so that the summary results from step 2 can be written to the document fields (`sprk_filesummary`, `sprk_filetldr`) at creation time rather than requiring a separate update.

**Document selector component**: A simple list at the top of `PlaybookLibraryShell` shown when multiple `documentIds` are passed. Each item shows file name, type icon, and file size. Single-select radio buttons for MVP. Can be expanded to multi-select (checkbox) in a future iteration — each selected document would get its own `sprk_analysis` record via the existing 1:N relationship.

**No schema changes required** — uses existing `sprk_documentid` lookup on `sprk_analysis`.

---

## 4. Technical Approach

### Architecture

All changes stay within the existing three-layer model:

```
Layer 1: @spaarke/ui-components (shared library changes)
├── components/WorkspaceShell/       → NEW: extracted workspace layout (E-09)
│   ├── WorkspaceShell.tsx           → Responsive grid layout
│   ├── ActionCardRow.tsx            → Square action cards (Get Started)
│   ├── MetricCardRow.tsx            → Square metric cards (Quick Summary)
│   ├── SectionPanel.tsx             → Titled section with toolbar/badge
│   └── types.ts                     → Config interfaces
├── components/CreateMatterWizard/   → Add AssociateToStep, rework follow-ons
├── components/CreateProjectWizard/  → Same changes + Secure Project moved to top (E-10)
├── components/SummarizeFilesWizard/ → hideTitle on WizardShell (E-11)
├── types/serviceInterfaces.ts       → Add openLookup to INavigationService
├── utils/adapters/                  → Add openLookup to Xrm adapter

Layer 2: Code Page wrappers (auth fix + consolidation)
├── CreateMatterWizard/main.tsx      → Fix authenticatedFetch + bffBaseUrl
├── CreateProjectWizard/main.tsx     → Same fix
├── PlaybookLibrary/main.tsx         → Merge AnalysisBuilder params into single wrapper (E-14)
├── AnalysisBuilder/                 → RETIRE after migration (E-14)

Layer 3: Consumers (sizing + param fix + workspace refactor)
├── LegalWorkspace/                  → Refactor to consume WorkspaceShell (E-09)
├── SummarizeFilesDialog             → Create documents on "Work on Analysis" follow-on (E-15)
├── sprk_wizard_commands.js          → Dialog size + bffBaseUrl + point to sprk_playbooklibrary
├── ActionCardHandlers.ts            → Dialog size
```

### Shared "AssociateToStep" Component

Since both CreateMatter and CreateProject need the same "Associate To" step, create a shared `AssociateToStep` component in the shared library (reusable, not duplicated):

```typescript
// @spaarke/ui-components/components/AssociateToStep/
interface IAssociateToStepProps {
  entityTypes: { logicalName: string; displayName: string }[];
  onRecordSelected: (record: ISelectedRecord | null) => void;
  navigationService: INavigationService;  // for openLookup
  dataService: IDataService;              // for container resolution
  optional?: boolean;                     // show "Skip" option
}
```

### Auth Fix Pattern

Adopt the DocumentUploadWizard's MSAL strategy for all wizard Code Pages:

```typescript
// In Code Page main.tsx:
import { initAuth, getAuthenticatedFetch } from "@spaarke/auth";

// Initialize MSAL at Code Page load
await initAuth({ clientId: "...", authority: "..." });
const authenticatedFetch = getAuthenticatedFetch();

// Pass to wizard component
<CreateMatterWizard authenticatedFetch={authenticatedFetch} bffBaseUrl={BFF_URL} />
```

---

## 5. AI Pre-fill Investigation Detail

### Current Flow (Broken)

```
Code Page loads in navigateTo iframe
  → main.tsx passes fetch.bind(window) as authenticatedFetch
  → CreateRecordWizard renders Add Files step
  → User uploads files
  → useAiPrefill fires POST to {bffBaseUrl}/workspace/matters/pre-fill
  → bffBaseUrl is empty string → relative URL → wrong server
  → OR fetch has no auth → 401 from BFF
  → Hook sets status='error', logs console.warn
  → User sees nothing — form fields stay empty
```

### Fixed Flow (Target)

```
Code Page loads in navigateTo iframe
  → main.tsx initializes MSAL auth (like DocumentUploadWizard)
  → main.tsx resolves bffBaseUrl from runtime config or env var
  → authenticatedFetch = MSAL-backed fetch with Bearer token
  → CreateRecordWizard renders Add Files step
  → User uploads files
  → useAiPrefill fires POST to https://spe-api-dev-67e2xz.azurewebsites.net/api/workspace/matters/pre-fill
  → BFF returns extracted data
  → Hook calls onApply → form fields populated
  → User sees AI badge on pre-filled fields
  → If error: visible banner with "Retry" button
```

### BFF Endpoints Required

| Wizard | Pre-fill Endpoint | Auth |
|--------|-------------------|------|
| CreateMatter | `POST /workspace/matters/pre-fill` | Bearer (MSAL) |
| CreateProject | `POST /workspace/projects/pre-fill` | Bearer (MSAL) |
| CreateMatter | `POST /workspace/matters/ai-summary` (SSE) | Bearer (MSAL) |

---

## 6. Relationship Schema Reference

| Relationship | Type | From | To | Schema Name |
|-------------|------|------|-----|-------------|
| Matter ↔ Project | N:N | sprk_matter | sprk_project | `sprk_Project_Matter_nn` |
| WorkAssignment → Matter | N:1 | sprk_workassignment | sprk_matter | `sprk_workassignment_RegardingMatter_sprk_matter_n1` |
| WorkAssignment → Project | N:1 | sprk_workassignment | sprk_project | `sprk_workassignment_RegardingProject_sprk_project_n1` |
| Event → Matter | N:1 | sprk_event | sprk_matter | (lookup field on sprk_event) |
| Event → Project | N:1 | sprk_event | sprk_project | (lookup field on sprk_event) |

---

## 7. Updated Wizard Step Sequence

### CreateMatter Wizard (After Enhancement)

| Step | Title | Content | Changes |
|------|-------|---------|---------|
| 1 | Associate To | Record type dropdown + lookup | **NEW** |
| 2 | Add Files | Drag-and-drop file upload + AI pre-fill trigger | Fix auth (E-02) |
| 3 | Enter Info | Matter form fields (name, type, practice area, etc.) | Lookups use side pane (E-04); for CreateProject: Secure Project section moved to top (E-10) |
| 4 | Next Steps | Follow-on action card selection | Cards: Create Event, Assign Work, Send Notification Email (E-05, E-06, E-07) |
| 4a | Create Event | Event creation form (conditional) | **NEW** (replaces AI Summary) |
| 4b | Assign Work | Work assignment creation form (conditional) | **REWORKED** (replaces Assign Resources) |
| 4c | Send Notification Email | Email composition (conditional) | Renamed (E-07) |

### CreateProject Wizard (After Enhancement)

Same step sequence, adapted for project entity context.

---

## 8. Success Criteria

- [ ] "Associate To" step works with Dataverse lookup side pane
- [ ] AI pre-fill triggers after file upload (fields populated with AI badges)
- [ ] All dialogs open at 60%×70% size
- [ ] Matter Type, Practice Area, Project Type use Dataverse lookup side pane
- [ ] "Assign Work" step creates sprk_workassignment with correct relationship
- [ ] "Create Event" follow-on card creates sprk_event with correct relationship
- [ ] "Send Notification Email" label consistent throughout
- [ ] Get Started and Quick Summary cards maintain square aspect ratio when viewport narrows
- [ ] Both card sections wrap to additional rows instead of stretching to rectangles
- [ ] Secure Project section appears at the top of CreateProject "Enter Info" step
- [ ] SummarizeFiles wizard has no duplicate title bar when opened as Dataverse dialog
- [ ] No console errors in browser DevTools
- [ ] Dark mode renders correctly for all new/changed steps

---

## 9. Risk Assessment

| Risk | Impact | Mitigation |
|------|--------|-----------|
| MSAL token acquisition fails in iframe | AI pre-fill broken | Fall back to parent Xrm token relay via postMessage |
| `Xrm.Utility.lookupObjects` not available in all contexts | Lookup side pane doesn't open | Fall back to inline search-as-you-type |
| N:N relationship association requires separate API call | Create + associate not atomic | Use transaction or retry with rollback guidance |
| Work assignment fields add complexity to wizard | User overwhelm | Make fields optional where possible, provide defaults |

---

## 10. Open Questions

1. **Should "Associate To" default to the entity being viewed?** — If launching from a Matter form (via ribbon), should it pre-select "Matter" and the current record?
2. **Should "Create Event" copy any fields from the parent?** — Auto-populate event name/date from matter context?
3. **Is the N:N relationship `sprk_Project_Matter_nn` bidirectional?** — Need to confirm association direction.
4. **Should the "Assign Work" step support multiple work assignments?** — Or just one per wizard run?

---

*This design should be reviewed before proceeding to `/design-to-spec` and `/project-pipeline`.*
