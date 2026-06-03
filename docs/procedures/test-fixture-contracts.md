# Test Fixture Contracts

> **Last Updated**: 2026-06-01 by `sdap.bff.api-test-suite-repair-r2` task 090 wrap-up
> **Purpose**: Enumerate the contracts between production code and test fixtures so future PRs don't introduce contract violations that surface as obscure test failures.
> **Cross-references**:
> - [`docs/procedures/testing-and-code-quality.md`](testing-and-code-quality.md) §18.2 (Fixture-Config-FIRST Inspection Protocol)
> - [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) § F.2 (binding rule)
> - r2 worked examples: RB-T028-07 (task 025) + RB-T028-08 (task 037)

---

## Why this document exists

In `sdap.bff.api-test-suite-repair-r2`, two ledger entries (RB-T028-07 + RB-T028-08) were initially flagged as "verify subsumed by task 011 cluster fix" but turned out to be **test fixture contract violations** that the cluster fix had unmasked but not closed.

Both bugs were invisible at fixture-author time (the test infrastructure was internally consistent) and only surfaced when a sibling change exercised the contract differently. **A canonical inventory of fixture contracts would have caught both preemptively.**

This document IS that canonical inventory.

---

## How to use this document

Before adding or modifying a test fixture:
1. Locate the production service or code path your fixture will exercise.
2. Read the relevant § below for that service.
3. Match every required config key, every claim format, every nullable-vs-required dep.

Before adding a new production service that consumes a config key OR a claim:
1. Search this document for prior usage of that key/claim.
2. Add an entry below documenting the new contract.
3. Update the test fixtures to provide it.

---

## §1. Authentication contracts

### §1.1 Entra ID `oid` claim — MUST be a valid GUID

**Production code**: `Sprk.Bff.Api.Api.Insights.PrecedentAdminEndpoints.CreatePrecedent` (and any handler doing `Guid.TryParse(callerOid, out var callerGuid)` for fallback identity).

**Contract**: The `oid` claim on the incoming JWT MUST be parseable as a `System.Guid`. Production receives this from Microsoft Entra ID where the claim contract guarantees a GUID; tests using `JwtBearerHandler` stubs MUST match this contract.

**Failure mode if violated**: `Guid.TryParse` returns false. Fallback identity logic silently produces null. Endpoint returns 201 successfully (outer assertion passes) but Moq verification `r.ReviewerByUserId.HasValue` fails → cryptic "expected once, but was 0 times" error.

**Worked example**: r2 task 037 / RB-T028-08. `IntegrationTestConstants.TestUserId` was the literal `"test-user-00000000-0000-0000-0000-integration001"` (47 chars, not GUID-parseable). Production fallback silently returned null reviewer. Fix: replace with valid GUID `"11111111-1111-1111-1111-111111111111"`.

**Fixture location**: `tests/integration/Spe.Integration.Tests/IntegrationTestFixture.cs` `IntegrationTestConstants.TestUserId`.

### §1.2 Entra ID `tid` claim — present + tenant-shape

**Production code**: `AuditEnrichmentMiddleware`, `GraphTokenCache`, multi-tenant cache-key builders.

**Contract**: The `tid` claim MUST be present on any authenticated request. Format is conventionally a GUID but production-tolerant of other strings.

**Failure mode**: Multi-tenant cache key builders return `tid=anonymous` or throw; cross-tenant isolation tests fail.

**Fixture location**: `IntegrationTestFixture.cs` config key `AzureAd:TenantId`.

### §1.3 `AzureAd:ClientId` + `AzureAd:Audience` — both required

**Production code**: `Sprk.Bff.Api.Configuration.AuthConfiguration` JWT validation.

**Contract**: Both keys MUST be set on the fixture's config dictionary; both MUST match the `aud` claim format expected by `JwtBearerOptions`.

**Failure mode**: 401 Unauthorized on every authenticated request, regardless of token validity.

**Fixture location**: `IntegrationTestFixture.cs` (`AzureAd:ClientId` = `"test-app-id"`, `AzureAd:Audience` = `"api://test-app-id"`).

---

## §2. Persistence contracts

### §2.1 `CosmosPersistence:Endpoint` requires `CosmosPersistence:DatabaseName`

**Production code**: `Sprk.Bff.Api.Services.Ai.Sessions.SessionPersistenceService` constructor (line ~52-53) throws `InvalidOperationException("CosmosPersistence:DatabaseName is not configured.")` when the endpoint is set but the database name is missing.

**Contract**: If a fixture sets `CosmosPersistence:Endpoint`, it MUST also set `CosmosPersistence:DatabaseName`. The two keys are paired.

**Failure mode if violated**: Per-request DI resolution throws `InvalidOperationException` when an endpoint resolves `ISessionPersistenceService` from `IServiceProvider`. Surfaces as 500 NoServiceFound on every request to endpoints that inject session services.

**Sibling pattern (informational)**: `AuditLogService` defaults to `"spaarke-ai"` if `DatabaseName` is missing. The asymmetry between `SessionPersistenceService` (throws) and `AuditLogService` (defaults) is by design but easy to miss. If touching either, audit both.

