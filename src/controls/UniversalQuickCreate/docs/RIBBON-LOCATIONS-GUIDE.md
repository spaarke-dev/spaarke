# Ribbon Button Locations - Troubleshooting Guide

## The Issue

The Documents subgrid on the Matter form needs a custom button, but the ribbon location path depends on the **subgrid control name**, not just the entity name.

## Finding the Correct Subgrid Control Name

### Method 1: Check in Form Editor

1. Go to https://make.powerapps.com
2. Navigate to **Tables** → **Matter** (sprk_matter)
3. Click **Forms** tab
4. Open the main form (e.g., "Information" form)
5. Click on the **Documents subgrid**
6. In the properties panel (right), look for **Name** field
7. This is your control name (e.g., `Subgrid_Documents`, `grid_Documents`, etc.)

### Method 2: Check in Ribbon Workbench

1. Open Ribbon Workbench
2. Select **Matter** entity
3. Expand **SubGrid** section
4. You should see a list of subgrids on Matter forms
5. Look for the Documents-related subgrid

## Ribbon Location Formats

Once you have the control name, use this format:

### For SubGrid-specific buttons:
```
Mscrm.SubGrid.[CONTROL_NAME].MainTab.Actions.Controls._children
```

**Examples:**
- If control name is `Documents`: `Mscrm.SubGrid.Documents.MainTab.Actions.Controls._children`
- If control name is `Subgrid_Documents`: `Mscrm.SubGrid.Subgrid_Documents.MainTab.Actions.Controls._children`
- If control name is `grid_sprk_document`: `Mscrm.SubGrid.grid_sprk_document.MainTab.Actions.Controls._children`

### Alternative: Form-level button (Always visible)
```
Mscrm.Form.sprk_matter.MainTab.Actions.Controls._children
```
This adds the button to the main form toolbar instead of the subgrid.

## Ribbon XML Template

Use this template and replace `[CONTROL_NAME]` with the actual subgrid control name:

```xml
<CustomAction Id="Spaarke.Matter.DocumentSubgrid.AddMultiple"
              Location="Mscrm.SubGrid.[CONTROL_NAME].MainTab.Actions.Controls._children"
              Sequence="5">
  <CommandUIDefinition>
    <Button Id="Spaarke.Matter.DocumentSubgrid.AddButton"
            Command="Spaarke.DocumentSubgrid.AddMultiple.Command"
            LabelText="Quick Create: Document"
            Alt="Quick Create: Document"
            ToolTipTitle="Quick Create: Document"
            ToolTipDescription="Upload multiple documents to SharePoint Embedded"
            TemplateAlias="o1"
            Image16by16="/_imgs/ribbon/AddRelationship_16.png"
            Image32by32="/_imgs/ribbon/AddRelationship_32.png" />
  </CommandUIDefinition>
</CustomAction>
```

## Easier Alternative: Use Command Designer Instead

If finding the correct ribbon location is difficult, I recommend using **Command Designer** (modern approach) instead:

1. Open the Matter **form** (not entity, but the actual form)
2. Click on the Documents **subgrid**
3. In properties, find **Command bar** section
4. Click **Edit command bar**
5. Click **+ New command**
6. Configure:
   - **Label**: Quick Create: Document
   - **Action**: Run JavaScript
   - **Library**: sprk_subgrid_commands.js
   - **Function**: Spaarke_AddMultipleDocuments
   - **Pass execution context**: ✅ Yes
7. Save and Publish

This approach is much simpler and doesn't require knowing the exact ribbon location path!

## Common Subgrid Control Names in Dataverse

Here are typical patterns:
- `Documents`
- `Subgrid_Documents`
- `grid_Documents`
- `sprk_document` (entity schema name)
- `grid_sprk_document`
- `Subgrid_sprk_document`

## What to Tell Me

If you want me to help further, please provide:
1. The **Name** property of the Documents subgrid from the Matter form
2. Or a screenshot of the Ribbon Workbench SubGrid section
3. Or we can switch to using Command Designer instead (recommended!)
