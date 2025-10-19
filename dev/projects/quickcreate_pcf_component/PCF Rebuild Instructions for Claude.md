**PCF Rebuild Instructions for Claude Code: Multi-Document Upload with Custom Page Architecture**

**Executive Summary**

**Project**: Rebuild UniversalQuickCreatePCF to use custom page/dialog architecture instead of Quick Create form for multi-document upload functionality.

**Root Problem**: The current implementation attempts to create multiple Dataverse records using context.webAPI.createRecord() within a Quick Create form context. Quick Create forms are designed for single-record creation, and their PCF context becomes corrupted after the first createRecord() call, causing subsequent calls to fail with 400 errors.

**Solution**: Move the PCF from Quick Create form to a custom page/dialog launched from the Documents subgrid command bar. This provides unrestricted access to Xrm.WebApi for creating multiple records without Quick Create lifecycle constraints.

**Developer Mindset**: Code this as Alex Butenko would - clean architecture, proper separation of concerns, framework-appropriate patterns, production-ready error handling, and no fighting against Power Platform design paradigms.

**Table of Contents**

- [Root Cause Analysis](https://claude.ai/chat/f56798a7-9176-401e-b2b4-5731b8f8c2b2#root-cause-analysis)
- [Architectural Changes](https://claude.ai/chat/f56798a7-9176-401e-b2b4-5731b8f8c2b2#architectural-changes)
- [Components to Keep](https://claude.ai/chat/f56798a7-9176-401e-b2b4-5731b8f8c2b2#components-to-keep)
- [Components to Modify](https://claude.ai/chat/f56798a7-9176-401e-b2b4-5731b8f8c2b2#components-to-modify)
- [Components to Create](https://claude.ai/chat/f56798a7-9176-401e-b2b4-5731b8f8c2b2#components-to-create)
- [Implementation Steps](https://claude.ai/chat/f56798a7-9176-401e-b2b4-5731b8f8c2b2#implementation-steps)
- [Code Patterns & Standards](https://claude.ai/chat/f56798a7-9176-401e-b2b4-5731b8f8c2b2#code-patterns--standards)
- [Testing & Validation](https://claude.ai/chat/f56798a7-9176-401e-b2b4-5731b8f8c2b2#testing--validation)

**Root Cause Analysis**

**The Quick Create Form Limitation**

**What Was Happening:**

// Inside Quick Create form PCF context

async handleSaveAndCreateDocuments() {

// Upload files to SharePoint Embedded ✅ (works fine)

const uploads = await this.multiFileService.uploadFiles(files);

// Create first Document record ✅ (succeeds)

await this.context.webAPI.createRecord('sprk_document', recordData1);

// Create second Document record ❌ (fails with 400)

await this.context.webAPI.createRecord('sprk_document', recordData2);

// Error: "An undeclared property 'sprk_matter' which only has

// property annotations in the payload but no property value"

}

**Why It Failed:**

- **Quick Create Context is Stateful**: The ComponentFramework.Context&lt;IInputs&gt; in a Quick Create form is designed for **one record creation** per form instance
- **Context Corruption**: After the first createRecord(), the context's internal state machine expects the form to close, not to create more records
- **Lifecycle Mismatch**: Second createRecord() call hits a stale/corrupted context, causing OData parsing errors
- **Design Intent**: Quick Create = "Quick" = One record. Microsoft never intended multiple record creation from this context.

**The Symptom (not root cause):** The 400 error about sprk_matter property annotations is a **symptom** of the corrupted context trying to serialize the lookup field incorrectly on the second call.

**Why Sequential Creation Didn't Help**

Switching from parallel to sequential was a **red herring**:

- The issue isn't concurrency
- The issue is **multiple calls** (regardless of timing) in a **single-record form context**
- Even with perfect OData syntax, the second call would fail

**Architectural Changes**

**Before (Quick Create Approach) ❌**

User clicks "+ New Document" on Documents Subgrid

↓

Opens Quick Create Form

↓

PCF Control renders inside Quick Create form

↓

User selects multiple files

↓

User clicks "Save" (Quick Create save button)

↓

PCF intercepts save, prevents default

↓

Uploads files to SharePoint Embedded ✅

↓

Attempts multiple context.webAPI.createRecord() calls ❌

↓

First record succeeds, second fails

**Problems:**

- Fighting Quick Create's single-record design
- Limited by form context lifecycle
- Complex form data extraction via getFormData()
- No control over save button behavior
- Error handling conflicts with form validation

**After (Custom Page Approach) ✅**

User clicks "+ Add Documents" on Documents Subgrid (Custom Command)

↓

Custom JavaScript opens Custom Page in Dialog

↓

PCF Control renders inside Custom Page (NOT Quick Create)

↓

PCF receives parent Matter context (ID, Name) as input parameters

↓

User selects multiple files & optional metadata

↓

User clicks "Upload & Save" (PCF's own button)

↓

PCF uploads files to SharePoint Embedded ✅

↓

PCF creates multiple Document records using Xrm.WebApi.createRecord() ✅

↓

Dialog closes, subgrid refreshes automatically

**Benefits:**

- Works with framework instead of against it
- Unlimited record creation (10, 50, 100+ files)
- Full control over UI/UX
- Clean separation of concerns
- Proper error handling without form conflicts
- Can use parallel or sequential creation (your choice)

**Components to Keep**

**✅ Keep These - They Work Correctly**

**1\. SdapApiClient.ts (SharePoint Embedded Upload)**

**Location**: services/SdapApiClient.ts

**Why Keep**:

- Handles OAuth2 On-Behalf-Of flow correctly
- BFF API integration works
- MSAL token acquisition is solid

**No Changes Needed**: This component uploads files to SharePoint Embedded and returns metadata (driveItemId, etc.). It's agnostic to how records are created afterward.

// Keep this method as-is

public async uploadFile(request: UploadFileRequest): Promise&lt;ApiResponse<SpeMetadata&gt;> {

const token = await this.getAccessToken();

// ... PUT request to BFF API

return { data: speMetadata, error: null };

}

**2\. FileUploadService.ts (File Upload Orchestration)**

**Location**: services/FileUploadService.ts

**Why Keep**:

- File validation logic is good
- Error handling for SPE uploads is solid

**Minor Change**: Remove any Quick Create form-specific logic, but core upload functionality stays.

**3\. MultiFileUploadService.ts (Parallel Upload Logic)**

**Location**: services/MultiFileUploadService.ts

**Keep**: The handleSyncParallelUpload() method for **Phase 1 (file uploads)**

**Remove**: The **Phase 2 (record creation)** section (lines 161-231)

**Rationale**: File upload parallelization works. Record creation will move to a new component that uses Xrm.WebApi.

// KEEP this part (Phase 1):

const uploadResults = await Promise.allSettled(

files.map(file =>

this.fileUploadService.uploadFile({ file, driveId })

)

);

// REMOVE this part (Phase 2) - will be replaced:

for (let i = 0; i < files.length; i++) {

await this.recordService.createDocument(...); // ❌ Delete this

}

**4\. Logging Infrastructure**

**Location**: services/LoggingService.ts, utils/Logger.ts

**Why Keep**:

- Production logging is essential
- Telemetry for debugging SPE issues

**No Changes**: Logging is orthogonal to record creation strategy.

**Components to Modify**

**🔧 Modify These Components**

**1\. UniversalQuickCreatePCF.ts (Main PCF Entry Point)**

**Current Role**: Renders inside Quick Create form, intercepts form save

**New Role**: Renders inside Custom Page dialog, manages its own save flow

**Changes Required**:

// REMOVE: Quick Create form dependencies

// - getFormData() method

// - preventDefault() save interception

// - \_notifyOutputChanged() for form save

// - Any form context assumptions

// ADD: Custom Page dialog capabilities

// - Accept parent context as input parameters (matterId, matterName)

// - Render custom UI with own Save/Cancel buttons

// - Direct control over save workflow

// - Dialog close notification

**Key Method Changes**:

// OLD (Quick Create):

private async handleSaveAndCreateDocuments(): Promise&lt;void&gt; {

const formData = this.getFormData(); // ❌ Form-specific

// ... upload files

await this.multiFileService.uploadFiles(files, formData); // ❌ Includes record creation

}

// NEW (Custom Page):

private async handleUploadAndSave(): Promise&lt;void&gt; {

// No form data extraction needed

const metadata = this.getMetadataFromPCFInputs(); // ✅ From user inputs in PCF UI

// Phase 1: Upload files (keep existing logic)

const uploadResults = await this.multiFileService.uploadFiles(files);

// Phase 2: Create records (new approach)

await this.documentRecordService.createMultipleDocuments({

uploadResults,

parentRecordId: this.\_matterId, // From input parameter

metadata

});

// Close dialog

this.closeDialog();

}

**2\. DataverseRecordService.ts (Record Creation)**

**Current Issues**:

- Uses context.webAPI.createRecord() (Quick Create bound)
- Complex lookup field handling with null workarounds
- 30-second timeout per record (indicates awareness of hangs)

**Changes Required**:

// OLD:

async createDocument(request: CreateDocumentRequest): Promise&lt;EntityReference&gt; {

// Complex lookup field cleanup

delete recordData\[lookupFieldName\];

delete recordData\[\`\_\${lookupFieldName}\_value\`\];

recordData\[lookupFieldName\] = null; // Workaround for context issues

await this.context.webAPI.createRecord('sprk_document', recordData); // ❌

}

// NEW:

async createDocument(request: CreateDocumentRequest): Promise&lt;string&gt; {

const recordData = {

"sprk_documentname": request.fileName,

"sprk_graphitemid": request.speMetadata.driveItemId,

"sprk_graphdriveid": request.speMetadata.driveId,

"sprk_filesize": request.speMetadata.fileSize,

// Clean lookup syntax - no null workarounds needed

"<sprk_matter@odata.bind>": \`/sprk_matters(\${request.parentRecordId})\`

};

// Use global Xrm.WebApi (not form context)

const result = await Xrm.WebApi.createRecord("sprk_document", recordData); // ✅

return result.id;

}

**Lookup Field Best Practice**:

// ✅ CORRECT (for Xrm.WebApi):

{

"<sprk_matter@odata.bind>": \`/sprk_matters(\${parentId})\`

}

// Do NOT include base field, do NOT set null

// ❌ WRONG (old workaround):

{

"sprk_matter": null, // Unnecessary and problematic

"<sprk_matter@odata.bind>": "/sprk_matters(guid)"

}

**3\. ControlManifest.Input.xml (PCF Manifest)**

**Changes Required**:

&lt;!-- REMOVE: Quick Create form bindings --&gt;

&lt;!-- <property name="value" ... usage="bound" /&gt; -->

&lt;!-- ADD: Parent context input parameters --&gt;

<property name="parentRecordId"

display-name-key="Parent Record ID"

description-key="GUID of parent Matter record"

of-type="SingleLine.Text"

usage="input"

required="true" />

<property name="parentEntityName"

display-name-key="Parent Entity Name"

description-key="Logical name of parent entity (sprk_matter)"

of-type="SingleLine.Text"

usage="input"

required="true" />

<property name="parentRecordName"

display-name-key="Parent Record Name"

description-key="Display name of parent Matter"

of-type="SingleLine.Text"

usage="input"

required="false" />

&lt;!-- Update control type --&gt;

&lt;type-group name="files"&gt;

&lt;type&gt;SingleLine.Text&lt;/type&gt; &lt;!-- Placeholder, not actually bound --&gt;

&lt;/type-group&gt;

&lt;!-- Specify this is for custom pages, not forms --&gt;

&lt;uses-feature name="WebAPI" required="true" /&gt;

&lt;uses-feature name="Utility" required="true" /&gt;

**Components to Create**

**🆕 New Components Needed**

**1\. Custom Page Definition**

**File**: Solution → Custom Pages → sprk_multifileupload_page.json

**Purpose**: Host the PCF control in a dialog (not Quick Create form)

{

"name": "sprk_multifileupload_page",

"displayName": "Add Documents",

"description": "Upload multiple documents to a Matter",

"type": "custompage",

"template": "single-control",

"controls": \[

{

"id": "fileUploadControl",

"type": "pcfcontrol",

"namespace": "SpaarkeComponents",

"constructor": "UniversalQuickCreatePCF",

"parameters": {

"parentRecordId": "{{parentRecordId}}",

"parentEntityName": "{{parentEntityName}}",

"parentRecordName": "{{parentRecordName}}"

}

}

\]

}

**2\. Command Bar Button Script**

**File**: Solution → Web Resources → sprk_subgrid_commands.js

**Purpose**: JavaScript function to open custom page dialog from Documents subgrid

// Web Resource: sprk_subgrid_commands.js

/\*\*

\* Opens multi-document upload dialog from Documents subgrid

\* @param {object} selectedControl - The subgrid control

\*/

function addMultipleDocuments(selectedControl) {

try {

// Get parent form context

const formContext = selectedControl.getFormContext

? selectedControl.getFormContext() // Modern API

: selectedControl.\_context; // Fallback for older versions

if (!formContext) {

Xrm.Navigation.openAlertDialog({

text: "Unable to access parent form context"

});

return;

}

// Get parent Matter record details

const matterId = formContext.data.entity.getId().replace(/\[{}\]/g, '');

const matterName = formContext.getAttribute("sprk_name")?.getValue() || "Unknown Matter";

const entityName = formContext.data.entity.getEntityName(); // "sprk_matter"

// Define custom page navigation

const pageInput = {

pageType: "custom",

name: "sprk_multifileupload_page",

entityName: "sprk_document",

recordId: null, // Not editing existing record

data: {

parentRecordId: matterId,

parentEntityName: entityName,

parentRecordName: matterName

}

};

// Dialog options

const navigationOptions = {

target: 2, // 1 = inline, 2 = dialog

position: 1, // 1 = center, 2 = side

width: { value: 70, unit: "%" },

height: { value: 85, unit: "%" },

title: "Add Documents to " + matterName

};

// Open dialog

Xrm.Navigation.navigateTo(pageInput, navigationOptions).then(

function success(result) {

// Dialog closed successfully

console.log("Document upload dialog closed", result);

// Refresh the subgrid to show new documents

selectedControl.refresh();

// Optional: Show confirmation

if (result && result.recordCount > 0) {

Xrm.Navigation.openAlertDialog({

text: \`Successfully uploaded \${result.recordCount} document(s)\`,

confirmButtonLabel: "OK"

});

}

},

function error(err) {

// Dialog was cancelled or error occurred

console.error("Document upload dialog error", err);

// Only show error if not user cancellation

if (err && err.errorCode !== 2) { // 2 = user cancelled

Xrm.Navigation.openAlertDialog({

text: "An error occurred opening the document upload dialog: " + err.message

});

}

}

);

} catch (error) {

console.error("Error in addMultipleDocuments:", error);

Xrm.Navigation.openAlertDialog({

text: "Unexpected error: " + error.message

});

}

}

/\*\*

\* Enable rule: Only show button if viewing an existing Matter record

\* @param {object} selectedControl - The subgrid control

\* @returns {boolean} - True if button should be enabled

\*/

function enableAddDocuments(selectedControl) {

try {

const formContext = selectedControl.getFormContext

? selectedControl.getFormContext()

: selectedControl.\_context;

if (!formContext) return false;

// Only enable if parent record exists (not on Create form)

const recordId = formContext.data.entity.getId();

return recordId !== null && recordId !== "";

} catch (error) {

console.error("Error in enableAddDocuments:", error);

return false;

}

}

**3\. DocumentRecordService.ts (New Service)**

**File**: services/DocumentRecordService.ts

**Purpose**: Handle multiple Document record creation using Xrm.WebApi (not form context)

/\*\*

\* Service for creating Document records in Dataverse

\* Uses Xrm.WebApi for unlimited multi-record creation

\*/

export class DocumentRecordService {

/\*\*

\* Create multiple Document records from upload results

\*/

public async createMultipleDocuments(request: CreateMultipleDocumentsRequest): Promise&lt;CreateMultipleDocumentsResponse&gt; {

const { uploadResults, parentRecordId, metadata } = request;

const createdRecords: string\[\] = \[\];

const errors: RecordCreationError\[\] = \[\];

// Create records sequentially for better error tracking

// (Can switch to parallel if desired: Promise.allSettled)

for (let i = 0; i < uploadResults.length; i++) {

const uploadResult = uploadResults\[i\];

if (uploadResult.status === 'rejected') {

errors.push({

fileName: uploadResult.fileName,

error: "File upload failed"

});

continue;

}

try {

const recordId = await this.createSingleDocument({

fileName: uploadResult.value.data.fileName,

speMetadata: uploadResult.value.data,

parentRecordId,

metadata

});

createdRecords.push(recordId);

} catch (error) {

errors.push({

fileName: uploadResult.value.data.fileName,

error: error.message

});

}

}

return {

successCount: createdRecords.length,

errorCount: errors.length,

recordIds: createdRecords,

errors

};

}

/\*\*

\* Create a single Document record using Xrm.WebApi

\* This is the KEY method - uses global API, not form context

\*/

private async createSingleDocument(request: CreateSingleDocumentRequest): Promise&lt;string&gt; {

const { fileName, speMetadata, parentRecordId, metadata } = request;

// Build record data

const recordData: any = {

// Core SPE fields

"sprk_documentname": fileName,

"sprk_filename": fileName,

"sprk_graphitemid": speMetadata.driveItemId,

"sprk_graphdriveid": speMetadata.driveId,

"sprk_filesize": speMetadata.fileSize,

// Lookup to parent Matter - CLEAN SYNTAX

"<sprk_matter@odata.bind>": \`/sprk_matters(\${parentRecordId})\`

// Note: No need for "sprk_matter": null

// Xrm.WebApi handles @odata.bind correctly

};

// Add optional metadata fields if provided

if (metadata) {

if (metadata.documentType) {

recordData\["sprk_documenttype"\] = metadata.documentType;

}

if (metadata.description) {

recordData\["sprk_description"\] = metadata.description;

}

// ... other fields

}

// Create record using Xrm.WebApi (not context.webAPI)

// This is CRITICAL - global API has no Quick Create limitations

const result = await Xrm.WebApi.createRecord("sprk_document", recordData);

return result.id;

}

}

// Type definitions

interface CreateMultipleDocumentsRequest {

uploadResults: UploadResult\[\];

parentRecordId: string;

metadata?: DocumentMetadata;

}

interface CreateMultipleDocumentsResponse {

successCount: number;

errorCount: number;

recordIds: string\[\];

errors: RecordCreationError\[\];

}

interface CreateSingleDocumentRequest {

fileName: string;

speMetadata: SpeMetadata;

parentRecordId: string;

metadata?: DocumentMetadata;

}

interface RecordCreationError {

fileName: string;

error: string;

}

interface DocumentMetadata {

documentType?: number; // Option set value

description?: string;

// ... other optional fields

}

**4\. React Component UI Updates**

**File**: components/MultiDocumentUpload.tsx (or similar)

**Changes**: Replace form-style UI with dialog-style UI

import React, { useState } from 'react';

import { PrimaryButton, DefaultButton, Stack, ProgressIndicator, MessageBar, MessageBarType } from '@fluentui/react';

export const MultiDocumentUploadDialog: React.FC&lt;MultiDocumentUploadDialogProps&gt; = (props) => {

const { parentRecordName, onSave, onCancel } = props;

const \[files, setFiles\] = useState&lt;File\[\]&gt;(\[\]);

const \[uploading, setUploading\] = useState(false);

const \[progress, setProgress\] = useState(0);

const handleFileChange = (e: React.ChangeEvent&lt;HTMLInputElement&gt;) => {

if (e.target.files) {

setFiles(Array.from(e.target.files));

}

};

const handleSave = async () => {

setUploading(true);

try {

await onSave(files, (percent) => setProgress(percent));

} finally {

setUploading(false);

}

};

return (

&lt;div className="document-upload-dialog"&gt;

{/\* Header \*/}

&lt;div className="dialog-header"&gt;

&lt;h2&gt;Add Documents&lt;/h2&gt;

&lt;p className="parent-info"&gt;Matter: {parentRecordName}&lt;/p&gt;

&lt;/div&gt;

{/\* File Selection \*/}

&lt;div className="dialog-body"&gt;

&lt;Stack tokens={{ childrenGap: 16 }}&gt;

&lt;div className="file-input-section"&gt;

&lt;label htmlFor="fileInput" className="form-label"&gt;

Select Files \*

&lt;/label&gt;

<input

id="fileInput"

type="file"

multiple

onChange={handleFileChange}

accept="\*/\*"

disabled={uploading}

/>

{files.length > 0 && (

&lt;div className="file-list"&gt;

&lt;p&gt;{files.length} file(s) selected&lt;/p&gt;

&lt;ul&gt;

{files.map((file, idx) => (

&lt;li key={idx}&gt;

{file.name} ({(file.size / 1024 / 1024).toFixed(2)} MB)

&lt;/li&gt;

))}

&lt;/ul&gt;

&lt;/div&gt;

)}

&lt;/div&gt;

{/\* Optional metadata fields \*/}

&lt;div className="metadata-section"&gt;

&lt;label className="form-label"&gt;Document Type&lt;/label&gt;

&lt;select className="form-select"&gt;

&lt;option value=""&gt;-- Select --&lt;/option&gt;

&lt;option value="1"&gt;Contract&lt;/option&gt;

&lt;option value="2"&gt;Invoice&lt;/option&gt;

&lt;option value="3"&gt;Report&lt;/option&gt;

&lt;/select&gt;

&lt;/div&gt;

{uploading && (

<ProgressIndicator

label="Uploading and creating documents..."

percentComplete={progress / 100}

/>

)}

&lt;/Stack&gt;

&lt;/div&gt;

{/\* Footer with action buttons \*/}

&lt;div className="dialog-footer"&gt;

&lt;Stack horizontal tokens={{ childrenGap: 8 }} horizontalAlign="end"&gt;

<DefaultButton

text="Cancel"

onClick={onCancel}

disabled={uploading}

/>

<PrimaryButton

text="Upload & Save"

onClick={handleSave}

disabled={files.length === 0 || uploading}

/>

&lt;/Stack&gt;

&lt;/div&gt;

&lt;/div&gt;

);

};

**Implementation Steps**

**Phase 1: Setup & Configuration (Foundation)**

**Step 1.1: Update ControlManifest.Input.xml**

\# Location: UniversalQuickCreatePCF/ControlManifest.Input.xml

**Changes**:

&lt;?xml version="1.0" encoding="utf-8" ?&gt;

&lt;manifest&gt;

<control namespace="SpaarkeComponents"

constructor="UniversalQuickCreatePCF"

version="2.0.0"

display-name-key="Multi_Document_Upload"

description-key="Upload multiple documents to SharePoint Embedded and create Dataverse records"

control-type="standard">

&lt;!-- Input Parameters (from custom page) --&gt;

<property name="parentRecordId"

display-name-key="Parent_Record_ID"

description-key="GUID of parent Matter record"

of-type="SingleLine.Text"

usage="input"

required="true" />

<property name="parentEntityName"

display-name-key="Parent_Entity_Name"

description-key="Logical name of parent entity"

of-type="SingleLine.Text"

usage="input"

required="true" />

<property name="parentRecordName"

display-name-key="Parent_Record_Name"

description-key="Display name of parent record"

of-type="SingleLine.Text"

usage="input"

required="false" />

&lt;!-- Resources --&gt;

&lt;resources&gt;

&lt;code path="index.ts" order="1"/&gt;

&lt;css path="css/UniversalQuickCreatePCF.css" order="1"/&gt;

&lt;/resources&gt;

&lt;!-- Features --&gt;

&lt;feature-usage&gt;

&lt;uses-feature name="WebAPI" required="true" /&gt;

&lt;uses-feature name="Utility" required="true" /&gt;

&lt;/feature-usage&gt;

&lt;/control&gt;

&lt;/manifest&gt;

**Step 1.2: Create Custom Page in Solution**

**Option A: Using Power Apps Studio**

- Open solution in Power Apps (make.powerapps.com)
- Click "New" → "Page" → "Custom page"
- Name: sprk_multifileupload_page
- Display Name: "Add Documents"
- Add PCF control to canvas
- Configure control properties to bind to page parameters

**Option B: Using Solution XML**

&lt;!-- File: Other/CustomPages/sprk_multifileupload_page.xml --&gt;

&lt;custompage name="sprk_multifileupload_page"&gt;

&lt;displayname&gt;Add Documents&lt;/displayname&gt;

&lt;description&gt;Upload multiple documents&lt;/description&gt;

&lt;/custompage&gt;

**Step 1.3: Create Command Bar Button Web Resource**

\# Create file: WebResources/sprk_subgrid_commands.js

\# Copy the addMultipleDocuments() function from "Components to Create" section above

Upload to solution as JavaScript Web Resource.

**Step 1.4: Configure Subgrid Command Button**

**Using Power Apps Command Designer:**

- Open Model-Driven App in App Designer
- Navigate to Documents subgrid configuration
- Edit Command Bar → Add Command
- Configure:
  - **Label**: "Add Documents"
  - **Icon**: DocumentAdd (or custom)
  - **Action**: Run JavaScript
  - **Library**: sprk_subgrid_commands.js
  - **Function**: addMultipleDocuments
  - **Parameters**: SelectedControl (first parameter)
  - **Visibility**: Show for existing records only

**Phase 2: Service Layer Refactoring**

**Step 2.1: Create DocumentRecordService.ts**

\# Create new file: services/DocumentRecordService.ts

\# Implement full class from "Components to Create" section

**Key Requirements**:

- Use Xrm.WebApi.createRecord() (not context.webAPI)
- Clean lookup syntax: "<sprk_matter@odata.bind>": "/sprk_matters(guid)"
- Batch error handling (continue on failure)
- Return created record IDs

**Step 2.2: Refactor MultiFileUploadService.ts**

**Changes**:

// File: services/MultiFileUploadService.ts

// REMOVE Phase 2 record creation logic (lines ~161-231)

// Keep only Phase 1 (file uploads)

public async uploadFiles(

files: File\[\],

driveId: string

): Promise&lt;UploadResult\[\]&gt; {

// Determine strategy

const strategy = this.determineStrategy(files);

if (strategy === 'sync-parallel') {

// Upload files only (no record creation)

return await this.handleSyncParallelUpload(files, driveId);

} else {

// Long-running logic

return await this.handleLongRunningUpload(files, driveId);

}

}

private async handleSyncParallelUpload(

files: File\[\],

driveId: string

): Promise&lt;UploadResult\[\]&gt; {

// Phase 1: Upload ALL files in parallel

const uploadResults = await Promise.allSettled(

files.map(file =>

this.fileUploadService.uploadFile({ file, driveId })

)

);

// Map results to standardized format

const results: UploadResult\[\] = uploadResults.map((result, index) => ({

fileName: files\[index\].name,

status: result.status,

data: result.status === 'fulfilled' ? result.value.data : null,

error: result.status === 'rejected' ? result.reason : null

}));

// Phase 2 (record creation) is NO LONGER HERE

// It will be handled by DocumentRecordService in PCF component

return results;

}

**Step 2.3: Simplify DataverseRecordService.ts (or deprecate)**

**Option A: Keep but simplify for single-record scenarios**

// File: services/DataverseRecordService.ts

// If you still need this for other PCF scenarios, keep it

// But for multi-document upload, use DocumentRecordService instead

// Simplify to remove Quick Create workarounds:

public async createDocument(request: CreateDocumentRequest): Promise&lt;string&gt; {

const recordData = {

"sprk_documentname": request.fileName,

"sprk_graphitemid": request.speMetadata.driveItemId,

"<sprk_matter@odata.bind>": \`/sprk_matters(\${request.parentRecordId})\`

};

// Can use Xrm.WebApi here too if available

const result = await Xrm.WebApi.createRecord("sprk_document", recordData);

return result.id;

}

**Option B: Deprecate entirely**

- Use DocumentRecordService for all scenarios
- Remove DataverseRecordService.ts
- Update imports across codebase

**Phase 3: PCF Component Refactoring**

**Step 3.1: Update UniversalQuickCreatePCF.ts - init() Method**

// File: UniversalQuickCreatePCF.ts

export class UniversalQuickCreatePCF implements ComponentFramework.StandardControl&lt;IInputs, IOutputs&gt; {

private \_context: ComponentFramework.Context&lt;IInputs&gt;;

private \_container: HTMLDivElement;

private \_notifyOutputChanged: () => void;

// Parent context (from custom page parameters)

private \_parentRecordId: string;

private \_parentEntityName: string;

private \_parentRecordName: string;

// Services

private \_multiFileService: MultiFileUploadService;

private \_documentRecordService: DocumentRecordService; // NEW

private \_logger: LoggingService;

/\*\*

\* Initialize PCF control in custom page context

\*/

public init(

context: ComponentFramework.Context&lt;IInputs&gt;,

notifyOutputChanged: () => void,

state: ComponentFramework.Dictionary,

container: HTMLDivElement

): void {

this.\_context = context;

this.\_container = container;

this.\_notifyOutputChanged = notifyOutputChanged;

// Extract parent context from input parameters

this.\_parentRecordId = context.parameters.parentRecordId?.raw || '';

this.\_parentEntityName = context.parameters.parentEntityName?.raw || '';

this.\_parentRecordName = context.parameters.parentRecordName?.raw || '';

// Validate parent context

if (!this.\_parentRecordId || !this.\_parentEntityName) {

this.showError("Missing required parent context. This control must be opened from a parent record.");

return;

}

// Initialize services

this.\_logger = new LoggingService(context);

this.\_multiFileService = new MultiFileUploadService(/\* dependencies \*/);

this.\_documentRecordService = new DocumentRecordService(); // NEW SERVICE

// Render React component

this.renderComponent();

this.\_logger.logInfo("PCF initialized in custom page", {

parentRecordId: this.\_parentRecordId,

parentEntityName: this.\_parentEntityName

});

}

// ... rest of class

}

**Step 3.2: Refactor handleSaveAndCreateDocuments()**

// File: UniversalQuickCreatePCF.ts

/\*\*

\* Main handler for file upload and document creation

\* NEW APPROACH: No form save interception, direct control

\*/

private async handleUploadAndSave(files: File\[\]): Promise&lt;void&gt; {

try {

this.\_logger.logInfo("Starting upload and save process", {

fileCount: files.length,

totalSize: files.reduce((sum, f) => sum + f.size, 0)

});

// Update UI state

this.setState({ uploading: true, progress: 0 });

// PHASE 1: Upload files to SharePoint Embedded (unchanged)

const driveId = await this.getDriveId(); // Your existing logic

const uploadResults = await this.\_multiFileService.uploadFiles(files, driveId);

this.\_logger.logInfo("File uploads completed", {

successCount: uploadResults.filter(r => r.status === 'fulfilled').length,

errorCount: uploadResults.filter(r => r.status === 'rejected').length

});

this.setState({ progress: 50 }); // 50% done (uploads complete)

// PHASE 2: Create Document records (NEW APPROACH)

const metadata = this.getMetadataFromInputs(); // Optional fields from UI

const recordResults = await this.\_documentRecordService.createMultipleDocuments({

uploadResults,

parentRecordId: this.\_parentRecordId,

metadata

});

this.\_logger.logInfo("Document records created", {

successCount: recordResults.successCount,

errorCount: recordResults.errorCount

});

this.setState({ progress: 100 }); // Done

// Show result to user

if (recordResults.errorCount > 0) {

this.showWarning(

\`Created \${recordResults.successCount} documents. \${recordResults.errorCount} failed.\`,

recordResults.errors

);

} else {

this.showSuccess(\`Successfully created \${recordResults.successCount} document(s).\`);

}

// Close dialog and return result to caller

this.closeDialog({

recordCount: recordResults.successCount,

recordIds: recordResults.recordIds

});

} catch (error) {

this.\_logger.logError("Upload and save failed", error);

this.showError("An error occurred during upload: " + error.message);

this.setState({ uploading: false });

}

}

/\*\*

\* Close the custom page dialog

\*/

private closeDialog(result?: any): void {

// Return result to the caller (command button script)

if (this.\_context.mode.isControlDisabled === false) {

// Signal completion

this.\_context.navigation.close(result);

}

}

/\*\*

\* Get optional metadata from PCF input fields

\*/

private getMetadataFromInputs(): DocumentMetadata {

// If your PCF has input fields for document type, description, etc.

return {

documentType: this.\_selectedDocumentType,

description: this.\_descriptionValue,

// ... other optional fields

};

}

**Step 3.3: Remove Quick Create-Specific Methods**

// DELETE these methods (no longer needed):

// ❌ getFormData() - No form to extract data from

// ❌ preventDefault() save interception

// ❌ Form validation hooks

// ❌ \_notifyOutputChanged() for form save (keep for other uses if needed)

// ❌ Any XRM form context manipulation

**Phase 4: UI Component Updates**

**Step 4.1: Update React Component**

// File: components/MultiDocumentUpload.tsx

import React, { useState, useCallback } from 'react';

import { PrimaryButton, DefaultButton, Stack, ProgressIndicator, MessageBar, MessageBarType } from '@fluentui/react';

interface MultiDocumentUploadProps {

parentRecordName: string;

onSave: (files: File\[\], onProgress: (percent: number) => void) => Promise&lt;void&gt;;

onCancel: () => void;

maxFiles?: number;

maxFileSize?: number;

}

export const MultiDocumentUpload: React.FC&lt;MultiDocumentUploadProps&gt; = ({

parentRecordName,

onSave,

onCancel,

maxFiles = 10,

maxFileSize = 10 \* 1024 \* 1024 // 10MB

}) => {

const \[files, setFiles\] = useState&lt;File\[\]&gt;(\[\]);

const \[uploading, setUploading\] = useState(false);

const \[progress, setProgress\] = useState(0);

const \[error, setError\] = useState&lt;string | null&gt;(null);

const handleFileChange = useCallback((e: React.ChangeEvent&lt;HTMLInputElement&gt;) => {

setError(null);

if (!e.target.files || e.target.files.length === 0) return;

const selectedFiles = Array.from(e.target.files);

// Validation

if (selectedFiles.length > maxFiles) {

setError(\`Maximum \${maxFiles} files allowed\`);

return;

}

const oversizedFiles = selectedFiles.filter(f => f.size > maxFileSize);

if (oversizedFiles.length > 0) {

setError(\`Files must be under \${maxFileSize / 1024 / 1024}MB\`);

return;

}

setFiles(selectedFiles);

}, \[maxFiles, maxFileSize\]);

const handleSave = useCallback(async () => {

if (files.length === 0) return;

setUploading(true);

setError(null);

try {

await onSave(files, setProgress);

} catch (err: any) {

setError(err.message || "An error occurred during upload");

setUploading(false);

}

}, \[files, onSave\]);

const formatFileSize = (bytes: number): string => {

return (bytes / 1024 / 1024).toFixed(2) + ' MB';

};

return (

&lt;div className="multi-document-upload"&gt;

{/\* Header \*/}

&lt;div className="dialog-header"&gt;

&lt;h2 className="dialog-title"&gt;Add Documents&lt;/h2&gt;

&lt;p className="parent-info"&gt;

&lt;strong&gt;Matter:&lt;/strong&gt; {parentRecordName}

&lt;/p&gt;

&lt;/div&gt;

{/\* Body \*/}

&lt;div className="dialog-body"&gt;

&lt;Stack tokens={{ childrenGap: 20 }}&gt;

{/\* Error Message \*/}

{error && (

&lt;MessageBar messageBarType={MessageBarType.error} onDismiss={() =&gt; setError(null)}>

{error}

&lt;/MessageBar&gt;

)}

{/\* File Selection \*/}

&lt;div className="form-section"&gt;

&lt;label htmlFor="fileInput" className="form-label"&gt;

Select Files &lt;span className="required"&gt;\*&lt;/span&gt;

&lt;/label&gt;

<input

id="fileInput"

type="file"

multiple

onChange={handleFileChange}

disabled={uploading}

className="file-input"

accept="\*/\*"

/>

&lt;div className="form-hint"&gt;

Maximum {maxFiles} files, {maxFileSize / 1024 / 1024}MB per file

&lt;/div&gt;

&lt;/div&gt;

{/\* Selected Files List \*/}

{files.length > 0 && (

&lt;div className="selected-files"&gt;

&lt;h4&gt;{files.length} file(s) selected:&lt;/h4&gt;

&lt;ul className="file-list"&gt;

{files.map((file, index) => (

&lt;li key={index} className="file-item"&gt;

&lt;span className="file-name"&gt;{file.name}&lt;/span&gt;

&lt;span className="file-size"&gt;({formatFileSize(file.size)})&lt;/span&gt;

&lt;/li&gt;

))}

&lt;/ul&gt;

&lt;/div&gt;

)}

{/\* Upload Progress \*/}

{uploading && (

&lt;div className="progress-section"&gt;

<ProgressIndicator

label="Uploading files and creating documents..."

description={\`\${progress}% complete\`}

percentComplete={progress / 100}

/>

&lt;/div&gt;

)}

&lt;/Stack&gt;

&lt;/div&gt;

{/\* Footer \*/}

&lt;div className="dialog-footer"&gt;

&lt;Stack horizontal tokens={{ childrenGap: 10 }} horizontalAlign="end"&gt;

<DefaultButton

text="Cancel"

onClick={onCancel}

disabled={uploading}

/>

<PrimaryButton

text={uploading ? "Uploading..." : "Upload & Save"}

onClick={handleSave}

disabled={files.length === 0 || uploading}

/>

&lt;/Stack&gt;

&lt;/div&gt;

&lt;/div&gt;

);

};

**Step 4.2: Update CSS for Dialog Styling**

/\* File: css/UniversalQuickCreatePCF.css \*/

/\* Dialog Container \*/

.multi-document-upload {

display: flex;

flex-direction: column;

height: 100%;

background: white;

font-family: "Segoe UI", -apple-system, BlinkMacSystemFont, sans-serif;

}

/\* Header \*/

.dialog-header {

padding: 20px 24px;

border-bottom: 1px solid #edebe9;

}

.dialog-title {

font-size: 20px;

font-weight: 600;

color: #323130;

margin: 0 0 8px 0;

}

.parent-info {

font-size: 14px;

color: #605e5c;

margin: 0;

}

/\* Body \*/

.dialog-body {

flex: 1;

padding: 24px;

overflow-y: auto;

}

.form-section {

margin-bottom: 20px;

}

.form-label {

display: block;

font-size: 14px;

font-weight: 600;

color: #323130;

margin-bottom: 8px;

}

.required {

color: #a4262c;

}

.form-hint {

font-size: 12px;

color: #605e5c;

margin-top: 4px;

}

.file-input {

display: block;

width: 100%;

padding: 8px;

font-size: 14px;

border: 1px solid #8a8886;

border-radius: 2px;

}

.file-input:disabled {

background: #f3f2f1;

cursor: not-allowed;

}

/\* Selected Files \*/

.selected-files {

background: #f3f2f1;

padding: 16px;

border-radius: 4px;

}

.selected-files h4 {

font-size: 14px;

font-weight: 600;

color: #323130;

margin: 0 0 12px 0;

}

.file-list {

list-style: none;

padding: 0;

margin: 0;

}

.file-item {

padding: 8px 0;

border-bottom: 1px solid #e1dfdd;

display: flex;

justify-content: space-between;

align-items: center;

}

.file-item:last-child {

border-bottom: none;

}

.file-name {

font-size: 14px;

color: #323130;

flex: 1;

}

.file-size {

font-size: 12px;

color: #605e5c;

margin-left: 12px;

}

/\* Progress \*/

.progress-section {

background: #f3f2f1;

padding: 16px;

border-radius: 4px;

}

/\* Footer \*/

.dialog-footer {

padding: 16px 24px;

border-top: 1px solid #edebe9;

background: #faf9f8;

}

**Phase 5: Testing & Validation**

**Step 5.1: Unit Testing Checklist**

**Test DocumentRecordService.ts:**

// Test: Single record creation

test('creates single document record with correct lookup', async () => {

const service = new DocumentRecordService();

const recordId = await service.createSingleDocument({

fileName: 'test.pdf',

speMetadata: mockSpeMetadata,

parentRecordId: mockMatterId

});

expect(recordId).toBeDefined();

expect(typeof recordId).toBe('string');

});

// Test: Multiple record creation

test('creates multiple document records successfully', async () => {

const service = new DocumentRecordService();

const result = await service.createMultipleDocuments({

uploadResults: mockUploadResults,

parentRecordId: mockMatterId

});

expect(result.successCount).toBe(3);

expect(result.errorCount).toBe(0);

expect(result.recordIds).toHaveLength(3);

});

// Test: Partial failure handling

test('continues creating records after failure', async () => {

const service = new DocumentRecordService();

const result = await service.createMultipleDocuments({

uploadResults: mockMixedResults, // Some succeed, some fail

parentRecordId: mockMatterId

});

expect(result.successCount).toBeGreaterThan(0);

expect(result.errorCount).toBeGreaterThan(0);

expect(result.errors).toBeDefined();

});

**Step 5.2: Integration Testing Scenarios**

**Scenario 1: Single File Upload**

- Navigate to Matter record
- Click Documents subgrid "+ Add Documents" button
- Select 1 file (< 10MB)
- Click "Upload & Save"
- Verify:
  - Dialog closes
  - Subgrid refreshes
  - 1 Document record appears
  - Record has correct driveItemId and lookup to Matter

**Scenario 2: Multiple Files (5 files)**

- Click "+ Add Documents"
- Select 5 files
- Click "Upload & Save"
- Verify:
  - All 5 files upload to SharePoint Embedded
  - All 5 Document records created
  - All records linked to correct Matter
  - Subgrid shows all 5 documents

**Scenario 3: Maximum Files (10 files)**

- Click "+ Add Documents"
- Select 10 files
- Verify:
  - No validation errors
  - All files upload successfully
  - All records created

**Scenario 4: Exceed Maximum (11 files)**

- Click "+ Add Documents"
- Attempt to select 11 files
- Verify:
  - Error message appears
  - Upload button disabled

**Scenario 5: Network Failure During Upload**

- Click "+ Add Documents"
- Select 3 files
- Disconnect network mid-upload
- Verify:
  - Error handling shows appropriate message
  - No orphaned records created
  - User can retry

**Scenario 6: Partial Upload Success**

- Select 5 files, where 3 succeed, 2 fail (simulate server error)
- Verify:
  - 3 Document records created
  - 2 errors logged
  - User sees summary: "Created 3 documents. 2 failed."
  - Subgrid shows successful records

**Step 5.3: Cross-Browser Testing**

Test in:

- ✅ Chrome (latest)
- ✅ Edge (latest)
- ✅ Firefox (latest)
- ✅ Safari (if Mac users)

**Step 5.4: Performance Testing**

**Test: 20 Files (stress test)**

- Upload 20 files (5MB each)
- Monitor:
  - Upload time
  - Record creation time
  - Memory usage
  - No browser crashes

**Target Metrics:**

- Upload: < 30 seconds for 20 files (network dependent)
- Record creation: < 20 seconds for 20 records
- Total: < 1 minute end-to-end

**Code Patterns & Standards**

**Alex Butenko-Style Best Practices**

**1\. TypeScript Type Safety**

// ✅ GOOD: Strong typing throughout

interface CreateDocumentRequest {

fileName: string;

speMetadata: SpeMetadata;

parentRecordId: string;

metadata?: DocumentMetadata;

}

async function createDocument(request: CreateDocumentRequest): Promise&lt;string&gt; {

// Implementation

}

// ❌ BAD: Weak typing

async function createDocument(data: any): Promise&lt;any&gt; {

// Implementation

}

**2\. Error Handling Pattern**

// ✅ GOOD: Structured error handling

try {

const result = await operation();

this.\_logger.logInfo("Operation succeeded", { result });

return result;

} catch (error: any) {

this.\_logger.logError("Operation failed", error, {

context: "additional debugging info"

});

// User-friendly message

this.showError("Unable to complete operation. Please try again.");

// Don't swallow - rethrow or return error state

throw new Error(\`Operation failed: \${error.message}\`);

}

// ❌ BAD: Silent failures

try {

await operation();

} catch {

// Do nothing - user has no idea what happened

}

**3\. Logging Standards**

// ✅ GOOD: Structured logging with context

this.\_logger.logInfo("Creating documents", {

fileCount: files.length,

parentRecordId: this.\_parentRecordId,

totalSize: files.reduce((sum, f) => sum + f.size, 0)

});

// ❌ BAD: String concatenation

console.log("Creating " + files.length + " documents for " + this.\_parentRecordId);

**4\. Service Dependency Injection**

// ✅ GOOD: Dependencies injected in constructor

export class UniversalQuickCreatePCF {

private \_multiFileService: MultiFileUploadService;

private \_documentRecordService: DocumentRecordService;

public init(context: ComponentFramework.Context&lt;IInputs&gt;, ...): void {

this.\_multiFileService = new MultiFileUploadService(

this.\_logger,

this.\_apiClient

);

this.\_documentRecordService = new DocumentRecordService();

}

}

// ❌ BAD: Tight coupling

export class UniversalQuickCreatePCF {

public async handleSave(): Promise&lt;void&gt; {

// Directly instantiating inside method

const service = new MultiFileUploadService();

await service.upload(files);

}

}

**5\. React Component Patterns**

// ✅ GOOD: Functional components with hooks

export const MultiDocumentUpload: React.FC&lt;Props&gt; = (props) => {

const \[state, setState\] = useState(initialState);

const handleChange = useCallback(() => { ... }, \[dependencies\]);

return &lt;div&gt;...&lt;/div&gt;;

};

// ❌ BAD: Class components (outdated for new code)

export class MultiDocumentUpload extends React.Component {

// Avoid for new PCF development

}

**6\. Async/Await Over Promises**

// ✅ GOOD: Clean async/await

async function uploadAndCreate(): Promise&lt;void&gt; {

const uploads = await uploadFiles(files);

const records = await createRecords(uploads);

return records;

}

// ❌ BAD: Promise chaining

function uploadAndCreate(): Promise&lt;void&gt; {

return uploadFiles(files)

.then(uploads => createRecords(uploads))

.then(records => records);

}

**7\. OData Syntax for Lookups**

// ✅ CORRECT: Clean @odata.bind

const recordData = {

"sprk_documentname": fileName,

"<sprk_matter@odata.bind>": \`/sprk_matters(\${parentId})\`

};

// ❌ WRONG: Including base field with null

const recordData = {

"sprk_documentname": fileName,

"sprk_matter": null, // Don't do this with Xrm.WebApi

"<sprk_matter@odata.bind>": \`/sprk_matters(\${parentId})\`

};

**8\. Naming Conventions**

// Services: PascalCase with "Service" suffix

class DocumentRecordService { }

// Private fields: \_camelCase with underscore prefix

private \_parentRecordId: string;

// Public methods: camelCase

public async createMultipleDocuments(): Promise&lt;void&gt; { }

// Constants: UPPER_SNAKE_CASE

const MAX_FILE_SIZE = 10 \* 1024 \* 1024;

// React components: PascalCase

export const MultiDocumentUpload: React.FC = () => { };

**Testing & Validation**

**Deployment Checklist**

**Pre-Deployment**

- \[ \] All TypeScript compiles without errors
- \[ \] ESLint passes with no warnings
- \[ \] Unit tests pass (if implemented)
- \[ \] Custom page created in solution
- \[ \] Web resource uploaded (sprk_subgrid_commands.js)
- \[ \] Command button configured on Documents subgrid
- \[ \] PCF solution built: npm run build
- \[ \] Solution packaged and ready for import

**Post-Deployment (DEV Environment)**

- \[ \] Import solution to DEV environment
- \[ \] Publish all customizations
- \[ \] Test command button appears on Documents subgrid
- \[ \] Test dialog opens from command button
- \[ \] Test single file upload (1 file)
- \[ \] Test multiple file upload (3-5 files)
- \[ \] Test maximum files (10 files)
- \[ \] Test file size validation
- \[ \] Test network error handling
- \[ \] Test partial success scenario
- \[ \] Verify all records have correct lookup to Matter
- \[ \] Verify SharePoint Embedded itemIds stored correctly
- \[ \] Check browser console for errors

**User Acceptance Testing (UAT Environment)**

- \[ \] Import solution to UAT
- \[ \] Business users test workflow
- \[ \] Performance acceptable (< 1 min for 10 files)
- \[ \] Error messages user-friendly
- \[ \] No data loss scenarios
- \[ \] Subgrid refreshes correctly

**Production Deployment**

- \[ \] Final solution package created
- \[ \] Deployment window scheduled
- \[ \] Backup current production solution
- \[ \] Import to PROD
- \[ \] Smoke test (upload 1 file)
- \[ \] Monitor for 24 hours
- \[ \] Document any issues

**Prompts for Claude Code**

**Initial Setup Prompt**

I need you to refactor a PCF control for Power Platform. The current implementation attempts to create multiple Dataverse records from within a Quick Create form, which is causing failures because Quick Create forms are designed for single-record creation only.

\*\*Your task\*\*: Rebuild the PCF to work in a custom page/dialog instead of Quick Create form, using Xrm.WebApi for unlimited multi-record creation.

\*\*Context\*\*:

\- Current PCF: UniversalQuickCreatePCF (TypeScript, React)

\- Purpose: Upload multiple files to SharePoint Embedded and create corresponding Document records in Dataverse

\- Problem: Second createRecord() call fails with 400 error due to Quick Create context corruption

\- Solution: Move to custom page, use Xrm.WebApi instead of context.webAPI

\*\*Components to keep\*\* (they work correctly):

1\. SdapApiClient.ts - SharePoint Embedded upload

2\. FileUploadService.ts - File validation and upload orchestration

3\. MultiFileUploadService.ts - Parallel upload logic (Phase 1 only)

4\. LoggingService.ts - Telemetry

\*\*Components to create\*\*:

1\. DocumentRecordService.ts - New service using Xrm.WebApi for record creation

2\. Custom page definition

3\. Command bar button script (sprk_subgrid_commands.js)

\*\*Components to modify\*\*:

1\. UniversalQuickCreatePCF.ts - Remove Quick Create dependencies, add custom page support

2\. ControlManifest.Input.xml - Change to accept parent context parameters

3\. React components - Update UI for dialog instead of form

\*\*Code like Alex Butenko\*\*: Senior PCF developer style - clean architecture, proper TypeScript typing, separation of concerns, framework-appropriate patterns.

Start by analyzing the current architecture and confirming you understand the root cause and new approach.

**Service Layer Refactoring Prompt**

Create the new DocumentRecordService.ts service class:

\*\*Requirements\*\*:

1\. Use Xrm.WebApi.createRecord() (NOT context.webAPI)

2\. Method: createMultipleDocuments() - accepts array of upload results

3\. Method: createSingleDocument() - private helper for one record

4\. Clean OData lookup syntax: "<sprk_matter@odata.bind>": "/sprk_matters(guid)"

5\. Error handling: Continue on failure, collect errors

6\. Return: { successCount, errorCount, recordIds, errors }

7\. Full TypeScript typing with interfaces

\*\*Entity\*\*: sprk_document

\*\*Fields\*\*:

\- sprk_documentname (string)

\- sprk_filename (string)

\- sprk_graphitemid (string) - from SharePoint Embedded

\- sprk_graphdriveid (string) - from SharePoint Embedded

\- sprk_filesize (number)

\- sprk_matter (lookup to sprk_matter) - use @odata.bind

Code this as a production-ready service with proper error handling and logging integration.

**PCF Component Refactoring Prompt**

Refactor UniversalQuickCreatePCF.ts main class:

\*\*Changes needed\*\*:

1\. init() method: Extract parent context from input parameters (parentRecordId, parentEntityName, parentRecordName)

2\. Remove: getFormData(), preventDefault(), form save interception

3\. Add: handleUploadAndSave() method for direct save control

4\. Add: closeDialog() method to close custom page

5\. Update: Use DocumentRecordService instead of DataverseRecordService

6\. Update: Progress reporting (0-50% uploads, 50-100% record creation)

\*\*Flow\*\*:

User clicks "Upload & Save" → Upload files to SPE → Create all Document records → Show results → Close dialog

Maintain Alex Butenko coding standards: proper error handling, structured logging, TypeScript type safety.

**UI Component Update Prompt**

Update the React component for custom page dialog:

\*\*Requirements\*\*:

1\. Component name: MultiDocumentUploadDialog

2\. Props: parentRecordName, onSave, onCancel

3\. State: files, uploading, progress, error

4\. UI elements:

\- Header with "Add Documents" title and parent Matter name

\- File input (multiple)

\- Selected files list with size display

\- Progress indicator during upload

\- Footer with Cancel and "Upload & Save" buttons

5\. Validation:

\- Max 10 files

\- Max 10MB per file

\- Show validation errors

6\. Styling: Match Power Platform Quick Create form aesthetic (Fluent UI)

Use React functional components with hooks (useState, useCallback). Style with CSS classes for Power Platform look and feel.

**Command Button Script Prompt**

Create the web resource JavaScript file for the subgrid command button:

\*\*File\*\*: sprk_subgrid_commands.js

\*\*Function\*\*: addMultipleDocuments(selectedControl)

\*\*Logic\*\*:

1\. Get parent form context from subgrid

2\. Extract Matter ID, name, entity name

3\. Call Xrm.Navigation.navigateTo() to open custom page in dialog

4\. Pass parent context as data parameters

5\. On dialog close: Refresh subgrid, show success message

6\. Error handling: User cancellation vs. actual errors

\*\*Function\*\*: enableAddDocuments(selectedControl)

\- Enable rule: Only show button if parent record exists (not on Create form)

Follow Power Platform JavaScript best practices. Include JSDoc comments.

**Testing Validation Prompt**

Create a testing checklist and validation scenarios:

\*\*Test cases\*\*:

1\. Single file upload (happy path)

2\. Multiple files (3-5 files)

3\. Maximum files (10 files)

4\. Exceed maximum (11 files - should show error)

5\. Oversized file (>10MB - should show error)

6\. Network failure during upload

7\. Partial success (some uploads succeed, some fail)

8\. Verify lookup relationships in Dataverse

9\. Verify SharePoint Embedded itemIds stored correctly

For each test case, document:

\- Steps to execute

\- Expected behavior

\- How to verify success

\- What to check in browser console

Also include performance benchmarks (target: <1 minute for 10 files).

**Summary: Critical Success Factors**

**1\. Root Cause Understanding**

✅ **Quick Create forms are single-record contexts** - don't fight this design ✅ Use custom pages for multi-record scenarios ✅ Xrm.WebApi is the right tool for multiple record creation

**2\. Architectural Clarity**

✅ Separate file upload (SPE) from record creation (Dataverse) ✅ Keep working components (SPE upload, logging) ✅ Replace problematic components (record creation service)

**3\. Code Quality Standards**

✅ TypeScript type safety throughout ✅ Structured error handling and logging ✅ Clean OData

syntax for lookups ✅ Service layer separation of concerns ✅ Production-ready error recovery

**4\. User Experience**

✅ Custom page styled like Quick Create (familiar UX) ✅ Progress indication during long operations ✅ Clear error messages with actionable guidance ✅ Automatic subgrid refresh after completion

**5\. Testing Rigor**

✅ Unit tests for service layer ✅ Integration tests for end-to-end flow ✅ Edge case validation (file limits, network failures) ✅ Performance benchmarks documented

**Detailed Implementation Guides**

**Phase 6: Advanced Scenarios & Edge Cases**

**Scenario 6.1: Handling Duplicate File Names**

**Problem**: Multiple files with same name uploaded to SharePoint Embedded

**Solution**:

// File: services/DocumentRecordService.ts

private generateUniqueFileName(originalName: string, existingNames: Set&lt;string&gt;): string {

let fileName = originalName;

let counter = 1;

// Parse extension

const lastDotIndex = originalName.lastIndexOf('.');

const baseName = lastDotIndex > 0

? originalName.substring(0, lastDotIndex)

: originalName;

const extension = lastDotIndex > 0

? originalName.substring(lastDotIndex)

: '';

// Increment until unique

while (existingNames.has(fileName)) {

fileName = \`\${baseName}\_\${counter}\${extension}\`;

counter++;

}

existingNames.add(fileName);

return fileName;

}

public async createMultipleDocuments(request: CreateMultipleDocumentsRequest): Promise&lt;CreateMultipleDocumentsResponse&gt; {

const existingNames = new Set&lt;string&gt;();

const results = \[\];

for (const uploadResult of request.uploadResults) {

if (uploadResult.status === 'rejected') continue;

// Ensure unique file name

const uniqueFileName = this.generateUniqueFileName(

uploadResult.value.data.fileName,

existingNames

);

const recordId = await this.createSingleDocument({

fileName: uniqueFileName,

speMetadata: uploadResult.value.data,

parentRecordId: request.parentRecordId,

metadata: request.metadata

});

results.push(recordId);

}

return {

successCount: results.length,

errorCount: request.uploadResults.length - results.length,

recordIds: results,

errors: \[\]

};

}

**Scenario 6.2: Retry Failed Uploads**

**Problem**: Network interruption causes some files to fail upload

**Solution**:

// File: UniversalQuickCreatePCF.ts

private async handleUploadAndSaveWithRetry(files: File\[\]): Promise&lt;void&gt; {

const maxRetries = 3;

let attempt = 0;

let failedFiles: File\[\] = \[...files\];

let allResults: UploadResult\[\] = \[\];

while (attempt &lt; maxRetries && failedFiles.length &gt; 0) {

attempt++;

this.\_logger.logInfo(\`Upload attempt \${attempt}\`, {

fileCount: failedFiles.length

});

// Attempt upload

const uploadResults = await this.\_multiFileService.uploadFiles(

failedFiles,

this.\_driveId

);

// Separate successes from failures

const succeeded = uploadResults.filter(r => r.status === 'fulfilled');

const failed = uploadResults.filter(r => r.status === 'rejected');

allResults.push(...succeeded);

if (failed.length === 0) {

// All succeeded

break;

}

// Prepare retry list

failedFiles = failed.map((result, index) => {

const originalIndex = uploadResults.indexOf(result);

return failedFiles\[originalIndex\];

});

// Wait before retry (exponential backoff)

if (attempt &lt; maxRetries && failedFiles.length &gt; 0) {

const delay = Math.pow(2, attempt) \* 1000; // 2s, 4s, 8s

this.\_logger.logInfo(\`Retrying in \${delay}ms\`, {

remainingFiles: failedFiles.length

});

await new Promise(resolve => setTimeout(resolve, delay));

}

}

// Create records for successful uploads

if (allResults.length > 0) {

await this.\_documentRecordService.createMultipleDocuments({

uploadResults: allResults,

parentRecordId: this.\_parentRecordId,

metadata: this.getMetadataFromInputs()

});

}

// Report results

if (failedFiles.length > 0) {

this.showWarning(

\`Uploaded \${allResults.length} of \${files.length} files. \${failedFiles.length} failed after \${maxRetries} attempts.\`,

failedFiles.map(f => ({ fileName: f.name, error: "Upload failed" }))

);

} else {

this.showSuccess(\`Successfully uploaded all \${files.length} files.\`);

}

}

**Scenario 6.3: Large File Upload Progress Tracking**

**Problem**: Large files take time, users need detailed progress feedback

**Solution**:

// File: services/FileUploadService.ts

public async uploadFileWithProgress(

request: UploadFileRequest,

onProgress: (percent: number) => void

): Promise&lt;ApiResponse<SpeMetadata&gt;> {

const { file, driveId } = request;

// For files > 4MB, use chunked upload

if (file.size > 4 \* 1024 \* 1024) {

return await this.uploadLargeFile(file, driveId, onProgress);

}

// For smaller files, single upload

onProgress(0);

const result = await this.apiClient.uploadFile(request);

onProgress(100);

return result;

}

private async uploadLargeFile(

file: File,

driveId: string,

onProgress: (percent: number) => void

): Promise&lt;ApiResponse<SpeMetadata&gt;> {

const chunkSize = 4 \* 1024 \* 1024; // 4MB chunks

const totalChunks = Math.ceil(file.size / chunkSize);

// Create upload session

const session = await this.apiClient.createUploadSession(driveId, file.name);

// Upload chunks

for (let i = 0; i < totalChunks; i++) {

const start = i \* chunkSize;

const end = Math.min(start + chunkSize, file.size);

const chunk = file.slice(start, end);

await this.apiClient.uploadChunk(session.uploadUrl, chunk, start, end, file.size);

// Report progress

const percent = Math.round(((i + 1) / totalChunks) \* 100);

onProgress(percent);

}

// Finalize upload

const metadata = await this.apiClient.completeUploadSession(session.uploadUrl);

return { data: metadata, error: null };

}

**Update PCF to show per-file progress:**

// File: UniversalQuickCreatePCF.ts

private async handleUploadAndSave(files: File\[\]): Promise&lt;void&gt; {

const fileProgress: Map&lt;string, number&gt; = new Map();

// Initialize progress for each file

files.forEach(file => fileProgress.set(file.name, 0));

// Upload with per-file progress tracking

const uploadPromises = files.map(file =>

this.\_fileUploadService.uploadFileWithProgress(

{ file, driveId: this.\_driveId },

(percent) => {

fileProgress.set(file.name, percent);

this.updateOverallProgress(fileProgress);

}

)

);

const uploadResults = await Promise.allSettled(uploadPromises);

// Continue with record creation...

}

private updateOverallProgress(fileProgress: Map&lt;string, number&gt;): void {

const totalFiles = fileProgress.size;

const totalProgress = Array.from(fileProgress.values()).reduce((sum, p) => sum + p, 0);

const averageProgress = totalProgress / totalFiles;

// Update UI

this.setState({

progress: Math.round(averageProgress / 2) // First 50% is uploads

});

}

**Scenario 6.4: Concurrent User Sessions**

**Problem**: Multiple users uploading to same Matter simultaneously

**Solution**: Use optimistic concurrency or server-side locking

// File: services/DocumentRecordService.ts

/\*\*

\* Create document with concurrency check

\* Ensures no duplicate records if multiple users upload simultaneously

\*/

private async createSingleDocument(request: CreateSingleDocumentRequest): Promise&lt;string&gt; {

const { fileName, speMetadata, parentRecordId } = request;

// Check if document with this driveItemId already exists

const existingRecords = await Xrm.WebApi.retrieveMultipleRecords(

"sprk_document",

\`?\$select=sprk_documentid&\$filter=sprk_graphitemid eq '\${speMetadata.driveItemId}' and \_sprk_matter_value eq '\${parentRecordId}'&\$top=1\`

);

if (existingRecords.entities.length > 0) {

this.\_logger.logWarning("Document already exists", {

driveItemId: speMetadata.driveItemId,

existingRecordId: existingRecords.entities\[0\].sprk_documentid

});

// Return existing record ID instead of creating duplicate

return existingRecords.entities\[0\].sprk_documentid;

}

// Safe to create

const recordData = {

"sprk_documentname": fileName,

"sprk_graphitemid": speMetadata.driveItemId,

"sprk_graphdriveid": speMetadata.driveId,

"<sprk_matter@odata.bind>": \`/sprk_matters(\${parentRecordId})\`

};

const result = await Xrm.WebApi.createRecord("sprk_document", recordData);

return result.id;

}

**Scenario 6.5: Audit Trail & Logging**

**Problem**: Need to track who uploaded which files and when

**Solution**: Add audit fields to Document records

// File: services/DocumentRecordService.ts

private async createSingleDocument(request: CreateSingleDocumentRequest): Promise&lt;string&gt; {

const { fileName, speMetadata, parentRecordId, metadata } = request;

// Get current user info

const userSettings = await Xrm.Utility.getGlobalContext().userSettings;

const userId = userSettings.userId.replace(/\[{}\]/g, '');

const userName = userSettings.userName;

const recordData = {

// Core fields

"sprk_documentname": fileName,

"sprk_filename": fileName,

"sprk_graphitemid": speMetadata.driveItemId,

"sprk_graphdriveid": speMetadata.driveId,

"sprk_filesize": speMetadata.fileSize,

// Lookup to parent

"<sprk_matter@odata.bind>": \`/sprk_matters(\${parentRecordId})\`,

// Audit fields (if they exist in your schema)

"sprk_uploadedby": userName,

"sprk_uploadedon": new Date().toISOString(),

"sprk_uploadsource": "MultiDocumentUpload_PCF_v2.0",

// Optional: Set owner to current user

"<ownerid@odata.bind>": \`/systemusers(\${userId})\`

};

const result = await Xrm.WebApi.createRecord("sprk_document", recordData);

// Log to Application Insights or custom logging

this.\_logger.logInfo("Document record created", {

recordId: result.id,

fileName: fileName,

uploadedBy: userName,

parentRecordId: parentRecordId,

driveItemId: speMetadata.driveItemId

});

return result.id;

}

**Phase 7: Performance Optimization**

**Optimization 7.1: Batch Record Creation (Advanced)**

**Problem**: Creating 50+ records sequentially is slow

**Solution**: Use Dataverse Web API \$batch endpoint

// File: services/DocumentRecordService.ts

/\*\*

\* Create multiple documents using batch request for better performance

\* Use this for 10+ records

\*/

public async createMultipleDocumentsBatch(request: CreateMultipleDocumentsRequest): Promise&lt;CreateMultipleDocumentsResponse&gt; {

const { uploadResults, parentRecordId, metadata } = request;

// Filter successful uploads

const successfulUploads = uploadResults.filter(r => r.status === 'fulfilled');

if (successfulUploads.length === 0) {

return {

successCount: 0,

errorCount: uploadResults.length,

recordIds: \[\],

errors: uploadResults.map(r => ({

fileName: r.value?.data?.fileName || 'Unknown',

error: 'Upload failed'

}))

};

}

// Build batch request

const batchId = this.generateBatchId();

const changesetId = this.generateChangesetId();

// Construct batch body

let batchBody = \`--batch_\${batchId}\\r\\n\`;

batchBody += \`Content-Type: multipart/mixed; boundary=changeset_\${changesetId}\\r\\n\\r\\n\`;

successfulUploads.forEach((uploadResult, index) => {

const speMetadata = uploadResult.value.data;

const recordData = {

"sprk_documentname": speMetadata.fileName,

"sprk_filename": speMetadata.fileName,

"sprk_graphitemid": speMetadata.driveItemId,

"sprk_graphdriveid": speMetadata.driveId,

"sprk_filesize": speMetadata.fileSize,

"<sprk_matter@odata.bind>": \`/sprk_matters(\${parentRecordId})\`

};

batchBody += \`--changeset_\${changesetId}\\r\\n\`;

batchBody += \`Content-Type: application/http\\r\\n\`;

batchBody += \`Content-Transfer-Encoding: binary\\r\\n\`;

batchBody += \`Content-ID: \${index + 1}\\r\\n\\r\\n\`;

batchBody += \`POST \${this.getWebApiUrl()}/sprk_documents HTTP/1.1\\r\\n\`;

batchBody += \`Content-Type: application/json\\r\\n\\r\\n\`;

batchBody += JSON.stringify(recordData) + '\\r\\n';

});

batchBody += \`--changeset_\${changesetId}--\\r\\n\\r\\n\`;

batchBody += \`--batch_\${batchId}--\\r\\n\`;

// Execute batch request

try {

const response = await this.executeBatchRequest(batchId, batchBody);

const parsedResults = this.parseBatchResponse(response);

return {

successCount: parsedResults.successes.length,

errorCount: parsedResults.errors.length,

recordIds: parsedResults.successes.map(r => r.id),

errors: parsedResults.errors

};

} catch (error) {

this.\_logger.logError("Batch request failed", error);

// Fallback to sequential creation

this.\_logger.logWarning("Falling back to sequential record creation");

return await this.createMultipleDocumentsSequential(request);

}

}

private async executeBatchRequest(batchId: string, batchBody: string): Promise&lt;string&gt; {

const clientUrl = Xrm.Utility.getGlobalContext().getClientUrl();

const webApiUrl = \`\${clientUrl}/api/data/v9.2/\$batch\`;

const response = await fetch(webApiUrl, {

method: 'POST',

headers: {

'Content-Type': \`multipart/mixed; boundary=batch_\${batchId}\`,

'Accept': 'application/json',

'OData-MaxVersion': '4.0',

'OData-Version': '4.0'

},

body: batchBody,

credentials: 'same-origin'

});

if (!response.ok) {

throw new Error(\`Batch request failed: \${response.status} \${response.statusText}\`);

}

return await response.text();

}

private parseBatchResponse(responseText: string): BatchResults {

const successes: Array&lt;{ id: string }&gt; = \[\];

const errors: RecordCreationError\[\] = \[\];

// Parse multipart response

const parts = responseText.split(/--changeset_\[^\\r\\n\]+/);

parts.forEach(part => {

if (part.includes('HTTP/1.1 201') || part.includes('HTTP/1.1 204')) {

// Success - extract ID from response

const idMatch = part.match(/"sprk_documentid":"(\[^"\]+)"/);

if (idMatch) {

successes.push({ id: idMatch\[1\] });

}

} else if (part.includes('HTTP/1.1 4') || part.includes('HTTP/1.1 5')) {

// Error

const errorMatch = part.match(/"message":"(\[^"\]+)"/);

errors.push({

fileName: 'Unknown', // Would need to track Content-ID mapping

error: errorMatch ? errorMatch\[1\] : 'Unknown error'

});

}

});

return { successes, errors };

}

private generateBatchId(): string {

return 'batch_' + Date.now() + '\_' + Math.random().toString(36).substr(2, 9);

}

private generateChangesetId(): string {

return 'changeset_' + Date.now() + '\_' + Math.random().toString(36).substr(2, 9);

}

private getWebApiUrl(): string {

const clientUrl = Xrm.Utility.getGlobalContext().getClientUrl();

return \`\${clientUrl}/api/data/v9.2\`;

}

interface BatchResults {

successes: Array&lt;{ id: string }&gt;;

errors: RecordCreationError\[\];

}

**When to use batch vs. sequential:**

// File: services/DocumentRecordService.ts

public async createMultipleDocuments(request: CreateMultipleDocumentsRequest): Promise&lt;CreateMultipleDocumentsResponse&gt; {

const uploadCount = request.uploadResults.filter(r => r.status === 'fulfilled').length;

// Use batch for 10+ records

if (uploadCount >= 10) {

this.\_logger.logInfo("Using batch request for performance", { count: uploadCount });

return await this.createMultipleDocumentsBatch(request);

}

// Use sequential for < 10 records (simpler, easier to debug)

this.\_logger.logInfo("Using sequential creation", { count: uploadCount });

return await this.createMultipleDocumentsSequential(request);

}

**Optimization 7.2: Parallel Record Creation (Alternative to Batch)**

**Problem**: Sequential creation is slow, batch is complex

**Solution**: Create records in parallel (simpler than batch, faster than sequential)

// File: services/DocumentRecordService.ts

/\*\*

\* Create multiple documents in parallel

\* Simpler than batch, faster than sequential

\* Good for 5-20 records

\*/

private async createMultipleDocumentsParallel(request: CreateMultipleDocumentsRequest): Promise&lt;CreateMultipleDocumentsResponse&gt; {

const { uploadResults, parentRecordId, metadata } = request;

const successfulUploads = uploadResults.filter(r => r.status === 'fulfilled');

// Create all records in parallel

const createPromises = successfulUploads.map(uploadResult =>

this.createSingleDocument({

fileName: uploadResult.value.data.fileName,

speMetadata: uploadResult.value.data,

parentRecordId,

metadata

}).then(

id => ({ status: 'fulfilled' as const, value: id }),

error => ({ status: 'rejected' as const, reason: error })

)

);

const results = await Promise.allSettled(createPromises);

// Process results

const recordIds: string\[\] = \[\];

const errors: RecordCreationError\[\] = \[\];

results.forEach((result, index) => {

if (result.status === 'fulfilled' && result.value.status === 'fulfilled') {

recordIds.push(result.value.value);

} else {

const fileName = successfulUploads\[index\].value.data.fileName;

const error = result.status === 'rejected'

? result.reason.message

: result.value.reason.message;

errors.push({ fileName, error });

}

});

return {

successCount: recordIds.length,

errorCount: errors.length,

recordIds,

errors

};

}

**Smart strategy selection:**

// File: services/DocumentRecordService.ts

public async createMultipleDocuments(request: CreateMultipleDocumentsRequest): Promise&lt;CreateMultipleDocumentsResponse&gt; {

const uploadCount = request.uploadResults.filter(r => r.status === 'fulfilled').length;

// Strategy selection based on count

if (uploadCount === 0) {

return {

successCount: 0,

errorCount: request.uploadResults.length,

recordIds: \[\],

errors: \[\]

};

} else if (uploadCount <= 3) {

// Sequential: Simplest, easiest debugging

return await this.createMultipleDocumentsSequential(request);

} else if (uploadCount <= 15) {

// Parallel: Good balance of speed and simplicity

return await this.createMultipleDocumentsParallel(request);

} else {

// Batch: Best performance for large volumes

return await this.createMultipleDocumentsBatch(request);

}

}

**Optimization 7.3: Caching Drive ID**

**Problem**: Fetching driveId on every upload adds latency

**Solution**: Cache driveId per parent Matter

// File: services/DriveService.ts (new)

export class DriveService {

private \_driveIdCache: Map&lt;string, CachedDriveId&gt; = new Map();

private \_cacheDuration = 5 \* 60 \* 1000; // 5 minutes

/\*\*

\* Get drive ID for Matter, with caching

\*/

public async getDriveIdForMatter(matterId: string): Promise&lt;string&gt; {

// Check cache

const cached = this.\_driveIdCache.get(matterId);

if (cached && (Date.now() - cached.timestamp) < this.\_cacheDuration) {

this.\_logger.logInfo("Drive ID cache hit", { matterId });

return cached.driveId;

}

// Cache miss - fetch from Dataverse

this.\_logger.logInfo("Drive ID cache miss, fetching", { matterId });

const matterRecord = await Xrm.WebApi.retrieveRecord(

"sprk_matter",

matterId,

"?\$select=sprk_graphdriveid"

);

if (!matterRecord.sprk_graphdriveid) {

throw new Error("Matter does not have a Drive ID");

}

// Update cache

this.\_driveIdCache.set(matterId, {

driveId: matterRecord.sprk_graphdriveid,

timestamp: Date.now()

});

return matterRecord.sprk_graphdriveid;

}

/\*\*

\* Clear cache for specific Matter (call after Matter update)

\*/

public clearCache(matterId: string): void {

this.\_driveIdCache.delete(matterId);

}

/\*\*

\* Clear all cache (call on PCF destroy)

\*/

public clearAllCache(): void {

this.\_driveIdCache.clear();

}

}

interface CachedDriveId {

driveId: string;

timestamp: number;

}

**Use in PCF:**

// File: UniversalQuickCreatePCF.ts

private \_driveService: DriveService;

public init(context: ComponentFramework.Context&lt;IInputs&gt;, ...): void {

// ... other initialization

this.\_driveService = new DriveService(this.\_logger);

}

private async handleUploadAndSave(files: File\[\]): Promise&lt;void&gt; {

// Get driveId with caching

const driveId = await this.\_driveService.getDriveIdForMatter(this.\_parentRecordId);

// Upload files

const uploadResults = await this.\_multiFileService.uploadFiles(files, driveId);

// ... rest of method

}

public destroy(): void {

// Clean up cache

this.\_driveService.clearAllCache();

}

**Phase 8: Error Handling & User Feedback**

**Error Handling 8.1: Comprehensive Error Types**

// File: types/Errors.ts

export enum ErrorType {

// File validation errors

FILE_TOO_LARGE = 'FILE_TOO_LARGE',

TOO_MANY_FILES = 'TOO_MANY_FILES',

INVALID_FILE_TYPE = 'INVALID_FILE_TYPE',

// Upload errors

UPLOAD_FAILED = 'UPLOAD_FAILED',

NETWORK_ERROR = 'NETWORK_ERROR',

AUTHENTICATION_FAILED = 'AUTHENTICATION_FAILED',

// Record creation errors

RECORD_CREATION_FAILED = 'RECORD_CREATION_FAILED',

DUPLICATE_RECORD = 'DUPLICATE_RECORD',

VALIDATION_ERROR = 'VALIDATION_ERROR',

// System errors

DRIVE_ID_NOT_FOUND = 'DRIVE_ID_NOT_FOUND',

PARENT_RECORD_NOT_FOUND = 'PARENT_RECORD_NOT_FOUND',

PERMISSION_DENIED = 'PERMISSION_DENIED',

UNKNOWN_ERROR = 'UNKNOWN_ERROR'

}

export class DocumentUploadError extends Error {

constructor(

public type: ErrorType,

public message: string,

public details?: any,

public userMessage?: string

) {

super(message);

this.name = 'DocumentUploadError';

}

/\*\*

\* Get user-friendly error message

\*/

public getUserMessage(): string {

if (this.userMessage) return this.userMessage;

switch (this.type) {

case ErrorType.FILE_TOO_LARGE:

return 'One or more files exceed the maximum size limit (10MB).';

case ErrorType.TOO_MANY_FILES:

return 'Too many files selected. Maximum 10 files allowed.';

case ErrorType.UPLOAD_FAILED:

return 'Failed to upload file to SharePoint. Please try again.';

case ErrorType.NETWORK_ERROR:

return 'Network connection error. Please check your connection and try again.';

case ErrorType.AUTHENTICATION_FAILED:

return 'Authentication failed. Please refresh the page and try again.';

case ErrorType.RECORD_CREATION_FAILED:

return 'Failed to create document record. Please contact your administrator.';

case ErrorType.DRIVE_ID_NOT_FOUND:

return 'This Matter is not configured for document storage. Please contact your administrator.';

case ErrorType.PERMISSION_DENIED:

return 'You do not have permission to upload documents to this Matter.';

default:

return 'An unexpected error occurred. Please try again or contact support.';

}

}

/\*\*

\* Check if error is retryable

\*/

public isRetryable(): boolean {

return \[

ErrorType.UPLOAD_FAILED,

ErrorType.NETWORK_ERROR,

ErrorType.RECORD_CREATION_FAILED

\].includes(this.type);

}

}

**Error Handling 8.2: Graceful Degradation**

// File: UniversalQuickCreatePCF.ts

private async handleUploadAndSaveWithFallback(files: File\[\]): Promise&lt;void&gt; {

try {

// Primary flow

await this.handleUploadAndSave(files);

} catch (error) {

if (error instanceof DocumentUploadError) {

this.\_logger.logError("Upload error", error, {

type: error.type,

retryable: error.isRetryable()

});

// Offer retry for retryable errors

if (error.isRetryable()) {

const confirmed = await this.showConfirmDialog(

error.getUserMessage(),

"Would you like to retry?"

);

if (confirmed) {

await this.handleUploadAndSaveWithFallback(files); // Recursive retry

}

} else {

// Non-retryable - just show error

this.showError(error.getUserMessage());

}

} else {

// Unknown error

this.\_logger.logError("Unexpected error", error);

this.showError("An unexpected error occurred. Please try again.");

}

}

}

private async showConfirmDialog(message: string, confirmText: string): Promise&lt;boolean&gt; {

const confirmStrings = {

text: message,

title: "Error",

confirmButtonLabel: "Retry",

cancelButtonLabel: "Cancel"

};

try {

const result = await Xrm.Navigation.openConfirmDialog(confirmStrings);

return result.confirmed;

} catch {

return false;

}

}

**Error Handling 8.3: Partial Success UI**

// File: components/UploadResultsDialog.tsx

interface UploadResultsDialogProps {

successCount: number;

errorCount: number;

errors: RecordCreationError\[\];

onClose: () => void;

onRetryFailed?: () => void;

}

export const UploadResultsDialog: React.FC&lt;UploadResultsDialogProps&gt; = ({

successCount,

errorCount,

errors,

onClose,

onRetryFailed

}) => {

return (

&lt;div className="upload-results-dialog"&gt;

{/\* Success Summary \*/}

{successCount > 0 && (

&lt;MessageBar messageBarType={MessageBarType.success}&gt;

Successfully uploaded {successCount} document{successCount !== 1 ? 's' : ''}.

&lt;/MessageBar&gt;

)}

{/\* Error Summary \*/}

{errorCount > 0 && (

<>

&lt;MessageBar messageBarType={MessageBarType.error}&gt;

{errorCount} document{errorCount !== 1 ? 's' : ''} failed to upload.

&lt;/MessageBar&gt;

&lt;div className="error-details"&gt;

&lt;h4&gt;Failed Files:&lt;/h4&gt;

&lt;ul&gt;

{errors.map((error, index) => (

&lt;li key={index}&gt;

&lt;strong&gt;{error.fileName}&lt;/strong&gt;: {error.error}

&lt;/li&gt;

))}

&lt;/ul&gt;

&lt;/div&gt;

{onRetryFailed && (

<PrimaryButton

text="Retry Failed Uploads"

onClick={onRetryFailed}

iconProps={{ iconName: 'Refresh' }}

/>

)}

&lt;/&gt;

)}

&lt;DefaultButton text="Close" onClick={onClose} /&gt;

&lt;/div&gt;

);

};

**Phase 9: Deployment & Monitoring**

**Deployment 9.1: Solution Packaging**

**File Structure:**

Solution/

├── ControlManifests/

│ └── UniversalQuickCreatePCF/

│ ├── ControlManifest.Input.xml

│ └── ...

├── CustomPages/

│ └── sprk_multifileupload_page.xml

├── WebResources/

│ ├── sprk_subgrid_commands.js

│ ├── UniversalQuickCreatePCF (compiled PCF bundle)

│ └── ...

├── Entities/

│ ├── sprk_matter/

│ └── sprk_document/

└── Other/

└── Solution.xml

**Build Script:**

// File: package.json

{

"scripts": {

"build": "pcf-scripts build",

"build:prod": "cross-env NODE_ENV=production pcf-scripts build",

"test": "jest",

"lint": "eslint src/\*\*/\*.{ts,tsx}",

"package": "msbuild /p:configuration=Release",

"deploy:dev": "npm run build:prod && pac solution import -p bin/Release/Solution.zip -pc -f -a",

"deploy:uat": "npm run build:prod && pac solution import -p bin/Release/Solution.zip -pc -f -a --environment uat",

"deploy:prod": "npm run build:prod && pac solution import -p bin/Release/Solution.zip -pc -f -a --environment production"

}

}

**Deployment 9.2: Environment Variables**

// File: config/EnvironmentConfig.ts

export class EnvironmentConfig {

private static \_instance: EnvironmentConfig;

private constructor() {}

public static getInstance(): EnvironmentConfig {

if (!EnvironmentConfig.\_instance) {

EnvironmentConfig.\_instance = new EnvironmentConfig();

}

return EnvironmentConfig.\_instance;

}

/\*\*

\* Get configuration based on environment

\*/

public getConfig(): AppConfig {

const clientUrl = Xrm.Utility.getGlobalContext().getClientUrl();

// Determine environment from URL

if (clientUrl.includes('dev') || clientUrl.includes('localhost')) {

return this.getDevConfig();

} else if (clientUrl.includes('uat') || clientUrl.includes('test')) {

return this.getUatConfig();

} else {

return this.getProdConfig();

}

}

private getDevConfig(): AppConfig {

return {

bffApiUrl: '<https://spe-api-dev-67e2xz.azurewebsites.net>',

maxFileSize: 10 \* 1024 \* 1024, // 10MB

maxFiles: 10,

enableDetailedLogging: true,

enablePerformanceMetrics: true,

uploadStrategy: 'parallel' // or 'sequential', 'batch'

};

}

private getUatConfig(): AppConfig {

return {

bffApiUrl: '<https://spe-api-uat-xyz123.azurewebsites.net>',

maxFileSize: 10 \* 1024 \* 1024,

maxFiles: 10,

enableDetailedLogging: true,

enablePerformanceMetrics: true,

uploadStrategy: 'parallel'

};

}

private getProdConfig(): AppConfig {

return {

bffApiUrl: '<https://spe-api-prod-abc456.azurewebsites.net>',

maxFileSize: 10 \* 1024 \* 1024,

maxFiles: 10,

enableDetailedLogging: false, // Reduce noise in production

enablePerformanceMetrics: true,

uploadStrategy: 'batch' // Best performance for production

};

}

}

interface AppConfig {

bffApiUrl: string;

maxFileSize: number;

maxFiles: number;

enableDetailedLogging: boolean;

enablePerformanceMetrics: boolean;

uploadStrategy: 'sequential' | 'parallel' | 'batch';

}

**Monitoring 9.3: Application Insights Integration**

// File: services/TelemetryService.ts

import { ApplicationInsights } from '@microsoft/applicationinsights-web';

export class TelemetryService {

private appInsights: ApplicationInsights;

constructor(instrumentationKey: string) {

this.appInsights = new ApplicationInsights({

config: {

instrumentationKey: instrumentationKey,

enableAutoRouteTracking: true,

enableRequestHeaderTracking: true,

enableResponseHeaderTracking: true

}

});

this.appInsights.loadAppInsights();

}

/\*\*

\* Track document upload event

\*/

public trackDocumentUpload(data: DocumentUploadTelemetry): void {

this.appInsights.trackEvent({

name: 'DocumentUpload',

properties: {

fileCount: data.fileCount,

totalSize: data.totalSize,

uploadDuration: data.uploadDuration,

recordCreationDuration: data.recordCreationDuration,

successCount: data.successCount,

errorCount: data.errorCount,

parentEntityName: data.parentEntityName,

uploadStrategy: data.uploadStrategy

}

});

}

/\*\*

\* Track performance metric

\*/

public trackMetric(name: string, value: number, properties?: any): void {

this.appInsights.trackMetric({

name,

average: value

}, properties);

}

/\*\*

\* Track error

\*/

public trackError(error: Error, properties?: any): void {

this.appInsights.trackException({

exception: error,

properties

});

}

}

interface DocumentUploadTelemetry {

fileCount: number;

totalSize: number;

uploadDuration: number; // ms

recordCreationDuration: number; // ms

successCount: number;

errorCount: number;

parentEntityName: string;

uploadStrategy: string;

}

**Use in PCF:**

// File: UniversalQuickCreatePCF.ts

private async handleUploadAndSave(files: File\[\]): Promise&lt;void&gt; {

const startTime = Date.now();

try {

// Phase 1: Upload

const uploadStartTime = Date.now();

const uploadResults = await this.\_multiFileService.uploadFiles(files, driveId);

const uploadDuration = Date.now() - uploadStartTime;

// Phase 2: Create records

const recordStartTime = Date.now();

const recordResults = await this.\_documentRecordService.createMultipleDocuments({

uploadResults,

parentRecordId: this.\_parentRecordId

});

const recordDuration = Date.now() - recordStartTime;

// Track telemetry

this.\_telemetryService.trackDocumentUpload({

fileCount: files.length,

totalSize: files.reduce((sum, f) => sum + f.size, 0),

uploadDuration,

recordCreationDuration: recordDuration,

successCount: recordResults.successCount,

errorCount: recordResults.errorCount,

parentEntityName: this.\_parentEntityName,

uploadStrategy: 'parallel'

});

// Track individual metrics

this.\_telemetryService.trackMetric('Upload_Duration_Per_File', uploadDuration / files.length);

this.\_telemetryService.trackMetric('Record_Creation_Duration_Per_Record', recordDuration / recordResults.successCount);

} catch (error) {

this.\_telemetryService.trackError(error, {

operation: 'handleUploadAndSave',

fileCount: files.length

});

throw error;

}

}

**Final Checklist for Claude Code**

**Pre-Implementation Review**

- \[ \] **Understand root cause**: Quick Create context limitations for multi-record creation
- \[ \] **Understand solution**: Custom page with Xrm.WebApi for unlimited record creation
- \[ \] **Review existing code**: Identify what to keep, modify, and create
- \[ \] **Set up development environment**: Node.js, PCF CLI, Power Platform CLI

**Implementation Phases**

**Phase 1: Foundation**

- \[ \] Update ControlManifest.Input.xml with parent context parameters
- \[ \] Create custom page definition
- \[ \] Create command button web resource (sprk_subgrid_commands.js)
- \[ \] Configure subgrid command bar button

**Phase 2: Services**

- \[ \] Create DocumentRecordService.ts with Xrm.WebApi integration
- \[ \] Refactor MultiFileUploadService.ts to remove record creation
- \[ \] Simplify or deprecate DataverseRecordService.ts
- \[ \] Add DriveService.ts for caching (optional optimization)

**Phase 3: PCF Component**

- \[ \] Update UniversalQuickCreatePCF.init() to accept parent context
- \[ \] Remove Quick Create dependencies (getFormData, preventDefault, etc.)
- \[ \] Implement handleUploadAndSave() with new flow
- \[ \] Add closeDialog() method
- \[ \] Implement error handling and retry logic

**Phase 4: UI**

- \[ \] Update React component for dialog styling
- \[ \] Add progress indicators
- \[ \] Add file validation UI
- \[ \] Add results summary dialog
- \[ \] Update CSS for Power Platform look and feel

**Phase 5: Testing**

- \[ \] Unit test DocumentRecordService
- \[ \] Integration test full upload flow
- \[ \] Test edge cases (max files, oversized files, network failures)
- \[ \] Performance test (20+ files)
- \[ \] Cross-browser testing

**Phase 6: Deployment**

- \[ \] Build solution package
- \[ \] Deploy to DEV environment
- \[ \] Deploy to UAT environment
- \[ \] User acceptance testing
- \[ \] Deploy to PROD environment
- \[ \] Post-deployment monitoring

**Quality Gates**

Each phase must meet these standards before proceeding:

- ✅ TypeScript compiles without errors
- ✅ ESLint passes (no warnings)
- ✅ Code follows Alex Butenko patterns (see "Code Patterns & Standards" section)
- ✅ Error handling implemented
- ✅ Logging integrated
- ✅ Comments and documentation added
- ✅ Manual testing passed

**Summary**

This document provides complete specifications for rebuilding the UniversalQuickCreatePCF control to use custom page architecture instead of Quick Create forms. The key takeaways:

- **Root Problem**: Quick Create forms can't handle multiple context.webAPI.createRecord() calls
- **Solution**: Custom page + Xrm.WebApi for unlimited record creation
- **Keep**: SharePoint Embedded upload logic (works correctly)
- **Replace**: Record creation service to use global API instead of form context
- **Add**: Command button, custom page, dialog management
- **Code Style**: Alex Butenko standards - clean, typed, production-ready

**This architecture will scale to 100+ files and is the Power Platform-recommended approach for multi-record scenarios.**