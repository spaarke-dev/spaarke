# Phase 3 - Task 1: Create Feature Modules

**Phase**: 3 (Feature Module Pattern)
**Duration**: 2-3 hours | **ACTUAL**: Already complete (discovered during review)
**Risk**: Low
**Patterns**: [di-feature-module.md](../patterns/di-feature-module.md)
**Anti-Patterns**: [anti-pattern-captive-dependency.md](../patterns/anti-pattern-captive-dependency.md)
**Status**: ✅ **COMPLETE** - Feature modules already exist and are better than planned

---

## ⚠️ TASK STATUS: ALREADY COMPLETE

**Discovery**: This task was found to be already complete during Phase 3 review. The feature modules exist and are actively used in Program.cs.

**Files Found**:
- ✅ `src/api/Spe.Bff.Api/Infrastructure/DI/SpaarkeCore.cs` (Authorization + Cache)
- ✅ `src/api/Spe.Bff.Api/Infrastructure/DI/DocumentsModule.cs` (SPE + Graph operations)
- ✅ `src/api/Spe.Bff.Api/Infrastructure/DI/WorkersModule.cs` (Background services)

**Location Difference**: Files are in `Infrastructure/DI/` (not `Extensions/` as originally planned).

**Verification**: See "Current State Verification" section below instead of executing implementation steps.

---

## 🤖 AI PROMPT (For Reference - Task Already Complete)

```
CONTEXT: Phase 3 Task 1 is ALREADY COMPLETE. Use this prompt only for verification or understanding.

VERIFICATION TASK: Verify that feature modules exist and are correctly implemented.

CURRENT STATE:
- Feature modules already created in Infrastructure/DI/ folder
- SpaarkeCore.cs: Contains authorization rules, RequestCache, DataverseAccessDataSource
- DocumentsModule.cs: Contains SPE operations (ContainerOperations, DriveItemOperations, UploadSessionManager, UserOperations, SpeFileStore)
- WorkersModule.cs: Contains Service Bus client, DocumentEventProcessor, IdempotencyService

VERIFICATION BEFORE PROCEEDING TO PHASE 4:
1. Verify all three feature module files exist and build successfully
2. Verify Program.cs calls all three modules (AddSpaarkeCore, AddDocumentsModule, AddWorkersModule)
3. Verify DI line count in Program.cs is ~20-30 lines (target achieved)
4. Verify all services resolve correctly (no DI errors at startup)
5. If any verification fails, review module implementation before proceeding

FOCUS: This is verification-only. Do NOT create new files or refactor existing code.
```

---

## Goal

Create **three feature module extension files** to organize DI registrations by feature area, preparing for Program.cs simplification.

**Feature Modules**:
1. **SpaarkeCore** - Authorization, caching, core services
2. **DocumentsModule** - SPE operations, Graph API, Dataverse
3. **WorkersModule** - Background services, job processors

**Why**: Simplifies Program.cs from 80+ DI lines to ~20 lines, organizes services by feature

---

## Pre-Flight Verification

### Step 0: Verify Context and Prerequisites

```bash
# 1. Verify Phase 2 complete
- [ ] All Phase 2 tasks complete (1-6)
- [ ] SpeFileStore created and wired up
- [ ] Old interfaces deleted
- [ ] All tests pass

# 2. Count current Program.cs DI lines
- [ ] grep -A 100 "var builder =" src/api/Spe.Bff.Api/Program.cs | grep "services.Add" | wc -l
- [ ] Record count: _____ lines (target: reduce to ~20)

# 3. Identify all current DI registrations
- [ ] grep "services.Add" src/api/Spe.Bff.Api/Program.cs
- [ ] List all services to be organized: _____

# 4. Create Extensions folder if needed
- [ ] mkdir -p src/api/Spe.Bff.Api/Extensions
```

**If any verification fails**: STOP and complete previous phases first.

---

## Current State Verification

**Instead of creating files, verify they already exist**:

