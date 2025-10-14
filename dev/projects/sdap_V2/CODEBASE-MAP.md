# SDAP Codebase Map (AI Vibe Coding Resource)

**Purpose**: Quick navigation for AI-assisted development
**Usage**: Find file → Understand purpose → Know what patterns to apply
**Last Updated**: 2025-10-13 (Post-refactoring target state)

---

## 🎯 Quick File Finder

### I need to...

| Task | File to Open | Pattern to Use |
|------|--------------|----------------|
| Add file upload endpoint | `Api/OBOEndpoints.cs` | [endpoint-file-upload.md](patterns/endpoint-file-upload.md) |
| Add file download endpoint | `Api/OBOEndpoints.cs` | [endpoint-file-download.md](patterns/endpoint-file-download.md) |
| Create new DTO | `Models/*.cs` | [dto-file-upload-result.md](patterns/dto-file-upload-result.md) |
| Fix OBO token flow | `Infrastructure/GraphClientFactory.cs` | [service-graph-client-factory.md](patterns/service-graph-client-factory.md) |
| Add token caching | `Services/GraphTokenCache.cs` | [service-graph-token-cache.md](patterns/service-graph-token-cache.md) |
| Fix Dataverse connection | `Shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` | [service-dataverse-connection.md](patterns/service-dataverse-connection.md) |
| Organize DI | `Extensions/*Module.Extensions.cs` | [di-feature-module.md](patterns/di-feature-module.md) |
| Add error handling | Any endpoint file | [error-handling-standard.md](patterns/error-handling-standard.md) |

---

## 📂 File Structure (Target State)

