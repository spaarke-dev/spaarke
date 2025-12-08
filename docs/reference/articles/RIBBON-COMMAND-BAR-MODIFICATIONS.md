# Ribbon and Command Bar Modifications

> **Last Updated**: December 2025
> **Category**: Dataverse Customization

---

## Overview

This document provides templates and guidance for modifying the Dataverse ribbon (command bar) in model-driven apps. It covers the RibbonDiffXml structure, common locations, and required attributes for successful icon display in the modern Unified Client Interface (UCI).

---

## Key Requirements for Icons

### Critical: The `Alt` Attribute

**The `Alt` attribute is REQUIRED** on all `Button` and `FlyoutAnchor` elements. Without it, the solution may fail to publish or icons may not display correctly.

### Image Attributes

| Attribute | Format | Purpose |
|-----------|--------|---------|
| `Image16by16` | SVG web resource | Classic ribbon (16x16 icon) |
| `Image32by32` | SVG web resource | Classic ribbon (32x32 icon) |
| `ModernImage` | SVG web resource | **Modern UCI command bar** |

**Important**: Use `.svg` extension in web resource names (e.g., `sprk_IconName16.svg`).

### SVG Web Resource Requirements

1. Must be uploaded as web resource type 11 (SVG)
2. Use `viewBox="0 0 20 20"` - Modern UI standard size
3. Use `fill="currentColor"` for automatic dark mode adaptation
4. **Do NOT include** `width` or `height` attributes (causes display issues)
5. Avoid embedded stylesheets - use inline styles or fill attributes
6. Name pattern: `{prefix}_{IconName}{Size}.svg`

### Best Practice: SVG Structure for ModernImage

```xml
<!-- sprk_ThemeMenu32.svg - Recommended structure -->
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor">
  <!-- Note: Modern UI uses 20x20 viewBox for optimal scaling -->
  <path d="M10 2a8 8 0 1 0 0 16 8 8 0 0 0 0-16zm0 1.5A6.5 6.5 0 0 1 16.5 10a6.5 6.5 0 0 1-6.5 6.5A6.5 6.5 0 0 1 3.5 10 6.5 6.5 0 0 1 10 3.5z"/>
</svg>
```

**Key Points:**
- ✅ Use `fill="currentColor"` - adapts to light/dark mode automatically
- ✅ Use `viewBox="0 0 20 20"` - Modern UI standard size
- ✅ Keep simple paths - complex SVGs may render poorly at small sizes
- ✅ No hardcoded colors (unless intentional)
- ❌ **Do NOT** include `width` or `height` attributes

### Dark Mode Support

**Automatic Dark Mode (Recommended)**

SVGs with `currentColor` automatically adapt to the theme:

```xml
<!-- SVG with currentColor automatically adapts -->
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20">
  <path fill="currentColor" d="..."/>
</svg>
```

Result:
- **Light mode**: Icon uses dark color (from theme)
- **Dark mode**: Icon uses light color (from theme)
- **No JavaScript needed!**

**Manual Dark Mode (Advanced)**

If you need different icons for light/dark mode:

```xml
<!-- Button definition with display rules -->
<Button Id="sprk.ThemeMenu.Button"
        ModernImage="$webresource:sprk_ThemeMenu_light.svg"
        LabelText="Dark Mode">
  <ModernCommandBar.Images>
    <Image ImageId="lightMode"
           Source="$webresource:sprk_ThemeMenu_light.svg" />
    <Image ImageId="darkMode"
           Source="$webresource:sprk_ThemeMenu_dark.svg" />
  </ModernCommandBar.Images>
</Button>
```

> **Note**: This is complex - prefer the `currentColor` approach.

---

## Solution Structure

### Folder Structure for Extracted Solution

```
{SolutionName}_extracted/
├── [Content_Types].xml
├── customizations.xml    # <-- RibbonDiffXml is here
└── solution.xml
```

### customizations.xml Structure

```xml
<ImportExportXml>
  <Entities>
    <Entity>
      <Name>sprk_EntityName</Name>
      <RibbonDiffXml>
        <CustomActions>...</CustomActions>
        <Templates>...</Templates>
        <CommandDefinitions>...</CommandDefinitions>
        <RuleDefinitions>...</RuleDefinitions>
        <LocLabels>...</LocLabels>
      </RibbonDiffXml>
    </Entity>
  </Entities>
</ImportExportXml>
```

