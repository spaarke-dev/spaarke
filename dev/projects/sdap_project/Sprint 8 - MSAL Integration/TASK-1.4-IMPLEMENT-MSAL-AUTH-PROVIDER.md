# Task 1.4: Implement MsalAuthProvider Class (Initialization Only)

**Sprint:** 8 - MSAL Integration
**Task:** 1.4 of 5 (Phase 1)
**Duration:** 60 minutes
**Status:** 📋 **READY TO START**

---

## Task Goal

> **Create MsalAuthProvider.ts with PublicClientApplication initialization logic (token acquisition in Phase 2).**

**Success Criteria:**
- ✅ `services/auth/MsalAuthProvider.ts` created with singleton pattern
- ✅ `initialize()` method implemented (MSAL setup + redirect handling)
- ✅ `isAuthenticated()` method implemented
- ✅ `getToken()` method stubbed (implementation in Phase 2)
- ✅ TypeScript compiles without errors
- ✅ Class follows IAuthProvider interface

---

## Context

### Why This Task Matters

`MsalAuthProvider` is the core authentication class that:
1. **Initializes MSAL.js** - Creates `PublicClientApplication` instance
2. **Handles redirects** - Processes OAuth redirect responses
3. **Manages accounts** - Tracks authenticated user
4. **Provides tokens** - (Phase 2) Acquires tokens via SSO silent

This task focuses on **initialization only** - token acquisition comes in Phase 2.

### ADR Compliance

**ADR-002 (No Heavy Plugins):**
- ✅ All code runs client-side in PCF control
- ✅ No Dataverse plugins involved

**ADR-007 (Storage Seam Minimalism):**
- ✅ Implements `IAuthProvider` interface (defined in Task 1.2)
- ✅ Singleton pattern (single instance like `SpeFileStore`)
- ✅ No over-abstraction

### Sprint 4 Integration

This class will eventually call Spe.Bff.Api OBO endpoints:

**Flow (Phase 2):**
```
MsalAuthProvider.getToken()
  ↓ ssoSilent() → User token
  ↓ Authorization: Bearer <token>
Spe.Bff.Api OBO endpoints
  ↓ TokenHelper.ExtractBearerToken()
  ↓ OBO flow → Graph token
SharePoint Embedded
```

---

## Prerequisites

**Before starting:**
- ✅ Task 1.1 completed (`@azure/msal-browser` installed)
- ✅ Task 1.2 completed (`types/auth.ts` created with `IAuthProvider`)
- ✅ Task 1.3 completed (`msalConfig.ts` created with validation)

---

## Step-by-Step Instructions

### Step 1: Navigate to PCF Project Directory

**Command:**
```bash
cd c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid
```

---

### Step 2: Create services/auth/MsalAuthProvider.ts

**File:** `services/auth/MsalAuthProvider.ts`

**Content:**