```
src/api/Spe.Bff.Api/
│
├── Program.cs                              ⭐ Entry point (~100 lines, ~20 DI lines)
│   Purpose: Startup, DI, middleware, endpoint registration
│   Pattern: Keep minimal, delegate to feature modules
│   ADR: ADR-010 (DI Minimalism)
│
├── Extensions/                             ✨ NEW - Feature Modules
│   ├── SpaarkeCore.Extensions.cs          Authorization, caching, core services
│   ├── DocumentsModule.Extensions.cs      SPE, Graph, Dataverse services
│   └── WorkersModule.Extensions.cs        Background services
│   Purpose: Organize DI by feature domain
│   Pattern: [di-feature-module.md](patterns/di-feature-module.md)
│   ADR: ADR-010 (Feature Modules)
│
├── Api/                                    ⭐ Minimal API Endpoints
│   ├── OBOEndpoints.cs                    File upload/download via OBO flow
│   ├── DocumentsEndpoints.cs              Container management
│   ├── UploadEndpoints.cs                 Upload session management
│   ├── DataverseDocumentsEndpoints.cs     Dataverse CRUD
│   ├── PermissionsEndpoints.cs            Permission checks
│   └── UserEndpoints.cs                   User info
│   Purpose: HTTP request handlers (thin layer)
│   Pattern: Inject concrete services (e.g., SpeFileStore, not ISpeFileStore)
│   Anti-Pattern: ⚠️ [anti-pattern-leaking-sdk-types.md](patterns/anti-pattern-leaking-sdk-types.md)
│
├── Storage/                                ✨ NEW - Storage Facades
│   └── SpeFileStore.cs                    ⭐ Concrete class (no interface)
│   Purpose: SPE file operations (upload, download, delete, list)
│   Pattern: Inject IGraphClientFactory, return DTOs only
│   ADR: ADR-007 (SPE Storage Seam Minimalism)
│   Anti-Pattern: ⚠️ [anti-pattern-interface-proliferation.md](patterns/anti-pattern-interface-proliferation.md)
│
├── Services/                               ⭐ Business Services
│   └── GraphTokenCache.cs                 ✨ NEW (Phase 4)
│   Purpose: Cache OBO tokens in Redis (97% latency reduction)
│   Pattern: [service-graph-token-cache.md](patterns/service-graph-token-cache.md)
│   ADR: ADR-009 (Redis-First Caching)
│   Lifetime: Singleton (inject IDistributedCache)
│
├── Infrastructure/                         ⭐ Infrastructure Services
│   ├── GraphClientFactory.cs              Creates Graph clients (OBO & app-only)
│   │   Purpose: OBO token exchange, client creation
│   │   Pattern: [service-graph-client-factory.md](patterns/service-graph-client-factory.md)
│   │   Changes: REMOVE UAMI_CLIENT_ID logic, ADD token caching
│   │   Lifetime: Singleton (implements IGraphClientFactory)
│   │
│   ├── GraphHttpMessageHandler.cs         ⭐ KEEP UNCHANGED
│   │   Purpose: Resilience (retry, circuit breaker, timeout)
│   │   Status: Excellent implementation, don't touch
│   │
│   └── UploadSessionManager.cs            ⭐ KEEP UNCHANGED
│       Purpose: Chunked upload coordination
│       Status: Working well, don't touch
│
├── BackgroundServices/                     ⭐ Background Workers
│   ├── DocumentEventProcessor.cs          Service Bus message processing
│   └── ServiceBusJobProcessor.cs          Job queue processing
│   Purpose: Async message processing
│   Status: ⭐ Correct per ADR-004, keep as-is
│   Lifetime: Hosted services
│
├── Models/                                 DTOs (Data Transfer Objects)
│   Purpose: API request/response models
│   Pattern: [dto-file-upload-result.md](patterns/dto-file-upload-result.md)
│   Rule: Never expose Graph SDK types (DriveItem, Entity, etc.)
│   ADR: ADR-007 (No SDK type leakage)
│
├── Configuration/                          Options Pattern
│   ├── DataverseOptions.cs                Dataverse config
│   └── GraphResilienceOptions.cs          Polly policies
│   Purpose: Strongly-typed configuration
│   Pattern: IOptions<T> with validation
│
└── Telemetry/                              ✨ NEW (Phase 4, optional)
    └── CacheMetrics.cs                     Cache hit/miss metrics
    Purpose: Monitor cache performance
    Pattern: OpenTelemetry metrics

---

DELETED FILES (Post-Refactoring):
├── Services/IResourceStore.cs              ❌ Unnecessary interface
├── Services/SpeResourceStore.cs            ❌ Merged into SpeFileStore
├── Services/ISpeService.cs                 ❌ Unnecessary interface
├── Services/OboSpeService.cs               ❌ Merged into SpeFileStore
├── Services/IDataverseSecurityService.cs   ❌ Use AuthorizationService instead
├── Services/DataverseSecurityService.cs    ❌ Use AuthorizationService instead
├── Services/IUacService.cs                 ❌ Use AuthorizationService instead
└── Services/UacService.cs                  ❌ Use AuthorizationService instead
```

---

## 📚 Shared Libraries

### src/shared/Spaarke.Core/

```
Spaarke.Core/
├── Authorization/
│   ├── AuthorizationService.cs            ⭐ Main authorization service
│   └── Rules/                             IAuthorizationRule implementations
│       ├── CanAccessDocumentRule.cs
│       ├── CanUploadFilesRule.cs
│       └── ...
│
├── Caching/
│   └── RequestCache.cs                    Per-request cache (scoped)
│
└── Extensions/
    └── ServiceCollectionExtensions.cs     DI registration helpers
```

**Status**: ⭐ KEEP ALL - Correct per ADR-003
**Purpose**: Reusable authorization logic across projects
**Pattern**: Inject `AuthorizationService`, not Dataverse-specific services
**Lifetime**: Singleton (AuthorizationService), Scoped (IAccessDataSource)

### src/shared/Spaarke.Dataverse/

```
Spaarke.Dataverse/
├── DataverseServiceClientImpl.cs          ⭐ ServiceClient wrapper
│   Purpose: Dataverse connection management
│   Pattern: [service-dataverse-connection.md](patterns/service-dataverse-connection.md)
│   Change: Singleton lifetime (was Scoped)
│   ADR: ADR-010 (Singleton for expensive resources)
│
└── DataverseAccessDataSource.cs           ⭐ IAccessDataSource implementation
    Purpose: Required seam for authorization
    Status: ⭐ KEEP - Required by ADR-003
    Lifetime: Scoped (depends on Singleton ServiceClient)
    Anti-Pattern: ⚠️ [anti-pattern-captive-dependency.md](patterns/anti-pattern-captive-dependency.md)
```

