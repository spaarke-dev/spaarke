# Target SDAP BFF API Architecture (After Refactoring)

## System Overview

### High-Level Flow (Same as Current)
```User (Browser)
↓
PCF Control (MSAL.js auth)
↓ JWT Token
BFF API (Spe.Bff.Api)
↓
External Services (Graph, Dataverse, Service Bus, Redis)

**Changes:** Only internal BFF API structure changes, no external integration changes

---

## Target Solutions

### Solution 1: Simplified Service Layer (3 layers)

**Target Call Chain:**
OBOEndpoints.cs
↓ injects SpeFileStore (concrete class)
SpeFileStore.cs
↓ injects IGraphClientFactory
GraphClientFactory.cs
↓ creates GraphServiceClient
↓ calls Microsoft Graph API


**Benefits:**
- 50% fewer layers (3 instead of 6)
- Easier to debug (3 files instead of 6)
- Easier to test (fewer mocks needed)
- Complies with ADR-007

**Example Code:**
```csharp// OBOEndpoints.cs (simplified)
public static async Task<IResult> UploadFile(
string containerId,
string fileName,
Stream fileContent,
SpeFileStore fileStore)  // ← Concrete class, no interface
{
var result = await fileStore.UploadFileAsync(
containerId,
fileName,
fileContent,
userToken);return Results.Ok(result);
}

---

### Solution 2: Correct App Registration

**Target Configuration:**
```json{
"API_APP_ID": "1e40baad-e065-4aea-a8d4-4b7ab273458c","AzureAd": {
"ClientId": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
"Audience": "api://1e40baad-e065-4aea-a8d4-4b7ab273458c",
"TenantId": "a221a95e-6abc-4434-aecc-e48338a1b2f2"
},"Dataverse": {
"ServiceUrl": "@Microsoft.KeyVault(...)",
"ClientId": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
"ClientSecret": "@Microsoft.KeyVault(...)"
}
}

**Benefits:**
- Correct app used for all authentication
- Clear separation: PCF client (170c98e1...) vs BFF API (1e40baad...)
- Easier to debug auth issues

---

### Solution 3: Token Caching with Redis

**Target Flow:**Request 1 (Cache Miss - 5% of requests):

User token arrives
Compute hash of user token
Check Redis cache → MISS
Perform OBO exchange (~200ms)
Store Graph token in Redis (TTL: 55 minutes)
Call Graph API
Request 2-20 (Cache Hit - 95% of requests):

User token arrives
Compute hash of user token
Check Redis cache → HIT (~5ms)
Use cached Graph token
Call Graph API


**Code Example:**
```csharp// GraphClientFactory.cs (with caching)
public async Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userToken)
{
var tokenHash = _tokenCache.ComputeTokenHash(userToken);// Check cache first
var cachedToken = await _tokenCache.GetTokenAsync(tokenHash);
if (cachedToken != null)
{
    // Cache hit - return immediately
    return CreateGraphClientWithToken(cachedToken);
}// Cache miss - perform OBO
var result = await _cca.AcquireTokenOnBehalfOf(...).ExecuteAsync();// Cache for 55 minutes (5-min buffer before 1-hour expiration)
await _tokenCache.SetTokenAsync(tokenHash, result.AccessToken, TimeSpan.FromMinutes(55));return CreateGraphClientWithToken(result.AccessToken);
}

**Benefits:**
- 95% reduction in OBO calls (from 100/100 to 5/100 requests)
- 97% reduction in OBO latency (from 200ms to 5ms average)
- Lower Azure AD load (reduced throttling risk)
- Complies with ADR-009

---

### Solution 4: Singleton ServiceClient

**Target Registration:**
```csharp// Program.cs or DocumentsModule.Extensions.cs
builder.Services.AddSingleton<DataverseServiceClientImpl>(sp =>
{
var options = sp.GetRequiredService<IOptions<DataverseOptions>>().Value;var connectionString = 
    $"AuthType=ClientSecret;" +
    $"Url={options.ServiceUrl};" +
    $"ClientId={options.ClientId};" +
    $"ClientSecret={options.ClientSecret};" +
    $"RequireNewInstance=false;";  // Enable connection poolingreturn new DataverseServiceClientImpl(connectionString);
});

**Benefits:**
- Connection created once at startup
- Reused across all requests
- 100% elimination of 500ms initialization overhead
- Built-in connection pooling

---

### Solution 5: Remove UAMI Confusion

**Target Code:**
```csharp// GraphClientFactory.cs (simplified)
public GraphClientFactory(IConfiguration configuration, ILogger<GraphClientFactory> logger)
{
_logger = logger;var apiAppId = configuration["API_APP_ID"] 
    ?? throw new InvalidOperationException("API_APP_ID not configured");
var clientSecret = configuration["API_CLIENT_SECRET"] 
    ?? throw new InvalidOperationException("API_CLIENT_SECRET not configured");
var tenantId = configuration["TENANT_ID"] 
    ?? throw new InvalidOperationException("TENANT_ID not configured");// Single auth path: client secret only
_cca = ConfidentialClientApplicationBuilder
    .Create(apiAppId)
    .WithClientSecret(clientSecret)
    .WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
    .Build();
}