```typescript
import {
  PublicClientApplication,
  AccountInfo,
  AuthenticationResult,
  SilentRequest,
  InteractionRequiredAuthError,
  PopupRequest,
} from "@azure/msal-browser";
import { msalConfig, loginRequest, validateMsalConfig } from "./msalConfig";
import { IAuthProvider, AuthToken, AuthError } from "../../types/auth";

/**
 * MSAL Authentication Provider for Universal Dataset Grid
 *
 * Responsibilities:
 * 1. Initialize PublicClientApplication (MSAL instance)
 * 2. Handle OAuth redirect responses
 * 3. Manage user account state
 * 4. (Phase 2) Acquire user tokens via SSO silent flow
 * 5. (Phase 2) Cache tokens in sessionStorage for performance
 * 6. (Phase 2) Refresh expired tokens automatically
 *
 * ADR Compliance:
 * - ADR-002: Client-side only (no plugins)
 * - ADR-007: Implements IAuthProvider interface (minimal abstraction)
 *
 * Usage:
 *   const authProvider = MsalAuthProvider.getInstance();
 *   await authProvider.initialize();
 *   const isAuth = authProvider.isAuthenticated();
 *
 * Sprint 4 Integration (Phase 2):
 *   const token = await authProvider.getToken(["api://spe-bff-api/user_impersonation"]);
 *   // Use token in Authorization: Bearer header for Spe.Bff.Api calls
 *
 * @see https://learn.microsoft.com/en-us/azure/active-directory/develop/scenario-spa-acquire-token
 */
export class MsalAuthProvider implements IAuthProvider {
  // ============================================================================
  // Private Fields
  // ============================================================================

  /**
   * Singleton instance
   *
   * Only one MsalAuthProvider should exist per PCF control instance.
   * Multiple instances would conflict with MSAL's internal state.
   */
  private static instance: MsalAuthProvider;

  /**
   * MSAL PublicClientApplication instance
   *
   * Core MSAL.js object for authentication operations.
   * Created during initialize(), null before initialization.
   */
  private msalInstance: PublicClientApplication | null = null;

  /**
   * Current authenticated account
   *
   * Represents the signed-in user. Used for token acquisition.
   * null if no user authenticated.
   */
  private currentAccount: AccountInfo | null = null;

  /**
   * Initialization state flag
   *
   * Prevents re-initialization and ensures initialize() called before other methods.
   */
  private isInitialized = false;

  // ============================================================================
  // Constructor (Private - Singleton Pattern)
  // ============================================================================

  /**
   * Private constructor for singleton pattern
   *
   * Use MsalAuthProvider.getInstance() instead of new MsalAuthProvider().
   */
  private constructor() {}

  // ============================================================================
  // Singleton Instance Access
  // ============================================================================

  /**
   * Get singleton instance of MsalAuthProvider
   *
   * Creates instance on first call, returns existing instance on subsequent calls.
   *
   * @returns The singleton MsalAuthProvider instance
   */
  public static getInstance(): MsalAuthProvider {
    if (!MsalAuthProvider.instance) {
      MsalAuthProvider.instance = new MsalAuthProvider();
    }
    return MsalAuthProvider.instance;
  }

  // ============================================================================
  // IAuthProvider Interface Implementation
  // ============================================================================

  /**
   * Initialize MSAL PublicClientApplication
   *
   * Call this once during PCF control initialization (index.ts init() method).
   *
   * Steps:
   * 1. Validate MSAL configuration (fail fast if misconfigured)
   * 2. Create PublicClientApplication instance
   * 3. Handle redirect response (if returning from Azure AD login)
   * 4. Set active account (if user already logged in)
   *
   * Idempotent: Safe to call multiple times (will skip if already initialized).
   *
   * @throws Error if configuration is invalid or initialization fails
   */
  public async initialize(): Promise<void> {
    // Skip if already initialized (idempotent)
    if (this.isInitialized) {
      console.warn("[MsalAuthProvider] Already initialized, skipping");
      return;
    }

    console.info("[MsalAuthProvider] Initializing MSAL...");

    try {
      // Step 1: Validate configuration (throws if invalid)
      validateMsalConfig();
      console.info("[MsalAuthProvider] Configuration validated ✅");

      // Step 2: Create PublicClientApplication instance
      this.msalInstance = new PublicClientApplication(msalConfig);
      console.info("[MsalAuthProvider] PublicClientApplication created ✅");

      // Step 3: Handle redirect response
      // If user was redirected to Azure AD for login and is now returning,
      // this processes the OAuth response and extracts tokens.
      const redirectResponse = await this.msalInstance.handleRedirectPromise();
      if (redirectResponse) {
        console.info(
          "[MsalAuthProvider] Redirect response processed, user authenticated via redirect ✅"
        );
        this.currentAccount = redirectResponse.account;
      }

      // Step 4: Set active account (if user already logged in)
      // If no redirect response, check if user has existing session
      if (!this.currentAccount) {
        const accounts = this.msalInstance.getAllAccounts();
        if (accounts.length > 0) {
          // Use first account (typically only one account for enterprise apps)
          this.currentAccount = accounts[0];
          console.info(
            `[MsalAuthProvider] Active account found: ${this.currentAccount.username} ✅`
          );
        } else {
          console.info("[MsalAuthProvider] No active account found (user not logged in)");
        }
      }

      // Mark as initialized
      this.isInitialized = true;
      console.info("[MsalAuthProvider] Initialization complete ✅");
    } catch (error) {
      console.error("[MsalAuthProvider] Initialization failed ❌", error);
      throw new Error(`MSAL initialization failed: ${error}`);
    }
  }

  /**
   * Check if user is authenticated
   *
   * @returns true if user has active account, false otherwise
   */
  public isAuthenticated(): boolean {
    return this.currentAccount !== null;
  }

  /**
   * Get access token for specified scopes
   *
   * TODO: Implement in Phase 2 (Task 2.1)
   *
   * Phase 2 implementation will:
   * 1. Try SSO silent token acquisition (ssoSilent)
   * 2. If SSO fails with InteractionRequiredAuthError, fallback to popup login
   * 3. Cache token in sessionStorage
   * 4. Return access token string
   *
   * @param scopes - OAuth scopes to request (e.g., ["api://spe-bff-api/user_impersonation"])
   * @returns Access token string
   * @throws Error - Currently throws (not implemented in Phase 1)
   */
  public async getToken(scopes: string[]): Promise<string> {
    if (!this.isInitialized || !this.msalInstance) {
      throw new Error("MSAL not initialized. Call initialize() first.");
    }

    // TODO: Phase 2 implementation
    throw new Error(
      "getToken() not yet implemented. This will be implemented in Phase 2 (Task 2.1)."
    );
  }

  /**
   * Clear token cache and sign out
   *
   * Removes cached tokens from MSAL and sessionStorage.
   * User will need to re-authenticate on next getToken() call.
   */
  public clearCache(): void {
    if (!this.msalInstance) {
      return;
    }

    console.info("[MsalAuthProvider] Clearing token cache");

    // Clear MSAL cache for all accounts
    const accounts = this.msalInstance.getAllAccounts();
    accounts.forEach((account) => {
      // Note: clearCache() is available in MSAL.js but may not be in all versions
      // Alternative: Remove accounts individually
      this.msalInstance?.clearCache(account);
    });

    // Reset current account
    this.currentAccount = null;

    console.info("[MsalAuthProvider] Token cache cleared ✅");
  }

  // ============================================================================
  // Helper Methods (For Debugging/Testing)
  // ============================================================================

  /**
   * Get current account info
   *
   * Returns the currently authenticated account, or null if not authenticated.
   * Useful for debugging and testing.
   *
   * @returns Current account or null
   */
  public getCurrentAccount(): AccountInfo | null {
    return this.currentAccount;
  }

  /**
   * Get initialization state
   *
   * @returns true if initialize() has been called successfully, false otherwise
   */
  public isInitializedState(): boolean {
    return this.isInitialized;
  }

  /**
   * Get account info for logging (sanitized)
   *
   * Returns sanitized account info safe to log (no tokens).
   *
   * @returns Sanitized account info object
   */
  public getAccountDebugInfo(): Record<string, unknown> | null {
    if (!this.currentAccount) {
      return null;
    }

    return {
      username: this.currentAccount.username,
      name: this.currentAccount.name,
      tenantId: this.currentAccount.tenantId,
      homeAccountId: this.currentAccount.homeAccountId,
      environment: this.currentAccount.environment,
    };
  }
}
```

