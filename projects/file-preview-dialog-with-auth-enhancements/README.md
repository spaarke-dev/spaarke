# File Preview Dialog with Auth Enhancements

> **Status**: In Progress
> **Branch**: `work/file-preview-dialog-with-auth-enhancements`
> **Created**: 2026-03-09

## Overview

Consolidate 3 fragmented file preview implementations and 16 separate auth implementations into standardized, reusable components. Creates a shared `@spaarke/auth` package that replaces ~8,149 lines of duplicated auth code, builds a `FilePreviewDialog` for consistent document preview UX, replaces the `UniversalQuickCreate` PCF with a React 18 `CreateDocumentDialog`, and migrates all code pages and PCF controls to shared auth.

## Graduation Criteria

- [ ] `@spaarke/auth` package created with unit tests passing
- [ ] `FilePreviewDialog` renders with 4 toolbar actions (Open File, Open Record, Copy Link, Add/Remove Workspace)
- [ ] FindSimilar + DocumentCard integrated with FilePreviewDialog
- [ ] `CreateDocumentDialog` code page functional (upload + create record)
- [ ] All 5 code pages migrated to `@spaarke/auth`
- [ ] All 7 PCF controls migrated to `@spaarke/auth`
- [ ] ~8,149 lines of auth code removed
- [ ] Zero auth regressions across all components
- [ ] Scope reconciliation complete (`SDAP.Access` vs `user_impersonation`)

## Quick Links

- [Specification](spec.md)
- [Implementation Plan](plan.md)
- [Task Index](tasks/TASK-INDEX.md)
- [Project Context](CLAUDE.md)

## Architecture

```
@spaarke/auth (NEW - src/client/shared/Spaarke.Auth/)
├── Token acquisition: bridge → cache → Xrm → MSAL ssoSilent → popup
├── authenticatedFetch() with 401 retry + RFC 7807 parsing
├── Token bridge utilities (parent ↔ child iframe)
└── Environment-portable config

FilePreviewDialog (NEW - src/solutions/LegalWorkspace/src/components/FilePreview/)
├── Fluent UI v9 Dialog (85vw × 85vh)
├── Toolbar: Open File | Open Record | Copy Link | Add to Workspace
├── iframe preview with loading/error states
└── Consumed by: FindSimilar, DocumentCard

CreateDocumentDialog (NEW - Phase 4)
├── React 18 code page replacing UniversalQuickCreate PCF
├── WizardShell: Upload → Details → Next Steps
└── Uses @spaarke/auth + authenticatedFetch()
```

## Phase Overview

| Phase | Description | Dependencies | Parallel Group |
|-------|-------------|--------------|----------------|
| 1 | `@spaarke/auth` shared package | None | Sequential |
| 2 | FilePreviewDialog component | Phase 1 | A |
| 3 | FilePreviewDialog integration | Phase 2 | A (sequential) |
| 4 | CreateDocumentDialog code page | Phase 1 | B |
| 5 | Code page migration (function-based) | Phase 1 | C |
| 6 | Code page migration (class-based) | Phase 1 | D |
| 7 | PCF migration (pilot) | Phase 1 | E |
| 8 | PCF migration (complete) | Phase 7 | F |
