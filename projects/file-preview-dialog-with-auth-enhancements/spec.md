# File Preview Dialog with Auth Enhancements — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-03-09
> **Source**: file-preview-dialog-design.md

## Executive Summary

Consolidate 3 fragmented file preview implementations and 16 separate auth implementations into standardized, reusable components. Create a shared `@spaarke/auth` package (~500 lines) that replaces ~8,149 lines of duplicated auth code, build a `FilePreviewDialog` component for consistent document preview UX, replace the `UniversalQuickCreate` PCF with a React 18 `CreateDocumentDialog` code page, and migrate all remaining code pages and PCF controls to the shared auth package.

## Scope

### In Scope

**Phase 1 — `@spaarke/auth` Shared Package**
- Create `src/client/shared/Spaarke.Auth/` as a new npm package
- Implement 5-strategy token acquisition cascade: parent bridge → in-memory cache → Xrm platform (frame-walk) → MSAL ssoSilent → MSAL popup
- Implement `authenticatedFetch()` with 401 retry (exponential backoff, 3 attempts) and RFC 7807 ProblemDetails parsing
- Token bridge utilities: `publishToken()`, `readBridgeToken()`, `clearBridgeToken()`
- Environment-portable config (multi-tenant authority, `window.location.origin` redirect)
- In-memory primary + sessionStorage backup token caching with 5-min expiry buffer
- Optional proactive refresh (for long-lived pages like PlaybookBuilder)
- Unit tests for all core logic

**Phase 2 — `FilePreviewDialog` Component (LegalWorkspace)**
- Create `FilePreviewDialog.tsx` in `src/solutions/LegalWorkspace/src/components/FilePreview/`
- 4 toolbar actions: Open File, Open Record, Copy Link, Add to Workspace
- Fluent UI v9 Dialog (85vw × 85vh, max 880px) with iframe preview
- Loading, error, and success states
- Uses `@spaarke/auth` via LegalWorkspace's `authenticatedFetch()`

**Phase 3 — FilePreviewDialog Integration**
- Replace 3 action buttons in `FindSimilarResultsStep.tsx` with single preview button + dialog
- Replace popover preview + overflow menu in `DocumentCard.tsx` with preview button + dialog
- Consolidate ~140 lines of duplicated preview code

**Phase 4 — `CreateDocumentDialog` Code Page**
- New React 18 code page replacing `UniversalQuickCreate` PCF control
- WizardShell with steps: Upload Files → Document Details → Next Steps
- Uses `@spaarke/auth` + `authenticatedFetch()` (replaces 805-line PCF auth stack)
- Same BFF endpoints (no server changes): `PUT /api/obo/containers/{id}/files/{path}`, `GET /api/navmap/...`
- Feature-flagged parallel deployment alongside existing PCF

**Phase 5 — Code Page Auth Migration (Function-Based)**
- Migrate AnalysisWorkspace, PlaybookBuilder, SprkChatPane to `@spaarke/auth`
- Remove ~1,627 lines of duplicated auth code

**Phase 6 — Code Page Auth Migration (Class-Based)**
- Migrate SemanticSearch, DocumentRelationshipViewer code pages to `@spaarke/auth`
- Fix hardcoded tenant IDs and redirect URIs (environment portability)
- Add parent token bridge support
- Remove ~601 lines

**Phase 7 — PCF Auth Migration (Pilot)**
- Migrate SpeDocumentViewer, SpeFileViewer to `@spaarke/auth`
- Remove ~934 lines

**Phase 8 — PCF Auth Migration (Complete)**
- Migrate UniversalDatasetGrid, SemanticSearchControl, DocumentRelationshipViewer PCF, EmailProcessingMonitor, AnalysisWorkspace PCF
- Reconcile scope naming (`SDAP.Access` vs `user_impersonation`)
- Remove ~3,861 lines

### Out of Scope

