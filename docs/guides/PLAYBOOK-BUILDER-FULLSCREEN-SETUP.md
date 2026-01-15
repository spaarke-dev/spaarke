# Playbook Builder Full-Screen Setup Guide

> **Purpose**: Deploy the Playbook Builder PCF control as a full-screen Custom Page dialog
> **Entity**: sprk_analysisplaybook (Analysis Playbook)
> **Components**: Custom Page + Ribbon Button + JavaScript Web Resource

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│  Analysis Playbook Form                                      │
│  ┌─────────────────────────────────────────────────────┐    │
│  │ [Open Builder] ← Ribbon Button                       │    │
│  └─────────────────────────────────────────────────────┘    │
│                          │                                   │
│                          ▼ Xrm.Navigation.navigateTo()       │
│  ┌─────────────────────────────────────────────────────┐    │
│  │           Custom Page Dialog (95% x 95%)             │    │
│  │  ┌─────────────────────────────────────────────┐    │    │
│  │  │       PlaybookBuilderHost PCF Control       │    │    │
│  │  │  ┌─────────────────────────────────────┐   │    │    │
│  │  │  │        React Flow Canvas            │   │    │    │
│  │  │  │   (nodes, edges, properties)        │   │    │    │
│  │  │  └─────────────────────────────────────┘   │    │    │
│  │  └─────────────────────────────────────────────┘    │    │
│  └─────────────────────────────────────────────────────┘    │
│                          │                                   │
│                          ▼ On Save → Patch sprk_canvasjson   │
└─────────────────────────────────────────────────────────────┘
```

---

## Step 1: Deploy Web Resource

Upload the JavaScript command script to Dataverse.

### 1.1 Create Web Resource

1. Navigate to **make.powerapps.com** → **Solutions**
2. Open your solution (e.g., `SpaarkePlatform`)
3. Click **New** → **More** → **Web resource**
4. Configure:
   - **Display Name**: `Playbook Commands`
   - **Name**: `sprk_playbook_commands` (will become `sprk_playbook_commands.js`)
   - **Type**: `JavaScript (JScript)`
5. Click **Choose file** and select:
   ```
   src/client/webresources/js/sprk_playbook_commands.js
   ```
6. Click **Save** → **Publish**

### 1.2 Verify Deployment

```bash
# List web resources
pac solution export --path temp.zip --name SpaarkePlatform
# Check for sprk_playbook_commands in the solution
```

---

## Step 2: Create Custom Page

Create a Canvas App Custom Page that hosts the PCF control.

### 2.1 Create New Custom Page

1. Navigate to **make.powerapps.com** → **Apps**
2. Click **New app** → **Page**
3. Configure:
   - **Name**: `Playbook Builder Page`
   - **Table**: `Analysis Playbook (sprk_analysisplaybook)`
4. Click **Create**

### 2.2 Add PCF Control

1. In Power Apps Studio, click **Insert** → **Get more components**
2. Search for `PlaybookBuilderHost`
3. Select and add the control
4. Configure control properties (see section 2.3)

### 2.3 Configure Control Data Binding

In Power Apps Studio, set the control properties:

| Property | Formula | Purpose |
|----------|---------|---------|
| `playbookId` | `Param("recordId")` | Record ID from dialog |
| `playbookName` | `LookUp('Analysis Playbooks', sprk_analysisplaybookid = GUID(Param("recordId"))).sprk_name` | Display name |
| `canvasJson` | `LookUp('Analysis Playbooks', sprk_analysisplaybookid = GUID(Param("recordId"))).sprk_canvasjson` | Current canvas state |

### 2.4 Add Save Logic

Add an OnChange handler to save canvas changes back to Dataverse:

```powerfx
// In App.OnStart or control OnChange
Set(varPlaybookId, GUID(Param("recordId")));

