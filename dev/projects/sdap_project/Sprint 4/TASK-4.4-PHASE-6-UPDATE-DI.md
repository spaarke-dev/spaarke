# Task 4.4 - Phase 6: Update Dependency Injection

**Sprint:** 4
**Phase:** 6 of 7
**Estimated Effort:** 1 hour
**Dependencies:** Phase 5 complete (interface files deleted)
**Status:** Not Started

---

## Objective

Update DI container registration in `DocumentsModule.cs` and `SpaarkeCore.cs` to remove interface-based registrations and register the new `UserOperations` class.

---

## Files to Modify

### 1. DocumentsModule.cs
**Path:** `src/api/Spe.Bff.Api/Infrastructure/DI/DocumentsModule.cs`

### 2. SpaarkeCore.cs
**Path:** `src/api/Spe.Bff.Api/Infrastructure/DI/SpaarkeCore.cs`

---

## Changes Required

### Step 1: Update DocumentsModule.cs

**Current Registration (to be removed):**
```csharp
// Remove these interface-based registrations
builder.Services.AddScoped<ISpeService, SpeService>();
builder.Services.AddScoped<IOboSpeService, OboSpeService>();
```

**New Registration (add):**
```csharp
// Add UserOperations registration
builder.Services.AddScoped<UserOperations>();
```

**Complete Updated Method:**
```csharp
public static void AddDocumentsModule(this WebApplicationBuilder builder)
{
    // Graph client factory (unchanged)
    builder.Services.AddSingleton<IGraphClientFactory, GraphClientFactory>();

    // Operation classes (unchanged, but UserOperations is new)
    builder.Services.AddScoped<ContainerOperations>();
    builder.Services.AddScoped<DriveItemOperations>();
    builder.Services.AddScoped<UploadSessionManager>();
    builder.Services.AddScoped<UserOperations>(); // NEW

    // Facade (unchanged)
    builder.Services.AddScoped<SpeFileStore>();

    // NO interface registrations (ISpeService, IOboSpeService removed per ADR-007)
}
```

---

### Step 2: Verify SpaarkeCore.cs

**Current Registration:**
The `SpaarkeCore.cs` file should already have the operation classes registered. Verify no interface-based registrations exist.

**Expected Content:**
```csharp
public static class SpaarkeCore
{
    public static void AddSpaarkeCore(this WebApplicationBuilder builder)
    {
        // Dataverse services
        builder.Services.AddScoped<IDataverseService, DataverseWebApiService>();
        builder.Services.AddScoped<IAccessDataSource, DataverseAccessDataSource>();
        builder.Services.AddHttpClient<DataverseWebApiClient>();

        // Authorization
        builder.Services.AddScoped<IAuthorizationService, Spaarke.Core.Auth.AuthorizationService>();
        builder.Services.AddScoped<IAuthorizationRule, TeamMembershipRule>();
        builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();

        // NO Graph/SPE registrations here (handled in DocumentsModule)
    }
}
```

**Action:** No changes needed if no interface-based registrations exist.

---

## Step-by-Step Implementation

### 1. Read Current DocumentsModule.cs
```bash
# Review current state
cat src/api/Spe.Bff.Api/Infrastructure/DI/DocumentsModule.cs
```

### 2. Remove Interface Registrations
Search for and remove these lines (if they exist):
```csharp
builder.Services.AddScoped<ISpeService, SpeService>();
builder.Services.AddScoped<IOboSpeService, OboSpeService>();
```

### 3. Add UserOperations Registration
Add this line with the other operation class registrations:
```csharp
builder.Services.AddScoped<UserOperations>();
```

### 4. Verify Final State
The final `AddDocumentsModule` method should look like this:

```csharp
using Spe.Bff.Api.Infrastructure.Graph;

namespace Spe.Bff.Api.Infrastructure.DI;

public static class DocumentsModule
{
    /// <summary>
    /// Registers all document management services (Graph, SPE, file operations).
    /// Implements ADR-007 storage seam with modular operation classes and SpeFileStore facade.
    /// </summary>
    public static void AddDocumentsModule(this WebApplicationBuilder builder)
    {
        // Graph client factory (creates authenticated Graph SDK clients)
        builder.Services.AddSingleton<IGraphClientFactory, GraphClientFactory>();

        // Modular operation classes (Sprint 3 refactor)
        builder.Services.AddScoped<ContainerOperations>();
        builder.Services.AddScoped<DriveItemOperations>();
        builder.Services.AddScoped<UploadSessionManager>();
        builder.Services.AddScoped<UserOperations>(); // Task 4.4 - added for OBO user operations

        // Facade (single public entry point per ADR-007)
        builder.Services.AddScoped<SpeFileStore>();

        // NO interface-based registrations per ADR-007
        // (ISpeService, SpeService, IOboSpeService, OboSpeService removed in Task 4.4)
    }
}
```

---

## Verification

### 1. Build Verification
```bash
dotnet build src/api/Spe.Bff.Api/Spe.Bff.Api.csproj
```

**Expected Result:** 0 errors

### 2. Grep Verification
```bash
# Verify no interface references in DI registrations
grep -r "ISpeService" src/api/Spe.Bff.Api/Infrastructure/DI/
grep -r "IOboSpeService" src/api/Spe.Bff.Api/Infrastructure/DI/

# Should return: no matches
```

### 3. Startup Verification
Check that application starts without DI resolution errors:
```bash
dotnet run --project src/api/Spe.Bff.Api/Spe.Bff.Api.csproj --no-build
```

**Expected Result:** Application starts, no `InvalidOperationException` about missing service registrations

---

## Acceptance Criteria

- [ ] `DocumentsModule.cs` has no interface-based registrations
- [ ] `UserOperations` is registered in DI container
- [ ] Build succeeds with 0 errors
- [ ] No grep matches for interface names in DI files
- [ ] Application starts without DI errors

---

## Rollback Plan

If issues occur:
1. Revert `DocumentsModule.cs` to previous state
2. Restore interface registrations temporarily
3. Investigate missing dependencies
4. Re-apply changes after resolving issues

---

## Next Phase

**Phase 7:** Build & Test - Full solution build, unit tests, integration tests
