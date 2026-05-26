# Document Upload Wizard — Integration Guide

> **Last Updated**: 2026-04-05
> **Web Resource**: `sprk_documentuploadwizard` (Webpage HTML)
> **Commands Script**: `sprk_subgrid_commands` (Script JS)
> **Source**: `src/solutions/DocumentUploadWizard/`

---

## Overview

The Document Upload Wizard is a standalone React 18 Code Page that provides multi-file upload with optional follow-on actions (Send Email, Work on Analysis, Find Similar). It runs inside a Dataverse dialog opened via `Xrm.Navigation.navigateTo`.

Any surface in Dataverse — ribbon buttons, PCF controls, Code Pages — can launch this wizard by passing the correct parameters.

---

## Required Parameters

The wizard expects URL query parameters passed via the `data` property of `navigateTo`:

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `parentEntityType` | `string` | Yes | Dataverse logical name (e.g. `sprk_matter`, `account`) |
| `parentEntityId` | `string` | Yes | Parent record GUID — **no braces, lowercase** |
| `parentEntityName` | `string` | Yes | Display name shown in dialog title bar |
| `containerId` | `string` | Yes | SharePoint Embedded container GUID |
| `theme` | `"light" \| "dark"` | Yes | Matches the current Dataverse theme |

### Supported Parent Entities

| Entity | Logical Name | Container ID Field | Display Name Fields |
|--------|-------------|-------------------|---------------------|
| Matter | `sprk_matter` | `sprk_containerid` | `sprk_matternumber`, `sprk_name` |
| Project | `sprk_project` | `sprk_containerid` | `sprk_projectname`, `sprk_name` |
| Invoice | `sprk_invoice` | `sprk_containerid` | `sprk_invoicenumber`, `name` |
| Account | `account` | `sprk_containerid` | `name` |
| Contact | `contact` | `sprk_containerid` | `fullname`, `lastname`, `firstname` |
| Communication | `sprk_communication` | `sprk_containerid` | `sprk_name` |

To add a new entity, see [HOW-TO-ADD-SDAP-TO-NEW-ENTITY.md](HOW-TO-ADD-SDAP-TO-NEW-ENTITY.md) and add the entity to `ENTITY_CONFIGURATIONS` in `sprk_subgrid_commands.js` and `EntityDocumentConfig` in the upload orchestrator.

---

## Dialog Configuration

| Property | Value |
|----------|-------|
| `pageType` | `"webresource"` |
| `webresourceName` | `"sprk_documentuploadwizard"` |
| `target` | `2` (dialog) |
| Width | 60% |
| Height | 70% |

---

## Integration Pattern A: From a Ribbon Button (Classic JS Web Resource)

This is the existing pattern used by `sprk_subgrid_commands.js` on the Documents subgrid. The ribbon passes `SelectedControl` (the subgrid), from which the script resolves the parent form context, entity info, and container ID.

**Reference implementation**: `src/solutions/DocumentUploadWizard/sprk_subgrid_commands.js`

No code changes needed — just wire the ribbon button to `Spaarke_AddMultipleDocuments` with `SelectedControl` as the CRM parameter. See the deployment notes at the bottom of that file for Ribbon Workbench configuration.

---

## Integration Pattern B: From a PCF Control (e.g. Semantic Search)

PCF controls run inside a Dataverse form and have access to `Xrm` on the global scope.

### Step 1: Add a launch function

