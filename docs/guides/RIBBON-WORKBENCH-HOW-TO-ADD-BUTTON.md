# Ribbon Customization Guide

> **Last Updated**: April 2026
> **Last Reviewed**: 2026-04-06

How to add ribbon buttons to Dataverse entities via solution export/import (programmatic) or Ribbon Workbench (visual).

---

## CrmParameter Reference (CRITICAL)

**Only use values from this table.** Invalid values corrupt the entity's active ribbon — the only fix is deleting and recreating the entity.

| CrmParameter Value | Context | JS Receives | Use For |
|---------------------|---------|-------------|---------|
| `PrimaryControl` | Form | `formContext` | Form command bar buttons |
| `SelectedControl` | Grid/Subgrid | `gridControl` | Grid/subgrid buttons (recommended) |
| `SelectedControlSelectedItemReferences` | Grid/Subgrid | `selectedItems[]` | Grid item references (not supported on older environments) |
| `PrimaryItemIds` | Grid | `selectedIds[]` | Bulk operations on IDs only |
| `FirstPrimaryItemId` | Grid | `firstId` | Single-selection grid actions |

> **WARNING**: `SelectedItemReferences` (without the `SelectedControl` prefix) is **NOT valid** and will corrupt the entity ribbon.
>
> **WARNING**: `SelectedControlSelectedItemReferences` is not recognized by older Dataverse environments. Prefer `SelectedControl` and extract rows in JS.

---

## Pattern: Form Button

```xml
<RibbonDiffXml>
  <CustomActions>
    <CustomAction Id="Sprk.Feature.Form.CustomAction"
                  Location="Mscrm.Form.sprk_entityname.MainTab.Actions.Controls._children"
                  Sequence="10">
      <CommandUIDefinition>
        <Button Id="Sprk.Feature.Form.Button"
                Command="Sprk.Feature.Form.Command"
                LabelText="Button Label"
                Alt="Button Label"
                ToolTipTitle="Button Label"
                ToolTipDescription="What this button does."
                TemplateAlias="o1"
                Image16by16="/_imgs/ribbon/Approve_16.png"
                Image32by32="/_imgs/ribbon/Approve_32.png"
                ModernImage="Accept" />
      </CommandUIDefinition>
    </CustomAction>
  </CustomActions>

  <CommandDefinitions>
    <CommandDefinition Id="Sprk.Feature.Form.Command">
      <EnableRules>
        <EnableRule Id="Sprk.Feature.Form.EnableRule" />
      </EnableRules>
      <DisplayRules />
      <Actions>
        <JavaScriptFunction Library="$webresource:sprk_/js/myscript.js"
                            FunctionName="Sprk.Feature.formAction">
          <CrmParameter Value="PrimaryControl" />
        </JavaScriptFunction>
      </Actions>
    </CommandDefinition>
  </CommandDefinitions>

  <RuleDefinitions>
    <TabDisplayRules />
    <DisplayRules />
    <EnableRules>
      <EnableRule Id="Sprk.Feature.Form.EnableRule">
        <CustomRule Library="$webresource:sprk_/js/myscript.js"
                    FunctionName="Sprk.Feature.enableButton"
                    Default="true">
          <CrmParameter Value="PrimaryControl" />
        </CustomRule>
      </EnableRule>
    </EnableRules>
  </RuleDefinitions>
</RibbonDiffXml>
```

**Key points**:
- `PrimaryControl` passes the form context to JS
- `Default="true"` shows the button while JS loads
- `Alt` attribute is **required** on all buttons

---

## Pattern: Grid Button (Homepage Grid)

```xml
<RibbonDiffXml>
  <CustomActions>
    <CustomAction Id="Sprk.Feature.Grid.CustomAction"
                  Location="Mscrm.HomepageGrid.sprk_entityname.MainTab.Actions.Controls._children"
                  Sequence="10">
      <CommandUIDefinition>
        <Button Id="Sprk.Feature.Grid.Button"
                Command="Sprk.Feature.Grid.Command"
                LabelText="Button Label"
                Alt="Button Label"
                ToolTipTitle="Button Label"
                ToolTipDescription="What this button does."
                TemplateAlias="o1"
                Image16by16="/_imgs/ribbon/Approve_16.png"
                Image32by32="/_imgs/ribbon/Approve_32.png"
                ModernImage="Accept" />
      </CommandUIDefinition>
    </CustomAction>
  </CustomActions>

  <CommandDefinitions>
    <CommandDefinition Id="Sprk.Feature.Grid.Command">
      <EnableRules>
        <EnableRule Id="Sprk.Feature.Grid.EnableRule" />
      </EnableRules>
      <DisplayRules />
      <Actions>
        <JavaScriptFunction Library="$webresource:sprk_/js/myscript.js"
                            FunctionName="Sprk.Feature.gridAction">
          <CrmParameter Value="SelectedControl" />
        </JavaScriptFunction>
      </Actions>
    </CommandDefinition>
  </CommandDefinitions>

  <RuleDefinitions>
    <TabDisplayRules />
    <DisplayRules />
    <EnableRules>
      <EnableRule Id="Sprk.Feature.Grid.EnableRule">
        <SelectionCountRule AppliesTo="SelectedEntity" Minimum="1" Maximum="100" />
      </EnableRule>
    </EnableRules>
  </RuleDefinitions>
</RibbonDiffXml>
```

