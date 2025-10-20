# How to Add SPE PCF Document Upload to New Entities

**Version:** 2.3.0 (Phase 7 - Auto-Discovery)
**Last Updated:** 2025-10-19
**Prerequisites:** Phase 7 NavMap service deployed

---

## Overview

This guide explains how to enable the SharePoint Embedded (SPE) PCF document upload control for a new parent entity in Dataverse. With Phase 7's navigation property auto-discovery, this process takes **15-30 minutes** compared to 2-4 hours in previous versions.

**What This Enables:**
- Upload files to SharePoint Embedded from entity forms
- Automatically create Document records in Dataverse
- Link documents to parent records via lookup
- Store file metadata (name, size, Graph item ID)

---

## Before You Start

### Required Dataverse Configuration

1. **Parent Entity Must Have:**
   - `sprk_containerid` field (Text, stores SPE Container ID)
   - A display name field (e.g., `sprk_matternumber`, `name`, `accountnumber`)

2. **Document Entity (`sprk_document`) Must Have:**
   - Lookup field to parent entity (e.g., `sprk_matter`, `sprk_account`)
   - Relationship to parent entity (schema name like `parent_document`)

3. **Parent Entity Form Must Have:**
   - Command button or ribbon button to launch PCF control
   - Or embedded PCF control on form

---

## Step-by-Step Process

### Step 1: Add Entity to Configuration (5 minutes)

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/config/EntityDocumentConfig.ts`

**Add your entity** to `ENTITY_DOCUMENT_CONFIGS`:

```typescript
export const ENTITY_DOCUMENT_CONFIGS: Record<string, EntityDocumentConfig> = {
    // ... existing entities ...

    // NEW: Your entity
    'your_entity': {  // Replace with actual entity logical name
        entityName: 'your_entity',
        lookupFieldName: 'your_entity',        // Lookup field on sprk_document
        relationshipSchemaName: 'your_entity_document',  // Relationship schema name
        containerIdField: 'sprk_containerid',  // Field containing SPE Container ID
        displayNameField: 'your_displayfield', // Field for display (e.g., 'name')
        entitySetName: 'your_entities',        // Plural OData collection name
        // navigationPropertyName is AUTO-DISCOVERED by NavMap service!
    },
};
```

**Example for Account:**
```typescript
'account': {
    entityName: 'account',
    lookupFieldName: 'sprk_account',        // Lookup on sprk_document
    relationshipSchemaName: 'account_document',  // Relationship name
    containerIdField: 'sprk_containerid',
    displayNameField: 'name',               // Account name field
    entitySetName: 'accounts',              // Standard entity set
},
```

---

### Step 2: Update Server NavMap Configuration (2 minutes)

**File:** `src/api/Spe.Bff.Api/appsettings.json` (or environment variables)

**Add your entity** to the NavMap parent list:

```json
{
  "NavigationMetadata": {
    "ChildEntity": "sprk_document",
    "Parents": [
      "sprk_matter",
      "sprk_project",
      "sprk_invoice",
      "account",
      "contact",
      "your_entity"  // ADD YOUR ENTITY HERE
    ]
  }
}
```

**What This Does:**
- Server queries metadata for your entity
- Auto-discovers navigation property name (case-sensitive!)
- Caches result for 5 minutes
- Returns in NavMap response to PCF

---

### Step 3: Deploy Configuration Changes (15 minutes)

**Deployment Order (CRITICAL):**

#### 3a. Deploy Backend First (5-10 min)

```bash
# Build and deploy Spe.Bff.Api
cd src/api/Spe.Bff.Api
dotnet publish -c Release
az webapp deploy --resource-group <rg> --name <webapp-name> --src-path <publish-folder>
```

**Verify:**
```bash
# Test NavMap endpoint
curl https://<api-url>/api/pcf/dataverse-navmap?v=1

