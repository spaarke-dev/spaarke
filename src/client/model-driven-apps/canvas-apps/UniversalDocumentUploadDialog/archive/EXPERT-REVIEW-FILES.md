# Expert Review - Custom Page Parameter Passing Issue

## Problem Summary

We're migrating Universal Document Upload from Quick Create form to Custom Page dialog. The Custom Page opens correctly, but parameters aren't flowing from the web resource → Custom Page → PCF control.

**Current Status:**
- ✅ Web resource passes `data: {...}` in navigateTo()
- ✅ Custom Page opens as right-side pane (no errors in console about invalid page inputs)
- ❌ PCF control shows blank - parameters aren't reaching it
- ❌ Custom Page formulas throwing "The '.' operator cannot be used on Text values" when accessing `Param("data").fieldName`

## Architecture Flow

```
Ribbon Button
  → Web Resource (sprk_subgrid_commands.js)
    → Xrm.Navigation.navigateTo() with data: {...}
      → Custom Page (sprk_documentuploaddialog_e52db)
        → Screen.OnVisible: Set variables from Param("data")
          → PCF Control (UniversalDocumentUpload)
            → Receives via context.parameters
```

## Key Files for Review

### 1. Web Resource (Parameter Source)
**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreateSolution/src/WebResources/sprk_subgrid_commands.js`
**Lines:** 300-330 (openDocumentUploadDialog function)

**Current Code:**
```javascript
const pageInput = {
    pageType: 'custom',
    name: 'sprk_documentuploaddialog_e52db',
    data: {
        parentEntityName: params.parentEntityName,
        parentRecordId: params.parentRecordId,
        containerId: params.containerId,
        parentDisplayName: params.parentDisplayName
    }
};

const navigationOptions = {
    target: 2,      // Dialog
    position: 2,    // Right side pane
    width: { value: 640, unit: 'px' }
};

Xrm.Navigation.navigateTo(pageInput, navigationOptions);
```

### 2. PCF Manifest (Property Definitions)
**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/ControlManifest.Input.xml`
**Version:** 3.0.2

**Current Properties:**
```xml
<property name="parentEntityName"
          display-name-key="Parent Entity Name"
          of-type="SingleLine.Text"
          usage="input"
          required="true" />

<property name="parentRecordId"
          display-name-key="Parent Record ID"
          of-type="SingleLine.Text"
          usage="input"
          required="true" />

<property name="containerId"
          display-name-key="Container ID"
          of-type="SingleLine.Text"
          usage="input"
          required="true" />

<property name="parentDisplayName"
          display-name-key="Parent Display Name"
          of-type="SingleLine.Text"
          usage="input"
          required="false" />
```

**Note:** All properties use `usage="input"` (not `usage="bound"`)