```bash
# 1. Verify feature module files exist
ls -la src/api/Spe.Bff.Api/Infrastructure/DI/SpaarkeCore.cs
ls -la src/api/Spe.Bff.Api/Infrastructure/DI/DocumentsModule.cs
ls -la src/api/Spe.Bff.Api/Infrastructure/DI/WorkersModule.cs
# Expected: All three files exist ✅

# 2. Verify Program.cs uses feature modules
grep "AddSpaarkeCore\|AddDocumentsModule\|AddWorkersModule" src/api/Spe.Bff.Api/Program.cs
# Expected: Should find all three method calls ✅

# 3. Verify services resolve correctly
dotnet build src/api/Spe.Bff.Api/Spe.Bff.Api.csproj
# Expected: Build succeeds with no DI errors ✅

# 4. Count DI registrations in Program.cs
grep -n "services.Add\|builder.Services.Add" src/api/Spe.Bff.Api/Program.cs | wc -l
# Expected: ~20-30 lines (target achieved) ✅
```

**If all verifications pass**: Task 3.1 is complete. Proceed to review Task 3.2.

**If any verification fails**: Review the "Implementation Reference" section below to understand expected state.

---

## Implementation Reference (Already Complete - For Understanding Only)

**Note**: These sections describe what SHOULD exist. The actual implementation already exists and may differ slightly from this reference.

### Files That Already Exist

```bash
✅ src/api/Spe.Bff.Api/Infrastructure/DI/SpaarkeCore.cs
✅ src/api/Spe.Bff.Api/Infrastructure/DI/DocumentsModule.cs
✅ src/api/Spe.Bff.Api/Infrastructure/DI/WorkersModule.cs
```

---

## Implementation

### ACTUAL CURRENT STATE (What Exists Now)

**File**: `src/api/Spe.Bff.Api/Infrastructure/DI/SpaarkeCore.cs` ✅ **EXISTS**

**Actual Implementation** (from codebase review):
```csharp
public static class SpaarkeCore
{
    public static IServiceCollection AddSpaarkeCore(this IServiceCollection services)
    {
        // SDAP Authorization services
        services.AddScoped<Spaarke.Core.Auth.AuthorizationService>();

        // Register HttpClient for DataverseAccessDataSource
        services.AddHttpClient<IAccessDataSource, DataverseAccessDataSource>(...);

        // Authorization rules (registered in execution order)
        services.AddScoped<IAuthorizationRule, OperationAccessRule>();
        services.AddScoped<IAuthorizationRule, TeamMembershipRule>();

        // Request cache for per-request memoization
        services.AddScoped<RequestCache>();

        return services;
    }
}
```

**File**: `src/api/Spe.Bff.Api/Infrastructure/DI/DocumentsModule.cs` ✅ **EXISTS**

**Actual Implementation**:
```csharp
public static class DocumentsModule
{
    public static IServiceCollection AddDocumentsModule(this IServiceCollection services)
    {
        // SPE specialized operation classes
        services.AddScoped<ContainerOperations>();
        services.AddScoped<DriveItemOperations>();
        services.AddScoped<UploadSessionManager>();
        services.AddScoped<UserOperations>();

        // SPE file store facade (delegates to specialized classes)
        services.AddScoped<SpeFileStore>();

        // Document authorization filters
        services.AddScoped<DocumentAuthorizationFilter>(...);

        return services;
    }
}
```

**File**: `src/api/Spe.Bff.Api/Infrastructure/DI/WorkersModule.cs` ✅ **EXISTS**

