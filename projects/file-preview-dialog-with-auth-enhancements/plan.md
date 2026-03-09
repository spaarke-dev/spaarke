# Implementation Plan — File Preview Dialog with Auth Enhancements

> **Created**: 2026-03-09
> **Spec**: [spec.md](spec.md)

## Architecture Context

### Discovered Resources

| Type | Count | Resources |
|------|-------|-----------|
| ADRs | 7 | ADR-001, ADR-006, ADR-008, ADR-010, ADR-012, ADR-021, ADR-022 |
| Auth Patterns | 5 | oauth-scopes, msal-client, obo-flow, token-caching, uac-access-control |
| Constraints | 4 | auth.md, pcf.md, webresource.md, api.md |
| Reference Implementations | 9 | bffAuthProvider, DocumentApiService, navigation, msalConfig, WizardShell, WizardDialog (CreateMatter), FileUploadZone, DocumentCard, FindSimilarResultsStep |
| Skills | 4 | code-review, adr-check, code-page-deploy, push-to-github |
| Scripts | 2 | Deploy-PCFWebResources.ps1, test-sdap-api-health.js |

### Key Architecture Decisions

1. **@spaarke/auth location**: New package at `src/client/shared/Spaarke.Auth/` (separate from UI components)
2. **Auth template**: LegalWorkspace `bffAuthProvider.ts` (268 lines) is the canonical pattern
3. **FilePreviewDialog**: Fluent UI v9 Dialog (not Popover or fixed panel)
4. **CreateDocumentDialog**: Uses existing `WizardShell` component
5. **React versions**: Code pages use React 18 `createRoot()`; PCF uses React 16 `ReactDOM.render()`
6. **Build**: TypeScript + tsc (matching `@spaarke/ui-components` pattern)

### Parallelization Strategy

After Phase 1 completes, Phases 2-7 can execute in parallel via Claude Code agent teams:

```
Phase 1 (Sequential — Foundation)
    │
    ├──→ Group A: Phase 2 → Phase 3 (FilePreviewDialog → Integration)
    ├──→ Group B: Phase 4 (CreateDocumentDialog)
    ├──→ Group C: Phase 5 (AnalysisWorkspace + PlaybookBuilder + SprkChatPane migration)
    ├──→ Group D: Phase 6 (SemanticSearch + DocRelViewer code page migration)
    └──→ Group E: Phase 7 (SpeDocumentViewer + SpeFileViewer PCF pilot)
              │
              └──→ Group F: Phase 8 (Remaining PCF migration — after pilot proves pattern)
```

**Up to 5 parallel agents** after Phase 1 completes.

---

## Phase Breakdown

### Phase 1: `@spaarke/auth` Shared Package (Foundation)

**Goal**: Create the shared auth package that all other phases depend on.

**Deliverables**:
1. Package scaffolding (`src/client/shared/Spaarke.Auth/`)
2. Token acquisition strategies (5-strategy cascade)
3. `authenticatedFetch()` with 401 retry + RFC 7807 parsing
4. Token bridge utilities
5. Config and initialization API
6. Unit test suite
7. LegalWorkspace migration (replace `bffAuthProvider.ts` with `@spaarke/auth`)

**Files**:
- `src/client/shared/Spaarke.Auth/package.json`
- `src/client/shared/Spaarke.Auth/tsconfig.json`
- `src/client/shared/Spaarke.Auth/src/index.ts`
- `src/client/shared/Spaarke.Auth/src/SpaarkeAuthProvider.ts`
- `src/client/shared/Spaarke.Auth/src/authenticatedFetch.ts`
- `src/client/shared/Spaarke.Auth/src/tokenBridge.ts`
- `src/client/shared/Spaarke.Auth/src/config.ts`
- `src/client/shared/Spaarke.Auth/src/types.ts`
- `src/client/shared/Spaarke.Auth/src/errors.ts`
- `src/client/shared/Spaarke.Auth/src/strategies/` (token acquisition strategies)
- `src/client/shared/Spaarke.Auth/tests/`

**Reference**: `src/solutions/LegalWorkspace/src/services/bffAuthProvider.ts` (template)

### Phase 2: FilePreviewDialog Component

**Goal**: Build the standardized file preview dialog in LegalWorkspace.

**Deliverables**:
1. `FilePreviewDialog.tsx` — main dialog component
2. `filePreviewService.ts` — copy link + workspace flag helpers
3. All 4 toolbar actions functional
4. Loading/error/success states

**Files**:
- `src/solutions/LegalWorkspace/src/components/FilePreview/FilePreviewDialog.tsx`
- `src/solutions/LegalWorkspace/src/components/FilePreview/filePreviewService.ts`

**Reference**: `DocumentApiService.ts`, `navigation.ts`, `DocumentCard.tsx` (existing preview pattern)

### Phase 3: FilePreviewDialog Integration