---

## Common Ribbon Locations

### Homepage Grid (Entity List View)

| Location Pattern | Description |
|-----------------|-------------|
| `Mscrm.HomepageGrid.{entity}.MainTab.Actions.Controls._children` | Main action buttons on grid |
| `Mscrm.HomepageGrid.{entity}.MainTab.Management.Controls._children` | Management section |
| `Mscrm.HomepageGrid.{entity}.MainTab.Save.Controls._children` | Save group |

### Subgrid (Related Records)

| Location Pattern | Description |
|-----------------|-------------|
| `Mscrm.SubGrid.{entity}.MainTab.Management.Controls._children` | Subgrid management buttons |
| `Mscrm.SubGrid.{entity}.MainTab.Actions.Controls._children` | Subgrid action buttons |

### Form Ribbon

| Location Pattern | Description |
|-----------------|-------------|
| `Mscrm.Form.{entity}.MainTab.Actions.Controls._children` | Form action buttons |
| `Mscrm.Form.{entity}.MainTab.Save.Controls._children` | Form save group |

### Global/Application Ribbon

| Location Pattern | Description |
|-----------------|-------------|
| `Mscrm.GlobalTab.MainTab.More._children` | "More Commands" overflow menu |

**Note**: Replace `{entity}` with the logical name in lowercase (e.g., `sprk_document`, `sprk_project`).

---

## Templates

### FlyoutAnchor with Menu (Dropdown Button)

```xml
<CustomAction Id="sprk.{Feature}.FlyoutAnchor.CustomAction"
              Location="Mscrm.HomepageGrid.{entity}.MainTab.Actions.Controls._children"
              Sequence="900">
  <CommandUIDefinition>
    <FlyoutAnchor Alt="$LocLabels:sprk.{Feature}.FlyoutAnchor.Alt"
                  Id="sprk.{Feature}.FlyoutAnchor"
                  Command="sprk.{Feature}.FlyoutCommand"
                  LabelText="$LocLabels:sprk.{Feature}.FlyoutAnchor.LabelText"
                  ToolTipTitle="$LocLabels:sprk.{Feature}.FlyoutAnchor.ToolTipTitle"
                  ToolTipDescription="$LocLabels:sprk.{Feature}.FlyoutAnchor.ToolTipDescription"
                  Image16by16="$webresource:sprk_{Feature}16.svg"
                  Image32by32="$webresource:sprk_{Feature}32.svg"
                  ModernImage="$webresource:sprk_{Feature}32.svg"
                  PopulateDynamically="false"
                  TemplateAlias="o1"
                  Sequence="900">
      <Menu Id="sprk.{Feature}.FlyoutAnchor.Menu">
        <MenuSection Id="sprk.{Feature}.MenuSection"
                     Title="$LocLabels:sprk.{Feature}.MenuSection.Title"
                     Sequence="10"
                     DisplayMode="Menu16">
          <Controls Id="sprk.{Feature}.MenuSection.Controls">
            <!-- Menu buttons go here -->
          </Controls>
        </MenuSection>
      </Menu>
    </FlyoutAnchor>
  </CommandUIDefinition>
</CustomAction>
```

### Button (Standalone or Menu Item)

```xml
<Button Alt="$LocLabels:sprk.{Feature}.{Action}.Alt"
        Id="sprk.{Feature}.{Action}"
        Command="sprk.{Feature}.{Action}Command"
        LabelText="$LocLabels:sprk.{Feature}.{Action}.LabelText"
        ToolTipTitle="$LocLabels:sprk.{Feature}.{Action}.ToolTipTitle"
        ToolTipDescription="$LocLabels:sprk.{Feature}.{Action}.ToolTipDescription"
        Image16by16="$webresource:sprk_{IconName}16.svg"
        ModernImage="$webresource:sprk_{IconName}16.svg"
        Sequence="10" />
```

### CommandDefinition with JavaScript