**Worked example**: r2 task 025 / RB-T028-07. `IntegrationTestFixture` set `Endpoint` but not `DatabaseName`. After task 011's `ChatSessionManager` promotion to unconditional, every per-request resolution of `ISessionPersistenceService` threw, taking down 9 upload tests. Fix: add `"CosmosPersistence:DatabaseName"] = "spaarke-ai-test"`.

**Fixture location**: `IntegrationTestFixture.cs` lines 97-103.

### §2.2 `Redis:Enabled` — opt-in only

**Production code**: `RedisCacheModule.AddRedisCache` — registers `IConnectionMultiplexer` only when `Redis:Enabled=true`.

**Contract**: Tests typically set `Redis:Enabled=false`; in-memory `IDistributedCache` is registered as the fallback. Any service that injects `IConnectionMultiplexer` directly (not `IDistributedCache`) MUST use `sp.GetService<IConnectionMultiplexer>()` with null-tolerance OR the fixture MUST register a `Mock<IConnectionMultiplexer>`.

**Failure mode**: 500 NoServiceFound on services injecting `IConnectionMultiplexer` directly when fixtures leave Redis off.

**Fixture location**: `IntegrationTestFixture.cs` `Redis:Enabled` = `"false"`; production code typically uses `IDistributedCache` (abstracted).

---

## §3. AI feature gate contracts

### §3.1 The compound gate — `DocumentIntelligence:Enabled` × `Analysis:Enabled`

**Production code**: `AnalysisServicesModule.AddAnalysisServicesModule` reads both flags. Many AI services are registered ONLY when both flags are true.

**Contract**: A fixture MAY set either flag to `false` to exercise kill-switch behavior. Per ADR-032 + r2 task 011, EVERY conditionally-registered service consumed by an unconditionally-mapped endpoint MUST have a Null-Object (P3 fail-fast throwing `FeatureDisabledException`) registered in the `else` branch. See `.claude/adr/ADR-032-bff-nullobject-kill-switch.md`.

**Failure mode**: Test fixtures that set the gate off MUST either (a) accept `503 Feature Disabled` from kill-switched endpoints OR (b) override-register the affected services with `Mock<T>` BEFORE the host starts. See §4 below.

**Worked example**: r2 task 011 closed 18 services migrating to ADR-032. The 4 RB-T028 ledger entries traced to this gate combination not being symmetric.

**Fixture location**: `IntegrationTestFixture.cs` lines 135-141 (`DocumentIntelligence:Enabled = "true"` + `Analysis:Enabled = "true"` for default IT mode).

### §3.2 `AzureOpenAI:Endpoint` + `AzureOpenAI:ChatModelName` — both required for `IChatClient`

**Production code**: `Sprk.Bff.Api.Infrastructure.DI.AiModule.AddAiModule` registers `IChatClient` only when both keys are present.

**Contract**: BOTH keys MUST be present together. If either is missing, `IChatClient` is not registered → 500 on endpoints injecting it.

**Failure mode**: 500 NoServiceFound on chat / playbook / agent endpoints.

**Fixture location**: `IntegrationTestFixture.cs` lines 148-149.

### §3.3 AI Search keys — `DocumentIntelligence:AiSearchEndpoint` + `DocumentIntelligence:AiSearchKey`

**Production code**: `AnalysisServicesModule.AddRagServices` registers `IRagService` + `SearchIndexClient` only when both keys are non-empty.

**Contract**: Tests exercising RAG paths MUST set both keys. Tests that DON'T want RAG can leave one or both empty — the `else` branch registers `NullRagService` (per ADR-032) and endpoints return 503.

**Failure mode if no Null-Object**: Pre-task-011, this gap would surface as 500 NoServiceFound on RAG endpoints. Post-task-011, the Null-Object returns 503.

**Fixture location**: `IntegrationTestFixture.cs` lines 144-145.

---

## §4. Service-override timing contract

### §4.1 DI overrides MUST happen in `ConfigureTestServices`, BEFORE host startup

**Production code**: ASP.NET endpoint metadata generation runs at host startup and validates that every endpoint handler parameter resolves from DI.

**Contract**: A fixture that wants to mock a service MUST register the mock in `ConfigureTestServices(services => services.AddSingleton<T>(mockObject))` BEFORE the host starts. Adding the mock post-startup (e.g., inside an `[Fact]` body) does NOT prevent metadata-gen from failing.

**Failure mode if violated**: Host startup aborts with `Failure to infer one or more parameters` — taking down the ENTIRE test fixture, not just the affected test. See [`docs/procedures/testing-and-code-quality.md`](testing-and-code-quality.md) §18.1 for diagnosis.

**Sibling pattern**: `ChatEndpointsTestFixture` overrides `ChatSessionManager` with a 3-arg ctor (lines 497-505) that omits `ISessionPersistenceService` — this is the right pattern when the production service has too many runtime deps for an integration test to satisfy.

