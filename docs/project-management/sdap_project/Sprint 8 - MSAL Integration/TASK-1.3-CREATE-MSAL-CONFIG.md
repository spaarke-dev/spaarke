# Task 1.3: Create MSAL Configuration File

**Sprint:** 8 - MSAL Integration
**Task:** 1.3 of 5 (Phase 1)
**Duration:** 45 minutes
**Status:** 📋 **READY TO START**

---

## Task Goal

> **Create msalConfig.ts with MSAL PublicClientApplication configuration and validation logic.**

**Success Criteria:**
- ✅ `services/auth/msalConfig.ts` created with complete MSAL configuration
- ✅ Azure App Registration details configured (Client ID, Tenant ID, Redirect URI)
- ✅ Configuration validation function implemented
- ✅ TypeScript compiles without errors
- ✅ Configuration follows MSAL.js best practices

---

## Context

### Why This Task Matters

MSAL.js requires configuration to connect to Azure AD:
1. **Client ID** - Identifies SDAP application in Azure AD
2. **Tenant ID** - Specifies which Azure AD tenant to use
3. **Redirect URI** - Where to redirect after authentication
4. **Scopes** - What permissions to request (Spe.Bff.Api access)

This configuration is centralized in one file for maintainability.

### ADR Compliance

**ADR-002 (No Heavy Plugins):**
- ✅ Configuration runs client-side (no plugins)

**ADR-007 (Storage Seam Minimalism):**
- ✅ Single configuration file (no over-abstraction)
- ✅ Scopes match existing `Spe.Bff.Api` OBO endpoints

### Sprint 4 Integration

From [ARCHITECTURE-DUAL-AUTH-GRAPH-INTEGRATION.md](c:\code_files\spaarke\docs\architecture\ARCHITECTURE-DUAL-AUTH-GRAPH-INTEGRATION.md):

**Spe.Bff.Api expects:**
```http
Authorization: Bearer <user-token>
```

**TokenHelper.ExtractBearerToken()** validates this header format.

**This task:** Configure MSAL to acquire tokens for `api://spe-bff-api/user_impersonation` scope.

---

## Prerequisites

**Before starting:**
- ✅ Task 1.1 completed (`@azure/msal-browser` installed)
- ✅ Task 1.2 completed (`services/auth/` directory exists, `types/auth.ts` created)
- ✅ Azure App Registration details available (see below)

### Required Azure App Registration Details

**Where to find these:**

1. **Azure Portal** → **Azure Active Directory** → **App registrations**
2. Search for "SDAP" or "Spaarke" application
3. Copy these values:

| Field | Azure Portal Location | Example |
|-------|----------------------|---------|
| **Client ID** | Overview → Application (client) ID | `12345678-1234-1234-1234-123456789abc` |
| **Tenant ID** | Overview → Directory (tenant) ID | `87654321-4321-4321-4321-cba987654321` |
| **Redirect URI** | Authentication → Redirect URIs | `https://org12345.crm.dynamics.com` |

**API Permissions Required:**
- `api://spe-bff-api/user_impersonation` (Delegated)
- `User.Read` (Microsoft Graph, Delegated) - optional

**If permissions missing:**
1. Azure Portal → App Registration → **API permissions**
2. Click **Add a permission** → **APIs my organization uses**
3. Search for "spe-bff-api" → Select → **Delegated permissions**
4. Check `user_impersonation` → **Add permissions**
5. Click **Grant admin consent for <tenant>**

---

## Step-by-Step Instructions

### Step 1: Navigate to PCF Project Directory

**Command:**
```bash
cd c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid
```

---

### Step 2: Create services/auth/msalConfig.ts

**File:** `services/auth/msalConfig.ts`

**Content:**

