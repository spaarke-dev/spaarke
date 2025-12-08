# Theme Menu Ribbon Configuration

This folder contains the Ribbon XML for the Spaarke Theme Menu.

## Overview

The Theme Menu adds a flyout submenu to the model-driven app command bar (More Commands) that allows users to switch between Auto, Light, and Dark themes.

## Prerequisites

Before configuring the ribbon, ensure the following web resources are deployed to Dataverse:

| Web Resource | Source File | Description |
|-------------|-------------|-------------|
| `sprk_ThemeMenu.js` | `src/client/webresources/js/sprk_ThemeMenu.js` | Theme menu JavaScript handler |
| `sprk_ThemeMenu16.svg` | `src/client/assets/icons/sprk_ThemeMenu16.svg` | Menu icon (16x16) |
| `sprk_ThemeMenu32.svg` | `src/client/assets/icons/sprk_ThemeMenu32.svg` | Menu icon (32x32) |
| `sprk_ThemeAuto16.svg` | `src/client/assets/icons/sprk_ThemeAuto16.svg` | Auto option icon |
| `sprk_ThemeLight16.svg` | `src/client/assets/icons/sprk_ThemeLight16.svg` | Light option icon |
| `sprk_ThemeDark16.svg` | `src/client/assets/icons/sprk_ThemeDark16.svg` | Dark option icon |

## Configuration Steps

### Option 1: Using Ribbon Workbench (Recommended)

1. **Open XrmToolBox** and connect to your Dataverse environment
2. **Launch Ribbon Workbench**
3. **Load your solution** (e.g., SpaarkeCore)
4. **Select "Application Ribbon"** from the entity dropdown
5. **Locate "Mscrm.GlobalTab.MainTab.More"** in the ribbon structure
6. **Add a FlyoutAnchor**:
   - ID: `sprk.ThemeMenu.FlyoutAnchor`
   - Label: "Theme"
   - Image16x16: `$webresource:sprk_ThemeMenu16.svg`
   - Image32x32: `$webresource:sprk_ThemeMenu32.svg`
7. **Add Menu with MenuSection** inside the FlyoutAnchor
8. **Add three Button elements** (NOT ToggleButton):
   - Auto: calls `Spaarke.Theme.setTheme('auto')`
   - Light: calls `Spaarke.Theme.setTheme('light')`
   - Dark: calls `Spaarke.Theme.setTheme('dark')`
9. **Create Command Definitions** with JavaScriptFunction actions
10. **Publish** the customizations

### Option 2: Import XML Directly

If your solution supports XML import:

1. Open the solution in Solution Explorer
2. Navigate to Application Ribbon
3. Import `sprk_ThemeMenuRibbon.xml`
4. Publish

## File Reference

- `sprk_ThemeMenuRibbon.xml` - Complete RibbonDiffXml for the theme menu

## Testing

After publishing:

1. Navigate to any form or view in the model-driven app
2. Click the "..." (More Commands) button in the command bar
3. Verify "Theme" appears with a submenu icon
4. Click "Theme" and verify Auto, Light, Dark options appear
5. Click each option and verify PCF controls update immediately

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Menu not appearing | Check web resources are published; verify solution is correct |
| JavaScript error on click | Verify `sprk_ThemeMenu.js` is published correctly |
| Icons not showing | Verify SVG web resources are deployed with correct names |
| Theme not changing | Check browser console for errors; verify PCF controls have theme listener |

## Related Files

- [Spec Document](../../../projects/mda-darkmode-theme/spec.md) - Section 3.6 for detailed requirements
- [Theme Menu JS](../../../src/client/webresources/js/sprk_ThemeMenu.js) - JavaScript handler
- [SVG Icons](../../../src/client/assets/icons/) - Theme icons
