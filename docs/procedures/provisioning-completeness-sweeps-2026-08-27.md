# Provisioning completeness sweeps — 2026-08-27

> **Status**: BINDING operator runbook + follow-up backlog. Consolidates the
> Wave 7 completeness-sweep findings (COMP-01, COMP-03, COMP-05, COMP-06,
> COMP-07, COMP-10, COMP-11, COMP-13) from
> `projects/customer-provisioning-orchestration-r1/notes/pre-dispatch-audit-punchlist-2026-08-27.md`.
>
> **Origin**: the pre-dispatch adversarial completeness audit surfaced 12
> dimensions no single audit lens owned (auth-flow, network egress, RBAC
> propagation, cost gating, envelope size, load, structured logs, crash
> recovery, rollback taxonomy, profile-registry, cross-worktree, observability).
> Wave 7 landed the code-side fixes that are self-contained
> (COMP-02 + COMP-09 in prereqs + enqueuer); this document is the durable
> runbook for the remaining dimensions that are documentation-plus-followup.
>
> **Companion prereqs entries**: PRQ-S-15 (egress probe, COMP-02), PRQ-S-16
> (SB tier / DLQ, COMP-09) — see `scripts/provisioning-prereqs/prereqs.yaml`.
>
> **Companion code additions**: `ServiceBusHandlerEnqueuer.EnsureBodyWithinCap`
> (COMP-09), `ServiceBusModuleOptions.MaxEnvelopeBodyBytes` (COMP-09).

---

## 1. COMP-01 — Auth-flow matrix (per-handler identity + audience + RBAC + expiry)

**Why this exists.** Every handler that mints a customer-tenant token to
call Dataverse, Graph, ARM, or the customer's KV does so through a
different combination of (identity, audience, RBAC grant, expiry-refresh
strategy, failure-classification path). No single audit owned the full
matrix — auth is a dynamic runtime concern that crosses every static
artifact. This section is the deliverable: a table an operator can scan
before a first-run for a new customer to know what tokens will fire and
where they will fail.

### Auth-flow matrix

