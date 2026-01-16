# Task Index: Document Check-Out/Check-In Viewer

> **Last Updated**: 2026-01-15
> **Status**: Active - Phase 5 In Progress
> **Total Tasks**: 27

## Quick Status

| Status | Count |
|--------|-------|
| Not Started | 5 |
| In Progress | 0 |
| Completed | 22 |
| Blocked | 0 |

## Task List

### Phase 1: Dataverse Schema

| ID | Title | Status | Dependencies | Est. Hours |
|----|-------|--------|--------------|------------|
| [001](001-create-fileversion-entity.poml) | Create sprk_fileversion Entity | ✅ completed | none | 3 |
| [002](002-add-document-checkout-fields.poml) | Add Checkout Fields to Document | ✅ completed | 001 | 2 |
| [003](003-deploy-schema-solution.poml) | Deploy Schema Solution to Dev | ✅ completed | 002 | 2 |

### Phase 2: BFF API Endpoints

| ID | Title | Status | Dependencies | Est. Hours |
|----|-------|--------|--------------|------------|
| [010](010-checkout-endpoint.poml) | Implement Checkout Endpoint | ✅ completed | 003 | 4 |
| [011](011-checkin-endpoint.poml) | Implement Check-In Endpoint | ✅ completed | 010 | 4 |
| [012](012-discard-endpoint.poml) | Implement Discard Endpoint | ✅ completed | 010 | 2 |
| [013](013-delete-endpoint.poml) | Implement Delete Document Endpoint | ✅ completed | 003 | 3 |
| [014](014-extend-preview-url.poml) | Extend Preview URL with Checkout Status | ✅ completed | 003 | 2 |
| [015](015-deploy-bff-api.poml) | Deploy BFF API to Dev | ✅ completed | 014 | 2 |

### Phase 3: SpeDocumentViewer PCF

| ID | Title | Status | Dependencies | Est. Hours |
|----|-------|--------|--------------|------------|
| [020](020-pcf-scaffolding.poml) | Create PCF Control Scaffolding | ✅ completed | 015 | 3 |
| [021](021-preview-mode.poml) | Implement Preview Mode | ✅ completed | 020 | 3 |
| [022](022-toolbar-component.poml) | Implement Fluent v9 Toolbar | ✅ completed | 021 | 3 |
| [023](023-checkout-checkin-flow.poml) | Implement Check-Out/Check-In Flow | ✅ completed | 022 | 4 |
| [024](024-edit-mode.poml) | Implement Edit Mode with embedview | ✅ completed | 023 | 3 |
| [025](025-deploy-pcf.poml) | Deploy PCF Control to Dev | ✅ completed | 024 | 2 |

### Phase 4: Delete & Ribbon

| ID | Title | Status | Dependencies | Est. Hours |
|----|-------|--------|--------------|------------|
| [030](030-delete-webresource.poml) | Create Delete JavaScript Webresource | ✅ completed | 013 | 3 |
| [031](031-ribbon-customization.poml) | Add Delete Ribbon Button | ✅ completed | 030 | 3 |
| [032](032-deploy-ribbon-solution.poml) | Deploy Ribbon Solution | ✅ completed | 031 | 2 |

### Phase 5: Migration & Integration

> **UPDATED 2026-01-15**: Added tasks 045-048 for .eml file support (email-to-document automation).
> Task 048 added for ribbon buttons (Refresh, Open in Web, Open in Desktop) per decision to use native Dataverse UX.
> Task 050 simplified - audit confirmed no forms use SpeFileViewer (fresh deployment, not migration).

| ID | Title | Status | Dependencies | Est. Hours |
|----|-------|--------|--------------|------------|
| [045](045-add-getviewurl-to-bffclient.poml) | Add getViewUrl to BffClient | ✅ completed | 025 | 1 |
| [046](046-switch-to-realtime-preview.poml) | Switch to Real-Time Preview | ✅ completed | 045 | 0.5 |
| [047](047-add-open-in-web-button.poml) | Add Open in Web Button (Hidden for .eml) | ✅ completed | 046 | 1.5 |
| [048](048-add-document-ribbon-buttons.poml) | Add Ribbon Buttons for Document Operations | ✅ completed | 047 | 2 |
| [050](050-migrate-spefileviewer.poml) | Deploy SpeDocumentViewer to Document Form | 🔲 not-started | 048 | 2 |
| [051](051-migrate-sourcedocumentviewer.poml) | Migrate SourceDocumentViewer in Analysis Workspace | 🔲 not-started | 050 | 4 |
| [052](052-ai-integration.poml) | Integrate AI Analysis on Check-In | 🔲 not-started | 051 | 3 |
| [053](053-documentation.poml) | Update Documentation | 🔲 not-started | 052 | 2 |

