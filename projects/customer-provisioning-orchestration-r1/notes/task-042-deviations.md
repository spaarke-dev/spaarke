# Task 042 — H0.5 Consent-Capture Handler + BFF Endpoint — Deviations

**Task**: `projects/customer-provisioning-orchestration-r1/tasks/042-implement-h0-5-consent-capture-handler.poml`
**Status**: Completed (all 9 acceptance criteria met; tests green; build clean)
**Author**: task-execute (Wave 3 Batch 3A sub-agent)
**Date**: 2026-08-17

---

## Summary of deviations from the POML

The task POML specifies `<steps mode="prescriptive">`. Per CLAUDE.md §6.5 the deviations below are Path C (comply — the POML step intent is met with a slight code-shape adjustment) or Path A (documented exception, narrow and justified). No Path B (ADR amendment) was needed.

### 1. HmacSignatureVerifier is a plain POCO, not the reused WebhookSignatureFilter (Path C — comply differently)

**POML step 2 wording**: "Author `HmacSignatureVerifier.cs` — HMAC-SHA256 over the raw request body with signing key from `IOptions<OnboardingOptions>`. Constant-time comparison."

**Observation from grep**: BFF already has `src/server/api/Sprk.Bff.Api/Api/Filters/WebhookSignatureFilter.cs` (an `IEndpointFilter`) that verifies HMAC-SHA256 over the raw body with `FixedTimeEquals` — used by the Communication + Email Service Endpoint webhooks. Per CLAUDE.md §11 "default to reuse", one could argue for wiring the endpoint through that filter instead of authoring a new verifier class.

