# SDAP File Upload & Document Creation Dialog (R2) - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-03-09
> **Source**: projects/sdap-file-upload-document-r2/design.md

## Executive Summary

Migrate the Document creation-from-files workflow from a Custom Page + UniversalQuickCreate PCF anti-pattern to a standalone React 18 Code Page wizard dialog. The new dialog provides a guided 3-step experience (Add Files → Summary → Next Steps), automatically runs the Document Profile playbook, generates a search-optimized `sprk_searchprofile` field, and offers contextual follow-on actions (Send Email, Work on Analysis, Find Similar). Prerequisite shared component extraction enables reuse across LegalWorkspace, UniversalQuickCreate, and this new dialog.

## Scope

### In Scope

1. **Shared component extraction** — Move WizardShell, FileUploadZone, FindSimilarDialog, SendEmailStep, upload services, and useAiSummary from LegalWorkspace/UniversalQuickCreate to `src/client/shared/`
2. **Document Upload Wizard Code Page** — New `src/solutions/DocumentUploadWizard/` React 18 HTML web resource (`sprk_documentuploadwizard`)
3. **3-step wizard** — Add Files (upload + record creation + RAG indexing), Summary (Document Profile streaming), Next Steps (Send Email, Analysis Builder, Find Similar)
4. **Search profile integration** — Add `BuildSearchProfile` deterministic builder to `DocumentProfileFieldMapper`, mapping to `sprk_searchprofile` field
5. **Dynamic email step** — Inline Send Email wizard step with LookupField, pre-filled templates, Dataverse email activity creation
6. **Post-wizard actions** — Success screen buttons to open Analysis Builder (user picks document) and FindSimilarDialog (pre-loaded)
7. **Ribbon integration** — Update existing ribbon commands on `sprk_document` form to open the new wizard dialog
8. **Increased file limits** — Support larger files via chunked upload sessions (existing BFF endpoint `POST /api/containers/{id}/upload`)
9. **LegalWorkspace import update** — Update LegalWorkspace to import WizardShell, FindSimilar, EmailStep, FileUpload from shared instead of local
10. **UniversalQuickCreate import update** — Update PCF to import upload services from shared

### Out of Scope

- Changes to BFF API endpoints (all existing endpoints are reused as-is)
- Changes to the Document Profile playbook JPS definition (other than adding `searchProfile` output)
- Changes to the RAG indexing pipeline
- Changes to the Analysis Builder Code Page
- Mobile-responsive layout (desktop dialog only)
- User preference persistence across sessions (ship simple for v1)
- Removing the UniversalQuickCreate PCF itself (only the Custom Page wrapper is deprecated)
- Other entity types for search profile (handled by `ai-semantic-search-optimization-r1`)

### Affected Areas

- `src/client/shared/` — New shared components, services, hooks (extracted)
- `src/solutions/DocumentUploadWizard/` — New Code Page solution
- `src/solutions/LegalWorkspace/` — Import path updates (Wizard, FindSimilar, EmailStep, FileUpload)
- `src/client/pcf/UniversalQuickCreate/` — Import path updates (upload services)
- `src/server/api/Sprk.Bff.Api/Services/Ai/DocumentProfileFieldMapper.cs` — Add `sprk_searchprofile` mapping + `BuildSearchProfile` function
- `src/client/webresources/js/` — Ribbon command scripts to open new dialog
- `src/solutions/` — Dataverse solution XML for new web resource

## Requirements

### Functional Requirements

1. **FR-01**: Wizard dialog opens via `Xrm.Navigation.navigateTo` with `parentEntityType`, `parentEntityId`, `parentEntityName`, `containerId` URL parameters — Acceptance: Dialog opens correctly from any parent entity form (Matter, Project, Invoice, etc.)
2. **FR-02**: Step 1 (Add Files) supports drag-and-drop and file picker for selecting files with validation — Acceptance: Files validated against limits; file list displays name, size, remove button; "Related To" shows parent entity name
3. **FR-03**: On "Next" from Step 1, files are uploaded to SPE via `MultiFileUploadService`, `sprk_document` records created in Dataverse, and RAG indexing enqueued — Acceptance: All three operations complete; progress indicator shown per file; errors displayed inline
4. **FR-04**: Step 2 (Summary) displays streaming Document Profile results per document using `useAiSummary` hook — Acceptance: TL;DR, Document Type, Keywords shown as they stream in; completed profiles show expanded card; in-progress shows spinner
5. **FR-05**: Document Profile playbook writes `sprk_filesummary`, `sprk_filetldr`, `sprk_filekeywords`, `sprk_documenttype`, `sprk_entities`, and `sprk_searchprofile` to each `sprk_document` record — Acceptance: All 6 fields populated after profiling completes
6. **FR-06**: `sprk_searchprofile` is built deterministically by `BuildSearchProfile` function from the other profile outputs (no additional AI call) — Acceptance: Search profile contains document type, TL;DR, entity names, keywords, parent entity context, file name
7. **FR-07**: Step 3 (Next Steps) presents checkbox cards for Send Email, Work on Analysis, Find Similar — Acceptance: Multi-select; checking Send Email injects a dynamic wizard step with Skip button
8. **FR-08**: Send Email dynamic step pre-fills subject, body, and supports recipient lookup via `LookupField` searching `systemuser` table — Acceptance: Email saved as Dataverse email activity on the parent entity
9. **FR-09**: "Work on Analysis" on success screen shows document picker, then opens Analysis Builder Code Page for the selected document — Acceptance: `navigateTo` opens `sprk_analysisbuilder` with correct parameters
10. **FR-10**: "Find Similar" on success screen opens shared `FindSimilarDialog` with uploaded documents pre-loaded (no re-upload) — Acceptance: Tenant-wide semantic search across documents, matters, projects
11. **FR-11**: Success screen shows "{N} documents added" with document links, warnings for partial failures, and action buttons for selected next steps plus Close — Acceptance: All scenarios (full success, partial failure, no next steps) display correctly
12. **FR-12**: Larger files supported via chunked upload sessions — Acceptance: Files exceeding the single-PUT limit use `POST /api/containers/{id}/upload` + `PUT /api/upload-session/chunk` endpoints; specify new limits in implementation