---

## 🔄 Dependency Flows

### Current (Before Refactoring) - 6 Layers

```
OBOEndpoints
↓ IResourceStore
SpeResourceStore
↓ ISpeService
OboSpeService
↓ IGraphClientFactory
GraphClientFactory
↓ GraphServiceClient
Graph SDK
```

**Problems**: Too many layers, unnecessary interfaces, hard to trace

### Target (After Refactoring) - 3 Layers

```
OBOEndpoints
↓ SpeFileStore (concrete)
SpeFileStore
↓ IGraphClientFactory
GraphClientFactory
↓ GraphServiceClient
Graph SDK
```

**Benefits**: 50% fewer layers, easier to debug, complies with ADR-007

### With Caching (Phase 4) - 3 Layers + Cache

```
OBOEndpoints
↓ SpeFileStore (concrete)
SpeFileStore
↓ IGraphClientFactory
GraphClientFactory
├─→ GraphTokenCache (check cache first)
│   ↓ IDistributedCache (Redis)
│   Cache hit → Return cached token (5ms)
│   Cache miss ↓
└─→ Perform OBO exchange (200ms)
    ↓ Store in cache (55-min TTL)
    ↓ GraphServiceClient
    Graph SDK
```

**Benefits**: 97% latency reduction, 95% cache hit rate

---

## ⚙️ Configuration Guide

### appsettings.json (Target State)

```json
{
  "API_APP_ID": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
  "TENANT_ID": "a221a95e-6abc-4434-aecc-e48338a1b2f2",

  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "Domain": "spaarke.com",
    "TenantId": "a221a95e-6abc-4434-aecc-e48338a1b2f2",
    "ClientId": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
    "ClientSecret": "@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/BFF-API-ClientSecret)",
    "Audience": "api://1e40baad-e065-4aea-a8d4-4b7ab273458c"
  },

  "Dataverse": {
    "ServiceUrl": "@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/SPRK-DEV-DATAVERSE-URL)",
    "ClientId": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
    "ClientSecret": "@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/BFF-API-ClientSecret)"
  },

  "ConnectionStrings": {
    "Redis": "spaarke-redis-dev.redis.cache.windows.net:6380,password=...,ssl=True",
    "ServiceBus": "@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/ServiceBus-ConnectionString)"
  }
}
```

**Key Points**:
- All `ClientId` values must match `API_APP_ID` (1e40baad...)
- Use Key Vault references for secrets (`@Microsoft.KeyVault(...)`)
- No `UAMI_CLIENT_ID` (removed)

### Azure Key Vault Secrets

Required secrets in `spaarke-spekvcert` Key Vault:

1. **BFF-API-ClientSecret** - API app registration client secret
2. **SPRK-DEV-DATAVERSE-URL** - Dataverse environment URL
3. **ServiceBus-ConnectionString** - Service Bus connection string

---

## 🧪 Testing Strategy

### What to Test

✅ **Service Logic**
- SpeFileStore methods (upload, download, delete)
- Authorization rules
- DTO mapping
- Error handling

✅ **Integration Points**
- Graph API calls (with mocked Graph client)
- Dataverse queries (with mocked ServiceClient)
- Service Bus processing (with TestClient)

### What NOT to Test

❌ **Framework Code**
- ASP.NET Core internals
- Graph SDK internals
- Azure services (use integration tests instead)

❌ **Configuration Binding**
- Use validation instead (ValidateOnStart)

### Mock Strategy

```csharp
// ✅ CORRECT: Mock at infrastructure boundary
var mockGraphFactory = new Mock<IGraphClientFactory>();
mockGraphFactory
    .Setup(x => x.CreateOnBehalfOfClientAsync(It.IsAny<string>()))
    .ReturnsAsync(/* mock Graph client */);

// Inject real service with mocked dependency
var fileStore = new SpeFileStore(mockGraphFactory.Object, logger);

// Test service logic
var result = await fileStore.UploadFileAsync(containerId, fileName, stream, token);
Assert.NotNull(result);
```