**Key points**:
- `SelectedControl` passes the grid context to JS (NOT `SelectedItemReferences`)
- `SelectionCountRule` is preferred over `CustomRule` for grid enable rules (native XML, no JS dependency)
- Extract selected rows in JS: `gridControl.getGrid().getSelectedRows()`
- Refresh grid after action: `gridControl.refresh()`

---

## Pattern: Subgrid Button

```xml
<CustomAction Id="Sprk.Feature.Subgrid.CustomAction"
              Location="Mscrm.SubGrid.sprk_entityname.MainTab.Actions.Controls._children"
              Sequence="5">
  <CommandUIDefinition>
    <Button Id="Sprk.Feature.Subgrid.Button"
            Command="Sprk.Feature.Subgrid.Command"
            LabelText="Add Documents"
            Alt="Add Documents"
            ToolTipTitle="Add Documents"
            ToolTipDescription="Upload multiple documents to this record"
            TemplateAlias="o1"
            Image16by16="/_imgs/ribbon/DocumentAdd_16.png"
            Image32by32="/_imgs/ribbon/DocumentAdd_32.png" />
  </CommandUIDefinition>
</CustomAction>
```

- Use `Mscrm.SubGrid.{entity}` location, NOT `Mscrm.HomepageGrid`
- Use `SelectedControl` CrmParameter (NOT `PrimaryControl`)

---

## JS Pattern: Grid Button Handler

```javascript
async function gridAction(gridControl) {
    // Extract selected items from grid context
    var selectedItems = [];
    if (gridControl && gridControl.getGrid) {
        var selectedRows = gridControl.getGrid().getSelectedRows();
        selectedRows.forEach(function (row) {
            var data = row.getData();
            selectedItems.push({
                Id: data.getEntity().getId().replace(/[{}]/g, ""),
                Name: data.getEntity().getPrimaryAttributeValue()
            });
        });
    }

    if (selectedItems.length === 0) {
        Xrm.Navigation.openAlertDialog({
            title: "No Selection",
            text: "Please select one or more records."
        });
        return;
    }

    // Process selected items...

    // Refresh grid after action
    if (gridControl && gridControl.refresh) {
        gridControl.refresh();
    }
}
```

---

## Deployment Steps

### Step 1: Deploy Web Resource First

Upload the JS web resource before importing the ribbon solution:

```powershell
pac solution list   # Find solution name
# Upload JS via Power Apps maker portal or pac tool
# Publish all customizations
```

### Step 2: Export Dedicated Ribbon Solution

```powershell
pac solution export --name {EntityName}Ribbons --path temp/{EntityName}Ribbons.zip --managed false
```

### Step 3: Edit customizations.xml

Extract ZIP, edit `customizations.xml`, re-package:

```powershell
Expand-Archive -Path temp/{SolutionName}.zip -DestinationPath temp/{SolutionName}_extracted -Force
# Edit customizations.xml
Compress-Archive -Path temp/{SolutionName}_extracted/* -DestinationPath temp/{SolutionName}_modified.zip -Force
```

### Step 4: Import

```powershell
pac solution import --path temp/{SolutionName}_modified.zip --publish-changes
```

---

## Lessons Learned (April 2026)

These were discovered during the Registration Request ribbon implementation:

1. **`SelectedItemReferences` is NOT a valid CrmParameter** — using it corrupted the entity ribbon. The only fix was deleting the entity entirely and recreating it.

2. **`SelectedControlSelectedItemReferences` not supported everywhere** — older Dataverse environments don't recognize this value. Use `SelectedControl` instead and extract rows via `gridControl.getGrid().getSelectedRows()`.

3. **Grid enable rules: prefer `SelectionCountRule` over `CustomRule`** — `SelectionCountRule` is native XML (no JS dependency) and more reliable for grid buttons.

4. **NEVER import the full SpaarkeCore solution for ribbon work** — it locked the entire system during import. Always use a dedicated small solution containing only the target entity.

5. **Grid refresh: use `gridControl.refresh()`** — `Xrm.Utility.refreshParentGrid()` doesn't work in modern UCI.

6. **Namespace export required** — Ribbon XML `FunctionName` uses dot notation (e.g., `Sprk.RegistrationRibbon.approveRequest`). The JS must export the namespace:
   ```javascript
   var Sprk = Sprk || {};
   Sprk.RegistrationRibbon = {
       approveRequest: approveRequest,
       // ...
   };
   ```

7. **MSAL CDN version matters** — verify the version exists before deploying (e.g., 2.38.3 returned 404; 2.35.0 worked).

---

## Working Example: Registration Request Entity

See the complete working implementation:
- **Ribbon XML**: `projects/spaarke-self-service-registration-app/notes/solutions/extracted/customizations.xml`
- **JS Web Resource**: `src/client/webresources/js/sprk_registrationribbon.js`
- **4 buttons**: Approve (form + grid) + Reject (form + grid)
- **Grid buttons**: `SelectedControl` + `SelectionCountRule`
- **Form buttons**: `PrimaryControl` + `CustomRule` with `Default="true"`
