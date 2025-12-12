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
grep -r "HttpClient\|WebRequest\|GraphServiceClient" src/dataverse/ --include="*.cs"

# Plugin classes in wrong location
find src/api -name "*Plugin.cs"

# Long operations (look for async patterns that shouldn't be there)
grep -r "await\|Task<\|async " src/dataverse/ --include="*Plugin.cs"
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
grep -r "User\.Claims\|User\.IsInRole\|ClaimsPrincipal" src/server/api/Sprk.Bff.Api/Api/ --include="*.cs"
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
find src/client/webresources -name "*.js" -newer .git/index 2>/dev/null

# Any JS web resources (review if being modified)
find src/client/webresources -name "*.js"
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
grep -r "DriveItem\|UploadSession\|GraphServiceClient" src/server/api/Sprk.Bff.Api/Api/ --include="*.cs"

# Graph types in services (outside Infrastructure)
grep -r "Microsoft.Graph" src/server/api/Sprk.Bff.Api/ --include="*.cs"
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
grep -r "UseAuthorization\|AuthorizationMiddleware" src/server/api/Sprk.Bff.Api/Program.cs

# Inline authorization in endpoints
grep -r "if.*User\\.IsInRole\|if.*User\\.Claims\|User\\.Identity\\.IsAuthenticated" src/server/api/Sprk.Bff.Api/Api/ --include="*.cs"
```

### Check For (Compliant)

```bash
# Endpoint filters
grep -r "AddEndpointFilter" src/server/api/Sprk.Bff.Api/ --include="*.cs"
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
grep -r "IMemoryCache" src/server --include="*.cs"

# Hybrid caching libraries
grep -r "CacheTower\|LazyCache\|EasyCaching\|HybridCache" --include="*.csproj"
```

### Check For (Compliant)

```bash
# IDistributedCache usage
grep -r "IDistributedCache" src/server --include="*.cs"
```

### Fix

Replace `IMemoryCache` with `IDistributedCache`. Remove hybrid caching libraries. Add L1 cache only if profiling proves necessary (update ADR first).

---

## ADR-010: DI Minimalism

**Constraint**: ≤15 non-framework registrations. Concrete types unless seam required.

### Check For (Warnings)

```bash
# Count DI registrations
grep -c "AddScoped\|AddSingleton\|AddTransient" src/server/api/Sprk.Bff.Api/Program.cs

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
grep -r "<subgrid" src/dataverse/solutions --include="*.xml"
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

## ADR-014: AI Caching and Reuse Policy

**Constraint**: AI calls must follow a defined caching/reuse strategy (don’t add ad-hoc caches per endpoint).

### Check For (Warnings)

```powershell
# AI call sites (review for reuse/caching policy adherence)
Get-ChildItem -Recurse -Path src/server -Include *.cs | Select-String -Pattern "StreamCompletionAsync|ChatCompletions|GetChatCompletions|Embeddings|DocumentIntelligence" | Select-Object -First 200

# Direct OpenAI/DI client usage in endpoints (prefer services; ensure reuse policy is applied there)
Get-ChildItem -Recurse -Path src/server/api/Sprk.Bff.Api/Api -Include *.cs | Select-String -Pattern "OpenAi|Azure\.AI\.OpenAI|DocumentIntelligence" | Select-Object -First 200
```

### Fix

- Centralize AI call orchestration in a service layer and apply the reuse/caching strategy there.
- If caching is not yet implemented for a given path, ensure the ADR-014 decision is explicitly followed (no “random” cache keys/TTLs).

---

## ADR-015: AI Data Governance

**Constraint**: No PII/prompt/document-content leakage in logs/telemetry. Follow approved retention, redaction, and data-minimization rules.

### Check For (Violations / High-Risk)

```powershell
# Prompt/document content logging (high risk)
Get-ChildItem -Recurse -Path src/server -Include *.cs | Select-String -Pattern "systemPrompt|userPrompt|fullPrompt|documentText|prompt\b" | Select-Object -First 200

# Ad-hoc logging of request bodies (high risk)
Get-ChildItem -Recurse -Path src/server -Include *.cs | Select-String -Pattern "Log(Information|Warning|Error)\(.*request|WriteAsJsonAsync\(request" | Select-Object -First 200
```

### Fix

- Log only identifiers/metadata (correlation IDs, document IDs, action IDs, token counts), not prompt/content.
- Add explicit redaction helpers for any user-provided strings.

---

## ADR-016: AI Cost, Rate Limits, and Backpressure

**Constraint**: AI endpoints must be protected by rate limiting and bounded concurrency/backpressure.

### Check For (Warnings)

```powershell
# AI endpoints should require rate limiting
Get-ChildItem -Recurse -Path src/server/api/Sprk.Bff.Api/Api/Ai -Include *.cs | Select-String -Pattern "Map(Group|Get|Post)|RequireRateLimiting\(" | Select-Object -First 200

# Look for AI endpoints missing rate limiting (manual review: MapPost/MapGet without RequireRateLimiting)
Get-ChildItem -Recurse -Path src/server/api/Sprk.Bff.Api/Api/Ai -Include *.cs | Select-String -Pattern "Map(Post|Get)\(\"/" | Select-Object -First 200
```

### Fix