```typescript
/**
 * Opens the Document Upload Wizard Code Page dialog.
 *
 * @param parentEntityType  Dataverse entity logical name (e.g. "sprk_matter")
 * @param parentEntityId    Parent record GUID (braces are stripped automatically)
 * @param parentEntityName  Display name shown in dialog title
 * @param containerId       SharePoint Embedded container GUID
 * @param onDialogClosed    Optional callback after dialog closes (e.g. refresh grid)
 */
function openDocumentUploadWizard(
    parentEntityType: string,
    parentEntityId: string,
    parentEntityName: string,
    containerId: string,
    onDialogClosed?: () => void,
): void {
    // Detect current Dataverse theme
    let theme = "light";
    try {
        const bodyBg = window.getComputedStyle(document.body).backgroundColor;
        const rgbMatch = bodyBg.match(/rgb\((\d+),\s*(\d+),\s*(\d+)\)/);
        if (rgbMatch) {
            const luminance =
                0.299 * parseInt(rgbMatch[1]) +
                0.587 * parseInt(rgbMatch[2]) +
                0.114 * parseInt(rgbMatch[3]);
            if (luminance < 128) theme = "dark";
        }
    } catch {
        /* ignore */
    }

    const cleanId = parentEntityId.replace(/[{}]/g, "").toLowerCase();

    const dataString =
        "parentEntityType=" + parentEntityType +
        "&parentEntityId=" + cleanId +
        "&parentEntityName=" + encodeURIComponent(parentEntityName) +
        "&containerId=" + containerId +
        "&theme=" + theme;

    Xrm.Navigation.navigateTo(
        {
            pageType: "webresource",
            webresourceName: "sprk_documentuploadwizard",
            data: encodeURIComponent(dataString),
        },
        {
            target: 2,
            width: { value: 60, unit: "%" },
            height: { value: 70, unit: "%" },
        },
    ).then(
        () => {
            // Dialog closed successfully — refresh document list
            onDialogClosed?.();
        },
        (err: { errorCode?: number; message?: string }) => {
            // errorCode 2 = user cancelled (ESC or Cancel button) — not an error
            if (err && err.errorCode !== 2) {
                console.error("[Upload] Dialog error:", err);
            }
        },
    );
}
```

### Step 2: Resolve parameters from PCF context

In a PCF control on a Dataverse form, you typically have:

```typescript
// Entity type and record ID from the form
const parentEntityType = context.page.entityTypeName;          // e.g. "sprk_matter"
const parentEntityId = context.page.entityId;                   // GUID with braces

// Display name — read from a bound field or fetch via WebApi
const parentEntityName = context.parameters.displayName?.raw ?? "";

// Container ID — read from the sprk_containerid field on the form
const containerId = context.parameters.containerId?.raw ?? "";
```

If `containerId` is not available as a bound parameter, query the business unit:

```typescript
async function resolveContainerId(): Promise<string | null> {
    const userSettings = Xrm.Utility.getGlobalContext().userSettings;
    const userId = userSettings.userId.replace(/[{}]/g, "");

    const user = await Xrm.WebApi.retrieveRecord(
        "systemuser", userId, "?$select=_businessunitid_value"
    );
    const buId = user["_businessunitid_value"];
    if (!buId) return null;

    const bu = await Xrm.WebApi.retrieveRecord(
        "businessunit", buId, "?$select=sprk_containerid"
    );
    return bu["sprk_containerid"] ?? null;
}
```

### Step 3: Wire to the "+ Add Document" button

```typescript
const handleAddDocument = async () => {
    const cid = containerId || await resolveContainerId();
    if (!cid) {
        // Show error — no container configured
        return;
    }

    openDocumentUploadWizard(
        parentEntityType,
        parentEntityId,
        parentEntityName,
        cid,
        () => {
            // Refresh your document grid/list after upload
            refreshDocumentList();
        },
    );
};
```

---

## Integration Pattern C: From a Code Page (e.g. Corporate Workspace)

Code Pages run inside an iframe, so `Xrm` is not on the direct `window` — it must be resolved from the parent frame.

### Step 1: Add a launch function with frame-walking