- SemanticSearch code page preview panel replacement (future — different auth + positioned panel UX)
- DocumentRelationshipViewer preview button (future — requires auth adaptation)
- Office Add-in auth (NAA-specific, keeps own implementation; shares config types only)
- BFF server-side changes (all existing endpoints stay as-is)
- Server-side token caching / OBO optimization (separate concern)

### Affected Areas

- `src/client/shared/Spaarke.Auth/` — new shared auth package
- `src/solutions/LegalWorkspace/src/components/FilePreview/` — new FilePreviewDialog
- `src/solutions/LegalWorkspace/src/components/FindSimilar/FindSimilarResultsStep.tsx` — integration
- `src/solutions/LegalWorkspace/src/components/RecordCards/DocumentCard.tsx` — integration
- `src/solutions/LegalWorkspace/src/services/bffAuthProvider.ts` — replaced by `@spaarke/auth`
- `src/client/code-pages/CreateDocument/` — new code page (Phase 4)
- `src/client/code-pages/AnalysisWorkspace/src/services/authService.ts` — replaced (Phase 5)
- `src/client/code-pages/PlaybookBuilder/src/services/authService.ts` — replaced (Phase 5)
- `src/client/code-pages/SprkChatPane/src/services/authService.ts` — replaced (Phase 5)
- `src/client/code-pages/SemanticSearch/src/services/auth/` — replaced (Phase 6)
- `src/client/code-pages/DocumentRelationshipViewer/src/services/auth/` — replaced (Phase 6)
- `src/client/pcf/SpeDocumentViewer/control/AuthService.ts` — replaced (Phase 7)
- `src/client/pcf/SpeFileViewer/control/AuthService.ts` — replaced (Phase 7)
- `src/client/pcf/UniversalDatasetGrid/control/services/auth/` — replaced (Phase 8)
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/services/auth/` — replaced (Phase 8)
- `src/client/pcf/DocumentRelationshipViewer/DocumentRelationshipViewer/services/auth/` — replaced (Phase 8)
- `src/client/pcf/EmailProcessingMonitor/control/AuthService.ts` — replaced (Phase 8)
- `src/client/pcf/AnalysisWorkspace/control/services/auth/` — replaced (Phase 8)

## Requirements

### Functional Requirements

**@spaarke/auth Package**

1. **FR-01**: Token acquisition cascade — acquire BFF API token (`api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation`) using 5 strategies in priority order: parent bridge → in-memory cache → Xrm platform (with frame-walk across window → parent → top) → MSAL ssoSilent → MSAL popup. — Acceptance: Token acquired in <0.5ms via bridge, <5ms via cache, <300ms via Xrm, <800ms via MSAL ssoSilent, <3s via popup.

2. **FR-02**: `authenticatedFetch(url, init?)` — auto-attach Bearer header, retry on 401 with exponential backoff (3 attempts), parse RFC 7807 ProblemDetails errors, return typed response or throw `AuthError` / `ApiError`. — Acceptance: 401 triggers token refresh + retry; ProblemDetails parsed into typed error.

3. **FR-03**: Token bridge utilities — `publishToken(token)` sets `window.__SPAARKE_BFF_TOKEN__`, `readBridgeToken()` checks current + parent frames, `clearBridgeToken()` cleans up on logout/close. — Acceptance: Child iframe reads parent token in <0.1ms.

4. **FR-04**: Environment-portable config — authority `https://login.microsoftonline.com/organizations`, redirect URI `window.location.origin`, configurable via window globals (`__SPAARKE_MSAL_CLIENT_ID__`, `__SPAARKE_BFF_URL__`). — Acceptance: Works in any Dataverse environment without code changes.

5. **FR-05**: Optional proactive refresh — `initAuth({ proactiveRefresh: true })` starts 4-minute refresh interval. — Acceptance: Token refreshed before expiry on long-lived pages.

6. **FR-06**: Optional Xrm requirement — `initAuth({ requireXrm: true })` throws if Xrm is unavailable. — Acceptance: SprkChatPane fails fast outside Dataverse host.

**FilePreviewDialog**