**Benefits:**
- No UAMI_CLIENT_ID logic
- No branching for different auth types
- Simpler, easier to understand
- Works in all environments (local, Azure)

---

## Target File Structuresrc/api/Spe.Bff.Api/
├── Program.cs                              (~100 lines, ~20 lines of DI)
│
├── Extensions/                             ✨ NEW
│   ├── SpaarkeCore.Extensions.cs          (Authorization, caching, core services)
│   ├── DocumentsModule.Extensions.cs      (SPE, Graph, Dataverse services)
│   └── WorkersModule.Extensions.cs        (Background services)
│
├── Storage/                                ✨ NEW
│   └── SpeFileStore.cs                    (Concrete class, no interface)
│
├── Services/                               ✨ CLEANED UP
│   └── GraphTokenCache.cs                 ✨ NEW (Phase 4)
│
├── Endpoints/                              (Updated to use SpeFileStore)
│   ├── OBOEndpoints.cs
│   ├── DocumentsEndpoints.cs
│   ├── DataverseDocumentsEndpoints.cs
│   ├── UploadEndpoints.cs
│   ├── PermissionsEndpoints.cs
│   └── UserEndpoints.cs
│
├── Infrastructure/                         (Simplified)
│   ├── GraphClientFactory.cs              (UAMI removed, cache added)
│   ├── GraphHttpMessageHandler.cs         (unchanged)
│   └── UploadSessionManager.cs            (unchanged)
│
├── BackgroundServices/                     (unchanged)
│   ├── DocumentEventProcessor.cs
│   └── ServiceBusJobProcessor.cs
│
├── Telemetry/                              ✨ NEW (Phase 4, optional)
│   └── CacheMetrics.cs
│
└── Configuration/                          (unchanged)
├── DataverseOptions.cs
└── GraphResilienceOptions.csDELETED FILES:
├── Services/IResourceStore.cs              ❌ DELETED
├── Services/SpeResourceStore.cs            ❌ DELETED
├── Services/ISpeService.cs                 ❌ DELETED
├── Services/OboSpeService.cs               ❌ DELETED
├── Services/IDataverseSecurityService.cs   ❌ DELETED
├── Services/DataverseSecurityService.cs    ❌ DELETED
├── Services/IUacService.cs                 ❌ DELETED
└── Services/UacService.cs                  ❌ DELETED

---

## Target DI Registrations (Program.cs)

