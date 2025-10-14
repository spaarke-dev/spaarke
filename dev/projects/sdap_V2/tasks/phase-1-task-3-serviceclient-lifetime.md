# Phase 1 - Task 3: Fix ServiceClient Lifetime

**Phase**: 1 (Configuration & Critical Fixes)
**Duration**: 45 minutes
**Risk**: Low
**Pattern**: [service-dataverse-connection.md](../patterns/service-dataverse-connection.md)
**Anti-Pattern**: [anti-pattern-captive-dependency.md](../patterns/anti-pattern-captive-dependency.md)

---

## Current State (Before Starting)

**Current Lifetime Issue**:
- DataverseServiceClientImpl registered as **Scoped** (per-request lifetime)
- ServiceClient is expensive to initialize (~500ms connection setup)
- Every API request creates a new ServiceClient instance = 500ms overhead

**Performance Impact**:
- Health check: ~500ms (should be <100ms)
- Dataverse queries: ~650ms (should be <100ms)
- User experience: Slow API responses

**Quick Verification**:
```bash
# Check current registration
grep -n "DataverseServiceClientImpl" src/api/Spe.Bff.Api/Program.cs
# Look for "AddScoped" - if present, you need this task
# Look for "AddSingleton" - task already complete!
```

---

## Background: Why ServiceClient is Scoped

**Historical Context**:
- Initial implementation followed common pattern: "Request-scoped services for web APIs"
- Assumption: Each request should have its own isolated Dataverse connection
- ServiceClient was treated like HttpClient (per-request instance)

**The Problem**:
- Unlike HttpClient, ServiceClient is **very expensive** to initialize
- ServiceClient maintains internal connection pooling (designed for reuse)
- Creating new instances defeats connection pooling benefits
- Result: 500ms initialization overhead on every request

**Why Singleton is Correct**:
- ServiceClient is **thread-safe** (designed for singleton use)
- Internal connection pooling requires long-lived instance
- Connection string with `RequireNewInstance=false` enables pooling
- Microsoft documentation recommends singleton lifetime for expensive clients

**What Changed Our Understanding**:
- Performance profiling revealed 500ms initialization overhead
- Microsoft docs clarify ServiceClient is designed for singleton use
- ADR-010 established: "Singleton for expensive resources"
- Similar to Entity Framework DbContext factory pattern

**Why This Fix is Safe**:
- ServiceClient handles concurrency internally (thread-safe)
- No per-request state stored in ServiceClient
- Connection pooling actually improves reliability under load
- Factory pattern with IOptions ensures proper initialization

---

## 🤖 AI PROMPT

```
CONTEXT: You are on Phase 1, Task 3 (FINAL task of Phase 1) of the SDAP BFF API refactoring. Tasks 1 and 2 should be complete.

TASK: Change DataverseServiceClientImpl DI registration from Scoped to Singleton lifetime to eliminate 500ms initialization overhead on every request.

CONSTRAINTS:
- Must change registration to AddSingleton with factory pattern
- Must set RequireNewInstance=false in connection string (enable pooling)
- Must verify no Scoped services are injected (captive dependency check)
- Must use IOptions<DataverseOptions> for configuration
- This is DI REGISTRATION CHANGE - not creating new files

VERIFICATION BEFORE STARTING:
1. Verify Task 1.1 complete: API_APP_ID is 1e40baad-...
2. Verify Task 1.2 complete: No UAMI references in GraphClientFactory
3. Verify DataverseServiceClientImpl currently registered as Scoped
4. Verify application starts and Dataverse connection works (baseline)
5. If any verification fails, complete previous tasks first or report status

FOCUS: Stay focused on DI registration only. Do NOT create SpeFileStore (Phase 2) or feature modules (Phase 3). After this task, Phase 1 is COMPLETE.
```

---

## Goal

Change `DataverseServiceClientImpl` registration from **Scoped** to **Singleton** lifetime to eliminate 500ms initialization overhead on every request.

**Problem**: ServiceClient is registered as Scoped, creating a new connection on each request (~500ms overhead)

**Impact**:
- 500ms latency on every Dataverse query
- Unnecessary connection overhead
- Health check takes 300ms+ instead of <50ms

