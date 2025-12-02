# Phase 1: MSAL.js Setup and Configuration

**Sprint:** 8 - MSAL Integration
**Phase:** 1 of 5
**Duration:** 5 days (Week 1)
**Status:** 📋 **READY TO START**

---

## Phase 1 Goal

> **Install @azure/msal-browser NPM package, create MSAL configuration files, and initialize PublicClientApplication in Universal Dataset Grid PCF control.**

**Success Criteria:**
- ✅ `@azure/msal-browser` package installed
- ✅ `msalConfig.ts` created with correct Azure App Registration details
- ✅ `MsalAuthProvider.ts` created with `PublicClientApplication` initialization
- ✅ MSAL initializes without errors in browser console
- ✅ No build errors or TypeScript warnings

---

## Prerequisites

### Required Information

Before starting Phase 1, gather the following from Azure Portal:

**Azure App Registration (SDAP):**
- ✅ **Client ID** (Application ID)
- ✅ **Tenant ID** (Directory ID)
- ✅ **Redirect URI** (Dataverse environment URL)

**Where to find:**
1. Azure Portal → Azure Active Directory → App registrations
2. Search for "SDAP" or "Spaarke" application
3. Copy "Application (client) ID" → This is your **Client ID**
4. Copy "Directory (tenant) ID" → This is your **Tenant ID**
5. Go to "Authentication" tab → Check "Redirect URIs" → Add if missing: `https://<your-env>.dynamics.com`

**API Permissions Required:**
- `api://spe-bff-api/user_impersonation` (Delegated)
- `User.Read` (Microsoft Graph, Delegated)

**If permissions missing:**
1. Azure Portal → App Registration → API permissions
2. Click "Add a permission"
3. Select "APIs my organization uses"
4. Search for "spe-bff-api"
5. Select "Delegated permissions" → Check "user_impersonation"
6. Click "Add permissions"
7. Click "Grant admin consent for <tenant>"

---

## Step-by-Step Implementation

### Step 1: Install @azure/msal-browser Package

**Location:** `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/`

**Command:**
```bash
cd c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid
npm install @azure/msal-browser --save
```

**Expected Output:**
```
+ @azure/msal-browser@2.38.4
added 1 package in 3s
```

**Verify Installation:**
```bash
npm list @azure/msal-browser
```

**Expected:**
```
UniversalDatasetGrid@1.0.0 c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid
└── @azure/msal-browser@2.38.4
```

**Update package.json:**
```json
{
  "dependencies": {
    "@azure/msal-browser": "^2.38.4",
    // ... other dependencies
  }
}
```

---

### Step 2: Create Directory Structure

**Create directories for auth-related files:**

```bash
# Create services/auth directory
mkdir -p src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/auth

# Create types directory (if not exists)
mkdir -p src/controls/UniversalDatasetGrid/UniversalDatasetGrid/types
```

**Expected directory structure:**
```
src/controls/UniversalDatasetGrid/UniversalDatasetGrid/
├── services/
│   ├── auth/           ← NEW
│   │   ├── msalConfig.ts        (Step 3)
│   │   └── MsalAuthProvider.ts  (Step 4)
│   └── fileService.ts   (existing)
├── types/
│   └── auth.ts          ← NEW (Step 5)
└── package.json
```

---

### Step 3: Create msalConfig.ts

**File:** `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/auth/msalConfig.ts`

**Purpose:** Central configuration for MSAL.js (client ID, tenant, scopes)

**Implementation:**

