# CLAUDE.md — SDAP File Upload & Document Creation Dialog (R2)

> **Project**: sdap-file-upload-document-r2
> **Branch**: `feature/sdap-file-upload-document-r2`
> **Status**: Active

## Project Summary

Migrate document upload from Custom Page + PCF to a React 18 Code Page wizard dialog with guided UX, automatic Document Profile playbook, search profile generation, and contextual next steps.

## Key Decisions

1. **Extract to shared day one** — WizardShell, FileUpload, FindSimilar, EmailStep, upload services, useAiSummary → `src/client/shared/`
2. **Send Email = inline wizard step** — Dynamic step injection with Skip button (same as Workspace pattern)
3. **Find Similar = shared component** — Tenant-wide search, extracted from LegalWorkspace
4. **Search profile = deterministic builder** — `BuildSearchProfile` in `DocumentProfileFieldMapper`, no extra AI call
5. **All entity types from day one** — Dynamic navigation property lookup already built
6. **Chunked uploads** — Support larger files via existing chunked upload endpoints
7. **No user preferences** — Ship simple for v1

## Applicable ADRs

| ADR | Key Constraint |
|-----|---------------|
| ADR-004 | Background jobs use Job Contract with idempotent handlers |
| ADR-006 | Standalone dialog → Code Page, not Custom Page + PCF |
| ADR-007 | All SPE ops through BFF API; no Graph SDK types in client |
| ADR-008 | Authorization via endpoint filters |
| ADR-012 | Shared components via `@spaarke/ui-components`; Fluent v9; dark mode |
| ADR-013 | AI via BFF endpoints; no direct Azure AI calls from client |
| ADR-021 | Fluent v9 exclusively; semantic tokens; makeStyles |
| ADR-022 | Code Pages bundle React 18; PCFs use platform React 16 |

## Key File Paths

### New Files (this project)
- `src/solutions/DocumentUploadWizard/` — New Code Page solution
- `src/client/shared/components/Wizard/` — Extracted WizardShell
- `src/client/shared/components/FileUpload/` — Extracted file upload zone
- `src/client/shared/components/EmailStep/` — Extracted email step
- `src/client/shared/components/FindSimilar/` — Extracted find similar
- `src/client/shared/services/document-upload/` — Extracted upload services
- `src/client/shared/hooks/useAiSummary.ts` — Extracted SSE hook

### Modified Files
- `src/server/api/Sprk.Bff.Api/Services/Ai/DocumentProfileFieldMapper.cs` — Add searchprofile mapping + builder
- `src/solutions/LegalWorkspace/` — Update imports to shared
- `src/client/pcf/UniversalQuickCreate/` — Update imports to shared
- `src/client/webresources/js/` — Ribbon command updates

### Reference Files
- `src/client/code-pages/CreateDocument/` — Code Page scaffold reference
- `src/solutions/LegalWorkspace/src/components/Wizard/WizardShell.tsx` — WizardShell source
- `src/solutions/LegalWorkspace/src/components/CreateMatter/WizardDialog.tsx` — Domain wizard reference
- `.claude/patterns/webresource/full-page-custom-page.md` — Code Page pattern

## Upload Pipeline (4 Phases)

```
Phase 1: Upload to SPE ─── MultiFileUploadService (parallel)
Phase 2: Create Dataverse records ─── DocumentRecordService (OData)
Phase 3: Document Profile playbook ─── useAiSummary → JPS orchestration (SSE)
         └─ Outputs: summary, tldr, keywords, type, entities, searchProfile
Phase 4: RAG indexing ─── POST /api/ai/rag/index-file (fire-and-forget)
```

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: When executing tasks in this project, Claude Code MUST invoke the `task-execute` skill. DO NOT read POML files directly and implement manually.

When Claude Code detects trigger phrases ("work on task X", "continue", "next task", etc.):
1. Read `projects/sdap-file-upload-document-r2/tasks/TASK-INDEX.md`
2. Find the target task
3. Invoke Skill tool with `skill="task-execute"` and the task file path
