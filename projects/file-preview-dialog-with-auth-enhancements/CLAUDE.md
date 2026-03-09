# Project: File Preview Dialog with Auth Enhancements

## Quick Context

**What**: Shared auth package + FilePreviewDialog + CreateDocumentDialog + full auth migration
**Branch**: `work/file-preview-dialog-with-auth-enhancements`
**Phase Count**: 8 phases, ~40 tasks
**Parallel Groups**: Up to 5 agents after Phase 1

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: When executing tasks, invoke the `task-execute` skill. DO NOT read POML files directly.

| User Says | Action |
|-----------|--------|
| "work on task X" | Invoke task-execute with task POML |
| "continue" / "next task" | Check TASK-INDEX.md, invoke task-execute |
| "keep going" | Find next 🔲 task, invoke task-execute |

## Applicable ADRs

| ADR | Constraint |
|-----|-----------|
| ADR-006 | Code Pages for dialogs; PCF for field-bound controls |
| ADR-008 | Endpoint filters for auth (no global middleware) |
| ADR-010 | DI minimalism (≤15 registrations) |
| ADR-012 | Shared component library; React 18-compatible; React 16/17 PCF compat |
| ADR-021 | Fluent UI v9 only; semantic tokens; dark mode required |
| ADR-022 | PCF: React 16 APIs; platform-provided libraries; no bundled React |

## Key Constraints

- `@spaarke/auth` at `src/client/shared/Spaarke.Auth/`
- Authority: `https://login.microsoftonline.com/organizations` (multi-tenant, never hardcode tenant)
- Redirect: `window.location.origin` (never hardcode URL)
- BFF scope: `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation`
- Client ID: `170c98e1-d486-4355-bcbe-170454e0207c` (DSM-SPE Dev 2)
- Token bridge: `window.__SPAARKE_BFF_TOKEN__`

## Reference Implementations

| Pattern | File |
|---------|------|
| Auth template | `src/solutions/LegalWorkspace/src/services/bffAuthProvider.ts` |
| BFF API service | `src/solutions/LegalWorkspace/src/services/DocumentApiService.ts` |
| Navigation utils | `src/solutions/LegalWorkspace/src/utils/navigation.ts` |
| MSAL config | `src/solutions/LegalWorkspace/src/config/msalConfig.ts` |
| WizardShell | `src/solutions/LegalWorkspace/src/components/Wizard/WizardShell.tsx` |
| FileUploadZone | `src/solutions/LegalWorkspace/src/components/CreateMatter/FileUploadZone.tsx` |
| Shared UI package | `src/client/shared/Spaarke.UI.Components/` |

## Parallel Execution Groups

After Phase 1 (tasks 001-007):
- **Group A**: Tasks 010-022 (FilePreviewDialog → integration)
- **Group B**: Tasks 030-037 (CreateDocumentDialog)
- **Group C**: Tasks 040-042 (code page migration: function-based)
- **Group D**: Tasks 050-051 (code page migration: class-based)
- **Group E**: Tasks 060-061 (PCF pilot)

After Phase 7 pilot:
- **Group F**: Tasks 070-074 (remaining PCF migration)

## Decisions Log

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-03-09 | All 8 phases in scope | Owner confirmed full auth consolidation |
| 2026-03-09 | `@spaarke/auth` as separate package | Clean separation from UI components |
| 2026-03-09 | Open File: desktop → web → download | 3-tier fallback cascade |
| 2026-03-09 | Workspace toggle reads `sprk_workspaceflag` | Field determines initial pin state |
| 2026-03-09 | Unit tests for auth; manual UAT for UI | Testing strategy confirmed |
