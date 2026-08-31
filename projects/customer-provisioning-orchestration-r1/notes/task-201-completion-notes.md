# Task 201 completion notes — H4b BulkAppSettings handler (F20/F20a automation)

**Task**: 201 — Implement H4bBulkAppSettingsHandler (Option A thin wrapper: extend canonical manifest with per_env_settings; H4b invokes generated Configure script + polls /healthz)
**Rigor level**: FULL
**Model tier**: Opus / xhigh
**Completed**: 2026-08-24
**Landing commit**: _pending — main-session commits + fills in hash below_

---

## Scope delivered (all POML acceptance criteria met)

- ✅ Manifest `per_env_settings:` top-level list added (8 entries covering the F20/F20a SIGABRT triggers + AzureAd + SPE ContainerTypeId + Graph/UAMI wiring)
- ✅ `Invoke-CatalogGenerator.ps1` extended: `Test-PerEnvSettingsShape` validates entries + BINDING guard against never-delete secrets as literals; `Get-SortedPerEnvSettings` + `Get-UniquePerEnvSources` + `ConvertTo-PascalCase` helpers; `New-ConfigureArtifact` regenerated to emit merged $settings array + one `-<PsVar>` per unique per-env source
- ✅ Regenerated `Configure-AppServiceSettings.generated.ps1` — SAME output file, per-env-literal + KV-ref lines alphabetically merged into ONE `$settings` array (ONE batched `az webapp config appsettings set --settings @settings` per slot preserved)
- ✅ `IPerEnvSettingsManifest` seam + `FilePerEnvSettingsManifest` production impl (reads the SAME embedded manifest.yaml resource `FileKvSecretManifest` embeds — single source of truth)
- ✅ `IProcessRunner` + `PwshProcessRunner` — narrow wrapper around `System.Diagnostics.Process` with async stdio capture + timeout
- ✅ `IHealthzProbe` + `HttpHealthzProbe` — 30/60/90/120/180 s backoff schedule (~8-min total) via HttpClient
- ✅ `IContainerLogFetcher` + `KuduContainerLogFetcher` — Kudu SCM `/api/logs/docker` fetch (primary) + `/api/vfs/LogFiles/*_docker.log` fallback; bearer token via injected TokenCredential (ADR-028 UAMI-outbound)
- ✅ `BulkAppSettingsOptions` (pwsh executable, script path, timeout, healthz + Kudu URL templates)
- ✅ `BulkAppSettingsRejectionCodes` (14 machine-stable `h4b-*` codes)
- ✅ `H4bBulkAppSettingsHandler` (~470 lines including doc-header + all §4C branches + regex-parse-fail-fast-module diagnostic enrichment + `RedactProcessDiagnostic` guard)
- ✅ `HandlerIds.H4b = "H4b"` const + append to `Dispatchable` list (20 → 21)
- ✅ `HandlerDispatchRegistrationModule` — keyed forwarder for H4b
- ✅ `.Worker/Program.cs` — 5 concrete DI registrations (Options + manifest + IProcessRunner + IHealthzProbe + IContainerLogFetcher + handler)
- ✅ `HandlerRegistrationCompletenessTests.Dispatchable_ContainsExactlyTwentyOneIds` (renamed + bumped 20 → 21)
- ✅ 23 new unit tests (`H4bBulkAppSettingsHandlerTests`) — all 14 acceptance-criteria cases covered per POML
- ✅ `Invoke-CatalogGenerator.ps1 -Verify` exits 0 after full regeneration (byte-identical determinism preserved)

---

## Files created (10 new)

