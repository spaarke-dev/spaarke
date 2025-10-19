# Task 1.2: Create Directory Structure and TypeScript Interfaces

**Sprint:** 8 - MSAL Integration
**Task:** 1.2 of 5 (Phase 1)
**Duration:** 30 minutes
**Status:** 📋 **READY TO START**

---

## Task Goal

> **Create directory structure for authentication code and define TypeScript interfaces for auth types.**

**Success Criteria:**
- ✅ `services/auth/` directory created
- ✅ `types/auth.ts` created with all interface definitions
- ✅ TypeScript compiles without errors
- ✅ Interfaces follow SDAP naming conventions

---

## Context

### Why This Task Matters

Before implementing MSAL authentication logic, we need:
1. **Organized directory structure** - Keep auth code separate from business logic
2. **Type definitions** - TypeScript interfaces for type safety and IntelliSense

This follows ADR-012 (Shared Component Library) principles - well-structured, reusable code.

### ADR Compliance

**ADR-002 (No Heavy Plugins):**
- ✅ All code runs client-side (no Dataverse plugins)

**ADR-006 (Prefer PCF):**
- ✅ TypeScript/React best practices for PCF controls

**ADR-007 (Storage Seam Minimalism):**
- ✅ Simple interfaces, no over-abstraction
- ✅ `IAuthProvider` interface matches existing `SpeFileStore` pattern

---

## Prerequisites

**Before starting:**
- ✅ Task 1.1 completed (`@azure/msal-browser` installed)
- ✅ Terminal open in PCF project directory

**Location:**
```
c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid\
```

---

## Step-by-Step Instructions

### Step 1: Navigate to PCF Project Directory

**Command:**
```bash
cd c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid
```

**Expected:** Terminal prompt shows project directory.

---

### Step 2: Create services/auth Directory

**Command (PowerShell):**
```powershell
New-Item -ItemType Directory -Path "services\auth" -Force
```

**Command (Git Bash):**
```bash
mkdir -p services/auth
```

**Expected Output:**
```
Directory: C:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid\services\auth

Mode                 LastWriteTime         Length Name
----                 -------------         ------ ----
d----          10/6/2025   2:00 PM                auth
```

**Verify:**
```bash
ls services/auth/
```

**Expected:** Directory exists (empty for now).

---

### Step 3: Create types Directory (if not exists)

**Check if exists:**
```bash
ls types/
```

**If directory missing, create:**
```powershell
# PowerShell
New-Item -ItemType Directory -Path "types" -Force

# Git Bash
mkdir -p types
```

---

### Step 4: Create types/auth.ts

**File:** `types/auth.ts`

**Command:**
```bash
# This will be created in next step - just checking path
echo "types/auth.ts will be created"
```

**Content to write:**

