# ADR Validation Rules Reference

> **Purpose**: Detailed validation rules and grep patterns for each ADR.
> **Usage**: Loaded by the `adr-check` skill during validation.

---

## ADR-001 + ADR-052: Minimal API BFF runtime; background-work placement per workload

**Constraint**: BFF endpoints in Minimal API; no Azure Functions or Durable Task packages inside `Sprk.Bff.Api`, and no Function-attributed methods (ADR-001). Where background, scheduled and event-driven work runs — the BFF, Azure Functions or Container Apps Jobs — is decided per workload under ADR-052 and stated in the Placement Justification. Inside the BFF: queue → `IJobHandler` (ADR-004); schedule → `IScheduledJob` (ADR-036). No new hand-rolled timer `BackgroundService`.

### Check For (Violations)

```bash
# Functions / Durable Task packages inside the BFF
grep -n "Microsoft.Azure.Functions\|Microsoft.Azure.WebJobs\|DurableTask" src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj

# Function attributes inside Sprk.Bff.Api (isolated worker uses [Function]; in-process used [FunctionName])
grep -rn "\[Function(\|\[FunctionName\|\[TimerTrigger\|\[QueueTrigger\|\[ServiceBusTrigger\|\[HttpTrigger" src/server/api/Sprk.Bff.Api/ --include="*.cs"

# A NEW hand-rolled timer BackgroundService (ADR-052 §1) — compare hits with the ArchTest ratchet allowlist
grep -rln "PeriodicTimer\|Task.Delay(" src/server/api/Sprk.Bff.Api/ --include="*.cs"
```

### Check For (Functions projects — verify ADR-052 §5–§6)

```bash
grep -rln "Microsoft.Azure.Functions.Worker" --include="*.csproj" .
```

For each Functions project:
- ✅ Lives under `src/server/functions/<Name>/`; references shared libraries only — never `Sprk.Bff.Api`
- ✅ The Placement Justification cites ADR-052 §3 signals and §4 costs; owner approval obtained for the first Function per tenancy model
- ✅ Authenticates app-only as the stamp's managed identity — no MSAL confidential client, no managed-identity assertion, no OBO
- ✅ Flex Consumption, Bicep in the stamp's provisioning, Key Vault references, identity-based host storage
- ❌ Reject if it hosts user-facing endpoints or duplicates BFF auth, correlation or ProblemDetails infrastructure

### Fix

- **Functions or Durable Task inside the BFF** → move the work to its own host (ADR-052), or to the BFF's in-process mechanism (ADR-004 / ADR-036)
- **Functions hosting BFF endpoints** → Minimal API endpoints in `Sprk.Bff.Api`
- **Multi-step orchestration** → ADR-052 §7 (Durable Task in its own host, or a hand-rolled state machine with a written reason)

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

Write-Host "`n--- ADR-001: Function attributes inside the BFF (violation) ---"
Get-ChildItem -Recurse -Path src/server/api/Sprk.Bff.Api -Include *.cs | Select-String -Pattern "\[Function\(|\[FunctionName\]|\[HttpTrigger\]|\[ServiceBusTrigger\]|\[TimerTrigger\]" | Select-Object -First 200

Write-Host "`n--- ADR-001: Functions / Durable Task packages inside the BFF (violation) ---"
Select-String -Path src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -Pattern "Microsoft.Azure.Functions|Microsoft.Azure.WebJobs|DurableTask"

Write-Host "`n--- ADR-052: Functions projects (review location, references, identity) ---"
Get-ChildItem -Recurse -Include *.csproj | Select-String -Pattern "Microsoft.Azure.Functions.Worker" | Select-Object -First 200

Write-Host "`n--- ADR-007: Graph Leakage (broad scan) ---"
Get-ChildItem -Recurse -Include *.cs | Select-String -Pattern "using Microsoft.Graph" | Where-Object { $_.Path -notmatch "\\Infrastructure\\" -and $_.Path -notmatch "SpeFileStore" } | Select-Object -First 200

Write-Host "`n--- ADR-008: Global Auth Middleware ---"
Select-String -Path "src/server/api/Sprk.Bff.Api/Program.cs" -Pattern "UseAuthorization|AuthorizationMiddleware" -AllMatches

Write-Host "`n--- ADR-009: IMemoryCache ---"
Get-ChildItem -Recurse -Path src -Include *.cs | Select-String -Pattern "IMemoryCache" | Select-Object -First 200

Write-Host "=== Check Complete ==="
```

---

## ADRs Without Inline Patterns (Read the ADRs)

For any ADR not covered by an inline pattern in this file (currently ADR-013 through ADR-027), validate by reading the current ADR index and the relevant ADR document(s):

- Source of truth: `docs/adr/README-ADRs.md`
- Validate by checking the specific constraints and checklists inside each ADR.

---

## ADR-028: Spaarke Auth Architecture (v2)

**Canonical**: `.claude/adr/ADR-028-spaarke-auth-architecture.md`. Function-based client contract (`useAuth()` + `authenticatedFetch` from `@spaarke/auth`); managed identity for server outbound; HMAC webhook signing; named API key auth schemes; tenant-specific MSAL authority.

### Pattern checks (grep)

```powershell
# VIOLATION: Raw fetch with manual Authorization header (must use authenticatedFetch)
Get-ChildItem -Recurse -Path src/client -Include *.ts,*.tsx -Exclude "*.test.*","*.spec.*" |
  Select-String -Pattern 'fetch\([^)]*headers[^)]*Authorization[^)]*Bearer' | Select-Object -First 200

