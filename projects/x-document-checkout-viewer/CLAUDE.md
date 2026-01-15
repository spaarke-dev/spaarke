# Document Check-Out/Check-In Viewer - AI Context

## Project Status
- **Phase**: Phase 5 - Migration & Integration (ACTIVE)
- **Last Updated**: 2026-01-15
- **Next Action**: Begin Task 045 (Add getViewUrl to BffClient)
- **Reactivated**: January 2026 for email-to-document automation support

## Task Summary
| Phase | Tasks | Description | Status |
|-------|-------|-------------|--------|
| 1: Dataverse Schema | 001-003 | Entity creation, fields, solution deploy | ✅ Complete |
| 2: BFF API Endpoints | 010-015 | checkout, checkin, discard, delete, preview-url | ✅ Complete |
| 3: SpeDocumentViewer PCF | 020-025 | Scaffolding, preview, toolbar, checkout flow, edit, deploy | ✅ Complete |
| 4: Delete & Ribbon | 030-032 | Webresource, ribbon button, deploy | ✅ Complete |
| 5: Migration & Integration | 045-053 | Real-time preview, Open in Web, form deploy, docs | 🔲 Active |
| Wrap-up | 090 | Final cleanup and validation | 🔲 Pending |

**Total**: 26 tasks (3 new) | **Completed**: 18 | **Remaining**: 8

## January 2026 Updates

Project reactivated to support email-to-document automation (.eml file viewing).

**New Tasks Added (Phase 5):**
- 045: Add getViewUrl to BffClient (real-time preview without cache)
- 046: Switch useDocumentPreview hook to real-time endpoint
- 047: Add "Open in Web" button (hidden for .eml files)

**Task 050 Updated:**
- Original: Migrate SpeFileViewer to SpeDocumentViewer
- Updated: Deploy SpeDocumentViewer to Document form (simplified - no migration needed)
- Reason: Audit confirmed no forms currently use SpeFileViewer or SpeDocumentViewer

**Related:** `projects/email-to-document-automation-r2/notes/document-viewer-remediation-plan.md`

## Key Files
- `spec.md` - Original design specification (permanent reference)
- `README.md` - Project overview and graduation criteria
- `plan.md` - Implementation plan with 5 phases
- `SPE-FILE-VIEWER-ARCHITECTURE.md` - Component architecture
- `tasks/` - Individual task files (POML format)

## Context Loading Rules
1. Always load this file first when working on document-checkout-viewer
2. Reference spec.md for design decisions and requirements
3. Load relevant task file from tasks/ based on current work
4. Apply `adr-aware` and `spaarke-conventions` skills automatically

## Architecture Summary

### Check-Out/Check-In Model
```
Preview Mode (embed.aspx)  →  Check Out  →  Edit Mode (embedview)  →  Check In  →  Preview Mode
     ↑                                                                      │
     └──────────────────────  Discard  ←────────────────────────────────────┘
```

### Key Components
| Component | Location | Purpose | Status |
|-----------|----------|---------|--------|
| SpeDocumentViewer | `src/client/pcf/SpeDocumentViewer/` | Unified PCF control | ✅ Built (v1.0.12) |
| sprk_fileversion | Dataverse | Version tracking entity | ✅ Deployed |
| BFF Endpoints | `src/server/api/Sprk.Bff.Api/` | checkout, checkin, discard, delete | ✅ Deployed |
| Delete Ribbon | Solution | sprk_DocumentDelete.js webresource | ✅ Deployed |
| Document Form Binding | Dataverse | SpeDocumentViewer on Document entity | 🔲 Task 050 |

### ADR Constraints
- **ADR-006**: Use PCF controls (exception: delete ribbon button)
- **ADR-012**: Extract shared component to `@spaarke/ui-components`

## Decisions Made
<!-- Log key decisions here as project progresses -->
- 2025-12-17: Separate sprk_fileversion entity (not just fields on Document) for full audit trail
- 2025-12-17: Delete via ribbon JavaScript for native UX, works without PCF loaded
- 2025-12-17: Preview uses embed.aspx (no Share), Edit uses embedview (Share OK during editing)
- 2026-01-15: Use getViewUrl() instead of getPreviewUrl() for real-time preview (no 30-60s cache delay)
- 2026-01-15: Hide "Open in Web" button for .eml files (no Office Online viewer exists)
- 2026-01-15: No SpeFileViewer migration needed - audit confirmed no forms use it
- 2026-01-15: Deprecate SpeFileViewer due to ADR-022 violation (bundles React 19)

## Current Constraints
- Preview mode must hide Share button (security requirement)
- Check-in must trigger AI analysis pipeline
- Version entity must track who checked out vs who checked in (may differ)
- Delete blocked while document is checked out

## Code Patterns to Follow

### BFF Endpoint Pattern
```csharp
// Use endpoint filters for authorization
app.MapPost("/api/documents/{id}/checkout", CheckoutHandler)
   .AddEndpointFilter<DocumentAuthorizationFilter>();
```

### PCF Component Pattern
```typescript
// Use Fluent v9 with makeStyles
import { makeStyles, Button, Tooltip } from "@fluentui/react-components";
import { EditRegular } from "@fluentui/react-icons";

// State machine for view modes
type ViewMode = "preview" | "edit" | "loading" | "processing" | "error";
```

### Toolbar Button Pattern
```typescript
<Tooltip content="Check out document" relationship="label">
    <Button
        appearance="subtle"
        size="small"
        icon={<EditRegular />}
        onClick={handleCheckout}
        disabled={isLoading || !canEdit}
    />
</Tooltip>
```

## Related Resources
- Existing SpeFileViewer: `src/client/pcf/SpeFileViewer/`
- Existing SourceDocumentViewer: `src/client/pcf/AnalysisWorkspace/control/components/`
- BFF API patterns: `docs/ai-knowledge/architecture/sdap-bff-api-patterns.md`
- AI Analysis: `src/server/api/Sprk.Bff.Api/Services/Ai/`

## Skills to Use
- `dataverse-deploy` - For deploying PCF and solutions
- `ribbon-edit` - For delete ribbon button customization
- `adr-check` - Validate ADR compliance before completion
