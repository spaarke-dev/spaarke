# H2 — Dev-boot DI validation on net10 (ValidateOnBuild/ValidateScopes)

> **Task**: 020 (P2) · **Spec**: FR-08 · **Design**: §5 H2 · **ADR**: ADR-032
> **Author**: task-execute (Opus 4.8, FULL rigor, xhigh) — 2026-08-12
> **Adversarial verification**: task 021 (non-author) — NFR-07
> **Status**: **FIXES COMPLETE — probe reads `DI-VALIDATION-RESULT: CLEAN`; BFF Release build 0 errors.** 10 root singletons fixed (7 analysed + R8/R9/R10 surfaced during probe iteration 45→19→2→0). See §4 as-built.

---

## 1. The .NET 9/10 change + how it was executed here

Since .NET 6, `WebApplicationBuilder` defaults `ValidateOnBuild` + `ValidateScopes` to **on in the Development environment** (off in Production/Testing). `Program.cs` uses plain `WebApplication.CreateBuilder(args)` + `builder.Build()` (line 223) with **no** `UseDefaultServiceProvider` override, so it inherits that default. Net effect: **on net10, booting the BFF in Development eagerly validates the whole DI graph and crashes on any captive dependency (singleton→scoped) or unresolvable ctor dep** — Production is unaffected. This is a quality forcing-function (design §4 principle 5), not a workaround target.

### Why a normal local boot can't run this (and how we did)

`ValidateOnBuild` eagerly instantiates every service. Several BFF services connect to infra in their constructors (`DataverseServiceClientImpl` connects eagerly — see task 010 / `TodoGenerationService` comment), and there is **no `appsettings.Development.json`** — so a raw `dotnet run` in Development without spaarke-dev config + reachable infra fails on **infrastructure**, not on the DI defects H2 targets. The codebase's own test fixtures confirm this: `tests/unit/Sprk.Bff.Api.Tests/CustomWebAppFactory.cs:148-155` **disables** both validations with the comment *"the production codebase has known singleton→scoped DI lifetime issues"*, and `MetricsDistributedCacheRegistrationTests.cs` does the same.

**Execution method (infra-independent, in-process):** a temporary diagnostic probe (`tests/unit/Sprk.Bff.Api.Tests/DiGraphValidationProbe.cs`) subclasses `CustomWebAppFactory` — reusing its full fake config + **mocked `IDataverseService`** + **fake `IGraphClientFactory`** + `RemoveAll<IHostedService>()` — and **re-enables** `ValidateOnBuild=true` + `ValidateScopes=true`, then forces the host build and captures the aggregate. Result: **45 validation errors, 100% pure `ValidateScopes` captive-dependency violations, zero infra/IO exceptions** (the mocks neutralized the eager-connecting services cleanly). The probe is a throwaway diagnostic; see §5 for its disposition.

---

## 2. Result: 45 errors, all captive-dependency (singleton → scoped)

Every one of the 45 is the same shape: `Cannot consume scoped service 'S' from singleton 'G'`. There are **no** unresolvable-service errors and **no** infra errors. The 45 are transitive cascades of **7 distinct root singleton registrations** that inject a scoped service. Fixing the 7 roots clears all 45.

## 3. The 7 root defects (closed set)

| # | Root singleton (registration) | Scoped dependency it captures | Registered at | Cascade count |
|---|---|---|---|---|
| R1 | `IOfficeJobHandler → UploadFinalizationWorker` (Singleton) | `SpeFileStore` | `Workers/Office/OfficeWorkersModule.cs:35` | 2 |
| R2 | `IOfficeJobHandler → ProfileSummaryWorker` (Singleton) | `ISpeFileOperations` | `Workers/Office/OfficeWorkersModule.cs:36` | 2 |
| R3 | `ICommunicationEnrichmentService → CommunicationEnrichmentService` (Singleton) | `ISpeFileOperations` **and** `IPostUploadIndexingEnqueuer` | `Infrastructure/DI/CommunicationModule.cs:286` | ~24 (largest fan-out) |
| R4 | `IncomingCommunicationProcessor` (Singleton) | `SpeFileStore` | `Infrastructure/DI/CommunicationModule.cs:272` | 2 |
| R5 | `IActionResolver → ActionResolver` (Singleton) | `IConsumerRoutingService` | `Services/Ai/LinearConsumers/LinearConsumersModule.cs:28` | ~7 |
| R6 | `INodeExecutor → AiAnalysisNodeExecutor` (Singleton) + `INodeExecutorRegistry → NodeExecutorRegistry` (Singleton) | `IRecordSearchService` | `Infrastructure/DI/AnalysisServicesModule.cs:1359` + `:1191` | ~10 |
| R7 | `INodeExecutor → LiveFactNode` (Singleton) | `IReadOnlyDictionary<string, ILiveFactResolver>` | (AnalysisServicesModule — keyed live-fact resolvers) | 1 |

