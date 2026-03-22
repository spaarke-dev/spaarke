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

### Out of Scope

- CreateEvent, CreateTodo, CreateWorkAssignment wizard changes (separate concern)
- SummarizeFiles, FindSimilar, PlaybookLibrary enhancements
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

---

## 4. Technical Approach

### Architecture

All changes stay within the existing three-layer model:

```
Layer 1: @spaarke/ui-components (shared library changes)
├── components/CreateMatterWizard/   → Add AssociateToStep, rework follow-ons
├── components/CreateProjectWizard/  → Same changes
├── types/serviceInterfaces.ts       → Add openLookup to INavigationService
├── utils/adapters/                  → Add openLookup to Xrm adapter

Layer 2: Code Page wrappers (auth fix)
├── CreateMatterWizard/main.tsx      → Fix authenticatedFetch + bffBaseUrl
├── CreateProjectWizard/main.tsx     → Same fix

Layer 3: Consumers (sizing + param fix)
├── WorkspaceGrid.tsx                → Dialog size + bffBaseUrl in data
├── sprk_wizard_commands.js          → Dialog size + bffBaseUrl in data
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
| 3 | Enter Info | Matter form fields (name, type, practice area, etc.) | Lookups use side pane (E-04) |
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