**Actual Implementation**:
```csharp
public static class WorkersModule
{
    public static IServiceCollection AddWorkersModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Service Bus client (if configured)
        var serviceBusConnectionString = configuration.GetValue<string>("ServiceBus:ConnectionString");
        if (!string.IsNullOrWhiteSpace(serviceBusConnectionString))
        {
            services.AddSingleton(sp => new ServiceBusClient(serviceBusConnectionString));
        }

        // Document event processor options
        services.Configure<DocumentEventProcessorOptions>(...);

        // Idempotency service
        services.AddScoped<IIdempotencyService, IdempotencyService>();

        // Document event processor (if Service Bus configured)
        if (!string.IsNullOrWhiteSpace(serviceBusConnectionString))
        {
            services.AddHostedService<DocumentEventProcessor>();
        }

        // Document event handler
        services.AddScoped<IDocumentEventHandler, DocumentEventHandler>();

        return services;
    }
}
```

**Architecture Notes**:
- Better than planned: Uses specialized operation classes (ContainerOperations, DriveItemOperations) in DocumentsModule
- Better than planned: DocumentsModule uses facade pattern with SpeFileStore delegating to specialized classes
- Better than planned: WorkersModule has conditional registration based on Service Bus configuration
- Location: Files in `Infrastructure/DI/` folder (not `Extensions/` as originally planned)

---

### ORIGINAL PLAN (For Reference Only - Already Superseded by Better Implementation)

### Step 1: Create SpaarkeCore.Extensions.cs (REFERENCE ONLY - ACTUAL FILE ALREADY EXISTS)

**File**: `src/api/Spe.Bff.Api/Extensions/SpaarkeCore.Extensions.cs` (PLANNED LOCATION - NOT USED)

**Pattern**: [di-feature-module.md](../patterns/di-feature-module.md)

```csharp
using Microsoft.Extensions.Caching.Distributed;
using Spaarke.Core.Authorization;
using Spaarke.Dataverse;

namespace Spe.Bff.Api.Extensions;

/// <summary>
/// Registers core authorization and caching services (ADR-010: DI Minimalism).
/// Includes: AuthorizationService, IAccessDataSource, Redis cache, request cache.
/// </summary>
public static class SpaarkeCoreExtensions
{
    /// <summary>
    /// Add Spaarke core services: authorization, caching, and access control.
    /// </summary>
    public static IServiceCollection AddSpaarkeCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ============================================================================
        // Authorization (Singleton - stateless rule evaluation)
        // ============================================================================
        services.AddSingleton<AuthorizationService>();

        // Access data source (Scoped - per-request Dataverse queries)
        services.AddScoped<IAccessDataSource, DataverseAccessDataSource>();

        // Register all authorization rules (Singleton - stateless logic)
        services.Scan(scan => scan
            .FromAssemblyOf<IAuthorizationRule>()
            .AddClasses(classes => classes.AssignableTo<IAuthorizationRule>())
            .AsImplementedInterfaces()
            .WithSingletonLifetime());

        // ============================================================================
        // Distributed Cache (Redis)
        // ============================================================================
        var redisConnectionString = configuration.GetConnectionString("Redis");

        if (!string.IsNullOrEmpty(redisConnectionString))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
                options.InstanceName = configuration["Redis:InstanceName"] ?? "sdap:";
            });
        }
        else
        {
            // Fallback to in-memory cache for development
            services.AddDistributedMemoryCache();
        }

        // ============================================================================
        // Request Cache (Scoped - per-request memoization)
        // ============================================================================
        services.AddScoped<RequestCache>();

        return services;
    }
}
```

**Key Points**:
- **AuthorizationService**: Singleton (stateless)
- **IAccessDataSource**: Scoped (per-request queries)
- **IAuthorizationRule**: Singleton (stateless, auto-scanned)
- **Redis cache**: Singleton (shared cache)
- **RequestCache**: Scoped (per-request memoization)

---

### Step 2: Create DocumentsModule.Extensions.cs

**File**: `src/api/Spe.Bff.Api/Extensions/DocumentsModule.Extensions.cs`

**Pattern**: [di-feature-module.md](../patterns/di-feature-module.md)