### Wrap-up

| ID | Title | Status | Dependencies | Est. Hours |
|----|-------|--------|--------------|------------|
| [090](090-project-wrap-up.poml) | Project Wrap-up | not-started | 053 | 2 |

## Dependency Graph

```
Phase 1 (Schema) - COMPLETED
  001 → 002 → 003
                │
                ▼
Phase 2 (BFF API) - COMPLETED
  ┌─────────────┴─────────────┐
  │                           │
010 → 011                   013
  │     │                     │
  ▼     │                     │
012     │     003 → 014 → 015 │
        │                 │   │
        └─────────────────┘   │
                              │
Phase 3 (PCF) - COMPLETED     │
  015 → 020 → 021 → 022       │
                  │           │
                  ▼           │
              023 → 024 → 025 │
                          │   │
                          │   │
Phase 4 (Delete) - COMPLETED  │
  013 → 030 → 031 → 032   │   │
                      │   │   │
                      ▼   ▼   │
Phase 5 (Integration) - ACTIVE (January 2026)
  025 → 045 → 046 → 047 → 048 → 050 → 051 → 052 → 053 → 090
        │     │     │     │
        │     │     │     └── Ribbon buttons (Refresh, Open in Web, Open in Desktop) - uses ribbon-edit skill
        │     │     └── "Open in Web" button code (hidden for .eml, moved to ribbon)
        │     └── Switch hook to real-time preview
        └── Add getViewUrl() to BffClient
```

## Execution Order

**Current Status (January 2026):**
- Phases 1-4: ✅ COMPLETED (18 tasks)
- Phase 5: 🔲 IN PROGRESS (9 tasks remaining)

**Phase 5 Execution Sequence:**
1. **045**: Add getViewUrl to BffClient (P1 - real-time preview) ✅
2. **046**: Switch useDocumentPreview hook (P1 - use real-time endpoint) ✅
3. **047**: Add "Open in Web" button code (P2 - hidden for .eml, moved to ribbon) ✅
4. **048**: Add Document Ribbon Buttons (P1 - Refresh, Open in Web, Open in Desktop) - **uses ribbon-edit skill**
5. **050**: Deploy to Document form (P1 - version 1.0.13)
6. **051**: Migrate SourceDocumentViewer in AnalysisWorkspace (P3)
7. **052**: AI Analysis on Check-In (optional - defer if not in scope)
8. **053**: Update Documentation
9. **090**: Project Wrap-up

## Notes

- Phase 4 (Delete & Ribbon) can run in parallel with Phase 3 (PCF) since they have independent dependencies
- Task 013 (Delete Endpoint) is shared dependency for both Phase 3 and Phase 4
- All deployment tasks (003, 015, 025, 032, 050) use `dataverse-deploy` skill

## January 2026 Update

**Project Reactivated**: This project was 78% complete (Phases 1-4) in December 2025 but
Phase 5 (Migration) was never started. Reactivated January 2026 for email-to-document automation.

**Key Changes:**
- Added tasks 045-048 for .eml file support and native Dataverse UX
- Task 048 added: Move Refresh, Open in Web, Open in Desktop to Form Ribbon (uses `ribbon-edit` skill)
- Decision: Native Dataverse ribbon buttons preferred over PCF toolbar for consistent UX
- Task 050 simplified: No migration needed (audit confirmed no forms use SpeFileViewer)
- Focus: Deploy SpeDocumentViewer to Document entity form for .eml preview/download

**Related:** See `projects/email-to-document-automation-r2/notes/document-viewer-remediation-plan.md`
for full context and requirements analysis.