```xml
<CommandDefinition Id="sprk.{Feature}.{Action}Command">
  <EnableRules>
    <EnableRule Id="sprk.{Feature}.EnableRule" />
  </EnableRules>
  <DisplayRules />
  <Actions>
    <JavaScriptFunction Library="$webresource:sprk_{Script}.js"
                        FunctionName="Namespace.Function.name">
      <StringParameter Value="paramValue" />
      <CrmParameter Value="PrimaryControl" />
    </JavaScriptFunction>
  </Actions>
</CommandDefinition>
```

### EnableRule with CustomRule

```xml
<EnableRule Id="sprk.{Feature}.EnableRule">
  <CustomRule FunctionName="Namespace.isEnabled"
              Library="$webresource:sprk_{Script}.js"
              Default="true" />
</EnableRule>
```

### LocLabels

```xml
<LocLabels>
  <LocLabel Id="sprk.{Feature}.FlyoutAnchor.LabelText">
    <Titles>
      <Title description="Button Label" languagecode="1033" />
    </Titles>
  </LocLabel>
  <LocLabel Id="sprk.{Feature}.FlyoutAnchor.ToolTipTitle">
    <Titles>
      <Title description="Tooltip Title" languagecode="1033" />
    </Titles>
  </LocLabel>
  <LocLabel Id="sprk.{Feature}.FlyoutAnchor.ToolTipDescription">
    <Titles>
      <Title description="Longer tooltip description" languagecode="1033" />
    </Titles>
  </LocLabel>
  <LocLabel Id="sprk.{Feature}.FlyoutAnchor.Alt">
    <Titles>
      <Title description="Accessibility text" languagecode="1033" />
    </Titles>
  </LocLabel>
</LocLabels>
```

---

## Complete Examples

### Simple Button (Modern Command Bar)

A minimal example for adding a single button to the command bar:

```xml
<RibbonDiffXml>
  <CustomActions>
    <CustomAction Id="sprk.ThemeMenu.CustomAction"
                  Location="Mscrm.HomepageGrid.{entity}.MainTab.Actions.Controls._children"
                  Sequence="50">
      <CommandUIDefinition>
        <Button Id="sprk.ThemeMenu.Button"
                Command="sprk.ThemeMenu.Command"
                Sequence="10"
                LabelText="Theme"
                Alt="Toggle theme"
                ToolTipTitle="Theme Selector"
                ToolTipDescription="Switch between light and dark mode"
                Image16by16="$webresource:sprk_ThemeMenuIcon16.svg"
                ModernImage="$webresource:sprk_ThemeMenuIcon16.svg"
                TemplateAlias="o1" />
      </CommandUIDefinition>
    </CustomAction>
  </CustomActions>

  <CommandDefinitions>
    <CommandDefinition Id="sprk.ThemeMenu.Command">
      <EnableRules />
      <DisplayRules />
      <Actions>
        <JavaScriptFunction
          Library="$webresource:sprk_theme_manager.js"
          FunctionName="Spaarke.toggleTheme" />
      </Actions>
    </CommandDefinition>
  </CommandDefinitions>
</RibbonDiffXml>
```

### Dark Mode Theme Menu (Flyout with Sub-Items)

See [document-ribbon-example.xml](document-ribbon-example.xml) for a complete working example with flyout menu.

**Key Elements:**

```xml
<!-- FlyoutAnchor with proper attributes -->
<FlyoutAnchor Alt="$LocLabels:sprk.ThemeMenu.FlyoutAnchor.Alt"
              Command="sprk.ThemeMenu.FlyoutCommand"
              Id="sprk.ThemeMenu.FlyoutAnchor"
              Image16by16="$webresource:sprk_ThemeMenu16.svg"
              Image32by32="$webresource:sprk_ThemeMenu32.svg"
              LabelText="$LocLabels:sprk.ThemeMenu.FlyoutAnchor.LabelText"
              ModernImage="$webresource:sprk_ThemeMenu32.svg"
              PopulateDynamically="false"
              TemplateAlias="o1"
              Sequence="900">
  <Menu Id="sprk.ThemeMenu.FlyoutAnchor.Menu">
    <MenuSection Id="sprk.ThemeMenu.MenuSection"
                 Title="$LocLabels:sprk.ThemeMenu.MenuSection.Title"
                 Sequence="10"
                 DisplayMode="Menu16">
      <Controls Id="sprk.ThemeMenu.MenuSection.Controls">
        <Button Alt="$LocLabels:sprk.ThemeMenu.Auto.Alt"
                Id="sprk.ThemeMenu.Auto"
                Command="sprk.ThemeMenu.SetAuto"
                LabelText="$LocLabels:sprk.ThemeMenu.Auto.LabelText"
                Image16by16="$webresource:sprk_ThemeAuto16.svg"
                ModernImage="$webresource:sprk_ThemeAuto16.svg"
                Sequence="10" />
        <!-- Additional menu items... -->
      </Controls>
    </MenuSection>
  </Menu>
</FlyoutAnchor>
```