```csharp
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Spe.Bff.Api.Storage;
using Spe.Bff.Api.Infrastructure.Graph;

namespace Spe.Bff.Api.Extensions;

/// <summary>
/// Registers SPE file operations, Graph API, and Dataverse services (ADR-010: DI Minimalism).
/// Includes: SpeFileStore, IGraphClientFactory, UploadSessionManager, DataverseServiceClientImpl.
/// </summary>
public static class DocumentsModuleExtensions
{
    /// <summary>
    /// Add documents module: SPE operations, Graph API, and Dataverse connection.
    /// </summary>
    public static IServiceCollection AddDocumentsModule(
        this IServiceCollection services)
    {
        // ============================================================================
        // SPE File Storage (Scoped - may hold per-request context)
        // ============================================================================
        services.AddScoped<SpeFileStore>();

        // ============================================================================
        // Graph API (Factory pattern - Singleton, stateless)
        // ============================================================================
        services.AddSingleton<IGraphClientFactory, GraphClientFactory>();

        // Upload session manager (Scoped - per-request upload tracking)
        services.AddScoped<UploadSessionManager>();

        // ============================================================================
        // Dataverse Connection (Singleton - connection pooling, thread-safe SDK)
        // ============================================================================
        services.AddSingleton<DataverseServiceClientImpl>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DataverseOptions>>().Value;

            var connectionString =
                $"AuthType=ClientSecret;" +
                $"Url={options.ServiceUrl};" +
                $"ClientId={options.ClientId};" +
                $"ClientSecret={options.ClientSecret};" +
                $"RequireNewInstance=false;"; // Enable connection pooling

            return new DataverseServiceClientImpl(connectionString);
        });

        // Dataverse Web API service (Scoped - per-request queries)
        services.AddScoped<DataverseWebApiService>();

        return services;
    }
}
```

**Key Points**:
- **SpeFileStore**: Scoped (per-request context)
- **IGraphClientFactory**: Singleton (factory pattern, stateless)
- **UploadSessionManager**: Scoped (per-request upload tracking)
- **DataverseServiceClientImpl**: Singleton (connection pooling, expensive resource)
- **DataverseWebApiService**: Scoped (per-request queries)

---

### Step 3: Create WorkersModule.Extensions.cs

**File**: `src/api/Spe.Bff.Api/Extensions/WorkersModule.Extensions.cs`

**Pattern**: [di-feature-module.md](../patterns/di-feature-module.md)

```csharp
using Spe.Bff.Api.Services.Jobs;
using Spe.Bff.Api.Services.Workers;

namespace Spe.Bff.Api.Extensions;

/// <summary>
/// Registers background services and job processing (ADR-010: DI Minimalism).
/// Includes: DocumentEventProcessor, ServiceBusJobProcessor, IdempotencyService.
/// </summary>
public static class WorkersModuleExtensions
{
    /// <summary>
    /// Add workers module: background services, job processors, and idempotency.
    /// </summary>
    public static IServiceCollection AddWorkersModule(
        this IServiceCollection services)
    {
        // ============================================================================
        // Background Processors (Hosted Service - singleton lifetime)
        // ============================================================================
        services.AddHostedService<DocumentEventProcessor>();
        services.AddHostedService<ServiceBusJobProcessor>();

        // ============================================================================
        // Job Handlers (Scoped - created per job execution)
        // ============================================================================
        services.AddScoped<DocumentCreatedJobHandler>();
        services.AddScoped<DocumentUpdatedJobHandler>();
        services.AddScoped<DocumentDeletedJobHandler>();

        // ============================================================================
        // Idempotency Service (Singleton - shared state tracking)
        // ============================================================================
        services.AddSingleton<IdempotencyService>();

        return services;
    }
}
```

**Key Points**:
- **Background processors**: HostedService (Singleton lifetime)
- **Job handlers**: Scoped (created per job execution)
- **IdempotencyService**: Singleton (shared state)

**Note**: If job handlers don't exist, remove those registrations.

---

### Step 4: Add Options Configuration Extensions (Optional)

**File**: `src/api/Spe.Bff.Api/Extensions/OptionsExtensions.cs`

