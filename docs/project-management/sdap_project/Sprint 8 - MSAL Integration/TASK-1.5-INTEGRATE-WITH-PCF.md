# Task 1.5: Integrate MsalAuthProvider with PCF Control

**Sprint:** 8 - MSAL Integration
**Task:** 1.5 of 5 (Phase 1)
**Duration:** 45 minutes
**Status:** 📋 **READY TO START**

---

## Task Goal

> **Integrate MsalAuthProvider initialization into Universal Dataset Grid PCF control's init() method.**

**Success Criteria:**
- ✅ `index.ts` updated to initialize `MsalAuthProvider` during control init
- ✅ Error handling added for MSAL initialization failures
- ✅ User-friendly error messages displayed if initialization fails
- ✅ PCF control builds successfully
- ✅ Test harness shows MSAL initialized without errors

---

## Context

### Why This Task Matters

The PCF control must initialize MSAL during its lifecycle init phase:
1. **Early initialization** - Before user interacts with control
2. **Error handling** - Gracefully handle config/network errors
3. **User feedback** - Show error messages if initialization fails

This completes Phase 1 - MSAL is initialized but not yet acquiring tokens (Phase 2).

### ADR Compliance

**ADR-002 (No Heavy Plugins):**
- ✅ MSAL runs client-side in PCF control
- ✅ No plugins involved

**ADR-006 (Prefer PCF):**
- ✅ Using PCF control lifecycle (init, updateView, destroy)
- ✅ TypeScript/React best practices

---

## Prerequisites

**Before starting:**
- ✅ Task 1.1-1.4 completed (MSAL package installed, provider implemented)
- ✅ Universal Dataset Grid PCF control exists
- ✅ `index.ts` file exists with init() method

**Expected file structure:**
```
src/controls/UniversalDatasetGrid/UniversalDatasetGrid/
├── index.ts                          ← Will modify this
├── services/
│   └── auth/
│       ├── msalConfig.ts             ✅ (Task 1.3)
│       └── MsalAuthProvider.ts       ✅ (Task 1.4)
└── types/
    └── auth.ts                       ✅ (Task 1.2)
```

---

## Step-by-Step Instructions

### Step 1: Locate PCF Control index.ts

**File:** `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/index.ts`

**Check if file exists:**
```bash
cd c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid
ls index.ts
```

**Expected:** File exists (PCF control entry point).

---

### Step 2: Review Current init() Method

**Read current index.ts:**

```bash
# View current init() method
cat index.ts | grep -A 20 "public init"
```

**Expected structure:**
```typescript
export class UniversalDatasetGrid implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  private context: ComponentFramework.Context<IInputs>;
  private container: HTMLDivElement;

  public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): void {
    this.context = context;
    this.container = container;

    // Existing initialization code...
  }

  // ... rest of control implementation
}
```

---

### Step 3: Add MsalAuthProvider Import

**Add to top of index.ts:**

```typescript
import { MsalAuthProvider } from "./services/auth/MsalAuthProvider";
```

**Complete imports section should look like:**
```typescript
import { IInputs, IOutputs } from "./generated/ManifestTypes";
import { MsalAuthProvider } from "./services/auth/MsalAuthProvider";
// ... other imports
```

---

### Step 4: Add MsalAuthProvider Field to Class

**Add private field to class:**

```typescript
export class UniversalDatasetGrid implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  private context: ComponentFramework.Context<IInputs>;
  private container: HTMLDivElement;

  // Add this field:
  /**
   * MSAL authentication provider
   * Handles user token acquisition for Spe.Bff.Api OBO endpoints
   */
  private authProvider: MsalAuthProvider;

  // ... rest of class
}
```

---

### Step 5: Update init() Method with MSAL Initialization

**Modify init() method:**

```typescript
/**
 * Control initialization
 *
 * Lifecycle method called when control is loaded.
 * Initializes MSAL authentication and React UI.
 */
public async init(
  context: ComponentFramework.Context<IInputs>,
  notifyOutputChanged: () => void,
  state: ComponentFramework.Dictionary,
  container: HTMLDivElement
): Promise<void> {
  // Store context and container
  this.context = context;
  this.container = container;

  try {
    // ============================================================================
    // Phase 1: Initialize MSAL Authentication
    // ============================================================================
    console.info("[UniversalDatasetGrid] Initializing MSAL authentication...");

    // Get singleton instance of MsalAuthProvider
    this.authProvider = MsalAuthProvider.getInstance();

    // Initialize MSAL (validates config, creates PublicClientApplication)
    await this.authProvider.initialize();

    console.info("[UniversalDatasetGrid] MSAL authentication initialized successfully ✅");

    // Check if user is authenticated (for logging only - Phase 1)
    const isAuth = this.authProvider.isAuthenticated();
    console.info(`[UniversalDatasetGrid] User authenticated: ${isAuth}`);

    // ============================================================================
    // Phase 2: Initialize React UI (TODO: Phase 2)
    // ============================================================================
    // TODO: In Phase 2, we'll use authProvider.getToken() to acquire tokens
    //       and pass to React components for API calls

    // ... existing control initialization code ...

  } catch (error) {
    // ============================================================================
    // Error Handling
    // ============================================================================
    console.error("[UniversalDatasetGrid] Failed to initialize MSAL:", error);

    // Show user-friendly error message
    this.showError(
      "Authentication initialization failed. Please refresh the page and try again. " +
      "If the problem persists, contact your administrator."
    );
  }
}
```

