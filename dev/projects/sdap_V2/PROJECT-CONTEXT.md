# SDAP BFF API Refactoring Project Context

**Last Updated**: 2025-10-14
**Status**: Ready for Phase 1 execution

---

## Project Overview

Refactoring the Spe.Bff.Api codebase to achieve ADR compliance by simplifying service layer abstractions while preserving all existing functionality.

**Approach**: Two-phase project
1. **Phase A (This Document)**: Code refactoring (Phases 1-4, ~40 hours)
2. **Phase B (Future)**: Repository restructure (after refactoring complete)

---

## Critical Constraints

### DO NOT Modify
- ❌ **PCF control code** (separate repository concern)
- ❌ **API endpoint routes or contracts** (breaking change for clients)
- ❌ **Dataverse schema** (requires deployment coordination)
- ❌ **Authentication flows** (security-critical)
- ❌ **File paths during refactoring** (wait for Phase B restructure)

### PRESERVE
- ✅ **All existing functionality** (this is refactoring, not feature work)
- ✅ **All test coverage** (update mocks, but keep tests)
- ✅ **All configuration keys** (same keys, correct values)
- ✅ **Current file structure** (restructure is Phase B, AFTER refactoring)

---

## Technology Stack

**Runtime**:
- .NET 8.0
- C# 12

**Frameworks**:
- ASP.NET Core Minimal API (no Controllers)
- Microsoft.Identity.Web (JWT authentication)
- Microsoft.Graph SDK v5+
- Microsoft.PowerPlatform.Dataverse.Client

**Infrastructure**:
- Azure Service Bus (async messaging)
- Redis (StackExchange.Redis - distributed cache)
- Azure Key Vault (secrets management)
- Azure App Service (hosting)

**Testing**:
- xUnit
- Moq (mocking framework)
- Microsoft.AspNetCore.Mvc.Testing (integration tests)

---

## Current Authentication Flows (DO NOT BREAK)

### Flow 1: PCF → BFF API
- **Token**: User JWT from Entra ID
- **Audience**: `api://1e40baad-e065-4aea-a8d4-4b7ab273458c`
- **Validation**: Microsoft.Identity.Web middleware

### Flow 2: BFF API → Graph API
- **Method**: OAuth 2.0 On-Behalf-Of (OBO) flow
- **Implementation**: `ConfidentialClientApplication` with client secret
- **Scopes**: `Sites.FullControl.All`, `Files.ReadWrite.All`

### Flow 3: BFF API → Dataverse
- **Method**: Server-to-Server (S2S) with client credentials
- **Implementation**: `ServiceClient` with connection string
- **Auth Type**: `AuthType=ClientSecret`

---

## Active ADRs to Enforce

