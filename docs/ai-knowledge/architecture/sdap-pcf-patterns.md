# SDAP PCF Control Patterns

> **Source**: SDAP-ARCHITECTURE-GUIDE.md (Component Architecture section)
> **Last Updated**: December 3, 2025
> **Applies To**: PCF control development, upload/preview UI changes

---

## TL;DR

Two PCF controls: **UniversalQuickCreate** (v2.3.0) for file upload, **SpeFileViewer** (v1.0.6) for preview/edit. Both use MSAL.js auth, Fluent UI, React. Upload control handles multi-file with progress; viewer embeds Office Online. Key config in `EntityDocumentConfig.ts`.

---

## Applies When

- Adding upload support to a new entity
- Modifying upload UI behavior
- Debugging PCF control issues
- Understanding file viewer functionality
- Building new PCF controls for SDAP

---

## Control 1: UniversalQuickCreate (Upload)

### Component Structure

```
UniversalQuickCreate/
├─ index.ts                    # Main PCF entry point
├─ components/
│   ├─ DocumentUploadForm.tsx  # Upload UI
│   ├─ FileList.tsx            # Selected files display
│   └─ UploadProgress.tsx      # Progress indicators
├─ services/
│   ├─ MsalAuthProvider.ts     # MSAL.js authentication
│   ├─ SdapApiClient.ts        # BFF API client
│   ├─ NavMapClient.ts         # Metadata discovery (Phase 7)
│   ├─ FileUploadService.ts    # Single file upload
│   ├─ MultiFileUploadService.ts # Batch orchestration
│   └─ DocumentRecordService.ts  # Dataverse record creation
└─ config/
    └─ EntityDocumentConfig.ts # Entity-relationship mappings
```

### Entity Configuration Pattern

```typescript
// EntityDocumentConfig.ts
export interface EntityDocumentConfig {
    entityName: string;              // e.g., "sprk_matter"
    lookupFieldName: string;         // e.g., "sprk_matter"
    relationshipSchemaName: string;  // e.g., "sprk_matter_document_1n"
    containerIdField: string;        // e.g., "sprk_containerid"
    displayNameField: string;        // e.g., "sprk_matternumber"
    entitySetName: string;           // e.g., "sprk_matters"
}

export const ENTITY_DOCUMENT_CONFIGS: Record<string, EntityDocumentConfig> = {
    'sprk_matter': {
        entityName: 'sprk_matter',
        lookupFieldName: 'sprk_matter',
        relationshipSchemaName: 'sprk_matter_document_1n',
        containerIdField: 'sprk_containerid',
        displayNameField: 'sprk_matternumber',
        entitySetName: 'sprk_matters'
    },
    'sprk_project': {
        entityName: 'sprk_project',
        lookupFieldName: 'sprk_project',
        relationshipSchemaName: 'sprk_Project_Document_1n',  // Note: case matters!
        containerIdField: 'sprk_containerid',
        displayNameField: 'sprk_projectname',
        entitySetName: 'sprk_projects'
    }
};
```

### Upload Flow Pattern

```typescript
// Simplified upload orchestration
async function uploadDocuments(files: File[], parentContext: ParentContext) {
    // 1. Get entity config
    const config = getEntityDocumentConfig(parentContext.entityName);
    
    // 2. Get auth token
    const token = await authProvider.getToken([
        'api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation'
    ]);
    
    // 3. Get navigation property name (Phase 7)
    const navMetadata = await navMapClient.getLookupNavigation(
        'sprk_document',
        config.relationshipSchemaName
    );
    
    // 4. Upload each file to SPE
    for (const file of files) {
        const driveItem = await sdapApiClient.uploadFile(
            file,
            parentContext.containerId,
            token
        );
        
        // 5. Create Dataverse record with correct binding
        const payload = {
            sprk_documentname: file.name,
            sprk_filename: file.name,
            sprk_filesize: file.size,
            sprk_graphitemid: driveItem.id,
            sprk_graphdriveid: parentContext.containerId,
            [`${navMetadata.navigationPropertyName}@odata.bind`]: 
                `/${config.entitySetName}(${parentContext.recordId})`
        };
        
        await context.webAPI.createRecord('sprk_document', payload);
    }
}
```

### Control Manifest Properties

```xml
<!-- ControlManifest.Input.xml -->
<property name="sdapApiBaseUrl" 
          display-name-key="API Base URL"
          of-type="SingleLine.Text" 
          usage="input" 
          required="true" />
```

---

## Control 2: SpeFileViewer (Preview/Edit)

### Component Structure

```
SpeFileViewer/
├─ index.ts                # PCF entry point
├─ FilePreview.tsx         # Main React component
├─ BffClient.ts            # API client
└─ SpeFileViewer.css       # Styles
```

### Preview Flow Pattern

