# Task 200 completion notes — H4-shared handler (F19 automation)

**Task**: 200 — Implement H4SharedKvSecretsPopulationHandler
**Rigor level**: FULL
**Model tier**: Opus / xhigh
**Completed**: 2026-08-24
**Landing commits**:
- Phase A (manifest + generator extension): `ad32b3a8c` (2026-08-24)
- Phase B+C (C# handler + seams + DI wiring + tests): pending (this session)

---

## Scope delivered (all POML acceptance criteria met)

- ✅ `H4SharedKvSecretsPopulationHandler` implementing `IProvisioningHandler`
- ✅ New seam `ISourceServiceKeyExtractor` + `SdkSourceServiceKeyExtractor` (5 branches, one per `SourceServiceType`)
- ✅ New seam `ISharedKvSecretAccessor` + `SecretClientKvSharedSecretAccessor` (per-secret KV read+write)
- ✅ New record `SharedKvSecretSource` + `SourceServiceType` enum (parses `<type>:<az-resource-name>`)
- ✅ New rejection-code catalog `SharedKvSecretsPopulationRejectionCodes`
- ✅ HandlerIds + Dispatchable list + keyed forwarder + Worker DI (3-file dance)
- ✅ `HandlerRegistrationCompletenessTests` count assertion bumped 19 → 20
- ✅ 19 new unit tests (H4SharedKvSecretsPopulationHandlerTests) covering all POML acceptance criteria
- ✅ `Invoke-CatalogGenerator.ps1 -Verify` still exits 0 (Phase A determinism preserved)

---

## Files created (7 new)

| Path | Purpose |
|---|---|
| `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/KvSecretsPopulation/H4SharedKvSecretsPopulationHandler.cs` | Main handler — implements IProvisioningHandler with 9-step HandleAsync flow |
| `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/KvSecretsPopulation/ISourceServiceKeyExtractor.cs` | Seam interface — reads current cleartext from source Azure services |
| `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/KvSecretsPopulation/SdkSourceServiceKeyExtractor.cs` | Production SDK impl — 5 branches (Search / CognitiveServices / ServiceBus / Storage / Redis) |
| `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/KvSecretsPopulation/ISharedKvSecretAccessor.cs` | Narrow per-secret KV read+write seam |
| `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/KvSecretsPopulation/SecretClientKvSharedSecretAccessor.cs` | Production SDK impl using Azure.Security.KeyVault.Secrets.SecretClient |
| `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/KvSecretsPopulation/SharedKvSecretSource.cs` | Parsed service_ref record + SourceServiceType enum + TryParse |
| `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/KvSecretsPopulation/SharedKvSecretsPopulationRejectionCodes.cs` | Machine-stable rejection codes (h4shared-* prefix) |
| `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Handlers/H4SharedKvSecretsPopulationHandlerTests.cs` | 19 unit tests — hand-rolled fakes; ADR-038 Path #1 (no live Azure) |

---

## Files modified (10)

| Path | Change |
|---|---|
| `Sprk.Provisioning.ControlPlane.Core/Handlers/KvSecretsPopulation/IKvSecretManifest.cs` | Added `KvSecretValueSource.FromSharedService = 5` enum value + optional `ServiceRef` field on `KvSecretEntry` record |
| `Sprk.Provisioning.ControlPlane.Core/Handlers/KvSecretsPopulation/FileKvSecretManifest.cs` | Parse `from-shared-service` value_source; parse + validate service_ref (conditionally required); populate KvSecretEntry.ServiceRef |
| `Sprk.Provisioning.ControlPlane.Core/Handlers/KvSecretsPopulation/KvSecretValueResolver.cs` | Added `FromSharedService` branch returning `Failed` with pointer to H4-shared (per-tenant handler MUST filter these out) |
| `Sprk.Provisioning.ControlPlane.Core/Handlers/KvSecretsPopulation/H4KvSecretsPopulationHandler.cs` | Added filter `entries.Where(e => e.ValueSource != FromSharedService)` before writer call — keeps per-tenant flow decoupled from H4-shared entries |
| `Sprk.Provisioning.ControlPlane.Core/Handlers/HandlerIds.cs` | Added `H4Shared = "H4-shared"` const; appended to `Dispatchable` list |
| `Sprk.Provisioning.ControlPlane.Core/Handlers/HandlerDispatchRegistrationModule.cs` | Added keyed forwarder line ~108–109 |
| `Sprk.Provisioning.ControlPlane.Core/Sprk.Provisioning.ControlPlane.Core.csproj` | Added 4 NuGet packages: Azure.ResourceManager.Search 1.3.0, .ServiceBus 1.1.0, .Storage 1.4.2, .Redis 1.4.0 |
| `Sprk.Provisioning.ControlPlane.Worker/Program.cs` | Added 3 DI registrations (extractor + accessor + handler) near H4 block |
| `Sprk.Provisioning.ControlPlane.Tests/Dispatch/HandlerRegistrationCompletenessTests.cs` | Bumped `Dispatchable_ContainsExactlyNineteenIds` → `TwentyIds`; count 19 → 20 |
| `Sprk.Provisioning.ControlPlane.Tests/Handlers/FileKvSecretManifestTests.cs` | Updated T4 InlineData — AiSearch--AdminKey mapping changed (Phase A) from FromBicepOutput → FromSharedService; added SPE-ContainerTypeId case for FromBicepOutput coverage; added new test asserting FromSharedService entries carry non-empty ServiceRef |

Files intentionally NOT modified (per constraint set): manifest.yaml, Invoke-CatalogGenerator.ps1, generated/* (all Phase A artifacts — determinism contract preserved).

---

## Deviations from POML

### Deviation 1 — reused existing `KvSecretsPopulationOptions` (no sibling class)

POML step 5 said "reuse existing OR new sibling if divergent config needed". H4-shared has zero divergent config requirements today (no shell-out timeout, no role-def ID since H4-shared has no T5 grant path). Reused as-is; adding a sibling class with duplicate members would be pure noise. If future divergence emerges, the swap to `SharedKvSecretsPopulationOptions` is a one-file edit.

### Deviation 2 — H4-per-tenant handler now filters FromSharedService entries

Not surfaced explicitly in POML but discovered during recon: the `FileKvSecretManifest` parser currently fails on any unknown value_source. Adding `FromSharedService` to the enum means H4-per-tenant would see those entries and (via `KvSecretValueResolver` returning Failed) mark them all as write-failed → per-tenant H4 quarantines on every run. Added a filter in `H4KvSecretsPopulationHandler.HandleAsync` step (5.5) to skip FromSharedService entries before writer call. The BINDING pre-check still fires on the full entry list (must protect against ALL Delete ops, per §7.9 R4). Logged the split count for observability.

### Deviation 3 — separate accessor seam instead of extending existing IKvSecretsWriter

POML step 5 said "reuse existing IKvSecretsWriter (SecretClientKvWriter)". Discovered during recon that `IKvSecretsWriter.WriteAsync` takes a `KvSecretWriteRequest` batch + delegates value resolution to `IKvSecretValueResolver`. Both are incompatible with H4-shared's model (per-secret writes with caller-supplied cleartext from the extractor). Rather than widen `IKvSecretsWriter`'s cleartext contract (breaks ADR-028 discipline — see `ISharedKvSecretAccessor.cs` file header's Component Justification), added a narrow purpose-built seam `ISharedKvSecretAccessor` with `ReadAsync` + `WriteAsync` single-secret methods. Extension test in the file header cites all three justification questions per CLAUDE.md §11.

### Deviation 4 — `HandlerRegistrationCompletenessTests.Dispatchable_ContainsExactlyNineteenIds` renamed → `TwentyIds`

POML did not call this out explicitly but the completeness gate has a hardcoded count assertion. Bumped 19 → 20 (added H4Shared). Renamed the test method to keep the name self-documenting.

### Deviation 5 — updated existing `FileKvSecretManifestTests.T4` InlineData

The T4 test previously asserted `AiSearch--AdminKey` → `FromBicepOutput`. Phase A of task 200 already flipped that entry to `from-shared-service` (see commit `ad32b3a8c` diff). Without updating the test, the entire test file failed after Phase A landed. Restored coverage by (a) flipping the AiSearch--AdminKey inline case to `FromSharedService`, (b) adding a new `SPE-ContainerTypeId` case to preserve `FromBicepOutput` mapping coverage, and (c) adding a new fact `ReadAsync_RealEmbeddedManifest_FromSharedServiceEntries_CarryServiceRef` that asserts ServiceRef is populated for all shared entries (and null for non-shared).

### Deviation 6 — Search SDK method signature

The `SearchServiceResource.GetAdminKeyAsync` method signature differs from the POML's suggested `.GetAdminKeys()`: the actual SDK method is `GetAdminKeyAsync(SearchManagementRequestOptions? options = null, CancellationToken cancellationToken = default)`. Called with `cancellationToken:` named argument. Returned `SearchServiceAdminKeyResult.PrimaryKey`.

---

## Build + test evidence

```
$ dotnet build src/server/services/Sprk.Provisioning.ControlPlane.Core/
Build succeeded. 0 Warning(s). 0 Error(s). Time Elapsed 00:00:02.21

$ dotnet build src/server/services/Sprk.Provisioning.ControlPlane.Worker/
Build succeeded. 0 Warning(s). 0 Error(s). Time Elapsed 00:00:02.44

$ dotnet test src/server/services/Sprk.Provisioning.ControlPlane.Tests/
Passed! - Failed: 0, Passed: 1531, Skipped: 1, Total: 1532, Duration: 26 s

# H4-shared new tests only:
Passed! - Failed: 0, Passed: 19, Skipped: 0, Total: 19, Duration: 66 ms

# Task 200 impact scope (H4Shared + H4 + Completeness + FileKvSecretManifest):
Passed! - Failed: 0, Passed: 87, Skipped: 0, Total: 87, Duration: 305 ms

$ pwsh scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1 -Verify
Manifest shape:    OK (32 secrets)
BINDING never-delete guard: OK (Dataverse-ClientSecret, BFF-API-ClientSecret)
Dev exception guard:        OK (spaarke-spekvcert)
VERIFY: OK - generated/ is in sync with manifest.yaml.
```

Existing `H4KvSecretsPopulationHandlerTests` (per-tenant, 32 methods) all still pass after adding the FromSharedService filter — `BuildCanonicalEntries()` in that test file uses only non-FromSharedService entries so filter is a no-op there.

`HandlerRegistrationCompletenessTests` passes: all 20 Dispatchable IDs resolve to a keyed handler with matching HandlerId property; H14a/b/c remain non-keyed (in-process sub-handlers).

---

## What's NOT done (deferred to follow-on tasks)

### Deferred #1 — Bicep hardening (5 UAMI RBAC assignments on source services)

POML escalation trigger #2 explicitly scopes this OUT: "If any source Azure service returns 403 during H4-shared happy-path testing against Model 1 Prod, ... file separate task for `stacks/model1-shared.bicep` (or `platform-shared.bicep`) to emit these role assignments for the L2 UAMI. Do NOT retry-loop; do NOT proceed to task 201."

The 5 role assignments the L2 UAMI needs on the SHARED source services:
- `Cognitive Services User` on the OpenAI account (`sprksharedprod-openai`)
- `Cognitive Services User` on the DocIntel account (`sprksharedprod-docintel`)
- `Search Service Contributor` on `sprksharedprod-search`
- `Azure Service Bus Data Owner` on `sprksharedprod-servicebus`
- `Storage Account Contributor` on `sprksharedprodsa`
- `Redis Cache Contributor` on `sprksharedprod-redis`

Filed as follow-on task. Handler ships fully wired + tested; live-fire against Model 1 Prod requires the RBAC first.

### Deferred #2 — live-fire smoke test against Model 1 Prod

Not in POML scope (H4-shared is a Path #1 unit-tested handler by design; live-Azure coverage belongs in env-guarded smoke tests). Once Deferred #1 is done, an operator can invoke H4-shared via the L2 REST API against Model 1 Prod to validate end-to-end. Success criteria (from POML acceptance criteria + F19 evidence): all 6 shared secrets present on `sprk-prod-kv`, BFF `/health` no longer fails-fast on the F20 config chain.

### Deferred #3 — dispatcher DAG integration

H4-shared is now keyed-registered and dispatchable, but the state-reconciler's DAG (`DagAdvancer` in `.Core/Reconciler/`) does not yet reference `HandlerIds.H4Shared` as an edge. This is intentionally out of task 200 scope per POML: the handler exists + is testable + is dispatchable; the reconciler DAG that determines WHEN to enqueue it is a separate wiring concern.

---

## Coordination

- Phase A + B+C together close spec.md FR-36 + F19 automation gap
- Blocks: task 201 (H4b BulkAppSettings) — H4b's KV refs resolve to secrets H4-shared populates
- Sequencing: H4-per-tenant (task 047) || H4-shared (task 200) → H4b (task 201) → H9 (task 052) → BFF boots configured
- No hot-path decorations updated (this is pure L2 code, does not touch BFF `Sprk.Bff.Api/`)

---

## POML acceptance-criteria checklist

- ✅ (a) `H4SharedKvSecretsPopulationHandler` implements `IProvisioningHandler`
- ✅ (b) `ISourceServiceKeyExtractor` seam + `SdkSourceServiceKeyExtractor` production impl
- ✅ (c) H4-shared resolves values directly via `ISourceServiceKeyExtractor` and passes to `ISharedKvSecretAccessor` (deviation: dedicated accessor seam vs shared writer — see Deviation 3)
- ✅ (d) manifest.yaml already extended by Phase A
- ✅ (e) `Invoke-CatalogGenerator.ps1` already extended by Phase A; `-Verify` still exits 0
- ✅ (f) 6 F19 manifest entries already committed by Phase A
- ✅ (g) `HandlerIds.H4Shared` const + entry in `Dispatchable` list
- ✅ (h) `HandlerDispatchRegistrationModule.cs` gains keyed factory forwarder
- ✅ (i) `.Worker/Program.cs` gains concrete DI registrations
- ✅ (j) `H4SharedKvSecretsPopulationHandlerTests.cs` — 19 tests covering all cited scenarios
- ✅ (k) `HandlerRegistrationCompletenessTests` still passes (count bumped 19 → 20)
- ✅ (l) Core build + Worker build + Tests all exit 0
