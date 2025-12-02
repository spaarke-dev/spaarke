# Sprint 7B - Revised Implementation Plan
## File Upload PCF + Backend Plugin

**Version:** 2.0.0
**Date:** 2025-10-07
**Timeline:** 11 days (2 weeks)
**Status:** Ready to Start

---

## Overview

Sprint 7B builds a file upload solution for Document creation in Dataverse with SharePoint Embedded (SPE) storage.

### Approach:

**Part 1: File Upload PCF Control**
- Upload files to SPE
- Store metadata in field
- Multi-file support

**Part 2: Backend Plugin**
- Apply field inheritance
- Process metadata
- Create additional records (multi-file)

---

## Implementation Phases

### Phase 1: File Upload PCF Control (5 days)

#### Day 1-2: Update Manifest and Core PCF

**Tasks:**

1. **Update `ControlManifest.Input.xml`**
   ```xml
   <?xml version="1.0" encoding="utf-8" ?>
   <manifest>
     <control namespace="Spaarke.Controls"
              constructor="SpeFileUpload"
              version="2.0.0"
              display-name-key="SPE_File_Upload"
              description-key="Upload files to SharePoint Embedded"
              control-type="standard">

       <!-- Bound field: SPE metadata JSON -->
       <property name="speMetadata"
                 display-name-key="SPE_Metadata"
                 description-key="SharePoint Embedded file metadata (JSON)"
                 of-type="Multiple"
                 usage="bound"
                 required="false" />

       <!-- Config: SDAP API URL -->
       <property name="sdapApiBaseUrl"
                 display-name-key="SDAP_API_Base_URL"
                 description-key="Base URL for SDAP BFF API"
                 of-type="SingleLine.Text"
                 usage="input"
                 required="true" />

       <!-- Config: Allow multiple files -->
       <property name="allowMultipleFiles"
                 display-name-key="Allow_Multiple_Files"
                 description-key="Allow multiple file uploads"
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

2. **Create `FileUploadPCF.ts`** (replaces `UniversalQuickCreatePCF.ts`)

   **Key Methods:**
   - `init()` - Initialize control, get Container ID
   - `getContainerIdFromParent()` - Retrieve from parent Matter
   - `handleFilesSelected()` - Upload files to SPE
   - `getOutputs()` - Return metadata JSON

   **Simplified Flow:**
   ```typescript
   export class FileUploadPCF implements ComponentFramework.StandardControl<IInputs, IOutputs> {
       private _metadataValue: string = "";
       private _containerid: string = "";
       private _uploadedFiles: SpeFileMetadata[] = [];

       public async init(context: ComponentFramework.Context<IInputs>): Promise<void> {
           // Get Container ID from parent Matter
           this._containerid = await this.getContainerIdFromParent(context);

           // Initialize MSAL
           await this.initializeMsal();

           // Render file upload UI
           this.renderUI();
       }

       private async handleFilesSelected(files: FileList): Promise<void> {
           // Upload each file
           for (let i = 0; i < files.length; i++) {
               const metadata = await this.uploadFile(files[i], this._containerid);
               this._uploadedFiles.push(metadata);
           }

           // Update bound field
           this._metadataValue = JSON.stringify(this._uploadedFiles);
           this._notifyOutputChanged();
       }

       public getOutputs(): IOutputs {
           return {
               speMetadata: this._metadataValue
           };
       }
   }
   ```

**Deliverable:** Working PCF control that binds to field

---

#### Day 3: Create File Upload UI Component

**Tasks:**

1. **Create `FileUploadField.tsx`** (replaces `QuickCreateForm.tsx`)

   **Features:**
   - File picker (single or multiple)
   - File list display
   - Upload progress
   - Error handling

   ```tsx
   export const FileUploadField: React.FC<Props> = ({ allowMultiple, onFilesUploaded }) => {
       const [selectedFiles, setSelectedFiles] = useState<File[]>([]);
       const [uploading, setUploading] = useState(false);

       const handleFileSelect = (event: React.ChangeEvent<HTMLInputElement>) => {
           const files = Array.from(event.target.files || []);
           setSelectedFiles(files);
       };

       const handleUpload = async () => {
           setUploading(true);
           try {
               await onFilesUploaded(selectedFiles);
           } finally {
               setUploading(false);
           }
       };

       return (
           <div>
               <Field label="Select Files">
                   <input type="file"
                          multiple={allowMultiple}
                          onChange={handleFileSelect}
                          disabled={uploading} />
               </Field>

               {selectedFiles.length > 0 && (
                   <div>
                       <h4>Selected Files:</h4>
                       <ul>
                           {selectedFiles.map((file, i) => (
                               <li key={i}>
                                   {file.name} ({formatFileSize(file.size)})
                               </li>
                           ))}
                       </ul>
                   </div>
               )}

               <Button onClick={handleUpload}
                       disabled={uploading || selectedFiles.length === 0}>
                   {uploading ? 'Uploading...' : 'Upload Files'}
               </Button>
           </div>
       );
   };
   ```

2. **Reuse `FilePickerField.tsx`** (existing component)

**Deliverable:** File upload UI component

---

#### Day 4: Testing & Refinement

**Tasks:**

1. **Unit Tests**
   - Test file selection
   - Test metadata JSON generation
   - Test Container ID retrieval

2. **Integration Tests**
   - Test file upload to SPE
   - Test MSAL authentication
   - Test multi-file upload

3. **Build & Package**
   ```bash
   cd src/controls/UniversalQuickCreate
   npm run build
   ```

**Deliverable:** Tested PCF control ready for deployment

---

#### Day 5: Deploy to Dev Environment

**Tasks:**

1. **Build Solution**
   ```bash
   cd UniversalQuickCreateSolution
   dotnet build -c Release
   ```

2. **Deploy to Dataverse**
   ```bash
   pac auth create --url https://dev-env.crm.dynamics.com
   pac solution import --path bin/Release/UniversalQuickCreateSolution.zip
   ```

3. **Configure Quick Create Form**
   - Add `sprk_fileuploadmetadata` field (hidden)
   - Bind PCF control to field
   - Configure parameters

4. **Test End-to-End**
   - Open Quick Create from Matter
   - Select files
   - Verify upload
   - Check metadata in field

**Deliverable:** Working file upload in dev environment

---

### Phase 2: Backend Field Inheritance Plugin (3 days)

#### Day 6: Plugin Project Setup

**Tasks:**

1. **Create Plugin Project**
   ```bash
   mkdir -p src/plugins/Spaarke.Plugins.FieldInheritance
   cd src/plugins/Spaarke.Plugins.FieldInheritance
   dotnet new classlib -f net462
   ```

2. **Add NuGet Packages**
   ```xml
   <PackageReference Include="Microsoft.CrmSdk.CoreAssemblies" Version="9.0.2.46" />
   <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
   ```

3. **Create Project Structure**
   ```
   Spaarke.Plugins.FieldInheritance/
   ├─ DocumentFieldInheritancePlugin.cs
   ├─ Models/
   │  └─ SpeFileMetadata.cs
   ├─ Services/
   │  └─ FieldInheritanceService.cs
   └─ Spaarke.Plugins.FieldInheritance.csproj
   ```

**Deliverable:** Plugin project setup

---

#### Day 7: Implement Plugin Logic

**Tasks:**

1. **Create `SpeFileMetadata.cs`**
   ```csharp
   public class SpeFileMetadata
   {
       public string DriveItemId { get; set; }
       public string WebUrl { get; set; }
       public string FileName { get; set; }
       public long FileSize { get; set; }
       public string MimeType { get; set; }
       public DateTime CreatedDateTime { get; set; }
   }
   ```

2. **Create `DocumentFieldInheritancePlugin.cs`**

   **See full code in:** [TASK-7B-BACKEND-PLUGIN-SPEC.md](TASK-7B-BACKEND-PLUGIN-SPEC.md)

   **Key Methods:**
   - `Execute()` - Main plugin entry point
   - `ProcessFileMetadata()` - Parse and populate SPE fields
   - `ApplyFieldInheritance()` - Copy fields from Matter
   - `CreateAdditionalDocuments()` - Handle multi-file

3. **Implement Field Inheritance Logic**
   ```csharp
   private void ApplyFieldInheritance(Entity document, EntityReference matterRef)
   {
       // Retrieve Matter
       var matter = service.Retrieve(
           matterRef.LogicalName,
           matterRef.Id,
           new ColumnSet("sprk_containerid", "ownerid")
       );

       // Copy Container ID
       if (matter.Contains("sprk_containerid"))
       {
           document["sprk_containerid"] = matter["sprk_containerid"];
       }

       // Copy Owner
       if (matter.Contains("ownerid") && !document.Contains("ownerid"))
       {
           document["ownerid"] = matter["ownerid"];
       }
   }
   ```

**Deliverable:** Plugin implementation complete

---

#### Day 8: Plugin Testing & Deployment

**Tasks:**

1. **Unit Tests**
   - Mock Dataverse context
   - Test field inheritance logic
   - Test JSON parsing
   - Test multi-file scenario

2. **Build Plugin**
   ```bash
   dotnet build -c Release
   ```

3. **Register Plugin**
   - Use Plugin Registration Tool
   - Register assembly
   - Register step:
     - Message: Create
     - Entity: sprk_document
     - Stage: Pre-Operation
     - Mode: Synchronous

4. **Test in Dev Environment**
   - Create Document via Quick Create
   - Verify field inheritance
   - Verify SPE fields populated
   - Check metadata field cleared

**Deliverable:** Plugin deployed and tested

---

### Phase 3: Integration & Form Setup (1 day)

#### Day 9: Quick Create Form Configuration

**Tasks:**

1. **Create Quick Create Form**
   - Entity: sprk_document
   - Name: "Quick Create - Document with File Upload"

2. **Add Fields to Form**
   - `sprk_documenttitle` (visible, required)
   - `sprk_description` (visible, optional)
   - `sprk_matter` (visible/auto from context)
   - `sprk_fileuploadmetadata` (hidden, PCF bound)

3. **Configure PCF Control**
   - Control: SPE File Upload
   - Bind to: sprk_fileuploadmetadata
   - Parameters:
     - sdapApiBaseUrl: https://your-api.azurewebsites.net/api
     - allowMultipleFiles: true

4. **Hide Metadata Field**
   - Remove field label
   - PCF control renders instead

5. **Set as Default Quick Create**
   - Publish form
   - Set as default for Quick Create

**Deliverable:** Configured Quick Create form

---

### Phase 4: Testing & Documentation (2 days)

#### Day 10: Comprehensive Testing

**Test Scenarios:**

1. **Single File Upload**
   - [ ] Open Quick Create from Matter subgrid
   - [ ] Select single file
   - [ ] Enter Document Title
   - [ ] Save
   - [ ] Verify: File in SharePoint
   - [ ] Verify: Document record created
   - [ ] Verify: Matter lookup populated
   - [ ] Verify: Container ID populated
   - [ ] Verify: SPE fields populated
   - [ ] Verify: Metadata field cleared

2. **Multi-File Upload**
   - [ ] Select 3 files
   - [ ] Save
   - [ ] Verify: 3 Document records created
   - [ ] Verify: All files in SharePoint
   - [ ] Verify: All have same Matter lookup
   - [ ] Verify: All have Container ID

3. **Error Scenarios**
   - [ ] No Container ID on Matter
   - [ ] File upload fails
   - [ ] Invalid file type
   - [ ] Network error

4. **Edge Cases**
   - [ ] Large file (50 MB)
   - [ ] Special characters in filename
   - [ ] Duplicate filename

**Deliverable:** Test results documented

---

#### Day 11: Documentation

**Tasks:**

1. **Admin Guide**
   - Update UNIVERSAL-QUICK-CREATE-ADMIN-GUIDE.md
   - Document revised approach
   - Configuration steps
   - Troubleshooting

2. **Developer Guide**
   - PCF control architecture
   - Plugin architecture
   - Deployment guide

3. **Completion Summary**
   - Create TASK-7B-REVISED-COMPLETION-SUMMARY.md
   - Document what was built
   - Lessons learned
   - Next steps

**Deliverable:** Complete documentation

---

## File Changes Summary

### Files to Delete:

```bash
# Delete unused components
rm src/controls/UniversalQuickCreate/UniversalQuickCreate/components/DynamicFormFields.tsx
rm src/controls/UniversalQuickCreate/UniversalQuickCreate/components/QuickCreateForm.tsx
rm src/controls/UniversalQuickCreate/UniversalQuickCreate/config/EntityFieldDefinitions.ts
rm src/controls/UniversalQuickCreate/UniversalQuickCreate/services/DataverseRecordService.ts
rm src/controls/UniversalQuickCreate/UniversalQuickCreate/types/FieldMetadata.ts
```

### Files to Create:

```bash
# PCF Control
src/controls/UniversalQuickCreate/UniversalQuickCreate/
├─ FileUploadPCF.ts (NEW - replaces UniversalQuickCreatePCF.ts)
├─ components/FileUploadField.tsx (NEW)
└─ css/FileUploadControl.css (RENAME from UniversalQuickCreate.css)

