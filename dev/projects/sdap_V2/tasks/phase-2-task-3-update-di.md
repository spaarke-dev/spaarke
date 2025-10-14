# Phase 2 - Task 3: Update DI Registrations

**Phase**: 2 (Simplify Service Layer)
**Duration**: 30 minutes
**Risk**: Low
**Patterns**: [di-feature-module.md](../patterns/di-feature-module.md)
**Anti-Patterns**: [anti-pattern-interface-proliferation.md](../patterns/anti-pattern-interface-proliferation.md)

---

## Current State (Before Starting)

**Current DI Problem**:
- Services registered as interfaces: `services.AddScoped<IResourceStore, SpeResourceStore>()`
- Unnecessary interface → implementation mapping
- Endpoint DI resolution looks for interfaces, not concretes
- Verbose: Must specify both interface and implementation

**Code Impact**:
- Boilerplate: Every service needs interface + implementation pair
- Runtime dispatch: Interface calls slower than direct calls
- Harder to navigate: F12 (Go to Definition) goes to interface, not implementation
- More test setup: Mock both interface and implementation bindings

**Quick Verification**:
```bash
# Check current DI registrations
grep -n "IResourceStore\|ISpeService" src/api/Spe.Bff.Api/Program.cs

# Should see interface-based registrations
# If you see only "SpeFileStore" - task already done!
```

---

## Background: Why DI Uses Interfaces

**Historical Context**:
- Microsoft DI documentation examples use interfaces
- Pattern: "Register interface, resolve concrete"
- Assumption: "Always need abstraction for DI"
- Each service got paired interface "for testability"

**How DI Registrations Evolved**:
1. **Initial**: Registered concrete classes directly (simple)
2. **V2**: Added interfaces "for best practices" (following tutorials)
3. **V3**: Created interface for every service "for mocking"
4. **V4**: Ended with 10+ interface/implementation pairs (over-abstracted)

**Why This Seemed Necessary**:
- DI tutorials: "Always register interface, not implementation"
- Mocking frameworks: "Need interface to create mock"
- SOLID principles: "Depend on abstractions"
- "Professional" code: "Production code uses interfaces"

**What Changed Our Understanding**:
- **ADR-010 (DI Minimalism)**: Register concretes directly
- Modern DI: Can resolve concrete classes without interfaces
- Testing: Mock at infrastructure boundaries (IGraphClientFactory), not every service
- Realization: Interface-per-class is cargo cult programming

**Why Concrete Registration is Correct**:
- **Still works**: DI container resolves concrete types just fine
- **Better performance**: No interface dispatch overhead
- **Better navigation**: F12 goes directly to implementation
- **Less boilerplate**: One registration line instead of two types
- **Clearer intent**: See what you're actually getting

**Real Example**:
```csharp
// ❌ OLD: Unnecessary interface → implementation mapping
services.AddScoped<IResourceStore, SpeResourceStore>();
services.AddScoped<ISpeService, OboSpeService>();
// Endpoint: private static async Task Handle(IResourceStore store, ...)
// F12 on IResourceStore → Goes to interface (not helpful!)

// ✅ NEW: Direct concrete registration
services.AddScoped<SpeFileStore>();
// Endpoint: private static async Task Handle(SpeFileStore fileStore, ...)
// F12 on SpeFileStore → Goes to implementation (exactly what you need!)
```

**Exception: Factory Pattern**:
```csharp
// ✅ Interface is OK here - factory pattern (multiple implementations possible)
services.AddSingleton<IGraphClientFactory, GraphClientFactory>();
// Why OK? Factory creates different GraphServiceClient instances at runtime
```

---

## 🤖 AI PROMPT