- Ensure all AI endpoints apply `.RequireRateLimiting("ai-stream")` or `.RequireRateLimiting("ai-batch")` (as appropriate).
- Add bounded concurrency (e.g., SemaphoreSlim/queue) at the orchestration/service layer if needed.

---

## ADR-017: Async Job Status and Persistence

**Constraint**: Enqueue-only async endpoints must expose a durable job status contract; avoid “fire-and-forget” without status.

### Check For (Warnings)

```powershell
# Async job enqueue patterns
Get-ChildItem -Recurse -Path src/server -Include *.cs | Select-String -Pattern "JobContract|JobOutcome|Enqueue|ServiceBus|Queue" | Select-Object -First 200

# Endpoints returning 202/Accepted should return a job identifier and link to status
Get-ChildItem -Recurse -Path src/server/api/Sprk.Bff.Api/Api -Include *.cs | Select-String -Pattern "StatusCodes\.Status202Accepted|Results\.Accepted\(" | Select-Object -First 200
```

### Fix

- Return `202 Accepted` with `{ jobId }` and a status URL.
- Persist job status transitions in the chosen store (per ADR-017).

---

## ADR-018: Feature Flags and Kill Switches

**Constraint**: Risky/expensive surfaces (especially AI) must be gated by feature flags/kill switches.

### Check For (Warnings)

```powershell
# AI endpoints/services without a feature gate (heuristic)
Get-ChildItem -Recurse -Path src/server/api/Sprk.Bff.Api/Api/Ai -Include *.cs | Select-String -Pattern "IOptions<|\.Enabled\b|Feature" | Select-Object -First 200
```

### Fix

- Add a single “master switch” option and (if needed) per-feature switches.
- Ensure disabled features return 503 with ProblemDetails (per ADR-019).

---

## ADR-019: API Errors and ProblemDetails

**Constraint**: Use consistent `ProblemDetails`/`ProducesProblem` patterns; avoid ad-hoc `{ error = ... }` payloads.

### Check For (Violations / Warnings)

```powershell
# Ad-hoc anonymous error payloads
Get-ChildItem -Recurse -Path src/server/api/Sprk.Bff.Api -Include *.cs | Select-String -Pattern "new\s*\{\s*error\s*=|WriteAsJsonAsync\(new\s*\{\s*error\s*=" | Select-Object -First 200

# Prefer Results.Problem / ProblemDetails helpers
Get-ChildItem -Recurse -Path src/server/api/Sprk.Bff.Api -Include *.cs | Select-String -Pattern "Results\.Problem\(|ProblemDetails" | Select-Object -First 200
```

### Fix

- Replace `Results.BadRequest(new { error = ... })` with `Results.Problem(...)` or shared helpers.
- Ensure endpoints declare `.ProducesProblem(...)` for expected failure modes.

---

## ADR-020: Versioning Strategy (APIs, Jobs, Client Packages)

**Constraint**: Version externally consumed contracts (HTTP, jobs, packages). Avoid breaking changes without versioning.

### Check For (Warnings)

```powershell
# Route versioning signals (heuristic)
Get-ChildItem -Recurse -Path src/server/api/Sprk.Bff.Api/Api -Include *.cs | Select-String -Pattern "MapGroup\(\"/api/|/v[0-9]+/" | Select-Object -First 200

# Job contract/version fields (heuristic)
Get-ChildItem -Recurse -Path src/server -Include *.cs | Select-String -Pattern "ContractVersion|SchemaVersion|MessageVersion|JobVersion" | Select-Object -First 200
```

### Fix

- Add explicit versioning for public endpoints and job messages.
- Document and enforce breaking-change rules per ADR-020.

---

## Quick Validation Script

For comprehensive check, run all patterns:

```powershell
# ADR Quick Check (PowerShell)

Write-Host "=== ADR Quick Check ==="

Write-Host "`n--- ADR-001: Azure Functions ---"
Get-ChildItem -Recurse -Include *.csproj,*.cs | Select-String -Pattern "Microsoft.Azure.WebJobs|Microsoft.Azure.Functions|\[FunctionName\]" | Select-Object -First 200

Write-Host "`n--- ADR-007: Graph Leakage (broad scan) ---"
Get-ChildItem -Recurse -Include *.cs | Select-String -Pattern "using Microsoft.Graph" | Where-Object { $_.Path -notmatch "\\Infrastructure\\" -and $_.Path -notmatch "SpeFileStore" } | Select-Object -First 200

Write-Host "`n--- ADR-008: Global Auth Middleware ---"
Select-String -Path "src/server/api/Sprk.Bff.Api/Program.cs" -Pattern "UseAuthorization|AuthorizationMiddleware" -AllMatches

Write-Host "`n--- ADR-009: IMemoryCache ---"
Get-ChildItem -Recurse -Path src -Include *.cs | Select-String -Pattern "IMemoryCache" | Select-Object -First 200

Write-Host "=== Check Complete ==="
```

---

## ADR-013 and Later (Read the ADRs)

The rules above include grep/pattern checks for ADR-001–ADR-012. For ADR-013+ (and any ADR not covered by a pattern), validate by reading the current ADR index and the relevant ADR document(s):

- Source of truth: `docs/reference/adr/README-ADRs.md`
- Validate ADR-013–ADR-020 by checking the specific constraints and checklists inside each ADR.