---

## Files to Edit

```bash
- [ ] src/api/Spe.Bff.Api/Program.cs (or Extensions/DocumentsModule.Extensions.cs if exists)
- [ ] src/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs (verify only)
```

---

## Implementation

### Step 1: Locate Current Registration

Search for current registration:
```bash
grep -r "DataverseServiceClientImpl" src/api/Spe.Bff.Api/

# Look for:
# services.AddScoped<DataverseServiceClientImpl>()
```

**Common locations**:
- `src/api/Spe.Bff.Api/Program.cs`
- `src/api/Spe.Bff.Api/Extensions/DocumentsModule.Extensions.cs`

### Step 2: Change to Singleton with Factory

```csharp
// ❌ OLD (WRONG): Scoped lifetime
builder.Services.AddScoped<DataverseServiceClientImpl>();

// ✅ NEW (CORRECT): Singleton lifetime with factory
builder.Services.AddSingleton<DataverseServiceClientImpl>(sp =>
{
    var options = sp.GetRequiredService<IOptions<DataverseOptions>>().Value;

    // Build connection string with client secret auth
    var connectionString =
        $"AuthType=ClientSecret;" +
        $"Url={options.ServiceUrl};" +
        $"ClientId={options.ClientId};" +
        $"ClientSecret={options.ClientSecret};" +
        $"RequireNewInstance=false;"; // IMPORTANT: Enable connection pooling

    return new DataverseServiceClientImpl(connectionString);
});
```

**Key Points**:
1. Change from `AddScoped` to `AddSingleton`
2. Use factory pattern: `AddSingleton<T>(Func<IServiceProvider, T>)`
3. Inject `IOptions<DataverseOptions>` for configuration
4. Set `RequireNewInstance=false` to enable connection pooling
5. Connection created **once** at startup, reused for all requests

### Step 3: Verify Options Configuration

Ensure `DataverseOptions` is configured:

```csharp
// In Program.cs or module extension
builder.Services.AddOptions<DataverseOptions>()
    .Bind(builder.Configuration.GetSection(DataverseOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

### Step 4: Check for Captive Dependencies

Verify `DataverseServiceClientImpl` doesn't inject any Scoped services:

```csharp
// ✅ CORRECT: Singleton can inject Singleton or nothing
public class DataverseServiceClientImpl
{
    // No constructor dependencies OR
    // Only Singleton dependencies (like ILogger)
}

// ❌ WRONG: Singleton injecting Scoped would be captive dependency
public class DataverseServiceClientImpl
{
    private readonly ScopedService _scoped; // ⚠️ Captive dependency!
}
```

### Step 5: Verify Consumers Can Handle Singleton

Check that services using `DataverseServiceClientImpl` are compatible with Singleton lifetime:

```bash
# Find all usages
grep -r "DataverseServiceClientImpl" src/

# Verify consumers:
# - Scoped services can inject Singleton ✅
# - Transient services can inject Singleton ✅
# - Thread-safety is handled by ServiceClient internally ✅
```

---

## Validation

### Build Check
```bash
dotnet build
# Expected: Success, 0 warnings
```

### Application Start
```bash
cd src/api/Spe.Bff.Api
dotnet run
# Expected: Application starts, ServiceClient initialized once at startup
```

### Health Check Performance
```bash
# Test Dataverse health check
time curl https://localhost:5001/healthz/dataverse

# Expected (BEFORE): ~500ms (Scoped, new connection each time)
# Expected (AFTER): <100ms (Singleton, connection reused)
```

### Load Test (Connection Reuse)
```bash
# Make 10 consecutive requests
for i in {1..10}; do
  curl https://localhost:5001/healthz/dataverse
done

