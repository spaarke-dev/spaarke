# Quick Create - File Upload Only Component

**Version:** 1.0.0
**Date:** 2025-10-07
**Sprint:** 7B (Revised Scope)
**Status:** Design - RECOMMENDED APPROACH

---

## Executive Summary

**Revised Scope:** Focus Quick Create component **ONLY** on file upload to SharePoint Embedded, not field inheritance.

**Key Insight:** By removing field inheritance from the PCF control and handling it via backend process, the Quick Create approach becomes **much more feasible**.

---

## Scope Separation

### Sprint 7B (Current): File Upload Component

**Responsibility:** Upload file(s) to SharePoint Embedded

**What it Does:**
- ✅ Display file picker (single or multiple files)
- ✅ Upload selected file(s) to SPE via SDAP API
- ✅ Use MSAL for authentication
- ✅ Return SPE metadata (driveItemId, webUrl, etc.)

**What it Does NOT Do:**
- ❌ Field inheritance (Matter → Document)
- ❌ Set Document Title from file name
- ❌ Set Container ID from parent Matter
- ❌ Set Matter lookup

**Result:** Stores file in SharePoint, returns metadata for record creation

---

### Future Sprint: Backend Field Inheritance

**Responsibility:** Apply field mappings from parent to child records

**When:** After record is created (Dataverse plugin or Power Automate)

**What it Does:**
- ✅ Detect parent Matter from regardingObjectId
- ✅ Copy Container ID from Matter to Document
- ✅ Set Matter lookup on Document
- ✅ Set default values (Owner, etc.)

**Trigger:** OnCreate event for Document entity

**Result:** Document record has all inherited fields populated

---

## Revised Architecture

### Flow Diagram

```
┌─────────────────────────────────────────────────────────────┐
│  1. User opens Quick Create form for Document               │
│     (from Matter subgrid or "+ New" menu)                   │
└─────────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────────┐
│  2. Quick Create Form Displays                              │
│     ┌─────────────────────────────────────────────────┐    │
│     │  Document Title: [_______________]              │    │
│     │  Description:    [_______________]              │    │
│     │  Matter:         [Selected automatically]       │    │
│     │                                                  │    │
│     │  📎 File Upload (PCF Control)                   │    │
│     │  ┌──────────────────────────────────────────┐  │    │
│     │  │ [Choose Files] [file1.pdf] [file2.docx] │  │    │
│     │  │                                           │  │    │
│     │  │ Status: ✅ 2 files ready to upload       │  │    │
│     │  └──────────────────────────────────────────┘  │    │
│     │                                                  │    │
│     │  [Save and Close]  [Cancel]                     │    │
│     └─────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────────┐
│  3. User clicks "Save and Close"                            │
│                                                              │
│     PHASE A: File Upload (PCF Control)                      │
│     ├─ PCF control uploads files to SPE via SDAP API        │
│     ├─ Returns SPE metadata (driveItemId, webUrl, etc.)     │
│     └─ Stores metadata in hidden field on form              │
│                                                              │
│     PHASE B: Record Creation (Quick Create)                 │
│     ├─ Form saves with user-entered data                    │
│     ├─ Document Title, Description, Matter lookup           │
│     ├─ SPE metadata from PCF control                        │
│     └─ Record created in Dataverse                          │
└─────────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────────┐
│  4. Backend Process (Dataverse Plugin)                      │
│                                                              │
│     Trigger: OnCreate of Document                           │
│     ├─ Get regardingObjectId (parent Matter)                │
│     ├─ Retrieve Matter record (Container ID, etc.)          │
│     ├─ Apply field inheritance logic                        │
│     │  ├─ Set sprk_containerid from Matter                 │
│     │  ├─ Set sprk_matter lookup (if not already set)      │
│     │  └─ Set other default values                         │
│     └─ Update Document record                               │
└─────────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────────┐
│  5. Complete Document Record                                │
│                                                              │
│     Document Fields:                                        │
│     ├─ sprk_documenttitle: "Contract.pdf" (user entered)   │
│     ├─ sprk_description: "Service agreement" (user)        │
│     ├─ sprk_matter: Matter lookup (Quick Create or plugin) │
│     ├─ sprk_containerid: "b!ABC..." (plugin inherited)     │
│     ├─ sprk_sharepointurl: "https://..." (PCF uploaded)    │
│     ├─ sprk_driveitemid: "01XYZ..." (PCF uploaded)         │
│     └─ ownerid: User (plugin inherited)                    │
└─────────────────────────────────────────────────────────────┘
```

