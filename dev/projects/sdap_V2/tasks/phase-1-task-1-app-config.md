# Phase 1 - Task 1: Fix App Registration Configuration

**Phase**: 1 (Configuration & Critical Fixes)
**Duration**: 30 minutes
**Risk**: Low
**Pattern**: [service-dataverse-connection.md](../patterns/service-dataverse-connection.md)

---

## Current State (Before Starting)

**Current App ID Issue**:
- Using PCF client app: `170c98e1-...` (WRONG - this is for the PCF control)
- Should use BFF API app: `1e40baad-e065-4aea-a8d4-4b7ab273458c`

**Authentication Flow Impact**:
- Tokens arriving from PCF have audience for BFF API app
- But API is configured to expect PCF app tokens
- Result: Token validation fails with "invalid audience"

**Quick Verification**:
```bash
# Check what's currently configured
grep "API_APP_ID" src/api/Spe.Bff.Api/appsettings.json
# If you see "170c98e1" - you need this task
# If you see "1e40baad" - task already complete!
```

---

## Background: Why This Configuration Error Exists

**Historical Context**:
- Initially, the solution used a single app registration for both PCF control and BFF API
- Later, separate app registrations were created for proper security boundaries
- The configuration files weren't fully updated during the migration
- This left the API configured with the PCF client app ID instead of its own app ID

**Root Cause**:
- App registration separation happened, but config update was incomplete
- Both `appsettings.json` and `appsettings.Development.json` still reference old PCF app

**Why It Matters**:
- PCF control requests tokens with audience `api://1e40baad-...` (BFF API app)
- BFF API validates tokens expecting audience from its configured `ClientId`
- Mismatch = authentication fails with "invalid audience" error
- Dataverse connections also fail because wrong ClientId is used for S2S auth

---

## 🤖 AI PROMPT

```
CONTEXT: You are starting Phase 1 of the SDAP BFF API refactoring. This is the FIRST task - fixing the app registration configuration.

TASK: Update API_APP_ID and all ClientId references from PCF client app (170c98e1) to BFF API app (1e40baad-e065-4aea-a8d4-4b7ab273458c).

CONSTRAINTS:
- Must update BOTH appsettings.json AND appsettings.Development.json
- All ClientId values must match API_APP_ID (consistency check)
- Must add Audience field to AzureAd section
- This is CONFIGURATION ONLY - do not modify code files

VERIFICATION BEFORE STARTING:
1. Verify you're on a clean git branch (no uncommitted changes)
2. Verify application currently starts (baseline)
3. Document current API_APP_ID value for rollback if needed
4. If any verification fails, STOP and resolve issues first

FOCUS: Stay focused on configuration files only. Do NOT modify GraphClientFactory, ServiceClient registration, or any code in this task. Those are separate tasks (1.2 and 1.3).
```

---

## Goal

Fix the `API_APP_ID` configuration to use the correct BFF API app registration instead of the PCF client app.

**Problem**: Currently using PCF client app ID (`170c98e1-...`) instead of BFF API app ID (`1e40baad-...`)

**Impact**: Authentication failures, incorrect token audience, Dataverse connection issues

---

## Files to Edit

```bash
- [ ] src/api/Spe.Bff.Api/appsettings.json
- [ ] src/api/Spe.Bff.Api/appsettings.Development.json
```

---

## Implementation

### Step 1: Update appsettings.json

**File**: `src/api/Spe.Bff.Api/appsettings.json`

```json
// ❌ CURRENT (WRONG)
{
  "API_APP_ID": "170c98e1-...",  // PCF client app
  "AzureAd": {
    "ClientId": "170c98e1-...",
    "TenantId": "a221a95e-6abc-4434-aecc-e48338a1b2f2"
  },
  "Dataverse": {
    "ClientId": "170c98e1-..."
  }
}

// ✅ CORRECT (NEW)
{
  "API_APP_ID": "1e40baad-e065-4aea-a8d4-4b7ab273458c",  // BFF API app
  "AzureAd": {
    "ClientId": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
    "Audience": "api://1e40baad-e065-4aea-a8d4-4b7ab273458c",
    "TenantId": "a221a95e-6abc-4434-aecc-e48338a1b2f2"
  },
  "Dataverse": {
    "ClientId": "1e40baad-e065-4aea-a8d4-4b7ab273458c"
  }
}
```

