# Implementation Plan — SDAP File Upload & Document Creation Dialog (R2)

> **Created**: 2026-03-09
> **Source**: [spec.md](spec.md)

## Architecture Context

### Applicable ADRs

| ADR | Title | Relevance |
|-----|-------|-----------|
| ADR-004 | Async Job Contract | RAG indexing uses Service Bus job with idempotent handlers |
| ADR-006 | Anti-Legacy-JS | Standalone dialog → Code Page (React 18), not Custom Page + PCF |
| ADR-007 | SpeFileStore Facade | All SPE operations through BFF API; no Graph SDK types in client |
| ADR-008 | Endpoint Filters | BFF API authorization via endpoint filters, not middleware |
| ADR-012 | Shared Component Library | Extract to `@spaarke/ui-components`; Fluent v9; dark mode; React 18-compatible |
| ADR-013 | AI Architecture | Extend BFF API for AI; no separate microservice; no direct Azure AI calls from client |
| ADR-021 | Fluent UI v9 Design System | Fluent v9 only; semantic tokens; dark/light/high-contrast support |
| ADR-022 | PCF Platform Libraries | Code Pages bundle React 18; PCFs use platform React 16/17 |

### Discovered Resources

**Patterns**:
- `.claude/patterns/webresource/full-page-custom-page.md` — Code Page scaffold
- `.claude/patterns/webresource/custom-dialogs-in-dataverse.md` — Dialog navigation
- `.claude/patterns/pcf/dialog-patterns.md` — PCF → Code Page dialog opening
- `.claude/patterns/ai/streaming-endpoints.md` — SSE streaming patterns
- `.claude/patterns/auth/msal-client.md` — MSAL browser authentication
- `.claude/patterns/dataverse/web-api-client.md` — Direct OData calls

**Knowledge Docs**:
- `docs/guides/JPS-COMPREHENSIVE-GUIDE.md` — JPS playbook pipeline
- `docs/guides/RAG-ARCHITECTURE.md` — RAG indexing pipeline
- `docs/guides/PLAYBOOK-CREATION-GUIDE.md` — Playbook design
- `docs/guides/WORKSPACE-ENTITY-CREATION-GUIDE.md` — Workspace entity patterns

**Constraints**:
- `.claude/constraints/webresource.md` — Web resource bundling rules
- `.claude/constraints/ai.md` — AI service constraints
- `.claude/constraints/api.md` — BFF API constraints
- `.claude/constraints/auth.md` — Authentication constraints

**Code References**:
- `src/client/code-pages/CreateDocument/` — Complete Code Page example (React 18, Webpack, WizardShell)
- `src/solutions/LegalWorkspace/src/components/Wizard/` — WizardShell components
- `src/solutions/LegalWorkspace/src/components/FindSimilar/` — FindSimilar dialog
- `src/solutions/LegalWorkspace/src/components/CreateMatter/SendEmailStep.tsx` — Email step
- `src/client/pcf/UniversalQuickCreate/control/services/` — Upload services
- `src/server/api/Sprk.Bff.Api/Services/Ai/DocumentProfileFieldMapper.cs` — Field mapper

**Scripts**:
- `scripts/Deploy-PCFWebResources.ps1` — PCF/web resource deployment
- `scripts/Deploy-Playbook.ps1` — Playbook deployment
- `scripts/Test-SdapBffApi.ps1` — BFF API testing

---

## Phase Breakdown

### Phase 1: Shared Component Extraction (Foundation)

**Objective**: Extract reusable components from LegalWorkspace and UniversalQuickCreate to `src/client/shared/` so both existing solutions and the new dialog can import from a single source.

**Deliverables**:

1.1. **Extract WizardShell to shared** — Move `WizardShell.tsx`, `WizardStepper.tsx`, `WizardSuccessScreen.tsx`, `wizardShellReducer.ts`, `wizardShellTypes.ts` from `src/solutions/LegalWorkspace/src/components/Wizard/` to `src/client/shared/components/Wizard/`