> Note the `NullActionResolver` (AnalysisServicesModule.cs:563) is the ADR-032 compound-OFF null-object peer of R5's `ActionResolver`; its lifetime must be changed symmetrically with R5 (ADR-032 symmetry constraint).

### The full 45 (raw probe output)
Captured at `c:\tmp\h2-di-probe.txt` (probe run 2026-08-12). Every line maps to one of R1–R7 above via its `from singleton '…'` clause.

---

## 4. Fixes applied (per root — behavior-preserving, NFR-01) — AS BUILT

**Result: `DI-VALIDATION-RESULT: CLEAN`** (probe run after fixes, ValidateOnBuild + ValidateScopes ON). BFF `dotnet build -c Release` = **0 errors**. Two fix families used:

- **Family A — scope-per-unit-of-work** (inject/reuse `IServiceScopeFactory`; resolve the scoped service inside a per-operation scope at the use-site; scope lifetime spans any downloaded-stream consumption). Used for every singleton host that must stay singleton.
- **Family B — demote the singleton to scoped** (only when the service holds no process-wide state AND is consumed exclusively by scoped/transient/endpoint-param consumers, never by a singleton).

### The 7 originally-analysed roots

| # | Root | Scoped dep(s) captured | Fix | Files |
|---|---|---|---|---|
| R1 | `UploadFinalizationWorker` (Singleton `IOfficeJobHandler`) | `SpeFileStore`, `IPostUploadIndexingEnqueuer` | **A** — reused existing `_scopeFactory`; resolve at use-sites (`UploadToSpeAsync`, `ProcessEmailAttachmentsAsync`, `ProcessSingleAttachmentAsync`, `EnqueueRagIndexingAsync`) | `Workers/Office/UploadFinalizationWorker.cs` |
| R2 | `ProfileSummaryWorker` (Singleton `IOfficeJobHandler`) | `IAppOnlyAnalysisService` (flagged transitively via `ISpeFileOperations`) | **A** — injected `IServiceScopeFactory`; resolve `IAppOnlyAnalysisService` per-message in `ProcessAsync` | `Workers/Office/ProfileSummaryWorker.cs` |
| R3 | `CommunicationEnrichmentService` (Singleton) | `IPostUploadIndexingEnqueuer` + `ICommunicationTriageAi` + `ICommunicationProposeAi` + `ICommunicationCreateTaskAi` (transitively pulled `ISpeFileOperations`) | **A** — injected `IServiceScopeFactory`; resolve each of the 4 at its single use-site (consumed by singletons → demote ruled out) | `Services/Communication/CommunicationEnrichmentService.cs` |
| R4 | `IncomingCommunicationProcessor` (Singleton) | `SpeFileStore`, `IPostUploadIndexingEnqueuer` | **A** — injected `IServiceScopeFactory`; resolve at use-sites (`ProcessIncomingAttachmentsAsync`, `ArchiveEmlAsync`, `EnqueueRagIndexingAsync`) | `Services/Communication/IncomingCommunicationProcessor.cs` |
| R5 | `ActionResolver` (Singleton `IActionResolver`) | `IConsumerRoutingService` | **B** — `AddSingleton`→`AddScoped` (all consumers are Scoped services or per-request endpoint params; no singleton consumer; stateless). **ADR-032 symmetry**: `NullActionResolver` peer demoted symmetrically. | `Services/Ai/LinearConsumers/LinearConsumersModule.cs`, `Infrastructure/DI/AnalysisServicesModule.cs` |
| R6 | `AiAnalysisNodeExecutor` (Singleton `INodeExecutor`) | `IRecordSearchService` | **A** — removed ctor field; resolve from the executor's **existing per-execution scope** (passed into `RetrieveEntityContextAsync`). `NodeExecutorRegistry` stays Singleton (genuine process-wide state); its error cleared transitively. | `Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` |
| R7 | `LiveFactNode` (Singleton `INodeExecutor`) | `IReadOnlyDictionary<string, ILiveFactResolver>` | **A** — injected `IServiceScopeFactory`; resolve the dispatch map per-execution in `ExecuteAsync` (scope spans `resolver.ResolveAsync`) | `Services/Ai/Nodes/LiveFactNode.cs` |

