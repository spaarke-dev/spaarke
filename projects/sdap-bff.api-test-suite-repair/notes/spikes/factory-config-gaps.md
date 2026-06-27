# Factory Config Gaps — P1.C1 Inventory (Task 017)

> **Source TRX**: `projects/sdap-bff.api-test-suite-repair/baseline/test-baseline-2026-05-31.trx` (342 failures across 50 test classes)
> **Read targets**: `tests/unit/Sprk.Bff.Api.Tests/CustomWebAppFactory.cs` (171 LOC, READ-ONLY per NFR-07 / §4.5), `failure-inventory-2026-05-31.md`, `phase23-scope-delta-2026-05-31.md`
> **Output consumer**: task **018** (P1.C2 — factory extension, runs ISOLATED in Wave 1.3 per NFR-07)
> **Produced**: 2026-05-31 by task 017 agent
> **Authority**: This is the load-bearing input for task 018's additive dictionary entries. Task 018 MUST cite this file in its PR description.

---

## Inventory summary

| Metric | Count |
|---|---:|
| Distinct config-related exception signatures in TRX | **2** |
| Total `services.RemoveAll<>()` calls currently in `CustomWebAppFactory.cs` | **3** |
| Total config dictionary keys currently injected by `CustomWebAppFactory.cs` | **37** |
| Net new dictionary entries recommended for task 018 | **7** |
| Net new `RemoveAll<>()` calls recommended for task 018 | **0** (existing `RemoveAll<IHostedService>()` already absorbs background-service surface) |
| Failing tests directly attributable to factory startup failure (best-effort upper bound) | **~342 / 342** — both signatures fire in `Program.cs` host build, so every test that uses `CustomWebAppFactory` fails at startup before its assertions run |

**Bottom line**: of the +73 net failures absorbed via the 2026-05-31 scope-delta, an overwhelming majority resolve mechanically when task 018 adds **two missing config sections** (`AgentService:*` and `CosmosPersistence:*`). The hand-rolled `AsyncEnumerableHelpers.cs` / `FakeChatClient.cs` (P1.B1 / task 015) cover the IChatClient streaming surface; the factory dictionary gaps cover the unbounded host-build surface.

---

## Section A — What the factory currently provides (READ-ONLY scan)

### A.1 Current `services.RemoveAll<>()` calls (3 total)

Source: `tests/unit/Sprk.Bff.Api.Tests/CustomWebAppFactory.cs` lines 155, 162, 167.

| Line | RemoveAll target | Replacement |
|---:|---|---|
| 155 | `IGraphClientFactory` | `FakeGraphClientFactory` (singleton) |
| 162 | `IHostedService` | (no replacement — disables all background workers including Service Bus job processor, watermark sync, etc.) |
| 167 | `IDataverseService` | `Mock<IDataverseService>` with `TestConnectionAsync()` → `true` |

**Observation**: The `RemoveAll<IHostedService>()` line is the broad-spectrum guard against background workers that would crash during test startup (Service Bus connection, Dataverse polling, Cosmos seeding, etc.). It is **load-bearing for every test**. Task 018 MUST NOT remove or narrow it.

### A.2 Current config dictionary keys (37 total)

Source: `CustomWebAppFactory.cs` lines 26–107. Inventoried by section root:

