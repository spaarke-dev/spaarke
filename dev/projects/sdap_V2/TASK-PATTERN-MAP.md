# Task → Pattern Quick Map

**Purpose**: Instantly find which pattern file to use for your current task
**Usage**: Find your task → Open pattern file → Copy code → Done

---

## Phase 1: Critical Fixes

### Task 1.1: Fix Dataverse Connection
**Problem**: Using Managed Identity instead of client secret
**Pattern**: [patterns/service-dataverse-connection.md](patterns/service-dataverse-connection.md)
**Time**: 15 minutes
**Apply to**: `src/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs`

**Quick Fix**:
```csharp
// Replace MI code with:
var connectionString =
    $"AuthType=ClientSecret;" +
    $"Url={config.ServiceUrl};" +
    $"ClientId={config.ClientId};" +
    $"ClientSecret={config.ClientSecret};" +
    $"RequireNewInstance=false;";

_serviceClient = new ServiceClient(connectionString);
```

---

### Task 1.2: Remove UAMI_CLIENT_ID
**Problem**: GraphClientFactory references non-existent UAMI config
**Pattern**: [patterns/service-graph-client-factory.md](patterns/service-graph-client-factory.md)
**Time**: 10 minutes
**Apply to**: `src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs`

**Quick Fix**:
- Remove: `_uamiClientId` field
- Remove: Lines checking for UAMI
- Keep: Client secret authentication only

---

### Task 1.3: Organize DI Registrations
**Problem**: Program.cs has 680+ lines of scattered registrations
**Pattern**: [patterns/di-feature-module.md](patterns/di-feature-module.md)
**Time**: 30 minutes
**Create**: `src/api/Spe.Bff.Api/Extensions/*ModuleExtensions.cs`

**⚠️ Avoid**: [patterns/anti-pattern-interface-proliferation.md](patterns/anti-pattern-interface-proliferation.md)
**⚠️ Avoid**: [patterns/anti-pattern-captive-dependency.md](patterns/anti-pattern-captive-dependency.md)

**Quick Fix**:
1. Create `SpaarkeCoreExtensions.cs`
2. Create `DocumentsModuleExtensions.cs`
3. Create `WorkersModuleExtensions.cs`
4. Update `Program.cs` to call modules
5. Register concrete classes (not interfaces unless factory/collection)
6. Follow lifetime hierarchy (Singleton → Singleton, Scoped → Any)

---

## Phase 2: Token Caching

### Task 2.1: Implement GraphTokenCache
**Goal**: Add Redis caching for 97% latency reduction
**Pattern**: [patterns/service-graph-token-cache.md](patterns/service-graph-token-cache.md)
**Time**: 20 minutes
**Create**: `src/api/Spe.Bff.Api/Services/GraphTokenCache.cs`

---

### Task 2.2: Integrate Cache with Factory
**Goal**: Add caching to OBO flow
**Pattern**: [patterns/service-graph-client-factory.md](patterns/service-graph-client-factory.md)
**Time**: 15 minutes
**Apply to**: `src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs`

---

## Common Development Tasks

### Add File Upload Endpoint
**Patterns Needed**:
1. [patterns/endpoint-file-upload.md](patterns/endpoint-file-upload.md) - Main endpoint
2. [patterns/dto-file-upload-result.md](patterns/dto-file-upload-result.md) - Return type
3. [patterns/error-handling-standard.md](patterns/error-handling-standard.md) - Error cases

**⚠️ Avoid**: [patterns/anti-pattern-leaking-sdk-types.md](patterns/anti-pattern-leaking-sdk-types.md)

**Time**: 25 minutes total
**Apply to**: `src/api/Spe.Bff.Api/Api/OBOEndpoints.cs`

**Key Point**: Never return `DriveItem` - always return `FileUploadResult` DTO

---

### Add File Download Endpoint
**Patterns Needed**:
1. [patterns/endpoint-file-download.md](patterns/endpoint-file-download.md) - Main endpoint
2. [patterns/error-handling-standard.md](patterns/error-handling-standard.md) - Error cases

**Time**: 15 minutes total
**Apply to**: `src/api/Spe.Bff.Api/Api/OBOEndpoints.cs`

---

### Add New DTO
**Pattern**: [patterns/dto-file-upload-result.md](patterns/dto-file-upload-result.md)
**Time**: 5 minutes
**Create in**: `src/api/Spe.Bff.Api/Models/`

**Quick Template**:
```csharp
public record YourDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public DateTimeOffset? CreatedDateTime { get; init; }
}
```

---

### Add Error Handling
**Pattern**: [patterns/error-handling-standard.md](patterns/error-handling-standard.md)
**Time**: 5 minutes
**Apply to**: Any endpoint handler

**Quick Copy**:
```csharp
catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
{
    logger.LogWarning("Resource not found: {ResourceId}", resourceId);
    return Results.NotFound(new { error = "Resource not found" });
}
catch (Exception ex)
{
    logger.LogError(ex, "Operation failed");
    return Results.Problem("Operation failed", statusCode: 500);
}
```

---

## Troubleshooting Tasks

### Fix 401 Unauthorized
**Problem**: Token issues
**Patterns**:
- Check: [patterns/service-graph-client-factory.md](patterns/service-graph-client-factory.md) - OBO flow
- Check: [patterns/error-handling-standard.md](patterns/error-handling-standard.md) - Token extraction

