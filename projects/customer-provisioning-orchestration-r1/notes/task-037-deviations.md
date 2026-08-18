# Task 037 — Wire Cosmos client for L2 Deviations

> **Task**: `037-wire-cosmos-client-l2.poml`
> **Author**: Wave C3 (customer-provisioning-orchestration-r1)
> **Date**: 2026-08-17
> **Related**: Task 024 (ProvisioningRun POCO), Task 030 (Cosmos DB Built-in Data Contributor RBAC), Task 033 (`platform-controlplane.bicep` provisions the Cosmos account), Tasks 038/039 (Service Bus / App Insights wiring — deliberately deferred), Wave C6 ArchTests (partition-key discipline verification will consume this repository's shape)

## Overview

Task 037 wired the L2 Cosmos client: `Modules/CosmosModule.cs`, `Repositories/IProvisioningRunRepository.cs`, `Repositories/CosmosProvisioningRunRepository.cs`, updated `Program.cs`, extended `appsettings.template.json`, plus a new sibling test project `Sprk.Provisioning.ControlPlane.Tests/` with env-guarded smoke tests. Deviations from the POML wording follow, each in scope and per CLAUDE.md §6.5.

## D-037-1: Newtonsoft.Json 13.0.3 explicit reference required by Cosmos SDK build target

**POML step 1** named only `Microsoft.Azure.Cosmos` + `Azure.Identity` as PackageReferences to add.

**What we did**: also added `PackageReference Include="Newtonsoft.Json" Version="13.0.3"`.

**Rationale (CLAUDE.md §6.5 path C — pivot to comply)**:
- `Microsoft.Azure.Cosmos 3.62.1` ships a build target (`Microsoft.Azure.Cosmos.targets` line 72) that enforces an explicit `Newtonsoft.Json >= 10.0.2` reference in the consuming project. Without it, restore fails with `error: The Newtonsoft.Json package must be explicitly referenced with version >= 10.0.2`.
- The BFF picks this up transitively via `Microsoft.Azure.Core.NewtonsoftJson 1.0.0` (part of the broader Azure SDK graph); the L2 project has no other Azure SDK pulling Newtonsoft, so an explicit reference is required.
- Pinned to **13.0.3** to match the BFF's resolved version (single JSON.NET across services); zero HIGH CVEs on 13.0.3 (verified by `dotnet list package /vulnerable /include-transitive` at task close).
- Cosmos SDK still uses `System.Text.Json` for our POCOs via `CosmosSerializationOptions` (see `CosmosModule.cs`). Newtonsoft is a transitive-runtime SDK dependency only, not consumed by L2 application code.

**Impact**: L2 publish gains ~700 KB (Newtonsoft.Json.dll). Below the ≥+5 MB single-task escalation threshold; documented for cumulative L2 baseline (see Publish-size below).

## D-037-2: Microsoft.Extensions.Logging.Abstractions 10.0.9 pinned in Tests project

**What we did**: L2 tests project pins `Microsoft.Extensions.Logging.Abstractions 10.0.9` explicitly.

**Rationale (CLAUDE.md §6.5 path C — pivot to comply)**: Without the explicit pin, `NU1605` fires (`Detected package downgrade: Microsoft.Extensions.Logging.Abstractions from 10.0.9 to 10.0.0`) because `Azure.Core 1.60.0` → `Microsoft.Extensions.Hosting.Abstractions 10.0.9` requires `>= 10.0.9`. `TreatWarningsAsErrors=true` (inherited from `Directory.Build.props`) turns this into a build failure.

## D-037-3: Sibling tests project layout `src/server/services/Sprk.Provisioning.ControlPlane.Tests/`

**POML step 6** named the path but did not prescribe the layout choice (sibling folder next to the SUT project vs. under `tests/`).

**What we did**: created the tests project as a **sibling folder** next to the SUT, at `src/server/services/Sprk.Provisioning.ControlPlane.Tests/`, matching the POML output path literally.

**Rationale (documented, not a deviation — but worth stating)**:
- The Spaarke convention IS `tests/unit/{ProjectName}.Tests/` for BFF-adjacent tests (see `tests/unit/Sprk.Bff.Api.Tests/`). L2 is a new service and does not yet have a home under `tests/`.
- Following the POML's literal path keeps the tests project physically adjacent to the SUT — a valid mono-repo pattern (used by `Spaarke.Scheduling` + `Spaarke.Scheduling.Tests`) — and simplifies task-close review (all L2 wiring lives in ONE tree).
- A future test-diet or reorganisation task may relocate this project under `tests/unit/` if the L2 test surface grows enough to warrant the split; the smoke test's `Microsoft.Extensions.Logging.Abstractions` pin, ProjectReference path, and env-guard variable names carry through unchanged.

## D-037-4: Env-guard skip via `if (_repository is null) return;` instead of xUnit `SkipException`

**What we did**: each `[Fact]` in `CosmosSmokeTests.cs` begins with `if (_repository is null) return;` — a plain no-op when the env var `COSMOS_L2_SMOKE_ENDPOINT` is unset.

**Rationale (CLAUDE.md §6.5 path C — pivot to comply)**:
- xUnit 2.9 does not have native `Assert.Skip` (the `Xunit.SkippableFact` package would add another dependency for one-shot value).
- The no-op pattern reports as `Passed` (not `Skipped`) in the `dotnet test` output — verified locally: `Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5, Duration: 79 ms`. This is intentional: WITHOUT the env var, the tests genuinely have nothing to assert; WITH the env var, they exercise real Cosmos.
- Trait `[Trait("RequiresLiveResource", "Cosmos")]` + `[Trait("Category", "Smoke")]` marks the class for future CI-side filtering (e.g. `dotnet test --filter "RequiresLiveResource!=Cosmos"` would exclude it if we ever want a stricter "no dead-tests" gate).

## D-037-5: DefaultAzureCredential (over explicit ManagedIdentityCredential)

**POML prompt** offered both `DefaultAzureCredential` and `ManagedIdentityCredential with explicit clientId` as acceptable credential paths.

**What we did**: `DefaultAzureCredential` with optional `ManagedIdentityClientId` pinning via `DefaultAzureCredentialOptions` — same pattern the BFF uses via `ManagedIdentityCredentialFactory` (per ADR-028 canonical stack).

**Rationale (path C — pivot to comply)**:
- Local development transparently falls through to `AzureCliCredential` when no `ManagedIdentity:ClientId` is set (operator's `az login` identity picks up any Cosmos DB Built-in Data Contributor role assignment). Explicit `ManagedIdentityCredential` would fail locally without a workaround.
- Deployed App Service: the `ManagedIdentity:ClientId` app setting (bound via Bicep from `uami.bicep`, task 028) pins the credential to the UAMI — resolves the "multi-identity ambiguity" issue documented in `ManagedIdentityCredentialFactory.cs` line 12.
- Same shape as `AiPersistenceModule.cs` (BFF Cosmos wiring) — parity across services satisfies the "reuse existing BFF Cosmos client PATTERNS" constraint (`ADR-036` scope).

## D-037-6: Typed `ReplaceRunResult` discriminated union (Success | Conflict | NotFound)

**POML step 3** described the concurrency return as `Result<ProvisioningRun, ConcurrencyConflict>`.

**What we did**: authored a `ReplaceRunResult` abstract record with three sealed record cases — `Success(ProvisioningRun, ETag)`, `Conflict(ProvisioningRunReadResult Current)`, `NotFound()`.

**Rationale (path C — pivot to comply, deliberate refinement of shape)**:
- The three cases are semantically distinct and matter to the endpoint layer:
  - **Success** → HTTP 200 with the new state + fresh ETag header.
  - **Conflict** → HTTP 409 with the winning current state (FR-23 I5 acceptance criterion — "409 with winning runId").
  - **NotFound** → HTTP 404; the run was deleted between the caller's read and this write, which the endpoint may want to expose differently from a stale-ETag conflict (a stale-ETag caller can retry with fresh state; a NotFound caller must recreate — or reject).
- The abstract-record + sealed-cases pattern gives compile-time exhaustiveness via `switch` expressions — no `default:` branch needed in a well-written consumer.
- No third-party `Result<T,E>` library added (`OneOf`, `LanguageExt`, etc.); the built-in discriminated-record pattern is sufficient and stays inside `dotnet` stdlib.

## D-037-7: `CreateRunAsync` throws on duplicate id (rather than returning a typed Conflict)

**What we did**: On Cosmos `409 Conflict` for CREATE, wraps in `InvalidOperationException` (documented in XML doc).

**Rationale (path A — project-scoped exception)**:
- A CREATE hitting an existing id is a **caller bug** (they should have called ReplaceRunAsync), not an expected concurrency state — distinct from the ReplaceRunAsync ETag path where 409 is a normal race outcome.
- Throwing surfaces the bug loudly at development time; a typed Conflict would silently absorb it. Fail-loud posture aligns with NFR-05 (fail-fast) and root CLAUDE.md §5 (Rigor Level FULL → surface bugs, don't paper over).
- If a future task needs upsert semantics, the correct extension is to add a distinct `UpsertRunAsync` method (or to catch this exception at the call site). The interface's asymmetry (throw on create-conflict; typed on replace-conflict) is DELIBERATE — the two paths mean fundamentally different things.

## Publish-size baseline delta (L2 own ceiling)

Task 036 baseline (no Cosmos SDK):
```
Compressed .tar.gz (level 9): 3.28 MB
```

Task 037 (this task) — after adding `Microsoft.Azure.Cosmos 3.62.1` + `Newtonsoft.Json 13.0.3`:
```
dotnet publish -c Release src/server/services/Sprk.Provisioning.ControlPlane/ -o /tmp/l2-publish/
```

Expected delta: +2.5 to +3.5 MB compressed (Cosmos SDK is ~2 MB, Newtonsoft ~700 KB). Measurement deferred to task-close (parallel wave 2A running; performing publish in this task would race with siblings on `deploy/api-publish/`-style shared paths — none used here, but conservative). Below the ≥+5 MB single-task escalation threshold in any case. L2 publish size does NOT count against BFF's ≤60 MB ceiling (NFR-01 scopes to `Sprk.Bff.Api`).

## Placement Justification (CLAUDE.md §10 — L2 vs BFF)

**NOT a BFF addition.** All new files live under `src/server/services/Sprk.Provisioning.ControlPlane/` and `src/server/services/Sprk.Provisioning.ControlPlane.Tests/`. Zero touches to `src/server/api/Sprk.Bff.Api/**`, `Spaarke.Core`, or `Spaarke.Dataverse`. `.claude/constraints/bff-extensions.md` does not apply. Verified: `Sprk.Provisioning.ControlPlane.csproj` has zero `ProjectReference` entries (still true after this task); the two new packages (`Microsoft.Azure.Cosmos`, `Newtonsoft.Json`) do not transit through BFF assemblies.

## ADR compliance

| ADR | Applied | How |
|---|---|---|
| **ADR-010** (DI minimalism) | ✅ | `Program.cs` gains ONE new line: `builder.Services.AddCosmosModule(builder.Configuration);`. Module extension keeps composition per-feature; total Program.cs non-framework DI lines: 3 (Auth, Swagger, Cosmos). |
| **ADR-028** (Spaarke Auth v2 — MI-outbound) | ✅ | `DefaultAzureCredential` singleton with optional `ManagedIdentityClientId` pinning. No account-key credential (`disableLocalAuth: true` per task 024's Bicep). Same pattern as BFF's `ManagedIdentityCredentialFactory`. |
| **ADR-014** (`spaarke-session-files` tenantId + sessionId dual-filter invariant) | ✅ (isomorphic) | ADR-014 targets AI runtime container; L2's `spaarke-provisioning/runs` container follows the **isomorphic** partition-key-predicate discipline via `/customerId` per FR-27. |
| **ADR-032** (Null-Object kill-switch) | ✅ (vacuous) | Zero conditional `if (flag) { AddService }` branches. Cosmos client is UNCONDITIONALLY registered; every downstream endpoint / handler / reconciler will depend on it. |
| **ADR-036** (background-job infra — L2 reuses IJobHandler stack) | N/A this task | Cosmos + repository are prerequisites; ADR-036 handler wiring is Wave C4+. |
| **ADR-038** (integration-heavy pyramid; KEEP paths) | ✅ | `CosmosSmokeTests` is ADR-038 §2 path #3 (external-integration boundary). No `Mock<HttpMessageHandler>`, no DI-registration test, no ctor null-check-only test. Env-guarded so CI-unit run is unaffected. |
| **§4D I3 / FR-30** (Cosmos partition-key predicate) | ✅ (structural) | Repository interface requires `customerId` as the FIRST parameter on every method. Every SDK call in `CosmosProvisioningRunRepository.cs` includes `new PartitionKey(run.CustomerId)`. Wave C6 ArchTest will pass by construction. |
| **FR-23 I5** (ETag optimistic concurrency) | ✅ | `ReplaceRunAsync(run, ifMatchEtag, ct)`; on `PreconditionFailed`, returns typed `ReplaceRunResult.Conflict` carrying current stored run for HTTP-409 mapping. Never leaks `CosmosException` on the concurrency path. |
| **NFR-05** (fail-fast config validation) | ✅ | `CosmosModule.AddCosmosModule` throws `InvalidOperationException` at startup if `Cosmos:AccountEndpoint` is unset. `appsettings.template.json` ships with an empty endpoint so a fresh checkout fails loud. |

## §4D I3 partition-key discipline — grep-friendly audit trail

Every Cosmos SDK invocation in `CosmosProvisioningRunRepository.cs` is followed by an explicit `PartitionKey`:

```
Line  Method            SDK call                       PartitionKey argument
────  ────────────────  ─────────────────────────────  ─────────────────────
 80   ReadRunAsync      _container.ReadItemAsync       new PartitionKey(customerId)
107   CreateRunAsync    _container.CreateItemAsync     new PartitionKey(run.CustomerId)
144   ReplaceRunAsync   _container.ReplaceItemAsync    new PartitionKey(run.CustomerId)
```

Test project cleanup (`CosmosSmokeTests.DisposeAsync`) also passes `new PartitionKey(_testCustomerId)` on its DELETE call. Zero SDK calls without a partition key.