```csharp
// ❌ WRONG: Mocking concrete class (before refactoring)
var mockSpeService = new Mock<ISpeService>(); // Interface removed in refactor!

// ❌ WRONG: Mocking SDK types directly
var mockDriveItem = new Mock<DriveItem>(); // Graph SDK type, don't mock
```

**Pattern**: Mock interfaces at boundaries (IGraphClientFactory, IAccessDataSource), inject real concrete classes (SpeFileStore)

---

## 🎯 ADR Compliance Quick Check

| File | ADR | Compliance Check |
|------|-----|------------------|
| `Storage/SpeFileStore.cs` | ADR-007 | ✅ Concrete class, no interface<br>✅ Returns DTOs only (not DriveItem)<br>✅ Focused on SPE operations |
| `Services/GraphTokenCache.cs` | ADR-009 | ✅ Redis-only (no L1/L2 hybrid)<br>✅ Distributed cache<br>✅ 55-minute TTL |
| `Extensions/*Module.Extensions.cs` | ADR-010 | ✅ Feature modules<br>✅ Register concretes (not interfaces)<br>✅ Clear grouping |
| `Infrastructure/GraphClientFactory.cs` | ADR-010 | ✅ Implements IGraphClientFactory (factory pattern OK)<br>✅ Singleton lifetime<br>❌ Remove UAMI_CLIENT_ID logic |
| `Shared/Spaarke.Dataverse/` | ADR-010 | ⚠️ Change to Singleton lifetime<br>✅ Keep IAccessDataSource (required seam) |

---

## 🚀 Performance Targets

| Operation | Before | After | Pattern Used |
|-----------|--------|-------|--------------|
| File Upload (small) | 700ms | 150ms | Token caching ([service-graph-token-cache.md](patterns/service-graph-token-cache.md)) |
| File Download | 500ms | 100ms | Token caching |
| Dataverse Query | 650ms | 50ms | Singleton ServiceClient ([service-dataverse-connection.md](patterns/service-dataverse-connection.md)) |
| Health Check | 300ms | 50ms | Singleton ServiceClient |

**Cache Performance**:
- Hit Rate: 95% (19 out of 20 requests)
- Hit Latency: 5ms (Redis lookup)
- Miss Latency: 200ms (OBO exchange)
- Average: 15ms = (95% × 5ms) + (5% × 200ms)

---

## 📋 Interface Inventory

### ✅ Interfaces to KEEP (3 total)

| Interface | Why Keep | Pattern |
|-----------|----------|---------|
| `IGraphClientFactory` | Factory pattern - creates multiple client types | Factory |
| `IAccessDataSource` | Seam for Dataverse access data (ADR-003) | Seam |
| `IAuthorizationRule` | Collection pattern - multiple implementations | Strategy |

### ❌ Interfaces to REMOVE (8 total)

| Interface | Why Remove | Replacement |
|-----------|-----------|-------------|
| `IResourceStore` | Single implementation | Use `SpeFileStore` concrete class |
| `ISpeService` | Single implementation | Merged into `SpeFileStore` |
| `IOboSpeService` | Duplicate of ISpeService | Merged into `SpeFileStore` |
| `IDataverseSecurityService` | Single implementation | Use `AuthorizationService` from Spaarke.Core |
| `IUacService` | Single implementation | Use `AuthorizationService` |
| `IDataverseService` | Single implementation | Use concrete class |

**Anti-Pattern**: ⚠️ [anti-pattern-interface-proliferation.md](patterns/anti-pattern-interface-proliferation.md)

---

## 🎨 Coding Guidelines for AI

### When Adding New Code

1. **Check patterns library first**: [patterns/README.md](patterns/README.md)
2. **Find task mapping**: [TASK-PATTERN-MAP.md](TASK-PATTERN-MAP.md)
3. **Copy pattern code**: Use copy-paste from pattern files
4. **Verify checklist**: Every pattern has a completion checklist
5. **Check anti-patterns**: Avoid common mistakes