```typescript
// FilePreview.tsx - loadPreview()
async function loadPreview(documentId: string) {
    // 1. Get token
    const token = await msalAuth.getToken([...]);
    
    // 2. Get preview URL from BFF
    const response = await bffClient.getPreviewUrl(documentId, token);
    // Response: { previewUrl: "https://...sharepoint.com/...", documentInfo: {...} }
    
    // 3. Render iframe
    setIframeSrc(response.previewUrl);
    // URL includes ?action=embedview&nb=true (hides SharePoint header)
}
```

### Editor Flow Pattern

```typescript
// FilePreview.tsx - handleOpenEditor()
async function handleOpenEditor() {
    // 1. Get office URL with permissions
    const response = await bffClient.getOfficeUrl(documentId, token);
    // Response: { 
    //   officeUrl: "https://...?action=edit&nb=true",
    //   permissions: { canEdit: true, role: "write" }
    // }
    
    // 2. Check permissions
    if (!response.permissions.canEdit) {
        setShowReadOnlyDialog(true);
    }
    
    // 3. Switch iframe to editor
    setIframeSrc(response.officeUrl);
    setMode('editor');
}
```

### Control Manifest Properties

```xml
<!-- ControlManifest.Input.xml -->
<property name="documentId" of-type="SingleLine.Text" usage="bound" required="true" />
<property name="bffApiUrl" of-type="SingleLine.Text" usage="input" required="true" />
<property name="clientAppId" of-type="SingleLine.Text" usage="input" required="true" />
<property name="bffAppId" of-type="SingleLine.Text" usage="input" required="true" />
<property name="tenantId" of-type="SingleLine.Text" usage="input" required="true" />
<property name="controlHeight" of-type="Whole.None" usage="input" default-value="600" />
```

### Responsive Height Pattern

```typescript
// index.ts - init()
const controlHeight = context.parameters.controlHeight?.raw ?? 600;
this.container.style.minHeight = `${controlHeight}px`;
this.container.style.height = '100%';
this.container.style.display = 'flex';
this.container.style.flexDirection = 'column';
```

---

## Supported File Types

### Office Files (Preview + Edit)
- Word: docx, doc, docm, dot, dotx, dotm
- Excel: xlsx, xls, xlsm, xlsb, xlt, xltx, xltm
- PowerPoint: pptx, ppt, pptm, pot, potx, potm, pps, ppsx, ppsm

### Other Files (Preview Only)
- PDF, images (png, jpg, gif), text files

---

## Deployment

```bash
# Build
cd src/controls/UniversalQuickCreate
npm install
npm run build

# Deploy
pac auth create --url https://spaarkedev1.crm.dynamics.com
pac pcf push --publisher-prefix sprk

# Verify
# Power Apps → Solutions → Default Solution → Custom controls
# Should show: Spaarke.Controls.UniversalDocumentUpload v2.3.0
```

---

## Adding New Entity Support

**Step 1**: Add configuration to `EntityDocumentConfig.ts`:
```typescript
'sprk_invoice': {
    entityName: 'sprk_invoice',
    lookupFieldName: 'sprk_invoice',
    relationshipSchemaName: 'sprk_invoice_document',  // Exact Dataverse name!
    containerIdField: 'sprk_containerid',
    displayNameField: 'sprk_invoicenumber',
    entitySetName: 'sprk_invoices'
}
```

**Step 2**: Ensure Dataverse has:
- `sprk_containerid` field on parent entity
- 1:N relationship to `sprk_document`
- Lookup field on `sprk_document`

**Step 3**: Rebuild and deploy:
```bash
npm run build
pac pcf push --publisher-prefix sprk
```

---

## Common Mistakes

| Mistake | Effect | Fix |
|---------|--------|-----|
| Wrong `relationshipSchemaName` | NavMap API returns 404 | Check exact name in Dataverse relationships |
| Missing `sprk_containerid` on parent | Upload fails | Add field to parent entity |
| Hardcoded navigation property | Wrong case causes 400 | Use Phase 7 NavMapClient |
| Double `/api` in URL | 404 on API calls | NavMapClient strips trailing `/api` |

---

## Debugging

### Console Logging
```javascript
// Built-in logging
[UniversalQuickCreate] Initializing...
[NavMapClient] Lookup navigation retrieved { navigationPropertyName: "sprk_Matter" }
[FileUploadService] Upload complete: document.pdf
[DocumentRecordService] Record created: ca5bbb9f-...
```

### Common Issues
- **401 on upload**: Token expired, refresh page
- **404 on NavMap**: Wrong relationship name in config
- **400 on record create**: Navigation property case mismatch

---

## Related Articles

- [sdap-overview.md](sdap-overview.md) - System architecture
- [sdap-auth-patterns.md](sdap-auth-patterns.md) - MSAL.js token handling
- [sdap-bff-api-patterns.md](sdap-bff-api-patterns.md) - Backend endpoints

---

*Condensed from PCF component sections of architecture guide*