### Web Resources Required

| Name | Type | Description |
|------|------|-------------|
| `sprk_ThemeMenu16.svg` | SVG (11) | 16x16 menu icon |
| `sprk_ThemeMenu32.svg` | SVG (11) | 32x32 menu icon |
| `sprk_ThemeAuto16.svg` | SVG (11) | Auto option icon |
| `sprk_ThemeLight16.svg` | SVG (11) | Light option icon |
| `sprk_ThemeDark16.svg` | SVG (11) | Dark option icon |
| `sprk_ThemeMenu.js` | JS (3) | JavaScript functions |

---

## Workflow: Modifying Ribbons

### Step 1: Export Solution

```powershell
pac solution export --name {SolutionName} --path "temp\{SolutionName}.zip" --managed false
```

### Step 2: Extract

```powershell
Expand-Archive -Path "temp\{SolutionName}.zip" -DestinationPath "temp\{SolutionName}_extracted" -Force
```

### Step 3: Edit customizations.xml

1. Locate `<RibbonDiffXml>` section for your entity
2. Add/modify elements following templates above
3. **Ensure all `Alt` attributes are present**
4. Add corresponding `LocLabels`

### Step 4: Repackage

```powershell
Compress-Archive -Path "temp\{SolutionName}_extracted\*" -DestinationPath "temp\{SolutionName}_modified.zip" -Force
```

### Step 5: Import

```powershell
pac solution import --path "temp\{SolutionName}_modified.zip" --publish-changes
```

---

## Hiding Existing Buttons

To hide out-of-the-box (OOTB) or existing custom buttons, use `<HideCustomAction>`. This is the **only supported method** - do not try to delete or modify the original button definition.

### HideCustomAction Syntax

```xml
<HideCustomAction Id="sprk.Hide.{OriginalButtonId}"
                  Location="{OriginalButtonLocation}"
                  HideActionId="{OriginalCustomActionId}" />
```

### Finding Button IDs to Hide

1. **Export the solution** containing the button you want to hide
2. **Search customizations.xml** for the button's `Id` attribute
3. **Note the `Location`** from its parent `<CustomAction>`
4. **Note the `Id`** of the `<CustomAction>` element (this is `HideActionId`)

### Example: Hiding the OOTB "New" Button on a Subgrid

```xml
<RibbonDiffXml>
  <CustomActions>
    <!-- Hide the default "Add New" button on document subgrid -->
    <HideCustomAction Id="sprk.Hide.Mscrm.AddNewRecordFromSubGridStandard"
                      Location="Mscrm.SubGrid.sprk_document.MainTab.Management.Controls._children"
                      HideActionId="Mscrm.SubGrid.sprk_document.AddNewStandard" />
    
    <!-- Hide the default "Add Existing" button on document subgrid -->
    <HideCustomAction Id="sprk.Hide.Mscrm.AddExistingRecordFromSubGridStandard"
                      Location="Mscrm.SubGrid.sprk_document.MainTab.Management.Controls._children"
                      HideActionId="Mscrm.SubGrid.sprk_document.AddExistingStandard" />
  </CustomActions>
  <!-- Rest of ribbon definition... -->
</RibbonDiffXml>
```

### Common OOTB Button IDs to Hide

