# Task 2.1: Implement SSO Silent Token Acquisition

**Sprint:** 8 - MSAL Integration
**Phase:** 2 - Token Acquisition
**Task:** 2.1 of 3 (Phase 2)
**Duration:** 60 minutes
**Status:** 📋 **READY TO START**

---

## Task Goal

> **Implement getToken() method in MsalAuthProvider with ssoSilent() token acquisition and fallback to popup login.**

**Success Criteria:**
- ✅ `getToken()` method fully implemented (no longer throws "not implemented" error)
- ✅ `acquireTokenSilent()` and `acquireTokenPopup()` helper methods implemented
- ✅ SSO silent flow works without user interaction
- ✅ Fallback to popup login when SSO fails (InteractionRequiredAuthError)
- ✅ Proper error handling and logging
- ✅ TypeScript compiles without errors

---

## Context

### Why This Task Matters

Token acquisition is the **core functionality** of MSAL integration:
1. **SSO Silent** - Acquires tokens without user interaction (best UX)
2. **Popup Fallback** - Shows login popup only when SSO fails
3. **Security** - Tokens used in Authorization header for Spe.Bff.Api calls

This completes the authentication flow started in Phase 1.

### ADR Compliance

**ADR-002 (No Heavy Plugins):**
- ✅ Token acquisition happens client-side (no plugins)
- ✅ No HTTP calls from Dataverse plugins

**ADR-007 (Storage Seam Minimalism):**
- ✅ Follows `IAuthProvider` interface from Task 1.2
- ✅ Matches `SpeFileStore` pattern (single facade, concrete implementation)

### Sprint 4 Integration

From [ARCHITECTURE-DUAL-AUTH-GRAPH-INTEGRATION.md](c:\code_files\spaarke\docs\architecture\ARCHITECTURE-DUAL-AUTH-GRAPH-INTEGRATION.md):

**OBO Flow (lines 569-621):**
```
PCF Control (MSAL.js)
  ↓ acquires token via ssoSilent()  ← THIS TASK
  ↓ sends: Authorization: Bearer <token>
Spe.Bff.Api (Sprint 4 OBO endpoints)
  ↓ TokenHelper.ExtractBearerToken()
  ↓ OBO flow to Graph API
SharePoint Embedded
```

**TokenHelper.ExtractBearerToken()** (lines 472-500) expects:
```http
Authorization: Bearer <user-token>
```

**This task:** Implement the "acquires token via ssoSilent()" step.

---

## Prerequisites

**Before starting:**
- ✅ Phase 1 (Tasks 1.1-1.5) completed
- ✅ `MsalAuthProvider.initialize()` working
- ✅ `getToken()` currently throws "not implemented" error

**Expected state:**
- `services/auth/MsalAuthProvider.ts` exists with stubbed `getToken()` method
- `msalInstance` initialized in `initialize()` method
- `currentAccount` tracked in class

---

## Step-by-Step Instructions

### Step 1: Navigate to MsalAuthProvider.ts

**File:** `services/auth/MsalAuthProvider.ts`

**Location:**
```bash
cd c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid
code services/auth/MsalAuthProvider.ts
```

---

### Step 2: Implement getToken() Method

**Find the stubbed method:**
```typescript
public async getToken(scopes: string[]): Promise<string> {
  if (!this.isInitialized || !this.msalInstance) {
    throw new Error("MSAL not initialized. Call initialize() first.");
  }

  // TODO: Phase 2 implementation
  throw new Error(
    "getToken() not yet implemented. This will be implemented in Phase 2 (Task 2.1)."
  );
}
```

**Replace with full implementation:**