1.2. **Extract FileUpload components to shared** — Move `FileUploadZone.tsx`, `UploadedFileList.tsx` from LegalWorkspace's CreateMatter to `src/client/shared/components/FileUpload/`

1.3. **Extract EmailStep to shared** — Genericize `SendEmailStep.tsx`, `LookupField.tsx`, `emailHelpers.ts` from LegalWorkspace's CreateMatter to `src/client/shared/components/EmailStep/`

1.4. **Extract FindSimilar to shared** — Move `FindSimilarDialog.tsx`, `FindSimilarResultsStep.tsx`, `findSimilarService.ts`, `findSimilarTypes.ts`, `FilePreviewDialog.tsx` from LegalWorkspace to `src/client/shared/components/FindSimilar/`

1.5. **Extract upload services to shared** — Move `MultiFileUploadService.ts`, `FileUploadService.ts`, `SdapApiClient.ts`, `DocumentRecordService.ts`, types from UniversalQuickCreate to `src/client/shared/services/document-upload/`. Adapt for dual API (PCF context.webAPI + direct OData).

1.6. **Extract useAiSummary hook to shared** — Move `useAiSummary.ts` from UniversalQuickCreate to `src/client/shared/hooks/`

1.7. **Update LegalWorkspace imports** — Update all imports in LegalWorkspace to reference shared components. Verify build succeeds.

1.8. **Update UniversalQuickCreate imports** — Update PCF to import upload services from shared. Verify build succeeds.

### Phase 2: Document Upload Wizard Code Page (Core)

**Objective**: Build the new React 18 Code Page dialog with the 3-step wizard and full upload pipeline.

**Deliverables**:

2.1. **Scaffold Code Page solution** — Create `src/solutions/DocumentUploadWizard/` with `index.html`, `webpack.config.js`, `package.json`, `tsconfig.json`, `main.tsx`, `App.tsx`. Follow `CreateDocument` Code Page pattern.

2.2. **Implement DocumentUploadWizardDialog** — Domain-specific wizard component using WizardShell from shared. Configure 3 base steps (add-files, summary, next-steps). Parse URL parameters.

2.3. **Implement AddFilesStep** — File selection with FileUploadZone from shared. Validation (file limits). File list display. "Related To" read-only display. On Next: execute Phases 1-2-4 (upload, records, RAG indexing).

2.4. **Implement upload orchestrator** — `uploadOrchestrator.ts` coordinating MultiFileUploadService, DocumentRecordService (direct OData for Code Page), and RAG indexing. Progress callbacks.

2.5. **Implement chunked upload support** — Detect files exceeding single-PUT limit. Use `POST /api/containers/{id}/upload` + `PUT /api/upload-session/chunk` for large files.

2.6. **Implement SummaryStep** — Display Document Profile streaming results using useAiSummary hook. Per-document profile cards with TL;DR, type, keywords. Progress indicators.

2.7. **Implement NextStepsStep** — Checkbox cards for Send Email, Work on Analysis, Find Similar. Dynamic step injection for Send Email with Skip button.

2.8. **Implement Send Email dynamic step** — Use shared SendEmailStep. Pre-fill subject/body templates with parent entity and document links. LookupField for recipients.

2.9. **Implement success screen** — WizardSuccessScreen with document count, warnings, action buttons. Document picker for "Work on Analysis". "Find Similar" opens shared dialog. "Close" closes wizard.

2.10. **Implement next step launchers** — `nextStepLauncher.ts` for opening Analysis Builder (`navigateTo sprk_analysisbuilder`) and rendering FindSimilarDialog inline.

### Phase 3: Search Profile Integration (Backend)

**Objective**: Add `sprk_searchprofile` as a deterministic output of the Document Profile pipeline.

**Deliverables**:

3.1. **Add searchprofile mapping to DocumentProfileFieldMapper** — Add `"searchprofile" => "sprk_searchprofile"` to `GetFieldName` switch. Add to `SupportedOutputTypes`.

