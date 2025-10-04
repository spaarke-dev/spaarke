# Debugging Guide

Advanced debugging techniques for the Universal Dataset Grid PCF component.

---

## Browser Developer Tools

### Opening Developer Tools

**Chrome/Edge**:
- Windows: `F12` or `Ctrl+Shift+I`
- Mac: `Cmd+Option+I`

**Firefox**:
- Windows: `F12` or `Ctrl+Shift+I`
- Mac: `Cmd+Option+I`

**Safari**:
- Mac: `Cmd+Option+C`
- Enable Developer menu first: Safari > Preferences > Advanced > Show Develop menu

---

## Console Debugging

### Enable Verbose Logging

Add to browser console:
```javascript
localStorage.setItem("UniversalDatasetGrid_Debug", "true");
location.reload();
```

**Output**:
```
[UniversalDatasetGrid] Initializing component...
[UniversalDatasetGrid] Dataset mode detected
[UniversalDatasetGrid] Entity: account
[UniversalDatasetGrid] Records: 125
[UniversalDatasetGrid] Configuration loaded: {...}
[UniversalDatasetGrid] Registering 4 built-in commands
[UniversalDatasetGrid] Registering 2 custom commands
[UniversalDatasetGrid] Virtualization enabled (threshold: 100)
[UniversalDatasetGrid] Rendering Grid view
[CommandExecutor] Executing command: approve
[CommandExecutor] Command completed successfully
[DatasetGrid] Refreshing dataset...
```

### Disable Verbose Logging

```javascript
localStorage.removeItem("UniversalDatasetGrid_Debug");
location.reload();
```

---

## Inspecting Component State

### React DevTools