---

## PCF Control: File Upload Only

### Simplified Manifest

```xml
<?xml version="1.0" encoding="utf-8" ?>
<manifest>
  <control namespace="Spaarke.Controls"
           constructor="FileUploadControl"
           version="1.0.0"
           display-name-key="File_Upload_Control"
           description-key="Upload files to SharePoint Embedded"
           control-type="standard">

    <!-- Bound field: Stores SPE metadata JSON -->
    <property name="speMetadata"
              display-name-key="SPE_Metadata"
              description-key="SharePoint Embedded file metadata (JSON)"
              of-type="Multiple"
              usage="bound"
              required="false" />

    <!-- Configuration: Container ID field name -->
    <property name="containerIdFieldName"
              display-name-key="Container_ID_Field"
              description-key="Name of Container ID field on parent entity"
              of-type="SingleLine.Text"
              usage="input"
              required="false"
              default-value="sprk_containerid" />

    <!-- Configuration: SDAP API Base URL -->
    <property name="sdapApiBaseUrl"
              display-name-key="SDAP_API_Base_URL"
              description-key="Base URL for SDAP BFF API"
              of-type="SingleLine.Text"
              usage="input"
              required="true"
              default-value="https://localhost:7299/api" />

    <!-- Configuration: Allow multiple files -->
    <property name="allowMultipleFiles"
              display-name-key="Allow_Multiple_Files"
              description-key="Allow uploading multiple files at once"
              of-type="TwoOptions"
              usage="input"
              required="false"
              default-value="true" />

    <resources>
      <code path="index.ts" order="1"/>
      <css path="css/FileUploadControl.css" order="1" />
    </resources>

    <feature-usage>
      <uses-feature name="WebAPI" required="true" />
      <uses-feature name="Utility" required="true" />
    </feature-usage>
  </control>
</manifest>
```

### Key Simplifications

1. **No field inheritance logic** - Removed from PCF
2. **No parent record retrieval** - Don't need Matter data
3. **No field mappings configuration** - Moved to backend
4. **Single responsibility** - Just upload files

---

## How It Works

### Step 1: Get Container ID

**Challenge:** PCF control needs Container ID to upload files

**Solutions:**

#### Option A: Read from Parent Context (Recommended)
```typescript
// Quick Create provides regardingObjectId when opened from subgrid
const formContext = (context as any).mode?.contextInfo;
const parentEntityName = formContext?.regardingEntityName; // "sprk_matter"
const parentRecordId = formContext?.regardingObjectId;     // Matter GUID

// Retrieve parent record to get Container ID
const parentRecord = await context.webAPI.retrieveRecord(
    parentEntityName,
    parentRecordId,
    "?$select=sprk_containerid"
);

const containerid = parentRecord.sprk_containerid;
```

**Pros:**
- ✅ Works when opened from Matter subgrid
- ✅ No admin configuration needed
- ✅ Automatic

**Cons:**
- ❌ Doesn't work when opened from "+ New" menu (no parent context)
- ❌ Extra API call

---

#### Option B: Read from Form Field (Alternative)
```typescript
// Admin adds sprk_containerid field to Quick Create form (hidden)
// User or business rule populates it before opening form

const formContext = (context as any).utils?.getFormContext?.();
const containerid = formContext?.getAttribute("sprk_containerid")?.getValue();
```

**Pros:**
- ✅ Works even without parent context
- ✅ Admin control over source

**Cons:**
- ❌ Requires admin to add hidden field
- ❌ Requires business rule or JavaScript to populate it
- ❌ More configuration

---

#### Option C: Ask User (Fallback)
```typescript
// If no Container ID found, show error with helpful message
if (!containerid) {
    alert("Please select a Matter first, or ensure the Matter has a Container ID.");
    return;
}
```

**Pros:**
- ✅ Clear error message
- ✅ Prevents bad uploads

**Cons:**
- ❌ Poor UX if happens often

---

### Step 2: Upload Files

**PCF Control Behavior:**