7. **FR-07**: Open File action — fetch open links via `DocumentApiService.getDocumentOpenLinks(documentId)` (lazy, on click). Open desktop URL first; if unavailable, open web URL; if neither available, download file. — Acceptance: Correct URL opened based on availability cascade.

8. **FR-08**: Open Record action — open `sprk_document` Dataverse record in new tab via `navigateToEntity()`. — Acceptance: Record form opens in new browser tab.

9. **FR-09**: Copy Link action — copy Dataverse record URL (`{clientUrl}/main.aspx?etn=sprk_document&id={documentId}`) to clipboard. Show "Copied!" indicator for 2 seconds. Fallback to `document.execCommand('copy')` if Clipboard API unavailable. — Acceptance: URL copied to clipboard; visual confirmation shown.

10. **FR-10**: Add to / Remove from Workspace toggle — read `sprk_workspaceflag` from props or query on mount. If `true`, show `PinOffRegular` icon + "Remove from Workspace" label. If `false`, show `PinRegular` icon + "Add to Workspace" label. Toggle via `Xrm.WebApi.updateRecord()`. Call `onWorkspaceFlagChanged` callback after success. — Acceptance: Pin state toggles correctly; parent notified of change.

11. **FR-11**: Document preview — fetch preview URL via `DocumentApiService.getDocumentPreviewUrl(documentId)` on dialog open. Render in sandboxed iframe (`allow-scripts allow-same-origin allow-forms allow-popups`). Show Spinner during loading, error message + retry button on failure. — Acceptance: Preview renders for supported file types; errors handled gracefully.

12. **FR-12**: Dialog shell — Fluent UI Dialog, 85vw × 85vh (max 880px wide), title bar with document name + close button, ESC key closes dialog. — Acceptance: Dialog renders correctly, traps focus, closes on ESC.

**CreateDocumentDialog (Phase 4)**

13. **FR-13**: WizardShell with 3 steps — Upload Files (drag-and-drop with progress), Document Details (name, type, description, client lookups), Next Steps (optional follow-on actions). — Acceptance: All 3 steps navigate correctly; form validation works.

14. **FR-14**: File upload — `authenticatedFetch('PUT /api/obo/containers/{id}/files/{path}')` with multi-file support and progress tracking. — Acceptance: Files upload to SPE container with visible progress.

15. **FR-15**: Record creation — create `sprk_document` record via `Xrm.WebApi.createRecord()` or `authenticatedFetch()` with lookup associations via NavMap. — Acceptance: Document record created with correct relationships.

16. **FR-16**: Parallel deployment — run alongside existing UniversalQuickCreate PCF during validation period. Separate button or feature flag. — Acceptance: Both paths functional simultaneously.

**Auth Migration (Phases 5-8)**

17. **FR-17**: Per-component migration — replace each component's auth files with `import { initAuth, authenticatedFetch } from '@spaarke/auth'` (~10 lines per consumer). — Acceptance: Each migrated component authenticates and calls BFF API identically to before.

18. **FR-18**: Scope reconciliation — resolve `SDAP.Access` vs `user_impersonation` scope naming in AnalysisWorkspace PCF (Phase 8). — Acceptance: Single consistent scope name across all components.

### Non-Functional Requirements

- **NFR-01**: Token bridge latency ≤0.1ms (parent → child).
- **NFR-02**: MSAL ssoSilent latency ≤800ms (cold start).
- **NFR-03**: `@spaarke/auth` package size ≤15KB gzipped (no unnecessary dependencies).
- **NFR-04**: FilePreviewDialog renders within 200ms of open (excluding preview URL fetch).
- **NFR-05**: All UI components support light, dark, and high-contrast themes (ADR-021).
- **NFR-06**: WCAG 2.1 AA accessibility compliance (keyboard navigation, focus trapping, screen reader labels).
- **NFR-07**: Zero auth regressions — every migrated component must authenticate identically to before.

## Technical Constraints

### Applicable ADRs