```
CONTEXT: You are working on Phase 2 of the SDAP BFF API refactoring, specifically updating DI registrations to use concrete classes instead of interfaces.

TASK: Update DI registrations in Program.cs or DocumentsModule.Extensions.cs to register SpeFileStore as concrete class and remove old interface registrations.

CONSTRAINTS:
- Must register SpeFileStore as concrete class (no interface)
- Must use Scoped lifetime (per-request context)
- Must remove old interface registrations (IResourceStore, ISpeService)
- Must preserve IGraphClientFactory registration (factory pattern - OK to use interface)

VERIFICATION BEFORE STARTING:
1. Verify Phase 2 Task 2 complete (endpoints updated to use SpeFileStore)
2. Verify SpeFileStore.cs exists in Storage folder
3. Verify current DI has old interface registrations (IResourceStore, ISpeService)
4. If any verification fails, STOP and complete previous tasks first

FOCUS: Stay focused on DI registrations only. Do NOT update tests (that's Task 2.4) or delete old files (that's Task 2.6).
```

---

## Goal

Update dependency injection registrations to register **SpeFileStore** as a concrete class and remove old interface-based registrations.

**Problem**:
- Current: Services registered as interfaces (IResourceStore, ISpeService)
- Unnecessary interfaces violate ADR-010

**Target**:
- New: Register SpeFileStore as concrete class
- Remove old interface registrations
- Preserve IGraphClientFactory (factory pattern - OK)

---

## Pre-Flight Verification

### Step 0: Verify Context and Prerequisites

```bash
# 1. Verify Phase 2 Task 2 complete
- [ ] Check endpoints use SpeFileStore
grep "SpeFileStore" src/api/Spe.Bff.Api/Api/OBOEndpoints.cs

# 2. Verify SpeFileStore exists
- [ ] ls src/api/Spe.Bff.Api/Storage/SpeFileStore.cs

# 3. Find current DI registrations
- [ ] Locate old registrations
grep -r "AddScoped<IResourceStore" src/api/Spe.Bff.Api/
grep -r "AddScoped<ISpeService" src/api/Spe.Bff.Api/

# 4. Identify DI location (Program.cs or Extensions)
- [ ] Check if DocumentsModule.Extensions.cs exists
ls src/api/Spe.Bff.Api/Extensions/DocumentsModule.Extensions.cs
```

**If any verification fails**: STOP and complete previous tasks first.

---

## Files to Edit

```bash
Option A (if feature modules exist):
- [ ] src/api/Spe.Bff.Api/Extensions/DocumentsModule.Extensions.cs

Option B (if no feature modules yet):
- [ ] src/api/Spe.Bff.Api/Program.cs
```

---

## Implementation

### Option A: Update DocumentsModule.Extensions.cs (Preferred)

**File**: `src/api/Spe.Bff.Api/Extensions/DocumentsModule.Extensions.cs`

#### Before (OLD - with interfaces):
```csharp
using Spe.Bff.Api.Infrastructure.Graph;

namespace Spe.Bff.Api.Extensions;

public static class DocumentsModuleExtensions
{
    public static IServiceCollection AddDocumentsModule(this IServiceCollection services)
    {
        // ❌ OLD: Interface-based registrations
        services.AddScoped<IResourceStore, SpeResourceStore>();
        services.AddScoped<ISpeService, OboSpeService>();

        // Graph API (keep this - factory pattern)
        services.AddSingleton<IGraphClientFactory, GraphClientFactory>();
        services.AddScoped<UploadSessionManager>();

        return services;
    }
}
```

#### After (NEW - concrete classes):
```csharp
using Spe.Bff.Api.Storage;
using Spe.Bff.Api.Infrastructure.Graph;

namespace Spe.Bff.Api.Extensions;

/// <summary>
/// Registers SPE file operations and Graph API services (ADR-010: DI Minimalism).
/// </summary>
public static class DocumentsModuleExtensions
{
    public static IServiceCollection AddDocumentsModule(this IServiceCollection services)
    {
        // ✅ NEW: Concrete class registration
        // Scoped - may hold per-request context (user token)
        services.AddScoped<SpeFileStore>();

        // ✅ Graph API (factory pattern - interface is OK per ADR-010)
        // Singleton - factory pattern, stateless
        services.AddSingleton<IGraphClientFactory, GraphClientFactory>();

        // Scoped - per-request upload tracking
        services.AddScoped<UploadSessionManager>();

        return services;
    }
}
```

### Option B: Update Program.cs (if no feature modules)

**File**: `src/api/Spe.Bff.Api/Program.cs`