**Goal**: Replace existing preview patterns with the standardized dialog.

**Deliverables**:
1. FindSimilarResultsStep — replace 3 action buttons with single preview button
2. DocumentCard — replace popover preview with dialog trigger
3. ~140 lines of duplicated code removed

**Files modified**:
- `src/solutions/LegalWorkspace/src/components/FindSimilar/FindSimilarResultsStep.tsx`
- `src/solutions/LegalWorkspace/src/components/RecordCards/DocumentCard.tsx`

### Phase 4: CreateDocumentDialog Code Page

**Goal**: Build React 18 code page replacing UniversalQuickCreate PCF.

**Deliverables**:
1. Code page scaffolding (webpack, HTML template, entry point)
2. FileUploadStep (drag-and-drop with progress)
3. DocumentDetailsStep (form fields + lookups)
4. NextStepsStep (follow-on actions)
5. Upload service (authenticatedFetch → BFF)
6. Record creation service (Xrm.WebApi or authenticatedFetch)
7. Feature-flagged parallel deployment

**Files**:
- `src/client/code-pages/CreateDocument/` (new code page)
- `src/solutions/LegalWorkspace/src/components/CreateDocument/` (embedded components)

**Reference**: `WizardShell.tsx`, `WizardDialog.tsx` (CreateMatter), `FileUploadZone.tsx`

### Phase 5: Code Page Migration — Function-Based (3 components)

**Goal**: Migrate AnalysisWorkspace, PlaybookBuilder, SprkChatPane to `@spaarke/auth`.

**Per-component pattern**:
1. Replace auth imports with `@spaarke/auth`
2. Remove local auth files (~200-500 lines each)
3. Verify authentication still works

**Parallel**: All 3 migrations are independent — can run as 3 parallel tasks.

### Phase 6: Code Page Migration — Class-Based (2 components)

**Goal**: Migrate SemanticSearch + DocumentRelationshipViewer code pages. Fix hardcoded tenant/URL issues.

**Per-component pattern**:
1. Replace class-based singleton with `@spaarke/auth`
2. Remove hardcoded tenant IDs and redirect URIs
3. Add parent token bridge support
4. Verify environment portability

**Parallel**: Both migrations are independent.

### Phase 7: PCF Migration — Pilot (2 controls)

**Goal**: Prove `@spaarke/auth` works in PCF controls (React 16 environment).

**Per-component pattern**:
1. Replace `AuthService.ts` + `BffClient.ts` with `@spaarke/auth`
2. Ensure React 16 compatibility
3. Test in Dataverse form

**Pilot controls**: SpeDocumentViewer, SpeFileViewer (smallest, most isolated).

### Phase 8: PCF Migration — Complete (5 controls)

**Goal**: Complete auth migration across all remaining PCF controls.

**Controls**:
1. UniversalDatasetGrid (largest — ~1,305 lines removed)
2. SemanticSearchControl
3. DocumentRelationshipViewer PCF
4. EmailProcessingMonitor
5. AnalysisWorkspace PCF (requires scope reconciliation)

**Parallel**: All 5 independent after Phase 7 pilot validates.

---

## Risk Assessment

| Risk | Impact | Mitigation |
|------|--------|------------|
| PCF React 16 compatibility | High | Phase 7 pilot before full rollout |
| Scope reconciliation (`SDAP.Access` vs `user_impersonation`) | Medium | Investigate early in Phase 1; resolve in Phase 8 |
| CreateDocumentDialog partial failure handling | High | Feature flag + parallel deployment with existing PCF |
| Auth regression in any migrated component | Critical | Per-component manual UAT; keep old code until verified |
| Context exhaustion (40+ tasks) | Medium | Parallel agent execution; checkpointing every 3 steps |

---

## References

| Resource | Path |
|----------|------|
| Auth patterns (comprehensive) | `docs/architecture/sdap-auth-patterns.md` |
| Auth constraints | `.claude/constraints/auth.md` |
| PCF constraints | `.claude/constraints/pcf.md` |
| Webresource constraints | `.claude/constraints/webresource.md` |
| Custom dialog patterns | `.claude/patterns/webresource/custom-dialogs-in-dataverse.md` |
| MSAL client patterns | `.claude/patterns/auth/msal-client.md` |
| Token caching patterns | `.claude/patterns/auth/token-caching.md` |
| OAuth scopes | `.claude/patterns/auth/oauth-scopes.md` |
| Shared component library | `src/client/shared/Spaarke.UI.Components/` |
| WizardShell | `src/solutions/LegalWorkspace/src/components/Wizard/WizardShell.tsx` |
| bffAuthProvider (template) | `src/solutions/LegalWorkspace/src/services/bffAuthProvider.ts` |
| DocumentApiService | `src/solutions/LegalWorkspace/src/services/DocumentApiService.ts` |
| Navigation utilities | `src/solutions/LegalWorkspace/src/utils/navigation.ts` |
