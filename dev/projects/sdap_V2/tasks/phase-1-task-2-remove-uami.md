# Phase 1 - Task 2: Remove UAMI Logic

**Phase**: 1 (Configuration & Critical Fixes)
**Duration**: 45 minutes
**Risk**: Low
**Pattern**: [service-graph-client-factory.md](../patterns/service-graph-client-factory.md)

---

## Current State (Before Starting)

**Current UAMI Issue**:
- GraphClientFactory has branching logic for UAMI vs Client Secret authentication
- `_uamiClientId` field exists but is never populated (no `UAMI_CLIENT_ID` in config)
- This creates dead code and unnecessary complexity

**Authentication Flow Reality**:
- Only Client Secret authentication is actually used
- UAMI code path is never executed
- Adds confusion: "Which auth method is actually running?"

**Quick Verification**:
```bash
# Check if UAMI logic exists
grep -n "UAMI\|_uamiClientId" src/api/Spe.Bff.Api/Infrastructure/GraphClientFactory.cs
# If you see results - you need this task
# If no results - task already complete!
```

---

## Background: Why UAMI Logic Exists

**Historical Context**:
- Originally considered using User-Assigned Managed Identity (UAMI) for Graph API access
- UAMI would eliminate need to store client secrets in Key Vault
- GraphClientFactory was built to support both UAMI and Client Secret paths

**Why It Was Never Used**:
- UAMI requires Azure-hosted infrastructure with proper identity assignments
- Development and testing environments use client secrets for simplicity
- Security team approved client secret approach with Key Vault storage
- UAMI configuration was never added to appsettings files

**Current Reality**:
- `UAMI_CLIENT_ID` is **not configured** in any environment
- Only client secret path ever executes
- UAMI code is dead code that adds complexity without benefit