```typescript
/**
 * Opens the Document Upload Wizard from within a Code Page.
 * Resolves Xrm.Navigation from the parent Dataverse frame.
 */
function openDocumentUploadWizard(
    parentEntityType: string,
    parentEntityId: string,
    parentEntityName: string,
    containerId: string,
    onDialogClosed?: () => void,
): void {
    // Resolve Xrm from parent frame hierarchy
    const xrm =
        (window as any).Xrm ??
        (window as any).parent?.Xrm ??
        (window as any).top?.Xrm;

    if (!xrm?.Navigation?.navigateTo) {
        console.error("[Workspace] Xrm.Navigation not available — cannot open upload wizard");
        return;
    }

    // Detect theme
    let theme = "light";
    try {
        const bodyBg = window.getComputedStyle(document.body).backgroundColor;
        const rgbMatch = bodyBg.match(/rgb\((\d+),\s*(\d+),\s*(\d+)\)/);
        if (rgbMatch) {
            const luminance =
                0.299 * parseInt(rgbMatch[1]) +
                0.587 * parseInt(rgbMatch[2]) +
                0.114 * parseInt(rgbMatch[3]);
            if (luminance < 128) theme = "dark";
        }
    } catch {
        /* ignore */
    }

    const cleanId = parentEntityId.replace(/[{}]/g, "").toLowerCase();

    const dataString =
        "parentEntityType=" + parentEntityType +
        "&parentEntityId=" + cleanId +
        "&parentEntityName=" + encodeURIComponent(parentEntityName) +
        "&containerId=" + containerId +
        "&theme=" + theme;

    xrm.Navigation.navigateTo(
        {
            pageType: "webresource",
            webresourceName: "sprk_documentuploadwizard",
            data: encodeURIComponent(dataString),
        },
        {
            target: 2,
            width: { value: 60, unit: "%" },
            height: { value: 70, unit: "%" },
        },
    ).then(
        () => { onDialogClosed?.(); },
        (err: any) => {
            if (err?.errorCode !== 2) {
                console.error("[Workspace] Upload dialog error:", err);
            }
        },
    );
}
```

### Step 2: Wire to the "+ Add Document" button

The Corporate Workspace already knows the parent entity context from its own URL parameters or state. Pass those through:

```tsx
<Button
    appearance="primary"
    icon={<DocumentAddRegular />}
    onClick={() =>
        openDocumentUploadWizard(
            entityType,      // e.g. "sprk_matter"
            entityId,        // from workspace URL params
            entityName,      // display name
            containerId,     // from workspace state
            () => refreshDocumentList(),
        )
    }
>
    Add Document
</Button>
```

---

## Container ID Resolution

The wizard requires a SharePoint Embedded container ID. The container ID can come from:

1. **Parent record field** — `sprk_containerid` on the entity form (fastest, synchronous)
2. **Business unit lookup** — Query `systemuser` → `businessunit` → `sprk_containerid` (async fallback)
3. **Passed as a parameter** — If the calling surface already has it (e.g. workspace state)

The `sprk_subgrid_commands.js` reference implementation tries option 1, then falls back to option 2 and writes the container ID back to the parent record for future use.

---

## Theme Detection

The wizard supports light and dark themes via the `theme` URL parameter. Use this standard snippet to detect the current Dataverse theme:

```typescript
let theme = "light";
try {
    const bodyBg = window.getComputedStyle(document.body).backgroundColor;
    const rgbMatch = bodyBg.match(/rgb\((\d+),\s*(\d+),\s*(\d+)\)/);
    if (rgbMatch) {
        const luminance =
            0.299 * parseInt(rgbMatch[1]) +
            0.587 * parseInt(rgbMatch[2]) +
            0.114 * parseInt(rgbMatch[3]);
        if (luminance < 128) theme = "dark";
    }
} catch { /* ignore */ }
```

---

## Post-Dialog Actions

After the wizard dialog closes:

1. **Refresh the document list/grid** — New `sprk_document` records have been created
2. **No success popup needed** — The wizard shows its own success screen before closing
3. **Handle cancellation gracefully** — `errorCode === 2` means the user cancelled; do not show an error

---

## Deployment

### Build (Two-Step Pipeline)

```bash
cd src/solutions/DocumentUploadWizard
npm run build                            # Step 1: Webpack → out/bundle.js
powershell -File build-webresource.ps1   # Step 2: Inline → out/sprk_documentuploadwizard.html
```

### Deploy to Dataverse

Upload two web resources via Power Apps maker portal:

| Web Resource | Type | File |
|-------------|------|------|
| `sprk_documentuploadwizard` | Webpage (HTML) | `out/sprk_documentuploadwizard.html` |
| `sprk_subgrid_commands` | Script (JS) | `out/sprk_subgrid_commands.js` |

Then **Save** and **Publish All**.

### Verify