```typescript
class FileUploadControl implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private _uploadedFiles: SpeFileMetadata[] = [];
    private _containerid: string = "";

    public async init(context: ComponentFramework.Context<IInputs>): Promise<void> {
        // Get Container ID from parent context
        this._containerid = await this.getContainerIdFromParent(context);

        // Initialize MSAL
        await this.initializeMsal();

        // Render file upload UI
        this.renderFileUploadUI();
    }

    private async handleFilesSelected(files: FileList): Promise<void> {
        for (let i = 0; i < files.length; i++) {
            const file = files[i];

            // Upload to SPE via SDAP API
            const speMetadata = await this.uploadFileToSpe(file, this._containerid);

            // Store metadata
            this._uploadedFiles.push(speMetadata);
        }

        // Update bound field with metadata JSON
        this._notifyOutputChanged();
    }

    public getOutputs(): IOutputs {
        return {
            speMetadata: JSON.stringify(this._uploadedFiles)
        };
    }

    private async uploadFileToSpe(file: File, containerid: string): Promise<SpeFileMetadata> {
        // Get MSAL token
        const token = await this.msalProvider.getToken();

        // Call SDAP API
        const response = await fetch(`${this.sdapApiBaseUrl}/files/upload`, {
            method: 'POST',
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                containerid: containerid,
                fileName: file.name,
                fileContent: await this.fileToBase64(file)
            })
        });

        const result = await response.json();

        return {
            driveItemId: result.id,
            webUrl: result.webUrl,
            fileName: file.name,
            fileSize: file.size,
            createdDateTime: result.createdDateTime
        };
    }
}
```

---

### Step 3: Form Saves with Metadata

**Quick Create Form Saves:**

```json
{
  "sprk_documenttitle": "Contract.pdf",
  "sprk_description": "Service agreement",
  "sprk_matter@odata.bind": "/sprk_matters(matter-guid)",

  // SPE metadata from PCF control (JSON string in hidden field)
  "sprk_spemetadata": "[{\"driveItemId\":\"01ABC\",\"webUrl\":\"https://...\",\"fileName\":\"Contract.pdf\"}]"
}
```

**Note:** Form does NOT have Container ID yet - that's populated by plugin

---

### Step 4: Plugin Applies Field Inheritance

**Dataverse Plugin (OnCreate, Pre-Operation):**

```csharp
public class DocumentCreatePlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        var service = (IOrganizationService)serviceProvider.GetService(typeof(IOrganizationService));

        Entity document = (Entity)context.InputParameters["Target"];

        // 1. Get parent Matter from regardingObjectId
        EntityReference matterRef = null;

        if (document.Contains("sprk_matter"))
        {
            matterRef = (EntityReference)document["sprk_matter"];
        }
        else if (context.InputParameters.Contains("regardingObjectId"))
        {
            matterRef = (EntityReference)context.InputParameters["regardingObjectId"];
            document["sprk_matter"] = matterRef; // Set lookup
        }

        if (matterRef == null) return; // No parent, skip

        // 2. Retrieve Matter record
        Entity matter = service.Retrieve(
            matterRef.LogicalName,
            matterRef.Id,
            new ColumnSet("sprk_containerid", "ownerid")
        );

        // 3. Apply field inheritance
        if (matter.Contains("sprk_containerid"))
        {
            document["sprk_containerid"] = matter["sprk_containerid"];
        }

        if (matter.Contains("ownerid") && !document.Contains("ownerid"))
        {
            document["ownerid"] = matter["ownerid"];
        }

        // 4. Parse SPE metadata and populate fields
        if (document.Contains("sprk_spemetadata"))
        {
            var metadataJson = document["sprk_spemetadata"].ToString();
            var metadata = JsonConvert.DeserializeObject<List<SpeFileMetadata>>(metadataJson);

            if (metadata.Count > 0)
            {
                var firstFile = metadata[0]; // Use first file for single-file scenarios
                document["sprk_sharepointurl"] = firstFile.webUrl;
                document["sprk_driveitemid"] = firstFile.driveItemId;
                document["sprk_filename"] = firstFile.fileName;
                document["sprk_filesize"] = firstFile.fileSize;
            }
        }
    }
}
```

---

## Benefits of This Approach

### 1. PCF Control Complexity: Dramatically Reduced ✅

**Before (Full Quick Create):**
- Complex form context access
- Field inheritance logic
- Parent record retrieval
- Multiple field mappings
- Coordination with form save
- **Complexity: 8/10**