---

### Fix 403 Forbidden
**Problem**: Permission issues
**Patterns**:
- Check: [patterns/service-dataverse-connection.md](patterns/service-dataverse-connection.md) - App user setup
- Check: [patterns/error-handling-standard.md](patterns/error-handling-standard.md) - Access denied handling

---

### Fix 500 Internal Server Error
**Problem**: Unexpected errors
**Patterns**:
- Add: [patterns/error-handling-standard.md](patterns/error-handling-standard.md) - Proper error handling
- Check logs for specific error

---

## Decision Tree

```
┌─ I need to...
│
├─ Create an endpoint
│  ├─ Upload file → patterns/endpoint-file-upload.md
│  │  ⚠️ Avoid → patterns/anti-pattern-leaking-sdk-types.md
│  └─ Download file → patterns/endpoint-file-download.md
│
├─ Create a DTO
│  └─ Any DTO → patterns/dto-file-upload-result.md (template)
│     ⚠️ Never return DriveItem, Entity
│
├─ Setup a service
│  ├─ Graph API → patterns/service-graph-client-factory.md
│  ├─ Token caching → patterns/service-graph-token-cache.md
│  └─ Dataverse → patterns/service-dataverse-connection.md
│
├─ Organize DI
│  └─ Feature modules → patterns/di-feature-module.md
│     ⚠️ Avoid → patterns/anti-pattern-interface-proliferation.md
│     ⚠️ Avoid → patterns/anti-pattern-captive-dependency.md
│
└─ Handle errors
   └─ Standard errors → patterns/error-handling-standard.md
```

---

## Pattern Priority by Task Type

### High-Frequency Tasks (Use Daily)
1. **error-handling-standard.md** - Every endpoint needs this
2. **endpoint-file-upload.md** - Common operation
3. **endpoint-file-download.md** - Common operation

### Medium-Frequency Tasks (Use Weekly)
4. **dto-file-upload-result.md** - New DTOs occasionally
5. **service-graph-client-factory.md** - Reference for OBO issues

### Low-Frequency Tasks (Setup Once)
6. **service-graph-token-cache.md** - One-time setup
7. **service-dataverse-connection.md** - One-time setup
8. **di-feature-module.md** - One-time organization

---

## Workflow Examples

### Example 1: "I need to add file delete endpoint"

```bash
Step 1: Find similar pattern
→ Open: patterns/endpoint-file-upload.md (reference)

Step 2: Copy structure
→ Copy: MapPut → Change to MapDelete
→ Copy: Error handling (same errors apply)

Step 3: Adapt code
→ Change: UploadFile → DeleteFile
→ Change: request.Body → no body needed
→ Change: Returns Ok() → Returns NoContent()

Step 4: Apply
→ File: src/api/Spe.Bff.Api/Api/OBOEndpoints.cs

Time: 10 minutes
```

---

### Example 2: "OBO flow is slow, need caching"

```bash
Step 1: Implement cache
→ Open: patterns/service-graph-token-cache.md
→ Create: src/api/Spe.Bff.Api/Services/GraphTokenCache.cs
→ Time: 20 minutes

Step 2: Integrate with factory
→ Open: patterns/service-graph-client-factory.md
→ Edit: GraphClientFactory.cs to add cache check
→ Time: 15 minutes

Step 3: Register in DI
→ Open: patterns/di-feature-module.md
→ Add: services.AddSingleton<GraphTokenCache>();
→ Time: 2 minutes

Total Time: 37 minutes
Result: 97% latency reduction
```

---

### Example 3: "Dataverse connection failing"

```bash
Step 1: Check authentication
→ Open: patterns/service-dataverse-connection.md

Step 2: Find the problem
→ Section: "❌ WRONG: Managed Identity"
→ See: Current broken implementation

Step 3: Apply fix
→ Section: "✅ CORRECT: Client Secret"
→ Copy: Connection string code
→ Replace: Current implementation

Time: 10 minutes
```

---

## Quick Commands

```bash
# Find all pattern files
ls patterns/*.md

# Open most-used patterns (split screen)
code patterns/endpoint-file-upload.md patterns/error-handling-standard.md

# Search for specific pattern
grep -l "Token Cache" patterns/*.md

# See pattern count
ls patterns/*.md | wc -l
```

---

## Tips for Maximum Speed

1. **Keep README.md Open**: Quick navigation to patterns
2. **Use Split Screen**: Pattern (right) + Your code (left)
3. **Copy Full Blocks**: Don't type, copy and adapt
4. **Follow Checklists**: Every pattern has verification checklist
5. **Trust Time Estimates**: Patterns are tested, times are accurate

---

**Related Files**:
- Pattern Library: [patterns/README.md](patterns/README.md)
- Full Reference: [CODE-PATTERNS.md](CODE-PATTERNS.md)
- Anti-Patterns: [ANTI-PATTERNS.md](ANTI-PATTERNS.md)
- ADRs: [ARCHITECTURAL-DECISIONS.md](ARCHITECTURAL-DECISIONS.md)
- Target Architecture: [TARGET-ARCHITECTURE.md](TARGET-ARCHITECTURE.md)

**Last Updated**: 2025-10-13
**Pattern Count**: 11 patterns (8 correct + 3 anti-patterns)
**Status**: Ready for vibe coding 🎯
