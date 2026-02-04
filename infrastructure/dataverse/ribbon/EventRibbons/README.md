# Event Ribbon - Update Related Button

Adds "Update Related" command bar button to the Event entity in three locations:
- **Homepage Grid** - When Event records are selected in the main view
- **Main Form** - On the Event form command bar
- **SubGrid** - When Events appear in subgrids on other entities

## Prerequisites

Deploy these web resources to Dataverse **BEFORE** importing the ribbon:

### 1. JavaScript Web Resource

| Property | Value |
|----------|-------|
| **Name** | `sprk_updaterelated_commands` |
| **Display Name** | Update Related Commands |
| **Type** | JavaScript (JScript) |
| **Source File** | `src/client/webresources/js/sprk_updaterelated_commands.js` |

### 2. SVG Icon Web Resource

| Property | Value |
|----------|-------|
| **Name** | `sprk_UpdateRelated` |
| **Display Name** | Update Related Icon |
| **Type** | SVG |
| **Source File** | `src/client/assets/icons/sprk_UpdateRelated.svg` |

## Deployment Steps

### Option A: Ribbon Workbench (Recommended)

1. Open XrmToolBox → Ribbon Workbench
2. Select the solution containing `sprk_event` entity
3. Add a new button using the `RibbonDiffXml_sprk_event.xml` as reference
4. Configure:
   - Command: `Spaarke_UpdateRelated_Grid` / `Spaarke_UpdateRelated_Form` / `Spaarke_UpdateRelated_SubGrid`
   - Library: `$webresource:sprk_updaterelated_commands.js`
   - Parameter: `SelectedControl` (for grid/subgrid) or `PrimaryControl` (for form)
5. Publish

### Option B: Manual XML Import

1. **Create dedicated ribbon solution**:
   ```powershell
   # In Power Apps maker portal:
   # Create new unmanaged solution "EventRibbons"
   # Add sprk_event entity (table metadata only, no forms/views)
   ```

2. **Export the solution**:
   ```powershell
   pac solution export --name EventRibbons --path "./EventRibbons.zip" --managed false
   ```

3. **Extract and modify**:
   ```powershell
   Expand-Archive -Path "EventRibbons.zip" -DestinationPath "EventRibbons_extracted"
   ```

4. **Edit `customizations.xml`**:
   - Find the `<Entity>` element for `sprk_event`
   - Locate the `<RibbonDiffXml>` section
   - Replace or merge with contents from `RibbonDiffXml_sprk_event.xml`

5. **Re-package and import**:
   ```powershell
   Compress-Archive -Path "EventRibbons_extracted/*" -DestinationPath "EventRibbons_modified.zip"
   pac solution import --path "EventRibbons_modified.zip" --publish-changes
   ```

## Button Locations

| Location | CustomAction Id | Command Parameter |
|----------|-----------------|-------------------|
| Homepage Grid | `sprk.UpdateRelated.HomepageGrid.CustomAction` | `SelectedControl` |
| Main Form | `sprk.UpdateRelated.Form.CustomAction` | `PrimaryControl` |
| SubGrid | `sprk.UpdateRelated.SubGrid.CustomAction` | `SelectedControl` |

## Testing

1. **Homepage Grid**:
   - Navigate to Events list view
   - Select one or more Event records
   - Click "Update Related" button
   - Confirm in dialog
   - Verify related records are updated

2. **Main Form**:
   - Open an existing Event record
   - Click "Update Related" in command bar
   - Confirm in dialog
   - Verify related records are updated

3. **SubGrid**:
   - Open a parent record with Events subgrid
   - Select Event(s) in the subgrid
   - Click "Update Related"
   - Confirm in dialog
   - Verify related records are updated

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Button not appearing | Verify web resources are published; clear browser cache |
| "Web resource not found" on import | Deploy JS and SVG web resources first |
| Button disabled | Check enable rules; for grid/subgrid, select records first |
| API call fails | Verify BFF API is running at configured URL |

## Files

| File | Purpose |
|------|---------|
| `RibbonDiffXml_sprk_event.xml` | Ribbon XML definition |
| `src/client/webresources/js/sprk_updaterelated_commands.js` | JavaScript commands |
| `src/client/assets/icons/sprk_UpdateRelated.svg` | Button icon |