**Fixture location**: `tests/unit/Sprk.Bff.Api.Tests/CustomWebAppFactory.cs` `ConfigureTestServices`.

---

## §5. Background-job / hosted-service contracts

### §5.1 Test fixtures SHOULD remove hosted services

**Production code**: Multiple `*BackgroundService.cs` files registered via `AddHostedService<T>()`. They start at host startup and run continuously.

**Contract**: Test fixtures typically SHOULD remove all hosted services to keep tests deterministic:
```csharp
services.RemoveAll<IHostedService>();
```
unless the test specifically exercises a hosted service.

**Failure mode if hosted services run during tests**: Race conditions on Redis / Dataverse / shared resources. Difficult-to-reproduce flakes.

**Fixture location**: `CustomWebAppFactory.cs` `ConfigureTestServices`.

---

## §6. Common config keys reference

The following keys are routinely set in `tests/integration/Spe.Integration.Tests/IntegrationTestFixture.cs`. Adding a new test that needs a new key SHOULD add it here AND to the fixture.

| Domain | Required keys (paired) | Source code touchpoint |
|---|---|---|
| Auth (Entra ID) | `AzureAd:Instance`, `AzureAd:TenantId`, `AzureAd:ClientId`, `AzureAd:Audience` | `AuthConfiguration` |
| Service Bus | `ServiceBus:ConnectionString`, `ServiceBus:QueueName` (paired) | `ServiceBusJobProcessor` |
| Cosmos | `CosmosPersistence:Endpoint`, `CosmosPersistence:DatabaseName` (paired — see §2.1) | `SessionPersistenceService` |
| Cache | `Redis:Enabled` (opt-in only — see §2.2) | `RedisCacheModule` |
| Graph | `Graph:TenantId`, `Graph:ClientId`, `Graph:ClientSecret`, `Graph:UseManagedIdentity`, `Graph:Scopes:0` | `GraphClientFactory` |
| Dataverse | `Dataverse:EnvironmentUrl`, `Dataverse:ServiceUrl`, `Dataverse:ClientId`, `Dataverse:ClientSecret`, `Dataverse:TenantId` | `DataverseService` |
| AI gate | `DocumentIntelligence:Enabled`, `Analysis:Enabled` (compound — see §3.1) | `AnalysisServicesModule` |
| AI Chat | `AzureOpenAI:Endpoint`, `AzureOpenAI:ChatModelName` (paired — see §3.2) | `AiModule` |
| AI Search | `DocumentIntelligence:AiSearchEndpoint`, `DocumentIntelligence:AiSearchKey` (paired — see §3.3) | `AnalysisServicesModule.AddRagServices` |
| Storage admin | `SpeAdmin:KeyVaultUri` | `SpeAdminModule` |
| Multi-tenant | `ManagedIdentity:ClientId` | `ManagedIdentityCredentialFactory` |
| Resilience | `AiSearchResilience:*`, `GraphResilience:*` | Polly policy configuration |

---

## §7. When adding a new fixture

1. Read this document end-to-end first.
2. Inherit from `IntegrationTestFixture` if possible — it sets ~40 canonical keys correctly.
3. If you can't inherit (rare): copy the FULL config dictionary from `IntegrationTestFixture.cs` and adjust per your test.
4. Override SPECIFIC services in `ConfigureTestServices` (see §4.1 timing rule).
5. **Validate the fixture starts cleanly**: in a smoke test, instantiate the fixture and call `GetService<IServiceProvider>().GetRequiredService<TestServer>()`. If it throws, fix BEFORE adding test logic.

---

## §8. When updating production code

1. If you add a new `Configure<TOptions>` binding, document the required keys here.
2. If your code calls `Guid.TryParse` on a claim, document the claim format here.
3. If your code throws when a config key is missing, document the throw + the paired keys here.
4. If your service consumes another conditional service transitively, document the chain.

---

## §9. Diagnostic flowchart

When a previously-passing test starts failing with a cryptic Moq error / 500 / startup abort:

```
Did host startup abort with "Failure to infer one or more parameters"?
  YES → §4.1 service-override timing. Run `bff-extensions.md` § F.1 static-scan recipe.
  NO ↓

Did Moq verification fail with "expected once, but was 0 times"?
  YES → §1.1 (oid GUID-parseability) or §2.1 (CosmosPersistence:DatabaseName missing).
        Check what the production code does on the unhappy path. The
        endpoint may return 201 successfully while silently dropping
        the service interaction.
  NO ↓

Did the test return 500 NoServiceFound?
  YES → §3 (compound gate symmetry per ADR-032) or §4.1 (override timing).
  NO ↓

Did the test return 503 Feature Disabled with errorCode "ai.<X>.disabled"?
  YES → Expected behavior (ADR-018 + ADR-032). The test fixture set
        a kill-switch flag to off. Either accept the 503 in the
        assertion OR set the flag on.
  NO ↓

Other symptom? Open a ledger entry; cite this document § as
"fixture-config audited but no contract violation found".
```

---

*This document grows over time. Add a new § for every newly-discovered contract.*