```typescript
/**
 * Get access token for specified scopes
 *
 * Attempts silent token acquisition first (SSO), falls back to popup if needed.
 *
 * Flow:
 * 1. Check if MSAL initialized
 * 2. Try SSO silent token acquisition (acquireTokenSilent or ssoSilent)
 * 3. If SSO fails with InteractionRequiredAuthError, fallback to popup login
 * 4. Return access token string
 *
 * The acquired token is used in Authorization: Bearer header for Spe.Bff.Api calls.
 *
 * @param scopes - OAuth scopes to request (e.g., ["api://spe-bff-api/user_impersonation"])
 * @returns Access token string for use in Authorization header
 * @throws Error if token acquisition fails after all retry attempts
 */
public async getToken(scopes: string[]): Promise<string> {
  // Validate initialization
  if (!this.isInitialized || !this.msalInstance) {
    throw new Error("MSAL not initialized. Call initialize() first.");
  }

  // Validate scopes parameter
  if (!scopes || scopes.length === 0) {
    throw new Error("Scopes parameter is required and must not be empty.");
  }

  console.info(`[MsalAuthProvider] Acquiring token for scopes: ${scopes.join(", ")}`);

  try {
    // ========================================================================
    // Step 1: Try SSO Silent Token Acquisition
    // ========================================================================
    // This is the preferred method - acquires token without user interaction
    // Uses existing browser session or cached tokens

    const tokenResponse = await this.acquireTokenSilent(scopes);

    console.info("[MsalAuthProvider] Token acquired successfully via silent flow ✅");
    console.debug("[MsalAuthProvider] Token expires:", tokenResponse.expiresOn);

    return tokenResponse.accessToken;

  } catch (error) {
    // ========================================================================
    // Step 2: Handle InteractionRequiredAuthError (Fallback to Popup)
    // ========================================================================
    // If SSO silent fails because user interaction required (consent, MFA, etc.),
    // fall back to popup login

    if (error instanceof InteractionRequiredAuthError) {
      console.warn(
        "[MsalAuthProvider] Silent token acquisition failed, user interaction required. " +
        "Falling back to popup login..."
      );

      try {
        const tokenResponse = await this.acquireTokenPopup(scopes);

        console.info("[MsalAuthProvider] Token acquired successfully via popup ✅");
        console.debug("[MsalAuthProvider] Token expires:", tokenResponse.expiresOn);

        return tokenResponse.accessToken;

      } catch (popupError) {
        console.error("[MsalAuthProvider] Popup token acquisition failed ❌", popupError);
        throw new Error(
          `Failed to acquire token via popup: ${popupError instanceof Error ? popupError.message : "Unknown error"}`
        );
      }
    }

    // ========================================================================
    // Step 3: Handle Other Errors
    // ========================================================================
    console.error("[MsalAuthProvider] Token acquisition failed ❌", error);
    throw new Error(
      `Failed to acquire token: ${error instanceof Error ? error.message : "Unknown error"}`
    );
  }
}
```

---

### Step 3: Implement acquireTokenSilent() Helper Method

**Add private method after getToken():**

```typescript
/**
 * Acquire token silently using SSO
 *
 * This is the primary token acquisition method.
 * Attempts to get token without user interaction.
 *
 * Flow:
 * 1. If currentAccount exists, use acquireTokenSilent (with account hint)
 * 2. If no currentAccount, use ssoSilent (discovers account from browser session)
 * 3. Update currentAccount if token acquired via ssoSilent
 *
 * @param scopes - OAuth scopes to request
 * @returns AuthenticationResult with access token and metadata
 * @throws InteractionRequiredAuthError if user interaction needed (consent, MFA, login)
 * @throws Error for other token acquisition failures
 */
private async acquireTokenSilent(scopes: string[]): Promise<AuthenticationResult> {
  if (!this.msalInstance) {
    throw new Error("MSAL instance not initialized");
  }

  // ========================================================================
  // Option 1: acquireTokenSilent (if we have an active account)
  // ========================================================================
  // If user previously authenticated and we have their account info,
  // use acquireTokenSilent with account parameter for best performance

  if (this.currentAccount) {
    console.debug(
      `[MsalAuthProvider] Using acquireTokenSilent with account: ${this.currentAccount.username}`
    );

    const silentRequest: SilentRequest = {
      scopes,
      account: this.currentAccount,
    };

    try {
      const tokenResponse = await this.msalInstance.acquireTokenSilent(silentRequest);
      console.debug("[MsalAuthProvider] acquireTokenSilent succeeded ✅");
      return tokenResponse;

    } catch (error) {
      // If acquireTokenSilent fails, fall through to ssoSilent as backup
      console.debug(
        "[MsalAuthProvider] acquireTokenSilent failed, trying ssoSilent as fallback"
      );
      // Continue to ssoSilent below
    }
  }

  // ========================================================================
  // Option 2: ssoSilent (discover account from browser session)
  // ========================================================================
  // If no currentAccount or acquireTokenSilent failed, use ssoSilent.
  // This attempts to acquire token using existing browser session
  // (user already logged into Model-driven apps, so session exists)

  console.debug("[MsalAuthProvider] Using ssoSilent to discover account from browser session");

  const ssoRequest: SilentRequest = {
    scopes,
    loginHint: loginRequest.loginHint, // Optional: user email if known
  };

  const tokenResponse = await this.msalInstance.ssoSilent(ssoRequest);

  // Update currentAccount from SSO response
  if (tokenResponse.account) {
    this.currentAccount = tokenResponse.account;
    console.debug(
      `[MsalAuthProvider] Account discovered via ssoSilent: ${this.currentAccount.username} ✅`
    );
  }

  console.debug("[MsalAuthProvider] ssoSilent succeeded ✅");
  return tokenResponse;
}
```

