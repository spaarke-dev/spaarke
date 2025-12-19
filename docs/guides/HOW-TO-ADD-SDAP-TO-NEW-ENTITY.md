# How to Add SDAP (Document Upload) to a New Entity

**Version:** 1.0.0
**Last Updated:** October 20, 2025
**Phase:** 7 (Dynamic Metadata Discovery)

---

## Overview

This guide explains how to enable SDAP (SharePoint Embedded Document Attachment Platform) document upload functionality for a new Dataverse entity. With Phase 7 dynamic metadata discovery, most configuration is automatic.

**Time Required:** 15-30 minutes
**Prerequisites:** System Administrator role in Dataverse, Power Platform maker access

---

## Quick Checklist

- [ ] 1. Add `hip_ContainerId` field to parent entity
- [ ] 2. Create 1:N relationship between parent entity and `sprk_document`
- [ ] 3. Add Document subgrid to parent entity form
- [ ] 4. Update PCF control configuration (EntityDocumentConfig.ts)
- [ ] 5. Build and deploy PCF control
- [ ] 6. Test document upload functionality

---

## Detailed Steps

### Step 1: Add Container ID Field to Parent Entity

Every entity that supports document uploads needs a field to store the SharePoint Embedded container ID.

**Power Apps Maker Portal:**
1. Go to [Power Apps](https://make.powerapps.com/)
2. Select environment: **SPAARKE DEV 1** (or your target environment)
3. **Tables** → Find your entity (e.g., `hip_project`)
4. **Columns** → **+ New column**
5. Configure:
   - **Display name:** `Container ID`
   - **Schema name:** `hip_ContainerId` ⚠️ **Must match this exact casing**
   - **Data type:** Text → Single line of text
   - **Max length:** 200
   - **Required:** No (optional - will be created on first document upload)
6. Click **Save**

**Why This Field:**
- Stores the SharePoint Embedded container Drive ID (e.g., `b!yLRdWEOAdkaWXskuRfByI...`)
- Created automatically when first document is uploaded to a record
- Links the Dataverse record to its SPE document container

---

### Step 2: Create 1:N Relationship

Create a **One-to-Many** relationship from your parent entity to `sprk_document`.

**Power Apps Maker Portal:**
1. **Tables** → Select your entity (e.g., `hip_project`)
2. **Relationships** tab
3. **+ New relationship** → **One-to-many**
4. Configure:
   - **Related table:** `sprk_document` (Document)
   - **Lookup column (on sprk_document):**
     - **Display name:** `[Your Entity Name]` (e.g., "Project")
     - **Schema name:** `hip_Project` ⚠️ **Must use capital P** (matches entity schema name casing)
   - **Relationship name:** Will auto-generate (e.g., `hip_Project_Document_1n`)
     - ⚠️ **Note this name exactly** - you'll need it for PCF configuration
5. Click **Save**
6. **Publish customizations**

**Relationship Naming Convention:**
- Format: `{ParentEntity}_{ChildEntity}_{Suffix}`
- Example: `hip_Project_Document_1n`
- The lookup navigation property will be: `hip_Project` (capital P)

---

### Step 3: Add Document Subgrid to Form

Add a subgrid to display documents and enable the Universal Quick Create button.

**Power Apps Maker Portal:**
1. **Tables** → Your entity → **Forms** tab
2. Open the **Main form** (Information form)
3. Add a **Section** (if needed):
   - **Label:** "Documents" or "Attachments"
   - **Columns:** 1 (full width recommended)
4. Inside the section, add **Subgrid**:
   - **Label:** "Documents"
   - **Table:** `Document (sprk_document)`
   - **Default view:** `Active Documents` or create custom view
   - **Relationship:** Select the relationship you created (e.g., `hip_Project_Document_1n`)
5. **Subgrid properties** → **Controls** tab:
   - Add **Read-only Grid** control (if not present)
   - Configure grid to show relevant columns (e.g., `sprk_documentname`, `sprk_filesize`, `createdon`)
6. **Save** and **Publish**

**Command Button:**
The Universal Quick Create button is added automatically by the ribbon customization. If missing:
- Check ribbon customizations are published
- Verify the `sprk_uploadcontext` subgrid command button is configured

---

### Step 4: Update PCF Control Configuration

Add your entity to the PCF control configuration file.

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/config/EntityDocumentConfig.ts`

**Add New Entity Entry:**
```typescript
/**
 * [Your Entity Name] Entity (hip_project)
 * [Brief description of what this entity represents]
 */
'hip_project': {
    entityName: 'hip_project',                          // Entity logical name
    lookupFieldName: 'hip_project',                     // Lookup field on sprk_document
    relationshipSchemaName: 'hip_Project_Document_1n',  // ⚠️ EXACT relationship name from Step 2
    containerIdField: 'hip_containerid',                // Container ID field from Step 1 (lowercase!)
    displayNameField: 'hip_projectname',                // Primary name field for display
    entitySetName: 'hip_projects'                       // Plural form for OData queries
},
```

**Field Mappings:**
- `entityName`: Entity logical name (e.g., `hip_project`)
- `lookupFieldName`: Lookup field schema name on `sprk_document` (same as entity name)
- `relationshipSchemaName`: **CRITICAL** - Must match the exact relationship name from Step 2
- `containerIdField`: The field created in Step 1 (use lowercase: `hip_containerid`)
- `displayNameField`: Primary name field (e.g., `hip_projectname`, `name`, `sprk_matternumber`)
- `entitySetName`: Plural entity set name for OData (e.g., `hip_projects`, `accounts`, `sprk_matters`)

**Example - Standard Dynamics Entity (Account):**
```typescript
'account': {
    entityName: 'account',
    lookupFieldName: 'sprk_account',                    // Custom field on sprk_document
    relationshipSchemaName: 'sprk_Account_Document_1n', // Note: capital A
    containerIdField: 'sprk_containerid',               // Custom field added to account
    displayNameField: 'name',                           // Standard account name field
    entitySetName: 'accounts'
},
```

---

### Step 5: Build and Deploy PCF Control

Build the updated PCF control and deploy to Dataverse.

**Command Line:**
```bash
# Navigate to PCF project
cd /c/code_files/spaarke/src/controls/UniversalQuickCreate

# Build the control
npm run build

# Deploy to Dataverse (requires pac CLI authentication)
pac pcf push --publisher-prefix sprk
```

**Verification:**
- Build should complete with no errors
- Deploy should show "Solution Imported successfully"
- Customizations are published automatically

**Alternative - Manual Deployment:**
If `pac pcf push` fails, use the solution file:
```bash
# Build solution package
msbuild /t:Rebuild /p:Configuration=Release

# Import via Power Apps
# Solutions → Import → Select bin/Debug/UniversalQuickCreateSolution.zip
```

---

### Step 6: Test Document Upload

Verify the configuration works end-to-end.

**Testing Steps:**
1. Open a record of your new entity (e.g., a Project record)
2. Navigate to the **Documents** subgrid
3. Click **Universal Quick Create** button (command bar)
4. **Upload a test file**:
   - Small file (< 1MB) recommended for first test
   - Any file type supported (PDF, DOCX, TXT, etc.)
5. **Verify in browser console** (F12 → Console):
   ```javascript
   [Phase 7] Querying navigation metadata for hip_project
   NavMapClient: Getting lookup navigation {
     childEntity: 'sprk_document',
     relationship: 'hip_Project_Document_1n'
   }
   NavMapClient: Lookup navigation retrieved {
     navigationPropertyName: 'hip_Project',  // ← Correct case!
     source: 'dataverse'
   }
   Created Document record: [GUID] ✅
   ```
6. **Verify Document record created**:
   - Refresh the subgrid
   - Document should appear in the list
   - Click to open - should link to SPE file
7. **Verify Container ID populated**:
   - Check the parent record's `hip_ContainerId` field
   - Should contain a long string starting with `b!`
   - This was created automatically on first upload

**Second Upload Test (Cache Verification):**
1. Upload another file to the same record
2. Check browser console:
   ```javascript
   NavMapClient: Lookup navigation retrieved {
     source: 'cache'  // ← Second upload uses cached metadata!
   }
   ```

---

## Troubleshooting

### Issue: "Metadata not found. The entity or relationship may not exist in Dataverse."

**Cause:** Relationship schema name in PCF config doesn't match actual Dataverse relationship.

**Fix:**
1. In Power Apps → Tables → Your Entity → Relationships
2. Find the relationship to `sprk_document`
3. Click to view details → Note the **Relationship Name** field
4. Update `EntityDocumentConfig.ts` with the EXACT name (case-sensitive!)
5. Rebuild and redeploy PCF control

**Example:**
```typescript
// Wrong
relationshipSchemaName: 'hip_project_document',

// Correct (matches actual Dataverse relationship)
relationshipSchemaName: 'hip_Project_Document_1n',
```

---

### Issue: Document uploads but doesn't appear in subgrid

**Cause:** Subgrid relationship doesn't match the lookup field created.

**Fix:**
1. Edit form → Subgrid properties
2. **Relationship** dropdown → Select the correct relationship
3. Save and Publish

---

### Issue: "Failed to create Document record" - 400 Bad Request

**Cause:** Navigation property name case mismatch.

**Fix:**
This should be automatically resolved by Phase 7 dynamic metadata discovery. If it persists:
1. Check browser console for the navigation property name returned by NavMap API
2. Verify the relationship exists in Dataverse
3. Check BFF API logs for Dataverse query errors

---

### Issue: Container ID not populating

**Cause:** Field name mismatch in PCF configuration.

**Fix:**
1. Verify the field exists on your entity: `hip_containerid` (all lowercase)
2. Update `EntityDocumentConfig.ts`:
   ```typescript
   containerIdField: 'hip_containerid',  // Must match actual field logical name
   ```
3. Rebuild and redeploy

---

## Advanced Configuration

### Custom Display Name Field

If your entity doesn't have a simple name field:

```typescript
displayNameField: 'hip_projectnumber',  // Or concatenated field if available
```

For composite names, you may need to customize the UI layer. The display name is used in upload dialog headers and notifications.

### Multiple Relationships to Document

If you need multiple types of document relationships (e.g., "Project Documents" vs "Project Templates"):

1. Create separate relationships with descriptive names:
   - `hip_Project_Document_Standard_1n`
   - `hip_Project_Document_Template_1n`
2. Add separate entries in `EntityDocumentConfig.ts`:
   ```typescript
   'hip_project_standard': {
       entityName: 'hip_project',
       relationshipSchemaName: 'hip_Project_Document_Standard_1n',
       // ... other config
   },
   'hip_project_template': {
       entityName: 'hip_project',
       relationshipSchemaName: 'hip_Project_Document_Template_1n',
       // ... other config
   },
   ```
3. Configure separate subgrids on the form

---

## Updating Existing Relationship Names

If you need to rename an existing relationship to follow standard naming conventions (e.g., changing `sprk_matter_document` to `sprk_matter_document_1n`), follow these steps carefully.

### When to Update Relationship Names

**Scenarios:**
- Standardizing relationship naming across all entities
- Fixing inconsistent naming conventions
- Aligning with organizational standards (e.g., always use `_1n` suffix)
- Migrating from legacy relationships

**⚠️ Important Considerations:**
- **Existing data is preserved** - Dataverse allows relationship renaming without data loss
- **Subgrids must be updated** - Forms reference relationships by schema name
- **PCF configuration must be updated** - `EntityDocumentConfig.ts` must match new name
- **No code changes in BFF API** - Phase 7 discovers metadata automatically
- **Test thoroughly** - Verify uploads work after relationship rename

---

### Steps to Rename a Relationship

#### Step 1: Update Relationship in Dataverse

**Power Apps Maker Portal:**
1. Go to [Power Apps](https://make.powerapps.com/)
2. Select environment: **SPAARKE DEV 1**
3. **Tables** → `sprk_matter` (or your entity)
4. **Relationships** tab
5. Find the relationship to `sprk_document`
6. **Delete the old relationship** (don't worry - data is preserved):
   - Click on the relationship (e.g., `sprk_matter_document`)
   - Click **Delete relationship**
   - ⚠️ **CRITICAL:** Note any forms/subgrids using this relationship (you'll need to update them)
   - Confirm deletion
7. **Create new relationship** with standard naming:
   - **+ New relationship** → **One-to-many**
   - **Related table:** `sprk_document`
   - **Lookup column:**
     - **Display name:** "Matter" (or your entity name)
     - **Schema name:** `sprk_Matter` (use proper casing)
   - **Relationship name:** Manually set to `sprk_matter_document_1n` (or your standard format)
   - Click **Save**
8. **Publish customizations**

**Result:** New relationship created with standardized name. Existing document records are preserved but no longer linked until subgrids are updated.

---

#### Step 2: Update Form Subgrids

**For each form with a Document subgrid:**
1. **Tables** → Your entity → **Forms** tab
2. Open each form that has a Documents subgrid
3. Click on the **Documents subgrid**
4. **Subgrid properties:**
   - **Relationship:** Select the NEW relationship name (e.g., `sprk_matter_document_1n`)
   - Click **OK**
5. **Save** and **Publish** the form

**Verification:**
- Open a record with existing documents
- The Documents subgrid should now show existing documents (they're re-linked via the new relationship)

---

#### Step 3: Update PCF Configuration

**ONLY THIS FILE NEEDS TO BE UPDATED** - No other code changes required!

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/config/EntityDocumentConfig.ts`

**Find the entity entry and update `relationshipSchemaName`:**
```typescript
// Before
'sprk_matter': {
    entityName: 'sprk_matter',
    lookupFieldName: 'sprk_matter',
    relationshipSchemaName: 'sprk_matter_document',  // ← OLD NAME
    containerIdField: 'sprk_containerid',
    displayNameField: 'sprk_matternumber',
    entitySetName: 'sprk_matters'
},

// After
'sprk_matter': {
    entityName: 'sprk_matter',
    lookupFieldName: 'sprk_matter',
    relationshipSchemaName: 'sprk_matter_document_1n',  // ← NEW NAME
    containerIdField: 'sprk_containerid',
    displayNameField: 'sprk_matternumber',
    entitySetName: 'sprk_matters'
},
```

**That's it!** No other files need changes because Phase 7 queries metadata dynamically.

---

#### Step 4: Build and Deploy PCF Control

```bash
cd /c/code_files/spaarke/src/controls/UniversalQuickCreate
npm run build
pac pcf push --publisher-prefix sprk
```

---

#### Step 5: Test Document Upload

1. Open a record of the updated entity
2. Upload a test document
3. Verify in browser console:
   ```javascript
   [Phase 7] Querying navigation metadata for sprk_matter
   NavMapClient: Getting lookup navigation {
     relationship: 'sprk_matter_document_1n'  // ← New relationship name
   }
   NavMapClient: Lookup navigation retrieved {
     navigationPropertyName: 'sprk_Matter',   // ← Discovered automatically
     source: 'dataverse'
   }
   Created Document record: [GUID] ✅
   ```

---

### AI Code Agent Prompts

Use these prompts with AI coding assistants (Claude Code, GitHub Copilot, etc.) to make the required changes:

#### Prompt 1: Update Single Entity Relationship Name

```
Update the relationship schema name for the sprk_matter entity in the PCF configuration.

File: src/controls/UniversalQuickCreate/UniversalQuickCreate/config/EntityDocumentConfig.ts

Change the relationshipSchemaName for 'sprk_matter' from 'sprk_matter_document' to 'sprk_matter_document_1n'

Only change the relationshipSchemaName property. Do not modify any other properties.
```

**Expected AI Action:**
- Opens `EntityDocumentConfig.ts`
- Finds the `'sprk_matter'` entry
- Changes `relationshipSchemaName: 'sprk_matter_document'` to `relationshipSchemaName: 'sprk_matter_document_1n'`
- Saves the file

---

#### Prompt 2: Update Multiple Entity Relationships to Standard Convention

```
Update all entity relationship schema names in the PCF configuration to follow the standard naming convention: {entity}_{document}_1n

File: src/controls/UniversalQuickCreate/UniversalQuickCreate/config/EntityDocumentConfig.ts

For each entity in ENTITY_DOCUMENT_CONFIGS, update the relationshipSchemaName to use the format:
- sprk_matter: 'sprk_matter_document_1n'
- sprk_project: 'sprk_project_document_1n'
- sprk_invoice: 'sprk_invoice_document_1n'
- account: 'sprk_account_document_1n'
- contact: 'sprk_contact_document_1n'

Only modify the relationshipSchemaName properties. Preserve all other configuration values.
```

**Expected AI Action:**
- Opens `EntityDocumentConfig.ts`
- For each entity entry, updates `relationshipSchemaName` to match the standard `{entity}_document_1n` pattern
- Preserves all other properties unchanged
- Saves the file

---

#### Prompt 3: Add New Entity and Update Existing Relationship

```
Add a new entity configuration for hip_client and update the sprk_matter relationship name.

File: src/controls/UniversalQuickCreate/UniversalQuickCreate/config/EntityDocumentConfig.ts

1. Update sprk_matter:
   - Change relationshipSchemaName from 'sprk_matter_document' to 'sprk_matter_document_1n'

2. Add new entry for hip_client:
   ```typescript
   'hip_client': {
       entityName: 'hip_client',
       lookupFieldName: 'hip_client',
       relationshipSchemaName: 'hip_Client_Document_1n',
       containerIdField: 'hip_containerid',
       displayNameField: 'hip_clientname',
       entitySetName: 'hip_clients'
   },
   ```

After making changes, build and deploy the PCF control:
```bash
cd /c/code_files/spaarke/src/controls/UniversalQuickCreate
npm run build
pac pcf push --publisher-prefix sprk
```
```

**Expected AI Action:**
- Opens `EntityDocumentConfig.ts`
- Updates `sprk_matter` relationship name
- Adds new `hip_client` entry after existing entries
- Runs build and deploy commands
- Reports success/failure

---

#### Prompt 4: Verify Current Relationship Names

```
List all current relationship schema names from the PCF configuration to verify they match Dataverse.

File: src/controls/UniversalQuickCreate/UniversalQuickCreate/config/EntityDocumentConfig.ts

Output a table showing:
- Entity Name
- Current relationshipSchemaName
- Expected standard name ({entity}_document_1n)
- Match status (✓ or ✗)
```

**Expected AI Output:**
```
| Entity Name   | Current Relationship          | Standard Name                | Status |
|---------------|-------------------------------|------------------------------|--------|
| sprk_matter   | sprk_matter_document          | sprk_matter_document_1n      | ✗      |
| sprk_project  | sprk_Project_Document_1n      | sprk_project_document_1n     | ✓      |
| sprk_invoice  | sprk_invoice_document         | sprk_invoice_document_1n     | ✗      |
| account       | account_document              | sprk_account_document_1n     | ✗      |
| contact       | contact_document              | sprk_contact_document_1n     | ✗      |
```

---

### Why Only PCF Config Needs Updates

**With Phase 7 Dynamic Metadata Discovery:**

```
❌ Before Phase 7 (Hardcoded):
PCF Config → Hardcoded navigation property → Document creation
└─ If relationship renamed → PCF config AND hardcoded values need updates

✅ After Phase 7 (Dynamic):
PCF Config → NavMap API → Dataverse metadata → Navigation property discovered
└─ If relationship renamed → ONLY PCF config needs update
```

**What Doesn't Need Updates:**
- ✅ **BFF API code** - No hardcoded relationship names
- ✅ **NavMap endpoints** - Query Dataverse dynamically
- ✅ **Caching logic** - Cache keys use relationship name from config
- ✅ **Document creation logic** - Uses discovered navigation property

**What Does Need Updates:**
- ⚠️ **EntityDocumentConfig.ts** - The ONLY file referencing relationship names
- ⚠️ **Dataverse forms** - Subgrid relationship references
- ⚠️ **Browser cache** - Will auto-refresh after 15 minutes (or clear manually)

---

### Testing Checklist After Relationship Rename

- [ ] Build succeeds with no errors
- [ ] Deployment completes successfully
- [ ] Subgrids show existing documents
- [ ] Can upload new documents
- [ ] Browser console shows correct relationship name
- [ ] NavMap API returns correct navigation property
- [ ] Document records created successfully
- [ ] Second upload uses cached metadata (source: 'cache')
- [ ] Existing documents still accessible
- [ ] No broken links or missing files

---

### Rollback Procedure

If issues occur after renaming:

**Quick Rollback (PCF Only):**
1. Revert `EntityDocumentConfig.ts` to previous relationship name
2. Rebuild and deploy PCF
3. Document uploads will work again

**Full Rollback (Dataverse + PCF):**
1. Delete new relationship in Dataverse
2. Recreate old relationship with original name
3. Update form subgrids to use old relationship
4. Revert `EntityDocumentConfig.ts`
5. Rebuild and deploy PCF

**Data Safety:**
- Document records are never deleted during relationship operations
- Container IDs remain unchanged
- SPE files remain accessible
- Only the link between entity and documents is temporarily broken during transition

---

## Supported Entity Types

SDAP supports any Dataverse entity (standard or custom) that has:
1. A container ID field (text field for storing Drive ID)
2. A 1:N relationship to `sprk_document`
3. A form with a subgrid for documents

**Currently Configured Entities:**
- `sprk_matter` - Legal matters
- `sprk_project` - Projects
- `sprk_invoice` - Invoices
- `account` - Standard Dynamics accounts (if configured)
- `contact` - Standard Dynamics contacts (if configured)

---

## Phase 7 Benefits

With Phase 7 dynamic metadata discovery, adding new entities is simpler:

**Before Phase 7:**
- Manually query Dataverse for navigation property name
- PowerShell validation scripts required
- Hardcode navigation property in config (e.g., `sprk_Matter`)
- Redeploy if relationship name changes

**After Phase 7:**
- Navigation property discovered automatically at runtime
- No manual metadata queries needed
- Only relationship schema name required in config
- Correct casing guaranteed by Dataverse metadata
- 15-minute caching for performance

---

## Documentation Updates

When adding a new entity, update:

1. **This Guide** - Add to "Currently Configured Entities" list
2. **EntityDocumentConfig.ts** - JSDoc comments with entity purpose
3. **Deployment Notes** - If special permissions or setup required
4. **User Training Materials** - How to use document upload for new entity

---

## Support

**Common Questions:**
- **Q:** Can I use this with custom entities from other solutions?
- **A:** Yes, as long as they have a container ID field and relationship to `sprk_document`.

- **Q:** What file types are supported?
- **A:** All file types supported by SharePoint Embedded (no restrictions by default).

- **Q:** Is there a file size limit?
- **A:** SharePoint Embedded supports files up to 250GB. Practical limits depend on network and browser.

- **Q:** Can I control permissions on uploaded documents?
- **A:** Yes, via SharePoint Embedded container permissions and Dataverse security roles.

**For Issues:**
- Check browser console for detailed error messages
- Review BFF API logs in Azure App Service
- Verify Dataverse Application User has System Administrator role
- Ensure all Azure AD permissions granted (Dynamics CRM, SharePoint)

---

## Summary

Adding SDAP to a new entity requires:
1. **Dataverse Setup** (10 min):
   - Add `hip_ContainerId` field
   - Create 1:N relationship to `sprk_document`
   - Add Documents subgrid to form

2. **PCF Configuration** (5 min):
   - Add entity to `EntityDocumentConfig.ts`
   - Specify relationship schema name (exact match!)

3. **Deployment** (5-10 min):
   - Build PCF control
   - Deploy to Dataverse
   - Test upload functionality

**Total Time:** 15-30 minutes per entity

With Phase 7, the complex metadata discovery is handled automatically, making multi-entity SDAP deployment scalable and maintainable.
