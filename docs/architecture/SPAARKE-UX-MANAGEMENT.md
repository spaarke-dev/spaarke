# Spaarke UX Management

> **Last Updated**: December 5, 2025
>
> **Purpose**: Documents UX customizations in Spaarke model-driven apps, including themes, ribbons, and other user experience configurations.

---

## Table of Contents

1. [Dark Mode Theme Persistence](#dark-mode-theme-persistence)
2. [Entity Ribbon Customizations](#entity-ribbon-customizations)
3. [Maintenance Procedures](#maintenance-procedures)

---

## Dark Mode Theme Persistence

### Overview

Spaarke implements user-selectable dark mode that **persists across browser sessions**. Users can choose between:

| Theme | Behavior |
|-------|----------|
| **Auto** | Follows system preference (default) |
| **Light** | Always light mode |
| **Dark** | Always dark mode |

### Technical Implementation

Power Platform MDA dark mode is controlled via URL flag: `flags=themeOption%3Ddarkmode`

The persistence mechanism uses:
1. **localStorage** - Stores user preference (`spaarke-theme` key)
2. **Ribbon EnableRule** - Triggers on every entity grid load
3. **URL Redirect** - Adds/removes dark mode flag based on stored preference

```
┌─────────────────────────────────────────────────────────────────┐
│                    Theme Persistence Flow                        │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  User clicks "Dark" in Theme menu                               │
│         │                                                        │
│         ▼                                                        │
│  localStorage.setItem('spaarke-theme', 'dark')                  │
│         │                                                        │
│         ▼                                                        │
│  Redirect to URL with flags=themeOption%3Ddarkmode              │
│         │                                                        │
│         ▼                                                        │
│  ═══════════════════════════════════════════════════            │
│  Later: User navigates to different entity grid                 │
│         │                                                        │
│         ▼                                                        │
│  Ribbon loads → EnableRule calls isEnabled()                    │
│         │                                                        │
│         ▼                                                        │
│  init() checks: localStorage='dark' but URL missing flag?       │
│         │                                                        │
│         ▼                                                        │
│  Auto-redirect to add dark mode flag                            │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### Components

#### Web Resource: `sprk_ThemeMenu.js`

**Location**: `src/client/webresources/js/sprk_ThemeMenu.js`

Key functions:
- `Spaarke.Theme.setTheme(theme)` - Called by ribbon buttons, saves to localStorage and redirects
- `Spaarke.Theme.isEnabled()` - Called by ribbon EnableRule, triggers `init()` as side effect
- `Spaarke.Theme.init()` - Checks localStorage vs URL and redirects if mismatch

#### Ribbon Solutions

| Solution | Entities Covered | Location |
|----------|------------------|----------|
| DocumentRibbons | sprk_spedocument | `infrastructure/dataverse/ribbon/DocumentRibbons/` |
| MatterRibbons | sprk_Matter | `infrastructure/dataverse/ribbon/MatterRibbons/` |
| ThemeMenuRibbons | sprk_Project, sprk_Invoice, sprk_Event | `infrastructure/dataverse/ribbon/ThemeMenuRibbons/` |

#### Ribbon Structure (per entity)

Each entity ribbon includes:
- **FlyoutAnchor** - Theme dropdown menu at `Mscrm.HomepageGrid.{entity}.MainTab.Actions.Controls._children`
- **Buttons** - Auto, Light, Dark options
- **Commands** - SetAuto, SetLight, SetDark calling `Spaarke.Theme.setTheme()`
- **EnableRule** - Calls `Spaarke.Theme.isEnabled()` which triggers theme enforcement

### Web Resource Icons

| Resource Name | Purpose |
|---------------|---------|
| sprk_ThemeMenu16.svg | Flyout button icon (16x16) |
| sprk_ThemeMenu32.svg | Flyout button icon (32x32) |
| sprk_ThemeAuto16.svg | Auto option icon |
| sprk_ThemeLight16.svg | Light option icon |
| sprk_ThemeDark16.svg | Dark option icon |

---

## Entity Ribbon Customizations

### Entities with Theme Menu

The following entities have the Theme menu ribbon button configured:

| Entity Schema Name | Display Name | Ribbon Solution |
|--------------------|--------------|-----------------|
| sprk_spedocument | Document | DocumentRibbons |
| sprk_Matter | Matter | MatterRibbons |
| sprk_Project | Project | ThemeMenuRibbons |
| sprk_Invoice | Invoice | ThemeMenuRibbons |
| sprk_Event | Event | ThemeMenuRibbons |

### Ribbon Location Pattern

All theme menu buttons use location:
```
Mscrm.HomepageGrid.{entity_lowercase}.MainTab.Actions.Controls._children
```

Example for sprk_Project:
```
Mscrm.HomepageGrid.sprk_project.MainTab.Actions.Controls._children
```

---

## Maintenance Procedures

### Adding Theme Menu to a New Entity

When adding a new main entity to the application, you **must** add the Theme menu ribbon to ensure theme persistence works when users navigate to that entity.

#### Option 1: Add to Existing ThemeMenuRibbons Solution (Recommended)

1. Edit `infrastructure/dataverse/ribbon/ThemeMenuRibbons/Other/Customizations.xml`

2. Add a new `<Entity>` block following this template:

```xml
<Entity>
  <Name LocalizedName="{DisplayName}" OriginalName="{DisplayName}">sprk_{EntityName}</Name>
  <EntityInfo>
    <entity Name="sprk_{EntityName}" unmodified="1">
      <attributes />
    </entity>
  </EntityInfo>
  <RibbonDiffXml>
    <CustomActions>
      <CustomAction Id="sprk.ThemeMenu.{EntityName}.CustomAction" Location="Mscrm.HomepageGrid.sprk_{entityname_lowercase}.MainTab.Actions.Controls._children" Sequence="900">
        <CommandUIDefinition>
          <FlyoutAnchor Id="sprk.ThemeMenu.{EntityName}.Flyout" Command="sprk.ThemeMenu.{EntityName}.Command" LabelText="Theme" ToolTipTitle="Select Theme" ToolTipDescription="Choose your preferred color theme" Image16by16="$webresource:sprk_ThemeMenu16.svg" Image32by32="$webresource:sprk_ThemeMenu32.svg" PopulateDynamically="false" TemplateAlias="o1">
            <Menu Id="sprk.ThemeMenu.{EntityName}.Menu">
              <MenuSection Id="sprk.ThemeMenu.{EntityName}.Section" Title="Color Theme" Sequence="10">
                <Controls Id="sprk.ThemeMenu.{EntityName}.Controls">
                  <Button Id="sprk.ThemeMenu.{EntityName}.Auto" Command="sprk.ThemeMenu.{EntityName}.SetAuto" LabelText="Auto" Image16by16="$webresource:sprk_ThemeAuto16.svg" Sequence="10" />
                  <Button Id="sprk.ThemeMenu.{EntityName}.Light" Command="sprk.ThemeMenu.{EntityName}.SetLight" LabelText="Light" Image16by16="$webresource:sprk_ThemeLight16.svg" Sequence="20" />
                  <Button Id="sprk.ThemeMenu.{EntityName}.Dark" Command="sprk.ThemeMenu.{EntityName}.SetDark" LabelText="Dark" Image16by16="$webresource:sprk_ThemeDark16.svg" Sequence="30" />
                </Controls>
              </MenuSection>
            </Menu>
          </FlyoutAnchor>
        </CommandUIDefinition>
      </CustomAction>
    </CustomActions>
    <Templates><RibbonTemplates Id="Mscrm.Templates"></RibbonTemplates></Templates>
    <CommandDefinitions>
      <CommandDefinition Id="sprk.ThemeMenu.{EntityName}.Command"><EnableRules><EnableRule Id="sprk.ThemeMenu.{EntityName}.EnableRule" /></EnableRules><DisplayRules /><Actions /></CommandDefinition>
      <CommandDefinition Id="sprk.ThemeMenu.{EntityName}.SetAuto"><EnableRules /><DisplayRules /><Actions><JavaScriptFunction Library="$webresource:sprk_ThemeMenu.js" FunctionName="Spaarke.Theme.setTheme"><StringParameter Value="auto" /></JavaScriptFunction></Actions></CommandDefinition>
      <CommandDefinition Id="sprk.ThemeMenu.{EntityName}.SetLight"><EnableRules /><DisplayRules /><Actions><JavaScriptFunction Library="$webresource:sprk_ThemeMenu.js" FunctionName="Spaarke.Theme.setTheme"><StringParameter Value="light" /></JavaScriptFunction></Actions></CommandDefinition>
      <CommandDefinition Id="sprk.ThemeMenu.{EntityName}.SetDark"><EnableRules /><DisplayRules /><Actions><JavaScriptFunction Library="$webresource:sprk_ThemeMenu.js" FunctionName="Spaarke.Theme.setTheme"><StringParameter Value="dark" /></JavaScriptFunction></Actions></CommandDefinition>
    </CommandDefinitions>
    <RuleDefinitions><TabDisplayRules /><DisplayRules /><EnableRules><EnableRule Id="sprk.ThemeMenu.{EntityName}.EnableRule"><CustomRule FunctionName="Spaarke.Theme.isEnabled" Library="$webresource:sprk_ThemeMenu.js" Default="true" /></EnableRule></EnableRules></RuleDefinitions>
    <LocLabels />
  </RibbonDiffXml>
</Entity>
```

3. Update Solution.xml to add the entity as a RootComponent:

```xml
<RootComponent type="1" schemaName="sprk_{EntityName}" behavior="2" />
```

4. Pack and import:

```bash
cd infrastructure/dataverse/ribbon/ThemeMenuRibbons
pac solution pack --zipfile ThemeMenuRibbons.zip --folder .
pac solution import --path ThemeMenuRibbons.zip --publish-changes
```

#### Option 2: Create Separate Ribbon Solution

For entities that require additional ribbon customizations beyond the theme menu, create a dedicated solution following the pattern in `DocumentRibbons/` or `MatterRibbons/`.

### Updating the Theme Menu JavaScript

If you need to modify theme behavior:

1. Edit `src/client/webresources/js/sprk_ThemeMenu.js`

2. Update the web resource in Dataverse:

```powershell
# Using Dataverse Web API (PowerShell)
$content = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes((Get-Content -Path "src/client/webresources/js/sprk_ThemeMenu.js" -Raw)))

$body = @{
    content = $content
} | ConvertTo-Json

Invoke-RestMethod -Uri "$env:DATAVERSE_URL/api/data/v9.2/webresourceset(sprk_ThemeMenu.js_GUID)" `
    -Method PATCH -Body $body -Headers $headers -ContentType "application/json"
```

3. Publish the web resource in the Power Platform maker portal

### Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| Theme doesn't persist to new entity | Entity missing ribbon customization | Add theme menu ribbon per procedure above |
| Theme menu not appearing | Ribbon solution not imported | Import the relevant ribbon solution |
| Dark mode flickers on load | EnableRule not triggering | Verify EnableRule references `Spaarke.Theme.isEnabled` |
| Console errors on theme change | Web resource not published | Publish `sprk_ThemeMenu.js` |

### Known Limitations

1. **Initial page load** - The very first page load after login may briefly show wrong theme until ribbon loads
2. **Non-entity pages** - Pages without entity grids (dashboards, settings) won't trigger ribbon, so theme may not enforce until navigating to an entity
3. **URL sharing** - If a user shares a URL with/without dark mode flag, recipient may see different theme until they navigate

---

## Future Considerations

### Potential Improvements

1. **Application-level enforcement** - Investigate using App Module ribbon or Client API `Xrm.Navigation` hooks for more universal enforcement
2. **Server-side preference** - Store theme preference in Dataverse user settings for cross-browser persistence
3. **ThemeEnforcer PCF** - A PCF control exists at `src/client/pcf/ThemeEnforcer/` that could be embedded in sitemap for alternative enforcement (currently not used)

---

## Related Documentation

- [MDA Dark Mode Theme Project](../../projects/mda-darkmode-theme/spec.md) - Original design specification
- [ADR-006: PCF over Web Resources](../adr/ADR-006-pcf-over-webresources.md) - Architecture decision for UI components