| Handler | Identity used | Target audience | Required RBAC on target | Expiry-refresh | Known failure-classification path |
|---|---|---|---|---|---|
| **H0 preflight** | L2 UAMI (`DefaultAzureCredential`) | `management.azure.com` | Reader on customer sub (Lighthouse-delegated) | Azure SDK auto-refresh (1h JWT) | `RequestFailedException 401` → `HttpRequestException` → `FailureClass.Resumable` (safe default) |
| **H0.5 consent** | Operator OBO (BFF consent-callback) | `graph.microsoft.com` scoped `Application.ReadWrite.All` | Global admin on customer tenant | Interactive re-consent | 403 → OperatorGate — `Resumable` |
| **H1 sub-readiness** | L2 UAMI | `management.azure.com` | Reader on customer sub | SDK auto-refresh | 403 → `Resumable`; 5xx → `Resumable` |
| **H2a Bicep deploy** | L2 UAMI | `management.azure.com` (customer sub) | **Contributor** on customer sub (Lighthouse) | SDK auto-refresh | 403 → `Resumable` (RBAC propagation window, see §4); 5xx → `Resumable` |
| **H2b AI Search index** | L2 UAMI → customer AI Search MI-endpoint | `search.azure.com` | Search Service Contributor | SDK auto-refresh | 401 → `Resumable`; 403 → check propagation (§4) |
| **H3 EntraAppReg** | Operator OBO (per §I1 tenant-scoped) | `graph.microsoft.com` scoped `Application.ReadWrite.OwnedBy` | Application Administrator on customer tenant | Operator re-auth if 1h expires mid-run | 403 on cross-tenant → `CrossTenantFicRefusedException` (Quarantined by handler) |
| **H4 KV secrets pop** | L2 UAMI | `{customerKv}.vault.azure.net` | Key Vault Secrets Officer | SDK auto-refresh | 403 → `Resumable` (RBAC propagation) |
| **H4-shared** | L2 UAMI | shared platform services (SBus, ACR, Cosmos, Search) then platform KV | Reader on source + Secrets Officer on target | SDK auto-refresh | 403 → `Resumable`; missing source → `Resumable` |
| **H4b BulkAppSettings** | L2 UAMI | `management.azure.com` (App Service PATCH) | App Service Contributor on customer sub | SDK auto-refresh | 413 (envelope size, COMP-09) → **`InvalidOperationException` from `EnsureBodyWithinCap` → `Resumable`** |
| **H5 DV env create** | L2 UAMI → Dataverse admin | `admin.services.crm.dynamics.com` | Power Platform Admin on customer tenant | SDK auto-refresh | 429 → `Resumable` (Dataverse capacity); 403 → operator intervention |
| **H6 solution import** | Operator OBO (per-tenant Dataverse admin) | `{customer}.crm.dynamics.com` | System Customizer + Import Solution | Operator re-auth if 1h expires | Import failure → `Resumable` with import log |
| **H7 env-var values** | Operator OBO | `{customer}.crm.dynamics.com` | System Administrator | Operator re-auth | 403 → `Resumable` |
| **H8 SPE container** | L2 UAMI → per-tenant Graph app | `graph.microsoft.com` `Application` scope | `SharePoint.ContainerType.Manage` app-role | SDK auto-refresh | 24h SPE provisioning wait — **near-instantaneous in practice** per feedback_spe_container_timing.md; NOT a Wave H-4 blocker |
| **H9 BFF deploy** | L2 UAMI | `management.azure.com` (App Service ZIP-deploy) | App Service Contributor | SDK auto-refresh | 5xx during zip upload → `Resumable` |
| **H10 DV AppUser + Graph parity** | Operator OBO | `{customer}.crm.dynamics.com` + `graph.microsoft.com` | System Admin (DV) + Directory.Read.All (Graph) | Operator re-auth if 1h expires | 403 on either side → `Resumable` |
| **H11 user provisioning** | L2 UAMI (per-tenant Graph app) | `graph.microsoft.com` | `User.Invite.All` + `Directory.ReadWrite.All` | SDK auto-refresh | 429 → `Resumable`; 403 on B2B → operator intervention |
| **H12a AI seed chain** | L2 UAMI → Dataverse web API | `{customer}.crm.dynamics.com` | System Customizer | SDK auto-refresh | 401 → `Resumable` |
| **H12b AppConfig seed** | Operator OBO | `{customer}.crm.dynamics.com` | System Customizer | Operator re-auth | 400 payload → seeder-specific handling (see per-seeder rejection codes) |
| **H12c runtime refs** | L2 UAMI | `management.azure.com` (App Service PATCH) | App Service Contributor | SDK auto-refresh | 403 → `Resumable` |
| **H13 E2E acceptance** | L2 UAMI (ARM Cost Management) | `management.azure.com` | Cost Management Reader on customer sub | SDK auto-refresh | Missing subscription → `InvalidOperationException` → `Resumable` (per ArmCostEnvelopeChecker silent-fail defense §a) |
| **H14 wiring parent** | Operator OBO | Multiple: Exchange, Graph, Dataverse | Global admin (Exchange), `Directory.ReadWrite.All`, System Admin | Operator re-auth | Per-sub-step; see H14a/b/c |

### Concrete rules the matrix codifies

- **Every L2 UAMI-side token** is subject to Azure SDK auto-refresh (typically 1h JWT with 5-min pre-expiry refresh). A handler that runs for &gt;55 min risks a mid-run 401 — that is why `ServiceBus:DefaultTimeToLive` defaults to 30 minutes (spec.md §4.2).
- **Every Operator-OBO handler** carries the operator's own AAD identity per NFR-11. If the operator's session token expires mid-run, the handler will surface a 401 → `FailureClass.Resumable`. Operator re-auths and hits `POST /api/runs/{id}/resume` per Fallback Matrix F2.
- **The `FailureClassifier` safe default is `Resumable`** — never `Quarantined`. The only handlers that intentionally quarantine are those that write partial external state the operator MUST review before re-running (H3 cross-tenant refusal, H5 partial DV env, H8 partial SPE container).
- **No handler auto-retries on 401 today.** COMP-12 (main-session-only, see punchlist) tracks the auth-v4-coordination follow-up for handler-side 401-with-token-refresh; until then, an auth-rotation-window race is why the operator gate must SendMessage to auth-v4 before dispatching task 186.

### Follow-up backlog

