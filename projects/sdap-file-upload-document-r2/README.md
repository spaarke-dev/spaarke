# SDAP File Upload & Document Creation Dialog (R2)

> **Status**: Implementation Complete — Awaiting Deployment
> **Branch**: `feature/sdap-file-upload-document-r2`
> **Created**: 2026-03-09

## Overview

Migrate the Document creation-from-files workflow from a Custom Page + UniversalQuickCreate PCF anti-pattern to a standalone React 18 Code Page wizard dialog. The new dialog provides a guided 3-step experience (Add Files → Summary → Next Steps), automatically runs the Document Profile playbook, generates a search-optimized `sprk_searchprofile` field, and offers contextual follow-on actions.

## Key Deliverables

1. **Shared component extraction** — WizardShell, FileUpload, FindSimilar, EmailStep, upload services to `src/client/shared/`
2. **Document Upload Wizard Code Page** — `sprk_documentuploadwizard` HTML web resource
3. **Search profile integration** — `BuildSearchProfile` in `DocumentProfileFieldMapper`
4. **Ribbon integration** — Updated commands to open new dialog
5. **LegalWorkspace + UniversalQuickCreate import updates**

## Architecture

```
Dataverse Form → Ribbon Button → navigateTo(sprk_documentuploadwizard)
    │
    └─ React 18 Code Page
        ├─ WizardShell (from shared)
        │   ├─ Step 1: Add Files → SPE upload + Dataverse records + RAG indexing
        │   ├─ Step 2: Summary → Document Profile playbook (SSE streaming)
        │   └─ Step 3: Next Steps → Send Email / Analysis / Find Similar
        └─ Dual Pipeline: SPE storage + AI Search indexing
```

## Graduation Criteria

- [ ] Files upload to SPE and `sprk_document` records created
- [ ] Files indexed to Azure AI Search (dual-index)
- [ ] Document Profile writes all 6 fields including `sprk_searchprofile`
- [ ] Send Email creates Dataverse email activity
- [ ] Analysis Builder opens for user-selected document
- [ ] Find Similar searches tenant-wide with pre-loaded documents
- [ ] Dark mode + light mode working
- [ ] Shared components extracted; LegalWorkspace + UniversalQuickCreate build
- [ ] Larger files upload via chunked sessions
- [ ] Old Custom Page ribbon commands updated

## Quick Links

- [Specification](spec.md)
- [Implementation Plan](plan.md)
- [Task Index](tasks/TASK-INDEX.md)
- [Project Context](CLAUDE.md)