### Non-Functional Requirements

- **NFR-01**: Dialog renders in both light and dark mode using Fluent UI v9 semantic tokens — no hard-coded colors
- **NFR-02**: File upload progress updates per-file in real-time (not batch)
- **NFR-03**: Document Profile streaming displays incremental results within 2 seconds of playbook start
- **NFR-04**: All SPE operations go through BFF API — no direct Graph API calls from client
- **NFR-05**: Code Page bundles React 18 — not dependent on Dataverse platform-provided React
- **NFR-06**: Shared components extracted to `src/client/shared/` are React 18-compatible; components also consumed by PCF must remain React 16/17-compatible

## Technical Constraints

### Applicable ADRs

- **ADR-006**: Standalone dialog → Code Page (not Custom Page + PCF wrapper). Ribbon scripts minimal (invocation only).
- **ADR-007**: All SPE operations through `SpeFileStore` facade via BFF API. No Graph SDK types in client.
- **ADR-012**: Reuse `@spaarke/ui-components`. Fluent v9 only. Semantic tokens. Dark mode. No PCF-specific APIs in shared components.
- **ADR-013**: AI calls through BFF endpoints only. No direct Azure OpenAI/Search calls from Code Page. Rate limiting on AI endpoints.
- **ADR-021**: Fluent UI v9 exclusively. `FluentProvider` with theme. `makeStyles` (Griffel) for styling. WCAG 2.1 AA.
- **ADR-022**: Code Pages bundle React 18 (`createRoot`). PCFs use platform React 16 (`ReactDOM.render`). Shared components must not use React 18-only APIs if consumed by PCF.

### MUST Rules