### Additional roots that surfaced during probe iteration (masked by the above until fixed)

The validator reports one captured-scoped dep per descriptor, so fixing the first-flagged dep on a root revealed deeper captures (POML anticipated this). Iteration: 45 → 19 → 2 → **0**.

| # | Root | Scoped dep(s) | Why it was masked | Fix |
|---|---|---|---|---|
| R8 | `UploadFinalizationWorker` | `IEmailToEmlConverter`, `AttachmentFilterService` | Both have a **Scoped registration (`EmailServicesModule`) that wins** over the Singleton one (`OfficeWorkersModule`); previously masked by the R1 `SpeFileStore` capture flagged first | **A** — resolve both from the same per-operation scope in `ProcessEmailAttachmentsAsync` |
| R9 | `CommunicationService` (Singleton) | `SpeFileStore` (direct ctor field) | Masked by R3: validator hit `ISpeFileOperations` via the enrichment chain first; once R3 was fixed the direct `SpeFileStore` capture surfaced (largest fan-out, ~14 errors) | **A** — reused existing `_scopeFactory`; method-scoped resolution in `ArchiveToSpeAsync`, `FetchEmlAttachmentsForEmbedAsync`, `DownloadAndBuildAttachmentsAsync` (scope spans stream consumption) |
| R10 | `IncomingCommunicationProcessor` (Singleton) | `IEmailAttachmentProcessor` (Scoped) | Masked by the R4 direct `SpeFileStore` capture; `EmailAttachmentProcessor` is Scoped (injects `SpeFileStore`) and was captured by the singleton | **A** — resolve `IEmailAttachmentProcessor` from the same per-message scope in `ProcessIncomingAttachmentsAsync` |

### Test updates (consequence of ctor signature changes — same probe project compiles)

Removing captured ctor params changed several public ctors. Updated manual constructions to the new signatures, routing formerly-injected scoped doubles through a mock `IServiceScopeFactory` where the test exercises that path (no behavior change): `AiAnalysisNodeExecutorTests`, `PlaybookExecutionTests`, `LiveFactNodeTests`, `InsightsNodesIntegrationTests`, `InboundPipelineTests`, `CommunicationIntegrationTests`, the 7 `seam/Communication/*SeamTests` (new `EnrichmentScopeFactoryStub`), and 10 `Services/Communication` `CommunicationService` test files (new `SpeScopeFactoryStub` for the archival/embed suites; `null!` SpeFileStore args dropped elsewhere).

**Verification loop**: iterated `dotnet test --filter DiGraphValidationProbe` after each wave until `DI-VALIDATION-RESULT: CLEAN`. Production boot path unaffected — all fixes are environment-agnostic lifetime/anchoring corrections; no `IsDevelopment` branch, no validation weakened (FR-08 honored).

**Escalation check (§6.5)**: none required — every root was fixable as a pure lifetime/anchoring correction with no runtime behavior change.

---

## 5. Probe disposition — RESOLVED: promoted to a permanent CI guard

The throwaway diagnostic `DiGraphValidationProbe.cs` (wrote findings to `c:\tmp`, non-asserting) has been **promoted** to a permanent, CI-safe, **asserting** test: `tests/unit/Sprk.Bff.Api.Tests/DiGraphValidationTests.cs`.

- It boots the full BFF DI graph in Development with `ValidateOnBuild=true` + `ValidateScopes=true` (reusing `CustomWebAppFactory`'s network-free mocks) and **`Assert.Fail`s with the captive-dep list** if the graph regresses. No file I/O, deterministic, network-free — same pattern as the existing `MetricsDistributedCacheRegistrationTests`.
- **Why keep it**: CI never boots the app in Development, so without this guard the captive-dependency defects would regress silently — which is exactly how 45 had accumulated. This is the design §4 principle-5 forcing-function.
- **Shape note (tests/CLAUDE.md)**: this is a whole-graph architectural/startup-contract guard, distinct from the banned B3 per-service `GetRequiredService` assertion. Flagged for **task 021 (adversarial verify) / 030 (test suite)** to confirm placement/shape; trivially removable (1 file) if they rule it out.
- Verified after promotion: the guard test **passes** (graph is CLEAN) and the BFF Release build is green.