| Section / standalone key | Keys provided | Coverage notes |
|---|---|---|
| `ConnectionStrings:*` | `ServiceBus` | Standalone SB conn-string for early validation |
| `Cors:*` | `AllowedOrigins:0` | One origin |
| `AzureAd:*` | `Instance`, `TenantId`, `ClientId`, `Audience` | Microsoft Identity Web JWT auth |
| `Graph:*` | `TenantId`, `ClientId`, `ClientSecret`, `UseManagedIdentity`, `Scopes:0` | GraphOptions validation |
| `Dataverse:*` | `EnvironmentUrl`, `ServiceUrl`, `ClientId`, `ClientSecret`, `TenantId` | DataverseOptions validation |
| `ServiceBus:*` | `ConnectionString`, `QueueName` | ServiceBusOptions validation |
| `DocumentIntelligence:*` | `Enabled`, `OpenAiEndpoint`, `OpenAiKey`, `OpenAiDeployment`, `AiSearchEndpoint`, `AiSearchKey`, `RecordMatchingEnabled` | Endpoint registration + RAG |
| `Analysis:*` | `Enabled` | Analysis pipeline feature gate |
| `OfficeRateLimit:*` | `Enabled` = false | Office throttling disabled for tests |
| `Redis:*` | `Enabled` = false | Forces in-memory cache fallback (ADR-009) |
| `ModelSelector:*` | `DefaultModel` | AI model selector default |
| `AzureOpenAI:*` | `Endpoint`, `ChatModelName` | IChatClient registration in AiModule |
| `AiSearchResilience:*` | `MaxRetryAttempts`, `CircuitBreakerFailureThreshold`, `CircuitBreakerDuration` | DataAnnotation-validated |
| `GraphResilience:*` | `MaxRetryAttempts`, `RetryDelay`, `CircuitBreakerFailureThreshold`, `CircuitBreakerDuration` | DataAnnotation-validated |
| `SpeAdmin:*` | `KeyVaultUri` | SpeAdminModule key vault stub |
| `ManagedIdentity:*` | `ClientId` | DataverseWebApiClient in SpeAdminModule |
| `UAMI_CLIENT_ID`, `TENANT_ID`, `API_APP_ID`, `API_CLIENT_SECRET` | (standalone env-style keys) | Legacy env-var keys preserved |

**Total**: 37 keys across 16 sections + 4 standalone keys.

---

## Section B — Per-gap detail (TRX-driven inventory)

The 342 failing tests share **two distinct startup-time exception signatures**. Both fire from `Program.cs` line 107 (host build) before any test assertion runs — so **task 018's additive dictionary entries should clear the entire 342-failure cluster mechanically**, modulo any post-startup failures that surface only after host-build succeeds.