- **AUTH-1** (medium): Handler-side 401-retry-with-refresh middleware in the BFF `IJobHandler` infrastructure. Blocks on auth-v4 API shape.
- **AUTH-2** (low): Structured-log the token-audience + expiry for every SDK call in the L2 (adds observability for the auth-v4 rotation window).
- **AUTH-3** (medium): Auth-flow matrix as a runtime test — cross-reference each handler's actual `GetTokenAsync` call site against this table via source-scan. Similar shape to `CredentialCensusTests`.

---

## 2. COMP-03 — Profile-registry contract & phase-list completeness

**Why this exists.** `CreateRunRequest.Profile` is a free-form string
validated only as non-empty. The reconciler's `DagAdvancer.HandlerDependencies`
map is profile-agnostic — every handler runs for every profile unless
individual handlers branch on `TenancyModel` (Model1Shared vs Model2Dedicated).
Two silent-fail vectors exist:

1. **Unknown profile string** — CreateRun accepts it, run starts, first
   handler that keys on profile (e.g. H2a Bicep template selection) picks
   the default path or throws deep in the DAG.
2. **Profile→phase-list mapping** — no explicit registry; the DAG runs
   the same handlers for every profile. This is intentional (the
   reconciler owns dependency resolution, not profile-aware phase pruning)
   but the audit's concern was that a Model 1 Shared run should NOT run
   the full customer-tenant credential chain (H3 is customer-scoped).

### Current implementation reality