**Installation**:
- [Chrome Extension](https://chrome.google.com/webstore/detail/react-developer-tools/fmkadmapgofadopljbjfkapdkoienihi)
- [Firefox Extension](https://addons.mozilla.org/en-US/firefox/addon/react-devtools/)

**Usage**:
1. Install extension
2. Press F12 → Components tab
3. Navigate component tree:
   ```
   UniversalDatasetGrid
   ├── CommandToolbar
   │   ├── Button (create)
   │   ├── Button (open)
   │   └── Button (refresh)
   └── DatasetGrid
       └── GridView
   ```
4. Select component to view:
   - Props
   - State
   - Hooks
   - Source code location

**Editing Props Live**:
1. Select component
2. Right panel → Props
3. Edit value (e.g., change `viewMode: "Grid"` to `"Card"`)
4. Component re-renders with new value

---

## Debugging Configuration

### Inspect Loaded Configuration

```javascript
// Get configuration service (if exposed)
const config = EntityConfigurationService.getEntityConfiguration("account");
console.log("Account config:", config);

// Output:
// {
//   viewMode: "Card",
//   enabledCommands: ["open", "create"],
//   customCommands: { approve: {...} }
// }
```

### Validate Configuration JSON

```javascript
const configJson = `{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Grid"
  }
}`;

try {
  const config = JSON.parse(configJson);
  console.log("Valid JSON:", config);
} catch (error) {
  console.error("Invalid JSON:", error.message);
}
```

**Common JSON Errors**:
- Missing comma: `{ "a": 1 "b": 2 }`
- Trailing comma: `{ "a": 1, "b": 2, }`
- Unescaped quotes: `{ "message": "Hello "World"" }`
- Single quotes: `{ 'key': 'value' }`

---

## Network Debugging

### Inspecting API Calls

**F12 → Network Tab**:
1. Filter: XHR
2. Look for `/api/data/v9.2/` requests
3. Click request to view:
   - **Headers**: Request URL, method, headers
   - **Payload**: Request body
   - **Response**: API response
   - **Timing**: Request duration

**Example: Custom API Call**:
```
Request URL: https://org.crm.dynamics.com/api/data/v9.2/sprk_ApproveInvoice
Method: POST
Status: 200 OK

Request Payload:
{
  "InvoiceIds": "a1b2c3d4-...,e5f6g7h8-...",
  "ApprovedBy": "user-guid"
}

Response:
{
  "Success": true
}

Timing: 342ms
```

### Debugging Failed API Calls

**Status Code Errors**:

| Status | Meaning | Common Causes |
|--------|---------|---------------|
| **400** | Bad Request | Invalid parameters, missing required field |
| **401** | Unauthorized | Not authenticated |
| **403** | Forbidden | User lacks privilege |
| **404** | Not Found | Custom API doesn't exist, wrong URL |
| **500** | Server Error | Plugin error, Custom API logic error |

**Viewing Error Details**:
```javascript
// Response body for 400/500 errors
{
  "error": {
    "code": "0x80040217",
    "message": "Principal user (Id=...) is missing prvCreateAccount privilege"
  }
}
```

### Network Throttling

**Simulate Slow Network**:
1. F12 → Network tab
2. Throttling dropdown (top)
3. Select "Slow 3G" or "Fast 3G"
4. Reload page

**Purpose**: Test performance on mobile networks

---

## PCF Debugging

### Enable Source Maps

**Verify Source Maps Enabled**:
1. F12 → Sources tab
2. Look for TypeScript files (`.ts`, `.tsx`)
3. If only `.js` files visible, source maps not enabled

**Enable in tsconfig.json**:
```json
{
  "compilerOptions": {
    "sourceMap": true
  }
}
```

**Rebuild**:
```bash
npm run build
```

### Setting Breakpoints

**In TypeScript Source**:
1. F12 → Sources tab
2. Navigate to file (e.g., `CommandExecutor.ts`)
3. Click line number to set breakpoint (blue marker)
4. Execute action (click button)
5. Debugger pauses at breakpoint

**Debugger Controls**:
- **Resume** (F8): Continue execution
- **Step Over** (F10): Execute current line, move to next
- **Step Into** (F11): Enter function call
- **Step Out** (Shift+F11): Exit current function

### Inspecting Variables

**Scope Panel** (right side):
- **Local**: Variables in current function
- **Closure**: Variables from outer scope
- **Global**: Window, document, etc.

**Watch Panel**:
1. Add expression (e.g., `selectedRecords.length`)
2. Value updates as you step through code

**Console** (while paused):
```javascript
// Evaluate expressions
> selectedRecords
[{id: "1", name: "Acme"}, {id: "2", name: "Contoso"}]

> selectedRecords[0].name
"Acme"

> config.viewMode
"Grid"
```

---

## Debugging Commands

### Inspect Command Execution

**Add Logging to Handler**:
```typescript
const command: ICommand = {
  key: "approve",
  label: "Approve",
  handler: async (context: ICommandContext) => {
    console.log("Command handler called");
    console.log("Selected records:", context.selectedRecords);
    console.log("Entity name:", context.entityName);

    try {
      const result = await context.webAPI.execute({
        customApiName: "sprk_ApproveInvoice",
        parameters: {
          InvoiceIds: context.selectedRecords.map(r => r.id).join(",")
        }
      });

      console.log("Command succeeded:", result);
    } catch (error) {
      console.error("Command failed:", error);
    }
  }
};
```

### Debugging Token Interpolation

**Check Interpolated Values**:
```typescript
// In CustomCommandFactory.ts
private static interpolateTokens(
  params: Record<string, string>,
  context: ICommandContext
): any {
  const interpolated: any = {};

  for (const [key, value] of Object.entries(params)) {
    console.log(`Interpolating ${key}: ${value}`);

    if (value === "{selectedRecordId}") {
      interpolated[key] = context.selectedRecords[0]?.id;
      console.log(`  → ${interpolated[key]}`);
    } else if (value === "{selectedRecordIds}") {
      interpolated[key] = context.selectedRecords.map(r => r.id).join(",");
      console.log(`  → ${interpolated[key]}`);
    }
    // ... other tokens
  }

  return interpolated;
}
```

**Output**:
```
Interpolating InvoiceId: {selectedRecordId}
  → a1b2c3d4-5e6f-7g8h-9i0j-k1l2m3n4o5p6
Interpolating ApprovedBy: {currentUserId}
  → user-guid-here
```

---

## Debugging Custom APIs

### Plugin Trace Logs

**Enable Tracing**:
1. Navigate to **Settings > System > Administration**
2. **System Settings**
3. **Customization** tab
4. Enable **Plugin and workflow tracing**
5. Select **All** or **Exception**
6. Save

**View Trace Logs**:
1. **Settings > Customization > Plug-in Trace Log**
2. Filter by:
   - **Type Name**: Your plugin name
   - **Operation**: Custom API name
   - **Created On**: Recent
3. Open log to view details

**Trace Log Contents**:
```
Depth: 1
Message: Starting custom API execution
Correlation Id: abc123...

Input Parameters:
  InvoiceIds: "a1b2c3d4-...,e5f6g7h8-..."
  ApprovedBy: "user-guid"

Execution Time: 342ms

Exception: System.NullReferenceException: Object reference not set to an instance of an object.
   at ApproveInvoicePlugin.Execute(IServiceProvider serviceProvider)
```

### Adding Tracing to Plugin

```csharp
public void Execute(IServiceProvider serviceProvider)
{
    var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

    tracingService.Trace("Plugin started");

    var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
    var service = GetOrganizationService(serviceProvider, context.UserId);

    tracingService.Trace("Getting input parameters");
    var invoiceIds = (string)context.InputParameters["InvoiceIds"];
    tracingService.Trace($"Invoice IDs: {invoiceIds}");

    try
    {
        tracingService.Trace("Updating invoices");
        foreach (var id in invoiceIds.Split(','))
        {
            tracingService.Trace($"Updating invoice {id}");
            // ... update logic
        }

        tracingService.Trace("Plugin completed successfully");
    }
    catch (Exception ex)
    {
        tracingService.Trace($"Error: {ex.Message}");
        tracingService.Trace($"Stack trace: {ex.StackTrace}");
        throw;
    }
}
```

---

## Debugging Virtualization

### Check If Virtualization Active

```javascript
// In browser console
const grid = document.querySelector('[data-control-name="UniversalDatasetGrid"]');
const isVirtualized = grid.querySelector('[data-virtualized="true"]') !== null;
console.log("Virtualization active:", isVirtualized);
```

### Inspect Virtual Scrolling

**React DevTools**:
1. Components tab
2. Find `FixedSizeList` component (from react-window)
3. View props:
   - `itemCount`: Total record count
   - `itemSize`: Row height (px)
   - `overscanCount`: Extra rows rendered

### Force Disable Virtualization

**For Debugging Only**:
```javascript
// In browser console
const config = {
  enableVirtualization: false
};

// Update component props via React DevTools
// Or add to configuration JSON temporarily
```

---

## Debugging Field-Level Security

### Check FLS Cache

```javascript
// If FieldSecurityService exposed
const canRead = await FieldSecurityService.canRead("account", "revenue");
console.log("Can read account.revenue:", canRead);

// Clear cache to force fresh query
FieldSecurityService.clearCache();
```

### Verify FLS Settings in Dataverse

1. Navigate to **Settings > Security > Field Security Profiles**
2. Open relevant profile
3. Click **Field Permissions**
4. Check permissions for field:
   - **Read**: User can view field
   - **Update**: User can edit field

---

## Debugging Performance Issues

### Performance Profiling

**Chrome DevTools Performance Tab**:
1. F12 → Performance
2. Click Record (Ctrl+E)
3. Interact with grid:
   - Scroll
   - Switch views
   - Execute commands
4. Stop recording (Ctrl+E)
5. Analyze flame graph:
   - **Scripting** (yellow): JavaScript execution
   - **Rendering** (purple): Layout, paint
   - **System** (gray): Browser overhead

**Look for**:
- Long tasks (>50ms) - red flags
- Excessive rendering - layout thrashing
- JavaScript bottlenecks

### Memory Profiling

**Chrome DevTools Memory Tab**:
1. F12 → Memory
2. Select "Heap snapshot"
3. Click "Take snapshot"
4. Interact with grid (scroll, select, execute commands)
5. Take another snapshot
6. Compare snapshots:
   - **Comparison** view
   - Look for "Detached DOM" nodes (memory leaks)
   - Check "Delta" column (objects added)

**Healthy Profile**:
- Delta: <1000 objects
- Detached DOM: 0

**Memory Leak**:
- Delta: >10,000 objects
- Detached DOM: >100

---

## Debugging Event Handlers

### Check Event Listeners

**Chrome DevTools**:
1. F12 → Elements tab
2. Select element (e.g., grid container)
3. Right panel → Event Listeners
4. Expand to see:
   - Event type (click, keydown, etc.)
   - Handler function
   - Source location

**Remove Event Listener** (for testing):
```javascript
const grid = document.querySelector('[data-control-name="UniversalDatasetGrid"]');
const clone = grid.cloneNode(true);
grid.parentNode.replaceChild(clone, grid);
// All event listeners removed
```

---

## Debugging TypeScript Errors

### Common TypeScript Errors

**1. "Cannot find module"**
```
Error: Cannot find module '@spaarke/ui-components'
```

**Solution**:
```bash
npm install
npm run build
```

**2. "Property does not exist on type"**
```
Error: Property 'customCommands' does not exist on type 'IDatasetConfig'
```

**Solution**: Check type definition:
```typescript
interface IDatasetConfig {
  customCommands?: Record<string, ICustomCommandConfiguration>;  // Make optional
}
```

**3. "Type is not assignable"**
```
Error: Type 'string' is not assignable to type '"Grid" | "List" | "Card"'
```

**Solution**: Use type assertion:
```typescript
const viewMode: ViewMode = config.viewMode as ViewMode;
```

---

## Remote Debugging

### Debug on Mobile Device

**Chrome Remote Debugging** (Android):
1. Enable USB debugging on device
2. Connect device to computer via USB
3. Chrome → `chrome://inspect`
4. Select device
5. Click "Inspect" on Power Apps tab
6. Full DevTools for mobile device

**Safari Remote Debugging** (iOS):
1. Enable Web Inspector on device:
   - Settings > Safari > Advanced > Web Inspector
2. Connect device to Mac via USB
3. Safari > Develop > [Device Name] > [Power Apps Tab]
4. Full Web Inspector for mobile device

---

## Debugging in Production

### Safe Debugging Techniques

**1. Console Logging** (safe):
```typescript
console.log("[Debug] Selected records:", selectedRecords.length);
```

**2. localStorage Flags** (safe):
```javascript
if (localStorage.getItem("DEBUG_MODE") === "true") {
  console.log("Debug info:", debugData);
}
```

**3. Network Tab** (safe):
- Monitor API calls
- Check payloads and responses
- No code changes needed

**Avoid in Production**:
- ❌ Breakpoints (blocks all users)
- ❌ `debugger;` statements
- ❌ Performance profiling (slows down app)

### Reproducing Production Issues Locally

**1. Get Production Configuration**:
```javascript
// In production, copy configuration
const config = EntityConfigurationService.getEntityConfiguration("account");
console.log(JSON.stringify(config, null, 2));
// Copy JSON
```

**2. Apply Locally**:
```json
// Paste into local configuration JSON
{
  "schemaVersion": "1.0",
  "entityConfigs": {
    "account": { /* production config */ }
  }
}
```

**3. Test Locally**:
```bash
npm start
# Test with production configuration
```

---

## Debugging Checklist

### Before Filing Bug Report

- ✅ Check browser console for errors
- ✅ Verify configuration JSON is valid
- ✅ Check Network tab for failed API calls
- ✅ Test in different browser
- ✅ Test with verbose logging enabled
- ✅ Clear browser cache and retry
- ✅ Check plugin trace logs (for Custom API errors)
- ✅ Verify user has required privileges
- ✅ Test with simplified configuration

### Information to Include

- Configuration JSON
- Browser console errors (full stack trace)
- Network tab request/response (for API errors)
- Plugin trace log (for Custom API errors)
- Steps to reproduce
- Expected vs actual behavior
- Browser name/version
- Dataverse environment URL

---

## Advanced Debugging Tools

### Fiddler (HTTP Debugging Proxy)

**Setup**:
1. Download Fiddler
2. Enable HTTPS decryption
3. Configure browser to use proxy
4. Capture all HTTP traffic

**Use Cases**:
- Inspect encrypted HTTPS requests
- Modify requests/responses on-the-fly
- Simulate network conditions

### Postman (API Testing)

**Test Custom API Directly**:
1. Get access token:
   ```
   POST https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token
   Body:
     grant_type: client_credentials
     client_id: {clientId}
     client_secret: {secret}
     scope: https://{orgUrl}/.default
   ```

2. Call Custom API:
   ```
   POST https://{orgUrl}/api/data/v9.2/sprk_ApproveInvoice
   Headers:
     Authorization: Bearer {accessToken}
   Body:
     {
       "InvoiceIds": "guid1,guid2",
       "ApprovedBy": "user-guid"
     }
   ```

**Benefits**:
- Test Custom API without PCF component
- Isolate API issues from component issues

---

## Next Steps

- [Common Issues](./CommonIssues.md) - Troubleshooting guide
- [Performance Guide](./Performance.md) - Performance optimization
- [Developer Guide](../guides/DeveloperGuide.md) - Architecture and best practices
- [Configuration Guide](../guides/ConfigurationGuide.md) - Configuration reference