```typescript
import { Configuration, LogLevel } from "@azure/msal-browser";

/**
 * MSAL Configuration for SDAP Universal Dataset Grid
 *
 * This configuration enables SSO silent authentication in Dataverse PCF controls,
 * allowing the control to acquire user tokens and call Spe.Bff.Api OBO endpoints.
 */

// Azure App Registration Details (REPLACE WITH ACTUAL VALUES)
const CLIENT_ID = "<YOUR_CLIENT_ID>"; // From Azure Portal → App Registration
const TENANT_ID = "<YOUR_TENANT_ID>"; // From Azure Portal → App Registration
const REDIRECT_URI = "https://<your-dataverse-env>.dynamics.com"; // Your Dataverse environment

/**
 * MSAL Browser Configuration
 *
 * auth:
 *   - clientId: Azure AD application ID for SDAP
 *   - authority: Azure AD tenant authority URL
 *   - redirectUri: Dataverse environment URL (where user is redirected after login)
 *
 * cache:
 *   - cacheLocation: "sessionStorage" - tokens cleared when browser tab closed
 *   - storeAuthStateInCookie: false - not needed for modern browsers
 *
 * system:
 *   - loggerOptions: MSAL internal logging for debugging
 */
export const msalConfig: Configuration = {
  auth: {
    clientId: CLIENT_ID,
    authority: `https://login.microsoftonline.com/${TENANT_ID}`,
    redirectUri: REDIRECT_URI,
    navigateToLoginRequestUrl: false, // Stay on current page after login
  },
  cache: {
    cacheLocation: "sessionStorage", // Store tokens in sessionStorage (cleared on tab close)
    storeAuthStateInCookie: false,    // Not needed for modern browsers
  },
  system: {
    loggerOptions: {
      loggerCallback: (level, message, containsPii) => {
        // Don't log messages containing PII (personally identifiable information)
        if (containsPii) {
          return;
        }

        // Log MSAL messages to browser console
        switch (level) {
          case LogLevel.Error:
            console.error(`[MSAL] ${message}`);
            break;
          case LogLevel.Warning:
            console.warn(`[MSAL] ${message}`);
            break;
          case LogLevel.Info:
            console.info(`[MSAL] ${message}`);
            break;
          case LogLevel.Verbose:
            console.debug(`[MSAL] ${message}`);
            break;
        }
      },
      logLevel: LogLevel.Warning, // Only log warnings and errors (change to Verbose for debugging)
    },
  },
};

/**
 * Scopes for SSO Silent Token Acquisition
 *
 * These scopes are requested when acquiring tokens via ssoSilent().
 *
 * Required scopes:
 *   - api://spe-bff-api/user_impersonation: Access to Spe.Bff.Api on behalf of user
 *
 * Note: User.Read is typically not needed here since Spe.Bff.Api handles Graph calls.
 *       Only request scopes needed for BFF API access.
 */
export const loginRequest = {
  scopes: ["api://spe-bff-api/user_impersonation"],
  loginHint: undefined as string | undefined, // Set to user email before calling ssoSilent()
};

/**
 * Configuration Validation
 *
 * Throws error if configuration contains placeholder values.
 * Call this during MSAL initialization to catch config errors early.
 */
export function validateMsalConfig(): void {
  if (CLIENT_ID.includes("YOUR_CLIENT_ID")) {
    throw new Error(
      "MSAL Config Error: CLIENT_ID not set. Update msalConfig.ts with actual Azure App Registration Client ID."
    );
  }

  if (TENANT_ID.includes("YOUR_TENANT_ID")) {
    throw new Error(
      "MSAL Config Error: TENANT_ID not set. Update msalConfig.ts with actual Azure AD Tenant ID."
    );
  }

  if (REDIRECT_URI.includes("your-dataverse-env")) {
    throw new Error(
      "MSAL Config Error: REDIRECT_URI not set. Update msalConfig.ts with actual Dataverse environment URL."
    );
  }
}
```

**TODO for Implementation:**
1. Replace `<YOUR_CLIENT_ID>` with actual Client ID from Azure Portal
2. Replace `<YOUR_TENANT_ID>` with actual Tenant ID from Azure Portal
3. Replace `<your-dataverse-env>` with actual Dataverse environment URL (e.g., `https://org12345.crm.dynamics.com`)

**Example with real values:**
```typescript
const CLIENT_ID = "12345678-1234-1234-1234-123456789abc";
const TENANT_ID = "87654321-4321-4321-4321-cba987654321";
const REDIRECT_URI = "https://org12345.crm.dynamics.com";
```

---

### Step 4: Create MsalAuthProvider.ts

**File:** `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/auth/MsalAuthProvider.ts`

**Purpose:** Wrapper around PublicClientApplication with token acquisition logic

**Implementation:**

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
 * 2. Acquire user tokens via SSO silent flow (ssoSilent)
 * 3. Cache tokens in sessionStorage for performance
 * 4. Refresh expired tokens automatically
 * 5. Fallback to interactive login if SSO fails
 *
 * Usage:
 *   const authProvider = MsalAuthProvider.getInstance();
 *   await authProvider.initialize();
 *   const token = await authProvider.getToken(["api://spe-bff-api/user_impersonation"]);
 */
export class MsalAuthProvider implements IAuthProvider {
  private static instance: MsalAuthProvider;
  private msalInstance: PublicClientApplication | null = null;
  private currentAccount: AccountInfo | null = null;
  private isInitialized = false;

  // Private constructor for singleton pattern
  private constructor() {}