```typescript
/**
 * Authentication Types for Universal Dataset Grid
 *
 * These types define the authentication provider contract and related data structures
 * for MSAL.js integration in the PCF control.
 *
 * ADR Compliance:
 * - ADR-007: Simple interfaces, no over-abstraction
 * - ADR-002: Client-side only (no plugin code)
 */

/**
 * Access token with metadata
 *
 * Represents an OAuth 2.0 access token acquired from Azure AD via MSAL.js
 */
export interface AuthToken {
  /**
   * JWT access token string
   * Used in Authorization: Bearer <accessToken> header
   */
  accessToken: string;

  /**
   * Token expiration timestamp
   * Check this before reusing cached token
   */
  expiresOn: Date;

  /**
   * OAuth scopes granted for this token
   * Example: ["api://spe-bff-api/user_impersonation"]
   */
  scopes: string[];

  /**
   * Account username (email) associated with token
   * Optional - for debugging/logging only
   */
  account?: string;
}

/**
 * Authentication error information
 *
 * Provides structured error details when token acquisition fails
 */
export interface AuthError {
  /**
   * Error code from MSAL
   * Examples: "interaction_required", "consent_required", "invalid_grant"
   */
  errorCode: string;

  /**
   * Human-readable error message
   * Safe to display in UI (no PII)
   */
  errorMessage: string;

  /**
   * Whether error requires user interaction (popup login)
   * If true, fallback to acquireTokenPopup()
   */
  requiresInteraction: boolean;

  /**
   * Original error object from MSAL
   * For logging/debugging only - do not display to user
   */
  originalError?: unknown;
}

/**
 * Authentication provider interface
 *
 * Contract for authentication providers (currently only MSAL.js)
 * Follows ADR-007 principle: introduce interface when seam is needed
 *
 * Implementation: MsalAuthProvider (TASK-1.3)
 */
export interface IAuthProvider {
  /**
   * Initialize authentication provider
   *
   * Must be called once during PCF control initialization before any other methods.
   * Handles MSAL PublicClientApplication setup and redirect response processing.
   *
   * @throws Error if configuration invalid or initialization fails
   */
  initialize(): Promise<void>;

  /**
   * Get access token for specified OAuth scopes
   *
   * Attempts silent token acquisition first (SSO), falls back to popup if needed.
   * Caches tokens in sessionStorage for performance.
   *
   * @param scopes - OAuth scopes to request (e.g., ["api://spe-bff-api/user_impersonation"])
   * @returns Access token string for use in Authorization header
   * @throws Error if token acquisition fails after all retry attempts
   */
  getToken(scopes: string[]): Promise<string>;

  /**
   * Clear token cache and sign out
   *
   * Removes cached tokens from MSAL and sessionStorage.
   * User will need to re-authenticate on next getToken() call.
   */
  clearCache(): void;

  /**
   * Check if user is authenticated
   *
   * @returns true if user has active account, false otherwise
   */
  isAuthenticated(): boolean;
}

/**
 * Token cache entry (sessionStorage)
 *
 * Internal type for caching tokens in sessionStorage.
 * Not exported - implementation detail of MsalAuthProvider.
 *
 * @internal
 */
export interface TokenCacheEntry {
  /**
   * Access token string
   */
  token: string;

  /**
   * Expiration timestamp (Unix epoch milliseconds)
   * Check: Date.now() < expiresAt
   */
  expiresAt: number;

  /**
   * OAuth scopes for this token
   * Used to match cache entries to requested scopes
   */
  scopes: string[];
}

/**
 * MSAL configuration options
 *
 * Extends MSAL Configuration with SDAP-specific defaults.
 * Used in msalConfig.ts (TASK-1.3)
 */
export interface MsalConfigOptions {
  /**
   * Azure AD application (client) ID
   * From Azure Portal → App Registration
   */
  clientId: string;

  /**
   * Azure AD tenant ID
   * From Azure Portal → App Registration
   */
  tenantId: string;

  /**
   * Redirect URI after authentication
   * Must match Azure Portal App Registration → Authentication → Redirect URIs
   * Example: "https://org12345.crm.dynamics.com"
   */
  redirectUri: string;

  /**
   * OAuth scopes to request
   * Example: ["api://spe-bff-api/user_impersonation"]
   */
  scopes: string[];

  /**
   * Enable verbose MSAL logging
   * Default: false (only warnings/errors)
   */
  enableVerboseLogging?: boolean;
}
```

**Create the file:**

Use the `Write` tool to create `types/auth.ts` with the content above.

---

### Step 5: Verify TypeScript Compiles

**Command:**
```bash
npx tsc --noEmit
```

**Expected Output:**
```
# No output = success (TypeScript compiles without errors)
```

**If errors:**
```
types/auth.ts(10,15): error TS2304: Cannot find name 'unknown'.
```

**Fix:** Ensure TypeScript version is 3.0+ (supports `unknown` type).

**Check TypeScript version:**
```bash
npx tsc --version
# Should be 4.9+ for PCF projects
```

---

### Step 6: Verify Directory Structure

**Command:**
```bash
# PowerShell
tree /F services types

# Git Bash
find services types -type f
```

**Expected Output:**
```
services
└── auth
    (empty - will contain msalConfig.ts and MsalAuthProvider.ts in Task 1.3)

types
└── auth.ts
```

---

## Verification Checklist

**Task 1.2 complete when:**