```csharp
using Microsoft.Extensions.Options;

namespace Spe.Bff.Api.Extensions;

/// <summary>
/// Helper extensions for options configuration with validation.
/// </summary>
public static class OptionsExtensions
{
    /// <summary>
    /// Add strongly-typed options with validation and validation on start.
    /// </summary>
    public static IServiceCollection AddOptionsWithValidation<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = null)
        where TOptions : class
    {
        sectionName ??= typeof(TOptions).Name.Replace("Options", "");

        services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
```

**Usage**:
```csharp
// Instead of:
services.AddOptions<DataverseOptions>()
    .Bind(configuration.GetSection("Dataverse"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Use:
services.AddOptionsWithValidation<DataverseOptions>(configuration);
```

---

## Validation

### Build Check
```bash
dotnet build
# Expected: Success, 0 warnings
```

### File Structure Check
```bash
# Verify all extension files created
ls -la src/api/Spe.Bff.Api/Extensions/

# Expected files:
# - SpaarkeCore.Extensions.cs
# - DocumentsModule.Extensions.cs
# - WorkersModule.Extensions.cs
# - OptionsExtensions.cs (optional)
```

### Service Registration Review
```bash
# Count services in each module
grep "services.Add" src/api/Spe.Bff.Api/Extensions/SpaarkeCore.Extensions.cs | wc -l
grep "services.Add" src/api/Spe.Bff.Api/Extensions/DocumentsModule.Extensions.cs | wc -l
grep "services.Add" src/api/Spe.Bff.Api/Extensions/WorkersModule.Extensions.cs | wc -l

# Total should match current Program.cs service count
```

### Captive Dependency Check
```bash
# Verify no Singleton injects Scoped
# Example: Check GraphClientFactory (Singleton) doesn't inject SpeFileStore (Scoped)
grep -A 10 "class GraphClientFactory" src/api/Spe.Bff.Api/Infrastructure/GraphClientFactory.cs
# Verify constructor parameters are Singleton or Transient only
```

---

## Checklist

- [ ] **Pre-flight**: Verified Phase 2 complete
- [ ] **Pre-flight**: Counted current Program.cs DI lines
- [ ] **Pre-flight**: Extensions folder created
- [ ] Created `SpaarkeCore.Extensions.cs`
- [ ] Created `DocumentsModule.Extensions.cs`
- [ ] Created `WorkersModule.Extensions.cs`
- [ ] Created `OptionsExtensions.cs` (optional)
- [ ] All services have correct lifetime (Singleton vs Scoped)
- [ ] No captive dependencies (Singleton → Scoped)
- [ ] XML documentation comments added
- [ ] ADR-010 reference in comments
- [ ] Build succeeds: `dotnet build`
- [ ] All existing services accounted for

---

## Expected Results

**Before**:
- ❌ Program.cs has 80+ lines of DI registrations
- ❌ Services scattered across Program.cs
- ❌ No clear feature boundaries

**After**:
- ✅ Three feature module files created
- ✅ Services organized by feature area
- ✅ Clear separation of concerns
- ✅ Ready for Program.cs simplification (Task 3.2)

**Metrics**:
- Feature modules created: 3
- Services organized: ~15-20
- Lines per module: ~30-50

---

## Lifetime Decision Reference

### Singleton (Shared, Stateless, Expensive)
| Service | Justification |
|---------|---------------|
| `AuthorizationService` | Stateless rule evaluation |
| `IAuthorizationRule` implementations | Stateless logic |
| `IGraphClientFactory` | Factory pattern, stateless |
| `DataverseServiceClientImpl` | Connection pooling, expensive |
| `IdempotencyService` | Shared state tracking |
| `IDistributedCache` | Shared cache |

### Scoped (Per-Request Context)
| Service | Justification |
|---------|---------------|
| `SpeFileStore` | Per-request user context |
| `UploadSessionManager` | Per-request upload tracking |
| `IAccessDataSource` | Per-request Dataverse queries |
| `DataverseWebApiService` | Per-request queries |
| `RequestCache` | Per-request memoization |
| Job handlers | Per-job execution |