// When canvasJson output changes, patch back to Dataverse
If(
    !IsBlank(PlaybookBuilderHost.canvasJson),
    Patch(
        'Analysis Playbooks',
        LookUp('Analysis Playbooks', sprk_analysisplaybookid = varPlaybookId),
        { sprk_canvasjson: PlaybookBuilderHost.canvasJson }
    )
)
```

### 2.5 Configure Page Layout

1. Set page dimensions to fill container:
   - Width: `Parent.Width`
   - Height: `Parent.Height`
2. Remove default header/footer elements for clean full-screen experience
3. Set PCF control to fill page:
   - X: `0`
   - Y: `0`
   - Width: `Parent.Width`
   - Height: `Parent.Height`

### 2.6 Save and Publish

1. Click **File** → **Save**
2. Click **File** → **Publish**
3. Note the Custom Page logical name (shown in URL or properties)
   - Example: `sprk_playbookbuilderpage_xxxxx`
4. Update `sprk_playbook_commands.js` with the actual logical name

---

## Step 3: Add Ribbon Button

Add a command bar button to the Analysis Playbook form.

### 3.1 Ribbon XML

Create or update the ribbon definition for `sprk_analysisplaybook`:

```xml
<RibbonDiffXml>
  <CustomActions>
    <!-- Open Builder button on form command bar -->
    <CustomAction Id="Spaarke.Playbook.OpenBuilder.CustomAction"
                  Location="Mscrm.Form.sprk_analysisplaybook.MainTab.Actions.Controls._children"
                  Sequence="10">
      <CommandUIDefinition>
        <Button Id="Spaarke.Playbook.OpenBuilder.Button"
                Command="Spaarke.Playbook.OpenBuilder.Command"
                LabelText="Open Builder"
                Alt="Open Builder"
                ToolTipTitle="Open Builder"
                ToolTipDescription="Open the visual Playbook Builder in full-screen mode"
                TemplateAlias="o1"
                Image16by16="/_imgs/ribbon/Grid_Expand_16.png"
                Image32by32="/_imgs/ribbon/Grid_Expand_32.png"
                ModernImage="GridExpand" />
      </CommandUIDefinition>
    </CustomAction>
  </CustomActions>

  <CommandDefinitions>
    <CommandDefinition Id="Spaarke.Playbook.OpenBuilder.Command">
      <EnableRules>
        <EnableRule Id="Spaarke.Playbook.OpenBuilder.EnableRule" />
      </EnableRules>
      <DisplayRules />
      <Actions>
        <JavaScriptFunction Library="$webresource:sprk_playbook_commands.js"
                           FunctionName="Spaarke_OpenPlaybookBuilder">
          <CrmParameter Value="PrimaryControl" />
        </JavaScriptFunction>
      </Actions>
    </CommandDefinition>
  </CommandDefinitions>

  <RuleDefinitions>
    <EnableRules>
      <EnableRule Id="Spaarke.Playbook.OpenBuilder.EnableRule">
        <JavaScriptFunction Library="$webresource:sprk_playbook_commands.js"
                           FunctionName="Spaarke_EnableOpenPlaybookBuilder">
          <CrmParameter Value="PrimaryControl" />
        </JavaScriptFunction>
      </EnableRule>
    </EnableRules>
  </RuleDefinitions>
</RibbonDiffXml>
```

### 3.2 Deploy Ribbon via Ribbon Workbench

1. Open **XrmToolBox** → **Ribbon Workbench**
2. Load your solution
3. Select `sprk_analysisplaybook` entity
4. Add button to Form command bar:
   - Right-click **MainTab** → **Customize Command**
   - Add new Button with above configuration
5. Publish

### 3.3 Alternative: Deploy via Solution XML

1. Export solution containing `sprk_analysisplaybook` entity
2. Extract solution ZIP
3. Edit `customizations.xml`:
   - Find `<Entity>` element for `sprk_analysisplaybook`
   - Add/update `<RibbonDiffXml>` section with above XML
4. Pack and import solution

---

## Step 4: Update Custom Page Name

After creating the Custom Page, update the JavaScript with the actual logical name:

```javascript
// In sprk_playbook_commands.js, line ~92
var pageInput = {
    pageType: "custom",
    name: "sprk_playbookbuilderpage_xxxxx",  // ← UPDATE WITH ACTUAL NAME
    recordId: params.playbookId
};
```

Re-upload the web resource after updating.

---

## Step 5: Test

### 5.1 Test Button Visibility

1. Open an existing Analysis Playbook record
2. Verify "Open Builder" button appears in command bar
3. Verify button is disabled for unsaved records

### 5.2 Test Dialog Opening

1. Click "Open Builder" button
2. Verify Custom Page opens as 95% x 95% dialog
3. Verify PCF control loads with correct playbook data
4. Verify canvas is interactive (add nodes, connect edges)

### 5.3 Test Save Flow

1. Make changes in the builder (add/modify nodes)
2. Click Save button in PCF toolbar
3. Close dialog
4. Verify `sprk_canvasjson` field is updated in Dataverse
5. Re-open builder and verify changes persisted

---

## Troubleshooting

### Button Not Appearing

1. Verify web resource is published
2. Clear browser cache (`Ctrl+Shift+R`)
3. Check browser console for JavaScript errors
4. Verify ribbon XML is correctly formatted

### Dialog Shows Error

1. Check Custom Page logical name matches JavaScript
2. Verify Custom Page is published
3. Check browser console for navigation errors
4. Verify PCF control is added to Custom Page

### Canvas Changes Not Saving

1. Check PCF `canvasJson` output is bound correctly
2. Verify Custom Page has save logic (Patch formula)
3. Check browser console for errors
4. Verify user has write permission on entity

---

## Files Reference

| File | Purpose |
|------|---------|
| `src/client/webresources/js/sprk_playbook_commands.js` | Ribbon button command script |
| `src/client/pcf/PlaybookBuilderHost/` | PCF control source |
| `docs/guides/PLAYBOOK-BUILDER-FULLSCREEN-SETUP.md` | This guide |

---

## Related Guides

- [PCF-CUSTOM-PAGE-DEPLOY.md](PCF-CUSTOM-PAGE-DEPLOY.md) - Custom Page deployment details
- [RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md](RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md) - Ribbon button patterns
- [PCF-V9-PACKAGING.md](PCF-V9-PACKAGING.md) - PCF packaging and versioning

---

*Last updated: January 2026*
