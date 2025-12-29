# Project Plan: Document Check-Out/Check-In Viewer

> **Last Updated**: 2025-12-18
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)

---

## 1. Executive Summary

**Purpose**: Create a unified document viewer with check-out/check-in version control, consolidating two existing components while addressing security (Share button exposure) and AI integration requirements.

**Scope**: Key deliverables
- Unified `SpeDocumentViewer` PCF control
- New `sprk_fileversion` Dataverse entity
- BFF API endpoints for document operations
- Delete functionality via Document ribbon
- Migration of existing components

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-006**: PCF controls over legacy webresources (exception: delete ribbon button)
- **ADR-012**: Shared component library - extract to `@spaarke/ui-components`

**From Spec**:
- Preview mode uses `embed.aspx` (no Share button)
- Edit mode uses `embedview` (Share acceptable during editing)
- Check-in triggers AI analysis pipeline
- Version entity tracks who checked out vs who checked in (may differ)

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Separate sprk_fileversion entity | Full audit trail, supports handoff scenarios | New entity + relationship |
| Denormalized checkout status on Document | Performance for quick lock checks | 5 new fields on Document |
| Delete via ribbon JavaScript | Works without PCF, native UX | Targeted ADR-006 exception |
| Check-in triggers AI | Stable document state for analysis | Integration with AI pipeline |

### Discovered Resources

**Applicable Skills**:
- `.claude/skills/dataverse-deploy/` - Deploy PCF and solutions
- `.claude/skills/ribbon-edit/` - Customize delete ribbon button
- `.claude/skills/adr-aware/` - Auto-applied for ADR compliance
- `.claude/skills/spaarke-conventions/` - Auto-applied for code standards

**Knowledge Articles**:
- `docs/ai-knowledge/architecture/sdap-architecture.md` - SDAP patterns
- `docs/reference/adr/ADR-006` - PCF over webresources
- `docs/reference/adr/ADR-012` - Shared component library

**Reusable Code**:
- `src/client/pcf/SpeFileViewer/` - BffClient, AuthService patterns
- `src/client/pcf/AnalysisWorkspace/control/components/SourceDocumentViewer.tsx` - Preview patterns

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Dataverse Schema
└─ Create sprk_fileversion entity
└─ Add checkout fields to Document
└─ Create version history subgrid

Phase 2: BFF API Endpoints
└─ POST checkout, checkin, discard
└─ DELETE document
└─ Modify preview-url to include checkout status

Phase 3: SpeDocumentViewer PCF
└─ Core component with preview mode
└─ Fluent v9 toolbar
└─ Check-out/check-in state management
└─ Edit mode with embedview

Phase 4: Delete & Ribbon
└─ sprk_DocumentDelete.js webresource
└─ Ribbon button customization
└─ BFF DELETE endpoint integration