---

### Step 6: Add Error Display Method

**Add helper method to class:**

```typescript
/**
 * Display error message in control
 *
 * Shows user-friendly error when initialization or operations fail.
 * Used for MSAL errors, API errors, etc.
 *
 * @param message - Error message to display (user-friendly, no technical details)
 */
private showError(message: string): void {
  // Clear container
  this.container.innerHTML = "";

  // Create error div
  const errorDiv = document.createElement("div");
  errorDiv.style.padding = "20px";
  errorDiv.style.color = "#a4262c"; // Office UI Fabric error red
  errorDiv.style.backgroundColor = "#fde7e9"; // Light red background
  errorDiv.style.border = "1px solid #a4262c";
  errorDiv.style.borderRadius = "4px";
  errorDiv.style.fontFamily = "'Segoe UI', Tahoma, Geneva, Verdana, sans-serif";
  errorDiv.style.fontSize = "14px";

  // Add error icon + message
  errorDiv.innerHTML = `
    <div style="display: flex; align-items: center; gap: 10px;">
      <svg width="20" height="20" viewBox="0 0 20 20" fill="none" xmlns="http://www.w3.org/2000/svg">
        <circle cx="10" cy="10" r="9" fill="#a4262c"/>
        <path d="M10 6v4M10 14h.01" stroke="#fff" stroke-width="2" stroke-linecap="round"/>
      </svg>
      <div>
        <strong>Error</strong><br/>
        ${message}
      </div>
    </div>
  `;

  // Add to container
  this.container.appendChild(errorDiv);
}
```

---

### Step 7: Update destroy() Method (Optional Cleanup)

**Add MSAL cleanup to destroy():**

```typescript
/**
 * Control cleanup
 *
 * Lifecycle method called when control is destroyed.
 * Cleans up MSAL resources.
 */
public destroy(): void {
  // Clear MSAL token cache (optional - sessionStorage will be cleared on tab close)
  if (this.authProvider) {
    console.info("[UniversalDatasetGrid] Clearing MSAL token cache");
    this.authProvider.clearCache();
  }

  // ... existing cleanup code ...
}
```

---

### Step 8: Build PCF Control

**Command:**
```bash
cd c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid
npm run build
```

**Expected Output:**
```
> UniversalDatasetGrid@1.0.0 build
> pcf-scripts build

Build started...
[pcf-scripts] Compiling TypeScript...
[pcf-scripts] Bundling...
Build completed successfully.
```

**Verify:**
- ✅ No TypeScript compilation errors
- ✅ No bundling errors
- ✅ `out/` directory created with bundle

---

### Step 9: Test in PCF Test Harness

**Start test harness:**
```bash
npm start watch
```

**Expected Output:**
```
[pcf-scripts] Starting test harness...
[pcf-scripts] Server running at http://localhost:8181
[pcf-scripts] Watching for changes...
```

**Open browser:**
- Navigate to: `http://localhost:8181`
- Open browser DevTools (F12) → Console tab

**Expected console logs:**
```
[UniversalDatasetGrid] Initializing MSAL authentication...
[MSAL] Configuration validated ✅
[MSAL] PublicClientApplication created ✅
[MSAL] No active account found (user not logged in)
[MSAL] Initialization complete ✅
[UniversalDatasetGrid] MSAL authentication initialized successfully ✅
[UniversalDatasetGrid] User authenticated: false
```

**If configuration not set:**
```
[UniversalDatasetGrid] Failed to initialize MSAL: Error: [MSAL Config] CLIENT_ID not set...
```
**Fix:** Update `msalConfig.ts` with actual Azure App Registration values (Task 1.3).

---

## Verification Checklist

**Task 1.5 complete when:**

- ✅ `index.ts` imports `MsalAuthProvider`
- ✅ `authProvider` field added to control class
- ✅ `init()` method updated to initialize MSAL
- ✅ Error handling added with user-friendly messages
- ✅ `showError()` helper method implemented
- ✅ `destroy()` method updated to clear MSAL cache
- ✅ `npm run build` completes without errors
- ✅ PCF test harness (`npm start watch`) shows MSAL initialized successfully
- ✅ Browser console shows MSAL logs without errors

**Quick verification command:**
```bash
cd c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid && \
npm run build && \
echo "✅ Task 1.5 Complete - Build successful"
```

---

## Troubleshooting

### Issue 1: "Cannot find module './services/auth/MsalAuthProvider'"