3.2. **Implement BuildSearchProfile function** — Deterministic builder in `DocumentProfileFieldMapper` that assembles search profile from other outputs (type, TL;DR, entities, keywords, parent entity, file name).

3.3. **Integrate BuildSearchProfile into CreateFieldMapping** — Call `BuildSearchProfile` after all outputs collected. Add `sprk_searchprofile` to field mapping dictionary.

3.4. **Add searchProfile output to Document Profile JPS definition** — Update JPS schema in Dataverse to include `searchProfile` output type (optional — builder is deterministic fallback).

3.5. **Test search profile generation** — Verify `sprk_searchprofile` populated after Document Profile runs. Validate content quality.

### Phase 4: Ribbon Integration & Deployment

**Objective**: Wire up the new dialog from Dataverse forms and deploy.

**Deliverables**:

4.1. **Build Code Page web resource** — Webpack build + inline HTML. Create `sprk_documentuploadwizard` web resource.

4.2. **Update ribbon commands** — Update ribbon button scripts on `sprk_document` (and other entity forms) to open new wizard dialog via `navigateTo`. Pass `parentEntityType`, `parentEntityId`, `parentEntityName`, `containerId`.

4.3. **Deploy to Dataverse** — Import web resource, publish customizations.

4.4. **End-to-end testing** — Test full flow: ribbon button → wizard → upload → profile → next steps. Test with multiple parent entity types.

4.5. **Dark mode testing** — Verify all UI renders correctly in dark and light modes.

### Phase 5: Wrap-Up

**Objective**: Documentation, cleanup, and project completion.

**Deliverables**:

5.1. **Update documentation** — Update relevant guides if behavior changed.

5.2. **Project wrap-up** — Update README status, create lessons-learned, archive.

---

## Dependencies Graph

```
Phase 1 (Shared Extraction)
  1.1 WizardShell ─┐
  1.2 FileUpload  ─┤
  1.3 EmailStep   ─┤── 1.7 Update LegalWorkspace imports
  1.4 FindSimilar ─┘
  1.5 Upload services ── 1.8 Update UniversalQuickCreate imports
  1.6 useAiSummary ──┘

Phase 2 (Wizard Code Page) ← depends on Phase 1 complete
  2.1 Scaffold ─── 2.2 WizardDialog ─┬─ 2.3 AddFilesStep
                                      ├─ 2.6 SummaryStep
                                      └─ 2.7 NextStepsStep
  2.4 Upload orchestrator ← 2.3
  2.5 Chunked upload ← 2.4
  2.8 Email step ← 2.7
  2.9 Success screen ← 2.7
  2.10 Next step launchers ← 2.9

Phase 3 (Search Profile) ← independent of Phase 2 (backend only)
  3.1 Field mapper ── 3.2 BuildSearchProfile ── 3.3 Integration ── 3.5 Testing
  3.4 JPS update (optional)

Phase 4 (Integration) ← depends on Phase 2 + Phase 3
  4.1 Build ── 4.2 Ribbon ── 4.3 Deploy ── 4.4 E2E testing ── 4.5 Dark mode

Phase 5 (Wrap-up) ← depends on Phase 4
```

## Parallel Execution Opportunities

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| A | 1.1, 1.2, 1.3, 1.4, 1.5, 1.6 | None | Independent component extractions |
| B | Phase 2, Phase 3 | Phase 1 | Wizard (frontend) and search profile (backend) are independent |

## Risk Items

| Risk | Impact | Mitigation |
|------|--------|------------|
| Shared extraction breaks LegalWorkspace build | High | Test imports immediately after each extraction (1.7) |
| WizardShell has hidden dependencies on LegalWorkspace | Medium | Audit imports before moving; stub any local dependencies |
| DocumentRecordService dual-API (webAPI + OData) adds complexity | Medium | Strategy pattern with clear interface; test both paths |
| Chunked upload sessions untested in Code Page context | Low | Falls back to single PUT for files under limit |