#### Before (OLD):
```csharp
var builder = WebApplication.CreateBuilder(args);

// ❌ OLD: Scattered registrations with interfaces
services.AddScoped<IResourceStore, SpeResourceStore>();
services.AddScoped<ISpeService, OboSpeService>();
services.AddSingleton<IGraphClientFactory, GraphClientFactory>();
services.AddScoped<UploadSessionManager>();

// ... rest of Program.cs
```

#### After (NEW):
```csharp
var builder = WebApplication.CreateBuilder(args);

// ============================================================================
// SPE File Operations (ADR-010: Register Concretes)
// ============================================================================

// ✅ NEW: Concrete class registration
services.AddScoped<SpeFileStore>();

// Graph API (factory pattern - interface is OK)
services.AddSingleton<IGraphClientFactory, GraphClientFactory>();
services.AddScoped<UploadSessionManager>();

// ============================================================================
// ... rest of Program.cs
```

---

## Validation

### Build Check
```bash
dotnet build
# Expected: Success, 0 warnings
```

### DI Registration Check
```bash
# Verify SpeFileStore is registered as concrete
grep "AddScoped<SpeFileStore>" src/api/Spe.Bff.Api/Extensions/*.cs
# OR
grep "AddScoped<SpeFileStore>" src/api/Spe.Bff.Api/Program.cs
# Expected: Should find registration

# Verify old interfaces are removed
grep -r "IResourceStore\|ISpeService" src/api/Spe.Bff.Api/Extensions/
grep -r "IResourceStore\|ISpeService" src/api/Spe.Bff.Api/Program.cs
# Expected: No results (except maybe in comments)

# Verify IGraphClientFactory is still registered (factory pattern)
grep "IGraphClientFactory" src/api/Spe.Bff.Api/Extensions/*.cs
# Expected: Should find registration (this is OK - factory pattern)
```

### Runtime Test (if API can start)
```bash
# Start application
dotnet run --project src/api/Spe.Bff.Api

# Check for DI errors in logs
# Expected: No "Unable to resolve service" errors

# Test health check
curl -X GET "https://localhost:5001/healthz"
# Expected: 200 OK

# Test endpoint (requires auth token)
curl -X GET "https://localhost:5001/api/documents/test/metadata?containerId=xxx" \
  -H "Authorization: Bearer $TOKEN"
# Expected: Should resolve SpeFileStore (not 500 DI error)
```

---

## Checklist

- [ ] **Pre-flight**: Verified Phase 2 Task 2 complete (endpoints updated)
- [ ] **Pre-flight**: Verified SpeFileStore.cs exists
- [ ] **Pre-flight**: Found old DI registrations (IResourceStore, ISpeService)
- [ ] Removed `services.AddScoped<IResourceStore, SpeResourceStore>()`
- [ ] Removed `services.AddScoped<ISpeService, OboSpeService>()`
- [ ] Added `services.AddScoped<SpeFileStore>()`
- [ ] Kept `services.AddSingleton<IGraphClientFactory, GraphClientFactory>()` (factory pattern)
- [ ] Kept `services.AddScoped<UploadSessionManager>()` (if exists)
- [ ] Used correct lifetime (Scoped for SpeFileStore)
- [ ] Added XML documentation comment
- [ ] Build succeeds: `dotnet build`
- [ ] Application starts without DI errors

---

## Expected Results

**Before**:
- ❌ Interface registrations: `AddScoped<IResourceStore, SpeResourceStore>()`
- ❌ Interface registrations: `AddScoped<ISpeService, OboSpeService>()`
- ❌ Unnecessary interfaces

**After**:
- ✅ Concrete registration: `AddScoped<SpeFileStore>()`
- ✅ No IResourceStore or ISpeService registrations
- ✅ Simplified DI (fewer lines)

---

## Lifetime Decision Rationale

### Why SpeFileStore is Scoped?

```csharp
services.AddScoped<SpeFileStore>();  // ✅ Correct
```

**Reasoning**:
- **Per-request context**: Each HTTP request may have different user token
- **No shared state**: SpeFileStore doesn't need to persist across requests
- **Memory efficiency**: Disposed after request completes
- **Safe default**: Scoped is safer than Singleton for services that interact with per-request data

### Why IGraphClientFactory is Singleton?