---

### Step 3: Verify TypeScript Compiles

**Command:**
```bash
npx tsc --noEmit
```

**Expected Output:**
```
# No output = success
```

**If errors:**
```
services/auth/MsalAuthProvider.ts(120,18): error TS2339: Property 'clearCache' does not exist on type 'PublicClientApplication'.
```

**Fix:** MSAL v2.38+ should have `clearCache()`. If missing, use alternative:
```typescript
// Alternative if clearCache() not available:
accounts.forEach((account) => {
  this.msalInstance?.logout({ account });
});
```

---

### Step 4: Test Initialization (Manual)

**Create temporary test file:** `test-msal-init.ts`

```typescript
import { MsalAuthProvider } from "./services/auth/MsalAuthProvider";

async function testInit() {
  try {
    const provider = MsalAuthProvider.getInstance();
    console.log("Provider instance created ✅");

    await provider.initialize();
    console.log("Provider initialized ✅");

    console.log("Is authenticated:", provider.isAuthenticated());
    console.log("Account info:", provider.getAccountDebugInfo());
  } catch (error) {
    console.error("Initialization test failed ❌", error);
  }
}

testInit();
```

**Run test:**
```bash
npx ts-node test-msal-init.ts
```

**Expected Output:**
```
Provider instance created ✅
[MsalAuthProvider] Initializing MSAL...
[MsalAuthProvider] Configuration validated ✅
[MsalAuthProvider] PublicClientApplication created ✅
[MsalAuthProvider] No active account found (user not logged in)
[MsalAuthProvider] Initialization complete ✅
Provider initialized ✅
Is authenticated: false
Account info: null
```