**After (File Upload Only):**
- Just upload files
- Return metadata
- No field inheritance
- No form coordination
- **Complexity: 3/10**

---

### 2. Quick Create Compatibility: Much Better ✅

**Before:**
- ❌ Needed access to multiple form fields
- ❌ Had to coordinate timing with form save
- ❌ Required reliable form context
- ❌ Many edge cases

**After:**
- ✅ Binds to single field (SPE metadata)
- ✅ No form coordination needed
- ✅ Uploads complete before form saves
- ✅ Few edge cases

---

### 3. Field Inheritance: More Reliable ✅

**Before (Frontend):**
- ⚠️ Limited form context access
- ⚠️ Unreliable in Quick Create
- ⚠️ Race conditions
- ⚠️ Hard to debug

**After (Backend Plugin):**
- ✅ Full Dataverse API access
- ✅ Reliable execution
- ✅ Synchronous (no race conditions)
- ✅ Easy to debug and trace

---

### 4. Separation of Concerns ✅

**Sprint 7B (Current):**
- Focus: File upload to SPE
- Technology: PCF control
- Responsibility: Upload files, return metadata

**Future Sprint:**
- Focus: Field inheritance
- Technology: Dataverse plugin or Power Automate
- Responsibility: Apply field mappings

**Result:** Clean separation, easier to maintain

---

### 5. Multi-File Upload: Easier ✅

**With simplified PCF:**

```typescript
// Allow multiple files
<input type="file" multiple onChange={handleFilesSelected} />

// Upload all files
const uploadedFiles = await Promise.all(
    Array.from(files).map(file => uploadFileToSpe(file, containerid))
);

// Return all metadata
return {
    speMetadata: JSON.stringify(uploadedFiles)
};
```

**Create multiple documents:**
```csharp
// Plugin: Create separate Document record for each file
if (document.Contains("sprk_spemetadata"))
{
    var metadata = JsonConvert.DeserializeObject<List<SpeFileMetadata>>(metadataJson);

    if (metadata.Count > 1)
    {
        // First file: Update current record
        var firstFile = metadata[0];
        document["sprk_sharepointurl"] = firstFile.webUrl;
        // ...

        // Additional files: Create new Document records
        for (int i = 1; i < metadata.Count; i++)
        {
            var additionalFile = metadata[i];
            var newDocument = new Entity("sprk_document")
            {
                ["sprk_documenttitle"] = additionalFile.fileName,
                ["sprk_matter"] = document["sprk_matter"],
                ["sprk_containerid"] = document["sprk_containerid"],
                ["sprk_sharepointurl"] = additionalFile.webUrl,
                ["sprk_driveitemid"] = additionalFile.driveItemId,
                ["sprk_filename"] = additionalFile.fileName
            };

            service.Create(newDocument);
        }
    }
}
```

---

## Quick Create Form Configuration

### Form Fields:

1. **sprk_documenttitle** (Text, required, visible)
   - User enters document title

2. **sprk_description** (Multiline Text, optional, visible)
   - User enters description

3. **sprk_matter** (Lookup, visible/hidden depending on context)
   - Auto-populated when opened from Matter subgrid
   - Or user selects manually

4. **sprk_spemetadata** (Multiline Text, hidden)
   - **PCF control binds here**
   - Stores JSON array of uploaded file metadata
   - Not visible to user

### PCF Control Configuration:

**Add control to `sprk_spemetadata` field:**

```
Control: FileUploadControl
Bind to field: sprk_spemetadata
Configuration:
  - sdapApiBaseUrl: https://your-api.azurewebsites.net/api
  - allowMultipleFiles: true
  - containerIdFieldName: sprk_containerid
```

---

## Implementation Comparison

### Option A: Full Quick Create (Complex)

| Aspect | Effort | Reliability |
|--------|--------|-------------|
| PCF Development | 2-3 weeks | Moderate |
| Field Inheritance | In PCF | Unreliable |
| Form Context | Complex | Fragile |
| Testing | Extensive | Many edge cases |
| Maintenance | High | Ongoing issues |

**Total Timeline:** 3-4 weeks

---

### Option B: File Upload Only + Backend Plugin (Recommended)