```typescript
import { Configuration, LogLevel } from "@azure/msal-browser";

/**
 * MSAL Configuration for SDAP Universal Dataset Grid
 *
 * This configuration enables SSO silent authentication in Dataverse PCF controls,
 * allowing the control to acquire user tokens and call Spe.Bff.Api OBO endpoints.
 *
 * ADR Compliance:
 * - ADR-002: Client-side authentication (no plugins)
 * - ADR-007: Single configuration file (no abstraction layers)
 *
 * Sprint 4 Integration:
 * - Acquires tokens for api://spe-bff-api/user_impersonation scope
 * - Tokens sent to Spe.Bff.Api OBO endpoints via Authorization header
 * - TokenHelper.ExtractBearerToken() validates header format
 *
 * @see https://learn.microsoft.com/en-us/azure/active-directory/develop/msal-js-initializing-client-applications
 */

// ============================================================================
// Azure App Registration Configuration
// ============================================================================
// TODO: Replace these placeholder values with actual Azure App Registration details
// from Azure Portal → App registrations → SDAP application

/**
 * Azure AD Application (Client) ID
 *
 * Where to find:
 * Azure Portal → Azure Active Directory → App registrations → SDAP → Overview → Application (client) ID
 *
 * Example: "12345678-1234-1234-1234-123456789abc"
 */
const CLIENT_ID = "<YOUR_CLIENT_ID>";

/**
 * Azure AD Tenant (Directory) ID
 *
 * Where to find:
 * Azure Portal → Azure Active Directory → App registrations → SDAP → Overview → Directory (tenant) ID
 *
 * Example: "87654321-4321-4321-4321-cba987654321"
 */
const TENANT_ID = "<YOUR_TENANT_ID>";

/**
 * Dataverse Environment Redirect URI
 *
 * Where to find:
 * Azure Portal → App registrations → SDAP → Authentication → Redirect URIs
 *
 * Format: https://<your-org>.crm.dynamics.com (or .crm2, .crm3, etc. depending on region)
 *
 * Example: "https://org12345.crm.dynamics.com"
 *
 * Note: This MUST match a redirect URI configured in Azure App Registration,
 *       otherwise authentication will fail with "redirect_uri_mismatch" error.
 */
const REDIRECT_URI = "https://<your-org>.crm.dynamics.com";

// ============================================================================
// MSAL Browser Configuration
// ============================================================================

/**
 * MSAL PublicClientApplication Configuration
 *
 * Configuration structure:
 * - auth: Azure AD authentication settings
 * - cache: Token cache settings (sessionStorage for PCF controls)
 * - system: Logging and telemetry settings
 *
 * @see https://azuread.github.io/microsoft-authentication-library-for-js/ref/msal-browser/interfaces/Configuration.html
 */
export const msalConfig: Configuration = {
  auth: {
    /**
     * Client ID from Azure App Registration
     * Identifies this application to Azure AD
     */
    clientId: CLIENT_ID,

    /**
     * Tenant-specific authority URL
     *
     * Format: https://login.microsoftonline.com/{tenantId}
     *
     * Why tenant-specific vs /common?
     * - Tenant-specific: Only allows users from specified tenant (more secure)
     * - /common: Allows any Azure AD user (not recommended for enterprise apps)
     *
     * SDAP uses tenant-specific for security.
     */
    authority: `https://login.microsoftonline.com/${TENANT_ID}`,

    /**
     * Redirect URI after authentication
     *
     * After user authenticates (popup or redirect), Azure AD redirects to this URI.
     * For PCF controls in Dataverse, this is the Dataverse environment URL.
     *
     * Important: Must match Azure App Registration → Authentication → Redirect URIs
     */
    redirectUri: REDIRECT_URI,

    /**
     * Whether to navigate to original request URL after authentication
     *
     * false: Stay on current page after login (recommended for PCF controls)
     * true: Navigate to loginRequestUrl (not needed for PCF)
     */
    navigateToLoginRequestUrl: false,
  },

  cache: {
    /**
     * Token cache location
     *
     * Options:
     * - "sessionStorage": Tokens cleared when browser tab closed (recommended for PCF)
     * - "localStorage": Tokens persist across browser sessions (less secure)
     * - "memoryStorage": Tokens only in memory (lost on page refresh)
     *
     * SDAP uses sessionStorage for security (tokens cleared on tab close).
     */
    cacheLocation: "sessionStorage",

    /**
     * Whether to store auth state in cookie
     *
     * false: Store in sessionStorage only (recommended for modern browsers)
     * true: Also store in cookie (needed for IE11, not supported in PCF)
     */
    storeAuthStateInCookie: false,
  },

  system: {
    /**
     * MSAL logging configuration
     *
     * Logs internal MSAL operations to browser console for debugging.
     */
    loggerOptions: {
      /**
       * Logger callback function
       *
       * @param level - Log level (Error, Warning, Info, Verbose)
       * @param message - Log message
       * @param containsPii - Whether message contains PII (personally identifiable information)
       */
      loggerCallback: (level, message, containsPii) => {
        // Don't log messages containing PII (email, name, etc.)
        if (containsPii) {
          return;
        }

        // Route to appropriate console method based on log level
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

      /**
       * Minimum log level to output
       *
       * Options:
       * - LogLevel.Error: Only errors
       * - LogLevel.Warning: Warnings and errors (recommended for production)
       * - LogLevel.Info: Info, warnings, and errors
       * - LogLevel.Verbose: All logs (use for debugging only)
       *
       * SDAP uses Warning for production (change to Verbose for debugging).
       */
      logLevel: LogLevel.Warning,
    },
  },
};