  /**
   * Get singleton instance of MsalAuthProvider
   */
  public static getInstance(): MsalAuthProvider {
    if (!MsalAuthProvider.instance) {
      MsalAuthProvider.instance = new MsalAuthProvider();
    }
    return MsalAuthProvider.instance;
  }

  /**
   * Initialize MSAL PublicClientApplication
   *
   * Call this once during PCF control initialization.
   *
   * Steps:
   * 1. Validate MSAL configuration
   * 2. Create PublicClientApplication instance
   * 3. Handle redirect response (if returning from login)
   * 4. Set active account
   *
   * @throws Error if configuration is invalid
   */
  public async initialize(): Promise<void> {
    if (this.isInitialized) {
      console.warn("[MsalAuthProvider] Already initialized, skipping");
      return;
    }

    console.info("[MsalAuthProvider] Initializing MSAL...");

    try {
      // Validate configuration (throws if invalid)
      validateMsalConfig();

      // Create PublicClientApplication instance
      this.msalInstance = new PublicClientApplication(msalConfig);

      // Handle redirect response (if user was redirected after login)
      await this.msalInstance.handleRedirectPromise();

      // Get active account (if user already logged in)
      const accounts = this.msalInstance.getAllAccounts();
      if (accounts.length > 0) {
        this.currentAccount = accounts[0];
        console.info(
          `[MsalAuthProvider] Active account: ${this.currentAccount.username}`
        );
      } else {
        console.info("[MsalAuthProvider] No active account found");
      }

      this.isInitialized = true;
      console.info("[MsalAuthProvider] Initialization complete");
    } catch (error) {
      console.error("[MsalAuthProvider] Initialization failed:", error);
      throw new Error(`MSAL initialization failed: ${error}`);
    }
  }

  /**
   * Check if user is authenticated
   */
  public isAuthenticated(): boolean {
    return this.currentAccount !== null;
  }

  /**
   * Get access token for specified scopes
   *
   * Flow:
   * 1. Check if MSAL initialized
   * 2. Try SSO silent token acquisition (ssoSilent)
   * 3. If SSO fails with InteractionRequiredAuthError, fallback to popup login
   * 4. Cache token in sessionStorage
   * 5. Return access token
   *
   * @param scopes - OAuth scopes to request (e.g., ["api://spe-bff-api/user_impersonation"])
   * @returns Access token string
   * @throws Error if token acquisition fails
   */
  public async getToken(scopes: string[]): Promise<string> {
    if (!this.isInitialized || !this.msalInstance) {
      throw new Error("MSAL not initialized. Call initialize() first.");
    }

    console.info(`[MsalAuthProvider] Acquiring token for scopes: ${scopes.join(", ")}`);

    try {
      // Try SSO silent token acquisition first
      const tokenResponse = await this.acquireTokenSilent(scopes);
      console.info("[MsalAuthProvider] Token acquired successfully (silent)");
      return tokenResponse.accessToken;
    } catch (error) {
      // If SSO silent fails due to interaction required, fallback to popup
      if (error instanceof InteractionRequiredAuthError) {
        console.warn(
          "[MsalAuthProvider] Interaction required, falling back to popup login"
        );
        const tokenResponse = await this.acquireTokenPopup(scopes);
        console.info("[MsalAuthProvider] Token acquired successfully (popup)");
        return tokenResponse.accessToken;
      }

      // Other errors - rethrow
      console.error("[MsalAuthProvider] Token acquisition failed:", error);
      throw new Error(`Failed to acquire token: ${error}`);
    }
  }

  /**
   * Acquire token silently using SSO
   *
   * This is the primary token acquisition method.
   * Uses ssoSilent() to get token without user interaction.
   *
   * @param scopes - OAuth scopes
   * @returns AuthenticationResult with access token
   * @throws InteractionRequiredAuthError if user interaction needed
   */
  private async acquireTokenSilent(scopes: string[]): Promise<AuthenticationResult> {
    if (!this.msalInstance) {
      throw new Error("MSAL instance not initialized");
    }

    // If we have an active account, use acquireTokenSilent
    if (this.currentAccount) {
      const silentRequest: SilentRequest = {
        scopes,
        account: this.currentAccount,
      };

      return await this.msalInstance.acquireTokenSilent(silentRequest);
    }

    // No active account - use ssoSilent (this is the key for Dataverse PCF)
    // ssoSilent attempts to acquire token using existing browser session
    const ssoRequest: SilentRequest = {
      scopes,
      loginHint: loginRequest.loginHint, // User email (if available)
    };

    const tokenResponse = await this.msalInstance.ssoSilent(ssoRequest);

    // Set current account from SSO response
    if (tokenResponse.account) {
      this.currentAccount = tokenResponse.account;
    }

    return tokenResponse;
  }