# Should return your new entity in response:
# {
#   "your_entity": {
#     "entitySet": "your_entities",
#     "lookupAttribute": "your_entity",
#     "navProperty": "YourEntity",  // Auto-discovered!
#     ...
#   }
# }
```

#### 3b. Deploy PCF Second (5-10 min)

```bash
# Build and deploy PCF control
cd src/controls/UniversalQuickCreate
npm run build
pac pcf push --publisher-prefix sprk
```

**Verify:**
- PCF version shows v2.3.0 or higher
- Control loads without errors
- NavMap logged in browser console

---

### Step 4: Configure Parent Entity Form (5 minutes)

#### Option A: Command Button (Recommended)

1. Open parent entity form in designer
2. Add command button to ribbon
3. Configure button to open PCF control in dialog
4. Pass parameters:
   - `parentEntityName`: Your entity logical name
   - `parentRecordId`: Current record ID
   - `containerId`: SPE Container ID field value
   - `parentDisplayName`: Display field value

**Example Button XML:**
```xml
<CommandDefinition Id="cmdOpenDocumentUpload">
  <EnableRules>
    <EnableRule Id="recordSelected" />
  </EnableRules>
  <DisplayRules>
    <DisplayRule Id="always" />
  </DisplayRules>
  <Actions>
    <JavaScriptFunction Library="sprk_documentupload.js" FunctionName="openDocumentUpload">
      <StringParameter Value="your_entity" />
    </JavaScriptFunction>
  </Actions>
</CommandDefinition>
```

**JavaScript:**
```javascript
function openDocumentUpload(entityName) {
    var formContext = Xrm.Page;
    var recordId = formContext.data.entity.getId();
    var containerId = formContext.getAttribute("sprk_containerid").getValue();
    var displayName = formContext.getAttribute("name").getValue(); // Adjust field

    Xrm.Navigation.openDialog({
        pageType: "custom",
        name: "sprk_documentupload_page",
        data: {
            parentEntityName: entityName,
            parentRecordId: recordId,
            containerId: containerId,
            parentDisplayName: displayName
        }
    });
}
```

#### Option B: Embedded on Form

1. Add PCF control to form section
2. Bind parameters to form fields
3. Configure visibility rules

---

### Step 5: Test the Integration (10 minutes)

#### 5.1 Basic Upload Test

1. Open a record of your new entity
2. Click document upload button (or navigate to PCF section)
3. Select a file
4. Click Upload
5. **Expected:**
   - File uploads to SPE ✅
   - Document record created ✅
   - Lookup to parent populated ✅
   - No errors in console ✅

#### 5.2 Verify Navigation Property

**Check browser console:**
```
[NavMapClient] Loaded navigation map from server
[NavMapClient] Navigation entry for your_entity: {...}
[DocumentRecordService] Using navigation property: YourEntity  // Check case!
[DocumentRecordService] Created Document record: {guid}
```

**Verify in Dataverse:**
```
Navigate to parent record → Related → Documents
Should see new document record with correct lookup
```

#### 5.3 Test Fallback (Optional)

**Simulate server down:**
1. Disable Spe.Bff.Api temporarily
2. Reload PCF control
3. Should see: `[NavMapClient] Server unavailable, using fallback`
4. Upload should still work if hardcoded fallback exists

---

## Troubleshooting

### Issue: "Unsupported entity type: your_entity"

**Cause:** Entity not in EntityDocumentConfig

**Fix:**
1. Verify `ENTITY_DOCUMENT_CONFIGS` includes your entity
2. Check spelling matches exactly (case-sensitive)
3. Rebuild and redeploy PCF

---

### Issue: "Navigation property not configured"

**Cause:** NavMap didn't return entry OR hardcoded fallback missing

**Fix:**
1. Check server NavMap response: `/api/pcf/dataverse-navmap?v=1`
2. Verify entity in `Parents` list (appsettings.json)
3. Check server logs for metadata query errors
4. Add hardcoded fallback if needed (temporary):

```typescript
const NAVMAP_FALLBACK: NavMap = {
    your_entity: {
        entitySet: "your_entities",
        lookupAttribute: "your_entity",
        navProperty: "YourEntity"  // ⚠️ Validate case via PowerShell!
    }
};
```

---

### Issue: "undeclared property 'your_entity'" (OData error)

**Cause:** Navigation property case incorrect

**Debug:**
1. Check browser console: `Using navigation property: ???`
2. Compare to NavMap response: `navProperty: "???"`
3. If different, NavMap has wrong case

**Fix:**
1. Validate correct case via PowerShell:

```powershell
$token = az account get-access-token --resource https://yourorg.crm.dynamics.com --query accessToken -o tsv
$headers = @{ Authorization = "Bearer $token" }
$base = "https://yourorg.crm.dynamics.com/api/data/v9.2"

