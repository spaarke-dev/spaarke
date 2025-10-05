# Troubleshooting Guide - Universal Dataset Grid PCF Component

**Version**: 1.0.0
**Last Updated**: 2025-10-04

---

## Quick Reference

| Issue | Section | Priority |
|-------|---------|----------|
| Control not loading | [TR-01](#tr-01-control-not-loading) | Critical |
| Import errors | [TR-02](#tr-02-solution-import-errors) | Critical |
| Data not displaying | [TR-03](#tr-03-data-not-displaying) | Critical |
| Performance issues | [TR-04](#tr-04-performance-issues) | High |
| Configuration not applying | [TR-05](#tr-05-configuration-issues) | High |
| Command not working | [TR-06](#tr-06-command-execution-issues) | Medium |
| Theme issues | [TR-07](#tr-07-theme-and-appearance-issues) | Low |

---

## TR-01: Control Not Loading

### Symptom
Control area is blank, shows error message, or displays loading spinner indefinitely.

### Possible Causes & Solutions

#### Issue 1.1: Solution Not Imported
**Diagnosis**:
```bash
# Check if solution is installed
pac solution list | grep "UniversalDatasetGridSolution"
```

**Solution**:
1. Navigate to make.powerapps.com > Solutions
2. Verify "UniversalDatasetGridSolution" is in list
3. If missing, import solution package
4. If present, verify version is 1.0.0

#### Issue 1.2: Control Not Added to Form
**Diagnosis**:
1. Open form in designer
2. Check if control exists in form sections
3. Verify control is not hidden

**Solution**:
1. Add control to form:
   - Form Designer > Add Component > Universal Dataset Grid
2. Configure dataset binding
3. Save and publish

#### Issue 1.3: JavaScript Errors
**Diagnosis**:
```javascript
// Check browser console (F12)
// Look for errors related to:
// - "UniversalDatasetGrid"
// - "Spaarke.UI.Components"
// - React errors
```

**Solution**:
1. Clear browser cache (Ctrl+Shift+Del)
2. Hard refresh page (Ctrl+F5)
3. Check console for specific error messages
4. See [TR-08](#tr-08-javascript-errors) for error-specific fixes

#### Issue 1.4: Platform Library Mismatch
**Diagnosis**:
```
Console Error: "React version mismatch"
or "Fluent UI version mismatch"
```

**Solution**:
1. Verify manifest platform libraries:
   - React: 16.8.6
   - Fluent UI: 9.0.0
2. If wrong versions, rebuild control
3. Reimport solution

#### Issue 1.5: Dataset Not Bound
**Diagnosis**:
Control loads but shows "No data" or empty state

**Solution**:
1. Open form in designer
2. Select control
3. Properties panel > Dataset
4. Ensure dataset is bound to entity dataset
5. Save and publish

---

## TR-02: Solution Import Errors

### Symptom
Solution import fails with validation or dependency errors.

### Possible Causes & Solutions

#### Issue 2.1: Missing Dependencies
**Error Message**: "Missing required components"

**Solution**:
1. Ensure PCF components enabled:
   - Power Platform Admin Center
   - Environment > Settings > Features
   - Enable "Power Apps component framework for canvas apps"
2. Retry import

#### Issue 2.2: Version Conflict
**Error Message**: "Version conflict" or "Solution already exists"

**Solution**:
1. Check existing version:
   ```bash
   pac solution list
   ```
2. Options:
   - **Upgrade**: Import with "Upgrade" option
   - **Delete old**: Delete old version first, then import
   - **Different env**: Deploy to different environment

#### Issue 2.3: Publisher Mismatch
**Error Message**: "Publisher does not match"

**Solution**:
1. Verify publisher "Spaarke" exists in environment
2. If not, solution will create it
3. If conflict, adjust publisher in solution or environment

#### Issue 2.4: Permissions Error
**Error Message**: "Insufficient permissions"

**Solution**:
1. Verify user has System Administrator or System Customizer role
2. Check environment permissions
3. Contact admin to grant necessary permissions

---

## TR-03: Data Not Displaying

### Symptom
Control loads but records not visible.

### Possible Causes & Solutions

#### Issue 3.1: No Data in Entity
**Diagnosis**:
```bash
# Verify records exist
# Check in default view or custom query
```

**Solution**:
1. Open entity in standard view
2. Verify records exist
3. If no records, create test data
4. Refresh control

#### Issue 3.2: Filter Blocking Records
**Diagnosis**:
Control shows "No records found" but entity has data

**Solution**:
1. Check view filters on form
2. Verify dataset filter configuration
3. Temporarily remove filters to test
4. Adjust filters as needed

#### Issue 3.3: Permission Issues
**Diagnosis**:
User cannot see records despite existing

**Solution**:
1. Check user's security role
2. Verify Read permission on entity
3. Check row-level security (if configured)
4. Grant necessary permissions

#### Issue 3.4: Field-Level Security
**Diagnosis**:
Grid displays but columns are empty

**Solution**:
1. Check field-level security profiles
2. Grant user access to secured fields
3. Or remove FLS from columns needed by control

#### Issue 3.5: Column Configuration Error
**Diagnosis**:
Columns not configured properly

**Solution**:
1. Verify view includes columns
2. Check dataset column configuration
3. Ensure primary field is included
4. Rebuild view if necessary

---

## TR-04: Performance Issues

### Symptom
Control is slow to load, laggy scrolling, or freezing.

### Possible Causes & Solutions

#### Issue 4.1: Large Dataset Without Virtualization
**Diagnosis**:
- Entity has 100+ records
- Scrolling is slow
- Browser freezes

**Solution**:
Enable virtualization in configuration:
```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "enableVirtualization": true,
    "virtualizationThreshold": 50
  }
}
```

#### Issue 4.2: Too Many Columns
**Diagnosis**:
- View has 20+ columns
- Horizontal scrolling
- Slow rendering

**Solution**:
1. Reduce columns in view to essential fields (8-12 recommended)
2. Create simplified view for control
3. Use card view for complex data

#### Issue 4.3: Complex Calculated Fields
**Diagnosis**:
- Calculated fields or rollup columns in view
- Slow data retrieval

**Solution**:
1. Remove calculated fields from view
2. Use simple fields only
3. Or cache calculated values in regular fields

#### Issue 4.4: Network Latency
**Diagnosis**:
- Initial load is slow
- "Loading..." state persists

**Solution**:
1. Check network connection
2. Verify Dataverse environment performance
3. Check Power Platform service health
4. Contact admin if environment-wide issue

#### Issue 4.5: Browser Issues
**Diagnosis**:
- Performance degrades over time
- Memory usage increases

**Solution**:
1. Clear browser cache
2. Close other tabs/applications
3. Restart browser
4. Update to latest browser version
5. Try different browser

---

## TR-05: Configuration Issues

### Symptom
Configuration JSON not applying or causing errors.

### Possible Causes & Solutions

#### Issue 5.1: Invalid JSON Syntax
**Diagnosis**:
```
Console Error: "JSON parse error"
```

**Solution**:
1. Validate JSON syntax using https://jsonlint.com
2. Common errors:
   - Missing commas
   - Trailing commas
   - Unquoted property names
   - Single quotes instead of double quotes
3. Fix syntax and reapply

**Valid Example**:
```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Grid"
  }
}
```

#### Issue 5.2: Wrong Schema Version
**Diagnosis**:
Configuration ignored or partial

**Solution**:
Ensure schemaVersion is "1.0":
```json
{
  "schemaVersion": "1.0",
  ...
}
```

#### Issue 5.3: Invalid View Mode
**Diagnosis**:
View mode not changing

**Solution**:
Use valid view modes only:
- "Grid"
- "List"
- "Card"

```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Grid"  // Valid
  }
}
```

#### Issue 5.4: Invalid Command Names
**Diagnosis**:
Commands not appearing

**Solution**:
Use valid command names:
- Built-in: "open", "create", "delete", "refresh"
- Custom: Any alphanumeric string

```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "enabledCommands": ["open", "refresh"]
  }
}
```

#### Issue 5.5: Entity Config Not Matching
**Diagnosis**:
Entity-specific config not applying

**Solution**:
Verify entity logical name is correct:
```json
{
  "entityConfigs": {
    "account": { ... },      // Correct
    "Account": { ... },      // Wrong - case sensitive!
    "accounts": { ... }      // Wrong - use singular
  }
}
```

Use logical name, not display name:
- ✅ "account" (logical name)
- ❌ "Account" (display name)
- ❌ "Accounts" (plural)

---

## TR-06: Command Execution Issues

### Symptom
Commands not working or producing errors.

### Possible Causes & Solutions

#### Issue 6.1: Command Not Visible
**Diagnosis**:
Expected command not in toolbar

**Solution**:
1. Check configuration enabledCommands:
```json
{
  "defaultConfig": {
    "enabledCommands": ["open", "create", "delete", "refresh"]
  }
}
```
2. Verify command name is correct
3. Check if command requires selection (select a record first)

#### Issue 6.2: Permission Denied
**Diagnosis**:
```
Error: "You don't have permission to perform this action"
```

**Solution**:
1. Verify user's security role
2. Check entity permissions:
   - Create permission for "Create" command
   - Delete permission for "Delete" command
   - Write permission for "Edit" command
3. Grant necessary permissions

#### Issue 6.3: Open Command Not Working
**Diagnosis**:
Clicking "Open" does nothing

**Solution**:
1. Check browser popup blocker
2. Allow popups for your Dataverse domain
3. Try Ctrl+Click to open in new tab
4. Check browser console for errors

#### Issue 6.4: Custom Command Failing
**Diagnosis**:
Custom command executes but fails

**Solution**:
1. Verify Custom API exists:
```bash
pac customapi list | grep "sprk_YourCommand"
```
2. Check Custom API parameters match configuration
3. Review Custom API execution logs
4. Test Custom API directly (Power Apps API)

#### Issue 6.5: Delete Command Confirmation Not Showing
**Diagnosis**:
Delete executes immediately without confirmation

**Solution**:
This is expected behavior if confirmation is disabled.
To enable:
```json
{
  "customCommands": {
    "delete": {
      "requireConfirmation": true
    }
  }
}
```

---

## TR-07: Theme and Appearance Issues

### Symptom
Control doesn't match app theme or looks incorrect.

### Possible Causes & Solutions

#### Issue 7.1: Theme Not Detected
**Diagnosis**:
Control always shows light theme

**Solution**:
Theme detection is automatic. If not working:
1. Verify app has theme configured
2. Check browser developer tools > body class
3. Should see "theme-light" or "theme-dark"
4. If missing, theme detection may fail - this is a known limitation

#### Issue 7.2: High Contrast Mode Issues
**Diagnosis**:
Control not readable in high contrast mode

**Solution**:
1. Disable high contrast temporarily
2. Or adjust Windows high contrast settings
3. Report issue for future enhancement

#### Issue 7.3: Custom Theme Colors
**Diagnosis**:
Organization's custom theme not applied to control

**Solution**:
Control uses Fluent UI defaults. Custom org themes may not fully apply to PCF controls. This is a platform limitation.

#### Issue 7.4: Layout Issues
**Diagnosis**:
- Overlapping elements
- Cutoff text
- Misaligned columns

**Solution**:
1. Ensure control has sufficient width
2. Check form section width settings
3. Try full-width section
4. Reduce number of columns if too many

---

## TR-08: JavaScript Errors

### Common Errors & Solutions

#### Error 8.1: "Cannot read property 'dataset' of undefined"
**Cause**: Dataset not properly bound

**Solution**:
1. Verify dataset binding in form designer
2. Ensure parameter name matches manifest: "dataset"
3. Republish form

#### Error 8.2: "React is not defined"
**Cause**: React platform library not loaded

**Solution**:
1. Verify manifest includes:
```xml
<platform-library name="React" version="16.8.6" />
```
2. Rebuild and redeploy control

#### Error 8.3: "FluentUI is not defined"
**Cause**: Fluent UI platform library not loaded

**Solution**:
1. Verify manifest includes:
```xml
<platform-library name="Fluent" version="9.0.0" />
```
2. Rebuild and redeploy control

#### Error 8.4: "JSON.parse: unexpected character"
**Cause**: Invalid JSON in configJson parameter

**Solution**:
1. Validate JSON using https://jsonlint.com
2. Fix syntax errors
3. Save and republish

#### Error 8.5: "Cannot call method 'openDatasetItem' of undefined"
**Cause**: Dataset API not available

**Solution**:
1. Ensure feature-usage includes WebAPI:
```xml
<uses-feature name="WebAPI" required="true" />
```
2. Rebuild control

---

## TR-09: Deployment Issues

### Issue 9.1: Control Not Appearing in Component List
**Diagnosis**:
After import, control not available to add to forms

**Solution**:
1. Verify solution import completed successfully
2. Clear browser cache
3. Sign out and sign back in
4. Wait 5-10 minutes for platform to register control
5. Check different browser

### Issue 9.2: "Control Not Supported" Error
**Diagnosis**:
Error when trying to add control to form

**Solution**:
1. Verify form type supports PCF controls (Main forms do, others may not)
2. Check entity supports custom controls
3. Try different form or entity

### Issue 9.3: Multiple Versions Conflict
**Diagnosis**:
Old version still showing after upgrade

**Solution**:
1. Uninstall old version completely
2. Clear browser cache
3. Import new version
4. Hard refresh browser (Ctrl+Shift+F5)

---

## TR-10: Data Integrity Issues

### Issue 10.1: Stale Data Displaying
**Diagnosis**:
Grid shows old data after record updates

**Solution**:
1. Click "Refresh" command
2. Or configure auto-refresh:
```json
{
  "defaultConfig": {
    "autoRefreshInterval": 30000  // 30 seconds
  }
}
```
3. Note: Auto-refresh may impact performance

### Issue 10.2: Selection Lost After Refresh
**Diagnosis**:
Selected records deselected after refresh

**Solution**:
This is expected behavior. Selection state clears on data reload for data integrity.

To persist selection (not recommended):
```json
{
  "defaultConfig": {
    "persistSelectionOnRefresh": true
  }
}
```

---

## Diagnostic Tools

### Browser Developer Console
```javascript
// Check control instance
window.UniversalDatasetGrid

// Check React version
React.version

// Check loaded libraries
Object.keys(window).filter(k => k.includes('React') || k.includes('Fluent'))
```

### Power Apps CLI
```bash
# List solutions
pac solution list

# List PCF controls
pac pcf list

# Check environment
pac env list

# View logs
pac telemetry enable
pac telemetry view
```

### Network Tab Analysis
1. Open Developer Tools (F12)
2. Go to Network tab
3. Reload page
4. Look for:
   - Failed requests (red)
   - Slow requests (>2s)
   - 404 errors (missing resources)

---

## Getting Help

### Before Contacting Support

Gather this information:
1. **Error Message**: Exact text and screenshot
2. **Browser**: Name and version (Chrome 120, Edge 119, etc.)
3. **Console Logs**: JavaScript errors from console
4. **Network Logs**: Failed requests from Network tab
5. **Configuration**: Your configJson value
6. **Environment**: Dev, Test, or Prod
7. **User Role**: Security role name
8. **Steps to Reproduce**: Detailed steps

### Support Channels

1. **Documentation**: Review [docs/guides/](../../docs/guides/)
2. **GitHub Issues**: Report at https://github.com/your-org/spaarke/issues
3. **Internal Support**: Contact Spaarke Engineering team
4. **Microsoft Support**: For platform issues

---

## Common Troubleshooting Workflow

```
Issue Occurs
    ↓
Check Browser Console (F12)
    ↓
Any JavaScript errors?
    ├─ Yes → See TR-08
    └─ No → Continue
    ↓
Check Network Tab
    ↓
Any failed requests?
    ├─ Yes → Check URLs, permissions
    └─ No → Continue
    ↓
Verify Configuration
    ├─ Check JSON syntax
    ├─ Validate schema
    └─ Test with minimal config
    ↓
Test in Incognito/Private Window
    ├─ Works? → Browser cache/extension issue
    └─ Still fails? → Platform or permission issue
    ↓
Test with Different User
    ├─ Works? → Permission issue
    └─ Still fails? → Configuration or deployment issue
    ↓
Still Not Resolved?
    → Contact Support with diagnostic info
```

---

## Best Practices to Avoid Issues

### 1. Configuration
- ✅ Always validate JSON before applying
- ✅ Start with minimal config, add features gradually
- ✅ Test in dev environment first
- ✅ Document your configuration

### 2. Performance
- ✅ Enable virtualization for large datasets
- ✅ Limit columns in view (8-12 recommended)
- ✅ Avoid calculated fields in grid views
- ✅ Monitor bundle size if customizing

### 3. Deployment
- ✅ Import to dev environment first
- ✅ Test thoroughly before production
- ✅ Use managed solutions for production
- ✅ Version your configurations

### 4. Testing
- ✅ Test with different user roles
- ✅ Test in multiple browsers
- ✅ Test with realistic data volumes
- ✅ Test all view modes

### 5. Monitoring
- ✅ Monitor browser console for errors
- ✅ Track user feedback
- ✅ Review performance metrics
- ✅ Keep documentation updated

---

## Known Limitations

1. **Platform Library Versions**: Must use specific React/Fluent versions (not latest)
2. **Theme Customization**: Limited to Fluent UI default themes
3. **Mobile Support**: Basic support, desktop optimized
4. **Offline Mode**: Not supported (requires Dataverse connection)
5. **Virtual Scrolling**: Activates at 50+ records (not configurable)

---

## FAQ

**Q: Can I use this control offline?**
A: No, requires active connection to Dataverse.

**Q: Can I customize the appearance with CSS?**
A: No, Fluent UI styling is built-in. Customization requires modifying source.

**Q: What's the maximum number of records supported?**
A: Tested up to 100 records. Virtualization handles larger datasets but performance may vary.

**Q: Can I use this on canvas apps?**
A: No, this is a model-driven app control only.

**Q: How do I update to a new version?**
A: Import new solution package with upgrade option.

---

**Document Version**: 1.0
**Last Updated**: 2025-10-04
**Feedback**: Report issues or suggest improvements via GitHub