**Cause:** Import path incorrect or file not created.

**Fix:**
```bash
# Check file exists
ls services/auth/MsalAuthProvider.ts

# If missing, complete Task 1.4 first

# Check import path from index.ts
# index.ts location: src/controls/UniversalDatasetGrid/UniversalDatasetGrid/index.ts
# MsalAuthProvider location: src/controls/.../services/auth/MsalAuthProvider.ts
# Import should be: "./services/auth/MsalAuthProvider"
```

---

### Issue 2: "Property 'init' is not assignable to type 'StandardControl'"

**Cause:** `init()` changed to async but PCF interface expects synchronous.

**Fix:** PCF supports async init in recent versions, but if error persists:
```typescript
// Option 1: Keep async (recommended if PCF version supports)
public async init(...): Promise<void> {
  // ...
}

// Option 2: Use fire-and-forget (if PCF doesn't support async init)
public init(...): void {
  this.initializeAsync().catch(error => {
    console.error("Init failed:", error);
    this.showError("Initialization failed. Please refresh.");
  });
}

private async initializeAsync(): Promise<void> {
  // Move MSAL initialization here
  this.authProvider = MsalAuthProvider.getInstance();
  await this.authProvider.initialize();
}
```

---

### Issue 3: Test harness shows "Authentication initialization failed"

**Cause:** MSAL configuration not set (CLIENT_ID, TENANT_ID, REDIRECT_URI).

**Fix:**
```bash
# Check msalConfig.ts has actual values (not placeholders)
cat services/auth/msalConfig.ts | grep "CLIENT_ID"

# Should show:
const CLIENT_ID = "12345678-1234-1234-1234-123456789abc";  # Real GUID

# If shows:
const CLIENT_ID = "<YOUR_CLIENT_ID>";  # ❌ Placeholder

# Update msalConfig.ts with actual Azure App Registration values (Task 1.3)
```

---

### Issue 4: "User authenticated: false" in console

**Expected behavior:** This is **correct** for Phase 1.

**Explanation:**
- Phase 1: Initialize MSAL (no token acquisition yet)
- User won't be authenticated until Phase 2 when we call `getToken()`
- `isAuthenticated()` returns false because no token acquired yet

**Action:** ✅ No action needed - proceed to Phase 2.

---

## Testing Notes

### What to Test in Phase 1

**✅ Should work:**
- PCF control loads without errors
- MSAL initialization logs appear in console
- No error messages displayed to user
- `npm run build` succeeds

**❌ Not yet implemented (Phase 2):**
- Token acquisition (`getToken()` not implemented yet)
- API calls with Authorization header
- User authentication state changes

### Expected Console Output

**Success case:**
```
[UniversalDatasetGrid] Initializing MSAL authentication...
[MsalAuthProvider] Initializing MSAL...
[MsalAuthProvider] Configuration validated ✅
[MsalAuthProvider] PublicClientApplication created ✅
[MsalAuthProvider] No active account found (user not logged in)
[MsalAuthProvider] Initialization complete ✅
[UniversalDatasetGrid] MSAL authentication initialized successfully ✅
[UniversalDatasetGrid] User authenticated: false
```

**Expected behavior:** No errors, `User authenticated: false` is correct.

---

## Phase 1 Complete! 🎉

**Congratulations!** Phase 1 (MSAL Setup) is now complete:

✅ **Task 1.1** - NPM package installed
✅ **Task 1.2** - Directory structure and TypeScript interfaces created
✅ **Task 1.3** - MSAL configuration file created
✅ **Task 1.4** - MsalAuthProvider class implemented (initialization only)
✅ **Task 1.5** - Integrated with PCF control

**What we have now:**
- MSAL.js installed and configured
- `PublicClientApplication` initialized in PCF control
- Error handling for initialization failures
- Foundation ready for token acquisition (Phase 2)

---

## Next Steps

**After Task 1.5 completion:**

✅ **Phase 1 Complete** - MSAL.js setup and integration

➡️ **Phase 2: Token Acquisition Implementation**
- Implement `getToken()` method with `ssoSilent()`
- Add sessionStorage caching logic
- Implement token expiration checking
- Add token refresh logic
- Fallback to interactive login if SSO fails

**See:** Phase 2 task documents (will be created next)

---

## Files Modified

**Modified:**
- `index.ts` - Added MSAL initialization, error handling, cleanup

**No new files created in this task**

---

## Related Documentation

- [PCF Control Lifecycle](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/control-implementation-library)
- [Sprint 8 MSAL Overview](./SPRINT-8-MSAL-OVERVIEW.md)
- [Sprint 4 Dual Auth Architecture](../../../docs/architecture/ARCHITECTURE-DUAL-AUTH-GRAPH-INTEGRATION.md)

---

**Task Status:** 📋 **READY TO START**
**Next Phase:** Phase 2 - Token Acquisition
**Estimated Duration:** 45 minutes

---