// ============================================================================
// OAuth Scopes Configuration
// ============================================================================

/**
 * OAuth scopes for SSO Silent Token Acquisition
 *
 * Scopes define what permissions the token grants.
 *
 * Required scope for SDAP:
 * - api://spe-bff-api/user_impersonation: Access Spe.Bff.Api on behalf of user
 *
 * Why not User.Read?
 * - Spe.Bff.Api performs OBO flow to get Graph token for user
 * - PCF doesn't need direct Graph access (BFF handles it)
 * - Requesting only needed scope follows principle of least privilege
 *
 * Scope format:
 * - api://<application-id-or-name>/<permission-name>
 * - Example: api://12345678-1234-1234-1234-123456789abc/user_impersonation
 *   OR: api://spe-bff-api/user_impersonation (if friendly name configured)
 */
export const loginRequest = {
  /**
   * Scopes to request when acquiring token
   *
   * Array of scope strings. Multiple scopes can be requested:
   * scopes: ["api://app1/scope1", "api://app2/scope2"]
   *
   * SDAP requests single scope for Spe.Bff.Api access.
   */
  scopes: ["api://spe-bff-api/user_impersonation"],

  /**
   * Login hint (user email)
   *
   * When calling ssoSilent(), this can be set to user's email to skip account picker.
   * Set dynamically in MsalAuthProvider based on current Dataverse user.
   *
   * Example: "alice@contoso.com"
   */
  loginHint: undefined as string | undefined,
};

// ============================================================================
// Configuration Validation
// ============================================================================

/**
 * Validate MSAL configuration before initialization
 *
 * Checks that all required configuration values are set (not placeholder values).
 * Throws descriptive error if configuration invalid.
 *
 * Call this in MsalAuthProvider.initialize() to fail fast on misconfiguration.
 *
 * @throws Error if CLIENT_ID, TENANT_ID, or REDIRECT_URI contain placeholder values
 */
export function validateMsalConfig(): void {
  // Check CLIENT_ID
  if (!CLIENT_ID || CLIENT_ID.includes("YOUR_CLIENT_ID")) {
    throw new Error(
      "[MSAL Config] CLIENT_ID not set. " +
        "Update msalConfig.ts with actual Azure App Registration Client ID. " +
        "Find at: Azure Portal → App registrations → SDAP → Overview → Application (client) ID"
    );
  }

  // Check TENANT_ID
  if (!TENANT_ID || TENANT_ID.includes("YOUR_TENANT_ID")) {
    throw new Error(
      "[MSAL Config] TENANT_ID not set. " +
        "Update msalConfig.ts with actual Azure AD Tenant ID. " +
        "Find at: Azure Portal → App registrations → SDAP → Overview → Directory (tenant) ID"
    );
  }

  // Check REDIRECT_URI
  if (!REDIRECT_URI || REDIRECT_URI.includes("your-org")) {
    throw new Error(
      "[MSAL Config] REDIRECT_URI not set. " +
        "Update msalConfig.ts with actual Dataverse environment URL. " +
        "Format: https://<your-org>.crm.dynamics.com " +
        "Must match: Azure Portal → App registrations → SDAP → Authentication → Redirect URIs"
    );
  }

  // Validate GUID format for CLIENT_ID and TENANT_ID
  const guidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

  if (!guidRegex.test(CLIENT_ID)) {
    throw new Error(
      `[MSAL Config] CLIENT_ID has invalid GUID format: "${CLIENT_ID}". ` +
        "Expected format: 12345678-1234-1234-1234-123456789abc"
    );
  }

  if (!guidRegex.test(TENANT_ID)) {
    throw new Error(
      `[MSAL Config] TENANT_ID has invalid GUID format: "${TENANT_ID}". ` +
        "Expected format: 12345678-1234-1234-1234-123456789abc"
    );
  }

  // Validate REDIRECT_URI format
  if (!REDIRECT_URI.startsWith("https://") || !REDIRECT_URI.includes(".dynamics.com")) {
    throw new Error(
      `[MSAL Config] REDIRECT_URI has invalid format: "${REDIRECT_URI}". ` +
        "Expected format: https://<org>.crm.dynamics.com (or .crm2, .crm3, etc.)"
    );
  }

  console.info("[MSAL Config] Configuration validation passed ✅");
}