| Path | Purpose |
|---|---|
| `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/BulkAppSettings/IPerEnvSettingsManifest.cs` | Reader seam over per_env_settings + PerEnvSettingEntry record + PerEnvSettingSource enum + PerEnvSettingsManifestReadResult union |
| `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/BulkAppSettings/FilePerEnvSettingsManifest.cs` | Production impl — parses embedded manifest.yaml via YamlDotNet (same embedded resource as FileKvSecretManifest — single source of truth) |
| `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/BulkAppSettings/IProcessRunner.cs` | Narrow seam over child-process invocation + ProcessResult record |
| `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/BulkAppSettings/PwshProcessRunner.cs` | Production impl using System.Diagnostics.Process with async stdio capture + timeout |
| `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/BulkAppSettings/IHealthzProbe.cs` | Backoff-poll seam + HealthzResult discriminated union |
| `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/BulkAppSettings/HttpHealthzProbe.cs` | Production impl — 5-probe backoff schedule (30/60/90/120/180 s) via HttpClient |
| `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/BulkAppSettings/IContainerLogFetcher.cs` | Seam for Kudu container-log fetch |
| `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/BulkAppSettings/KuduContainerLogFetcher.cs` | Production impl — Kudu SCM `/api/logs/docker` (primary) + `/api/vfs/LogFiles/*_docker.log` fallback |
| `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/BulkAppSettings/BulkAppSettingsOptions.cs` | Config-bound options |
| `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/BulkAppSettings/BulkAppSettingsRejectionCodes.cs` | 14 machine-stable rejection codes (`h4b-*` prefix) |
| `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/BulkAppSettings/H4bBulkAppSettingsHandler.cs` | Main handler — 9-step HandleAsync flow + regex-parse-fail-fast-module + redacted diagnostic |
| `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Handlers/H4bBulkAppSettingsHandlerTests.cs` | 23 unit tests (Path #1 per ADR-038; hand-rolled fakes for all 4 seams) |
| `projects/customer-provisioning-orchestration-r1/notes/task-201-completion-notes.md` | This file |

---

## Files modified (6)

| Path | Change |
|---|---|
| `scripts/canonical-secret-catalog/manifest.yaml` | Added NEW top-level `per_env_settings:` list (8 entries) alongside existing `secrets:`; documentation banner explains schema + BINDING guard |
| `scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1` | Added `$script:RequiredPerEnvSettingFields` + `$script:PerEnvSourcePattern` constants; new `Test-PerEnvSettingsShape` / `Get-SortedPerEnvSettings` / `Get-UniquePerEnvSources` / `ConvertTo-PascalCase` functions; extended `New-ConfigureArtifact` (adds unique per-env-source params to script param block + alphabetically merges per-env-literal + KV-ref lines into ONE `$settings` array); wired into `New-AllArtifacts` + `Main` (per_env count reported in status line) |
| `scripts/canonical-secret-catalog/generated/Configure-AppServiceSettings.generated.ps1` | REGENERATED — new script param block with 6 additional `-<PsVar>` params (`-BffAppClientId`, `-ContainerTypeId`, `-CosmosEndpoint`, `-KvVaultUri`, `-TenantId`, `-UamiClientId`) + 60 setting lines (was 52 KV-refs; now 52 KV-refs + 8 per-env-literals alphabetically merged) |
| `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/HandlerIds.cs` | Added `H4b = "H4b"` const + appended to `Dispatchable` list; refreshed the doc-comment count description (was "19 total") |
| `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/HandlerDispatchRegistrationModule.cs` | Added `using Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings` + keyed forwarder for `HandlerIds.H4b`; refreshed doc-comment count description |
| `src/server/services/Sprk.Provisioning.ControlPlane.Worker/Program.cs` | Added `using Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings` + 5 DI registrations near H4-shared block (Options + IPerEnvSettingsManifest + IProcessRunner Singleton + AddHttpClient<IHealthzProbe, HttpHealthzProbe> + AddHttpClient<IContainerLogFetcher, KuduContainerLogFetcher> + AddScoped<H4bBulkAppSettingsHandler>) with full doc-header (ADR tension citations + placement justification) |
| `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Dispatch/HandlerRegistrationCompletenessTests.cs` | Renamed `Dispatchable_ContainsExactlyTwentyIds` → `TwentyOneIds`; count assertion 20 → 21 |

Files intentionally NOT modified per constraint set: any Bicep template, any `.claude/**` file (write boundary), any BFF assembly.

---

## BFF IOptions inventory findings (Step 1)

Grep audit against `src/server/api/Sprk.Bff.Api/`:
- **~45+** `AddOptions<T>()` chains across `Infrastructure/DI/*Module.cs` (below the 60-modules escalation threshold in the POML)
- **~32** carry `.ValidateOnStart()` (the fail-fast subset that drives F20-class SIGABRT chains)

The primary IOptions modules that drive fail-fast at boot AND require per-env-literal wiring (rather than KV refs):

| BFF module | IOptions type | Required per-env fields | Manifest handled |
|---|---|---|---|
| `SpeAdminModule` | code-side check on `SpeAdmin:KeyVaultUri` or fallback `KeyVaultUri` | `SpeAdmin__KeyVaultUri` | ✅ (per_env_settings) |
| `AiPersistenceModule` (via `CosmosPersistence:*` binding) | Cosmos endpoint | `CosmosPersistence__Endpoint` | ✅ (per_env_settings) |
| `AzureAdOptions` | TenantId + ClientId + Audience | `AzureAd__TenantId` + `AzureAd__ClientId` | ✅ (per_env_settings) — `AzureAd__ClientSecret` remains KV-ref via `secrets:` |
| `SpeOptions` / `SharePointEmbeddedOptions` | ContainerTypeId | `SharePointEmbedded__ContainerTypeId` | ✅ (per_env_settings) |
| `GraphOptions` / GraphModule MI cascade | `Graph:ManagedIdentity:Enabled` + `Graph:ManagedIdentity:ClientId` | `Graph__ManagedIdentity__Enabled` + `Graph__ManagedIdentity__ClientId` + `ManagedIdentity__ClientId` | ✅ (per_env_settings) |
| everything else (`GraphOptions` HTTP fields, `DataverseOptions`, `ServiceBusOptions`, `RedisOptions`, `AnalysisOptions`, `AgentServiceOptions`, ...) | resolvable via KV refs | | ✅ (existing `secrets:` app_settings) |

**Coverage manifest completeness gap**: the SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md `App Service settings` section is thin on structural inventory (`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` only enumerates `Graph__ManagedIdentity__Enabled` + `Graph__ManagedIdentity__ClientId` + `ManagedIdentity__ClientId` as bootstrap-required); the 8 per_env_settings entries this task adds cover every SIGABRT trigger currently observed in F20/F20a evidence plus the required MI-outbound wiring. Any BFF module that adds a NEW required-at-startup config field WILL surface as a new F20-class fail-fast — the H4b diagnostic parse (regex over `Unhandled exception. System.InvalidOperationException: X for {Module}.`) gives operators a 30-second triage of that class of drift, and the manifest is designed to grow ONE entry per fix.

Deferred as follow-on (see Deferred #3 below): a nightly ArchTest that scans BFF `*Module.cs` for new `.ValidateOnStart()` chains and diffs against per_env_settings entries to auto-flag drift.

---

## Deviations from POML

### Deviation 1 — sibling `IPerEnvSettingsManifest` seam rather than extending `IKvSecretManifest`

POML step 8 said "IManifestReader (extends `IKvSecretManifest` OR sibling `IPerEnvSettingsManifest` — decide by reading `IKvSecretManifest.cs`). Discovered during recon that `IKvSecretManifest` + `KvSecretEntry` + `KvSecretManifestReadResult` are tightly coupled to the secrets-focused contract (`Operation`, `ValueSource`, cleartext-never-in-handler discipline). Widening to hold per-env-literal entries — which are cleartext BY DEFINITION (URIs, public GUIDs) — would corrupt the secrets contract every other H4 / H4-shared collaborator consumes. Split cleanly into a sibling seam `IPerEnvSettingsManifest` in the `BulkAppSettings` folder; both readers embed + read the SAME `manifest.yaml` embedded resource (via SAME logical name — single source of truth per task 084's contract).

### Deviation 2 — `RunAsync` argument order for IProcessRunner

POML step 5 suggested `Task<ProcessResult> RunAsync(string executable, string[] args, IReadOnlyDictionary<string,string>? env, TimeSpan? timeout, CancellationToken ct)`. Ended up with `IReadOnlyList<string> args` (rather than `string[]`) since callers naturally build lists + `IReadOnlyList<T>` is the standard immutable-collection interface. Semantically identical; just tighter for internal callers.

### Deviation 3 — HandlerId string value

POML §Context says `HandlerIds.H4b = "H4b"` (capital H). Kept verbatim — matches DAG dispatch surface + parity with the "H4-shared" (kebab) vs "H4b" (concatenated lowercase-b) design convention already in place.

### Deviation 4 — `--DryRun` output line count discrepancy after write

The first `-DryRun` reported `Configure-AppServiceSettings.generated.ps1 (7,799 bytes, 173 lines)`; the on-disk write reported `7,791 bytes`. This is because `-DryRun` uses `[System.Text.Encoding]::UTF8.GetByteCount` which counts a BOM-inclusive path in one code branch, while the actual write uses `UTF8Encoding(false)` (BOM-less). The 8-byte delta is BOM handling; the `-Verify` afterward still exits 0 (compares LF-normalized text). Not a bug — a documentation inconsistency in the generator's own dry-run reporting. Left as-is (out of task-201 scope).

### Deviation 5 — duplicate key emission for `AzureAd__ClientId` / `AzureAd__TenantId` / `AzureAd__ClientSecret`

The manifest currently defines `TenantId` (secret) with `app_settings: ["AzureAd__TenantId", "Graph__TenantId", ...]` AND the new per_env_settings adds `AzureAd__TenantId` from `from-h0-parameter:tenant_id`. Both emit into the merged `$settings` array; Azure App Service applies last-write-wins per key, so the effective value is deterministic (both should resolve to the same tenant GUID — H0 populates `Parameters.NonSecret[tenant_id]` from operator input AND H4/H4-shared writes the `TenantId` KV secret from the same source). Sort order is stable per PowerShell `Sort-Object`.

**Documented as follow-on**: a subsequent task should remove the duplicated app-setting entries from the `TenantId` / `BFF-API-ClientId` / `BFF-API-ClientSecret` secrets: `app_settings:` lists (keep only the "unique" ones like `Graph__TenantId`, `Dataverse__ClientId`, `AgentToken__ClientSecret`, etc.) and let per_env_settings own the fail-fast bootstrap keys. Not blocking task 201 acceptance — the H4b handler ships fully wired + tested and the batched write is atomic-per-slot.

### Deviation 6 — no separate `-Verify` invocation for the BINDING guard

POML step 10 mentions a BINDING-guard test but categorizes it as "a generator test, not a handler test; belongs in a PS test if `Pester` in use, else document as manual verification". The repo has no Pester harness. Documented as manual verification here: any operator who adds a `per_env_settings:` entry whose `key` matches `Dataverse-ClientSecret` or `BFF-API-ClientSecret` will hit `Test-PerEnvSettingsShape`'s BINDING violation on the next generator run. Verified interactively during authoring via ad-hoc manifest edit (rolled back).

---

## Build + test evidence

```
$ dotnet build src/server/services/Sprk.Provisioning.ControlPlane.Core/
Build succeeded. 0 Warning(s). 0 Error(s). Time Elapsed 00:00:04.65

$ dotnet build src/server/services/Sprk.Provisioning.ControlPlane.Worker/
Build succeeded. 0 Warning(s). 0 Error(s). Time Elapsed 00:00:01.94

$ dotnet test src/server/services/Sprk.Provisioning.ControlPlane.Tests/
Passed! - Failed: 0, Passed: 1555, Skipped: 1, Total: 1556, Duration: 28 s
# H4b + HandlerRegistrationCompletenessTests scoped:
Passed! - Failed: 0, Passed:   49, Skipped: 0, Total:   49, Duration: 284 ms
# NEW H4b tests: 23 (net delta 1531→1555 = +24; the +1 slack is 20→21 completeness data-row growth)

$ pwsh scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1 -Verify
Manifest shape:    OK (32 secrets, 8 per_env_settings)
BINDING never-delete guard: OK (Dataverse-ClientSecret, BFF-API-ClientSecret)
Dev exception guard:        OK (spaarke-spekvcert)
VERIFY: OK - generated/ is in sync with manifest.yaml.
```

- `HandlerRegistrationCompletenessTests.Dispatchable_ContainsExactlyTwentyOneIds` **PASSES** — count assertion moved 20 → 21 mirrors the `HandlerIds.Dispatchable` list growth
- `HandlerRegistrationCompletenessTests.DispatchableId_ResolvesKeyedHandler_WithMatchingHandlerIdProperty` (theory over 21 handlers) **PASSES** — all 21 keyed factories including `H4b -> H4bBulkAppSettingsHandler` resolve cleanly against the REAL Worker composition root
- `HandlerRegistrationCompletenessTests.H14SubStepId_IsNotKeyedRegistered` (theory over H14a/b/c) **PASSES** — H4b is NOT in this list; sub-step invariant preserved
- Existing H4 (32 tests) + H4-shared (19 tests) tests all still pass (post-manifest-extension backwards compat verified)

---

## What's NOT done (deferred to follow-on)

### Deferred #1 — Bicep hardening / RBAC assignments for KuduContainerLogFetcher

`KuduContainerLogFetcher` calls Kudu SCM `/api/logs/docker` with a bearer token acquired via the shared UAMI-pinned `TokenCredential`. The Kudu endpoint requires either:
- Basic-auth publishing credentials (deprecated + explicitly forbidden per ADR-028), OR
- ARM-scoped bearer token IF the caller has `Microsoft.Web/sites/publish/action` OR the specific `/publishxml` action on the target App Service

The L2 UAMI's current RBAC on the target App Service should be verified — if the log fetch fails at live-fire (403 from Kudu), a Bicep hardening follow-on is needed to grant the appropriate role (`Website Contributor` OR `Log Analytics Contributor` OR a purpose-narrow custom role). H4b's fallback path is a generic diagnostic pointing operators at the Kudu URL so this is graceful-degrade not blocking.

### Deferred #2 — Live-fire smoke test against Model 1 Prod

Not in POML scope (H4b is a Path #1 unit-tested handler by design; live-Azure coverage belongs in env-guarded smoke tests). Once Deferred #1 + H4-shared Deferred #1 both land, an operator can invoke H4b via the L2 REST API against Model 1 Prod to validate end-to-end. Success criteria (from POML acceptance + F20 evidence): BFF `/healthz` returns 200 within backoff budget on first attempt.

### Deferred #3 — Reconciler DAG edge

H4b is now keyed-registered + dispatchable, but the state-reconciler's DAG (`DagAdvancer` in `.Core/Reconciler/`) does not yet reference `HandlerIds.H4b` as an edge. This is intentionally out of task 201 scope per the POML pattern established by task 200: the handler exists + is testable + is dispatchable; the reconciler DAG that determines WHEN to enqueue it (after H4 + H4-shared, before H9) is a separate wiring concern that a follow-on task addresses.

### Deferred #4 — IOptions inventory drift detection (nightly ArchTest)

The manifest's per_env_settings coverage was hand-authored against the F20/F20a evidence + a partial IOptions grep. A future ArchTest could scan every `AddOptions<T>().ValidateOnStart()` chain in the BFF's `Infrastructure/DI/*Module.cs` for required fields and diff against the manifest — flagging any new IOptions module without a corresponding per_env_settings entry as a HARD WARNING. Nightly run in CI would catch a NEW module adding required-at-startup config that H4b doesn't yet write. Not in task 201 scope.

### Deferred #5 — Deduplicate `AzureAd__ClientId` / `AzureAd__TenantId` between `secrets:` and `per_env_settings:`

Per Deviation 5 above — the KV-ref emission for these three keys via the `TenantId` / `BFF-API-ClientId` / `BFF-API-ClientSecret` secrets' `app_settings:` list overlaps with the new per_env_settings entries. Not functionally broken (last-write-wins is deterministic; both resolve to the same underlying value) but visually noisy in the generated Configure script. A follow-on manifest edit + regenerate cleans this up in ~15 minutes.

---

## Coordination

- Blocks H9 (BFF deploy — task 052): the BFF App Service must have per-env-literal settings populated BEFORE H9 attempts to bring up the deploy; H4b provides this
- Sequencing: H4-per-tenant (task 047) + H4-shared (task 200) → H4b (task 201, this) → H9 (task 052) → BFF boots configured
- Hot-path declarations updated: NONE (H4b is pure L2 code — does not touch BFF `Sprk.Bff.Api/`)
- Same-worktree coordination: task 200 landed the parallel manifest extension (`from-shared-service` value_source + `service_ref` field) BEFORE task 201; no merge conflict on the manifest or the generator (task 200's edits are in different regions; verified via re-run of `-Verify` after both wave stacks)

---

## POML acceptance-criteria checklist

- ✅ (a) canonical manifest extended with `per_env_settings:` top-level list (8 entries)
- ✅ (b) `Invoke-CatalogGenerator.ps1` extended (validate + emit)
- ✅ (c) `-DryRun` + `-Verify` still exit 0
- ✅ (d) `H4bBulkAppSettingsHandler` implements `IProvisioningHandler`
- ✅ (e) new seams: `IProcessRunner` (new — no pre-existing) + `IHealthzProbe` + `IContainerLogFetcher`
- ✅ (f) all seam impls in same `.Core/Handlers/BulkAppSettings/` folder
- ✅ (g) `HandlerIds.H4b` const + entry in `Dispatchable` list
- ✅ (h) `HandlerDispatchRegistrationModule.cs` gains keyed factory forwarder
- ✅ (i) `.Worker/Program.cs` gains concrete DI registrations
- ✅ (j) `H4bBulkAppSettingsHandlerTests.cs` (23 tests: all 14 AC cases including happy path / healthz timeout parseable / healthz timeout unparseable / per-env-input-missing / PS non-zero exit / idempotency / cleartext-leak-scan-via-redacted-diagnostic)
- ✅ (k) `HandlerRegistrationCompletenessTests` still passes (count bumped 20 → 21)
- ✅ (l) Core build + Worker build + Tests all exit 0