- **ADR-006**: Use React Code Page for standalone dialogs (not PCF + custom page wrapper). Code Pages use React 18, bundled independently. PCF only for field-bound form controls.
- **ADR-008**: Use endpoint filters for resource authorization. No global auth middleware.
- **ADR-010**: DI minimalism — ≤15 non-framework registrations. Register concretes by default.
- **ADR-012**: Shared component library — use `@spaarke/ui-components` for reusable components. React 18-compatible authoring. Verify React 16/17 compatibility for PCF consumption. No PCF-specific APIs in shared code. No hard-coded Dataverse entity schemas.
- **ADR-021**: Fluent UI v9 exclusively. `FluentProvider` with theme at root. Semantic tokens only (no hard-coded colors). Support dark mode. PCF: `ReactDOM.render()`. Code Pages: `createRoot()`.
- **ADR-022**: PCF platform libraries — React 16 APIs only in PCF. Declare platform libs in `ControlManifest.Input.xml`. No bundled React/ReactDOM in PCF (<5MB).

### MUST Rules

- ✅ MUST use Fluent UI v9 (`@fluentui/react-components`) exclusively — no v8 mixing
- ✅ MUST use `createRoot()` in code pages (React 18)
- ✅ MUST use `ReactDOM.render()` in PCF controls (React 16 platform-provided)
- ✅ MUST support light, dark, and high-contrast themes
- ✅ MUST use multi-tenant authority (`organizations`) — no hardcoded tenant IDs
- ✅ MUST use `window.location.origin` for redirect URI — no hardcoded URLs
- ✅ MUST keep `@spaarke/auth` free of PCF-specific APIs (`ComponentFramework.*`)
- ✅ MUST keep `@spaarke/auth` free of hard-coded Dataverse entity schemas
- ✅ MUST test `@spaarke/auth` React 16/17 compatibility for PCF consumption
- ❌ MUST NOT use legacy JavaScript webresources
- ❌ MUST NOT bundle React/ReactDOM in PCF controls
- ❌ MUST NOT use global middleware for auth
- ❌ MUST NOT use Fluent UI v8 components
- ❌ MUST NOT hard-code colors (use design tokens)

### Existing Patterns to Follow

- See `src/solutions/LegalWorkspace/src/services/bffAuthProvider.ts` for auth pattern (template for `@spaarke/auth`)
- See `src/solutions/LegalWorkspace/src/services/DocumentApiService.ts` for BFF API service pattern
- See `src/solutions/LegalWorkspace/src/utils/navigation.ts` for `navigateToEntity()` utility
- See `.claude/patterns/webresource/custom-dialogs-in-dataverse.md` for dialog rendering pattern
- See `docs/architecture/sdap-auth-patterns.md` for all 7 auth patterns documented

## Success Criteria

1. [ ] `@spaarke/auth` package created with unit tests — Verify: `npm test` passes, all 5 strategies tested
2. [ ] `FilePreviewDialog` renders with all 4 toolbar actions — Verify: manual UAT in LegalWorkspace
3. [ ] FindSimilar integration: 3 buttons replaced by 1 preview button — Verify: manual UAT, ~60 lines removed
4. [ ] DocumentCard integration: popover replaced by dialog — Verify: manual UAT, ~80 lines removed
5. [ ] `CreateDocumentDialog` code page functional (upload + create record) — Verify: manual UAT alongside existing PCF
6. [ ] AnalysisWorkspace, PlaybookBuilder, SprkChatPane migrated — Verify: each authenticates and functions correctly
7. [ ] SemanticSearch, DocumentRelationshipViewer code pages migrated — Verify: works in non-dev environments (portability)
8. [ ] SpeDocumentViewer, SpeFileViewer PCF controls migrated — Verify: auth works in Dataverse forms
9. [ ] Remaining PCF controls migrated — Verify: all controls authenticate correctly
10. [ ] Scope reconciliation complete (`SDAP.Access` vs `user_impersonation`) — Verify: single scope across all components
11. [ ] ~8,149 lines of auth code removed — Verify: `git diff --stat` against baseline
12. [ ] Zero auth regressions — Verify: all components authenticate and call BFF API correctly