/**
 * Get current MSAL configuration (for debugging)
 *
 * Returns sanitized configuration with sensitive values masked.
 * Safe to log or display in UI.
 *
 * @returns Sanitized configuration object
 */
export function getMsalConfigDebugInfo(): Record<string, unknown> {
  return {
    clientId: CLIENT_ID.replace(/./g, "*").slice(0, 8) + "...", // Mask client ID
    tenantId: TENANT_ID.replace(/./g, "*").slice(0, 8) + "...", // Mask tenant ID
    redirectUri: REDIRECT_URI,
    authority: msalConfig.auth.authority,
    cacheLocation: msalConfig.cache.cacheLocation,
    scopes: loginRequest.scopes,
  };
}
```

---

### Step 3: Update Configuration with Actual Values

**⚠️ IMPORTANT:** Replace placeholder values with actual Azure App Registration details.

**Find values in Azure Portal:**

1. Navigate to: **Azure Portal → Azure Active Directory → App registrations → SDAP**

2. Copy **Client ID**:
   - Location: Overview → Application (client) ID
   - Example: `12345678-1234-1234-1234-123456789abc`

3. Copy **Tenant ID**:
   - Location: Overview → Directory (tenant) ID
   - Example: `87654321-4321-4321-4321-cba987654321`

4. Copy **Redirect URI**:
   - Location: Authentication → Redirect URIs
   - Example: `https://org12345.crm.dynamics.com`

**Update msalConfig.ts:**

```typescript
// Replace these lines:
const CLIENT_ID = "<YOUR_CLIENT_ID>";
const TENANT_ID = "<YOUR_TENANT_ID>";
const REDIRECT_URI = "https://<your-org>.crm.dynamics.com";

// With actual values:
const CLIENT_ID = "12345678-1234-1234-1234-123456789abc";
const TENANT_ID = "87654321-4321-4321-4321-cba987654321";
const REDIRECT_URI = "https://org12345.crm.dynamics.com";
```

---

### Step 4: Verify TypeScript Compiles

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
services/auth/msalConfig.ts(5,10): error TS2305: Module '"@azure/msal-browser"' has no exported member 'Configuration'.
```

**Fix:** Ensure `@azure/msal-browser` installed (Task 1.1).

---

### Step 5: Test Configuration Validation

**Create temporary test file:** `test-msal-config.ts`

```typescript
import { validateMsalConfig, getMsalConfigDebugInfo } from "./services/auth/msalConfig";

// Test validation
try {
  validateMsalConfig();
  console.log("✅ Configuration valid");
  console.log("Config:", getMsalConfigDebugInfo());
} catch (error) {
  console.error("❌ Configuration invalid:", error);
}
```

**Run test:**
```bash
npx ts-node test-msal-config.ts
```

**Expected Output (if placeholders not replaced):**
```
❌ Configuration invalid: Error: [MSAL Config] CLIENT_ID not set. Update msalConfig.ts...
```

**Expected Output (after replacing with actual values):**
```
[MSAL Config] Configuration validation passed ✅
✅ Configuration valid
Config: {
  clientId: '********...',
  tenantId: '********...',
  redirectUri: 'https://org12345.crm.dynamics.com',
  authority: 'https://login.microsoftonline.com/87654321-4321-4321-4321-cba987654321',
  cacheLocation: 'sessionStorage',
  scopes: [ 'api://spe-bff-api/user_impersonation' ]
}
```

**Delete test file:**
```bash
rm test-msal-config.ts
```

---

## Verification Checklist

**Task 1.3 complete when:**

- ✅ File `services/auth/msalConfig.ts` exists
- ✅ File contains complete MSAL configuration with:
  - `msalConfig` constant (Configuration object)
  - `loginRequest` constant (scopes)
  - `validateMsalConfig()` function
  - `getMsalConfigDebugInfo()` function
- ✅ CLIENT_ID, TENANT_ID, REDIRECT_URI replaced with actual values (not placeholders)
- ✅ `npx tsc --noEmit` compiles without errors
- ✅ `validateMsalConfig()` runs without throwing errors
- ✅ All JSDoc comments present

**Quick verification command:**
```bash
cd c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid && \
test -f services/auth/msalConfig.ts && \
npx tsc --noEmit && \
echo "✅ Task 1.3 Complete"
```

---

## Troubleshooting

### Issue 1: "Cannot find module '@azure/msal-browser'"

**Cause:** Package not installed or import path incorrect.

**Fix:**
```bash
# Verify package installed
npm list @azure/msal-browser