### 3. PCF Control (Parameter Consumer)
**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts`
**Lines:** 400-434 (updateView method)

**Current Code:**
```typescript
public updateView(context: ComponentFramework.Context<IInputs>): void {
    this.context = context;

    // Extract parameter values (may be empty on first call while params hydrate)
    const parentEntityName = context.parameters.parentEntityName?.raw ?? "";
    const parentRecordId = context.parameters.parentRecordId?.raw ?? "";
    const containerId = context.parameters.containerId?.raw ?? "";
    const parentDisplayName = context.parameters.parentDisplayName?.raw ?? "";

    // Only initialize once when all required params are present
    if (!this._initialized) {
        if (parentEntityName && parentRecordId && containerId) {
            logInfo('UniversalDocumentUpload', 'Parameters hydrated - initializing async', {
                parentEntityName,
                parentRecordId,
                containerId
            });

            this._initialized = true;

            // Reinitialize with actual parameters
            this.initializeAsync(context);
        } else {
            // Params not ready yet - wait for next updateView call
            logInfo('UniversalDocumentUpload', 'Waiting for parameters to hydrate', {
                hasEntityName: !!parentEntityName,
                hasRecordId: !!parentRecordId,
                hasContainerId: !!containerId
            });
            return;
        }
    }
}
```

### 4. Custom Page Formulas (ISSUE HERE)

**Current App.OnStart:**
```powerfx
Set(_init, false);
Set(varParentEntityName, Blank());
Set(varParentRecordId, Blank());
Set(varContainerId, Blank());
Set(varParentDisplayName, Blank())
```

**Current Screen.OnVisible (THROWS ERROR):**
```powerfx
If(
    Not(_init),
    Set(_init, true);
    Set(_params, Param("data"));
    Set(varParentEntityName, _params.parentEntityName);
    Set(varParentRecordId, _params.parentRecordId);
    Set(varContainerId, _params.containerId);
    Set(varParentDisplayName, _params.parentDisplayName)
)
```

**Error:** "The '.' operator cannot be used on Text values"

**Attempted Alternatives (also throw errors):**
1. Direct access: `Set(varParentEntityName, Param("data").parentEntityName)` → Same error
2. With Text(): `Set(varParentEntityName, Text(Param("data").parentEntityName))` → Same error
3. With ParseJSON(): `Set(varInput, ParseJSON(Param("data")))` → Different errors

**PCF Control Binding in Custom Page:**
- Container control with UniversalDocumentUpload PCF
- Properties bound to variables:
  - `parentEntityName` → `varParentEntityName`
  - `parentRecordId` → `varParentRecordId`
  - `containerId` → `varContainerId`
  - `parentDisplayName` → `varParentDisplayName`
  - `sdapApiBaseUrl` → `"https://spe-api-dev-67e2xz.azurewebsites.net/api"`
- Visible property: `And(Not(IsBlank(varParentEntityName)), Not(IsBlank(varParentRecordId)), Not(IsBlank(varContainerId)))`

## Questions for Expert

1. **What is the correct Power FX syntax** to access fields from `Param("data")` when it's passed via `navigateTo()`?

2. **Is `Param("data")` returning a record, text, or untyped object?** The error suggests Power Apps thinks it's text, but we're passing a JavaScript object under `data`.

3. **Should we define input parameters** in the Custom Page settings UI before accessing them with `Param()`?

4. **Are there any Power Apps version requirements** for the `data` property in `navigateTo()` to work with Custom Pages?

5. **Is there a better way to debug** what `Param("data")` is actually returning? Can we use Notify() or something to inspect its type?

## What We've Tried

1. ✅ PCF manifest: Changed from `usage="bound"` → `usage="input"`
2. ✅ Web resource: Wrapped parameters under `data: {...}`
3. ✅ Custom Page added to Model-Driven App
4. ✅ Version bumped to 3.0.2 to verify fresh deployment
5. ✅ Hard cache clears, PCF control removed and re-added
6. ❌ Various Power FX syntaxes for accessing `Param("data")` fields - all throw errors

## Environment

- **Power Apps Environment:** SPAARKE DEV 1
- **PCF Framework:** Power Apps Component Framework (latest)
- **Custom Page:** sprk_documentuploaddialog_e52db
- **Model-Driven App:** Spaarke
- **PCF Version:** 3.0.2
- **Power Apps CLI:** 1.46.1

## File Locations

All files are in: `C:\code_files\spaarke\src\controls\UniversalQuickCreate\`

**Most Critical Files:**
1. `UniversalQuickCreate/ControlManifest.Input.xml` - PCF manifest
2. `UniversalQuickCreate/index.ts` - PCF control code
3. `UniversalQuickCreateSolution/src/WebResources/sprk_subgrid_commands.js` - Web resource
4. Custom Page: `sprk_documentuploaddialog_e52db` (in Dataverse, not file-based)

**Supporting Files:**
- `UniversalQuickCreate/types/index.ts` - TypeScript interfaces
- `UniversalQuickCreate/components/DocumentUploadForm.tsx` - React UI
- `UniversalQuickCreate/services/*.ts` - Service layer

## Next Steps

Once we know the correct Power FX syntax for accessing `Param("data")` fields, we should see:
1. Custom Page variables populated with values
2. PCF control Visible property becomes true
3. PCF updateView() receives parameters
4. Upload form renders successfully