| Aspect | Effort | Reliability |
|--------|--------|-------------|
| PCF Development | 3-5 days | High |
| Field Inheritance | Backend plugin | Very reliable |
| Form Context | Minimal | Simple |
| Testing | Moderate | Few edge cases |
| Maintenance | Low | Stable |

**Total Timeline:** 1-2 weeks

**Breakdown:**
- PCF file upload control: 3-5 days
- Backend plugin: 2-3 days
- Testing: 2-3 days

---

## Recommended Implementation Plan

### Phase 1: File Upload PCF Control (3-5 days)

**Tasks:**
1. Create new `FileUploadControl` manifest
2. Simplify PCF code:
   - Remove field inheritance logic
   - Remove form context complexity
   - Focus on file upload only
3. Add multi-file support
4. Return SPE metadata JSON
5. Test file upload functionality

**Deliverable:** Working file upload control that binds to single field

---

### Phase 2: Backend Field Inheritance Plugin (2-3 days)

**Tasks:**
1. Create Dataverse plugin project
2. Implement field inheritance logic:
   - Get parent Matter from context
   - Retrieve Matter fields
   - Apply mappings to Document
3. Parse SPE metadata JSON
4. Populate SPE fields on Document
5. Handle multi-file scenario (create additional records)

**Deliverable:** Plugin that applies field inheritance on Document create

---

### Phase 3: Quick Create Form Setup (1 day)

**Tasks:**
1. Create Quick Create form for Document
2. Add visible fields (title, description, matter)
3. Add hidden field (sprk_spemetadata)
4. Bind PCF control to hidden field
5. Configure PCF parameters
6. Test end-to-end flow

**Deliverable:** Working Quick Create form with file upload

---

### Phase 4: Testing & Documentation (2-3 days)

**Tasks:**
1. Test single file upload
2. Test multi-file upload
3. Test field inheritance
4. Test from Matter subgrid
5. Test from "+ New" menu
6. Document admin configuration
7. Document troubleshooting

**Deliverable:** Complete solution ready for production

---

## Answer to Your Questions

### Question 1: Custom Record Create Form

**Answer:** Yes, Quick Create IS a custom record create form! It's just a specialized type:

- ✅ Form builder configuration
- ✅ "+ New" button integration
- ✅ Subgrid integration
- ✅ Works for Documents, Tasks, Contacts, etc.

**You get the benefits of custom forms without losing Quick Create features!**

---

### Question 2: File Upload Only, No Field Inheritance

**Answer:** **YES! This makes the solution MUCH better aligned with Quick Create!**

**Why:**

1. **PCF Complexity Reduced by 70%**
   - No form context access needed
   - No field mapping logic
   - Just upload files and return metadata

2. **Reliability Increased**
   - Backend plugin more reliable than frontend form context
   - No race conditions
   - Easy to debug

3. **Multi-File Support Easier**
   - Just upload multiple files
   - Return array of metadata
   - Plugin creates multiple Document records

4. **Maintenance Reduced**
   - Simple PCF control
   - Plugin is standard Dataverse pattern
   - Less brittle

**This is the RIGHT approach!** ✅

---

## Revised Recommendation

### For Sprint 7B: File Upload Only PCF + Backend Plugin

**Scope:**
1. **PCF Control:** File upload to SPE (single or multiple files)
2. **Backend Plugin:** Field inheritance from Matter to Document
3. **Quick Create Form:** Simple form with PCF control

**Timeline:** 1-2 weeks (vs 3-4 weeks for full Quick Create approach)

**Benefits:**
- ✅ Quick Create features work (+ New button, subgrid integration)
- ✅ File upload reliable and simple
- ✅ Field inheritance reliable (backend)
- ✅ Multi-file support included
- ✅ Easy to maintain
- ✅ Clean separation of concerns

**This is the BEST approach!** 🎯

---

## Next Steps

1. **Confirm Approach:** File Upload Only PCF + Backend Plugin?
2. **Start Implementation:**
   - Day 1-3: Simplify PCF control (remove field inheritance)
   - Day 4-5: Add multi-file support
   - Day 6-7: Create backend plugin
   - Day 8-9: Testing
   - Day 10: Documentation

3. **Deploy to Test Environment**

**Ready to proceed?** 🚀

---

**Date:** 2025-10-07
**Sprint:** 7B (Revised Scope)
**Status:** RECOMMENDED - Awaiting Approval