**Delete test file:**
```bash
rm test-msal-init.ts
```

---

## Verification Checklist

**Task 1.4 complete when:**

- ✅ File `services/auth/MsalAuthProvider.ts` exists
- ✅ Class implements `IAuthProvider` interface
- ✅ Singleton pattern implemented (`getInstance()`)
- ✅ `initialize()` method implemented with:
  - Configuration validation
  - `PublicClientApplication` creation
  - Redirect response handling
  - Account discovery
- ✅ `isAuthenticated()` method implemented
- ✅ `getToken()` method stubbed (throws "not implemented" error)
- ✅ `clearCache()` method implemented
- ✅ Helper methods: `getCurrentAccount()`, `getAccountDebugInfo()`
- ✅ `npx tsc --noEmit` compiles without errors
- ✅ Manual test shows successful initialization

**Quick verification command:**
```bash
cd c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid && \
test -f services/auth/MsalAuthProvider.ts && \
npx tsc --noEmit && \
echo "✅ Task 1.4 Complete"
```

---

## Troubleshooting

### Issue 1: "Cannot find module '../../types/auth'"

**Cause:** Relative import path incorrect.

**Fix:**
```typescript
// Check file structure:
// services/auth/MsalAuthProvider.ts
// types/auth.ts

// Import should be:
import { IAuthProvider } from "../../types/auth";
// Go up 2 levels (auth → services → root), then into types
```

---

### Issue 2: "Property 'clearCache' does not exist"

**Cause:** MSAL version doesn't have `clearCache()` method.

**Fix:** Use logout instead:
```typescript
public clearCache(): void {
  if (!this.msalInstance) return;

  const accounts = this.msalInstance.getAllAccounts();
  accounts.forEach((account) => {
    this.msalInstance?.logout({ account, onRedirectNavigate: () => false });
  });

  this.currentAccount = null;
}
```

---

### Issue 3: "handleRedirectPromise() never resolves"

**Cause:** Waiting for redirect that isn't coming (browser not redirected).

**Fix:** Add timeout:
```typescript
const redirectResponse = await Promise.race([
  this.msalInstance.handleRedirectPromise(),
  new Promise((resolve) => setTimeout(() => resolve(null), 5000))
]);
```

---

## Next Steps

**After Task 1.4 completion:**

✅ **Task 1.4 Complete** - MsalAuthProvider class created with initialization

➡️ **Task 1.5: Integrate with PCF Control**
- Update `index.ts` to initialize `MsalAuthProvider`
- Add error handling for initialization failures
- Test in PCF test harness

**See:** `TASK-1.5-INTEGRATE-WITH-PCF.md`

---

## Files Created

**Created:**
- `services/auth/MsalAuthProvider.ts` - MSAL authentication provider class

**Modified:**
- None

---

## Related Documentation

- [MSAL.js PublicClientApplication](https://azuread.github.io/microsoft-authentication-library-for-js/ref/msal-browser/classes/PublicClientApplication.html)
- [Sprint 8 MSAL Overview](./SPRINT-8-MSAL-OVERVIEW.md)
- [ADR-007: Storage Seam Minimalism](../../../docs/adr/ADR-007-spe-storage-seam-minimalism.md)

---

**Task Status:** 📋 **READY TO START**
**Next Task:** Task 1.5 - Integrate with PCF Control
**Estimated Duration:** 60 minutes

---
