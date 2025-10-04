# Common Issues and Solutions

Troubleshooting guide for the Universal Dataset Grid PCF component.

---

## Control Not Rendering

### Symptom
Blank area where grid should appear, no errors in console.

### Possible Causes

1. **Solution not imported**
2. **PCF components not enabled in environment**
3. **Control not added to form/view**
4. **Dataset property not bound**

### Solutions

**1. Verify Solution Installed**
```bash
# Check installed solutions
pac solution list

# Import if missing
pac solution import --path SpaarkeSolution.zip
```

**2. Enable PCF Components in Environment**
1. Navigate to Power Platform Admin Center
2. Select environment
3. Settings > Features
4. Enable "Power Apps component framework for canvas apps"
5. Save

**3. Verify Control Added to Form**
1. Open form in designer
2. Check section for "Universal Dataset Grid" component
3. If missing, add via "Get more components"

**4. Verify Dataset Binding**
1. Select control in form designer
2. Check Properties panel
3. Ensure "dataset" property is bound to entity dataset
4. Save and publish

---

## Configuration Not Loading

### Symptom
Grid uses default settings instead of custom configuration.

### Possible Causes

1. **Invalid JSON syntax**
2. **Configuration property not bound**
3. **Schema version mismatch**
4. **Configuration stored in wrong property**

### Solutions

**1. Validate JSON**
```bash
# Use online validator
https://jsonlint.com/

# Check for common errors:
# - Missing commas
# - Trailing commas
# - Unescaped quotes
# - Invalid property names
```

**Example Valid JSON**:
```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Grid"
  }
}
```

**2. Verify Property Binding**
1. Open form designer
2. Select Universal Dataset Grid control
3. Check `configJson` property is populated
4. Ensure value is valid JSON string

**3. Check Schema Version**
```json
{
  "schemaVersion": "1.0"  // Must be "1.0"
}
```

**4. Check Browser Console**
Press F12, look for errors:
```
[UniversalDatasetGrid] Invalid configuration: ...
[UniversalDatasetGrid] Falling back to defaults
```

---

## Custom Commands Not Appearing

### Symptom
Custom commands defined in configuration but not visible in toolbar.

### Possible Causes

1. **Command key in `enabledCommands` array**
2. **Invalid command configuration**
3. **`requiresSelection` blocking visibility**
4. **Icon name invalid**

### Solutions

**1. Remove from enabledCommands**
```json
{
  "enabledCommands": ["open", "create"],  // Don't include custom command keys
  "customCommands": {
    "approve": { /* ... */ }  // Custom commands appear automatically
  }
}
```

**2. Validate Command Configuration**
```json
{
  "customCommands": {
    "approve": {
      "label": "Approve",              // Required
      "actionType": "customapi",       // Required
      "actionName": "sprk_Approve",    // Required
      "icon": "Checkmark"              // Optional
    }
  }
}
```

**3. Select a Record**
If `requiresSelection: true`, select record(s) first.

**4. Verify Icon Name**
```json
{
  "icon": "Checkmark"  // Correct (without "Regular" suffix)
  // NOT: "CheckmarkRegular"
}
```