### ADR-007: SPE Storage Seam Minimalism
- **Requirement**: Single concrete `SpeFileStore` class (no interface)
- **Requirement**: No generic storage interfaces
- **Requirement**: Return DTOs only (never `DriveItem`, `Entity` from Graph/Dataverse SDKs)
- **Reference**: [ARCHITECTURAL-DECISIONS.md](ARCHITECTURAL-DECISIONS.md#adr-007)

### ADR-009: Redis-First Caching
- **Requirement**: Distributed cache only (Redis via `IDistributedCache`)
- **Requirement**: No hybrid L1/L2 caching
- **Requirement**: Short TTL for security-sensitive data (55 minutes for tokens)
- **Reference**: [ARCHITECTURAL-DECISIONS.md](ARCHITECTURAL-DECISIONS.md#adr-009)

### ADR-010: DI Minimalism
- **Requirement**: Register concrete classes (no interfaces unless factory/collection)
- **Requirement**: Feature modules for DI organization
- **Requirement**: ~15-20 lines of DI code in Program.cs
- **Reference**: [ARCHITECTURAL-DECISIONS.md](ARCHITECTURAL-DECISIONS.md#adr-010)

---

## Repository Structure (Current - Phase A)

⚠️ **IMPORTANT**: Use these paths during refactoring. Repository restructure is Phase B (after refactoring complete).

### Source Code
```
src/
├── api/
│   └── Spe.Bff.Api/                    # ⭐ Primary refactoring target
│       ├── Program.cs                  # DI registration (SIMPLIFY to ~20 lines)
│       ├── Api/                        # Minimal API endpoints (UPDATE)
│       ├── Infrastructure/             # GraphClientFactory, etc. (UPDATE)
│       ├── Services/                   # SpeResourceStore, OboSpeService (CONSOLIDATE)
│       ├── Storage/                    # ✨ NEW: SpeFileStore goes here
│       ├── Extensions/                 # ✨ NEW: Feature modules go here
│       ├── Models/                     # DTOs (CREATE new DTOs)
│       ├── BackgroundServices/         # Service Bus processors (KEEP)
│       └── Configuration/              # Options classes (KEEP)
│
├── shared/
│   ├── Spaarke.Core/                   # ✅ KEEP: Authorization service, rules
│   └── Spaarke.Dataverse/              # ⚠️ UPDATE: Change to Singleton lifetime
│
└── tests/
    └── Spe.Bff.Api.Tests/              # ⚠️ UPDATE: Change mocking strategy
```

### Refactoring Documentation
```
dev/projects/sdap_V2/                   # All refactoring docs (this directory)
├── PROJECT-CONTEXT.md                  # This file
├── IMPLEMENTATION-CHECKLIST.md         # Daily task tracker
├── TARGET-ARCHITECTURE.md              # Target state after refactoring
├── ARCHITECTURAL-DECISIONS.md          # ADRs
├── CODEBASE-MAP.md                     # File structure guide
├── REFACTORING-CHECKLIST.md            # Detailed reference
├── CODE-PATTERNS.md                    # Full pattern reference
├── ANTI-PATTERNS.md                    # What NOT to do
├── TASK-PATTERN-MAP.md                 # Task → Pattern mapping
│
├── patterns/                           # Pattern library
│   ├── README.md                       # Pattern catalog
│   ├── QUICK-CARD.md                   # 30-second lookup
│   ├── endpoint-*.md                   # Endpoint patterns
│   ├── service-*.md                    # Service patterns
│   ├── di-*.md                         # DI patterns
│   └── anti-pattern-*.md               # Anti-patterns
│
└── tasks/                              # Task guides
    ├── README.md                       # Task template
    ├── phase-1-*.md                    # Phase 1 tasks (3 files)
    ├── phase-2-*.md                    # Phase 2 tasks (6 files)
    ├── phase-3-*.md                    # Phase 3 tasks (2 files)
    └── phase-4-*.md                    # Phase 4 tasks (4 files)
```

---

## Naming Conventions

### Services
- **Concrete classes**: `ServiceName` (no suffix, no interface)
  - Example: `SpeFileStore`, `GraphTokenCache`
- **Interfaces**: `IServiceName` (ONLY for factories or collections)
  - Example: `IGraphClientFactory` ✅ (factory pattern)
  - Example: `IAuthorizationRule` ✅ (collection pattern)
  - Example: `ISpeFileStore` ❌ (unnecessary - single implementation)

### DTOs
- **Pattern**: `EntityNameDto` or descriptive name
  - Example: `FileUploadResult`, `FileDownloadResult`, `FileMetadata`
- **Never expose SDK types**: Don't return `DriveItem`, `Entity`, etc.

### Endpoints
- **File name**: `EntityNameEndpoints.cs`
  - Example: `OBOEndpoints.cs`, `DocumentsEndpoints.cs`
- **Methods**: Static methods with minimal parameters
  - Example: `MapPut`, `MapGet`, `MapPost`, `MapDelete`

### Extensions
- **File name**: `FeatureNameExtensions.cs` in `Extensions/` folder
  - Example: `SpaarkeCore.Extensions.cs`
  - Example: `DocumentsModule.Extensions.cs`
- **Method**: `Add{FeatureName}` extension on `IServiceCollection`
  - Example: `services.AddSpaarkeCore(configuration)`

---

## Configuration Keys (DO NOT CHANGE KEYS)

### App Registration
```json
{
  "API_APP_ID": "1e40baad-e065-4aea-a8d4-4b7ab273458c",  // ✅ CORRECT (BFF API)
  "TENANT_ID": "a221a95e-6abc-4434-aecc-e48338a1b2f2",
  "DEFAULT_CT_ID": "8a6ce34c-6055-4681-8f87-2f4f9f921c06"
}
```

### Azure AD
```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "a221a95e-6abc-4434-aecc-e48338a1b2f2",
    "ClientId": "1e40baad-e065-4aea-a8d4-4b7ab273458c",  // Must match API_APP_ID
    "Audience": "api://1e40baad-e065-4aea-a8d4-4b7ab273458c",
    "ClientSecret": "@Microsoft.KeyVault(...)"
  }
}
```

### Dataverse
```json
{
  "Dataverse": {
    "ServiceUrl": "@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/SPRK-DEV-DATAVERSE-URL)",
    "ClientId": "1e40baad-e065-4aea-a8d4-4b7ab273458c",  // Must match API_APP_ID
    "ClientSecret": "@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/BFF-API-ClientSecret)"
  }
}
```

### Connection Strings
```json
{
  "ConnectionStrings": {
    "Redis": "spaarke-redis-dev.redis.cache.windows.net:6380,password=...,ssl=True",
    "ServiceBus": "@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/ServiceBus-ConnectionString)"
  }
}
```

### Key Vault Secrets
Required secrets in `spaarke-spekvcert` Key Vault:
1. `BFF-API-ClientSecret` - BFF API app registration client secret
2. `SPRK-DEV-DATAVERSE-URL` - Dataverse environment URL
3. `ServiceBus-ConnectionString` - Service Bus connection string

---

## Testing Requirements

### After EVERY Task
1. **Build**: `dotnet build`
   - Expected: Success with **0 warnings**

2. **Tests**: `dotnet test`
   - Expected: All tests pass

3. **Manual Testing**: Test affected endpoints
   - Use Postman collection or curl commands
   - Verify DTOs returned (not SDK types)

4. **Logs**: Check for errors
   - No authentication errors
   - No DI resolution failures

### Phase Completion
- Full integration testing
- Performance benchmarking (compare to baseline)
- Memory leak test (10-minute load test)

---

## Git Workflow

### Branch Strategy
```bash
# Create feature branch from main
git checkout -b refactor/adr-compliance

# Work in phases, commit after each task
git commit -m "refactor(phase-1): {task description}"

# Push after each phase
git push origin refactor/adr-compliance
```

### Commit Message Format
```
{type}({scope}): {short description}

{detailed body}

{footer with ADR references and metrics}
```

**Example**:
```bash
git commit -m "refactor(phase-1): fix app registration config and ServiceClient lifetime

- Fix API_APP_ID to use BFF API app (1e40baad...)
- Remove UAMI_CLIENT_ID logic from GraphClientFactory
- Change DataverseServiceClientImpl to Singleton lifetime

ADR Compliance: ADR-010 (Singleton for expensive resources)
Performance: Eliminates 500ms initialization overhead per request
Task: Phase 1 complete (3/3 tasks)"
```

---

## Success Criteria

### Code Quality
- ✅ Interface count: 10 → 3 (70% reduction)
- ✅ Concrete class registrations: ~5 → ~15
- ✅ Program.cs DI lines: 80+ → ~20 (75% reduction)
- ✅ Call chain depth: 6 layers → 3 layers (50% reduction)
- ✅ No SDK types in API contracts (100% DTO usage)
- ✅ No unnecessary abstractions

### Performance
- ✅ File upload: <200ms (target: 150ms, was 700ms) - 78% faster
- ✅ File download: <150ms (target: 100ms, was 500ms) - 80% faster
- ✅ Dataverse query: <100ms (target: 50ms, was 650ms) - 92% faster
- ✅ Cache hit rate: >90% (new capability)
- ✅ Cache hit latency: <10ms

### ADR Compliance
- ✅ **ADR-007**: SpeFileStore concrete class, returns DTOs only
- ✅ **ADR-009**: Redis-first caching, 55-minute TTL, no hybrid
- ✅ **ADR-010**: Concrete class registration, feature modules, ~20 DI lines

### Maintainability
- ✅ Easier to understand (3-layer structure vs 6)
- ✅ Easier to debug (fewer layers to trace)
- ✅ Easier to test (mock at boundaries only)
- ✅ Easier to extend (clear feature boundaries)

---

## Reference Documents

### Primary Guides
- **[IMPLEMENTATION-CHECKLIST.md](IMPLEMENTATION-CHECKLIST.md)** - Daily task tracker (~250 lines)
- **[tasks/*.md](tasks/)** - Focused task guides (15 files)
- **[patterns/README.md](patterns/README.md)** - Pattern catalog
- **[TASK-PATTERN-MAP.md](TASK-PATTERN-MAP.md)** - Task → Pattern mapping

### Architecture
- **[TARGET-ARCHITECTURE.md](TARGET-ARCHITECTURE.md)** - Target state after refactoring
- **[ARCHITECTURAL-DECISIONS.md](ARCHITECTURAL-DECISIONS.md)** - ADRs (authoritative)
- **[CODEBASE-MAP.md](CODEBASE-MAP.md)** - File structure guide

### Detailed References
- **[REFACTORING-CHECKLIST.md](REFACTORING-CHECKLIST.md)** - Deep reference with full code examples
- **[CODE-PATTERNS.md](CODE-PATTERNS.md)** - Full pattern reference (1500+ lines)
- **[ANTI-PATTERNS.md](ANTI-PATTERNS.md)** - What NOT to do

---

## AI Assistant Instructions

When working on this project:

### Context Awareness
1. **Read task file AI PROMPT section first** - Understand context and constraints
2. **Run pre-flight verification (Step 0)** - Verify prerequisites before starting
3. **Stay focused** - Each task has explicit FOCUS statement (don't drift to other tasks)
4. **Use current file paths** - Repository restructure is Phase B (after refactoring)

### Implementation
1. **Reference pattern files** - Don't write from scratch, copy-paste from patterns
2. **Check anti-patterns** - Verify you're not violating anti-patterns
3. **Follow validation steps** - Build, test, manual check after each task
4. **Use commit templates** - Copy commit messages from task files

### Quality Gates
1. **Pre-flight**: Verify prerequisites (Step 0 in task file)
2. **Implementation**: Follow step-by-step instructions
3. **Validation**: Run all validation commands
4. **Checklist**: Tick all boxes before marking complete
5. **Context verification**: Ensure task stayed focused (didn't drift)

---

## Phase B: Repository Restructure (Future)

⚠️ **DO NOT START THIS YET** - Complete Phase A (refactoring) first!

After refactoring is complete, we will:
1. Consolidate `dev/projects/` into `docs/`
2. Move PCF controls to `src/controls/`
3. Clean up root directory (`ERROR FILES/`, `publish/`, etc.)
4. Update all documentation to reflect new structure
5. Update CI/CD pipelines for new paths

**Estimated Duration**: 1-2 days
**Risk**: Low (only file moves, no logic changes)

---

**Last Updated**: 2025-10-14
**Current Phase**: Phase A - Refactoring (Ready to start Phase 1)
**Next Step**: Open [IMPLEMENTATION-CHECKLIST.md](IMPLEMENTATION-CHECKLIST.md) and start Phase 1, Task 1