```csharp
services.AddSingleton<IGraphClientFactory, GraphClientFactory>();  // ✅ Correct
```

**Reasoning**:
- **Factory pattern**: Creates different clients on demand
- **Stateless**: No per-request state stored
- **Performance**: Expensive to create (MSAL ConfidentialClientApplication setup)
- **Thread-safe**: MSAL library is thread-safe

---

## Anti-Pattern Verification

### ✅ Avoided: Interface Proliferation
```bash
# Verify no unnecessary interfaces registered
grep -E "AddScoped<I[A-Z].*Store" src/api/Spe.Bff.Api/
# Expected: No results (except IGraphClientFactory - factory pattern)
```

**Why**: ADR-010 - Only use interfaces for factories or collections

### ✅ Avoided: Captive Dependency
```bash
# Verify no Singleton services inject Scoped services
# SpeFileStore (Scoped) → IGraphClientFactory (Singleton) ✅ OK
# IGraphClientFactory (Singleton) → SpeFileStore (Scoped) ❌ WRONG
```

**Why**: Singleton can inject Singleton, Scoped can inject any, Transient can inject any

---

## Troubleshooting

### Issue: Application fails to start with DI error

**Cause**: Endpoints still reference old interfaces

**Fix**: Verify Task 2.2 is complete:
```bash
grep "IResourceStore\|ISpeService" src/api/Spe.Bff.Api/Api/*.cs
# Should return no results
```

### Issue: "Unable to resolve service for type 'SpeFileStore'"

**Cause**: SpeFileStore not registered

**Fix**: Verify registration exists:
```bash
grep "AddScoped<SpeFileStore>" src/api/Spe.Bff.Api/Extensions/*.cs
# Should find registration
```

### Issue: Old services still registered

**Cause**: Duplicate registrations in multiple locations

**Fix**: Search all locations:
```bash
grep -r "IResourceStore\|ISpeService" src/api/Spe.Bff.Api/
# Remove all old registrations
```

---

## Context Verification

Before marking complete, verify:
- [ ] ✅ SpeFileStore registered as concrete class (Scoped)
- [ ] ✅ Old interface registrations removed (IResourceStore, ISpeService)
- [ ] ✅ IGraphClientFactory still registered (factory pattern - OK)
- [ ] ✅ Application starts without DI errors
- [ ] ✅ Task stayed focused (did NOT update tests or delete old files)
- [ ] ✅ Build succeeds

**If any item unchecked**: Review and fix before proceeding to Task 2.4

---

## Commit Message

```bash
git add src/api/Spe.Bff.Api/Extensions/DocumentsModule.Extensions.cs
# OR
git add src/api/Spe.Bff.Api/Program.cs

git commit -m "refactor(di): register SpeFileStore as concrete class per ADR-010

- Register SpeFileStore as Scoped (per-request context)
- Remove old interface registrations: IResourceStore, ISpeService
- Preserve IGraphClientFactory registration (factory pattern - OK)
- Add XML documentation comment

DI Simplification: 2 interface registrations → 1 concrete registration
ADR Compliance: ADR-010 (DI Minimalism - Register Concretes)
Anti-Patterns Avoided: Interface proliferation
Task: Phase 2, Task 3"
```

---

## Next Task

⚠️ **IMPORTANT**: DI is updated but tests still use old interfaces!

➡️ [Phase 2 - Task 4: Update Test Mocking Strategy](phase-2-task-4-update-tests.md)

**What's next**: Update tests to mock IGraphClientFactory (infrastructure boundary) instead of old interfaces

---

## Related Resources

- **Patterns**:
  - [di-feature-module.md](../patterns/di-feature-module.md)
- **Anti-Patterns**:
  - [anti-pattern-interface-proliferation.md](../patterns/anti-pattern-interface-proliferation.md)
  - [anti-pattern-captive-dependency.md](../patterns/anti-pattern-captive-dependency.md)
- **Architecture**: [TARGET-ARCHITECTURE.md](../TARGET-ARCHITECTURE.md#dependency-injection-minimalism)
- **ADR**: [ARCHITECTURAL-DECISIONS.md](../ARCHITECTURAL-DECISIONS.md) - ADR-010