### Transient (Avoid - Use Scoped Instead)
- ❌ **Never use Transient** in ASP.NET Core
- Scoped provides same behavior with better performance

---

## Anti-Pattern Verification

### ✅ Avoided: Captive Dependency
```bash
# Verify GraphClientFactory (Singleton) doesn't inject SpeFileStore (Scoped)
grep -A 5 "GraphClientFactory(" src/api/Spe.Bff.Api/Infrastructure/GraphClientFactory.cs
# Expected: Should only inject Singleton or Transient services
```

**Why**: Singleton capturing Scoped leads to stale data and memory leaks

### ✅ Organized by Feature
```bash
# Verify services grouped by feature
ls src/api/Spe.Bff.Api/Extensions/
# Expected: SpaarkeCore (auth), DocumentsModule (SPE), WorkersModule (jobs)
```

**Why**: ADR-010 - Feature modules for clear boundaries

---

## Troubleshooting

### Issue: "Service X not found" when building

**Cause**: Service class doesn't exist or wrong namespace

**Fix**: Verify service exists:
```bash
find src/ -name "ServiceName.cs"
```

If service doesn't exist, remove from module registration.

### Issue: Circular dependency exception

**Cause**: Two services inject each other

**Fix**: Refactor to use events or mediator pattern:
```csharp
// Instead of: A → B and B → A
// Use: A → IEventBus ← B
```

### Issue: Captive dependency warning at runtime

**Cause**: Singleton injecting Scoped service

**Fix**: Change Singleton to Scoped or remove Scoped dependency:
```csharp
// Wrong:
services.AddSingleton<MyService>(); // Injects IAccessDataSource (Scoped) ❌

// Right:
services.AddScoped<MyService>(); // Can inject IAccessDataSource ✅
```

---

## Context Verification

Before marking complete, verify:
- [ ] ✅ Three feature module files created
- [ ] ✅ All services organized by feature
- [ ] ✅ Correct lifetimes used (Singleton vs Scoped)
- [ ] ✅ No captive dependencies
- [ ] ✅ Build succeeds
- [ ] ✅ Task stayed focused (did NOT refactor Program.cs - that's Task 3.2)

**If any item unchecked**: Review and fix before proceeding to Task 3.2

---

## Commit Message

```bash
git add src/api/Spe.Bff.Api/Extensions/
git commit -m "feat(di): create feature module extensions per ADR-010

- Create SpaarkeCore.Extensions.cs (authorization, caching)
- Create DocumentsModule.Extensions.cs (SPE, Graph, Dataverse)
- Create WorkersModule.Extensions.cs (background services, jobs)
- Create OptionsExtensions.cs (configuration helpers)
- Organize ~20 services by feature area
- Use correct lifetimes (Singleton vs Scoped)
- Add XML documentation comments

Preparation: Ready for Program.cs simplification (Task 3.2)
ADR Compliance: ADR-010 (Feature Modules)
Anti-Patterns Avoided: Captive dependency, scattered registrations
Task: Phase 3, Task 1"
```

---

## Next Task

➡️ [Phase 3 - Task 2: Refactor Program.cs](phase-3-task-2-refactor-program.md)

**What's next**: Simplify Program.cs to ~20 lines using feature modules created in this task

---

## Related Resources

- **Patterns**:
  - [di-feature-module.md](../patterns/di-feature-module.md)
- **Anti-Patterns**:
  - [anti-pattern-captive-dependency.md](../patterns/anti-pattern-captive-dependency.md)
  - [anti-pattern-interface-proliferation.md](../patterns/anti-pattern-interface-proliferation.md)
- **Architecture**: [TARGET-ARCHITECTURE.md](../TARGET-ARCHITECTURE.md#dependency-injection-minimalism)
- **ADR**: [ARCHITECTURAL-DECISIONS.md](../ARCHITECTURAL-DECISIONS.md) - ADR-010