# If missing, install (Task 1.1)
npm install @azure/msal-browser --save

# Retry compilation
npx tsc --noEmit
```

---

### Issue 2: "Configuration validation failed: CLIENT_ID not set"

**Cause:** Placeholder values not replaced with actual Azure App Registration details.

**Fix:**
1. Open Azure Portal → App registrations → SDAP
2. Copy Client ID, Tenant ID, Redirect URI
3. Replace placeholders in `msalConfig.ts`
4. Rerun `validateMsalConfig()`

---

### Issue 3: "redirect_uri_mismatch" error at runtime

**Cause:** REDIRECT_URI in code doesn't match Azure App Registration.

**Fix:**
1. Azure Portal → App registrations → SDAP → Authentication
2. Check **Redirect URIs** list
3. Ensure list includes: `https://<your-org>.crm.dynamics.com`
4. If missing, click **Add URI** → Enter Dataverse URL → **Save**
5. Update `msalConfig.ts` REDIRECT_URI to match exactly

---

### Issue 4: "Authority URL is invalid"

**Cause:** TENANT_ID contains placeholder or wrong format.

**Fix:**
```typescript
// ❌ Wrong
const TENANT_ID = "<YOUR_TENANT_ID>";
const TENANT_ID = "my-tenant-name"; // Name not allowed, must be GUID

// ✅ Correct
const TENANT_ID = "87654321-4321-4321-4321-cba987654321";
```

---

## Security Notes

### Why sessionStorage vs localStorage?

**sessionStorage (SDAP choice):**
- ✅ Tokens cleared when browser tab closed
- ✅ More secure (shorter token lifetime)
- ✅ Recommended for enterprise apps

**localStorage:**
- ❌ Tokens persist across browser sessions
- ❌ Higher risk if device compromised
- ❌ Not recommended for SDAP

### Why tenant-specific authority?

**Tenant-specific (`/{tenantId}`):**
- ✅ Only allows users from specified tenant
- ✅ More secure for enterprise apps
- ✅ SDAP requirement

**Common (`/common`):**
- ❌ Allows users from any Azure AD tenant
- ❌ Risk of accidental cross-tenant access
- ❌ Not suitable for SDAP

---

## Next Steps

**After Task 1.3 completion:**

✅ **Task 1.3 Complete** - MSAL configuration created and validated

➡️ **Task 1.4: Implement MsalAuthProvider Class**
- Create `services/auth/MsalAuthProvider.ts`
- Implement `IAuthProvider` interface
- Add `PublicClientApplication` initialization logic
- Implement `ssoSilent()` token acquisition (Phase 2 preview)

**See:** `TASK-1.4-IMPLEMENT-MSAL-AUTH-PROVIDER.md`

---

## Files Created

**Created:**
- `services/auth/msalConfig.ts` - MSAL configuration and validation

**Modified:**
- None

---

## Related Documentation

- [MSAL.js Configuration](https://learn.microsoft.com/en-us/azure/active-directory/develop/msal-js-initializing-client-applications)
- [Azure App Registration](https://learn.microsoft.com/en-us/azure/active-directory/develop/quickstart-register-app)
- [Sprint 8 MSAL Overview](./SPRINT-8-MSAL-OVERVIEW.md)
- [Sprint 4 Dual Auth Architecture](../../../docs/architecture/ARCHITECTURE-DUAL-AUTH-GRAPH-INTEGRATION.md)

---

**Task Status:** 📋 **READY TO START**
**Next Task:** Task 1.4 - Implement MsalAuthProvider
**Estimated Duration:** 45 minutes

---