**Why Remove It Now**:
- Violates YAGNI principle (You Aren't Gonna Need It)
- Makes code harder to understand ("two auth paths but only one works")
- Creates testing complexity (need to mock both paths)
- If UAMI is needed in future, we can add it back cleanly

---

## 🤖 AI PROMPT

```
CONTEXT: You are on Phase 1, Task 2 of the SDAP BFF API refactoring. Task 1 (app config) should be complete.

TASK: Remove all User-Assigned Managed Identity (UAMI) authentication logic from GraphClientFactory, keeping only client secret authentication.

CONSTRAINTS:
- Must remove _uamiClientId field completely
- Must remove all conditional branching for UAMI vs client secret
- Must keep ONLY client secret authentication path
- Must not break existing OBO token exchange functionality
- This is CODE CLEANUP - simplification, not new features

VERIFICATION BEFORE STARTING:
1. Verify Task 1.1 complete: API_APP_ID is 1e40baad-... (not 170c98e1)
2. Verify GraphClientFactory.cs exists and has UAMI logic
3. Verify application currently works with client secret auth
4. If any verification fails, complete Task 1.1 first or report status

FOCUS: Stay focused on GraphClientFactory.cs only. Do NOT modify ServiceClient registration (that's Task 1.3) or create SpeFileStore (that's Phase 2). Only remove UAMI dead code.
```

---

## Goal

Remove User-Assigned Managed Identity (UAMI) authentication logic from GraphClientFactory, keeping only client secret authentication.

**Problem**: Code has branching logic for UAMI vs client secret, but UAMI is never used and `UAMI_CLIENT_ID` is not configured

**Impact**: Unnecessary complexity, dead code, potential confusion

---

## Files to Edit

```bash
- [ ] src/api/Spe.Bff.Api/Infrastructure/GraphClientFactory.cs
```

---

## Implementation

### Step 1: Read Current Implementation

**File**: `src/api/Spe.Bff.Api/Infrastructure/GraphClientFactory.cs`

Identify UAMI-related code:
- `_uamiClientId` field
- Constructor parameter for `IConfiguration`
- Conditional branches checking UAMI

### Step 2: Remove UAMI Field

```csharp
// ❌ REMOVE THIS
private readonly string? _uamiClientId;
```

### Step 3: Simplify Constructor

```csharp
// ❌ OLD (COMPLEX)
public GraphClientFactory(
    IConfiguration configuration,
    ILogger<GraphClientFactory> logger,
    GraphHttpMessageHandler httpMessageHandler)
{
    _logger = logger;
    _httpMessageHandler = httpMessageHandler;

    _uamiClientId = configuration["UAMI_CLIENT_ID"]; // REMOVE THIS LINE

    var apiAppId = configuration["API_APP_ID"];
    var clientSecret = configuration["API_CLIENT_SECRET"];
    var tenantId = configuration["TENANT_ID"];

    // Conditional logic based on UAMI
    if (!string.IsNullOrEmpty(_uamiClientId))
    {
        // Managed Identity logic - REMOVE THIS ENTIRE BLOCK
    }
    else
    {
        // Client secret logic - KEEP THIS
        _cca = ConfidentialClientApplicationBuilder
            .Create(apiAppId)
            .WithClientSecret(clientSecret)
            .WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
            .Build();
    }
}

// ✅ NEW (SIMPLIFIED)
public GraphClientFactory(
    IConfiguration configuration,
    ILogger<GraphClientFactory> logger,
    GraphHttpMessageHandler httpMessageHandler)
{
    _logger = logger;
    _httpMessageHandler = httpMessageHandler;

    var apiAppId = configuration["API_APP_ID"]
        ?? throw new InvalidOperationException("API_APP_ID not configured");
    var clientSecret = configuration["API_CLIENT_SECRET"]
        ?? throw new InvalidOperationException("API_CLIENT_SECRET not configured");
    var tenantId = configuration["TENANT_ID"]
        ?? throw new InvalidOperationException("TENANT_ID not configured");

    // Single authentication path: client secret only
    _cca = ConfidentialClientApplicationBuilder
        .Create(apiAppId)
        .WithClientSecret(clientSecret)
        .WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
        .Build();
}
```

### Step 4: Remove Any UAMI Methods

Search for any methods like `CreateManagedIdentityClientAsync` and remove them.

### Step 5: Search for UAMI References

```bash
# Search entire solution for UAMI references
grep -ri "UAMI" src/
grep -ri "ManagedIdentity" src/ | grep -v "Azure.Identity" # Exclude package references

# Expected: No results (all UAMI logic removed)
```

---

## Validation

### Build Check
```bash
dotnet build
# Expected: Success, 0 warnings
```

### Code Review
```bash
# Verify no UAMI references remain
grep -r "UAMI" src/api/Spe.Bff.Api/Infrastructure/GraphClientFactory.cs
# Expected: No results

# Verify single authentication path
grep -A 10 "ConfidentialClientApplicationBuilder" src/api/Spe.Bff.Api/Infrastructure/GraphClientFactory.cs
# Expected: Only client secret logic, no conditionals
```

### Test Check
```bash
dotnet test
# Expected: All tests pass
```

### Application Start
```bash
cd src/api/Spe.Bff.Api
dotnet run
# Expected: Application starts without errors, no UAMI-related logs
```

### Functional Test
```bash
# Test OBO token exchange (requires valid user token)
curl -X POST https://localhost:5001/api/obo/upload \
  -H "Authorization: Bearer {user-token}" \
  -F "file=@test.txt"

# Expected: HTTP 200 OK, file uploaded successfully
```

---

## Checklist

- [ ] Removed `_uamiClientId` field
- [ ] Removed UAMI configuration reading from constructor
- [ ] Removed UAMI conditional branches
- [ ] Kept only client secret authentication path
- [ ] Added null checks with clear error messages
- [ ] Removed any UAMI-specific methods
- [ ] Searched solution for "UAMI" - no results
- [ ] Searched solution for "ManagedIdentity" (excluding packages) - no results
- [ ] Build succeeds: `dotnet build`
- [ ] Tests pass: `dotnet test`
- [ ] Application starts: `dotnet run`
- [ ] OBO token exchange works (functional test)

---

## Expected Results

**Before**:
- ❌ Branching logic: UAMI vs client secret
- ❌ `_uamiClientId` field (unused)
- ❌ Configuration check for `UAMI_CLIENT_ID`
- ❌ Potential confusion about which auth path is used

**After**:
- ✅ Single authentication path: client secret only
- ✅ Simplified constructor
- ✅ Clear error messages for missing config
- ✅ No dead code or unused fields

---

## Anti-Pattern Check

✅ **Avoided**: Premature abstraction (keeping unused auth paths)
✅ **Avoided**: Configuration complexity (multiple auth types)
✅ **Applied**: YAGNI (You Aren't Gonna Need It) - removed unused UAMI code

---

## Troubleshooting

### Issue: Build fails after removing UAMI

**Cause**: Other files reference UAMI logic

**Fix**: Search for and remove all UAMI references:
```bash
grep -r "UAMI" src/
grep -r "_uamiClientId" src/
```

### Issue: Tests fail after changes

**Cause**: Tests mock UAMI behavior

**Fix**: Update tests to mock only client secret path

### Issue: Configuration error on startup

**Cause**: Missing `API_CLIENT_SECRET` or other required config

**Fix**: Verify all required configuration values are set:
- `API_APP_ID`
- `API_CLIENT_SECRET`
- `TENANT_ID`

---

## Commit Message

```bash
git add src/api/Spe.Bff.Api/Infrastructure/GraphClientFactory.cs
git commit -m "refactor(auth): remove UAMI logic from GraphClientFactory

- Remove unused UAMI_CLIENT_ID field and logic
- Simplify to single authentication path (client secret)
- Add explicit null checks with clear error messages
- Remove branching logic for auth type selection

Rationale: UAMI is not used, adds unnecessary complexity
ADR: ADR-010 (Simplicity over premature abstraction)
Task: Phase 1, Task 2"
```

---

## Next Task

➡️ [Phase 1 - Task 3: Fix ServiceClient Lifetime](phase-1-task-3-serviceclient-lifetime.md)

---

## Related Resources

- **Pattern**: [service-graph-client-factory.md](../patterns/service-graph-client-factory.md)
- **Anti-Pattern**: Premature abstraction, dead code
- **Architecture**: [TARGET-ARCHITECTURE.md](../TARGET-ARCHITECTURE.md#solution-5-remove-uami-confusion)
