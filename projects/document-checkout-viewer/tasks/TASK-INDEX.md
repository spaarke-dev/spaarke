# Task Index: Document Check-Out/Check-In Viewer

> **Last Updated**: 2025-12-18
> **Status**: Ready for Implementation
> **Total Tasks**: 23

## Quick Status

| Status | Count |
|--------|-------|
| Not Started | 14 |
| In Progress | 0 |
| Completed | 9 |
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
| [020](020-pcf-scaffolding.poml) | Create PCF Control Scaffolding | not-started | 015 | 3 |
| [021](021-preview-mode.poml) | Implement Preview Mode | not-started | 020 | 3 |
| [022](022-toolbar-component.poml) | Implement Fluent v9 Toolbar | not-started | 021 | 3 |
| [023](023-checkout-checkin-flow.poml) | Implement Check-Out/Check-In Flow | not-started | 022 | 4 |
| [024](024-edit-mode.poml) | Implement Edit Mode with embedview | not-started | 023 | 3 |
| [025](025-deploy-pcf.poml) | Deploy PCF Control to Dev | not-started | 024 | 2 |

### Phase 4: Delete & Ribbon

| ID | Title | Status | Dependencies | Est. Hours |
|----|-------|--------|--------------|------------|
| [030](030-delete-webresource.poml) | Create Delete JavaScript Webresource | not-started | 013 | 3 |
| [031](031-ribbon-customization.poml) | Add Delete Ribbon Button | not-started | 030 | 3 |
| [032](032-deploy-ribbon-solution.poml) | Deploy Ribbon Solution | not-started | 031 | 2 |

### Phase 5: Migration & Integration

| ID | Title | Status | Dependencies | Est. Hours |
|----|-------|--------|--------------|------------|
| [050](050-migrate-spefileviewer.poml) | Migrate SpeFileViewer to SpeDocumentViewer | not-started | 025, 032 | 3 |
| [051](051-migrate-sourcedocumentviewer.poml) | Migrate SourceDocumentViewer in Analysis Workspace | not-started | 050 | 3 |
| [052](052-ai-integration.poml) | Integrate AI Analysis on Check-In | not-started | 051 | 3 |
| [053](053-documentation.poml) | Update Documentation | not-started | 052 | 2 |

### Wrap-up

| ID | Title | Status | Dependencies | Est. Hours |
|----|-------|--------|--------------|------------|
| [090](090-project-wrap-up.poml) | Project Wrap-up | not-started | 053 | 2 |

## Dependency Graph

```
Phase 1 (Schema)
  001 → 002 → 003
                │
                ▼
Phase 2 (BFF API)
  ┌─────────────┴─────────────┐
  │                           │
010 → 011                   013
  │     │                     │
  ▼     │                     │
012     │     003 → 014 → 015 │
        │                 │   │
        └─────────────────┘   │
                              │
Phase 3 (PCF)                 │
  015 → 020 → 021 → 022       │
                  │           │
                  ▼           │
              023 → 024 → 025 │
                          │   │
                          │   │
Phase 4 (Delete)          │   │
  013 → 030 → 031 → 032   │   │
                      │   │   │
                      ▼   ▼   │
Phase 5 (Integration)─────────┘
  025 + 032 → 050 → 051 → 052 → 053 → 090
```

## Execution Order

**Recommended sequence:**
1. Start with **001** (no dependencies)
2. Phase 1: 001 → 002 → 003
3. Phase 2: 010, 013, 014 can start in parallel after 003
4. Phase 3: 020-025 sequential after 015
5. Phase 4: 030-032 sequential after 013 (can run parallel with Phase 3)
6. Phase 5: 050-053 after both Phase 3 and Phase 4 complete
7. End with **090** (project wrap-up)

## Notes

- Phase 4 (Delete & Ribbon) can run in parallel with Phase 3 (PCF) since they have independent dependencies
- Task 013 (Delete Endpoint) is shared dependency for both Phase 3 and Phase 4
- All deployment tasks (003, 015, 025, 032) use `dataverse-deploy` skill