# Expected: All requests fast (<100ms), same connection reused
# Check logs: Should see "ServiceClient initialized" only ONCE at startup
```

### Verify No Captive Dependencies
```bash
dotnet run
# Check startup logs for DI warnings
# Expected: No warnings about captive dependencies
```

---

## Checklist

- [ ] Found current `DataverseServiceClientImpl` registration
- [ ] Changed from `AddScoped` to `AddSingleton`
- [ ] Used factory pattern with `IServiceProvider`
- [ ] Connection string includes `RequireNewInstance=false`
- [ ] Verified `DataverseOptions` configuration exists
- [ ] Verified no Scoped services injected into DataverseServiceClientImpl
- [ ] Build succeeds: `dotnet build`
- [ ] Application starts: `dotnet run`
- [ ] Health check fast: `<100ms` (was ~500ms)
- [ ] Load test: 10 requests all fast
- [ ] No DI warnings in startup logs

---

## Expected Results

**Before (Scoped)**:
- ❌ New connection created on every request (~500ms)
- ❌ Health check: ~500ms
- ❌ Dataverse query: ~650ms
- ❌ Logs show: "ServiceClient initialized" on every request

**After (Singleton)**:
- ✅ Connection created once at startup
- ✅ Health check: <100ms (83% faster)
- ✅ Dataverse query: <100ms (85% faster)
- ✅ Logs show: "ServiceClient initialized" only at startup
- ✅ Connection pooling enabled

---

## Performance Metrics

| Operation | Before (Scoped) | After (Singleton) | Improvement |
|-----------|-----------------|-------------------|-------------|
| Health Check | ~500ms | <100ms | 80% faster |
| Dataverse Query | ~650ms | <100ms | 85% faster |
| Connection Init | Every request | Once at startup | 100% elimination |

---

## Anti-Pattern Check

✅ **Avoided**: [Captive Dependency](../patterns/anti-pattern-captive-dependency.md)
- Verified no Scoped services injected into Singleton
- Followed lifetime hierarchy: Singleton → Singleton

✅ **Applied**: Singleton for expensive resources
- ServiceClient is expensive to create (~500ms)
- Connection pooling enabled
- Thread-safe (ServiceClient handles concurrency internally)

---

## Troubleshooting

### Issue: DI warning about captive dependency

**Cause**: DataverseServiceClientImpl injects a Scoped service

**Fix**:
1. Identify the Scoped dependency
2. Either:
   - Change dependency to Singleton (if possible)
   - Use service provider factory pattern to resolve Scoped on-demand

### Issue: Performance not improved

**Cause**: Connection string has `RequireNewInstance=true`

**Fix**: Verify connection string includes `RequireNewInstance=false`

### Issue: "Connection pool exhausted" error

**Cause**: Too many connections (shouldn't happen with Singleton)

**Fix**: Verify registration is actually Singleton (check DI configuration)

### Issue: Stale data in Singleton

**Concern**: Singleton might cache data across requests

**Clarification**: ServiceClient doesn't cache query results - it only manages the connection. Each query gets fresh data from Dataverse.

---

## Commit Message

```bash
git add src/api/Spe.Bff.Api/Program.cs  # or Extensions file
git commit -m "perf(dataverse): change ServiceClient to Singleton lifetime

- Change DataverseServiceClientImpl from Scoped to Singleton
- Add factory pattern with Options for configuration
- Enable connection pooling (RequireNewInstance=false)
- Eliminate 500ms initialization overhead per request

Performance:
- Health check: 500ms → <100ms (80% faster)
- Dataverse query: 650ms → <100ms (85% faster)

ADR: ADR-010 (Singleton for expensive resources)
Anti-Pattern Avoided: Captive dependency
Task: Phase 1, Task 3"
```

---

## Phase 1 Complete!

✅ All Phase 1 tasks completed:
- [x] Task 1: Fix app registration configuration
- [x] Task 2: Remove UAMI logic
- [x] Task 3: Fix ServiceClient lifetime

**Next Phase**: ➡️ [Phase 2: Simplify Service Layer](phase-2-task-1-create-spefilestore.md)

---

## Related Resources

- **Pattern**: [service-dataverse-connection.md](../patterns/service-dataverse-connection.md)
- **Anti-Pattern**: [anti-pattern-captive-dependency.md](../patterns/anti-pattern-captive-dependency.md)
- **Architecture**: [TARGET-ARCHITECTURE.md](../TARGET-ARCHITECTURE.md#solution-4-singleton-serviceclient)
- **ADR**: [ARCHITECTURAL-DECISIONS.md](../ARCHITECTURAL-DECISIONS.md) - ADR-010