**Target State (~20 lines):**
```csharpvar builder = WebApplication.CreateBuilder(args);// ============================================================================
// Feature Modules
// ============================================================================
builder.Services.AddSpaarkeCore(builder.Configuration);
builder.Services.AddDocumentsModule();
builder.Services.AddWorkersModule();// ============================================================================
// Options Pattern (Strongly-Typed Configuration)
// ============================================================================
builder.Services.AddOptions<DataverseOptions>()
.Bind(builder.Configuration.GetSection(DataverseOptions.SectionName))
.ValidateDataAnnotations()
.ValidateOnStart();builder.Services.AddOptions<GraphResilienceOptions>()
.Bind(builder.Configuration.GetSection(GraphResilienceOptions.SectionName))
.ValidateDataAnnotations()
.ValidateOnStart();builder.Services.AddOptions<ServiceBusOptions>()
.Bind(builder.Configuration.GetSection(ServiceBusOptions.SectionName))
.ValidateDataAnnotations()
.ValidateOnStart();// ============================================================================
// ASP.NET Core Framework Services
// ============================================================================
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
.AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));builder.Services.AddAuthorization();
builder.Services.AddCors(/* ... */);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();var app = builder.Build();
// ... middleware and endpoint mapping

**Benefits:**
- 75% reduction in lines (from 80 to 20)
- Clear sections with comments
- Easy to see all services at a glance
- Complies with ADR-010

---

## Target Performance Metrics

| Operation | Target Latency | Improvement vs Current | How Achieved |
|-----------|---------------|------------------------|--------------|
| File Upload (small) | ~150ms | 78% faster | Token caching (saves 200ms) |
| File Download | ~100ms | 80% faster | Token caching (saves 200ms) |
| Dataverse Query | ~50ms | 92% faster | Singleton ServiceClient (saves 500ms) |
| Health Check | ~50ms | 83% faster | Singleton ServiceClient (saves 250ms) |

### Cache Performance

| Metric | Target | Impact |
|--------|--------|--------|
| Cache Hit Rate | 95% | Most requests skip OBO exchange |
| Cache Miss Latency | 200ms | Same as current (OBO exchange) |
| Cache Hit Latency | 5ms | 97.5% faster than OBO |
| Average OBO Latency | 15ms | (95% × 5ms) + (5% × 200ms) |

---

## Feature Module Contents

### SpaarkeCore.Extensions.cs

Registers:
- `AuthorizationService` (singleton)
- `IAccessDataSource` → `DataverseAccessDataSource` (scoped)
- All `IAuthorizationRule` implementations (singletons)
- Redis distributed cache
- `RequestCache` (per-request cache)

### DocumentsModule.Extensions.cs

Registers:
- `SpeFileStore` (scoped, concrete class)
- `IGraphClientFactory` → `GraphClientFactory` (singleton)
- `GraphTokenCache` (singleton)
- `UploadSessionManager` (scoped)
- `DataverseServiceClientImpl` (singleton)

### WorkersModule.Extensions.cs

Registers:
- `DocumentEventProcessor` (hosted service)
- `ServiceBusJobProcessor` (hosted service)
- `IdempotencyService` (singleton)
- Job handlers (scoped)

---

## Target Interface Inventory

### Interfaces to KEEP (3 total)

1. **IGraphClientFactory** - Factory pattern, creates different client types
2. **IAccessDataSource** - Seam for Dataverse access data (ADR-003)
3. **IAuthorizationRule** - Collection pattern, multiple implementations

### Interfaces to REMOVE (8 total)

1. **IResourceStore** - Only one implementation, remove
2. **ISpeService** - Only one implementation, remove
3. **IOboSpeService** - Duplicate of ISpeService, remove
4. **IDataverseSecurityService** - Only one implementation, remove
5. **IUacService** - Only one implementation, remove
6. **IDataverseService** - Only one implementation, remove
7. Plus any other single-implementation interfaces found

---

## ADR Compliance Matrix

| ADR | Requirement | Current | Target | Status |
|-----|-------------|---------|--------|--------|
| **ADR-007** | Single SPE storage facade | ❌ IResourceStore + ISpeService | ✅ SpeFileStore (concrete) | ✅ Fixed |
| **ADR-007** | No generic interfaces | ❌ Generic IResourceStore | ✅ Focused SpeFileStore | ✅ Fixed |
| **ADR-007** | No Graph SDK types in controllers | ⚠️ Some leakage | ✅ Only DTOs returned | ✅ Fixed |
| **ADR-009** | Redis-first caching | ❌ No caching | ✅ GraphTokenCache + Redis | ✅ Fixed |
| **ADR-009** | No hybrid L1/L2 cache | ✅ N/A | ✅ Redis only | ✅ Compliant |
| **ADR-009** | Short TTL for security data | ✅ N/A | ✅ 55-minute TTL | ✅ Compliant |
| **ADR-010** | Register concretes | ❌ 10+ interfaces | ✅ 3 interfaces only | ✅ Fixed |
| **ADR-010** | Feature modules | ❌ Scattered registrations | ✅ 3 feature modules | ✅ Fixed |
| **ADR-010** | ~15 DI lines | ❌ 80+ lines | ✅ ~20 lines | ✅ Fixed |

---

## Success Metrics Summary

### Code Complexity

| Metric | Before | After | Improvement |
|--------|--------|-------|-------------|
| Service Registrations | 20+ | ~15 | 25% reduction |
| Interface Count | 10 | 3 | 70% reduction |
| Call Chain Depth | 6 layers | 3 layers | 50% reduction |
| Program.cs DI Lines | 80+ | ~20 | 75% reduction |
| Total Service Files | 18 | 10 | 44% reduction |

### Performance

| Metric | Before | After | Improvement |
|--------|--------|-------|-------------|
| Avg Request Latency | 700ms | 150ms | 78% faster |
| OBO Exchange Rate | 100% | 5% | 95% reduction |
| Dataverse Init Overhead | 500ms/req | 0ms | 100% elimination |
| Cache Hit Latency | N/A | 5ms | New capability |

### Maintainability

| Aspect | Before | After |
|--------|--------|-------|
| Understandability | Low (6-layer chains) | High (3-layer chains) |
| Testability | Medium (many mocks) | High (few mocks) |
| Debuggability | Hard (trace through 6 files) | Easy (trace through 3 files) |
| Extensibility | Medium (find right interface) | Easy (clear boundaries) |

---

## Target Architecture Principles

### 1. Simplicity Over Abstraction
- Concrete classes unless interface truly needed
- No premature abstraction
- YAGNI (You Aren't Gonna Need It)

### 2. Performance by Default
- Singleton for expensive resources (ServiceClient)
- Caching for expensive operations (OBO exchange)
- Connection pooling enabled

### 3. Explicit Over Implicit
- Clear feature boundaries (modules)
- Obvious service registrations
- Self-documenting code structure

### 4. ADR Compliance
- Every decision traceable to an ADR
- No "creative interpretation"
- Consistent patterns throughout

---

## Migration Path Summary

| Phase | Goal | Key Changes | Risk | Duration |
|-------|------|-------------|------|----------|
| Phase 1 | Fix config & critical issues | App ID, UAMI, ServiceClient lifetime | Low | 4-6 hours |
| Phase 2 | Simplify service layer | Create SpeFileStore, remove interfaces | Medium | 12-16 hours |
| Phase 3 | Organize DI | Feature modules, simplify Program.cs | Low | 4-6 hours |
| Phase 4 | Add caching | GraphTokenCache, Redis integration | Low | 4-6 hours |

**Total: ~40 hours (1 week sprint)**