1. Open a Matter (or other configured entity) form
2. Click "+ Add Documents" on the Documents subgrid
3. Wizard dialog opens at 60% x 70%
4. Upload files, verify record creation, test Next Steps

---

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| Dialog shows blank white page | `build-webresource.ps1` not run | Run the inline step after webpack build |
| HTTP 401 on record creation | Wrong token audience | Wizard uses `Xrm.WebApi` internally — ensure parent frame has Xrm |
| Container ID not found | Field empty + business unit not configured | Ensure `sprk_containerid` is set on the business unit |
| Dialog doesn't open | `Xrm.Navigation` not available | In Code Pages, resolve Xrm from parent/top frame |
| Wrong theme (light in dark mode) | Theme detection failed | Pass `theme=dark` explicitly if body background isn't readable |
| Subgrid doesn't refresh after upload | `selectedControl.refresh()` not called | Ensure the `onDialogClosed` callback triggers a grid refresh |

---

## Integration Pattern D: Standalone Launch (No Parent Context)

The wizard supports a **standalone mode** where no parent entity context is required. When launched without `parentEntityType` and `parentEntityId`, the wizard shows an "Associate To" step that lets users:

1. **Select a record** — pick an entity type (Matter, Project, etc.) and find a record via lookup
2. **Upload without association** — documents go to the business unit container with no parent record link

### Launch Function

Use `Spaarke_UploadDocumentsStandalone()` from `sprk_subgrid_commands.js`:

```javascript
// No parameters needed — standalone mode is detected automatically
Spaarke_UploadDocumentsStandalone();
```

This opens the same dialog (60% × 70%) with only the `theme` parameter. The wizard detects empty parent context and injects the "Associate To" step before "Add Files".

### How It Works

1. Wizard detects standalone mode (`parentEntityType` and `parentEntityId` are empty)
2. "Associate To" step appears as Step 1 (before "Add Files")
3. Entity types are loaded dynamically from `sprk_recordtype_ref` (polymorphic resolver pattern)
4. User selects a record via `Xrm.Utility.lookupObjects()` or checks "Upload without association"
5. Container ID is resolved from:
   - Selected record's `sprk_containerid` field (if associated)
   - Business unit `sprk_containerid` (if unassociated or record has no container)
6. Remaining wizard steps (Add Files, Summary, Next Steps) work identically

### Ribbon Configuration for Standalone Button

| Property | Value |
|----------|-------|
| Command ID | `Spaarke.Document.AddStandalone` |
| Function | `Spaarke_UploadDocumentsStandalone` |
| Library | `sprk_subgrid_commands` |
| CRM Parameters | (none required) |

### From a Code Page or PCF

```typescript
// Open standalone wizard — just pass theme, no parent context
const theme = "light"; // or detect from body background
const dataString = "theme=" + theme;

Xrm.Navigation.navigateTo(
    {
        pageType: "webresource",
        webresourceName: "sprk_documentuploadwizard",
        data: encodeURIComponent(dataString),
    },
    {
        target: 2,
        width: { value: 60, unit: "%" },
        height: { value: 70, unit: "%" },
    },
);
```

### Prerequisites

- `sprk_recordtype_ref` entity must have active rows for each supported entity type
- Business unit must have `sprk_containerid` configured (for unassociated uploads)
- `Xrm.Utility.lookupObjects` must be available in the parent frame

---

## RAG Indexing Contract (2026-05-22)

Every wizard upload triggers a Phase 4 call to `POST /api/ai/rag/index-file` so the document is searchable via the Semantic Search PCF, Find Similar, and the AI Search visualization graph. The contract between the wizard and the BFF is **load-bearing** — a partial caller produces orphan chunks (indexed but not linked back to Dataverse), which silently breaks the downstream UX.

### Required request body fields

```json
{
  "tenantId": "a221a95e-...",        // REQUIRED — Azure AD tenant; tenant isolation
  "driveId": "b!yLRdWE...",          // REQUIRED — SPE drive ID
  "itemId": "01LBYCMX5QS2...",       // REQUIRED — SPE drive-item ID (becomes speFileId in index)
  "fileName": "Memo.docx",           // REQUIRED — display name
  "documentId": "5f2b684b-...",      // REQUIRED for wizard/ribbon — Dataverse sprk_document GUID, lowercase
  "parentEntity": {                   // OPTIONAL but recommended when parent is known
    "entityType": "matter",          //   short form — strip the `sprk_` prefix
    "entityId": "12345...",          //   lowercase GUID
    "entityName": "Acme v Smith"
  }
}
```