# Backend Plugin
src/plugins/Spaarke.Plugins.FieldInheritance/
├─ DocumentFieldInheritancePlugin.cs (NEW)
├─ Models/SpeFileMetadata.cs (NEW)
└─ Spaarke.Plugins.FieldInheritance.csproj (NEW)
```

### Files to Keep (Reuse):

```bash
# These work as-is
src/controls/UniversalQuickCreate/UniversalQuickCreate/
├─ components/FilePickerField.tsx ✅
├─ services/auth/MsalAuthProvider.ts ✅
├─ services/auth/msalConfig.ts ✅
├─ services/FileUploadService.ts ✅
├─ services/SdapApiClient.ts ✅
├─ services/SdapApiClientFactory.ts ✅
├─ types/auth.ts ✅
├─ types/index.ts ✅ (update to remove FieldMetadata)
└─ utils/logger.ts ✅
```

---

## Success Criteria

### Phase 1 Complete:
- ✅ PCF control builds without errors
- ✅ Files upload to SharePoint Embedded
- ✅ Metadata stored in bound field
- ✅ MSAL authentication works
- ✅ Multi-file support functional

### Phase 2 Complete:
- ✅ Plugin registered successfully
- ✅ Field inheritance applies on create
- ✅ SPE fields populated from metadata
- ✅ Multi-file creates additional records
- ✅ Metadata field cleared after processing

### Phase 3 Complete:
- ✅ Quick Create form configured
- ✅ PCF control appears on form
- ✅ Form opens from Matter subgrid
- ✅ Form opens from "+ New" menu

### Phase 4 Complete:
- ✅ All test scenarios pass
- ✅ Documentation complete
- ✅ Solution deployed to dev
- ✅ Ready for UAT

---

## Risk Mitigation

### Risk 1: Container ID Not Available

**Mitigation:**
- Show clear error message
- Document how to provision containers
- Provide troubleshooting steps

### Risk 2: File Upload Fails

**Mitigation:**
- Robust error handling
- Retry logic
- User-friendly error messages
- Logging for debugging

### Risk 3: Plugin Fails to Execute

**Mitigation:**
- Extensive logging/tracing
- Try-catch blocks
- Fail gracefully
- Leave metadata field populated if error

### Risk 4: Multi-File Performance

**Mitigation:**
- Limit to 10 files per upload
- Show progress indicators
- Consider async plugin step for many files

---

## Next Steps After Completion

1. **User Acceptance Testing**
   - Business users test in dev environment
   - Collect feedback
   - Make adjustments

2. **Deploy to Test Environment**
   - Follow deployment runbook
   - Smoke test

3. **Deploy to Production**
   - Schedule maintenance window
   - Deploy solution and plugin
   - Verify functionality
   - Monitor for issues

4. **Sprint 7A Integration**
   - Test with Download/Replace/Delete (Sprint 7A)
   - Create test documents for Sprint 7A testing

---

## Timeline Summary

| Phase | Days | Status |
|-------|------|--------|
| Phase 1: File Upload PCF | 5 | Field created ✅ |
| Phase 2: Backend Plugin | 3 | Not Started |
| Phase 3: Integration | 1 | Not Started |
| Phase 4: Testing & Docs | 2 | Not Started |
| **Total** | **11 days** | **10% Complete** |

**Start Date:** 2025-10-08
**Target Completion:** 2025-10-22

---

**Status:** Ready to Start
**Approved:** Yes
**Blockers:** None - Field created ✅