---

### Step 4: Implement acquireTokenPopup() Helper Method

**Add private method after acquireTokenSilent():**

```typescript
/**
 * Acquire token using popup login (fallback)
 *
 * Used when SSO silent fails (e.g., user not logged in, consent required, MFA).
 * Opens popup window for user authentication.
 *
 * @param scopes - OAuth scopes to request
 * @returns AuthenticationResult with access token and metadata
 * @throws Error if popup blocked, user cancels, or authentication fails
 */
private async acquireTokenPopup(scopes: string[]): Promise<AuthenticationResult> {
  if (!this.msalInstance) {
    throw new Error("MSAL instance not initialized");
  }

  console.info("[MsalAuthProvider] Opening popup for user authentication...");

  const popupRequest: PopupRequest = {
    scopes,
    loginHint: this.currentAccount?.username, // Pre-fill email if known
  };

  try {
    const tokenResponse = await this.msalInstance.acquireTokenPopup(popupRequest);

    // Update currentAccount from popup response
    if (tokenResponse.account) {
      this.currentAccount = tokenResponse.account;
      console.info(
        `[MsalAuthProvider] User authenticated via popup: ${this.currentAccount.username} ✅`
      );
    }

    return tokenResponse;

  } catch (error) {
    // Handle specific popup errors
    if (error instanceof Error) {
      // User closed popup without completing authentication
      if (error.message.includes("user_cancelled") || error.message.includes("popup_window_closed")) {
        console.warn("[MsalAuthProvider] User cancelled popup authentication");
        throw new Error("Authentication cancelled by user");
      }

      // Popup blocked by browser
      if (error.message.includes("popup_window_error") || error.message.includes("BrowserAuthError")) {
        console.error("[MsalAuthProvider] Popup window blocked by browser");
        throw new Error(
          "Popup blocked. Please allow popups for this site and try again."
        );
      }
    }

    // Other errors
    console.error("[MsalAuthProvider] Popup authentication failed", error);
    throw error;
  }
}
```

---

### Step 5: Add Import for SilentRequest Type

**Verify imports at top of file include:**

```typescript
import {
  PublicClientApplication,
  AccountInfo,
  AuthenticationResult,
  SilentRequest,              // ✅ Already imported in Task 1.4
  InteractionRequiredAuthError, // ✅ Already imported in Task 1.4
  PopupRequest,               // ✅ Already imported in Task 1.4
} from "@azure/msal-browser";
```

---

### Step 6: Build and Test

**Build PCF control:**
```bash
cd c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid
npm run build
```

**Expected Output:**
```
Build completed successfully.
```

**Start test harness:**
```bash
npm start watch
```

**Test in browser console:**
```javascript
// Assuming control is loaded and visible in test harness

// Test token acquisition
(async () => {
  try {
    const token = await window.msalProvider.getToken(["api://spe-bff-api/user_impersonation"]);
    console.log("✅ Token acquired:", token.substring(0, 20) + "...");
  } catch (error) {
    console.error("❌ Token acquisition failed:", error);
  }
})();
```

**Note:** `window.msalProvider` won't be available by default. In Task 2.2, we'll add proper token caching and testing utilities.

---

## Verification Checklist

**Task 2.1 complete when:**

- ✅ `getToken()` method fully implemented (no "not implemented" error)
- ✅ `acquireTokenSilent()` private method implemented with:
  - Logic for using `acquireTokenSilent` with currentAccount
  - Fallback to `ssoSilent` when no account
  - Account discovery and update
- ✅ `acquireTokenPopup()` private method implemented with:
  - Popup request configuration
  - Account update from response
  - Error handling for cancelled/blocked popups
- ✅ `getToken()` method includes:
  - Initialization check
  - Scopes validation
  - Try silent flow first
  - Catch `InteractionRequiredAuthError` and fallback to popup
  - Comprehensive logging
- ✅ `npm run build` compiles without errors
- ✅ No TypeScript type errors

**Quick verification command:**
```bash
cd c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid && \
npx tsc --noEmit && \
npm run build && \
echo "✅ Task 2.1 Complete"
```

---

## Troubleshooting

### Issue 1: "InteractionRequiredAuthError is not defined"

**Cause:** Missing import from `@azure/msal-browser`.

**Fix:**
```typescript
import {
  PublicClientApplication,
  InteractionRequiredAuthError, // ✅ Add this
  // ... other imports
} from "@azure/msal-browser";
```

---

### Issue 2: "ssoSilent is not a function"

**Cause:** MSAL version doesn't have `ssoSilent()` method (too old).

**Fix:**
```bash
# Check MSAL version
npm list @azure/msal-browser
# Should be 2.38.x or higher

# If older, update:
npm install @azure/msal-browser@latest --save
npm run build
```