Phase 5: Migration & Integration
└─ Migrate SpeFileViewer usage
└─ Migrate SourceDocumentViewer in Analysis Workspace
└─ AI analysis trigger on check-in
└─ Documentation and cleanup
```

### Critical Path

**Blocking Dependencies:**
- Phase 2 BLOCKED BY Phase 1 (needs entity schema)
- Phase 3 BLOCKED BY Phase 2 (needs API endpoints)
- Phase 4 can run PARALLEL with Phase 3
- Phase 5 BLOCKED BY Phase 3 + Phase 4

**High-Risk Items:**
- Office Online embed URL parameters - Mitigation: Abstract URL building
- Concurrent checkout race conditions - Mitigation: Optimistic locking with clear errors

---

## 4. Phase Breakdown

### Phase 1: Dataverse Schema

**Objectives:**
1. Create sprk_fileversion entity with all fields
2. Add checkout status fields to sprk_document
3. Configure relationship and cascade behavior

**Deliverables:**
- [ ] `sprk_fileversion` entity created
- [ ] Document-FileVersion 1:N relationship
- [ ] Denormalized checkout fields on Document
- [ ] Version History subgrid on Document form
- [ ] Solution export with schema changes

**Critical Tasks:**
- Entity creation MUST BE FIRST - all other work depends on it

**Inputs**: spec.md schema section

**Outputs**: Dataverse solution with schema changes

---

### Phase 2: BFF API Endpoints

**Objectives:**
1. Implement checkout/checkin/discard endpoints
2. Implement delete endpoint
3. Extend preview-url with checkout status

**Deliverables:**
- [ ] `POST /api/documents/{id}/checkout` endpoint
- [ ] `POST /api/documents/{id}/checkin` endpoint
- [ ] `POST /api/documents/{id}/discard` endpoint
- [ ] `DELETE /api/documents/{id}` endpoint
- [ ] `GET /api/documents/{id}/preview-url` extended with checkoutStatus
- [ ] Unit tests for all endpoints
- [ ] API documentation

**Critical Tasks:**
- Checkout endpoint creates FileVersion record + locks Document
- Checkin releases lock + triggers AI (fire-and-forget)

**Inputs**: Phase 1 schema, existing BFF patterns

**Outputs**: Working API endpoints, deployed to dev

---

### Phase 3: SpeDocumentViewer PCF

**Objectives:**
1. Create unified viewer component
2. Implement preview mode (embed.aspx)
3. Implement edit mode (embedview)
4. Fluent v9 toolbar with contextual buttons

**Deliverables:**
- [ ] `SpeDocumentViewer.tsx` component
- [ ] Types, styles, hooks files
- [ ] Preview mode with iframe
- [ ] Edit mode with check-out/check-in
- [ ] Toolbar with state-based button visibility
- [ ] Discard confirmation dialog
- [ ] Check-in comment dialog
- [ ] PCF manifest with configurable properties
- [ ] Unit tests
- [ ] PCF deployed to dev

**Critical Tasks:**
- State machine: loading → preview ⇔ edit → processing
- Toolbar button visibility logic based on mode + permissions

**Inputs**: Phase 2 API, existing SpeFileViewer patterns

**Outputs**: Working PCF control

---

### Phase 4: Delete & Ribbon

**Objectives:**
1. Create delete JavaScript webresource
2. Add Delete Document ribbon button
3. Integrate with BFF DELETE endpoint

**Deliverables:**
- [ ] `sprk_DocumentDelete.js` webresource
- [ ] Ribbon customization (CommandDefinition, EnableRule, DisplayRule)
- [ ] Confirmation dialog with document name
- [ ] Progress indicator during deletion
- [ ] Navigation to grid on success
- [ ] Error handling with user-friendly messages

**Critical Tasks:**
- MSAL token acquisition for BFF call
- Graceful handling if document is checked out

**Inputs**: Phase 2 DELETE endpoint, ribbon-edit skill

**Outputs**: Working delete button on Document form

---

### Phase 5: Migration & Integration

**Objectives:**
1. Replace SpeFileViewer usage with SpeDocumentViewer
2. Replace SourceDocumentViewer in Analysis Workspace
3. Connect check-in to AI analysis pipeline
4. Update documentation

**Deliverables:**
- [ ] Document form updated to use SpeDocumentViewer
- [ ] Analysis Workspace using SpeDocumentViewer (read-only mode)
- [ ] AI analysis triggered on check-in
- [ ] Old components deprecated/removed
- [ ] Updated architecture documentation
- [ ] IT deployment guide updates

**Critical Tasks:**
- Ensure feature flags work correctly (enableEdit, enableDelete)
- Verify AI analysis receives document after check-in

**Inputs**: Phase 3 PCF, Phase 4 delete, AI pipeline

**Outputs**: Fully integrated solution

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| SharePoint Embedded | GA | Low | Standard embed URLs |
| Office Online embed | GA | Low | Monitor for parameter changes |
| MSAL.js 3.x | GA | Low | Already in use |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| SpeFileStore | `src/server/shared/` | Production |
| DataverseService | `src/server/shared/` | Production |
| AI Analysis Pipeline | `src/server/api/Sprk.Bff.Api/Services/Ai/` | Production |
| BffClient pattern | `src/client/pcf/SpeFileViewer/` | Production |

---

## 6. Testing Strategy

**Unit Tests** (80% coverage target):
- BFF API endpoint logic
- PCF component state transitions
- BffClient methods

**Integration Tests**:
- Checkout → Edit → Checkin flow
- Checkout conflict handling
- Delete with SPE file removal

**E2E Tests**:
- Full document editing cycle in Dataverse
- Version history display
- AI analysis trigger verification

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase 1:**
- [ ] sprk_fileversion entity queryable in Dataverse
- [ ] Document checkout fields visible on form

**Phase 2:**
- [ ] All API endpoints return correct responses
- [ ] 409 Conflict returned for checkout conflicts

**Phase 3:**
- [ ] Preview loads in < 3 seconds
- [ ] Checkout/checkin operations < 2 seconds
- [ ] Toolbar buttons update correctly on state change

**Phase 4:**
- [ ] Delete button visible only with permission
- [ ] Delete blocked when document checked out

**Phase 5:**
- [ ] Old components fully replaced
- [ ] No regression in Analysis Workspace

### Business Acceptance

- [ ] Users can safely preview documents (no Share exposure)
- [ ] Version history accurately tracks edits
- [ ] AI analysis runs on stable document versions

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|-------------|--------|------------|
| R1 | Office Online embed parameters change | Low | Medium | Abstract URL building, monitor MS docs |
| R2 | SharePoint preview cache delays | Medium | Low | Acceptable - shows committed version |
| R3 | Concurrent checkout race | Low | Medium | Optimistic locking with retry |
| R4 | User forgets to check in | Medium | Medium | Future: auto-reminder after 24h |
| R5 | Delete fails partway (SPE deleted, DV not) | Low | High | Transaction-like error handling |

---

## 9. Next Steps

1. **Review this PLAN.md** for accuracy
2. **Run** `/task-create projects/document-checkout-viewer` to generate task files
3. **Begin** Phase 1 implementation

---

**Status**: Ready for Tasks
**Next Action**: Run `/task-create` to decompose into executable tasks

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