  /**
   * Acquire token using popup login (fallback)
   *
   * Used when SSO silent fails (e.g., user not logged in, consent required).
   *
   * @param scopes - OAuth scopes
   * @returns AuthenticationResult with access token
   */
  private async acquireTokenPopup(scopes: string[]): Promise<AuthenticationResult> {
    if (!this.msalInstance) {
      throw new Error("MSAL instance not initialized");
    }

    const popupRequest: PopupRequest = {
      scopes,
      loginHint: this.currentAccount?.username,
    };

    const tokenResponse = await this.msalInstance.acquireTokenPopup(popupRequest);

    // Set current account from popup response
    if (tokenResponse.account) {
      this.currentAccount = tokenResponse.account;
    }

    return tokenResponse;
  }

  /**
   * Clear token cache and sign out
   */
  public clearCache(): void {
    if (!this.msalInstance) {
      return;
    }

    console.info("[MsalAuthProvider] Clearing token cache");

    // Clear MSAL cache
    const accounts = this.msalInstance.getAllAccounts();
    accounts.forEach((account) => {
      this.msalInstance?.clearCache(account);
    });

    // Reset current account
    this.currentAccount = null;
  }

  /**
   * Get current account info (for debugging)
   */
  public getCurrentAccount(): AccountInfo | null {
    return this.currentAccount;
  }
}
```

**Key Features:**
- ✅ Singleton pattern (only one MSAL instance)
- ✅ SSO silent token acquisition (`ssoSilent()`)
- ✅ Fallback to popup login if SSO fails
- ✅ Token caching in MSAL (uses sessionStorage)
- ✅ Comprehensive error handling
- ✅ Logging for debugging

---

### Step 5: Create TypeScript Interfaces

**File:** `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/types/auth.ts`

**Purpose:** Type definitions for authentication-related types

**Implementation:**

```typescript
/**
 * Authentication Types for Universal Dataset Grid
 */

/**
 * Access token with metadata
 */
export interface AuthToken {
  /** JWT access token */
  accessToken: string;

  /** Token expiration timestamp */
  expiresOn: Date;

  /** OAuth scopes granted */
  scopes: string[];

  /** Account username (email) */
  account?: string;
}

/**
 * Authentication error information
 */
export interface AuthError {
  /** Error code from MSAL */
  errorCode: string;

  /** Human-readable error message */
  errorMessage: string;

  /** Whether error requires user interaction (popup login) */
  requiresInteraction: boolean;

  /** Original error object */
  originalError?: unknown;
}

/**
 * Authentication provider interface
 *
 * Implemented by MsalAuthProvider
 */
export interface IAuthProvider {
  /**
   * Initialize authentication provider
   * Must be called before any other methods
   */
  initialize(): Promise<void>;

  /**
   * Get access token for specified scopes
   * @param scopes - OAuth scopes (e.g., ["api://spe-bff-api/user_impersonation"])
   * @returns Access token string
   */
  getToken(scopes: string[]): Promise<string>;

  /**
   * Clear token cache and sign out
   */
  clearCache(): void;

  /**
   * Check if user is authenticated
   */
  isAuthenticated(): boolean;
}

/**
 * Token cache entry (sessionStorage)
 */
export interface TokenCacheEntry {
  /** Access token */
  token: string;

  /** Expiration timestamp (Unix epoch milliseconds) */
  expiresAt: number;

  /** OAuth scopes */
  scopes: string[];
}
```

---

### Step 6: Initialize MsalAuthProvider in PCF Control

**File:** `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/index.ts`

**Purpose:** Initialize MSAL during PCF control initialization

**Modification:**

```typescript
import { MsalAuthProvider } from "./services/auth/MsalAuthProvider";

export class UniversalDatasetGrid implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  private context: ComponentFramework.Context<IInputs>;
  private container: HTMLDivElement;
  private authProvider: MsalAuthProvider;

  /**
   * Control initialization
   */
  public async init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): Promise<void> {
    this.context = context;
    this.container = container;

    try {
      // Initialize MSAL authentication provider
      console.info("[UniversalDatasetGrid] Initializing MSAL authentication...");
      this.authProvider = MsalAuthProvider.getInstance();
      await this.authProvider.initialize();
      console.info("[UniversalDatasetGrid] MSAL authentication initialized successfully");

      // Continue with rest of control initialization...
      // (existing code)
    } catch (error) {
      console.error("[UniversalDatasetGrid] Failed to initialize MSAL:", error);
      // Display error to user or fallback gracefully
      this.showError("Authentication initialization failed. Please refresh the page.");
    }
  }

  private showError(message: string): void {
    // Display error message in control
    this.container.innerHTML = `
      <div style="padding: 20px; color: red; border: 1px solid red;">
        <strong>Error:</strong> ${message}
      </div>
    `;
  }

  // ... rest of control implementation
}
```

---

### Step 7: Build and Test

**Build PCF Control:**

```bash
cd c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid
npm run build
```

**Expected Output:**
```
> UniversalDatasetGrid@1.0.0 build
> pcf-scripts build