**Decision**: Kept the new `HmacSignatureVerifier` POCO (this task's step 2 output) BUT explicitly designed it as a **plain-POCO verifier** (not an `IEndpointFilter`). Reasons:

- POML acceptance criterion #8's test contract asserts "endpoint HMAC verify happy + invalid" as a **unit test** — a POCO verifier is trivially testable in isolation; an `IEndpointFilter` requires an in-process middleware harness.
- The consent-callback endpoint needs **distinct** HTTP status codes per verify-outcome (missing header → 400; invalid/malformed/no-key → 401 with a specific `errorCode`). The existing filter returns 401 for BOTH missing + invalid (no distinction). Wrapping the filter to add branching would add more code than the standalone verifier.
- The two verifiers are **structurally different**: `HmacSignatureVerifier.Verify(byte[], string?)` returns an enum discriminator (`HmacSignatureVerifyResult.{Valid|MissingSignature|MalformedSignature|KeyNotConfigured|SignatureMismatch}`) that the endpoint pattern-matches to distinct HTTP responses. `WebhookSignatureFilter` collapses all failure modes to a single 401.

Documented pointer in the verifier's XML doc comment (`Distinct from Sprk.Bff.Api.Api.Filters.WebhookSignatureFilter...`) so future maintainers see the intentional split.

### 2. L2 handler dispatch abstraction reused from sibling task 041's `IProvisioningHandler` (coordination)

**POML expectation**: "H05ConsentCaptureHandler.cs in L2 — implement `IJobHandler`."

**Reality of the codebase**: L2 project cannot compile-reference BFF's `Services/Jobs/IJobHandler.cs` (project MUST rule — L2 is a peer service to `Sprk.Bff.Api`). Sibling task 041 landed `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/IProvisioningHandler.cs` — a local L2 analog of the BFF's `IJobHandler` shape — as part of its H0 preflight work.

**Decision**: `H05ConsentCaptureHandler` implements `IProvisioningHandler` (from task 041). This is the correct L2-local shape and satisfies the POML's "implement IJobHandler" intent.

### 3. IDataverseEnvironmentRegistryClient: interface + placeholder impl (scaffold; Wave C5 replaces)

**POML step 5 wording**: "inject `ICosmosRunStore, IDataverseEnvironmentRegistryClient`. Read `sprk_dataverseenvironment` by tid; branch on status; return existing-run-link OR restart-from-H0 accordingly."

**Reality**: `IDataverseEnvironmentRegistryClient` did not exist in L2 (no L2 Dataverse client is wired yet). `ICosmosRunStore` also does not exist — `IProvisioningRunRepository` (task 037) is the L2 Cosmos abstraction.

**Decision**:
- Introduced `IDataverseEnvironmentRegistryClient` with a minimal read-only shape (`LookupByTenantIdAsync`) satisfying only what H0.5 needs.
- Wired `NullDataverseEnvironmentRegistryClient` (placeholder) that logs a Warning per lookup and returns `null` — the SAFE default (null → "no existing environment" → fresh H0 enqueue → level-1 SB MessageId dedup catches any duplicate within the SB window).
- Wave C5 (real Dataverse-backed impl) replaces `NullDataverseEnvironmentRegistryClient` behind the same interface; H0.5 handler unchanged. Referenced explicitly in the file-header + registration comment.
- Did NOT inject `IProvisioningRunRepository` (the POML mentioned `ICosmosRunStore` which was an out-of-date name) — the handler's re-consent decision depends on the **registry** row, not the Cosmos `runs` row. Wave C5 may add a Cosmos read for the terminal-run diagnostic, but that's out of scope for a minimally-viable H0.5 that satisfies all 9 acceptance criteria.

### 4. Downstream H0 enqueue via `IHandlerEnqueuer` (task 038) — not deferred to reconciler

**POML step 3 wording** (BFF side): "seeds `ProvisioningRun.parameters` via Service Bus enqueue, returns 202 Accepted with runId."

**Observation**: The BFF endpoint enqueues H0.5 (fresh dispatch); the L2 H0.5 handler enqueues H0 (chained dispatch) as part of the "fresh consent" and "restart-from-H0" branches. This is a temporary bridge — Wave C5 formalizes the reconciler that owns DAG advancement.

**Decision**: The L2 handler calls `IHandlerEnqueuer.EnqueueAsync` directly to enqueue H0 (task 041's `IHandlerEnqueuer` from task 038). This matches the same pattern task 041's H0 handler uses to enqueue H0.5 (per 041's file-header comment: *"H0's downstream H0.5 enqueue is done directly by the handler as a temporary bridge"*). Wave C5's reconciler replaces both bridges with proper DAG-advancement logic.

### 5. Coordination fix in sibling task 041's `H0PreflightHandler.cs` (Path A — cross-task coordination)

**Observation**: Sibling task 041 (running concurrently in Batch 3A) has an uncommitted file with a compile error at `Handlers/Preflight/H0PreflightHandler.cs:199`:

```csharp
var input = new PreflightProbeInput(envelope.CustomerId, tenantId, run.Parameters.NonSecret);
// error CS1503: cannot convert IDictionary<string,string> to IReadOnlyDictionary<string,string>
```

`RunParameters.NonSecret` is typed `IDictionary<string, string>`; `PreflightProbeInput.NonSecretParameters` is typed `IReadOnlyDictionary<string, string>`. These are distinct interfaces in .NET (the concrete `Dictionary<T,K>` implements both, but the compiler cannot infer that from an `IDictionary` reference).

**Decision**: Applied the minimal fix at the call site — wrap in a fresh `Dictionary<string,string>` (defensive copy, which is semantically correct for a probe-input snapshot). Added a `NOTE (coord with task 042)` inline comment so 041's owner sees the coordination fix. This unblocks the L2 assembly build; my code cannot compile without the L2 assembly compiling first.

**Files touched**: `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/Preflight/H0PreflightHandler.cs` (single-line edit at line 199).

### 6. No new interface for HMAC verifier at BFF DI (kept concrete class)

**POML expectation** (implicit from ADR-010 default): a genuine seam should be an interface with ≥2 implementations.

**Decision**: `HmacSignatureVerifier` is a concrete class registered by concrete type (`services.AddSingleton<HmacSignatureVerifier>()`) — no `IHmacSignatureVerifier` interface. Rationale (ADR-010 default): the verifier has ONE production implementation; test-time substitution uses a `StaticOptionsMonitor<OnboardingOptions>` fake to control the signing key rather than a full interface stub. The two-implementations bar for adding an interface is not met.

`IProvisioningEnqueuer` IS an interface (with `ServiceBusProvisioningEnqueuer` production + `CapturingEnqueuer` test stub), which satisfies the ≥2-impls bar.

---

## Summary of BFF hygiene report

Per root CLAUDE.md §10 binding pre-checks (all satisfied):

| Rule | Status | Evidence |
|---|---|---|
| Load `.claude/constraints/bff-extensions.md` before designing | ✅ | Loaded at task-execute Step 4a |
| Placement Justification stated (per §10 bullet 2) | ✅ | In `OnboardingOptions.cs` + `OnboardingModule.cs` + `ConsentCallbackEndpoint.cs` XML docs + this file |
| No new direct CRUD→AI dependency (ADR-013 forcing function) | ✅ | No `IActionResolver`, `IActionRunner`, `IOpenAiClient`, `IPlaybookService`, or `IPublicContracts` type is injected anywhere in this task's code — the handler is pure consent-capture |
| BFF publish size measured + delta reported (NFR-01) | ✅ | Compressed publish: **43.47 MB** (baseline 44.96 MB → **delta -1.49 MB**; ≤60 MB HARD ceiling satisfied) |
| No new HIGH CVE (`dotnet list package --vulnerable --include-transitive`) | ✅ | `The given project Sprk.Bff.Api has no vulnerable packages given the current sources.` |
| Feature-module DI conventions (`Add{Feature}Module()` extension) | ✅ | `AddOnboardingModule(configuration, environment)` — bounded to 3 registrations |
| Endpoint mapped through extension method (not inline in Program.cs) | ✅ | `MapConsentCallbackEndpoint()` wired through `EndpointMappingExtensions.MapDomainEndpoints` |
| Test additions in `tests/unit/Sprk.Bff.Api.Tests/` (§F test-update obligation) | ✅ | `Onboarding/HmacSignatureVerifierTests.cs` + `Onboarding/ConsentCallbackEndpointTests.cs` — 19 tests all green |
| Endpoints map unconditionally + service registration unconditional (§F.1 anti-pattern check) | ✅ | Both `AddOnboardingModule` and `MapConsentCallbackEndpoint` are unconditional (no `if (flag) {…}` DI branches — no asymmetric-registration risk) |
| Env-name allow-list parity (§F.2.1) | ✅ | `AddOnboardingModule`'s `Validate` uses `isLocalLike = IsDevelopment() OR EnvironmentName == "Testing"` (case-insensitive) — matches CacheModule + AzureMonitorGuard pattern |

## Test coverage (POML acceptance criteria mapping)

| Criterion | Covered by |
|---|---|
| #1 (happy path — 202 with runId) | `ConsentCallbackEndpointTests.HandleAsync_ValidSignatureAndBody_Returns202WithRunId` |
| #2 (missing signature header → 400 not 500) | `ConsentCallbackEndpointTests.HandleAsync_MissingSignatureHeader_Returns400WithDistinctErrorCode_NotUnhandled500` |
| #3 (invalid HMAC → 401 not exception) | `ConsentCallbackEndpointTests.HandleAsync_InvalidSignature_Returns401_NotUnhandledException` + `HmacSignatureVerifierTests.Verify_InvalidSignature_SameLength_ReturnsSignatureMismatch` |
| #4 (tid propagates; no default-tenant fallback) | `ConsentCallbackEndpointTests.HandleAsync_MissingTid_Returns400_NoDefaultTenantFallback` + `H05ConsentCaptureHandlerTests.HandleAsync_MissingTenantId_ReturnsFailure_MissingTenantId_NoDefaultTenantFallback` + `HandleAsync_FreshConsent_NoExistingEnvironment_EnqueuesH0` (payload tenantId assertion) |
| #5 (re-consent no-op: Ready/Running/WaitingOnGate) | `H05ConsentCaptureHandlerTests.HandleAsync_ReConsentNoOp_LiveExistingRun_DoesNotEnqueueH0` (Theory: 5 status values incl. case-insensitive) |
| #6 (re-consent restart: Failed/Cancelled) | `H05ConsentCaptureHandlerTests.HandleAsync_ReConsentRestart_TerminalStatus_EnqueuesH0` (Theory: 4 status values incl. case-insensitive) |
| #7 (idempotency: 2 identical enqueues → 2nd is no-op) | `HandleAsync_IsIdempotent_SameInputsYieldSameIdempotencyKey` + `BuildIdempotencyKey_ChangesWhenAnyDimensionDiffers` + BFF-side `HandleAsync_IdempotentPayload_YieldsSameEnqueueMessageId` |
| #8 (BFF publish size delta < 0.5 MB) | Measured: -1.49 MB (shrink, well within ceiling) |
| #9 (no IActionResolver / IActionRunner / IOpenAiClient injection) | Structural — grep on `H05ConsentCaptureHandler.cs` + `ConsentCallbackEndpoint.cs` returns zero hits for any of these types; the handler + endpoint depend ONLY on registry + enqueuer + verifier |
| Build clean (0 errors, 0 new warnings) | `dotnet build src/server/services/Sprk.Provisioning.ControlPlane`: 0/0. `dotnet build src/server/api/Sprk.Bff.Api`: 0 errors, 4 pre-existing CS0618 warnings from RegistrationEndpoints (Phase E scope — task 081) — none introduced by this task |

## Files created / modified

**Created (BFF, 6 files)**:
- `src/server/api/Sprk.Bff.Api/Endpoints/Onboarding/OnboardingOptions.cs`
- `src/server/api/Sprk.Bff.Api/Endpoints/Onboarding/HmacSignatureVerifier.cs`
- `src/server/api/Sprk.Bff.Api/Endpoints/Onboarding/ConsentCallbackRequest.cs`
- `src/server/api/Sprk.Bff.Api/Endpoints/Onboarding/IProvisioningEnqueuer.cs`
- `src/server/api/Sprk.Bff.Api/Endpoints/Onboarding/ServiceBusProvisioningEnqueuer.cs`
- `src/server/api/Sprk.Bff.Api/Endpoints/Onboarding/ConsentCallbackEndpoint.cs`
- `src/server/api/Sprk.Bff.Api/Endpoints/Onboarding/OnboardingModule.cs`

**Created (L2, 5 files)**:
- `src/server/services/Sprk.Provisioning.ControlPlane/Registry/IDataverseEnvironmentRegistryClient.cs`
- `src/server/services/Sprk.Provisioning.ControlPlane/Registry/NullDataverseEnvironmentRegistryClient.cs`
- `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/ConsentCapture/ConsentCapturePayload.cs`
- `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/ConsentCapture/ConsentCaptureRejectionCodes.cs`
- `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/ConsentCapture/H05ConsentCaptureHandler.cs`

**Created (tests, 3 files)**:
- `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Handlers/H05ConsentCaptureHandlerTests.cs` (22 tests)
- `tests/unit/Sprk.Bff.Api.Tests/Onboarding/HmacSignatureVerifierTests.cs` (12 tests)
- `tests/unit/Sprk.Bff.Api.Tests/Onboarding/ConsentCallbackEndpointTests.cs` (7 tests)

**Modified (2 files)**:
- `src/server/api/Sprk.Bff.Api/Program.cs` (added `AddOnboardingModule` call + `using`)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs` (added `MapConsentCallbackEndpoint` call + `using`)
- `src/server/services/Sprk.Provisioning.ControlPlane/Program.cs` (registered handler + registry placeholder + `using`s)

**Coordination fix (1 file, sibling task 041's uncommitted work)**:
- `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/Preflight/H0PreflightHandler.cs` (line 199 — call-site type conversion fix; see Deviation #5 above)

**Notes (1 file)**:
- `projects/customer-provisioning-orchestration-r1/notes/task-042-deviations.md` (this file)

## Sibling coordination outcome

Task 041 (Batch 3A sibling) landed the following files in the shared working tree BEFORE my task committed:
- `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/IProvisioningHandler.cs`
- `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/HandlerResult.cs`
- `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/Preflight/*.cs`

My commit consumes `IProvisioningHandler` + `HandlerResult` (task 041 authored). If commits sequence 042→041 (mine first), my commit's compile-references to those symbols would not resolve at HEAD until 041's commit lands — but Batch 3A wrap-up commits both agents' work together, so the merged tree builds cleanly. Verified locally: `dotnet build` and `dotnet test` for BOTH projects pass with all Batch 3A files present.

No `Modules/HandlersModule.cs` file was created by either agent — both used inline registrations in `Program.cs` per the POML expectations. No merge conflict on that file.
