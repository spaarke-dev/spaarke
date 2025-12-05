# ADR Validation Rules Reference

> **Purpose**: Detailed validation rules and grep patterns for each ADR.
> **Usage**: Loaded by the `adr-check` skill during validation.

---

## ADR-001: Minimal API + BackgroundService

**Constraint**: No Azure Functions - use Minimal API + BackgroundService instead.

### Check For (Violations)

```bash
# Azure Functions packages in .csproj
grep -r "Microsoft.Azure.WebJobs\|Microsoft.Azure.Functions\|DurableTask" --include="*.csproj"

# Azure Functions attributes in C#
grep -r "\[FunctionName\]\|\[TimerTrigger\]\|\[QueueTrigger\]\|\[ServiceBusTrigger\]\|\[HttpTrigger\]" --include="*.cs"
```

### Fix

Remove Azure Functions packages. Convert to:
- **HTTP triggers** → Minimal API endpoints
- **Queue/Timer triggers** → BackgroundService workers
- **Durable Functions** → State machine with Service Bus

---

## ADR-002: Thin Dataverse Plugins

**Constraint**: Plugins must be <200 LoC, <50ms p95. No HTTP calls, no orchestration.

### Check For (Violations)

```bash
# HTTP calls in plugins
grep -r "HttpClient\|WebRequest\|GraphServiceClient" src/Dataverse/ --include="*.cs"

# Plugin classes in wrong location
find src/api -name "*Plugin.cs"

# Long operations (look for async patterns that shouldn't be there)
grep -r "await\|Task<\|async " src/Dataverse/ --include="*Plugin.cs"
```

### Fix

Move orchestration to BFF API or workers. Plugins should only:
- Validate input
- Transform/project data
- Dispatch messages to Service Bus

---

## ADR-003: Lean Authorization Seams

**Constraint**: Two seams only - `IAccessDataSource` for UAC, `SpeFileStore` for storage.

### Check For (Warnings)

```bash
# Verify AuthorizationService usage
grep -r "AuthorizationService" --include="*.cs"

# Inline authorization (anti-pattern)
grep -r "User\.Claims\|User\.IsInRole\|ClaimsPrincipal" src/api/Spe.Bff.Api/Api/ --include="*.cs"
```

### Fix

Centralize authorization in `AuthorizationService`. Use endpoint filters for enforcement.

---

## ADR-004: Async Job Contract

**Constraint**: Standard job envelope, Polly retries, idempotent handlers, poison queues.

### Check For (Warnings)

```bash
# Find job handlers
find src -name "*JobHandler.cs" -o -name "*Worker.cs"

# Verify Polly usage
grep -r "AddPolicyHandler\|RetryPolicy\|PolicyBuilder" --include="*.cs"

# Check for correlation ID
grep -r "CorrelationId\|x-correlation-id" --include="*.cs"
```

### Fix

Ensure all job handlers:
1. Accept standardized envelope with CorrelationId
2. Configure Polly retry policies
3. Are idempotent (safe to retry)
4. Route failures to poison queue

---

## ADR-005: Flat Storage in SPE

**Constraint**: No deep folder hierarchies. Use metadata for associations.

### Check For (Violations)

```bash
# Folder creation
grep -r "CreateFolder\|CreateDirectory\|mkdir" src/server --include="*.cs"

# Folder navigation (should use metadata)
grep -r "GetChildFolders\|ListFolders\|GetChildren.*folder" src/server --include="*.cs"
```

### Fix

Use flat storage structure. Associate files via metadata properties (`spk_entityid`, `spk_entitytype`), not folder hierarchy.

---

## ADR-006: PCF over Web Resources

**Constraint**: No new JavaScript web resources. Use PCF controls.

### Check For (Violations)

```bash
# New web resources
find src/webresources -name "*.js" -newer .git/index 2>/dev/null

# Any JS web resources (review if being modified)
find power-platform/webresources -name "*.js"
```

### Check For (Compliant)

```bash
# PCF controls exist
find src/client/pcf -name "*.tsx" -o -name "*.ts"
```

### Fix

Build new UI as PCF controls (TypeScript + React). Migrate legacy web resources when touched.

---

## ADR-007: Graph Isolation (SpeFileStore Facade)

**Constraint**: `Microsoft.Graph` types must not leak above `SpeFileStore` facade.

### Check For (Violations)

```bash
# Graph types outside allowed locations
grep -r "using Microsoft.Graph" --include="*.cs" | grep -v "Infrastructure" | grep -v "SpeFileStore"

# Graph types in endpoints
grep -r "DriveItem\|UploadSession\|GraphServiceClient" src/api/Spe.Bff.Api/Api/ --include="*.cs"

# Graph types in services (outside Infrastructure)
grep -r "Microsoft.Graph" src/server/Spe.Bff.Api/Services/ --include="*.cs"
```

### Fix

Replace Graph types with DTOs:
- `DriveItem` → `FileHandleDto`
- `UploadSession` → `UploadSessionDto`
- Route all Graph calls through `SpeFileStore`

---

## ADR-008: Endpoint Filter Authorization