$query = "EntityDefinitions(LogicalName='sprk_document')?`$expand=ManyToOneRelationships(`$select=SchemaName,ReferencingEntityNavigationPropertyName;`$filter=SchemaName eq 'your_entity_document')"
$result = Invoke-RestMethod -Uri "$base/$query" -Headers $headers
$navProp = $result.ManyToOneRelationships[0].ReferencingEntityNavigationPropertyName
Write-Host "Correct navigation property: $navProp"
```

2. Update server metadata query if needed
3. Clear cache: `sessionStorage.clear()` and reload

---

### Issue: Documents created but lookup not populated

**Cause:** Wrong entity set name OR wrong navigation property

**Debug:**
Check payload in network tab:
```json
{
  "YourEntity@odata.bind": "/your_entities(guid)"  // Must match exactly
}
```

**Fix:**
1. Verify `entitySetName` in config matches actual OData collection
2. Verify navigation property case matches metadata
3. Check Dataverse shows correct lookup field name

---

## Validation Checklist

After completing all steps, verify:

- [ ] Entity added to `EntityDocumentConfig.ts`
- [ ] Entity added to NavMap `Parents` list (server config)
- [ ] Backend deployed and NavMap endpoint returns entity
- [ ] PCF deployed (v2.3.0+)
- [ ] Form button/control configured
- [ ] Test upload succeeds
- [ ] Document record created
- [ ] Lookup field populated correctly
- [ ] No console errors
- [ ] NavMap logs show correct navigation property
- [ ] Fallback tested (optional)

---

## Advanced: Manual Navigation Property Validation (Pre-Phase 7)

**Only needed if NavMap service unavailable** or for validation:

```powershell
# Get access token
$token = az account get-access-token --resource https://yourorg.crm.dynamics.com --query accessToken -o tsv
$headers = @{ Authorization = "Bearer $token"; Accept = "application/json" }
$base = "https://yourorg.crm.dynamics.com/api/data/v9.2"

# 1. Get entity set name
$query = "EntityDefinitions(LogicalName='your_entity')?`$select=EntitySetName"
$result = Invoke-RestMethod -Uri "$base/$query" -Headers $headers
$entitySetName = $result.EntitySetName
Write-Host "Entity Set Name: $entitySetName"

# 2. Get lookup navigation property
$query = "EntityDefinitions(LogicalName='sprk_document')?`$expand=ManyToOneRelationships(`$select=SchemaName,ReferencingEntityNavigationPropertyName;`$filter=SchemaName eq 'your_entity_document')"
$result = Invoke-RestMethod -Uri "$base/$query" -Headers $headers
$navProp = $result.ManyToOneRelationships[0].ReferencingEntityNavigationPropertyName
Write-Host "Navigation Property: $navProp"  # ⚠️ Note the case!

# 3. Test document creation payload
$payload = @{
    sprk_documentname = "Test Document"
    sprk_filename = "test.pdf"
    "$navProp@odata.bind" = "/$entitySetName(test-guid-here)"
} | ConvertTo-Json

Write-Host "`nTest Payload:"
Write-Host $payload
```

---

## Best Practices

1. **Always deploy backend before PCF**
   - NavMap service must be available when PCF loads
   - PCF falls back gracefully if server unavailable

2. **Test in dev environment first**
   - Verify NavMap returns correct metadata
   - Test upload and record creation
   - Check console logs for warnings

3. **Monitor cache hit rate**
   - Should be >95% after first load
   - Low hit rate indicates caching issues

4. **Document custom configuration**
   - If you override defaults (entitySetName, etc.)
   - Note why and when changes were made

5. **Use environment variables for NavMap URL**
   - Makes it easy to switch environments
   - Avoid hardcoding URLs in code

---

## Rollback Procedure

**If new entity causes issues:**

1. **Remove from config** (immediate fix):
```typescript
// Comment out in EntityDocumentConfig.ts
// 'your_entity': { ... },
```

2. **Remove from server** (if needed):
```json
// Remove from appsettings.json Parents list
```

3. **Redeploy:**
```bash
# Rebuild and deploy
npm run build
pac pcf push --publisher-prefix sprk
```

4. **Verify:**
- NavMap no longer returns problematic entity
- Existing entities still work
- No console errors

---

## Support

**Questions or Issues:**
- Check troubleshooting section above
- Review Phase 7 task documents for detailed implementation
- Consult PHASE-7-ASSESSMENT.md for technical details

**Logs to Check:**
- Browser console: `[NavMapClient]`, `[DocumentRecordService]`
- Server logs: NavMapController, NavigationMetadataService
- Dataverse trace logs: Document creation errors

---

**Document Version:** 2.3.0
**Last Updated:** 2025-10-19
**Maintained By:** Development Team