Build started...
Build completed successfully.
```

**Check for Errors:**
- ✅ No TypeScript compilation errors
- ✅ No missing module errors
- ✅ No dependency errors

**Test in Browser (Dev Mode):**

```bash
npm start watch
```

**Open browser console:**
1. Check for MSAL initialization logs:
   ```
   [MsalAuthProvider] Initializing MSAL...
   [MsalAuthProvider] Initialization complete
   ```

2. Check for configuration errors:
   ```
   [MSAL Config Error] CLIENT_ID not set...  ← Fix this if you see it
   ```

3. Check MSAL browser library loaded:
   ```javascript
   // In browser console:
   window.msal  // Should be defined
   ```

---

## Verification Checklist

### Phase 1 Complete When:

- ✅ `@azure/msal-browser` package installed (check `package.json`)
- ✅ `msalConfig.ts` created with correct Client ID, Tenant ID, Redirect URI
- ✅ `MsalAuthProvider.ts` created with full implementation
- ✅ `types/auth.ts` created with interface definitions
- ✅ `index.ts` updated to initialize `MsalAuthProvider`
- ✅ PCF control builds without errors (`npm run build`)
- ✅ Browser console shows MSAL initialization logs (no errors)
- ✅ No configuration validation errors in console

**Test Command:**
```bash
# Run build
cd c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid
npm run build

# Check for errors
echo $LASTEXITCODE  # Should be 0 (success)
```

---

## Troubleshooting

### Issue 1: "MSAL Config Error: CLIENT_ID not set"

**Cause:** `msalConfig.ts` still has placeholder values

**Fix:**
1. Open `msalConfig.ts`
2. Replace `<YOUR_CLIENT_ID>` with actual Client ID from Azure Portal
3. Replace `<YOUR_TENANT_ID>` with actual Tenant ID
4. Replace `<your-dataverse-env>` with actual Dataverse environment URL
5. Rebuild: `npm run build`

### Issue 2: "Module not found: @azure/msal-browser"

**Cause:** NPM package not installed or `node_modules` not up to date

**Fix:**
```bash
cd c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid
npm install
npm run build
```

### Issue 3: TypeScript compilation errors in MsalAuthProvider.ts

**Cause:** Missing type definitions or incorrect imports

**Fix:**
1. Verify `@azure/msal-browser` is in `package.json`
2. Run `npm install` to ensure all dependencies installed
3. Check imports at top of `MsalAuthProvider.ts`:
   ```typescript
   import { PublicClientApplication, ... } from "@azure/msal-browser";
   ```
4. Rebuild: `npm run build`

### Issue 4: "Cannot read property 'getInstance' of undefined"

**Cause:** `MsalAuthProvider` not exported correctly

**Fix:**
1. Check `MsalAuthProvider.ts` has:
   ```typescript
   export class MsalAuthProvider implements IAuthProvider { ... }
   ```
2. Check `index.ts` import:
   ```typescript
   import { MsalAuthProvider } from "./services/auth/MsalAuthProvider";
   ```

### Issue 5: MSAL initialization hangs or never completes

**Cause:** `handleRedirectPromise()` waiting for redirect that never comes

**Fix:**
1. Set `navigateToLoginRequestUrl: false` in `msalConfig.ts` (already set in template above)
2. Verify redirect URI in Azure Portal matches Dataverse environment URL exactly

---

## Next Steps

**After Phase 1 completion:**

✅ **Phase 1 Complete** - MSAL.js installed and configured

➡️ **Phase 2: Token Acquisition Implementation**
- Implement `ssoSilent()` token acquisition
- Add sessionStorage caching logic
- Implement token expiration checking
- Add token refresh logic
- Test all token acquisition paths

**See:** `PHASE-2-TOKEN-ACQUISITION.md`

---

**Phase 1 Status:** 📋 **READY TO START**
**Next Phase:** Phase 2 - Token Acquisition Implementation
**Estimated Duration:** 5 days (Week 1)

---