**Constraint**: No global authorization middleware. Use endpoint filters.

### Check For (Violations)

```bash
# Global authorization middleware
grep -r "UseAuthorization\|AuthorizationMiddleware" src/api/Spe.Bff.Api/Program.cs

# Inline authorization in endpoints
grep -r "if.*User\\.IsInRole\|if.*User\\.Claims\|User\\.Identity\\.IsAuthenticated" src/api/Spe.Bff.Api/Api/ --include="*.cs"
```

### Check For (Compliant)

```bash
# Endpoint filters
grep -r "AddEndpointFilter" src/api/Spe.Bff.Api/ --include="*.cs"
```

### Fix

1. Remove global `UseAuthorization()` (keep `UseAuthentication()`)
2. Add endpoint filters: `.AddEndpointFilter<AuthorizationFilter>()`
3. Implement checks in `AuthorizationService`

---

## ADR-009: Redis-First Caching

**Constraint**: Use `IDistributedCache` (Redis). No `IMemoryCache` for cross-request caching.

### Check For (Violations)

```bash
# IMemoryCache usage
grep -r "IMemoryCache" src/api --include="*.cs"

# Hybrid caching libraries
grep -r "CacheTower\|LazyCache\|EasyCaching\|HybridCache" --include="*.csproj"
```

### Check For (Compliant)

```bash
# IDistributedCache usage
grep -r "IDistributedCache" src/api --include="*.cs"
```

### Fix

Replace `IMemoryCache` with `IDistributedCache`. Remove hybrid caching libraries. Add L1 cache only if profiling proves necessary (update ADR first).

---

## ADR-010: DI Minimalism

**Constraint**: ≤15 non-framework registrations. Concrete types unless seam required.

### Check For (Warnings)

```bash
# Count DI registrations
grep -c "AddScoped\|AddSingleton\|AddTransient" src/api/Spe.Bff.Api/Program.cs

# Find 1:1 interfaces (potential over-abstraction)
for iface in $(find src/server -name "I*.cs" -type f); do
  impl=$(basename "$iface" | sed 's/^I//')
  dir=$(dirname "$iface")
  if [ -f "$dir/$impl" ]; then
    echo "Review: $iface → $impl"
  fi
done
```

### Allowed Seams

- `IAccessDataSource` - UAC data seam
- `IFileStore` - Storage seam (if introduced)
- `IDataverseService` - Testing seam (optional)

### Fix

- Register concretes directly: `services.AddSingleton<SpeFileStore>()`
- Use Singleton for expensive resources (ServiceClient, GraphServiceClient)
- Group registrations in feature module extension methods

---

## ADR-011: Dataset PCF over Subgrids

**Constraint**: Use Dataset PCF controls instead of native subgrids for custom scenarios.

### Check For (Warnings)

```bash
# Subgrid references (might indicate legacy approach)
grep -r "subgrid\|SubGrid" src/client/pcf --include="*.tsx" --include="*.ts"

# FormXml with subgrid (in solution)
grep -r "<subgrid" power-platform/solutions --include="*.xml"
```

### Check For (Compliant)

```bash
# Dataset PCF controls
grep -r "DataSet\|IDataSetControl" src/client/pcf --include="*.tsx" --include="*.ts"
```

### Fix

Build Dataset PCF control when:
- Custom UI needed beyond native subgrid
- Advanced actions or interactions required
- Complex filtering or grouping needed

---

## ADR-012: Shared Component Library

**Constraint**: Reuse `@spaarke/ui-components` across modules. No duplicates.

### Check For (Violations)

```bash
# Duplicate component names
find src/client/pcf -name "*.tsx" -exec basename {} \; | sort | uniq -d

# Components that should be shared
grep -r "DataGrid\|FileUploader\|StatusBadge" src/client/pcf --include="*.tsx" -l
```

### Check For (Compliant)

```bash
# Shared component library exists
ls src/client/shared/Spaarke.UI.Components/

# PCF controls importing from shared
grep -r "@spaarke/ui-components\|from.*shared" src/client/pcf --include="*.tsx"
```

### Fix

1. Move reusable components to `src/client/shared/Spaarke.UI.Components/`
2. Update PCF controls to import from shared package
3. Follow shared library structure and export conventions

---

## Quick Validation Script

For comprehensive check, run all patterns:

```bash
#!/bin/bash
# Save as .claude/skills/adr-check/scripts/quick-check.sh

echo "=== ADR Quick Check ==="

echo -e "\n--- ADR-001: Azure Functions ---"
grep -r "Microsoft.Azure.Functions\|FunctionName" --include="*.csproj" --include="*.cs" -l

echo -e "\n--- ADR-007: Graph Leakage ---"
grep -r "using Microsoft.Graph" --include="*.cs" | grep -v Infrastructure | grep -v SpeFileStore

echo -e "\n--- ADR-008: Global Auth Middleware ---"
grep -r "UseAuthorization" src/api/Spe.Bff.Api/Program.cs

echo -e "\n--- ADR-009: IMemoryCache ---"
grep -r "IMemoryCache" src/api --include="*.cs" -l

echo "=== Check Complete ==="
```