- MUST place Code Page in `src/solutions/DocumentUploadWizard/` (solutions pattern, not `src/client/code-pages/`)
- MUST use `@spaarke/auth` for MSAL authentication in Code Page context
- MUST use direct Dataverse OData HTTP calls for record creation (Code Pages don't have PCF `context.webAPI`)
- MUST preserve dynamic navigation property lookup for parent entity binding (case-sensitive `@odata.bind`)
- MUST upload to SPE AND enqueue RAG indexing for every file (dual pipeline non-negotiable)
- MUST run Document Profile playbook via `POST /api/ai/analysis/execute` (not hardcoded OpenAI)
- MUST build `sprk_searchprofile` deterministically from other profile outputs via `BuildSearchProfile`
- MUST support all parent entity types from day one (dynamic `parentEntityType` parameter)
- MUST keep ribbon scripts minimal — only open dialog, no business logic

### MUST NOT Rules

- MUST NOT use Custom Page + PCF wrapper pattern (ADR-006 violation)
- MUST NOT call Graph API directly from client (ADR-007)
- MUST NOT hard-code colors, use custom CSS, or mix Fluent UI versions (ADR-021)
- MUST NOT use React 18-only APIs in shared components consumed by PCF (ADR-022)
- MUST NOT make direct Azure AI service calls from frontend (ADR-013)
- MUST NOT add user preference persistence (out of scope for v1)
- MUST NOT create new BFF API endpoints (reuse existing)

### Existing Patterns to Follow

- WizardShell pattern: `src/solutions/LegalWorkspace/src/components/Wizard/WizardShell.tsx`
- Domain wizard: `src/solutions/LegalWorkspace/src/components/CreateMatter/WizardDialog.tsx`
- Dynamic steps + canonical ordering: Same WizardDialog.tsx (lines 315-422)
- Code Page entry: `src/solutions/LegalWorkspace/index.html` + `src/solutions/LegalWorkspace/src/main.tsx`
- Send Email step: `src/solutions/LegalWorkspace/src/components/CreateMatter/SendEmailStep.tsx`
- Find Similar dialog: `src/solutions/LegalWorkspace/src/components/FindSimilar/FindSimilarDialog.tsx`
- Upload services: `src/client/pcf/UniversalQuickCreate/control/services/`
- Document Profile field mapper: `src/server/api/Sprk.Bff.Api/Services/Ai/DocumentProfileFieldMapper.cs`
- Dialog patterns: `.claude/patterns/pcf/dialog-patterns.md`

## Success Criteria

1. [ ] Files uploaded to SPE and `sprk_document` records created in Dataverse — Verify: Upload 3 files; confirm SPE items + Dataverse records exist
2. [ ] Files indexed to Azure AI Search (dual-index) — Verify: Upload file; query knowledge + discovery indexes for matching chunks
3. [ ] Document Profile playbook runs and writes all 6 fields including `sprk_searchprofile` — Verify: Upload file; check all fields populated on `sprk_document` record
4. [ ] Send Email creates Dataverse email activity — Verify: Check email step; confirm email activity created with correct regarding and body
5. [ ] Work on Analysis opens Analysis Builder for user-selected document — Verify: Select document on success screen; confirm Analysis Builder opens with correct parameters
6. [ ] Find Similar opens shared dialog with pre-loaded documents — Verify: Click Find Similar; confirm search runs without re-upload
7. [ ] Dialog works in dark mode and light mode — Verify: Toggle theme; confirm no hard-coded colors or broken layouts
8. [ ] Shared components extracted and existing solutions updated — Verify: LegalWorkspace and UniversalQuickCreate build successfully with shared imports
9. [ ] Larger files upload via chunked sessions — Verify: Upload file exceeding single-PUT limit; confirm chunked upload completes
10. [ ] Old Custom Page ribbon commands updated to open new dialog — Verify: Click ribbon button on entity form; new wizard opens

## Dependencies

### Prerequisites

- Existing BFF API endpoints operational (upload, RAG indexing, analysis, playbooks)
- `@spaarke/auth` MSAL wrapper available for Code Page authentication
- Dataverse `sprk_searchprofile` field exists on `sprk_document` entity
- WizardShell components in LegalWorkspace are stable (no active refactoring)

### External Dependencies

- Azure AI Search indexes available for RAG indexing
- Document Profile playbook deployed in Dataverse (`sprk_playbook` record)
- SPE containers provisioned for target environments

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Service extraction | Extract to shared now or copy-and-adapt? | Extract to `src/client/shared/` day one | Both UniversalQuickCreate and new dialog import from shared; prevents drift |
| Email pattern | Separate dialog or inline wizard step? | Inline wizard step (same as Workspace playbooks) | SendEmailStep extracted to shared; dynamic step injection pattern |
| Find Similar scope | Tenant-wide or parent-entity-scoped? | Tenant-wide (same as Workspace); extract to shared | FindSimilarDialog moved to shared; search pipeline unchanged |
| Deprecation timeline | When to remove old Custom Page? | Keep 1 release as fallback, remove in R3 | R2: ship new dialog + update defaults; R3: remove Custom Page |
| Entity support | Matter only or all entities? | All parent entity types from day one | Dynamic nav property lookup already built; URL params handle it |
| AI Summary | Playbook or standalone OpenAI? | Playbook (JPS-based Document Profile) | Uses existing playbook orchestration; NOT hardcoded prompts |
| Next steps skip | Can user skip a selected next step? | Yes — Skip button on each dynamic step | `footerActions` prop with Skip button per dynamic step |
| Search profile | Separate job or integrated into playbook? | Integrated — deterministic `BuildSearchProfile` builder in `DocumentProfileFieldMapper` | Zero extra AI calls; assembles from other outputs; no dependency on search-optimization project |
| File limits | Keep 10MB limit? | Increase — support larger files via chunked uploads | Use existing chunked upload endpoints; specify new limits during implementation |
| Analysis next step | First doc or user picks? | User picks which document to analyze | Add document picker to success screen before opening Analysis Builder |
| User preferences | Remember settings across sessions? | No — ship simple for v1 | No localStorage or preference storage needed |

## Assumptions

- Existing BFF API endpoints do not need modification for this project
- WizardShell components are domain-free and can be moved to shared without interface changes
- The `sprk_searchprofile` field already exists on `sprk_document` in Dataverse
- Chunked upload session endpoints are functional and tested
- LegalWorkspace and UniversalQuickCreate can safely update import paths without breaking changes

## Unresolved Questions

- [ ] Exact new file size limits for chunked upload (needs profiling/discussion) — Blocks: FR-12 implementation details
- [ ] Whether `sprk_searchprofile` needs to be added to Quick Find View columns during this project or deferred to search-optimization — Blocks: Dataverse configuration task scoping

---

*AI-optimized specification. Original: projects/sdap-file-upload-document-r2/design.md*