- **DAG completeness** is already asserted at compile time by
  `HandlerRegistrationCompletenessTests` (in Tests project) — that guard
  enforces that every `HandlerIds.Dispatchable` id has a keyed-DI entry
  AND a corresponding entry in `HandlerDependencies`. H4Shared + H4b (the
  audit's original concern) are BOTH present as of task 200/201.
- **Profile-conditional branching** exists inside individual handlers
  (e.g. `H2aBicepInfraDeployHandler` selects the shared-fabric vs
  dedicated-fabric ARM template via `TenancyModel`). This branching is
  NOT centrally enforced — a new profile string that doesn't match any
  handler's known set silently falls back to default paths.

### Recommended followup (subagent-safe follow-on)

Add a compile-time `KnownProfiles` const set in
`Sprk.Provisioning.ControlPlane.Models.ProvisioningRun` and validate
`CreateRunRequest.Profile ∈ KnownProfiles` at `RunsEndpoints.CreateRunAsync`.
This turns silent fallthrough into a 400 at intake. Wire path:

```csharp
public static class KnownProfiles
{
    public const string SpaarkeHostedModel1Trial = "spaarke-hosted-model1-trial";
    public const string SpaarkeHostedModel2      = "spaarke-hosted-model2";
    public const string CustomerOwnedModel2      = "customer-owned-model2";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        SpaarkeHostedModel1Trial, SpaarkeHostedModel2, CustomerOwnedModel2,
    };
}
```

**Follow-up backlog:**
- **PROF-1** (small): implement KnownProfiles + endpoint validation + ArchTest.
- **PROF-2** (medium): each handler that branches on profile / tenancyModel should log the branch chosen in structured-log — enables cross-profile audit.

---

## 3. COMP-05 — Handler crash-recovery + idempotency expectations

**Why this exists.** Handler audit confirmed handler classes exist but
did not test:
- (a) Worker crashes AFTER an external side-effect commits (e.g. H5 DV
  env created but Cosmos update lost) — does the next tick resume
  correctly?
- (b) Same run re-dispatched — every handler MUST be idempotent for
  same-`RunId` + same-`ParametersJson`; the Level-1 SB dedup + Level-2
  Redis lock guard the wire, but each handler body must ALSO tolerate
  re-execution.

### Per-handler idempotency posture (audit)

| Handler | External side-effect committed | Idempotent on re-run? | Notes |
|---|---|---|---|
| H0 | none (queries only) | ✅ | Pure query |
| H1 | none (queries only) | ✅ | Pure query |
| H2a | ARM deployment (`Incremental` mode) | ✅ | ARM incremental is idempotent by design |
| H2b | AI Search index create/update | ✅ | `CreateOrUpdateIndex` REST verb is idempotent |
| H3 | Entra app-reg + KV secrets + admin consent | ⚠️ **Partial** | Alt-key upsert on app-reg by display name; KV secrets replace; consent is grant-check-only (no duplicate grant) |
| H4 | KV secret writes | ✅ | KV `PUT` on same URI is version-bump-only |
| H4-shared | Shared KV secret writes | ✅ | Same as H4 |
| H4b | Bulk App Service settings PATCH | ✅ | ARM PATCH is idempotent |
| H5 | DV env creation (POST admin API) | ⚠️ **Partial** | Handler checks for existing env by name BEFORE POST; if crash between POST-succeeded and Cosmos-updated, next tick finds env and treats as noop |
| H6 | Solution import | ✅ | Dataverse import is atomic; re-import of same version is skip-with-log |
| H7 | Env-var value writes | ✅ | Dataverse alt-key upsert |
| H8 | SPE container creation | ⚠️ **Partial** | Container-id derived deterministically; re-create returns 409 which handler treats as noop |
| H9 | BFF zip deploy + slot swap | ⚠️ **Partial** | Slot swap is atomic; upload+swap gap is short-window risk |
| H10 | DV AppUser create + Graph app-role assign | ✅ | Both use alt-key semantics |
| H11 | B2B invite | ⚠️ **Partial** | Invite is idempotent per email; ROLE assignment is idempotent per (principal, role) |
| H12a | AI seed writes (Dataverse) | ✅ | Alt-key upsert; seed manifest engine tolerates duplicates |
| H12b | AppConfig seed (Dataverse) | ✅ | Alt-key upsert per seeder |
| H12c | Runtime references PATCH | ✅ | ARM PATCH is idempotent |
| H13 | none (verification only) | ✅ | Pure verify |
| H14 | Exchange policy + Graph webhook + DV endpoint | ⚠️ **Partial** | Each sub-step tolerates re-run; parent handler applies all-or-none via ReplaceRunAsync ETag |

### Partial-idempotency handlers require a "reconcile-first" tick

For the **Partial** rows above (H3, H5, H8, H9, H11, H14), the handler's
first action on re-entry MUST be an "external-state reconcile" that
observes the current external state and short-circuits if the desired
state already exists. This is already implemented per-handler; the
audit's concern was that no test asserts it.

### Follow-up backlog

- **CRASH-1** (medium): Add `tests/integration/seam/Provisioning/CrashRecoveryTests.cs`
  that spawns the Worker, kills it mid-H5-DV-env-create, restarts it, and
  asserts the H5 second tick observes the created env and short-circuits.
  Fixture requires a Dataverse test env + kill-window instrumentation.
- **CRASH-2** (small): Add an XML `<remarks>` block to each Partial
  handler above documenting the reconcile-first invariant so a future
  refactor doesn't accidentally remove it.

---

## 4. COMP-06 — Rollback taxonomy sweep + clear-quarantine lock-release

**Why this exists.** Rollback code exists (`FailureClassifier`,
`RollbackTransitions`, `QuarantineClearService`) but no audit verified:
- (a) every handler failure path lands in a defined class,
- (b) `RetryableWithCleanup` actually retries with backoff (LEVEL-1 dedup
  vs Attempt-hash — verified by task 107),
- (c) clear-quarantine RELEASES the `sprk_currentrunid` lock.

### (a) Per-handler failure-class inventory

Failure classes are declared in `HandlerResult.Failure.Class`; every
handler explicitly builds a `HandlerResult.Failure(...)` when it fails
recoverably. Escaped exceptions are caught by the dispatcher and
classified via `FailureClassifier.ClassifyException` — the SAFE default
is `Resumable`. There is currently no test asserting that every handler
declares at least one `Failure` with each expected class; this is the
audit's gap.

### (b) RetryableWithCleanup retry semantics

Verified by `ReconcilerEnqueuePayloadAttemptTests` +
`HandlerOutcomeApplierTests` — `RetryableWithCleanup` increments
`HandlerEnvelope.Attempt`, which changes `ComputeMessageId` (task 107
hash includes Attempt), which defeats SB Level-1 dedup so the retry
actually enqueues.

### (c) Clear-quarantine lock-release — **KNOWN GAP**

`QuarantineClearService.ClearAsync` mutates
`ProvisioningRun.Status = Failed` + `Quarantine.State = Cleared` + calls
`ReplaceRunAsync`, but does NOT release the Dataverse-registry-side
`sprk_currentrunid` lock. Consequence: a customer whose run was
Quarantined and then Cleared is **permanently blocked** from re-provisioning
because the sprk_currentrunid pointer still points at the now-cleared run.

### Fix required (main-session-only — REG-04 tie-in)

`QuarantineClearService.ClearAsync` MUST additionally invoke the
`CustomerRunGuard.ReleaseAsync` (or equivalent registry-side release)
after `ReplaceRunAsync.Success`. This is coupled to the REG-04 decision
about the CustomerRunGuard MI-FIC credential seam (Wave 0 Decision 9 —
deferred to Wave 2 B4).

### Follow-up backlog

- **ROLLBACK-1** (HIGH, main-session): `QuarantineClearService` releases
  `sprk_currentrunid` on Success — coordinate with REG-04 fix.
- **ROLLBACK-2** (medium, subagent-safe): Add
  `tests/unit/Sprk.Provisioning.ControlPlane.Tests/Rollback/PerHandlerFailureClassCoverageTests.cs`
  — a source-reflection test that scans every `IProvisioningHandler`
  implementation and asserts each declares at least one `FailureClass`
  it can produce (surfaces silent gaps where a handler only returns
  Success).

---

## 5. COMP-07 — RBAC matrix + wait-for-propagation helper

**Why this exists.** Skill-drift audit flagged unabsorbed F15/F16/F16.5/F18
findings around per-customer FIC SP creation + operator UAMI RBAC. The
composite RBAC picture — for each Azure resource created during a run,
who is the assignor + who is the assignee + what role — has no single
owner. Consequence: H3 creates a customer app-reg + grants Contributor
on the customer sub, H5 immediately tries to use it, 403 because RBAC
has not propagated (Azure ARM RBAC propagation is typically 30-60s but
can be longer).

### Composite RBAC matrix (all handlers that grant OR consume RBAC)

| Grant fires in | Principal (assignee) | Role | Scope | Consumed by |
|---|---|---|---|---|
| H2a | L2 UAMI | Contributor | `/subscriptions/{customerSub}` | H2a itself + H4 + H4b + H9 + H12c |
| H3 (customer app-reg) | Customer app-reg SP | Contributor | `/subscriptions/{customerSub}` | H5, H8 |
| H3 (customer app-reg on customer KV) | Customer app-reg SP | Key Vault Secrets User | `{customerKv}.vault.azure.net` | H4 (BFF at runtime) |
| H3 (operator OBO) | Operator | Application Administrator | Customer tenant | H3 itself (grant-then-use, same-token — no propagation risk) |
| H4 | L2 UAMI | Key Vault Secrets Officer | `{customerKv}.vault.azure.net` | H4 itself (grant-then-use — no propagation risk) |
| H4-shared | L2 UAMI | Key Vault Secrets Officer | `{platformKv}.vault.azure.net` | H4-shared itself + H4b + H9 |
| H5 (Lighthouse delegation) | Spaarke L2 UAMI | Reader (via delegation) | Customer sub | H1 (Reader-scope probes) |
| H10 | Customer DV AppUser | System Administrator | `{customer}.crm.dynamics.com` | Every subsequent DV-touching handler (H6, H7, H12a, H12b) |

### Propagation-risk gates

Two grant→consume gates carry propagation risk:

1. **H3 → H5**: customer app-reg gets Contributor on customer sub in
   H3, H5 uses it via Lighthouse-delegated Reader. If H5 fires
   immediately, RBAC may not have propagated. Current mitigation: H5's
   SDK auto-retry on 403 with polynomial backoff (up to 5 min). Not
   guaranteed to suffice under heavy tenant load.

2. **H10 → H11 (also H10 → H6)**: DV AppUser + Graph app-role assigned
   in H10, H11 (B2B invite) uses Graph token from same principal. Same
   Graph token; RBAC change is on Graph app-role side. Typically
   propagates in &lt;10s but is not deterministic.

### Follow-up backlog

- **RBAC-1** (medium): Add a shared `WaitForRbacPropagationAsync(scope,
  principalId, role, timeout=5min)` helper in
  `Sprk.Provisioning.ControlPlane.Core/Infrastructure` that polls
  `az role assignment list` on the scope until the assignment appears
  from the assignor's perspective. Called explicitly by H3, H4, H10 at
  end-of-handler. Replaces the current "trust polynomial backoff on the
  consumer side" pattern.
- **RBAC-2** (low): The composite matrix above should be codified as a
  test fixture that `H3PostGrantVerificationTests` reads to assert every
  grant it makes appears in the matrix (drift alarm).

---

## 6. COMP-10 — H0 cost-envelope BLOCKING semantics

**Why this exists.** Cost envelope code exists (`ArmCostEnvelopeChecker`,
task 183) but is currently consumed at **H13 (final acceptance)**, not
H0 (preflight). The audit's concern is that a Model 1 Prod run silently
overspends before H13 fires — H13 fires *after* every provisioning
handler has run, so the cost overrun has already been incurred.

### Current implementation

- `ArmCostEnvelopeChecker.CheckAsync` returns an `ExceedsAdvisoryThreshold`
  boolean; H13 sets its Success/Failure based on `H13AcceptanceOptions.
  CostDriftFailsRun` (default `false` — advisory-warn only).
- No H0-side cost check exists.

### Design tension (project-level judgment call)

An H0-side cost check would need to:
1. Query current subscription MTD cost — cheap.
2. Estimate the incremental cost of the run (Model 1 Shared: ~$0/customer
   marginal; Model 2 Dedicated: $200-500/month per stamp).
3. Enforce a hard ceiling per `costEnvelopePolicy` (batch-mode:
   `abort-on-overrun` default proposed).

Blocker: Model 2 Dedicated marginal-cost estimation depends on
Bicep-parameterized SKU choices that are not fully known until H2a.
H0 cannot know the exact marginal cost — only the *expected envelope*
per tenancy model.

### Recommended follow-up

- **COST-1** (medium): Extend `H0PreflightHandler` to invoke
  `ArmCostEnvelopeChecker.CheckAsync` with `TenancyModel`-derived
  expected envelope. On `ExceedsAdvisoryThreshold`, return
  `HandlerResult.Failure(FailureClass.Resumable, reason: "cost-envelope-overrun")`
  when a new `H0Options.CostEnvelopeAbortsPreflight` flag is `true`.
  Batch-mode intake defaults the flag to `true`.
- **COST-2** (small): Wire `costEnvelopePolicy` into the intake schema
  as an operator-tunable field (default `abort-on-overrun` for batch,
  `warn-only` for interactive).

---

## 7. COMP-11 — Multi-customer load posture + rate-limit guard

**Why this exists.** Registry audit scoped concurrency to same-customer
(`sprk_currentrunid` lock). No audit owned multi-customer horizontal load:
- Cosmos partition contention (each customer is a partition; concurrent
  runs against DIFFERENT partitions is well-supported).
- Shared platform KV writes across parallel handlers (H4-shared writes
  from N parallel runs contend on the same secrets).
- Service Bus per-message throughput (200 msg/sec per queue on Standard
  tier).
- Graph API throttling (Microsoft Graph enforces per-tenant + per-app
  throttles — 15k requests / 10 min for most endpoints).

### Current load-test coverage

- `tests/integration/Sprk.Provisioning.ControlPlane.LoadTests/`
  - `EnqueueLatencyScenario.cs` — proves single-tenant enqueue latency
    under load; does NOT exercise multi-customer concurrency.
  - `ReconcilerConcurrencyScenario.cs` — proves reconciler tick under
    concurrent-runs load; does NOT exercise cross-partition contention.
  - `LongHandlerScenario.cs` — proves long-handler behavior; does NOT
    exercise rate-limiting under multi-tenant onboarding.

### Follow-up backlog

- **LOAD-1** (medium): Add
  `tests/integration/Sprk.Provisioning.ControlPlane.LoadTests/MultiCustomerOnboardingScenario.cs`
  that spawns 10 concurrent customers, verifies all reach Completed with
  no 429s from Cosmos/SBus/Graph.
- **LOAD-2** (medium): Add rate-limit guard in
  `ServiceBusHandlerEnqueuer` — an app-side leaky-bucket that caps
  enqueue rate at 150 msg/sec (75% of Standard-tier limit) with
  backpressure surfacing as `Resumable`. Complements the SB service-side
  throttle.
- **LOAD-3** (low): Structured-log Graph 429 responses with retry-after
  values so an operator can distinguish "real failure" from "hit the
  throttle wall".

---

## 8. COMP-13 — Structured-log schema + secret-absence guarantee

**Why this exists.** NFR-11 requires every operator action be auditable
(operator's OWN AAD identity per §17 skill entry). Observability was not
in any of the 7 audit lenses.

### Required structured-log fields (per L2 log emission)

Every L2 log line emitted from `Sprk.Provisioning.ControlPlane.*` MUST
carry:

| Field | Type | Required | Source |
|---|---|---|---|
| `runId` | string (GUID) | ✅ | Cosmos runId (partition-key second field) |
| `customerId` | string | ✅ | Cosmos partition key |
| `customerIdHash` | string (8 hex) | ✅ (for correlations across log sinks) | `SHA256(customerId)[0..8]` — see `ServiceBusHandlerEnqueuer.HashCustomerIdForLog` |
| `handlerId` | string | ✅ (in handler + dispatcher scopes) | `HandlerIds.H*` |
| `tenantId` | string (GUID) | ✅ (in cross-tenant scopes) | `run.Parameters.NonSecret["tenantId"]` per Wave 0 Decision 1 |
| `operatorObjectId` | string (GUID) | ✅ (in operator-triggered scopes) | JWT `oid` claim |
| `attempt` | int | ✅ (in retry scopes) | `HandlerEnvelope.Attempt` |
| `messageId` | string (SHA256 hex) | ✅ (in SB enqueue/dispatch scopes) | `ServiceBusHandlerEnqueuer.ComputeMessageId` |
| `elapsedMs` | int | ✅ (in outcome scopes) | Timing measurement |
| `failureClass` | string | ✅ (in failure outcome scopes) | `HandlerResult.Failure.Class` |
| `rejectionCode` | string | ✅ (in per-handler-rejection scopes) | Per-handler `RejectionCodes` class |

### Secret-absence invariants

Structured-log emission MUST NOT contain:
- KV secret values (only KV URI refs)
- JWT bearer tokens (only claims: `oid`, `tid`, `roles`)
- Connection strings with `AccountKey=` / `SharedAccessKey=` / `Password=`
- Any base64 blob &gt;40 chars (heuristic for opaque secrets)

Enforced structurally by `CosmosProvisioningSecretGuardTests`
(runtime scan of L2 assembly types + fixture files).

### Follow-up backlog

- **OBS-1** (medium): Add
  `tests/Spaarke.ArchTests/StructuredLogSchemaTests.cs` — a source-scan
  that asserts every `ILogger` call site in `Sprk.Provisioning.ControlPlane.*`
  either uses the canonical field names above OR is on the explicit
  exception list (documented per file).
- **OBS-2** (small): Add a Kusto query template in
  `docs/procedures/kusto-queries-provisioning.md` for the top-5 operator
  investigations (which handler halted? what failure class? how long did
  each tick take?).
- **OBS-3** (medium): Emit an OpenTelemetry span per handler dispatch
  with the fields above as attributes; enables Application Insights
  end-to-end trace visualization.

---

## 9. Cross-worktree note (COMP-12 — main-session-only)

The auth-v4 rotation window coordination (COMP-12, HIGH) is documented
here for completeness but is a **main-session-only** follow-up per the
Wave 0 ADR-note Decision 10. Coordination expectation: `SendMessage` to
the auth-v4 branch before dispatching task 186 (live-fire) to confirm
no in-flight rotation windows. Handler-side 401 retry-with-refresh
(AUTH-1 above) is the longer-term durable fix.

---

## 10. Traceability

- **Punchlist source**: `projects/customer-provisioning-orchestration-r1/notes/pre-dispatch-audit-punchlist-2026-08-27.md` §Wave 7
- **Wave 0 ADR-note**: `projects/customer-provisioning-orchestration-r1/notes/wave-0-adr-note-2026-08-27.md`
- **Code fixes in this wave**:
  - `src/server/services/Sprk.Provisioning.ControlPlane.Core/Enqueue/ServiceBusHandlerEnqueuer.cs` — `EnsureBodyWithinCap`
  - `src/server/services/Sprk.Provisioning.ControlPlane.Core/Modules/ServiceBusModule.cs` — `MaxEnvelopeBodyBytes`
  - `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Enqueue/ServiceBusEnvelopeSizeGuardTests.cs`
- **Prereqs additions**:
  - `scripts/provisioning-prereqs/prereqs.yaml` — PRQ-S-15, PRQ-S-16
- **Follow-up backlog**: 15 items enumerated above (AUTH-1..3, PROF-1..2,
  CRASH-1..2, ROLLBACK-1..2, RBAC-1..2, COST-1..2, LOAD-1..3, OBS-1..3).
  Recommend a `spaarke-provisioning-hardening-r2` project to consume
  these as a coherent wave.