**Key Changes**:
1. `API_APP_ID`: `170c98e1-...` → `1e40baad-e065-4aea-a8d4-4b7ab273458c`
2. `AzureAd.ClientId`: Must match `API_APP_ID`
3. `AzureAd.Audience`: Add this field with value `api://1e40baad-...`
4. `Dataverse.ClientId`: Must match `API_APP_ID`

### Step 2: Update appsettings.Development.json

**File**: `src/api/Spe.Bff.Api/appsettings.Development.json`

Apply the same changes as Step 1.

### Step 3: Search for Old App ID

```bash
# Search entire solution for old PCF client app ID
grep -r "170c98e1" src/
```

**Expected**: No results (all references should be updated)

---

## Validation

### Build Check
```bash
dotnet build
# Expected: Success, 0 warnings
```

### Configuration Check
```bash
# Verify all ClientId values match
grep -E "(API_APP_ID|ClientId)" src/api/Spe.Bff.Api/appsettings.json

# Expected output (all should be 1e40baad-...):
# "API_APP_ID": "1e40baad-e065-4aea-a8d4-4b7ab273458c"
# "ClientId": "1e40baad-e065-4aea-a8d4-4b7ab273458c"
# "ClientId": "1e40baad-e065-4aea-a8d4-4b7ab273458c"
```

### Application Start
```bash
cd src/api/Spe.Bff.Api
dotnet run
# Expected: Application starts without authentication errors
```

### Health Check
```bash
# Test API health endpoint
curl https://localhost:5001/healthz
# Expected: HTTP 200 OK
```

---

## Checklist

- [ ] Updated `API_APP_ID` in appsettings.json
- [ ] Updated `AzureAd.ClientId` to match `API_APP_ID`
- [ ] Added `AzureAd.Audience` field
- [ ] Updated `Dataverse.ClientId` to match `API_APP_ID`
- [ ] Applied same changes to appsettings.Development.json
- [ ] Searched solution for old app ID (`170c98e1-...`) - no results
- [ ] Build succeeds: `dotnet build`
- [ ] Application starts: `dotnet run`
- [ ] Health check passes: `curl /healthz`
- [ ] No authentication errors in logs

---

## Expected Results

**Before**:
- ❌ Authentication failures
- ❌ Token audience mismatch errors
- ❌ Dataverse connection issues
- ❌ Logs show: "Invalid audience" or "Token validation failed"

**After**:
- ✅ Application starts without errors
- ✅ Health check returns 200 OK
- ✅ No authentication errors in logs
- ✅ All ClientId values consistent

---

## Troubleshooting

### Issue: "Invalid audience" error

**Cause**: `AzureAd.Audience` not set or doesn't match app ID

**Fix**: Ensure `Audience` is `api://1e40baad-e065-4aea-a8d4-4b7ab273458c`

### Issue: Dataverse connection fails

**Cause**: `Dataverse.ClientId` doesn't match BFF API app ID

**Fix**: Verify `Dataverse.ClientId` is `1e40baad-e065-4aea-a8d4-4b7ab273458c`

### Issue: Old app ID still found in search

**Cause**: Reference missed in update

**Fix**: Update all occurrences of `170c98e1-...` to `1e40baad-...`

---

## Commit Message

```bash
git add src/api/Spe.Bff.Api/appsettings*.json
git commit -m "fix(config): update API_APP_ID to use BFF API app registration

- Change API_APP_ID from PCF client app (170c98e1) to BFF API app (1e40baad)
- Update all ClientId values to match API_APP_ID
- Add Audience field to AzureAd configuration

Fixes: Authentication failures, token audience mismatch
ADR: ADR-010 (Configuration correctness)
Task: Phase 1, Task 1"
```

---

## Next Task

➡️ [Phase 1 - Task 2: Remove UAMI Logic](phase-1-task-2-remove-uami.md)

---

## Related Resources

- **Pattern**: [service-dataverse-connection.md](../patterns/service-dataverse-connection.md)
- **Architecture**: [TARGET-ARCHITECTURE.md](../TARGET-ARCHITECTURE.md#solution-2-correct-app-registration)
- **Codebase Map**: [CODEBASE-MAP.md](../CODEBASE-MAP.md#configuration-guide)