## Dependencies

### Prerequisites

- LegalWorkspace `bffAuthProvider.ts` serves as template for `@spaarke/auth` (Phase 1)
- `@spaarke/auth` must be complete before Phases 2-8 begin
- FilePreviewDialog (Phase 2) must be complete before integration (Phase 3)
- Phase 4 (CreateDocumentDialog) can run in parallel with Phase 3

### External Dependencies

- Azure AD app registration `api://1e40baad-e065-4aea-a8d4-4b7ab273458c` must exist with `user_impersonation` scope
- BFF API endpoints must be deployed and functional (no changes needed)
- Dataverse `sprk_document` entity must have `sprk_workspaceflag` field
- MSAL.js library (`@azure/msal-browser`) available as dependency

### Parallelization Strategy

Phases are designed for parallel execution via Claude Code agent teams:

| Parallel Group | Phases | Dependency |
|---------------|--------|------------|
| **Sequential** | Phase 1 | None — must complete first |
| **Parallel A** | Phase 2 → Phase 3 | Phase 1 complete |
| **Parallel B** | Phase 4 | Phase 1 complete |
| **Parallel C** | Phase 5 | Phase 1 complete |
| **Parallel D** | Phase 6 | Phase 1 complete |
| **Parallel E** | Phase 7 | Phase 1 complete |
| **Parallel F** | Phase 8 | Phase 1 complete |
| **Sequential** | Phase 7 (extract to shared lib) | Phases 2-6 complete |

## Owner Clarifications

*Answers captured during design-to-spec interview:*

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Project scope | Which phases are in scope? | All 8 phases; tasks should support parallel execution via agent teams | Full task decomposition across all phases; parallelization in task dependencies |
| Package location | Where should `@spaarke/auth` live? | New package at `src/client/shared/Spaarke.Auth/` | Separate npm package with own package.json, tsconfig, build pipeline |
| Open File fallback | What if desktop URL unavailable? | Desktop → web → download file | 3-tier cascade in Open File handler |
| Workspace toggle | How does pin/unpin work? | If `sprk_workspaceflag` is `true`, show "Remove from Workspace" | Read flag from props or query on mount; toggle state determines icon + label |
| Title bar metadata | Show file size/type/date? | No — title bar shows document name only | Clean, simple title bar |
| Testing strategy | What level of testing? | Unit tests for `@spaarke/auth`; manual UAT for UI components | Unit test suite for shared package; manual verification for dialog + migrations |

## Assumptions

*Proceeding with these assumptions (owner did not specify):*

- **MSAL version**: Assuming `@azure/msal-browser` v3.x (current in codebase) — will verify during Phase 1
- **`@spaarke/auth` build tool**: Assuming TypeScript + tsup/rollup for library bundling (consistent with `@spaarke/ui-components`)
- **CreateDocumentDialog location**: Building as embedded LegalWorkspace component first, with standalone code page option for Phase 7 extraction
- **Feature flag for CreateDocumentDialog**: Assuming URL parameter or Dataverse configuration flag to toggle between PCF and code page paths
- **Scope reconciliation**: Assuming `user_impersonation` is the canonical scope (AnalysisWorkspace PCF's `SDAP.Access` is a legacy name for the same permission)
- **UniversalQuickCreate retirement**: PCF control deprecated but kept in codebase during validation; removed in a future cleanup project

## Unresolved Questions

- [ ] `SDAP.Access` vs `user_impersonation` — are these the same Azure AD scope or different permissions? Blocks: Phase 8 scope reconciliation
- [ ] Should `@spaarke/auth` export a React hook (`useAuth()`) or only imperative APIs? Blocks: consumer API design
- [ ] CreateDocumentDialog — standalone code page (`src/client/code-pages/CreateDocument/`) or LegalWorkspace embedded? Blocks: Phase 4 file structure

---

*AI-optimized specification. Original design: file-preview-dialog-design.md*