# VIOLATION: Retired token-transport symbols
Get-ChildItem -Recurse -Path src -Include *.ts,*.tsx,*.js |
  Select-String -Pattern 'tokenBridge|__SPAARKE_BFF_TOKEN__|BridgeStrategy|XrmStrategy|MsalSilentStrategy|MsalRedirectStrategy|BridgeAuthStrategy|SessionStorageStrategy|__spaarke_bff_token_cache__' |
  Select-Object -First 200

# VIOLATION: PublicClientApplication instantiated outside @spaarke/auth
Get-ChildItem -Recurse -Path src/client -Include *.ts,*.tsx |
  Select-String -Pattern 'new PublicClientApplication\(' |
  Where-Object { $_.Path -notmatch 'shared\\Spaarke\.Auth\\' } | Select-Object -First 200

# CHECK: GraphClientFactory uses DefaultAzureCredential (canonical) when MI enabled
Select-String -Path src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs -Pattern 'DefaultAzureCredential|ManagedIdentityCredential'

# VIOLATION: secret-based credential for a BFF-identity client (ADR-028 A4).
# App-only  -> DefaultAzureCredential (UAMI) from DI.
# Confidential client / OBO -> MI-FIC client assertion, or KV certificate. NEVER a secret.
#
# NOTE (2026-08-17): the previous rule excluded `$_.Path -notmatch 'OBO|onBehalfOf'` — a PATH filter that
# never matched, because OBO appears in file CONTENT, not in file names. Every OBO site therefore tripped
# this check on every run, with no sanctioned alternative to move to. That false-positive churn is the
# reason A4 exists. Filter on the E-3 allowlist instead.
#
# $E3 = the transitional sites enumerated in ADR-028 exception E-3. Cite E-3 for these; do NOT report as
# violations. E-3 is time-boxed to `spaarke-auth-v4-dataverse-MI` — remove this allowlist when it closes.
$E3 = 'GraphClientFactory|DataverseAccessDataSource|DataverseUserClient|AgentTokenService|ReportingEmbedService|ReportingProfileManager|DataverseServiceClientImpl|DataverseWebApiService|DataverseWebApiClient'
# E-1 (per-customer owning apps) is architectural, not transitional — permanently exempt.
$E1 = 'SpeAdminTokenProvider|SpeAdminGraphService'

Get-ChildItem -Recurse -Path src/server -Include *.cs |
  Select-String -Pattern 'new ClientSecretCredential|\.WithClientSecret\(' |
  Where-Object { $_.Path -notmatch $E3 -and $_.Path -notmatch $E1 } | Select-Object -First 200

# VIOLATION: NEW confidential client built with a secret. E-3 does NOT license expansion — any
# .WithClientSecret( outside the allowlists above is a Critical finding, even during the migration.
# Also flag per-request CCA construction: client assertions require singleton-cached clients
# (reference impl: DataverseUserClient's static CCA cache keyed (tenant|client)).
Get-ChildItem -Recurse -Path src/server -Include *.cs |
  Select-String -Pattern 'ConfidentialClientApplicationBuilder' |
  Where-Object { $_.Path -notmatch $E3 -and $_.Path -notmatch $E1 -and $_.Path -notmatch 'CiamGraphClientFactory' } |
  Select-Object -First 200

# VIOLATION: MSAL authority /common or /organizations (INV-3 / INV-6)
Get-ChildItem -Recurse -Path src/client -Include *.ts,*.tsx,*.js |
  Select-String -Pattern 'authority.*/(common|organizations)' | Select-Object -First 200

# VIOLATION: accessToken typed as string prop (function-based contract retired this)
Get-ChildItem -Recurse -Path src/client -Include *.ts,*.tsx |
  Select-String -Pattern 'accessToken:\s*string|token:\s*string' |
  Where-Object { $_.Path -notmatch 'shared\\Spaarke\.Auth\\' -and $_.Path -notmatch '\.test\.|\.spec\.' } | Select-Object -First 200
```

### Expected exemptions (D-AUTH-7 sites — NOT violations if justified inline)
- SSE / EventSource setup where token must be in URL query (browser EventSource doesn't support headers)
- XHR upload where the SDK requires `accessToken` injection at construction
- Dataverse-direct Xrm.WebApi calls (different auth path from BFF)
- Third-party SDK constructors that require a token string

A finding is **only** a violation if the code site is NOT in the documented D-AUTH-7 exception list AND does not have an inline justification comment explaining why.