| Button | HideActionId Pattern | Location Pattern |
|--------|---------------------|------------------|
| New (Grid) | `Mscrm.HomepageGrid.{entity}.NewRecord` | `Mscrm.HomepageGrid.{entity}.MainTab.Management.Controls._children` |
| New (Subgrid) | `Mscrm.SubGrid.{entity}.AddNewStandard` | `Mscrm.SubGrid.{entity}.MainTab.Management.Controls._children` |
| Add Existing (Subgrid) | `Mscrm.SubGrid.{entity}.AddExistingStandard` | `Mscrm.SubGrid.{entity}.MainTab.Management.Controls._children` |
| Delete (Grid) | `Mscrm.HomepageGrid.{entity}.DeleteMenu` | `Mscrm.HomepageGrid.{entity}.MainTab.Management.Controls._children` |
| Edit (Form) | `Mscrm.Form.{entity}.Edit` | `Mscrm.Form.{entity}.MainTab.Actions.Controls._children` |

> **Note**: OOTB button IDs vary by Dataverse version. Always verify by exporting and inspecting.

### Important Considerations

1. **HideCustomAction is additive** - You're adding a "hide" instruction, not deleting anything
2. **Order matters** - Place `<HideCustomAction>` elements inside `<CustomActions>` alongside regular `<CustomAction>` elements
3. **Use unique IDs** - The `Id` attribute of `<HideCustomAction>` must be unique (use `sprk.Hide.` prefix)
4. **Cannot hide conditionally** - To show/hide based on conditions, use `DisplayRules` instead of `HideCustomAction`

---

## Subgrid Considerations

Subgrid ribbons have **different requirements** than homepage grid or form ribbons.

### Key Differences

| Aspect | Homepage Grid / Form | Subgrid |
|--------|---------------------|----------|
| Location prefix | `Mscrm.HomepageGrid.{entity}` or `Mscrm.Form.{entity}` | `Mscrm.SubGrid.{entity}` |
| CrmParameter for context | `PrimaryControl` | `SelectedControl` |
| Parent record access | N/A | Via `SelectedControl.getParentForm()` |
| Selection access | `SelectedControlSelectedItemIds` | `SelectedControlSelectedItemReferences` |

### Subgrid Location Patterns

| Location | Description |
|----------|-------------|
| `Mscrm.SubGrid.{entity}.MainTab.Management.Controls._children` | Management buttons (New, Add Existing) |
| `Mscrm.SubGrid.{entity}.MainTab.Actions.Controls._children` | Action buttons |
| `Mscrm.SubGrid.{entity}.ContextualTabs.SubGridCommands._children` | Contextual commands (when records selected) |

### JavaScript for Subgrid Commands

**Getting the subgrid context:**

```javascript
// In your ribbon JavaScript function
function onSubgridButtonClick(selectedControl) {
    // selectedControl is the subgrid control
    
    // Get parent form context
    var parentFormContext = selectedControl.getParentForm();
    
    // Get parent record ID
    var parentId = parentFormContext.data.entity.getId();
    
    // Get selected records in subgrid
    var selectedIds = selectedControl.getGrid().getSelectedRows();
    selectedIds.forEach(function(row) {
        var recordId = row.data.entity.getId();
        console.log("Selected: " + recordId);
    });
}
```

**CommandDefinition for subgrid:**

```xml
<CommandDefinition Id="sprk.Document.SubgridAction.Command">
  <EnableRules>
    <EnableRule Id="sprk.Document.SubgridAction.EnableRule" />
  </EnableRules>
  <DisplayRules />
  <Actions>
    <JavaScriptFunction Library="$webresource:sprk_SubgridCommands.js"
                        FunctionName="Spaarke.Document.onSubgridAction">
      <!-- Use SelectedControl for subgrids, not PrimaryControl -->
      <CrmParameter Value="SelectedControl" />
    </JavaScriptFunction>
  </Actions>
</CommandDefinition>
```

### EnableRules for Subgrids

**Enable only when records are selected:**

```xml
<EnableRule Id="sprk.Document.SelectionRequired.EnableRule">
  <SelectionCountRule AppliesTo="SelectedEntity" Minimum="1" />
</EnableRule>
```

**Enable only for single selection:**

```xml
<EnableRule Id="sprk.Document.SingleSelection.EnableRule">
  <SelectionCountRule AppliesTo="SelectedEntity" Minimum="1" Maximum="1" />
</EnableRule>
```

### Complete Subgrid Button Example