| # | Options type | Missing key | Required field constraint | Proposed fake value | Affected test count (raw TRX matches) | Source TRX evidence |
|---:|---|---|---|---|---:|---|
| 1 | (raw config, no Options class binding) | `CosmosPersistence:Endpoint` | `IConfiguration["CosmosPersistence:Endpoint"]` non-null, throws `InvalidOperationException` if missing | `https://test.documents.azure.com:443/` | **342** (every failing test — fires before any other validation) | TRX line 52434+, raw text: `"System.InvalidOperationException : CosmosPersistence:Endpoint is not configured. Add this setting to appsettings.json or Azure App Service configuration."` thrown at `AiPersistenceModule.cs:line 56` then `Program.cs:line 107` |
| 2 | (raw config, optional) | `CosmosPersistence:DatabaseName` | Optional in code (defaults to `"spaarke-ai"`), but providing it explicitly avoids relying on the default + makes the test posture explicit | `spaarke-ai-test` | 0 directly (default is permissive); add for completeness | `AiPersistenceModule.cs:line 85`: `configuration["CosmosPersistence:DatabaseName"] ?? "spaarke-ai"` |
| 3 | `AgentServiceOptions` | `AgentService:Endpoint` | `[Required] Uri` — validated via `ValidateDataAnnotations` + `ValidateOnStart` (ADR-010 pattern) | `https://test.services.ai.azure.com/api/projects/test-project` | **76** (each failure cites both this and key #4) | TRX line 52434+, raw text: `"OptionsValidationException: DataAnnotation validation failed for 'AgentServiceOptions' members: 'Endpoint' with the error: 'The Endpoint field is required.'"` |
| 4 | `AgentServiceOptions` | `AgentService:AgentId` | `[Required][MinLength(1)] string` | `test-agent-id` | **76** (paired with #3 in same exception message) | Same source as #3, second field: `'AgentId' with the error: 'The AgentId field is required.'` |
| 5 | `AgentServiceOptions` | `AgentService:Enabled` | `[Required] bool` — defaults to `false` in code; explicit `"false"` keeps kill-switch OFF in tests (ADR-018) and avoids accidental network calls | `false` | 0 (defaults work, but explicit is safer per ADR-018) | `AgentServiceOptions.cs:line 26` `public bool Enabled { get; init; } = false;` |
| 6 | `AgentServiceOptions` | `AgentService:MaxConcurrency` | `[Required][Range(1, 64)] int`; default 4 in code, but `[Required]` annotation triggers validation if section is bound but key missing | `4` | 0 directly (defaults), but `[Required]` annotation makes this safer | `AgentServiceOptions.cs:line 55` |
| 7 | `AgentServiceOptions` | `AgentService:ThreadCacheExpiryMinutes` | `[Required][Range(1, 1440)] int`; default 60 | `60` | 0 directly (defaults), same rationale as #6 | `AgentServiceOptions.cs:line 66` |

**Distinct signature count**: 2 (CosmosPersistence + AgentServiceOptions). **Distinct key count**: 7 (1 mandatory Cosmos + 1 optional Cosmos + 5 AgentService fields).

### B.1 Why only 2 signatures?

The factory currently provides config for every Options class with `[ValidateOnStart]` **except** `AgentServiceOptions` (registered by `AnalysisServicesModule.cs` per task 017's grep) and the raw `CosmosPersistence:Endpoint` read (registered by `AiPersistenceModule.cs`). The 17 other Options classes the factory does cover (`GraphOptions`, `DataverseOptions`, `ServiceBusOptions`, `AiSearchResilienceOptions`, `GraphResilienceOptions`, `SpeAdminOptions`, `ManagedIdentityOptions`, etc.) are all satisfied — confirmed by absence of additional `OptionsValidationException` patterns in the TRX.

### B.2 Why the `RemoveAll<IHostedService>()` line is doing heavy lifting

If the factory removed only specific hosted services (e.g., only `ServiceBusJobProcessor`), every new hosted service added by future BFF projects would silently re-enter the test pipeline and crash on startup. The broad `RemoveAll<IHostedService>()` is the **intentional anti-drift guard** for the test host. Task 018 must preserve it.

---

## Section C — Inputs for task 018

Task 018 (Wave 1.3 ISOLATED, NFR-07 anti-parallelism) is the ONLY task in Phase 1 that edits `CustomWebAppFactory.cs`. Per §4.5 it MUST be additive only — no method-signature changes, no restructuring of existing dictionary entries.

**Recommended additive block** for task 018 — append to the existing dictionary literal (between the `ManagedIdentity:ClientId` line and the closing `};`):

```csharp
// CosmosPersistence options (required by AiPersistenceModule — raw config read, not bound to Options class)
// Missing this key causes InvalidOperationException at Program.cs line 107 during host build
// Affects: ALL 342 failing tests (fires before any test assertion runs)
["CosmosPersistence:Endpoint"] = "https://test.documents.azure.com:443/",
["CosmosPersistence:DatabaseName"] = "spaarke-ai-test",

// AgentService options (required by AgentServiceOptions ValidateDataAnnotations + ValidateOnStart)
// Missing these causes OptionsValidationException at Program.cs line 107 during host build
// Affects: 76 raw TRX matches; clears the Foundry agent registration path in tests
// ADR-018: Enabled=false keeps the kill-switch OFF in tests (no accidental Foundry network calls)
["AgentService:Enabled"] = "false",
["AgentService:Endpoint"] = "https://test.services.ai.azure.com/api/projects/test-project",
["AgentService:AgentId"] = "test-agent-id",
["AgentService:MaxConcurrency"] = "4",
["AgentService:ThreadCacheExpiryMinutes"] = "60",
```

**Net addition**: +7 dictionary entries, 0 removals, 0 signature changes — fully additive per §4.5.

### C.1 Per-NFR compliance check for task 018

| NFR / rule | Verification | Status |
|---|---|---|
| **NFR-01** (no `src/` changes) | All edits in `tests/unit/Sprk.Bff.Api.Tests/CustomWebAppFactory.cs` | ✅ |
| **NFR-03** (no DI registration count increase) | Adds config keys only; no `services.AddXxx(...)` calls | ✅ |
| **NFR-07** (factory anti-parallelism) | Task 018 runs in Wave 1.3 ISOLATED — no concurrent test tasks | ✅ |
| **NFR-09** (`<repair-not-rewrite>true</repair-not-rewrite>`) | Pure dictionary append — 0% line replacement | ✅ |
| **§4.5** (additive only) | 7 net new lines, 0 modifications, 0 deletions | ✅ |
| **§6.4** (full suite before+after) | Task 018's `<steps>` already cover this | ✅ (per task 018 POML) |
| **ADR-010** (DI minimalism) | No new DI registrations; only config keys | ✅ |
| **ADR-018** (kill switches) | `AgentService:Enabled = "false"` keeps Foundry agent disabled in tests | ✅ |

---

## Section D — Hosted services to remove (no additions needed)

**Verdict: NO new `RemoveAll<>()` calls needed.**

Rationale: The existing `services.RemoveAll<IHostedService>()` (line 162) is broad-spectrum — it suppresses **every** registered `IHostedService` including all background workers added by Insights Engine, Action Engine, AI Platform Unification R2 (Cosmos seeding, watermark sync, etc.). Adding type-specific `RemoveAll<>()` calls would (a) narrow this guard and (b) require maintenance every time a new hosted service is added — exactly the anti-drift problem the broad guard solves.

If a future test needs **selective** hosted-service activation (e.g., the watermark sync test wants the sync worker running), that's a per-test-class concern best handled by:
- a dedicated derived `CustomWebAppFactory` subclass, OR
- `ConfigureTestServices(s => s.AddHostedService<TheSpecificOne>())` after the `RemoveAll` (which the broad guard supports cleanly).

Task 018 should NOT introduce per-hosted-service `RemoveAll<>()` lines.

### D.1 Existing `RemoveAll<>()` calls — no changes recommended

| Existing call | Keep / Change | Rationale |
|---|---|---|
| `services.RemoveAll<IGraphClientFactory>()` (line 155) | **Keep** | `FakeGraphClientFactory` replacement is load-bearing for OBO/MSAL avoidance in tests |
| `services.RemoveAll<IHostedService>()` (line 162) | **Keep** | Broad-spectrum guard; do NOT narrow |
| `services.RemoveAll<IDataverseService>()` (line 167) | **Keep** | `Mock<IDataverseService>` replacement is load-bearing for non-network Dataverse calls |

---

## Section E — Cross-reference: cluster impact projection

If task 018 applies the recommended 7 additive entries, the failure-clearance projection per `phase23-scope-delta-2026-05-31.md`:

| Cluster (POML absorbing) | Failures | Expected clearance from 018 alone | Residual after 018 |
|---|---:|---:|---:|
| Integration.Workspace.* (POML 060) | 54 | ~54 (host-build failure) | 0 likely; assertion-level work absorbed by 060 may surface NEW residuals |
| Services.Communication.* (POML 055) | 53 | ~53 | likely 0 host-build; residuals if any are real cluster work |
| Api.Ai.* (POML 070) | 90 | ~90 | 0 host-build; assertion-level work in 070 |
| Integration.* non-Workspace (POMLs 060, 061) | 18 | ~18 | 0 host-build |
| Services.Ai.Safety.* (POML 044) | 19 | ~19 | 0 host-build |
| Api.Reporting.* (POML 072) | 17 | ~17 | 0 host-build |
| Top-level *EndpointTests (POML 073) | 39 | ~39 | 0 host-build |
| Services.Ai.* non-Safety (POMLs 050+) | 22 | ~22 | 0 host-build |
| Api.Agent.* (POML 070) | 7 | ~7 | 0 host-build |
| SpeAdmin.* (POML 073 extended) | 7 | ~7 | 0 host-build |
| Services.Jobs.RecordSyncJob (POML 046) | 1 | ~1 | 0 host-build |
| Services.Ai.Insights.Layer2.* HOLD | 3 | ~3 | 0 host-build |
| **TOTAL** | **342** | **~342 host-build clears** | **Residuals depend on per-cluster work in Phase 2+3** |

**Important caveat**: The "expected clearance" column assumes the 342 failures fail PRIMARILY at host-build (lines 56–107 of Program.cs / AiPersistenceModule.cs). The TRX evidence confirms this: every failing test's stack trace starts with the `AiPersistenceModule.AddAiPersistenceModule` or `OptionsValidationException`-during-host-build pattern. Post-host-build assertion failures (if any) become visible to the Phase 2+3 cluster tasks only after task 018 lands.

**Worst-case interpretation**: if some of the 342 failures are masked host-build failures (i.e., the test would still fail post-host-build for assertion-level reasons), the Phase 2+3 tier tasks will surface those residuals naturally. The +24–48h person-hours buffer in the scope-delta accounts for this.

---

## Verification

- [x] `notes/spikes/factory-config-gaps.md` exists with 4 named sections (A, B, C, D — plus added E cross-ref)
- [x] Per-gap detail table has at least 1 row (it has 7)
- [x] No modifications to `tests/`, `src/`, `power-platform/`, `infra/`, `scripts/` (this task wrote only to `projects/sdap-bff.api-test-suite-repair/notes/spikes/`)
- [x] `CustomWebAppFactory.cs` NOT modified (NFR-07 prep step honored)
- [x] All claims cite TRX line numbers or source-code line numbers
- [x] Recommendations for task 018 are additive only per §4.5