**Icon Reference**: [Fluent UI Icons](https://react.fluentui.dev/?path=/docs/icons-catalog--default)

---

## Command Button Disabled

### Symptom
Command button visible but grayed out (disabled).

### Possible Causes

1. **`requiresSelection: true` but no records selected**
2. **Selection count below `minSelection`**
3. **Selection count above `maxSelection`**

### Solutions

**1. Select Appropriate Number of Records**
```json
{
  "minSelection": 1,    // Select at least 1 record
  "maxSelection": 10    // Select no more than 10 records
}
```

**2. Check Selection Counter in Toolbar**
Toolbar displays: "2 selected" → Indicates current selection

**3. Disable Selection Requirement (if appropriate)**
```json
{
  "requiresSelection": false  // Button always enabled
}
```

---

## Custom API Not Executing

### Symptom
Command button click does nothing or shows generic error.

### Possible Causes

1. **Custom API not registered in Dataverse**
2. **User lacks execute privilege**
3. **Parameter validation error**
4. **Plugin error**
5. **Typo in action name**

### Solutions

**1. Verify Custom API Exists**
1. Navigate to **Advanced Settings > Customizations > Customize the System**
2. Go to **Custom APIs**
3. Search for API name (e.g., `sprk_ApproveInvoice`)
4. Verify "Is Function" and "Binding Type" settings

**2. Check User Privileges**
1. Custom API properties → **Execute Privilege Name**
2. User's security role must have this privilege
3. Or set to "None" for public access

**3. Validate Parameters in Console**
Press F12, check Network tab:
```json
// Request payload should show interpolated values
{
  "InvoiceId": "a1b2c3d4-...",  // Not "{selectedRecordId}"
  "ApprovedBy": "user-guid"
}
```

**4. Check Plugin Trace Logs**
1. Navigate to **Settings > Plug-in Trace Log**
2. Filter by Custom API name
3. Look for error messages
4. Common errors:
   - `NullReferenceException` - Missing parameter
   - `FaultException` - Business logic error
   - `SecurityException` - Permission denied

**5. Verify Action Name**
```json
{
  "actionName": "sprk_ApproveInvoice"  // Must match Custom API unique name
}
```

---

## Token Interpolation Not Working

### Symptom
Token literal (e.g., `{selectedRecordId}`) passed to API instead of actual value.

### Possible Causes

1. **Typo in token name**
2. **Token not supported for parameter type**
3. **No records selected when token requires selection**

### Solutions

**1. Verify Token Spelling** (case-sensitive)
```json
{
  "parameters": {
    "RecordId": "{selectedRecordId}",     // Correct
    "RecordId": "{SelectedRecordId}",     // Incorrect (uppercase S)
    "RecordId": "{selectedrecordid}"      // Incorrect (all lowercase)
  }
}
```

**2. Use Correct Token for Data Type**

| Token | Type | Use Case |
|-------|------|----------|
| `{selectedRecordId}` | `Guid` | Single record ID |
| `{selectedRecordIds}` | `String` | Comma-separated IDs |
| `{selectedRecord}` | `Object` | Full record object |
| `{selectedCount}` | `Number` | Count of selected records |
| `{entityName}` | `String` | Entity logical name |
| `{currentUserId}` | `Guid` | Current user's ID |

**3. Select Records First**
If using `{selectedRecordId}` or `{selectedRecordIds}`, select record(s) before executing command.

**4. Check Console for Warnings**
```
[CustomCommandFactory] Warning: Token {invalidToken} not recognized
```

---

## Performance Issues

### Symptom
Grid slow to load, scroll, or interact.

### Possible Causes

1. **Large dataset (>1000 records)**
2. **Virtualization disabled**
3. **Too many columns**
4. **Complex custom renderers**
5. **Network latency**

### Solutions

**1. Enable Virtualization**
```json
{
  "enableVirtualization": true,
  "virtualizationThreshold": 50  // Lower for faster activation
}
```

**2. Reduce Dataset Size**
- Apply view filters to limit records
- Use paging if available
- Consider splitting large datasets across multiple views

**3. Reduce Visible Columns**
- Hide unnecessary columns in view designer
- Display only essential fields

**4. Use Grid View Instead of Card View**
Card view renders more complex UI:
```json
{
  "viewMode": "Grid"  // Faster than "Card"
}
```

**5. Enable Compact Toolbar**
```json
{
  "compactToolbar": true  // Less DOM elements
}
```

**6. Check Network Performance**
- Press F12 → Network tab
- Look for slow API calls (>1s)
- Check "Time" column
- If slow, consider Dataverse performance tuning

---

## Grid Not Refreshing After Command

### Symptom
Execute command (approve, delete, etc.) but grid doesn't update.

### Possible Causes

1. **`refresh: true` not set in command config**
2. **Command failed silently**
3. **Browser cache issue**

### Solutions

**1. Enable Refresh in Command Config**
```json
{
  "customCommands": {
    "approve": {
      "label": "Approve",
      "actionType": "customapi",
      "actionName": "sprk_Approve",
      "refresh": true  // Add this
    }
  }
}
```

**2. Check Command Success**
Press F12 → Console:
```
[CommandExecutor] Command 'approve' completed successfully
[DatasetGrid] Refreshing dataset...
```

If no success message, command may have failed - check Network tab.

**3. Clear Browser Cache**
```
Ctrl+Shift+R (Windows)
Cmd+Shift+R (Mac)
```

**4. Manual Refresh**
Click "Refresh" button in toolbar to force update.

---

## View Mode Not Switching

### Symptom
Click view mode switcher (Grid/List/Card) but view doesn't change.

### Possible Causes

1. **View mode not supported for entity**
2. **JavaScript error**
3. **Custom CSS interfering**

### Solutions

**1. Check Console for Errors**
Press F12:
```
[DatasetGrid] Error switching to Card view: ...
```

**2. Verify View Mode Supported**
All view modes should work, but check configuration:
```json
{
  "viewMode": "Grid"  // Must be "Grid", "List", or "Card"
}
```

**3. Disable Custom CSS**
If using custom CSS on form, temporarily disable to test.

**4. Hard Refresh**
```
Ctrl+Shift+R
```

---

## Mobile/Tablet Display Issues

### Symptom
Grid doesn't fit on mobile screen or buttons too small.

### Solutions

**1. Use List or Card View**
```json
{
  "viewMode": "List"  // Better for mobile than Grid
}
```

**2. Enable Compact Toolbar**
```json
{
  "compactToolbar": true  // Icon-only buttons (larger)
}
```

**3. Reduce Visible Columns**
Grid view on mobile should show 2-3 columns max.

**4. Test on Actual Device**
Browser resize doesn't perfectly simulate mobile behavior.

**5. Disable Keyboard Shortcuts**
```json
{
  "enableKeyboardShortcuts": false  // Mobile devices don't need
}
```

---

## Accessibility Issues

### Symptom
Screen reader not announcing actions or keyboard navigation not working.

### Solutions

**1. Enable Accessibility**
```json
{
  "enableAccessibility": true  // Default: true
}
```

**2. Disable Virtualization for Full Accessibility**
```json
{
  "enableVirtualization": false  // Full DOM tree for screen readers
}
```

**3. Verify Keyboard Shortcuts Enabled**
```json
{
  "enableKeyboardShortcuts": true
}
```

**4. Test with Screen Reader**
- **Windows**: NVDA or JAWS
- **Mac**: VoiceOver (Cmd+F5)
- **Expected announcements**:
  - "Universal Dataset Grid, 10 records loaded"
  - "Row 1 of 10, Acme Corp"
  - "2 of 10 selected"

**5. Report Accessibility Bugs**
If screen reader doesn't announce properly, file GitHub issue with:
- Screen reader name/version
- Browser name/version
- Steps to reproduce

---

## Browser Compatibility Issues

### Symptom
Control works in one browser but not another.

### Solutions

**1. Verify Browser Support**

| Browser | Min Version | Status |
|---------|-------------|--------|
| Chrome | 90+ | ✅ Fully supported |
| Edge | 90+ | ✅ Fully supported |
| Firefox | 88+ | ✅ Fully supported |
| Safari | 14+ | ✅ Fully supported |
| IE 11 | - | ❌ Not supported |

**2. Clear Browser Cache**
```
Ctrl+Shift+Delete → Clear cache and cookies
```

**3. Disable Browser Extensions**
Ad blockers or privacy extensions may interfere.

**4. Check Console for Errors**
Different browsers may show different errors.

**5. Test in Incognito/Private Mode**
Eliminates cache and extension interference.

---

## Solution Import Failures

### Symptom
Error when importing solution containing Universal Dataset Grid.

### Possible Causes

1. **Missing dependencies (Fluent UI, React)**
2. **Namespace conflict**
3. **Version mismatch**

### Solutions

**1. Verify Dependencies**
ControlManifest.Input.xml should include:
```xml
<resources>
  <code path="index.ts" order="1" />
  <platform-library name="React" version="18.2.0" />
  <platform-library name="Fluent" version="9.46.2" />
</resources>
```

**2. Check for Namespace Conflicts**
If another control uses `Spaarke.UI.Components` namespace:
```xml
<control namespace="Spaarke.UI.Components.V2"  <!-- Change namespace -->
         constructor="UniversalDatasetGridControl" />
```

**3. Import as Unmanaged First**
Try importing as unmanaged solution to see detailed errors.

**4. Check Solution XML**
Validate solution XML for syntax errors:
```bash
pac solution check --path SpaarkeSolution.zip
```

---

## Debugging Tips

### Enable Verbose Logging

Add to browser console:
```javascript
localStorage.setItem("UniversalDatasetGrid_Debug", "true");
location.reload();
```

Console will show detailed logs:
```
[UniversalDatasetGrid] Initializing...
[UniversalDatasetGrid] Configuration loaded: {...}
[UniversalDatasetGrid] Registering 4 commands
[UniversalDatasetGrid] Rendering 100 records (virtualized)
```

### Network Inspection

Press F12 → Network tab:
1. Filter: XHR
2. Look for `/api/data/v9.2/` requests
3. Check request payload and response
4. Look for 4xx/5xx errors

### React DevTools

Install React DevTools extension:
1. Inspect component tree
2. View component props and state
3. Profile re-renders

---

## Getting Help

### Before Filing an Issue

1. ✅ Check this troubleshooting guide
2. ✅ Search existing GitHub issues
3. ✅ Verify configuration JSON is valid
4. ✅ Check browser console for errors
5. ✅ Test in different browser
6. ✅ Clear cache and retry

### Filing a GitHub Issue

Include:
- **Description**: What you're trying to do
- **Expected Behavior**: What should happen
- **Actual Behavior**: What actually happens
- **Configuration**: Full JSON configuration
- **Environment**:
  - Dataverse environment URL
  - Browser name/version
  - PCF solution version
- **Console Errors**: Copy/paste from F12
- **Screenshots**: If applicable

### Support Channels

- **GitHub Issues**: https://github.com/spaarke/universal-dataset-grid/issues
- **Documentation**: https://docs.spaarke.com

---

## Next Steps

- [Performance Tuning](./Performance.md) - Optimize grid performance
- [Debugging Guide](./Debugging.md) - Advanced debugging techniques
- [Configuration Guide](../guides/ConfigurationGuide.md) - Complete configuration reference
- [API Reference](../api/UniversalDatasetGrid.md) - Component API