```xml
<RibbonDiffXml>
  <CustomActions>
    <!-- Custom "Upload Documents" button on document subgrid -->
    <CustomAction Id="sprk.Document.Upload.CustomAction"
                  Location="Mscrm.SubGrid.sprk_document.MainTab.Management.Controls._children"
                  Sequence="5">
      <CommandUIDefinition>
        <Button Id="sprk.Document.Upload.Button"
                Command="sprk.Document.Upload.Command"
                Alt="$LocLabels:sprk.Document.Upload.Alt"
                LabelText="$LocLabels:sprk.Document.Upload.LabelText"
                ToolTipTitle="$LocLabels:sprk.Document.Upload.ToolTipTitle"
                ToolTipDescription="$LocLabels:sprk.Document.Upload.ToolTipDescription"
                Image16by16="$webresource:sprk_Upload16.svg"
                ModernImage="$webresource:sprk_Upload16.svg"
                Sequence="5"
                TemplateAlias="o1" />
      </CommandUIDefinition>
    </CustomAction>
  </CustomActions>

  <CommandDefinitions>
    <CommandDefinition Id="sprk.Document.Upload.Command">
      <EnableRules>
        <EnableRule Id="sprk.AlwaysEnabled" />
      </EnableRules>
      <DisplayRules />
      <Actions>
        <JavaScriptFunction Library="$webresource:sprk_DocumentCommands.js"
                            FunctionName="Spaarke.Document.openUploadDialog">
          <CrmParameter Value="SelectedControl" />
        </JavaScriptFunction>
      </Actions>
    </CommandDefinition>
  </CommandDefinitions>

  <RuleDefinitions>
    <TabDisplayRules />
    <DisplayRules />
    <EnableRules>
      <EnableRule Id="sprk.AlwaysEnabled">
        <CustomRule FunctionName="Spaarke.Rules.isEnabled"
                    Library="$webresource:sprk_DocumentCommands.js"
                    Default="true" />
      </EnableRule>
    </EnableRules>
  </RuleDefinitions>

  <LocLabels>
    <LocLabel Id="sprk.Document.Upload.Alt">
      <Titles><Title description="Upload Documents" languagecode="1033" /></Titles>
    </LocLabel>
    <LocLabel Id="sprk.Document.Upload.LabelText">
      <Titles><Title description="Upload" languagecode="1033" /></Titles>
    </LocLabel>
    <LocLabel Id="sprk.Document.Upload.ToolTipTitle">
      <Titles><Title description="Upload Documents" languagecode="1033" /></Titles>
    </LocLabel>
    <LocLabel Id="sprk.Document.Upload.ToolTipDescription">
      <Titles><Title description="Upload multiple documents to this record" languagecode="1033" /></Titles>
    </LocLabel>
  </LocLabels>
</RibbonDiffXml>
```

---

## Troubleshooting

### Common Issues

| Issue | Cause | Solution |
|-------|-------|----------|
| Icons show as placeholder (jigsaw) | Missing `ModernImage` or wrong format | Use SVG with `.svg` extension in `ModernImage` |
| Solution fails to import | Missing `Alt` attribute | Add `Alt="$LocLabels:..."` to all buttons/flyouts |
| Icons not updating | Browser cache | Clear cache, Ctrl+F5 |
| Button not visible | Wrong `Location` path | Verify entity logical name in path |
| JavaScript not executing | Web resource not published | Publish web resources before importing solution |

### SVG Icon Issues

| Issue | Cause | Solution |
|-------|-------|----------|
| Icon not showing at all | SVG has `width`/`height` attributes | Remove `width` and `height` from SVG |
| Icon wrong size | Incorrect `viewBox` | Use `viewBox="0 0 20 20"` for Modern UI |
| Icon wrong color in dark mode | Hardcoded fill color | Use `fill="currentColor"` instead |
| Icon shows but looks distorted | Complex paths or transforms | Simplify SVG, flatten transforms |
| Web resource returns 404 | Not published or wrong name | Verify web resource exists and name matches exactly |

---

## Related Resources

- **Skill**: `.claude/skills/ribbon-edit/SKILL.md` - AI skill for ribbon modifications
- [Example: document-ribbon-example.xml](document-ribbon-example.xml) - Working Dark Mode menu example
- [Microsoft Docs: Command bar customization](https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/use-command-designer)

---

*Last updated: December 2025*