**Alternative:** Use only `acquireTokenSilent()` (skip `ssoSilent()`):
```typescript
// If ssoSilent not available, use acquireTokenSilent without account
const silentRequest: SilentRequest = {
  scopes,
  loginHint: loginRequest.loginHint,
};
const tokenResponse = await this.msalInstance.acquireTokenSilent(silentRequest);
```

---

### Issue 3: "Popup blocked" error

**Expected behavior:** Modern browsers block popups by default.

**User action required:**
1. Browser shows "Popup blocked" icon in address bar
2. User clicks icon → "Always allow popups from this site"
3. User retries operation

**For development:** Disable popup blocker in browser settings.

---

### Issue 4: Silent token acquisition always fails

**Possible causes:**

1. **User not logged into Dataverse**
   - **Fix:** User must be authenticated to Model-driven app first
   - ssoSilent leverages existing Dataverse session

2. **Wrong scopes configured**
   - **Fix:** Verify `msalConfig.ts` has correct scope:
     ```typescript
     scopes: ["api://spe-bff-api/user_impersonation"]
     ```

3. **Missing consent**
   - **Fix:** Admin must grant consent for delegated permissions in Azure Portal
   - Azure Portal → App Registration → API permissions → Grant admin consent

4. **REDIRECT_URI mismatch**
   - **Fix:** `msalConfig.ts` REDIRECT_URI must exactly match Azure App Registration
   - Check: Azure Portal → App Registration → Authentication → Redirect URIs

---

## Testing Scenarios

### Scenario 1: SSO Silent Success (Expected Flow)

**Precondition:** User already logged into Dataverse Model-driven app

**Steps:**
1. Load PCF control
2. Call `getToken()`

**Expected:**
```
[MsalAuthProvider] Acquiring token for scopes: api://spe-bff-api/user_impersonation
[MsalAuthProvider] Using ssoSilent to discover account from browser session
[MsalAuthProvider] Account discovered via ssoSilent: alice@contoso.com ✅
[MsalAuthProvider] ssoSilent succeeded ✅
[MsalAuthProvider] Token acquired successfully via silent flow ✅
```

**Result:** Token returned, no popup shown ✅

---

### Scenario 2: SSO Fails, Popup Fallback

**Precondition:** User not logged in, or consent required

**Steps:**
1. Load PCF control
2. Call `getToken()`

**Expected:**
```
[MsalAuthProvider] Acquiring token for scopes: api://spe-bff-api/user_impersonation
[MsalAuthProvider] Using ssoSilent to discover account from browser session
[MsalAuthProvider] Silent token acquisition failed, user interaction required. Falling back to popup login...
[MsalAuthProvider] Opening popup for user authentication...
[MsalAuthProvider] User authenticated via popup: alice@contoso.com ✅
[MsalAuthProvider] Token acquired successfully via popup ✅
```

**Result:** Popup shown, user logs in, token returned ✅

---

### Scenario 3: User Cancels Popup

**Precondition:** Popup login required

**Steps:**
1. Load PCF control
2. Call `getToken()`
3. User closes popup without logging in

**Expected:**
```
[MsalAuthProvider] Opening popup for user authentication...
[MsalAuthProvider] User cancelled popup authentication
```

**Result:** Error thrown: "Authentication cancelled by user" ❌

---

## Next Steps

**After Task 2.1 completion:**

✅ **Task 2.1 Complete** - Token acquisition with ssoSilent and popup fallback implemented

➡️ **Task 2.2: Implement Token Caching in sessionStorage**
- Add sessionStorage caching layer
- Implement token expiration checking
- Proactive token refresh (before expiration)
- Cache key management

**See:** `TASK-2.2-IMPLEMENT-TOKEN-CACHING.md`

---

## Files Modified

**Modified:**
- `services/auth/MsalAuthProvider.ts` - Implemented `getToken()`, `acquireTokenSilent()`, `acquireTokenPopup()`

---

## Related Documentation

- [MSAL.js Token Acquisition](https://learn.microsoft.com/en-us/azure/active-directory/develop/scenario-spa-acquire-token)
- [ssoSilent Documentation](https://azuread.github.io/microsoft-authentication-library-for-js/ref/msal-browser/classes/PublicClientApplication.html#ssoSilent)
- [Sprint 4 OBO Flow](../../../docs/architecture/ARCHITECTURE-DUAL-AUTH-GRAPH-INTEGRATION.md#flow-2-on-behalf-of-user-context)

---

**Task Status:** 📋 **READY TO START**
**Next Task:** Task 2.2 - Token Caching
**Estimated Duration:** 60 minutes

---
