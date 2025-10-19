# Sprint 7A - Task 1: Code Review and Verification

**Task:** Code Review and Verification
**Status:** 📋 Ready to Execute
**Estimated Time:** 1-2 hours
**Prerequisites:** None

---

## Goal

Verify that all Sprint 7A code is using the updated MSAL authentication infrastructure from Sprint 8.

## Success Criteria

- [x] All file operation services use `SdapApiClientFactory.create()`
- [x] No deprecated PCF context token usage found
- [x] All services follow MSAL error handling patterns
- [x] MSAL initialization verified in index.ts

## Quick Context

Sprint 8 updated the authentication layer to use MSAL. Sprint 7A services should automatically use MSAL through dependency injection. This task verifies that's actually the case.

---

## Step 1.1: Review SdapApiClientFactory Usage

**File:** `components/UniversalDatasetGridRoot.tsx`

### What to Check

Look for how `SdapApiClient` is instantiated:

**✅ CORRECT Pattern:**
```typescript
const apiClient = SdapApiClientFactory.create(
    sdapConfig.baseUrl,
    sdapConfig.timeout
);
```

**❌ WRONG Pattern (deprecated):**
```typescript
const token = (context as any).userSettings?.accessToken;
const apiClient = new SdapApiClient(baseUrl, () => Promise.resolve(token));
```

### Action

```bash
cd /c/code_files/spaarke/src/controls/UniversalDatasetGrid/UniversalDatasetGrid

# Search for SdapApiClient usage
grep -n "SdapApiClient" components/UniversalDatasetGridRoot.tsx

# Should show: SdapApiClientFactory.create()
```

### Expected Result

✅ `SdapApiClientFactory.create()` is used (already correct from Sprint 8)

### Documentation

- [ ] Record line number where factory is used
- [ ] Confirm no direct `new SdapApiClient()` calls

---

## Step 1.2: Verify Service Constructors

**Files to Review:**
- `services/FileDownloadService.ts`
- `services/FileDeleteService.ts`
- `services/FileReplaceService.ts`

### What to Check

Services should receive `SdapApiClient` instance, not handle tokens directly:

**✅ CORRECT Pattern:**
```typescript
export class FileDownloadService {
    constructor(private apiClient: SdapApiClient) {}

    async downloadFile(driveId: string, itemId: string, fileName: string) {
        // Uses this.apiClient - no token handling here
        const blob = await this.apiClient.downloadFile({ driveId, itemId });
    }
}
```

**❌ WRONG Pattern:**
```typescript
export class FileDownloadService {
    constructor(private token: string) {}  // ❌ No!
}
```

### Action

Open each service file and verify:
1. Constructor accepts `SdapApiClient` instance
2. Service methods call `this.apiClient.xxx()`
3. No direct token handling in service code

### Expected Result

✅ All services use dependency injection pattern (already correct from Sprint 7A)

### Documentation

- [ ] FileDownloadService - uses SdapApiClient ✅
- [ ] FileDeleteService - uses SdapApiClient ✅
- [ ] FileReplaceService - uses SdapApiClient ✅

---

## Step 1.3: Check MSAL Initialization

**File:** `index.ts`

### What to Check

MSAL should be initialized when control starts:

**✅ CORRECT Pattern:**
```typescript
public init(...) {
    this.initializeMsalAsync(container);
    // ...
}

private initializeMsalAsync(container: HTMLDivElement): void {
    (async () => {
        this.authProvider = MsalAuthProvider.getInstance();
        await this.authProvider.initialize();
        logger.info('Control', 'MSAL initialized successfully ✅');
    })();
}
```

### Action

```bash
# Find MSAL initialization
grep -A 20 "initializeMsalAsync" index.ts
```

### Expected Result

✅ MSAL initialized in `init()` method (added by Sprint 8)

### Verification Checklist

- [ ] `initializeMsalAsync()` called in `init()` method
- [ ] Error handling displays user-friendly message
- [ ] `destroy()` method clears MSAL cache

---

## Step 1.4: Review Error Handling

**File:** `services/SdapApiClient.ts`

### What to Check

Automatic 401 retry with MSAL cache clear:

**✅ CORRECT Pattern:**
```typescript
private async fetchWithTimeout(url: string, options: RequestInit): Promise<Response> {
    let attempt = 0;
    const maxAttempts = 2;

    while (attempt < maxAttempts) {
        attempt++;
        const response = await fetch(url, { ...options });

        // Automatic retry on 401
        if (response.status === 401 && attempt < maxAttempts) {
            MsalAuthProvider.getInstance().clearCache();
            const newToken = await this.getAccessToken();
            options.headers['Authorization'] = `Bearer ${newToken}`;
            continue; // Retry
        }

        return response;
    }
}
```

### Action

Open `SdapApiClient.ts` and find the `fetchWithTimeout()` method (around line 207).

### Verification Checklist

- [ ] Method has retry loop (maxAttempts = 2)
- [ ] 401 status triggers cache clear
- [ ] Fresh token acquired on retry
- [ ] Updated Authorization header used

### Expected Result

✅ 401 auto-retry implemented (added by Sprint 8)

---

## Task 1 Completion Checklist

### Code Review Complete

- [ ] Step 1.1: SdapApiClientFactory usage verified
- [ ] Step 1.2: Service constructors verified
- [ ] Step 1.3: MSAL initialization verified
- [ ] Step 1.4: Error handling verified

### Findings

Record any issues found:

**Issues Found:** (list any problems)
- None expected - Sprint 8 already updated everything

**Compliance Status:**
- [ ] ✅ Fully compliant - no issues found
- [ ] ⚠️ Minor issues - document and fix
- [ ] ❌ Major issues - requires remediation

---

## Expected Outcome

✅ **All checks should pass** - Sprint 7A is already MSAL-compliant at the code level.

The dependency injection pattern used in Sprint 7A means services automatically inherited MSAL authentication when Sprint 8 updated `SdapApiClientFactory`.

---

## Next Steps

After completing Task 1:
- → **If all checks pass:** Proceed to [TASK-7A.2-BUILD-VERIFICATION.md](TASK-7A.2-BUILD-VERIFICATION.md)
- → **If issues found:** Document issues and create remediation plan

---

## Quick Commands Reference

```bash
# Navigate to control directory
cd /c/code_files/spaarke/src/controls/UniversalDatasetGrid/UniversalDatasetGrid

# Search for factory usage
grep -rn "SdapApiClientFactory" .

# Search for deprecated token usage
grep -rn "userSettings.accessToken" .

# Check MSAL initialization
grep -A 30 "initializeMsalAsync" index.ts
```

---

**Task Owner:** Sprint 7A MSAL Compliance
**Created:** October 6, 2025
**Estimated Completion:** 1-2 hours
**Next Task:** [TASK-7A.2-BUILD-VERIFICATION.md](TASK-7A.2-BUILD-VERIFICATION.md)