### When Modifying Existing Code

1. **Identify current layer**: Is this endpoint, service, or infrastructure?
2. **Check target architecture**: [TARGET-ARCHITECTURE.md](TARGET-ARCHITECTURE.md)
3. **Apply refactoring**: Remove interfaces, simplify layers
4. **Verify ADR compliance**: Check ADR matrix in target architecture
5. **Update tests**: Mock at boundaries only

### Common AI Pitfalls to Avoid

❌ **Creating unnecessary interfaces**
- Anti-Pattern: [anti-pattern-interface-proliferation.md](patterns/anti-pattern-interface-proliferation.md)
- Fix: Register concrete classes unless factory/collection

❌ **Returning Graph SDK types**
- Anti-Pattern: [anti-pattern-leaking-sdk-types.md](patterns/anti-pattern-leaking-sdk-types.md)
- Fix: Create DTOs, map SDK types to DTOs

❌ **Injecting Scoped into Singleton**
- Anti-Pattern: [anti-pattern-captive-dependency.md](patterns/anti-pattern-captive-dependency.md)
- Fix: Follow lifetime hierarchy (Singleton → Singleton, Scoped → Scoped/Transient)

❌ **Adding many layers of abstraction**
- Pattern: Keep 3-layer structure (Endpoint → Service → Infrastructure)
- ADR: ADR-007 (Minimalism)

❌ **Creating hybrid caching**
- Pattern: Redis-only caching ([service-graph-token-cache.md](patterns/service-graph-token-cache.md))
- ADR: ADR-009 (Redis-First)

---

## 🔍 Quick Search Patterns

### Find all endpoints
```bash
src/api/Spe.Bff.Api/Api/*Endpoints.cs
```

### Find all services
```bash
src/api/Spe.Bff.Api/Services/*.cs
src/api/Spe.Bff.Api/Storage/*.cs
```

### Find all DI registrations
```bash
src/api/Spe.Bff.Api/Program.cs
src/api/Spe.Bff.Api/Extensions/*Extensions.cs
```

### Find all DTOs
```bash
src/api/Spe.Bff.Api/Models/*.cs
```

### Find all tests
```bash
tests/Spe.Bff.Api.Tests/**/*.cs
```

---

## 📚 Related Documentation

### Architecture Documents
- **[TARGET-ARCHITECTURE.md](TARGET-ARCHITECTURE.md)** - Target state after refactoring
- **[ARCHITECTURAL-DECISIONS.md](ARCHITECTURAL-DECISIONS.md)** - ADRs (authoritative source)
- **[SDAP-ARCHITECTURE-OVERVIEW-V2.md](../../SDAP-ARCHITECTURE-OVERVIEW-V2-2025-10-13-2213.md)** - High-level overview

### Pattern Library
- **[patterns/README.md](patterns/README.md)** - Pattern catalog
- **[patterns/QUICK-CARD.md](patterns/QUICK-CARD.md)** - 30-second lookup
- **[TASK-PATTERN-MAP.md](TASK-PATTERN-MAP.md)** - Task → Pattern mapping
- **[CODE-PATTERNS.md](CODE-PATTERNS.md)** - Full reference (1500+ lines)
- **[ANTI-PATTERNS.md](ANTI-PATTERNS.md)** - What NOT to do

### Implementation Plans
- **[REFACTORING-PLAN.md](REFACTORING-PLAN.md)** - Phase-by-phase plan
- **[IMPLEMENTATION-CHECKLIST.md](IMPLEMENTATION-CHECKLIST.md)** - Task checklist

---

## 🎯 Vibe Coding Tips

1. **Use split screen**: Code (left) + Pattern (right)
2. **Start with patterns**: Don't write from scratch
3. **Copy full blocks**: Copy → Paste → Adapt
4. **Check checklists**: Every pattern has verification steps
5. **Avoid anti-patterns**: Check anti-pattern files before committing

---

**Last Updated**: 2025-10-13
**Status**: ✅ Aligned with Target Architecture
**Pattern Count**: 11 patterns (8 correct + 3 anti-patterns)
**Ready for**: AI-assisted vibe coding
