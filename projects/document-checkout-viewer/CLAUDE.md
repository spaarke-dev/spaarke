# Document Check-Out/Check-In Viewer - AI Context

## Project Status
- **Phase**: Ready for Task Creation
- **Last Updated**: 2025-12-18
- **Next Action**: Run `/task-create` to decompose plan into executable tasks

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
| Component | Location | Purpose |
|-----------|----------|---------|
| SpeDocumentViewer | `src/client/pcf/SpeDocumentViewer/` | Unified PCF control (TO CREATE) |
| sprk_fileversion | Dataverse | Version tracking entity (TO CREATE) |
| BFF Endpoints | `src/server/api/Sprk.Bff.Api/` | checkout, checkin, discard, delete |
| Delete Ribbon | Solution | sprk_DocumentDelete.js webresource |

### ADR Constraints
- **ADR-006**: Use PCF controls (exception: delete ribbon button)
- **ADR-012**: Extract shared component to `@spaarke/ui-components`

## Decisions Made
<!-- Log key decisions here as project progresses -->
- 2025-12-17: Separate sprk_fileversion entity (not just fields on Document) for full audit trail
- 2025-12-17: Delete via ribbon JavaScript for native UX, works without PCF loaded
- 2025-12-17: Preview uses embed.aspx (no Share), Edit uses embedview (Share OK during editing)

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