- ✅ Directory `services/auth/` exists
- ✅ Directory `types/` exists
- ✅ File `types/auth.ts` exists with all interface definitions:
  - `AuthToken`
  - `AuthError`
  - `IAuthProvider`
  - `TokenCacheEntry`
  - `MsalConfigOptions`
- ✅ `npx tsc --noEmit` runs without errors
- ✅ All interfaces have JSDoc comments

**Quick verification command:**
```bash
cd c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid && \
test -d services/auth && \
test -f types/auth.ts && \
npx tsc --noEmit && \
echo "✅ Task 1.2 Complete"
```

---

## Troubleshooting

### Issue 1: "mkdir: cannot create directory: Permission denied"

**Cause:** Insufficient permissions to create directory.

**Fix:**
```bash
# Run terminal as Administrator (Windows)
# OR check folder permissions

# Verify you're in correct directory
pwd
# Should be: c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid
```

---

### Issue 2: TypeScript compilation errors with "unknown" type

**Cause:** TypeScript version too old (< 3.0).

**Fix:**
```bash
# Check TypeScript version
npx tsc --version

# If < 4.9, update:
npm install typescript@latest --save-dev

# Retry compilation
npx tsc --noEmit
```

---

### Issue 3: "Cannot find module 'types/auth'"

**Cause:** TypeScript can't resolve type imports (tsconfig.json issue).

**Fix:**
```bash
# Check tsconfig.json includes types directory
cat tsconfig.json | grep -A 5 "include"

# Should include:
#   "include": ["**/*.ts", "**/*.tsx"]

# If missing, this will be fixed when we import the types in Task 1.3
```

---

## Code Explanation

### Why IAuthProvider Interface?

Following ADR-007 (Storage Seam Minimalism):
- ✅ Simple interface, not over-abstracted
- ✅ Only methods SDAP actually needs
- ✅ Matches `SpeFileStore` pattern (concrete class with interface for testing)

**Current implementation:** `MsalAuthProvider` (Task 1.3)

**Future:** If we add another auth provider (unlikely), interface enables easy swap.

### Why TokenCacheEntry?

MSAL.js has internal caching, but we add sessionStorage layer for:
1. **Performance** - Avoid MSAL overhead on every request
2. **Expiration checks** - Proactively refresh before token expires
3. **Debugging** - Inspect cached tokens in browser DevTools

### Why MsalConfigOptions?

Type-safe configuration prevents runtime errors:
```typescript
// ✅ Type-safe
const config: MsalConfigOptions = {
  clientId: "abc123",     // ✅ Required
  tenantId: "def456",     // ✅ Required
  redirectUri: "https://org.crm.dynamics.com",  // ✅ Required
  scopes: ["api://app/.default"]  // ✅ Required
};

// ❌ Compiler error
const badConfig: MsalConfigOptions = {
  clientId: "abc123"
  // Missing tenantId, redirectUri, scopes - TypeScript error!
};
```

---

## Next Steps

**After Task 1.2 completion:**

✅ **Task 1.2 Complete** - Directory structure and TypeScript interfaces created

➡️ **Task 1.3: Create MSAL Configuration File**
- Create `services/auth/msalConfig.ts`
- Define MSAL PublicClientApplication configuration
- Add Azure App Registration details (Client ID, Tenant ID, Redirect URI)
- Implement configuration validation

**See:** `TASK-1.3-CREATE-MSAL-CONFIG.md`

---

## Files Created

**Created:**
- `services/auth/` - Directory for authentication code
- `types/auth.ts` - TypeScript interface definitions

**Modified:**
- None (only new files)

---

## Related Documentation

- [TypeScript Handbook - Interfaces](https://www.typescriptlang.org/docs/handbook/interfaces.html)
- [MSAL.js Types](https://github.com/AzureAD/microsoft-authentication-library-for-js/tree/dev/lib/msal-browser)
- [Sprint 8 MSAL Overview](./SPRINT-8-MSAL-OVERVIEW.md)
- [ADR-007: Storage Seam Minimalism](../../../docs/adr/ADR-007-spe-storage-seam-minimalism.md)

---

**Task Status:** 📋 **READY TO START**
**Next Task:** Task 1.3 - Create MSAL Config File
**Estimated Duration:** 30 minutes

---