### Why each field matters

| Field | If missing | Symptom users see |
|---|---|---|
| `documentId` | Index chunk has no Dataverse FK ("orphan") | Search Indexed = No on the Dataverse record; Find Similar can't resolve the source vector; Open / Download buttons can't navigate; nothing in DocumentRelationshipViewer graph |
| `parentEntity` | `parentEntityType` / `parentEntityId` null on chunk | Entity-scoped search misses the doc; matter / project workspaces won't surface it in context |
| `tenantId` | BFF returns 400 (validated) | Hard failure (visible) — not a silent issue |
| `documentId` not lowercased | Chunk has uppercase GUID, mismatches lowercase lookups | Find Similar / related-doc queries return no results for that document |

The BFF defensively lowercases `documentId` in `FileIndexingService.IndexTextInternalAsync`, but clients should also normalize at source for a cleaner contract. See [`.claude/patterns/ai/indexing-pipeline.md`](../../.claude/patterns/ai/indexing-pipeline.md) for the canonical convergence point.

### Verifying a wizard-uploaded document is correctly indexed

Three checks, in order:

```bash
# 1. Did the BFF endpoint return success? (App Insights)
az monitor app-insights query --apps spe-insights-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --analytics-query "traces | where timestamp > ago(15m) | where message has '<FileName>' | order by timestamp asc"
# Expect: "Resolved deployment ... IndexName=spaarke-knowledge-index-v2", then
#         "Batch indexed N/N documents", then
#         "Indexed <FileName> ... speFileId=01LBY... N/N chunks in Xms"

# 2. Did the chunk land with the linkage fields populated? (Azure AI Search)
SEARCH_KEY=$(az search admin-key show --service-name spaarke-search-dev \
  --resource-group spe-infrastructure-westus2 --query "primaryKey" -o tsv)
curl -s -H "api-key: $SEARCH_KEY" -H "Content-Type: application/json" \
  -X POST "https://spaarke-search-dev.search.windows.net/indexes/spaarke-knowledge-index-v2/docs/search?api-version=2024-07-01" \
  -d '{"search":"<FileName>","searchFields":"fileName","top":10,
       "select":"id,fileName,createdAt,documentId,parentEntityType,parentEntityId"}'
# Expect: every chunk has documentId populated and lowercase; parentEntityType set if scoped

# 3. Did the Dataverse tracking field flip to Yes?
# Open the sprk_document record — Search Indexed toggle should show Yes,
# sprk_searchindexed=true, sprk_searchindexedon = recent UTC timestamp.
```

### Failure modes documented in [.claude/FAILURE-MODES.md](../../.claude/FAILURE-MODES.md)

- **AP-2**: Orphan chunks from optional-field omission (the wizard / ribbon pair that drifted apart).
- **AP-3**: GUID case mismatch between Xrm and Dataverse Web API clients.
- **G-4**: Privilege filter on a non-filterable index field (the dev environment trap).

---

## Related

- [HOW-TO-ADD-SDAP-TO-NEW-ENTITY.md](HOW-TO-ADD-SDAP-TO-NEW-ENTITY.md) — Adding document upload support to new entities
- [PCF-DEPLOYMENT-GUIDE.md](PCF-DEPLOYMENT-GUIDE.md) — PCF control deployment
- [RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md](RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md) — Adding ribbon buttons
- [RAG-TROUBLESHOOTING.md](RAG-TROUBLESHOOTING.md) — Diagnostic commands and known failure modes for the indexing pipeline
- [`.claude/patterns/ai/indexing-pipeline.md`](../../.claude/patterns/ai/indexing-pipeline.md) — Pattern pointer file for indexing code
- ADR-006 — Code Pages for standalone dialogs
- ADR-021 — Fluent UI v9 design system
- ADR-022 — PCF platform libraries vs Code Page bundled React 18
