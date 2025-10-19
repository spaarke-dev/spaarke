# Quick Create PCF Control - Feasibility Analysis

**Version:** 1.0.0
**Date:** 2025-10-07
**Sprint:** 7B - Universal Quick Create
**Status:** Analysis

---

## Executive Summary

**Question:** Can we build the Universal Quick Create control as a proper Quick Create PCF component that works with Power Apps Quick Create forms?

**Short Answer:** **Yes, BUT with significant architectural changes and important limitations.**

**Recommendation:** See [Decision Matrix](#decision-matrix) section for detailed comparison.

---

## Quick Create Form Benefits (Out-of-the-Box)

### Why Quick Create Forms Are Valuable:

1. **✅ Form Builder Integration**
   - Drag-and-drop field configuration
   - Visual form designer
   - Easy for admins to modify
   - No code changes needed

2. **✅ "+ New" Button in Navigation**
   - Automatically enabled when Quick Create is active
   - Appears in top navigation bar
   - Consistent UX across all entities
   - Users expect this behavior

3. **✅ Subgrid Integration**
   - "+ New Record" button auto-appears in subgrids
   - No custom ribbon buttons needed
   - Works everywhere automatically

4. **✅ Mobile Support**
   - Quick Create works on mobile apps
   - Consistent mobile experience
   - No additional mobile configuration

5. **✅ Quick Create Menu**
   - Appears in global "+ New" menu
   - Shows recent entities
   - Fast record creation workflow

6. **✅ Business Process Flows**
   - Can integrate with BPF
   - Guided data entry
   - Stage-based forms

---

## Current Architecture: Why It Doesn't Work with Quick Create

### Current Control Configuration

**File:** `ControlManifest.Input.xml`

```xml
<control namespace="Spaarke.Controls"
         constructor="UniversalQuickCreate"
         version="1.0.0"
         control-type="standard">  <!-- ⚠️ Dataset control -->

    <!-- Dataset binding (for grids, not forms) -->
    <data-set name="dataset" display-name-key="Dataset_Display_Key">
      <property-set name="field" ... />
    </data-set>

    <!-- Configuration parameters -->
    <property name="defaultValueMappings" ... />
    <property name="enableFileUpload" ... />
    <property name="sdapApiBaseUrl" ... />
</control>
```

### Issues:

1. **Dataset Binding** - Quick Create forms don't use datasets, they use individual fields
2. **No Field Binding** - Control doesn't bind to a specific form field
3. **Full Form Control** - Tries to control entire form, not a field
4. **Form Context** - Relies on dataset context, not form field context

---

## What Would Need to Change: Architectural Options

### Option 1: Field-Level PCF Control (Feasible) ✅

**Approach:** Make the control bind to a **single field** on the Quick Create form, but render the entire file upload experience.

#### Manifest Changes:

```xml
<control namespace="Spaarke.Controls"
         constructor="UniversalQuickCreate"
         version="1.0.0"
         control-type="standard">  <!-- Keep as standard -->

    <!-- REMOVE dataset binding -->
    <!-- ADD field binding instead -->
    <property name="boundField"
              display-name-key="Bound_Field"
              description-key="Field to bind control to"
              of-type="SingleLine.Text"
              usage="bound"
              required="true" />

    <!-- Keep configuration parameters -->
    <property name="defaultValueMappings" ... />
    <property name="enableFileUpload" ... />
    <property name="sdapApiBaseUrl" ... />
</control>
```

#### How It Would Work:

1. **Admin adds control to a specific field** (e.g., `sprk_documenttitle`)
2. **Control renders** in place of that field
3. **Control displays:**
   - File upload picker (if enabled)
   - Document Title field (the bound field)
   - Description field (additional field)
   - Other configured fields
4. **When user saves:**
   - Control saves file to SharePoint
   - Control updates bound field (`sprk_documenttitle`) with file name
   - Control updates other fields via form context
   - Quick Create form saves record

#### Code Changes Required:

```typescript
// UniversalQuickCreatePCF.ts

export class UniversalQuickCreatePCF implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private _value: string; // Bound field value
    private _notifyOutputChanged: () => void;

    public init(context: ComponentFramework.Context<IInputs>): void {
        // Get bound field value
        this._value = context.parameters.boundField.raw || "";
        this._notifyOutputChanged = context.parameters.boundField.notifyOutputChanged;

        // Get form context to access other fields
        const formContext = (context as any).mode?.contextInfo;

        // Render form fields (including bound field)
        this.renderFormFields();
    }

    public updateView(context: ComponentFramework.Context<IInputs>): IOutputs {
        // Update bound field value when user types
        this._value = context.parameters.boundField.raw || "";
        this.renderFormFields();

        return {
            boundField: this._value
        };
    }

    public getOutputs(): IOutputs {
        return {
            boundField: this._value
        };
    }

    private async handleSave(file: File, formData: Record<string, unknown>): Promise<void> {
        // 1. Upload file to SharePoint
        const speMetadata = await uploadFile(file);

        // 2. Update bound field with file name
        this._value = file.name;
        this._notifyOutputChanged();

        // 3. Update other form fields via form context
        // NOTE: This is the tricky part - accessing other fields from PCF
        const formContext = (this.context as any).ui?.formContext;
        if (formContext) {
            formContext.getAttribute("sprk_description").setValue(formData.description);
            formContext.getAttribute("sprk_containerid").setValue(formData.containerid);
            // ... etc
        }

        // 4. Form saves automatically when user clicks "Save and Close"
    }
}
```

#### Limitations:

1. **❌ Cannot fully control form fields** - PCF controls have limited access to other form fields
2. **❌ Bound field requirement** - Must bind to a specific field (can't be standalone)
3. **❌ Form API access is limited** - Can't reliably access all form fields from PCF
4. **❌ Save timing issues** - PCF saves before form saves, tricky to coordinate
5. **⚠️ Admin confusion** - Control appears as field replacement, not obvious

---

### Option 2: Virtual Field Control (Complex) ⚠️

**Approach:** Create a "virtual" field that doesn't map to a real Dataverse column, just for hosting the control.

#### How It Would Work:

1. **Create a calculated field** (e.g., `sprk_virtualquickcreate`) that doesn't store data
2. **Bind control to this field**
3. **Control renders full form experience**
4. **On save, update real fields** via form context

#### Issues:

- ❌ Calculated fields have limitations
- ❌ Still can't reliably access other form fields
- ❌ Hacky workaround
- ❌ Hard to maintain

---

### Option 3: Replace Entire Quick Create Form (Not Possible) ❌

**Approach:** Replace the entire Quick Create form with PCF control.

#### Why It Doesn't Work:

- ❌ Power Apps doesn't support full-form PCF controls for Quick Create
- ❌ Only field-level controls are supported
- ❌ Would need to be a custom page (different technology)

---

## Critical Limitations of Quick Create PCF Approach

### Technical Limitations:

1. **Form Context Access is Restricted**
   - PCF controls have limited access to form context on Quick Create
   - `formContext.getAttribute()` may not work reliably
   - Parent entity context is harder to access
   - Different behavior than Main Forms

2. **Field Binding Required**
   - Control MUST bind to a specific field
   - Cannot be a "standalone" control on the form
   - Bound field must be on the entity schema
   - Cannot create "virtual" controls easily

3. **Save Coordination Issues**
   - PCF control `getOutputs()` called before form save
   - File upload is async, may not complete before save
   - Hard to prevent save until file upload completes
   - Race conditions between PCF and form save

4. **Parent Context Access**
   - Getting parent entity info (Matter ID, Container ID) is harder
   - Quick Create doesn't always provide regardingObjectId
   - May need to parse URL or use custom logic

5. **File Upload Timing**
   - User clicks "Save and Close"
   - Form tries to save immediately
   - File upload may still be in progress
   - Need to block save, upload file, then allow save
   - This is complex and fragile

### User Experience Limitations:

1. **Confusing Configuration**
   - Admin must bind control to a specific field
   - Not obvious which field to choose
   - Looks like field replacement, not form enhancement

2. **Inconsistent Behavior**
   - Control renders differently than expected
   - Bound field appears twice (once in control, once in form)
   - Admin must hide bound field, confusing

3. **Error Handling**
   - If file upload fails, form may already be saving
   - Can't reliably cancel save
   - User may create record without file

### Functional Limitations:

1. **Cannot Dynamically Render Fields**
   - Must show all fields via control
   - Cannot use form's field rendering
   - Loses form builder benefits

2. **Cannot Leverage Form Features**
   - Business rules don't apply to PCF fields
   - Field validations bypassed
   - Form-level scripts don't fire

3. **Mobile Experience**
   - PCF controls may not render well on mobile Quick Create
   - File upload UX challenging on mobile
   - May need separate mobile configuration

---

## What We Would Lose

### By Using PCF Control in Quick Create (vs. Regular Form Fields):

1. **❌ Form Builder Drag-and-Drop** - PCF control renders its own fields, ignores form layout
2. **❌ Business Rules** - Don't apply to PCF-rendered fields
3. **❌ Field Validations** - Bypassed by PCF (must implement custom validation)
4. **❌ Form Scripts** - OnChange, OnLoad events don't fire for PCF fields
5. **❌ Field Security** - PCF may bypass field-level security
6. **❌ Dynamics Forms Features** - Conditional visibility, lookups, etc.

### By Using Main Form Dialog (vs. Quick Create):

1. **❌ "+ New" Navigation Button** - Must add custom button
2. **❌ Auto Subgrid Integration** - Must customize ribbon
3. **❌ Quick Create Menu** - Won't appear in global menu
4. **⚠️ Mobile Experience** - Needs testing, may work fine

---

## Decision Matrix

### Quick Create PCF Control Approach

| Factor | Rating | Notes |
|--------|--------|-------|
| **Form Builder Integration** | ❌ Lost | Control renders own fields, ignores form layout |
| **Easy Admin Config** | ⚠️ Moderate | Must bind to field, hide field, configure parameters |
| **"+ New" Button Works** | ✅ Yes | Out-of-box Quick Create features work |
| **File Upload** | ⚠️ Complex | Timing issues, async coordination needed |
| **Parent Field Inheritance** | ⚠️ Limited | Harder to access parent context |
| **Field Validations** | ❌ Lost | Must reimplement in PCF |
| **Business Rules** | ❌ Lost | Don't apply to PCF fields |
| **Mobile Support** | ⚠️ Unknown | Needs testing |
| **Development Effort** | 🔴 High | Significant rework needed (2-3 weeks) |
| **Maintenance** | 🔴 High | Complex, fragile, many edge cases |
| **User Experience** | ⚠️ Moderate | Works but limitations visible |

**Overall: Feasible but with significant trade-offs** ⚠️

---

### Main Form Dialog Approach (Current Recommendation)

| Factor | Rating | Notes |
|--------|--------|-------|
| **Form Builder Integration** | ✅ Full | Regular form, all features work |
| **Easy Admin Config** | ✅ Yes | Standard form designer |
| **"+ New" Button Works** | ❌ No | Must add custom button |
| **File Upload** | ✅ Excellent | Full control, proper async handling |
| **Parent Field Inheritance** | ✅ Full | JavaScript has full access |
| **Field Validations** | ✅ Full | All form features work |
| **Business Rules** | ✅ Full | Apply normally |
| **Mobile Support** | ✅ Good | Dialogs work on mobile |
| **Development Effort** | 🟢 Low | 1-2 hours (JavaScript + ribbon) |
| **Maintenance** | 🟢 Low | Simple, reliable |
| **User Experience** | ✅ Excellent | More screen space for file upload |

**Overall: Best balance of features and effort** ✅

---

### Hybrid Approach (Best of Both Worlds?)

**Idea:** Support BOTH Quick Create (simple) AND Main Form Dialog (full features)

#### Implementation:

1. **Quick Create Form (Simple Mode)**
   - Use out-of-box Quick Create with NO PCF control
   - Fields: Document Title, Description, Matter (lookup)
   - NO file upload
   - For quick text-only document creation

2. **Main Form Dialog (Full Mode)**
   - Custom "New Document with File" button
   - Opens main form as dialog
   - PCF control for file upload
   - Full field inheritance

3. **User Choice:**
   - Quick text document → Use "+ New Document" (Quick Create)
   - Document with file → Use "New Document with File" (Dialog)

#### Configuration:

```
Matter Form:
├─ Documents Subgrid
│  ├─ "+ New Document" (Quick Create, no file)
│  └─ "📎 New Document with File" (Custom button, dialog with PCF)
│
└─ Top Navigation:
   └─ "+ New" menu includes Document (Quick Create, no file)
```

#### Benefits:

- ✅ Quick Create still works (simple docs)
- ✅ File upload works (full dialog)
- ✅ User chooses based on need
- ✅ Both workflows available

#### Trade-offs:

- ⚠️ Two ways to create documents (may confuse users)
- ⚠️ More buttons to maintain
- ✅ Flexibility

---

## Recommendations

### Recommended: Main Form Dialog (Solution 1)

**Use this if:**
- ✅ You want reliable file upload
- ✅ You want full field inheritance
- ✅ You want easy maintenance
- ✅ You can add a custom button

**Implementation:** 1-2 hours
**Maintenance:** Low
**Risk:** Low

---

### Alternative: Hybrid Approach (Solution 2)

**Use this if:**
- ✅ You want to keep Quick Create for simple docs
- ✅ You want full features for docs with files
- ✅ Users create both types of documents
- ✅ You can educate users on two workflows

**Implementation:** 2-3 hours
**Maintenance:** Moderate
**Risk:** Low-Moderate

---

### Not Recommended: Quick Create PCF Control (Solution 3)

**Only use this if:**
- You absolutely MUST have "+ New" button work for file upload
- You're willing to accept significant limitations
- You have 2-3 weeks for development
- You can accept complex maintenance

**Implementation:** 2-3 weeks
**Maintenance:** High
**Risk:** High

---

## Detailed Implementation: Quick Create PCF Control

### If You Choose to Proceed with Quick Create PCF

Here's what you would need to do:

#### Step 1: Update Manifest

**File:** `ControlManifest.Input.xml`

```xml
<?xml version="1.0" encoding="utf-8" ?>
<manifest>
  <control namespace="Spaarke.Controls"
           constructor="UniversalQuickCreate"
           version="1.0.0"
           display-name-key="Universal_Quick_Create_Display_Key"
           description-key="Universal_Quick_Create_Desc_Key"
           control-type="standard">

    <!-- CHANGE: Remove data-set, add field binding -->
    <property name="boundField"
              display-name-key="Document_Title_Field"
              description-key="Field to bind control to (e.g., sprk_documenttitle)"
              of-type="SingleLine.Text"
              usage="bound"
              required="true" />

    <!-- Keep existing configuration parameters -->
    <property name="defaultValueMappings"
              display-name-key="Default_Value_Mappings"
              description-key="JSON mapping of parent fields to child default values"
              of-type="SingleLine.Text"
              usage="input"
              required="false" />

    <property name="enableFileUpload"
              display-name-key="Enable_File_Upload"
              description-key="Enable file upload field"
              of-type="TwoOptions"
              usage="input"
              required="false"
              default-value="true" />

    <property name="sdapApiBaseUrl"
              display-name-key="SDAP_API_Base_URL"
              description-key="Base URL for SDAP BFF API"
              of-type="SingleLine.Text"
              usage="input"
              required="false"
              default-value="https://localhost:7299/api" />

    <resources>
      <code path="index.ts" order="1"/>
      <css path="css/UniversalQuickCreate.css" order="1" />
    </resources>

    <feature-usage>
      <uses-feature name="WebAPI" required="true" />
      <uses-feature name="Utility" required="true" />
    </feature-usage>
  </control>
</manifest>
```

---

#### Step 2: Update PCF TypeScript Code

**File:** `UniversalQuickCreatePCF.ts`

```typescript
import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import * as ReactDOM from "react-dom/client";

export class UniversalQuickCreatePCF implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private container: HTMLDivElement;
    private reactRoot: ReactDOM.Root | null = null;
    private context: ComponentFramework.Context<IInputs>;

    // Bound field value
    private _boundFieldValue: string;
    private _notifyOutputChanged: () => void;

    // Form context (to access other fields)
    private _formContext: any;

    // Configuration
    private defaultValueMappings: Record<string, Record<string, string>> = {};
    private enableFileUpload = true;
    private sdapApiBaseUrl = '';

    public init(context: ComponentFramework.Context<IInputs>): void {
        this.context = context;

        // Get bound field value and notify callback
        this._boundFieldValue = context.parameters.boundField.raw || "";
        this._notifyOutputChanged = context.parameters.boundField.notifyOutputChanged || (() => {});

        // Try to get form context (may not work reliably in Quick Create!)
        try {
            // Method 1: Try mode.contextInfo
            this._formContext = (context as any).mode?.contextInfo;

            // Method 2: Try Xrm.Page (deprecated but may work)
            if (!this._formContext && typeof window !== 'undefined' && (window as any).Xrm) {
                this._formContext = (window as any).Xrm.Page;
            }

            // Method 3: Try parent window (if in iframe)
            if (!this._formContext && typeof window !== 'undefined' && window.parent) {
                this._formContext = (window.parent as any).Xrm?.Page;
            }
        } catch (error) {
            console.warn("Could not access form context", error);
        }

        // Load configuration
        this.loadConfiguration(context);

        // Create container
        this.container = document.createElement("div");
        this.container.className = "universal-quick-create-container";

        // Create React root
        this.reactRoot = ReactDOM.createRoot(this.container);

        // Render component
        this.renderReactComponent();
    }

    public updateView(context: ComponentFramework.Context<IInputs>): IOutputs {
        // Update bound field value when it changes
        this._boundFieldValue = context.parameters.boundField.raw || "";

        // Re-render component
        this.renderReactComponent();

        return this.getOutputs();
    }

    public getOutputs(): IOutputs {
        return {
            boundField: this._boundFieldValue
        };
    }

    private renderReactComponent(): void {
        if (!this.reactRoot) return;

        const props = {
            boundFieldValue: this._boundFieldValue,
            onBoundFieldChange: (value: string) => {
                this._boundFieldValue = value;
                this._notifyOutputChanged();
            },
            enableFileUpload: this.enableFileUpload,
            sdapApiBaseUrl: this.sdapApiBaseUrl,
            onSave: this.handleSave.bind(this),
            formContext: this._formContext
        };

        this.reactRoot.render(React.createElement(QuickCreateFormField, props));
    }

    private async handleSave(file: File | undefined, formData: Record<string, unknown>): Promise<void> {
        try {
            // 1. Upload file to SharePoint (if provided)
            if (file && this.enableFileUpload) {
                const speMetadata = await this.uploadFile(file);

                // Update bound field with file name
                this._boundFieldValue = file.name;
                this._notifyOutputChanged();
            }

            // 2. Update other form fields via form context
            if (this._formContext) {
                // Try to set field values
                // NOTE: This may not work reliably in Quick Create!
                try {
                    if (this._formContext.getAttribute) {
                        // Method 1: Xrm.Page style
                        const descAttr = this._formContext.getAttribute("sprk_description");
                        if (descAttr) {
                            descAttr.setValue(formData.description);
                        }
                    } else if (this._formContext.data?.entity) {
                        // Method 2: Form context style
                        this._formContext.data.entity.attributes.get("sprk_description")?.setValue(formData.description);
                    }
                } catch (error) {
                    console.error("Failed to set form field values", error);
                    // Fallback: Store in sessionStorage, retrieve in OnLoad?
                    sessionStorage.setItem("pendingDocumentData", JSON.stringify(formData));
                }
            }

            // 3. Form will save automatically when user clicks "Save and Close"

        } catch (error) {
            console.error("Save failed", error);
            throw error;
        }
    }

    private async uploadFile(file: File): Promise<any> {
        // File upload implementation (existing code)
        // ...
    }

    private loadConfiguration(context: ComponentFramework.Context<IInputs>): void {
        // Load configuration (existing code)
        // ...
    }

    public destroy(): void {
        if (this.reactRoot) {
            this.reactRoot.unmount();
        }
    }
}
```

---

#### Step 3: Update React Component

**File:** `QuickCreateFormField.tsx`

```typescript
import * as React from 'react';
import { Field, Input, Textarea, Button } from '@fluentui/react-components';

interface QuickCreateFormFieldProps {
    boundFieldValue: string;
    onBoundFieldChange: (value: string) => void;
    enableFileUpload: boolean;
    sdapApiBaseUrl: string;
    onSave: (file: File | undefined, formData: Record<string, unknown>) => Promise<void>;
    formContext: any;
}

export const QuickCreateFormField: React.FC<QuickCreateFormFieldProps> = (props) => {
    const [documentTitle, setDocumentTitle] = React.useState(props.boundFieldValue);
    const [description, setDescription] = React.useState('');
    const [selectedFile, setSelectedFile] = React.useState<File | undefined>();
    const [isSaving, setIsSaving] = React.useState(false);

    // Update bound field when title changes
    React.useEffect(() => {
        props.onBoundFieldChange(documentTitle);
    }, [documentTitle]);

    const handleFileSelect = (event: React.ChangeEvent<HTMLInputElement>) => {
        const file = event.target.files?.[0];
        setSelectedFile(file);

        // Auto-populate title with file name if empty
        if (file && !documentTitle) {
            setDocumentTitle(file.name);
        }
    };

    const handleSaveClick = async () => {
        setIsSaving(true);

        try {
            await props.onSave(selectedFile, {
                sprk_documenttitle: documentTitle,
                sprk_description: description
            });

            // Don't close form - let Quick Create handle that
        } catch (error) {
            alert(`Save failed: ${error.message}`);
        } finally {
            setIsSaving(false);
        }
    };

    return (
        <div style={{ padding: '10px' }}>
            {props.enableFileUpload && (
                <Field label="Select File" required>
                    <input
                        type="file"
                        onChange={handleFileSelect}
                        disabled={isSaving}
                    />
                    {selectedFile && (
                        <div style={{ marginTop: '5px', fontSize: '12px', color: '#666' }}>
                            Selected: {selectedFile.name} ({(selectedFile.size / 1024).toFixed(1)} KB)
                        </div>
                    )}
                </Field>
            )}

            <Field label="Document Title" required>
                <Input
                    value={documentTitle}
                    onChange={(e, data) => setDocumentTitle(data.value)}
                    disabled={isSaving}
                />
            </Field>

            <Field label="Description">
                <Textarea
                    value={description}
                    onChange={(e, data) => setDescription(data.value)}
                    rows={3}
                    disabled={isSaving}
                />
            </Field>

            {/* NOTE: This button may not be needed - Quick Create has its own Save button */}
            {/* Keeping it for file upload trigger */}
            {props.enableFileUpload && selectedFile && (
                <Button
                    appearance="primary"
                    onClick={handleSaveClick}
                    disabled={isSaving || !documentTitle}
                    style={{ marginTop: '10px' }}
                >
                    {isSaving ? 'Uploading...' : 'Upload File'}
                </Button>
            )}
        </div>
    );
};
```

---

#### Step 4: Quick Create Form Configuration

1. **Create Quick Create form for Document entity**
2. **Add fields to form:**
   - sprk_documenttitle (this is where PCF binds)
   - sprk_description (optional, shown by PCF)
   - sprk_matter (lookup, hidden - set by PCF)
   - sprk_containerid (text, hidden - set by PCF)

3. **Configure PCF control on sprk_documenttitle field:**
   - Click field properties
   - Go to "Controls" tab
   - Add control "Universal Quick Create"
   - Set as default for Web
   - Configure parameters:
     ```json
     defaultValueMappings: {"sprk_matter":{"sprk_containerid":"sprk_containerid"}}
     enableFileUpload: true
     sdapApiBaseUrl: https://your-api.azure.com/api
     ```

4. **Hide sprk_documenttitle field label** (PCF renders its own)

5. **Publish form**

---

#### Step 5: Testing Checklist

- [ ] Quick Create opens from "+ New Document"
- [ ] File picker appears
- [ ] Can select file
- [ ] Document Title auto-populates with file name
- [ ] Can edit Document Title
- [ ] Can enter Description
- [ ] File uploads when "Upload File" clicked
- [ ] Form saves when "Save and Close" clicked
- [ ] Document record created with file
- [ ] Matter lookup populated
- [ ] Container ID populated

---

### Expected Issues and Workarounds

#### Issue 1: Form Context Not Available

**Problem:** `formContext.getAttribute()` doesn't work in Quick Create PCF

**Workaround:**
```typescript
// Store data in sessionStorage, retrieve on record create
sessionStorage.setItem('pendingDocData_' + Date.now(), JSON.stringify(formData));

// On entity OnLoad event (plugin or JavaScript)
// Retrieve from sessionStorage and populate fields
```

#### Issue 2: Save Timing

**Problem:** File upload async, form saves before upload completes

**Workaround:**
```typescript
// Block Quick Create save until upload completes
context.mode.setControlState({
    disabled: true  // Disable save button during upload
});

// After upload
context.mode.setControlState({
    disabled: false
});
```

**NOTE:** This API may not be available in all contexts!

#### Issue 3: Parent Context Missing

**Problem:** Can't get parent Matter ID in Quick Create

**Workaround:**
```typescript
// Parse URL for parent info
const urlParams = new URLSearchParams(window.location.search);
const parentId = urlParams.get('_CreateFromId');
const parentType = urlParams.get('_CreateFromType');

// Or use sessionStorage to pass from subgrid
```

---

## Conclusion

### Can You Build Quick Create PCF Control?

**Yes**, but:

1. **Requires significant rework** (2-3 weeks development)
2. **Many limitations and workarounds** needed
3. **Fragile implementation** - easy to break
4. **High maintenance burden**
5. **May not work reliably** across all scenarios

### Should You Build It?

**Probably not**, unless:

- You absolutely MUST have "+ New" button work
- You have time and budget for 2-3 weeks dev + ongoing maintenance
- You can accept the limitations
- You're okay with potential issues

### Recommended Path Forward

**Use Main Form Dialog approach:**

1. **Quick to implement** (1-2 hours)
2. **Reliable and maintainable**
3. **Full features available**
4. **Only downside:** Custom button instead of "+ New"

**OR**

**Use Hybrid approach:**

1. Keep Quick Create for simple docs (no file)
2. Add custom button for docs with files
3. Best of both worlds

---

## Next Steps

**Please decide:**

1. ✅ **Main Form Dialog** - Fast, reliable, recommended
2. ⚠️ **Hybrid Approach** - Flexible, moderate effort
3. 🔴 **Quick Create PCF** - High effort, many limitations

Let me know which path you'd like to take, and I'll help implement it!

---

**Date:** 2025-10-07
**Sprint:** 7B - Universal Quick Create
**Status:** Analysis Complete - Awaiting Decision
