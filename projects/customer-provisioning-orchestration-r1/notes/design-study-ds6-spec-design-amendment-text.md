# DS-6 — spec.md + design.md Amendment Text (Wave A locked decisions → ready-to-apply)

> **Produced**: 2026-08-18 by design-study sub-agent (research + writing only — NO spec.md/design.md edits performed; main session applies).
> **Inputs (evidence base)**: [`design-study-ds1b-option-d-hybrid-deep-dive.md`](./design-study-ds1b-option-d-hybrid-deep-dive.md) (DS-1b), [`design-study-ds2-dispatcher-design.md`](./design-study-ds2-dispatcher-design.md) (DS-2), [`design-study-ds2b-concurrency-safety-deep-dive.md`](./design-study-ds2b-concurrency-safety-deep-dive.md) (DS-2b), [`design-study-ds8-uami-dv-appuser-maturity.md`](./design-study-ds8-uami-dv-appuser-maturity.md) (DS-8), [`design-study-ds5-cat456-remediation.md`](./design-study-ds5-cat456-remediation.md) (DS-5), [`r1-gap-analysis-2026-08-18.md`](./r1-gap-analysis-2026-08-18.md) (GA).
> **Locked decisions reflected** (owner, 2026-08-18): (1) Option D hybrid runtime; (2) session-serialized dispatch; (3) Path X L2 Dataverse creds; (4) H9 artifact-based deploy; (5) queue delete + Bicep recreate with `requiresSession` + `requiresDuplicateDetection`; (6) `attempt` field in retry envelope; (7) C4.5 enum-serialization fix; (8) BFF zero role in provisioning execution.
> **Version discipline**: applying this amendment bumps design.md **v3.3 → v3.4** and requires a §20 CHANGELOG entry (text supplied in D-20 below). spec.md's source line (line 5) updates to cite v3.4.

---

## 1. Diff inventory

Every section needing amendment, keyed **S-n** (spec.md, 435 lines) and **D-n** (design.md, 1,884 lines). Full replacement text for each item is in §2 (spec) and §3 (design) under the same key. "VERIFY-NO-CHANGE" items are audited and confirmed correct; they get at most a clarifying sentence.

### spec.md

| Key | Location | Current text (gist, quoted where load-bearing) | Why wrong now | Fix class |
|---|---|---|---|---|
| **S-1** | Line 5 (source header) | "design.md v3.3 (1,884 lines)" | Version bump to v3.4 after D-side applies | Mechanical |
| **S-2** | Line 13 (Exec Summary) | "backed by idempotent deterministic L1 handlers (`IJobHandler` per ADR-004)" | Handlers are L2-local `IProvisioningHandler` (L2 MUST NOT compile-reference BFF's `IJobHandler` — task-042 deviation, GA §B); runtime is Option D | Replace |
| **S-3** | Lines 24–25 (Scope items 1–2) | "19 idempotent `IJobHandler` handlers"; L2 = "fire-and-forget handler execution via Service Bus + state-reconciler `BackgroundService`" | Same terminology fix; L2 scope must name the dispatcher + sidecar (the execution engine that GA §A6 shows "no task ever owned") | Replace |
| **S-4** | Line 103 (Affected Areas, L2 row) | "REST API + Cosmos state + state-reconciler `BackgroundService` + 19 `IJobHandler`s + endpoint filters" | Missing dispatcher, sidecar, `IProvisioningHandler` naming | Replace |
| **S-5** | Line 147 (FR-12, H9) | "BFF deployed via `Deploy-BffApi.ps1` + hardened `Deploy-Release.ps1` Phase 4" | H9 re-scoped to artifact-based deploy — `Deploy-BffApi.ps1` runs `dotnet publish` at provision time (DS-1b #19: "broken under every option") | Replace |
| **S-6** | Line 160 (**FR-22**) | "handlers run in BFF's existing `IJobHandler` infrastructure (ADR-004) with 3-level idempotency (MessageId dedup + Redis idempotency lock + deterministic idempotency key)" | **THE root muddle** (GA reading-guide): contradicts the spec's own MUST rule ("register provisioning handlers in L2, not BFF"), contradicts owner clarification, and named no dispatcher owner — the reason r1 shipped without E2E | **Restructure** |
| **S-7** | Line 161 (FR-23) | I5 serialization "via optimistic concurrency on `sprk_currentrunid`" only | Must also name the transport half (SessionProcessor) + the DS-2b riders (Conflict arms retained; conditional-patch fallback) + Path X credential for the guard | Replace |
| **S-8** | Line 162 (FR-24, §4C) | 4-class taxonomy; no retry-envelope detail | Must add `attempt` field to defeat the L1-dedup-kills-retry latent defect (locked decision 6; consequence of C4.6 dedup going live) | Amend |
| **S-9** | Line 197 (NFR-01) | BFF publish-size ceiling text | Correct but now ambiguous — must state it is **BFF-only**, and state the L2 budget (stock code deploy + sidecar image ≤ 250 MB ceiling) | Amend |
| **S-10** | Line 206 (NFR-10) | "All handlers 3-level idempotent (ADR-004) — Service Bus MessageId dedup + Redis `IdempotencyService` check/lock + Dataverse alternate-key upsert" | L3 for L2 handlers is the Cosmos `CompletedPhases` durable scan (+ Dataverse alt-keys where applicable); L1 requires the queue's `requiresDuplicateDetection: true` (currently false live — C4.6); MessageId hash must include `attempt` | Replace |
| **S-11** | Lines 216–229 (Applicable ADRs table) | No ADR-036 row | Add ADR-036 row recording verified scope finding (dispatcher/reconciler are in ADR-036's explicitly-excluded queue-consumer family — see §7) | Add row |
| **S-12** | Lines 233, 254 + block (MUST rules) | "MUST implement all L1 handlers as `IJobHandler` per ADR-004" | Terminology + 8 new MUST rules for the locked decisions (sessions, queue properties, `attempt`, Path X, artifact deploy, sidecar containment, serializer contract) | Replace + add |
| **S-13** | Lines 259–269 (Existing Patterns) | "13 production `IJobHandler` implementations serve as pattern exemplars" | Still true as *exemplars*; add one sentence: L2 dispatcher mirrors `ServiceBusJobProcessor` with the DS-2 §1.5 divergences | Amend |
| **S-14** | Lines 299–314 (New Components table) | L2 row (301), reconciler row (310) predate dispatcher/sidecar | Update L2 + reconciler rows; ADD two rows (dispatcher; EXO sidecar) with §11 three-question justification | Replace + add |
| **S-15** | Lines 318–333 (ADR Tensions) | ADR-004 row (324) says L2 "reuses existing `IJobHandler` infrastructure"; ADR-028 row (331) says H14a cert via "`EXCHANGE_CONNECT_APP_ID` / `EXCHANGE_CONNECT_CERT_THUMBPRINT`" env vars | ADR-004 row wording must match L2-local contract; ADR-028 H14a row must reflect sidecar execution + `-Certificate`-object auth (DS-1b §3: thumbprint mode assumes a Windows cert store the Linux sidecar lacks); ADD one informational row for the Option D runtime (verified: no ADR conflict — see §7) | Replace + add |
| **S-16** | Lines 341–362 (Success Criteria) | SC 2 (`IJobHandler`), SC 3, SC 5, SC 20 predate Wave A | SC 2/3/20 rewrites; SC 5 verified still correct with one clarifying clause; ADD SC 23 (Option D pipeline evidence) | Replace + add |
| **S-17** | Line 191 (FR-35) | Canonical naming | **VERIFY-NO-CHANGE** — naming rules are runtime-agnostic; H4's SDK port (`SecretClient`) consumes the same canonical catalog. No drift. | None |
| **S-18** | Line 137 (FR-02, H0.5) | Consent-callback on BFF | **VERIFY-NO-CHANGE** in substance — H0.5 remains the ONE BFF touch-point; S-6's FR-22(a) adds the explicit "BFF zero role" statement so the boundary is stated once, normatively | None (covered by S-6) |
| **S-19** | Line 171 (FR-27, Cosmos) | ProvisioningRun contract | Add serializer-contract acceptance (C4.5): Newtonsoft `StringEnumConverter` on `RunStatus`/`GateState`/`QuarantineState`, `"id"` dual-attribute, no `ttl` member — plus the seam test | Amend |
| **S-20** | Lines 173–179 (FR-28..32, §4D I1–I5) | Tenant-isolation invariants | **VERIFY-NO-CHANGE** — I1–I5 unaffected by runtime/dispatch/credential decisions. Path X *strengthens* I-posture (L2 gets its own auditable identity). Note only. | None |

### design.md

| Key | Location | Current text (gist) | Why wrong now | Fix class |
|---|---|---|---|---|
| **D-1** | Lines 1–21 (header) + line 3 (Status) | "Draft v3.3 …" | Version bump + v3.4 revision bullet | Mechanical |
| **D-2** | Lines 104–121 (§4A tooling table) | L2 row (118): "Custom **.NET 8** control-plane service"; rows for H0/H2a/H2b/H3/H5/H6/H8/H12/H13 name PS scripts as *the tool* | .NET 8 is flatly wrong (.NET 10); under Option D the execution tool per layer is SDK/REST — scripts survive as parity references + operator tooling, and ONE row (H14a) keeps PowerShell (sidecar) | **Restructure table** |
| **D-3** | Lines 123–131 (§4.1 preamble) | "handlers … fit the ADR-004 job contract"; "13 production `IJobHandler` implementations prove the pattern"; 3-level idempotency = "MessageId dedup + Redis + Dataverse alternate keys" | Contract naming (`IProvisioningHandler`); idempotency L1/L3 corrections (queue dedup ON + `attempt` in hash; `CompletedPhases` L3); insert pointer to new §4.1b | Replace |
| **D-4** | After line 172 (new **§4.1b**) | — (missing) | Handler runtime classification (12 Class A + H14 Class C + 6 in-process) with SDK package per handler — the Option D fact base from DS-1b §1–2 | **New section** |
| **D-5** | Line 146 (§4.1 catalog, H9 row) | "`Deploy-BffApi.ps1` + `auth-deployment-setup.md` + hardened Phase 4" | Artifact-based re-scope (no `dotnet publish` at provision time) | Replace row |
| **D-6** | Lines 261–302 (**§4.2**) | Hosting para (265); concurrency I5 (275); crash I6 (277); execution model steps 1–4 (279–288) — step 2 (282) says "Handler execution happens in the BFF's existing `IJobHandler` infrastructure … a dedicated worker consumes the Service Bus queue" | **THE root muddle, design side** (GA §A6). Restructure into §4.2 core + new §4.2a (runtime topology) + §4.2b (dispatcher + handler resolution) | **Restructure** |
| **D-7** | Lines 191–213 (§4C) | Failure classes + Cosmos transitions; no retry-envelope shape | Add retry-envelope `attempt` semantics + note sessions interplay (re-dispatch is a *fresh enqueue* with incremented `attempt`, never SB Abandon-loop — DS-2 §1.5) | Amend |
| **D-8** | Lines 246–259 (§4B trap catalog, T4 row 255) | T4 owned by H14, action-and-verify | **VERIFY-CONFIRMED + note**: sidecar preserves T4 semantics exactly — the script's get-before-set (`Get-` at :169 → conditional `New-` at :195 → re-verify at :205, DS-1b §0) runs unchanged inside the sidecar; the C# `ExchangePolicySidecarClient` maps the same JSON envelope onto `HandlerResult`. One-sentence note in T4 row. | Amend (1 sentence) |
| **D-9** | Lines 426–447 (§5.1) + 457–493 (§5.4) | "Handlers implement `IJobHandler`"; Option A "reuses ADR-004 `IJobHandler` + Service Bus + Redis idempotency" | Terminology; add the DS-2b flip-path sentence (conditional-patch append, not ETag-retry) to §5.4's migration story | Amend |
| **D-10** | Lines 532–557 (§6.2 ProvisioningRun) | Field table; no serialization contract | Add **serialization-contract note** (C4.5/#19/#20 family): Cosmos default serializer is Newtonsoft — every enum dual-attributed with `StringEnumConverter`; `RunId`→`"id"` dual-attribute; no `Ttl` property; contract test named | Amend |
| **D-11** | Lines 1037–1056 (§9.2) + 1097–1116 (§9.3) | UAMI = *customer-stamp* identity; App Users = BFF app-reg + customer UAMI | **VERIFY-still-correct for customer envs**; ADD §9.2/§9.3 cross-reference to new §9.6 (L2 control-plane identity — Path X) so the two UAMIs (customer-stamp vs L2 control-plane) can't be conflated | Amend + new §9.6 |
| **D-12** | After line 1140 (new **§9.6**) | — (missing) | L2 control-plane identity: UAMI-as-Dataverse-App-User on ADMIN env, scoped `Spaarke Provisioning Registry` role, `Grant-ControlPlaneIdentity.ps1`, Path Y deletion list, never-delete rule on the KV secret (DS-8) | **New section** |
| **D-13** | Lines 1141–1167 (§9A table) | 14 rows, all customer-scoped | Add **row 15**: L2 control-plane UAMI + admin-env App User (who provisions: one-time grant script; who verifies: H13 control-plane self-probe; rotation: none — that's the point) | Add row |
| **D-14** | Lines 1348–1357 (§11.2 IaC) | No `sprk-provisioning-jobs` queue row; `platform-controlplane.bicep` row silent on queue/RBAC/sidecar | Add queue-in-IaC (C5.4: `requiresSession: true`, `requiresDuplicateDetection: true`, PT1H, create-time-only + live delete/recreate), SB RBAC (C5.5), sidecar sitecontainer + ACR | Amend |
| **D-15** | Lines 1359–1366 (§11.3) | `JobSubmissionService` = "ASSESS"; `IJobHandler` = "REUSE" | Resolve: BFF Jobs stack = **REFERENCE ONLY** (dispatcher mirrors `ServiceBusJobProcessor` per DS-2 §1.5); L2 has its own enqueuer + dispatcher + `DispatchIdempotencyService`; nothing in BFF is reused at runtime | Replace |
| **D-16** | Lines 1497–1512 (§14 phasing) | Phase C/C' rows predate the dispatcher gap | Add **Phase C'' (Wave D-1/D-2)** row: dispatcher + queue surgery + serialization fix + collaborator ports per DS-1b §7 wave plan | Add row |
| **D-17** | Lines 1514–1598 (§14A) | U1/U2/U3 upgrade classes; H9 row (1541) "blue-green via staging slot" | Add L2-control-plane upgrade surface to §14A.1 (U1 extension: L2 code via `Deploy-ControlPlane.ps1`; sidecar image monthly rebuild cadence, ACR tag pinning); H9 upgrade row gains artifact provenance ({buildId} ties to version-compat matrix) | Amend |
| **D-18** | Lines 1602–1628 (§15) | SC 2/3/20 predate Wave A; north star silent on Option D | Mirror S-16; north-star sentence gains "via the Option D pipeline" clause + new SC 23 | Replace + add |
| **D-19** | Lines 1631–1661 (§16) | Resolved decisions end at v3.3 | Add **v3.4 resolutions table** (B6–B11): runtime, dispatch, L2 creds, H9 artifact, queue properties, serializer contract | Add block |
| **D-20** | Line 1738+ (§20 CHANGELOG) | Ends at v3.3 | Add v3.4 entry | Add block |
| **D-21** | Lines 1173–1236 (§10.1–10.3) | Run params + 7 env vars (H7) | **VERIFY-NO-CHANGE** — H7's hard-required 7-value list is untouched by Wave A (H7 was already in-process REST; DS-1b lists it among the 6 indifferent handlers). No drift. | None |
| **D-22** | Lines 1308–1346 (§11.1a) | 8 authoritative solutions | **VERIFY-NO-CHANGE** — H6's solution list is unaffected; only H6's *invocation mechanics* change (Web API `ImportSolution`/`StageAndUpgrade` port, D-4). Solution ZIPs-as-versioned-artifacts is already noted as invariant-under-every-option (DS-1b #14). | None |
| **D-23** | Lines 1004–1035 (§9.1 v3 tenancy) | One Spaarke tenant + one multitenant BFF app | **VERIFY-NO-CHANGE** — Path X concerns L2→admin-env registry writes only; customer app-reg model untouched. DS-8 §5: MI tokens are home-tenant-only, which *enforces* (not violates) this design; the sanctioned future cross-tenant path is Path Z (MI-as-FIC), noted in D-12. | None |

**Tally: 16 spec.md sections amended + 4 verified-no-change · 20 design.md sections amended (3 of them new sections) + 3 verified-no-change.**

---

## 2. spec.md amendments (full replacement text)

### S-2 — Executive Summary (line 13), replace the first sentence

> Build the single systematic process for standing up a new Spaarke customer environment and deploying the platform into it. One orchestrated pipeline, driven by an L2 control-plane App Service with Cosmos DB state, executed by idempotent deterministic L1 handlers implementing the L2-local `IProvisioningHandler` contract (ADR-004-shaped; the BFF's `IJobHandler` is a pattern exemplar, never a compile-time dependency), dispatched session-serialized per customer over Service Bus, running as pure .NET (SDK/REST) with a single minimal PowerShell sidecar for the one platform-forced residual (H14a Exchange), invoked via an L3 Claude Code operator skill. **The BFF has zero role in provisioning execution; its only touch-point is the H0.5 consent-callback endpoint.**

(Remainder of the paragraph — the D3 two-tier sentences — unchanged.)

### S-3 — Scope items 1–2 (lines 24–25), replace

> 1. **L1 handler catalog — 19 idempotent handlers implementing the L2-local `IProvisioningHandler` contract (ADR-004-shaped)**: H0 … H14 *(handler list unchanged — keep existing enumeration verbatim)*. Runtime classification per design.md §4.1b: 12 formerly-shell-out handlers ported to pure .NET SDK/REST collaborators + 6 already-in-process handlers = 18 pure .NET; H14 mixed (H14a Exchange sub-step executes via the EXO sidecar).
> 2. **L2 control-plane service** — standalone .NET 10 App Service (`platform-controlplane.bicep`) with Cosmos DB state, run lifecycle, gate management, REST + AAD (bearer, `Operator`/`Reader` app-roles). Execution model (design.md §4.2/§4.2a/§4.2b): REST enqueues to Service Bus queue `sprk-provisioning-jobs` (sessions + duplicate detection ON) → L2's own `ProvisioningHandlerDispatcher` (`ServiceBusSessionProcessor`, `SessionId = CustomerId`, `MaxConcurrentCallsPerSession = 1`) resolves handlers by `HandlerId` via keyed DI and executes them in-process on the stock (code-deployed) L2 App Service, with the H14a Exchange residual delegated to a localhost-only pwsh + ExchangeOnlineManagement sitecontainer sidecar → state-reconciler `BackgroundService` advances the DAG.

### S-4 — Affected Areas L2 row (line 103), replace the "What changes" cell

> L2 control-plane .NET 10 App Service — REST API + Cosmos state + `ProvisioningHandlerDispatcher` (session processor) + state-reconciler `BackgroundService` + crash-recovery startup service + 19 `IProvisioningHandler`s (SDK/REST collaborators per design.md §4.1b) + `ExchangePolicySidecarClient` + endpoint filters. Companion: EXO sidecar image (`infrastructure/`+CI) and `scripts/Deploy-ControlPlane.ps1`.

### S-5 — FR-12 (line 147), replace

> 12. **FR-12 (H9)**: BFF deployed **from the versioned CI-published artifact** — H9 resolves the artifact by `{buildId}` from the CI-published blob (produced by the existing BFF CI workflow), zip-deploys via Kudu/ARM (`Azure.ResourceManager.AppService` / `POST …/api/zipdeploy` with MI token), then executes the hardened `Deploy-Release.ps1` Phase 4 web-resource step (`customerId`-driven, no `spaarkedev1` hardcode). **Provision-time compilation is FORBIDDEN**: H9 MUST NOT run `dotnet publish`/`dotnet build` (the pre-amendment `Deploy-BffApi.ps1:221` behavior — built the BFF from repo source at provision time, unreproducible + un-versionable per DS-1b #19). Blue-green via staging slot in upgrade mode with rollback via re-swap. r3-era gates (analyzers-as-errors + god-class ratchet + ArchTests + naming-conformance + Graph app-role parity) run **in CI against the artifact**; H9's runtime gate check degrades to an artifact-metadata verification. **Acceptance**: `/health` returns 200; deployed build's `{buildId}` recorded in `interStepState` + `sprk_bffversion`; slot-swap smoke test produces no cold-start KV-ref failures; artifact provenance (CI run URL) recorded in run record; grep of H9 collaborators shows zero `dotnet publish` invocation.

### S-6 — FR-22 (line 160), replace entirely — THE core fix

> 22. **FR-22**: Handler execution model — **Option D hybrid runtime, owned end-to-end by L2** (DS-1b + DS-2/DS-2b, locked 2026-08-18):
>     - **(a) Enqueue**: L2 REST endpoints validate the request, write intent to Cosmos, enqueue a `HandlerEnvelope` to Service Bus queue `sprk-provisioning-jobs`, and return 202 Accepted (<100 ms roundtrip). **The BFF has zero role in provisioning execution.** Its only provisioning touch-point is the H0.5 `POST /api/onboarding/consent-callback` endpoint (customer-facing consent hop), which enqueues to the same queue and then exits the story. The BFF's `ServiceBusJobProcessor` never consumes `sprk-provisioning-jobs`; provisioning handlers are never registered in BFF DI.
>     - **(b) Dispatch**: L2's own `ProvisioningHandlerDispatcher` (a `BackgroundService` hosting a `ServiceBusSessionProcessor` with `SessionId = CustomerId`, `MaxConcurrentCallsPerSession = 1` (hard-coded correctness invariant, not tuning), `MaxConcurrentSessions = MaxConcurrentCustomers` (config, default 4), lock renewal sized to the 65-min handler pole) consumes the queue. **Handler resolution is keyed DI by `HandlerId`** ("H0"…"H14") against the L2-local `IProvisioningHandler` contract. Within one customer, handlers execute strictly one-at-a-time (single-writer-per-aggregate — the ProvisioningRun document has at most one handler-writer, making DAG branch-join write races structurally impossible per DS-2b §0); cross-customer runs execute in parallel. Handlers returning `WaitingOnGate` complete their message and release the session — **gates hold no session slot**.
>     - **(c) Runtime**: handlers execute **in-process on the L2 App Service** (stock `DOTNETCORE|10.0` code-based deploy — no custom container, no pwsh/az/pac in the main site) using .NET SDK/REST collaborators per design.md §4.1b. The single platform-forced PowerShell residual — H14a Exchange `ApplicationAccessPolicy` (no Graph API exists for it or its App-RBAC successor, verified 2026-08-18 per DS-1b §0) — executes in a **minimal pwsh + ExchangeOnlineManagement sidecar** (App Service Linux sitecontainer, ~200–230 MB, localhost-only HTTP, same UAMI, non-routable from the public front end) invoked through the `IExchangePolicyApplier` seam.
>     - **(d) Reconcile**: the state-reconciler `BackgroundService` polls Cosmos every 5 s, computes the DAG ready-set from `completedPhases`, and enqueues ready handlers.
>     - **(e) 3-level idempotency**: **L1** = Service Bus duplicate detection on deterministic `MessageId = SHA256(HandlerId|RunId|CustomerId|paramHash|attempt)` — queue MUST be provisioned with `requiresDuplicateDetection: true` (`duplicateDetectionHistoryTimeWindow: PT1H`); **L2** = Redis dispatch lock at the dequeue path (`DispatchIdempotencyService`); **L3** = durable dedup via the Cosmos `completedPhases` scan in every handler (+ Dataverse alternate-key upserts where handlers write Dataverse).
>     **Acceptance**: a ≥30-min handler completes without HTTP timeout under load test; no duplicate handler execution when multiple L2 instances run the reconciler; **session-freeze unit test asserts the constructed `ServiceBusSessionProcessorOptions.MaxConcurrentCallsPerSession == 1`** (protects the correctness invariant from config drift, DS-2b R3); a §4C retry re-enqueue (incremented `attempt`) is delivered — NOT swallowed by duplicate detection; contract test confirms the L2 project has no compile reference to BFF `IJobHandler` and the BFF has no reference to provisioning handlers; sidecar-invocation contract test (request/response envelope round-trip against the documented shape).

### S-7 — FR-23 (line 161), replace

> 23. **FR-23**: Concurrency + crash recovery (I5 + I6) — same-customer serialization has **two cooperating halves**: (i) **admission**: optimistic concurrency on `sprk_dataverseenvironment.sprk_currentrunid` (`null → newRunId`, conflict = 409 with winning run ID) gates run creation; (ii) **transport**: the session-serialized dispatcher (FR-22b) guarantees at most one handler-writer per customer during execution. Cross-customer runs parallel. **Riders (DS-2b)**: (R1) every handler KEEPS its `ReplaceRunAsync` Conflict arm — sessions eliminate handler∥handler races but NOT handler∥operator races (cancel / gate-advance / clear-quarantine endpoints + reconciler outcome-applier remain concurrent writers; the existing log-or-`Resumable` posture is the now-rare backstop); (R2) the documented flip path if L2 ever needs concurrent per-customer dispatch is **Cosmos conditional-patch append** (server-side atomic check-and-append), NOT ETag-retry loops — do not pre-build it. The `sprk_currentrunid` guard authenticates via **Path X** (FR-38). Crash recovery: on startup L2 scans Cosmos for `status ∈ {Running, WaitingOnGate}` older than 2× median-handler-duration + re-schedules from `currentPhase` with incremented `attempt`. **Acceptance**: concurrent-run test returns 409 on same customer; crash-then-restart resumes orphaned runs; session-freeze test per FR-22.

### S-8 — FR-24 (line 162), append to the existing text (keep 4-class taxonomy verbatim)

> **Retry envelope (added v3.4 per locked decision 6)**: every enqueued `HandlerEnvelope` carries `attempt` (int, ≥1). The reconciler / resume path increments `attempt` on every §4C re-enqueue of the same handler for the same run. `attempt` participates in the deterministic MessageId hash (FR-22e), so: legitimate retries get fresh MessageIds and are delivered; true duplicates (same attempt — e.g., two reconciler instances racing) still dedup at L1. Without this field, enabling queue duplicate detection (C4.6 fix) would silently swallow every §4C retry issued within the PT1H window — the retry path would be dead on arrival. **Acceptance**: integration test — fail a handler, resume, assert redelivery occurs and `attempt=2` is visible in the envelope; enqueue the same (handler, run, attempt) twice, assert single delivery.

### S-9 — NFR-01 (line 197), append two sentences

> **Scope clarification (v3.4)**: this ceiling governs the **BFF publish only**. The L2 control plane is a separate budget: main site is a stock code-based publish (no custom container; report size informationally in `Deploy-ControlPlane.ps1` output); the EXO sidecar image has its own ceiling of **≤ 250 MB compressed** (current design point ~200–230 MB per DS-1b §3), enforced at CI image-push time alongside the Trivy gate.

### S-10 — NFR-10 (line 206), replace

> - **NFR-10**: **All handlers 3-level idempotent** (ADR-004-shaped). For L2 provisioning handlers: L1 = Service Bus duplicate detection on `MessageId = SHA256(HandlerId|RunId|CustomerId|paramHash|attempt)` (queue property `requiresDuplicateDetection: true` — an IaC-declared, create-time-only property per FR-22e); L2 = Redis dispatch lock at the dispatcher dequeue path; L3 = durable dedup via Cosmos `completedPhases` scan in the handler body (+ Dataverse alternate-key upsert where the handler writes Dataverse). Version tokens `{schemaVer}` are deterministic content hashes / semantic versions per §4.1 preamble.

### S-11 — Applicable ADRs table (lines 216–229), add row

> | **ADR-036** (background-job infrastructure) | L2's dispatcher + reconciler are **queue-consumer / poll-driven services — the family ADR-036 explicitly excludes from its scope** ("Queue-consumer services (`ServiceBusJobProcessor` family) — different shape (event-driven, not schedule-driven)"). No `IScheduledJob` obligation attaches; no cron-style work exists in L2. Verified 2026-08-18 (DS-6 §7); no tension row required. |

### S-12 — MUST rules block (lines 231–257): replace line 233 and add new rules

Replace line 233:

> - ✅ **MUST** implement all L1 handlers against the L2-local `IProvisioningHandler` contract (ADR-004-shaped: one message, one handler, one outcome, idempotent, deterministic key). **MUST NOT** compile-reference the BFF's `IJobHandler` from the L2 project (peer services, no cross-reference).

Add after line 254 (the enqueue MUST):

> - ✅ **MUST** dispatch via `ServiceBusSessionProcessor` with `SessionId = CustomerId` and `MaxConcurrentCallsPerSession = 1`; the value 1 is a correctness invariant protected by a freeze unit test (FR-22b)
> - ✅ **MUST** declare queue `sprk-provisioning-jobs` in the L2 Bicep stamp with `requiresSession: true` + `requiresDuplicateDetection: true` (`PT1H` window) — both create-time-only; the pre-amendment live queue (created via `az` defaults with both OFF) MUST be deleted and recreated from Bicep (safe: drain-verify first; namespace-scope RBAC survives deletion)
> - ✅ **MUST** include `attempt` in every `HandlerEnvelope` and in the MessageId hash (FR-24 retry envelope)
> - ✅ **MUST** authenticate all L2 reads/writes to the ADMIN Dataverse env (registry lookups, `sprk_currentrunid` guard, H13 `sprk_setupstatus = Ready` PATCH) via **Path X**: the L2 UAMI registered as a Dataverse Application User with the scoped `Spaarke Provisioning Registry` custom security role, tokens via `DefaultAzureCredential` pinned to the UAMI (FR-38). **MUST NOT** provision the Path Y secrets (`CustomerRunGuard__ClientSecret`, `Dataverse__ClientSecret` L2 app-setting/param). **MUST** delete the L2 stamp's `dataverseClientSecretName` Bicep param + KV-ref emission — but **MUST NOT** delete the `Dataverse-ClientSecret` KV secret itself (the BFF shared-lib path still consumes it until NG1 #3b; BINDING never-delete)
> - ✅ **MUST** deploy the BFF in H9 from the versioned CI-published artifact; **MUST NOT** run `dotnet publish`/`dotnet build` at provision time (FR-12)
> - ✅ **MUST** keep the EXO sidecar non-routable (localhost sitecontainer binding + per-boot shared-secret header); only H14a's `IExchangePolicyApplier` client may call it; the sidecar image contains pwsh + the pinned ExchangeOnlineManagement module + the one script — no az, no pac, no dotnet
> - ✅ **MUST** dual-attribute every enum serialized into Cosmos run documents (`RunStatus`, `GateState`, `QuarantineState`) with Newtonsoft `StringEnumConverter` alongside the STJ attribute (C4.5 — the Cosmos default serializer is Newtonsoft and ignores STJ converters; same defect family as bugs #19/#20), backed by the serializer-contract test (FR-27)
> - ✅ **MUST NOT** shell out to pwsh/az/pac from the L2 main-site process (Option D invariant; the sole sanctioned PowerShell path is the H14a sidecar; temporary Wave D-2 fallback for H3/H6/H2a scripts, if a hard date forces it, runs those scripts in the sidecar — never the main site)

### S-13 — Existing Patterns (line 261), append one sentence to the first bullet

> The L2 `ProvisioningHandlerDispatcher` mirrors `ServiceBusJobProcessor`'s shape (BackgroundService + processor events + per-message DI scope + dead-letter policy) with four deliberate divergences per DS-2 §1.5: session processor (not plain), keyed-DI resolution by `HandlerId` (not enumerate-and-match), 65-min lock renewal (not 10), and §4C `RollbackTransitions` as retry authority (message completed once the outcome is applied; re-dispatch is a fresh enqueue with incremented `attempt` — never SB Abandon-loop double-retry).

### S-14 — New Components table (lines 299–314): replace two rows, add two rows

Replace row at line 301 (L2 control-plane), cell 1 unchanged, extend the description in cell 1 to read "L2 control-plane .NET 10 App Service (stock code deploy) + `ProvisioningHandlerDispatcher` + reconciler + 19 `IProvisioningHandler`s"; other cells unchanged.

Replace row at line 310 (state-reconciler) — cost-of-doing-nothing cell, append: "Without the DISPATCHER (added v3.4 — the reconciler enqueues but nothing consumed the queue), no handler ever executes: the exact §C-1.1 gap that blocked Phase F E2E."

Add rows:

> | **`ProvisioningHandlerDispatcher` (L2 session dispatcher)** | BFF `ServiceBusJobProcessor` (reference pattern, not reusable — drains a different queue, lives in the wrong service per MUST rules, no sessions) | No — extending BFF's processor would register provisioning handlers in BFF DI, violating §5.2/D8/D12 and the "BFF zero role" boundary | Without it, enqueued envelopes are never consumed — GA §A6: "THE execution engine. Without it no handler ever runs"; r1 shipped without E2E precisely because no task owned this component |
> | **EXO sidecar (pwsh + ExchangeOnlineManagement sitecontainer)** | `ExchangePolicyScriptApplier` shell-out (pre-Option-D); no Graph/SDK equivalent exists (verified 2026-08-18: legacy AAP **and** successor RBAC-for-Applications are both EXO-PowerShell-only, DS-1b §0) | No — cannot extend a .NET collaborator to call a nonexistent API; cannot keep main-site shell-outs without carrying pwsh+az+pac in the control plane (the rejected fat-container Option A) | Without it, H14a cannot execute at all under Option D → T4 trap unowned → app-only mail 403s ship silently; without the *sidecar* form specifically, the alternative is a ~1.5–2 GB fat image carrying az CLI's CVE stream as permanent fleet infrastructure |

### S-15 — ADR Tensions (lines 318–333): replace two rows, add one row

**Replace ADR-004 row (line 324)** — in the Rationale cell, replace "reuses existing `IJobHandler` infrastructure" with:

> "…Custom state machine is ~500–800 LOC net-new in L2 + an L2-local `IProvisioningHandler` contract that preserves ADR-004's shape (one message / one handler / one outcome; deterministic idempotency key; `attempt`/`correlationId` fields per the ADR-004 Job Contract schema) without compile-referencing the BFF. Session-serialized dispatch (v3.4) is a transport-ordering choice *within* the ADR-004 at-least-once model — handlers remain individually idempotent; sessions are not relied on for exactly-once. Documented in §5.1 + §5.4 + this row."

**Replace ADR-028 row (line 331)** — replace the final two sentences of the Rationale cell with:

> "The residual executes in the dedicated EXO sidecar (v3.4, design.md §4.2a): the sidecar fetches the PFX from platform KV at call time using the **same L2 UAMI** (sitecontainers reach the App Service MSI endpoint) and passes `-Certificate` (an `X509Certificate2` object) rather than `-CertificateThumbprint` — thumbprint mode assumes a Windows cert store the Linux container does not have (DS-1b §3). The cert never lands on disk; cleartext never traverses Cosmos. The AAP→App-RBAC migration (R22) is a sidecar-script change behind the stable `IExchangePolicyApplier` seam, not a handler change. Task 073 / `H14aExchangePolicySubHandler.cs` + `scripts/Set-ExchangeApplicationAccessPolicy.ps1`."

**Add row** (informational — verified no conflict; recorded here so the runtime decision has an auditable ADR disposition):

> | **ADR-036 / ADR-004 / B2** (runtime topology) | ADR-036 governs *schedule-driven* jobs; ADR-004 bans Durable Functions; design decision B2 chose App Service for L2 | Does the Option D runtime (pure-.NET main site + EXO sidecar sitecontainer + session dispatcher) conflict with any of these? | **C (comply — verified, no exception needed)** | Verified 2026-08-18 (DS-6 §7): ADR-036 explicitly excludes queue-consumer services from its scope and contains no "pure .NET handler" MUST; ADR-004 is satisfied (no Durable Functions; handlers keep the job-contract shape); B2 is *preserved, not amended* — the sidecar is a sitecontainer on the SAME App Service Plan/UAMI/App Insights (zero new Azure resources beyond an ACR repo tag), so B2's parity rationale survives intact. The rejected alternatives (ACA Job, separate App Service, ACI) are recorded in DS-1b §3. |

### S-16 — Success Criteria (lines 341–362): replace SC 2, 3, 20; clarify SC 5; add SC 23

> 2. [ ] **19 idempotent handlers** — each of H0…H14 implements `IProvisioningHandler`, is 3-level idempotent per NFR-10, independently testable, reports outcome to the Cosmos run record, and executes pure .NET per §4.1b (H14a via sidecar) — Verify: integration test runs each handler twice; second run is no-op (L3 `completedPhases` match); grep confirms zero `ProcessStartInfo`/pwsh/az/pac in main-site handler collaborators (H14a's sidecar client excepted)
> 3. [ ] **L2 sequencing + serialization + crash recovery** — dispatcher consumes `sprk-provisioning-jobs` session-serialized (`SessionId=CustomerId`, `MaxConcurrentCallsPerSession=1`); reconciler advances the DAG; per-customer serialization enforced at both admission (I5 guard, Path X creds) and transport (sessions); orphaned runs auto-resume on startup with incremented `attempt` — Verify: concurrent-run test returns 409; crash-restart test resumes from `currentPhase`; session-freeze unit test green
> 5. [ ] **E2E acceptance** — *(text unchanged — "brand-new environment reaches `Setup Status = Ready` via new pipeline…" still applies verbatim)* — with the clarifying clause: the pipeline exercised IS the Option D + session-serialized pipeline of FR-22 (v3.4); a run that reaches Ready via manual script execution does NOT satisfy this criterion
> 20. [ ] **§4.2 handler execution model verified** — L2 REST enqueue-and-return-202 under load test; ≥30-min handler completes without HTTP timeout; reconciler advances DAG correctly; queue properties live-verified (`az servicebus queue show` → `requiresSession=true`, `requiresDuplicateDetection=true`); retry-with-`attempt` delivered through dedup; serializer-contract test + scanner seam test green (a `Running` run written by the repository IS returned by `CosmosActiveRunScanner`) — Verify: load-test suite + contract/seam tests green
> 23. [ ] **Option D runtime landed** — main L2 site is a stock code-based deploy (no custom container); EXO sidecar image ≤ 250 MB compressed, Trivy-gated, non-routable; H14a executes through `IExchangePolicyApplier` → sidecar with T4 action-and-verify semantics preserved; H9 deploys from CI artifact (no provision-time build); Path X live (L2 UAMI systemuser exists on admin env with scoped role; registry writes attributed to it; Path Y Bicep params deleted; `Dataverse-ClientSecret` KV secret intact) — Verify: Phase F run log + `az` reads + Dataverse `systemusers` query + audit-log attribution sample

### S-19 — FR-27 (line 171), append

> **Serialization contract (v3.4, C4.5)**: the Cosmos client uses the SDK default (Newtonsoft) serializer with camelCase policy; therefore every enum on the run-document graph (`RunStatus`, `GateState`, `QuarantineState`) is dual-attributed with Newtonsoft `StringEnumConverter` (STJ attributes alone are ignored on the write path — proven defect class, bugs #19/#20/C4.5); `RunId` carries the dual `id` attribute; no `Ttl` property exists on the POCO. **Acceptance**: serializer-contract unit test (Newtonsoft + `CamelCasePropertyNamesContractResolver` round-trip asserts `"id"` present, `"status":"Running"` as string, gate/quarantine values as strings, no `"ttl"` member) + integration seam test (repository writes a `Running` run → `CosmosActiveRunScanner.ScanAsync` returns it — the test that would have caught the reconciler-blinding defect).

### S-add — new FR-38 (append after FR-37, §Governance block)

> 38. **FR-38 (Path X — L2 control-plane Dataverse credential model, locked 2026-08-18 per DS-8)**: all L2 reads/writes against the ADMIN Dataverse environment (registry lookups for H0.5 re-consent + `environmentId` resolution; `sprk_currentrunid` optimistic guard; H13's `sprk_setupstatus = Ready` PATCH) authenticate as the **L2 UAMI registered as a Dataverse Application User** on the admin env with the scoped custom security role `Spaarke Provisioning Registry` (org-level Read/Write/Create/Append on `sprk_dataverseenvironment` + minimum basics — NOT System Administrator), tokens via `DefaultAzureCredential` pinned to the UAMI (`{adminEnvUrl}/.default`). One-time per-env grant via idempotent `Grant-ControlPlaneIdentity.ps1` (role-ensure → app-user-ensure via the H10 Web-API idiom → role-associate → `WhoAmI` verify; also carries the C5.8 Graph app-role grants — one identity script for the control plane). Rotation: none exists (platform-managed) — that is the point. Path Y (client secret) is NOT provisioned: it would create a new documented ADR-028 §MUST violation, mis-attribute registry writes to the BFF's systemuser, and add a permanent rotation burden. Cross-tenant note: MI tokens are home-tenant-only, which *enforces* the registry-is-admin-env-only design; the sanctioned future cross-tenant path (customer-owned-tenant Model 2 handler writes) is MI-as-FIC (Path Z), noted in design.md §9.6 — not built in r1. **Acceptance**: `systemusers?$filter=applicationid eq {l2-uami-app-id}` on admin env returns 1 with the scoped role; a canary registry write is audit-attributed to the UAMI's systemuser; L2 stamp Bicep contains no `dataverseClientSecretName` param and no `CustomerRunGuard__ClientSecret`/`Dataverse__ClientSecret` app-settings; `Dataverse-ClientSecret` KV secret still exists (BFF consumer); H13 live-probe set includes "L2 systemuser exists + role assigned".

---

## 3. design.md amendments (full replacement text)

### D-1 — Header (line 3): set Status to

> **Status**: **Draft v3.4 — Wave A design-study integration (Option D runtime · session-serialized dispatch · Path X L2 credentials · H9 artifact deploy · queue/serializer hardening), pending owner sign-off.** v3.3 content otherwise carried forward.

Add revision bullet after line 10:

> - 2026-08-18 (v3.4: Wave A design studies integrated per DS-6 amendment text. **§4.2 restructured** — execution model corrected from "BFF's IJobHandler infrastructure" (contradicted the design's own D8/D12 + MUST rules and left the dispatcher unowned — the root cause of Phase F shipping without E2E) to L2-owned Option D: new §4.2a Runtime & Deployment Topology (stock App Service + EXO sidecar sitecontainer per DS-1b), new §4.2b Dispatcher & Handler Resolution (ServiceBusSessionProcessor, SessionId=CustomerId, MaxConcurrentCallsPerSession=1, keyed DI by HandlerId per DS-2/DS-2b); **new §4.1b** handler runtime classification (12 Class-A pure-.NET ports + H14 Class-C + 6 in-process); **H9 re-scoped** to CI-artifact deploy; **§4C** retry envelope gains `attempt`; **§6.2** serialization contract (Newtonsoft StringEnumConverter on all run-doc enums per C4.5); **new §9.6** L2 control-plane identity — Path X UAMI-as-Dataverse-App-User (DS-8); §9A row 15; §11.2/§11.3 dispositions updated; §14 Phase C'' wave plan; §14A L2/sidecar upgrade surface; §15 SC updates + SC 23; §16 v3.4 resolutions B6–B11.)

### D-2 — §4A tooling table (lines 104–121): replace the table

Preserve the section preamble (line 106) with one amendment — append: "**v3.4**: under Option D (DS-1b), the *execution* vehicle for handler logic is .NET SDK/REST in-process in L2; the PowerShell scripts listed below survive as (a) parity references for the ports, (b) operator/dev tooling, and (c) the H14a sidecar payload. Only H14a executes PowerShell at provision time."

Replacement table:

> | Layer | Execution vehicle (v3.4) | Parity reference / retained script | Handlers |
> |---|---|---|---|
> | **Azure stamp** (per-customer RG, App Service, KV, Storage, Service Bus, OpenAI, AI Search, Doc Intel, App Insights, Cosmos, optional SignalR) | `Azure.ResourceManager.Resources` ARM deployment of CI-pre-compiled `customer.bicep`→ARM-JSON (+ `WhatIfAtSubscriptionScopeAsync` for structured drift detection) | `Provision-Customer.ps1` steps 1–3 (~450 effective lines; steps 4–10 duplicate other handlers' jobs) + the 25 Bicep modules (unchanged — Bicep remains the IaC authoring language) | H2a |
> | **Dataverse environment lifecycle** | BAP admin REST (`api.bap.microsoft.com` … `/scopes/admin/environments`) via `HttpClient` + `DefaultAzureCredential` — the same REST sequence `Provision-Customer.ps1` STEP 5 already uses; TF Power Platform provider remains the deferred design target (M-10) | `pac admin create-environment` path retired from the runtime; H10 App User via Dataverse Web API (already in-process) | H5, H10 |
> | **Managed solution import** (8 solutions, dependency-ordered) | Dataverse Web API `ImportSolution` / `StageAndUpgrade` + `ImportJob` polling; solution ZIPs are **versioned build artifacts in the publish payload** (invariant under every runtime option) | `Deploy-DataverseSolutions.ps1` (parity acceptance tests against recorded outputs — heavy port, Wave D-2) | H6 |
> | **AI Search indexes** (7 canonical, 3072-dim) | `Azure.Search.Documents.Indexes.SearchIndexClient` with UAMI RBAC auth (deletes admin-key handling); index JSON schemas as content files | `scripts/ai-search/Deploy-AllIndexes.ps1` (script remains the catalog authority for the 7-index list) | H2b |
> | **Config-seed layer** | YamlDotNet manifest engine + Dataverse Web API upserts in-process (the pattern H12c already uses); declarative manifest still names the authoritative source per artifact | `Invoke-SeedManifest.ps1`, per-module seeders (parity references) | H12a / H12b / H12c |
> | **BFF deploy + web resources** | CI-published artifact fetch by `{buildId}` + Kudu/ARM zip-deploy + slot swap via `WebSiteSlotResource.SwapSlotAsync`; **no provision-time build** | `Deploy-Release.ps1` Phase 4 (hardened, `customerId`-driven) retained for the web-resource step | H9 |
> | **Entra app registration** (~14 grants) | `Microsoft.Graph` 6.x (`Applications`, `ServicePrincipals`, `Oauth2PermissionGrants`) + `SecretClient`; app-user step via Dataverse Web API (H10 idiom) | `Register-EntraAppRegistrations.ps1` (parity acceptance tests — heavy port, Wave D-2) | H3 |
> | **SPE container-type + container** | `Microsoft.Graph` `POST /storage/fileStorage/containerTypes` under `ClientCertificateCredential` (T6 cert from KV) | `Create-NewContainerType.ps1` family | H8 |
> | **KV secrets / identity patch / RBAC** | `SecretClient` + `Azure.ResourceManager.AppService` (`KeyVaultReferenceIdentity` patch, both slots) + `Azure.ResourceManager.Authorization` role assignments | `AzCli*` collaborators retired | H4 |
> | **Preflight quota probes** | `Azure.ResourceManager.CognitiveServices` / `.Compute` usage APIs + BAP REST + `SecretClient` | `Test-*.ps1` probe scripts | H0 |
> | **E2E acceptance probes** | C# `HttpClient` probes — converges with the C3.1/C3.2 obligation to write the 11 real trap/invariant probes (same work done once); naming-conformance as pure-C# port; cost via Cost Management REST | `Validate-DeployedEnvironment.ps1`, `naming-conformance-check.ps1` | H13 |
> | **Exchange ApplicationAccessPolicy (T4)** | **PowerShell — the sole residual**: `Set-ExchangeApplicationAccessPolicy.ps1` inside the EXO sidecar (§4.2a); no Graph API exists for AAP or its App-RBAC successor (verified 2026-08-18, DS-1b §0 — plan for the sidecar to live years; R22 migration is a sidecar-script change behind `IExchangePolicyApplier`) | — (the script IS the payload) | H14a |
> | **Consent-capture landing** (D18) | BFF endpoint (unchanged — the one BFF touch-point) | — | H0.5 |
> | **L2 orchestration** | Custom **.NET 10** control-plane service (§4.2) — REST + dispatcher + reconciler + crash recovery | — | All |
> | **L3 operator UX** | `/provision-environment` Claude Code skill → L2 REST | — | — |

(Keep the existing "Rejected alternatives" line 121, and append: "(d) fat tools container carrying pwsh+az+pac+EXO (~1.5–2 GB) — rejected as Option A per DS-1b §4/§7: az CLI's Python CVE stream, 25 stdout parsers preserving the T-trap silent-fail class, and two ambient auth sessions as permanent fleet infrastructure.")

### D-3 — §4.1 preamble (lines 123–131): replace lines 125–127 and 131

Replace lines 125–127:

> Provisioning steps implemented as idempotent handlers. Each handler is a self-contained, coarse-grained operation implementing the **L2-local `IProvisioningHandler` contract** (`src/server/services/Sprk.Provisioning.ControlPlane/Handlers/IProvisioningHandler.cs`) — ADR-004-shaped (one message, one handler, one outcome) but never a compile-time reference to the BFF's `IJobHandler` (peer services). The BFF's 13 production `IJobHandler` implementations remain the *pattern exemplars* that prove the shape at scale; the L2 dispatcher mirrors `ServiceBusJobProcessor` with the §4.2b divergences.

Replace line 131 (idempotency semantics), keep `{schemaVer}` sentence, and re-state the three levels:

> This makes re-running the same handler with unchanged inputs a no-op. Three-level idempotency (v3.4 precise form): **L1** — Service Bus duplicate detection on `MessageId = SHA256(HandlerId|RunId|CustomerId|paramHash|attempt)` (queue property `requiresDuplicateDetection: true`; the `attempt` term keeps §4C retries deliverable, §4C); **L2** — Redis dispatch lock at the dispatcher dequeue path; **L3** — durable dedup via the Cosmos `completedPhases` scan in each handler body (+ Dataverse alternate-key upserts where applicable). Runtime classification per handler: **§4.1b**.

### D-4 — NEW §4.1b (insert after line 172, before §4.1a)

> ### 4.1b Handler runtime classification — Option D (added v3.4 per DS-1b)
>
> Locked 2026-08-18: the runtime is **Option D hybrid** — every collaborator with an SDK/REST equivalent executes as pure .NET in-process in L2; the single platform-forced residual executes in the EXO sidecar (§4.2a). Of ~29 shell-out collaborators audited across 13 handlers (DS-1b §1, per-collaborator file:line evidence there), **exactly one** has no .NET equivalent.
>
> | Class | Definition | Handlers | Count |
> |---|---|---|---|
> | **A — pure .NET** | Every collaborator has an SDK/REST equivalent | H0, H2a, H2b, H3, H4, H5, H6, H8, H9 (post-artifact-re-scope), H12a, H12b, H13 | 12 |
> | **C — mixed** | One residual PS collaborator among SDK-capable ones | H14 (H14a only; H14b/c already in-process REST) | 1 |
> | **in-process already** | Never shelled out | H0.5, H1, H7, H10, H11, H12c | 6 |
>
> Per-handler SDK surface (packages already largely in the BFF/L2 dependency set):
>
> | Handler | Primary .NET surface |
> |---|---|
> | H0 | `Azure.ResourceManager.CognitiveServices` + `.Compute` usage APIs; BAP admin REST; `Azure.Security.KeyVault.Secrets.SecretClient` |
> | H2a | `Azure.ResourceManager.Resources` (ARM deploy of CI-pre-compiled Bicep→JSON + `WhatIf` structured drift); `.AppService` (T1 identity read); `SecretClient` |
> | H2b | `Azure.Search.Documents.Indexes.SearchIndexClient` (UAMI RBAC — admin-key handling deleted) |
> | H3 | `Microsoft.Graph` 6.x (`Applications`/`ServicePrincipals`/`Oauth2PermissionGrants`); `SecretClient`; Dataverse Web API (`HttpClient`) |
> | H4 | `SecretClient`; `Azure.ResourceManager.AppService` (`KeyVaultReferenceIdentity` PATCH both slots); `.Authorization` (role assignments) |
> | H5 | BAP admin REST via `HttpClient` + `DefaultAzureCredential` (the `Provision-Customer.ps1` STEP 5 sequence ported) |
> | H6 | Dataverse Web API `ImportSolution`/`StageAndUpgrade` + `ImportJob` polling; solution ZIPs as versioned publish-payload artifacts |
> | H8 | `Microsoft.Graph` `fileStorageContainerTypes` under `ClientCertificateCredential` (T6); `SecretClient` |
> | H9 | Artifact fetch by `{buildId}` + Kudu zip-deploy / `Azure.ResourceManager.AppService`; `WebSiteSlotResource.SwapSlotAsync` |
> | H12a | YamlDotNet + Dataverse Web API (H12c's existing in-process pattern) |
> | H12b | Dataverse Web API upserts (~40-line mechanical ports); the two deferred seeders (field-mapping, chart-def) authored directly in C# |
> | H13 | `HttpClient` probe suite (converges with the 11 real T/I probes owed under C3.1/C3.2); pure-C# naming-conformance port; Cost Management REST |
> | H14 | H14b/c in-process REST (unchanged); **H14a → `ExchangePolicySidecarClient : IExchangePolicyApplier` → sidecar HTTP** (§4.2a) |
>
> **Wave sequencing** (DS-1b §7): **Wave D-1** — dispatcher (§4.2b) + sidecar + the 9 thin az-one-liner SDK swaps + H0/H2b/H5/H12a/H12b/H13 ports + H9 artifact re-scope (~10 of 13 shell-out handlers executable). **Wave D-2** — H3, H6, H2a heavy ports with parity acceptance tests against recorded script outputs. Bounded fallback if a hard commercial date lands mid-wave: run those scripts temporarily in the sidecar (it has pwsh; add nothing but the scripts) — a contained concession, never a main-site shell-out.

### D-5 — §4.1 catalog H9 row (line 146): replace the "Source logic" cell

> **v3.4 artifact-based**: fetch CI-published artifact by `{buildId}` + zip-deploy + slot-swap (no provision-time `dotnet publish` — forbidden per FR-12) + hardened `Deploy-Release.ps1` Phase 4 for web resources (Gap 2 — `customerId`-driven, no `spaarkedev1` hardcode). r3 gates run in CI against the artifact; H9 verifies artifact metadata.

### D-6 — §4.2 restructure (lines 261–302)

**Keep unchanged**: hosting/B2 paragraph (265 — still correct; §4.2a extends it), protocol/auth (267–271), state store (273), API surface table (290–302).

**Replace the Concurrency paragraph (line 275)** with:

> **Concurrency (I5 resolved v3 · v3.4 transport half added)**: same-customer serialization has two cooperating halves. **Admission**: optimistic concurrency on Dataverse `sprk_dataverseenvironment.sprk_currentrunid` (`null → newRunId` conditionally; conflict → 409 with the winning run ID) — authenticated via Path X (§9.6). **Transport**: the §4.2b session dispatcher guarantees at most one handler executing (and therefore at most one handler-writer on the ProvisioningRun document) per customer at any instant. Cross-customer runs execute in parallel (own Cosmos partition + Dataverse row + SB session). Handlers KEEP their `ReplaceRunAsync` Conflict arms: sessions eliminate handler∥handler races, not handler∥operator races (cancel / gate-advance / clear-quarantine endpoints and the reconciler outcome-applier remain concurrent writers; the log-or-`Resumable` posture is the now-rare backstop). Documented flip path if per-customer concurrent dispatch is ever required (SLA < ~3 h — arithmetically impossible while the 24 h SPE gate exists): **Cosmos conditional-patch append** (server-side atomic check-and-append), NOT ETag-retry loops; do not pre-build (DS-2b §2b/§9).

**Replace the Handler execution model block (lines 279–288)** with:

> **Handler execution model (v3.2 added · v3.4 CORRECTED + restructured)**: App Service's 230 s HTTP timeout means L2 REST endpoints cannot synchronously invoke long handlers. The execution model is **fire-and-forget via Service Bus + L2-owned session dispatcher + state-reconciler**, all hosted in the L2 control plane:
>
> 1. **HTTP endpoint** (e.g., `POST /api/runs/{id}/resume`) validates, writes intent to Cosmos, enqueues a `HandlerEnvelope` (carrying `HandlerId`, `RunId`, `CustomerId`, `paramHash`, **`attempt`** — §4C) to `sprk-provisioning-jobs`, returns 202 Accepted. Roundtrip <100 ms.
> 2. **Handler execution happens in L2's own dispatcher** (§4.2b) — `ProvisioningHandlerDispatcher` consumes the queue session-serialized per customer, resolves the handler by `HandlerId` via keyed DI, invokes it in-process (pure .NET per §4.1b; H14a via the §4.2a sidecar), and applies the outcome to Cosmos via the §4C taxonomy. *(v3.4 correction: v3.2–v3.3 said "the BFF's existing `IJobHandler` infrastructure" — that contradicted D8/D12, the spec MUST rules, and the implementation, and left the consumer unowned; the BFF has zero role in provisioning execution — its ServiceBusJobProcessor drains a different queue and never registers provisioning handlers.)*
> 3. **State-reconciler `BackgroundService`** polls Cosmos every 5 s, computes the DAG ready-set from `completedPhases`, and enqueues ready handlers with the appropriate `attempt`. This advances the pipeline without blocking any HTTP request.
> 4. **Client polling** unchanged (`GET /api/runs/{id}`, 15–30 s interactive cadence).
>
> **Why not Durable Functions** *(paragraph at line 286 unchanged)*. **Concurrency safety in the reconciler** *(line 288)* — replace with: multiple L2 instances each run the reconciler; duplicate enqueues collapse at L1 (duplicate detection on the deterministic MessageId — same `attempt` ⇒ same MessageId ⇒ single delivery) and at L2 (Redis dispatch lock); the session processor distributes work across instances with zero coordination code (the broker grants each session lock to exactly one instance).

**Insert NEW §4.2a and §4.2b immediately after the block above (before the API surface table):**

> #### 4.2a Runtime & Deployment Topology — Option D (added v3.4 per DS-1b)
>
> **Main site**: the L2 App Service is a **stock `DOTNETCORE|10.0` code-based deploy — no custom container**. Solution ZIPs, seed manifests, index schemas, and CI-pre-compiled ARM JSON travel as publish-payload content (~tens of MB). The main site contains **zero shells**: no pwsh, no az CLI, no pac. Every Azure/Graph/Dataverse/BAP operation runs through scoped SDK clients / `HttpClient` under `DefaultAzureCredential` pinned to the L2 UAMI — no ambient CLI auth sessions; failures surface as typed exceptions that map exactly onto the §4C taxonomy (this is what retires the stdout-parser silent-fail class the T1–T6 catalog exists to kill).
>
> **EXO sidecar**: one **sitecontainer** on the SAME App Service (Linux sitecontainers, GA — same Plan, same UAMI, same App Insights; zero new Azure resources beyond an ACR repo tag; B2's parity rationale preserved). Image: `mcr.microsoft.com/powershell:7.4-mariner` + pinned `ExchangeOnlineManagement` module + `Set-ExchangeApplicationAccessPolicy.ps1` + a ~60-line HTTP listener. **≈200–230 MB compressed; ceiling 250 MB, Trivy-gated in CI.** Contract: `POST http://localhost:8091/apply-policy` `{tenantId, expectedAppIds[], policyScopeGroupId, correlationId: RunId, timeoutSeconds}` → `{outcome: Success|Failure|AlreadyCompliant, policiesApplied[], diagnostic}` — mirrors the script's existing `Write-ResultJson` envelope. The C# `ExchangePolicySidecarClient : IExchangePolicyApplier` maps the envelope onto `HandlerResult` exactly as the shell-out applier mapped exit codes. Auth: (main→sidecar) localhost-only binding + per-boot shared-secret header from platform KV; (sidecar→Exchange) app-only `Connect-ExchangeOnline` with the PFX fetched from platform KV at call time under the same UAMI, passed as `-Certificate` (X509 object — thumbprint mode assumes a Windows cert store a Linux container lacks). No new idempotency layer: the script is get-before-set idempotent; sidecar HTTP failures map connection-refused/timeout → `InfraFault` (Resumable), structured `Failure` → existing H14 classification. Observability: `correlationId = RunId` per request; one structured JSON log line per request → same Log Analytics workspace. Build: same GitHub Actions workflow as the main deploy, monthly rebuild cadence (pwsh + one signed module — a quiet loop, not az CLI's Python tree). Why this residual exists at all, and why it will live years: no Graph API exists for `ApplicationAccessPolicy` **or** its designated successor RBAC-for-Applications (both EXO-PowerShell-only, verified 2026-08-18 — DS-1b §0 with Microsoft Learn cites); the R22 migration is a sidecar-script change behind `IExchangePolicyApplier`, not a handler change.
>
> **Rejected topologies** (DS-1b §3–4): fat tools container (Option A — ~1.5–2 GB, az CLI CVE stream, 25 stdout parsers, ambient auth sessions as permanent fleet infrastructure); ACA Job (reopens B2's Container-Apps rejection for one call/run); separate App Service (a second host for one cmdlet); ACI (cold start + separate identity story).
>
> #### 4.2b Dispatcher & Handler Resolution (added v3.4 per DS-2/DS-2b)
>
> **`Dispatch/ProvisioningHandlerDispatcher`** — a `BackgroundService` hosting a `ServiceBusSessionProcessor` on `sprk-provisioning-jobs`:
>
> ```csharp
> _processor = _serviceBusClient.CreateSessionProcessor(_queueName, new ServiceBusSessionProcessorOptions
> {
>     MaxConcurrentSessions        = _options.MaxConcurrentCustomers,   // config, default 4 — cross-customer parallelism
>     MaxConcurrentCallsPerSession = 1,   // HARD-CODED — single-writer-per-customer correctness invariant (freeze test)
>     SessionIdleTimeout           = TimeSpan.FromSeconds(30),          // gated runs release their session
>     PrefetchCount                = 0,                                 // long handlers — no prefetch
>     AutoCompleteMessages         = false,
>     MaxAutoLockRenewalDuration   = TimeSpan.FromMinutes(65)           // H9/H6 pole
> });
> ```
>
> **Why session-serialized** (DS-2b, adversarially re-examined against 5 alternatives): every handler holds its run doc + ETag for a 10–60 min body and issues ONE terminal `ReplaceRunAsync` — under concurrent per-customer dispatch, every DAG branch-join becomes a systematic conflict generator that converts completed 30-min handlers into §4C re-dispatch churn. Sessions make handler∥handler races **structurally impossible** instead of survivable. The parallelism traded away is ~45–70 min of active compute on a ~27 h E2E (the 24 h SPE gate + consent gates dominate, and **gated runs hold no session**) ≈ 3–4% — and serialization is a per-customer latency policy with **zero fleet-throughput cost at any scale** (throughput = sessions × instances; multi-instance scale-out is broker-native). This is the single-writer-per-aggregate industry pattern (Orleans grain-per-key / Kafka partition-per-key / SB sessions). Flip condition + fallback recorded in §4.2 Concurrency (conditional-patch append).
>
> **Handler resolution**: keyed DI by `HandlerId` (`"H0"`…`"H14"`) against `IProvisioningHandler` — the option the code itself anticipates (`IProvisioningHandler.cs` header; `HandlersModule.cs`). Divergences from the BFF's `ServiceBusJobProcessor` reference pattern (all deliberate, DS-2 §1.5): session processor (vs plain); keyed resolution (vs enumerate-and-match — instantiating 19 handler graphs per message is wasteful); 65-min lock renewal (vs 10); **retry authority is §4C `RollbackTransitions`** — the dispatcher completes the message once the outcome is *applied*; re-dispatch is a fresh enqueue with incremented `attempt`, never the SB Abandon/redeliver loop (which would double-retry against §4C). Level-2 idempotency (Redis dispatch lock) sits in the dequeue path; handlers own Level 3. Dead-letter policy mirrors the BFF's (`InvalidFormat` / `HandlerResolutionFailed` / `NoHandler` / `Poisoned` / `MaxRetriesExceeded`).
>
> **Queue contract (IaC-declared, §11.2)**: `sprk-provisioning-jobs` with `requiresSession: true` + `requiresDuplicateDetection: true` (`duplicateDetectionHistoryTimeWindow: PT1H`) — both **create-time-only**; the pre-v3.4 live queue (az-CLI defaults: both OFF — sessions inert, dedup inert) MUST be deleted and recreated from the Bicep declaration (drain-verify first; namespace-scope RBAC survives). A session receiver on a non-session queue throws; a sessionful queue with a non-session receiver deadlocks — queue property and receiver type are one decision, taken together here.
>
> **Forcing functions**: unit freeze-test on `MaxConcurrentCallsPerSession == 1`; contract test that L2 has no `IJobHandler` compile reference; runbook/deploy-script verification `az servicebus queue show --query "requiresSession,requiresDuplicateDetection"`.

### D-7 — §4C (lines 191–213): append after the state-transition list (line 211)

> **Retry envelope (v3.4)**: re-dispatch after `Failed → Running` (resume) or reconciler-driven retry is a **fresh enqueue with `attempt` incremented**. `attempt` participates in the deterministic MessageId hash, so L1 duplicate detection (ON as of v3.4) never swallows a legitimate §4C retry issued inside the PT1H dedup window, while true duplicates (same attempt — racing reconciler instances) still collapse to one delivery. The dispatcher never uses SB Abandon as a retry mechanism (§4.2b) — §4C is the sole retry authority.

### D-8 — §4B T4 row (line 255): append one sentence to the Verification cell

> **v3.4**: H14a executes via the EXO sidecar (§4.2a); T4's action-and-verify semantics are byte-identical — the same get-before-set script runs unchanged inside the sidecar, and `ExchangePolicySidecarClient` maps its JSON result envelope onto the same create-if-missing / verify-drift-diagnostic branches.

### D-9 — §5.1/§5.4: three surgical replacements

- Line 439 ("Handlers implement `IJobHandler`.") → "Handlers implement the L2-local `IProvisioningHandler` (ADR-004-shaped; no BFF compile reference)."
- §5.4 Option A first Pro cell (line 465): "reuses ADR-004 `IJobHandler` + Service Bus + Redis idempotency + Cosmos" → "reuses the ADR-004 *pattern* (job contract shape, Service Bus, Redis idempotency, Cosmos — all stack primitives) via the L2-local `IProvisioningHandler` contract".
- §5.4 migration story (line 493), append: "**v3.4 note**: within the current architecture, the sanctioned concurrency flip path is Cosmos conditional-patch append per §4.2 — a smaller step than any workflow-product migration, and equally deferred (no failing behavior today)."

### D-10 — §6.2 (after line 557): add serialization-contract note

> **Serialization contract (v3.4 — C4.5 / bug #19/#20 family)**: the Cosmos client uses the SDK **default (Newtonsoft) serializer** with camelCase policy; STJ attributes are ignored on the write path. Therefore, on the run-document POCO graph: (1) `RunStatus`, `GateState`, `QuarantineState` carry **dual converters** — STJ `JsonStringEnumConverter` AND `[Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]` — so `status` is written as a string and `CosmosActiveRunScanner`'s `WHERE c.status IN ('Running','WaitingOnGate')` matches (without this, the reconciler and I6 crash recovery scan zero rows forever — a working dispatcher looks hung); (2) `RunId` carries dual `id` attributes; (3) no `Ttl` property (Cosmos rejects `"ttl": null`; if TTL returns, it must be Newtonsoft-visible with `NullValueHandling.Ignore`). Guarded by the serializer-contract unit test + the repository→scanner integration seam test (`tests/integration/seam/**` — the test class that would have caught this). Misleading comments at `CosmosModule.cs:140` and `CosmosActiveRunScanner.cs:40–44` corrected to state the real mechanism.

### D-11/D-12 — §9.2 cross-ref + NEW §9.6 (insert after §9.5, line 1140)

Append to §9.2 (after line 1056): "**Do not conflate the two UAMIs**: this section's UAMI is the *customer-stamp* identity (per-customer, consumed by the customer's BFF). The *L2 control-plane* UAMI and its admin-env Dataverse App User are §9.6."

> ### 9.6 L2 Control-Plane Identity — Path X (added v3.4 per DS-8)
>
> **Decision (locked 2026-08-18)**: all L2 reads/writes to the ADMIN Dataverse environment — registry lookups (H0.5 re-consent, `environmentId` resolution), the `sprk_currentrunid` I5 guard, and H13's `sprk_setupstatus = Ready` PATCH — authenticate as the **L2 UAMI registered as a Dataverse Application User** on the admin env, holding the scoped custom security role **`Spaarke Provisioning Registry`** (org-level Read/Write/Create/Append on `sprk_dataverseenvironment` + minimum basics — deliberately NOT System Administrator), tokens via `DefaultAzureCredential(ManagedIdentityClientId)` with scope `{adminEnvUrl}/.default` — the same idiom H10's `DataverseWebApiAppUserCreator` and the H5 health probe already use in-process.
>
> **Why**: the only ADR-028-compliant option ("MUST use `DefaultAzureCredential` for all server outbound — NOT `ClientSecretCredential`"); first-party supported (PPAC accepts MI Application IDs for app users, Microsoft Learn ms.date 2026-04-03; `pac admin assign-user --application-user`); the repo already ships the exact registration code (H10) and the L2 code headers pre-declare this migration (`CustomerRunGuardOptions.cs` "FUTURE MIGRATION" block); gives L2 a **distinct, auditable Dataverse identity** with its own service-protection budget instead of impersonating the BFF's systemuser as SysAdmin; **zero rotation surface** (platform-managed credential). Path Y (BFF app-reg client secret) rejected: new documented ADR-028 violation, false audit attribution, permanent rotation runbook, widened blast radius of a BFF secret leak.
>
> **Mechanics**: one-time per-env idempotent `Grant-ControlPlaneIdentity.ps1` — role-ensure → app-user-ensure (find-by-`applicationid` → POST `/systemusers` → `systemuserroles_association/$ref`) → `WhoAmI` verify; the same script carries the L2 UAMI's Graph app-role grants (C5.8) — one identity script for the control plane. Data-plane operation, not ARM — no Bicep. No admin consent exists or is needed (Dataverse authorizes via security roles; creating the systemuser row IS the authorization act).
>
> **Deletions this decision drives**: L2 stamp Bicep `dataverseClientSecretName` param + KV-ref emission; `CustomerRunGuardOptions.ClientId/ClientSecret` fields + `Validate()` clauses; the dummy-secret bug #18 dies at source. **What does NOT get deleted**: the `Dataverse-ClientSecret` KV **secret** (the BFF shared-lib path consumes it until NG1 #3b — BINDING never-delete). H4's *customer-side* `Dataverse-ClientSecret` seeding also stays (the customer BFF is still secret-based until #3b — explicitly not r1's migration).
>
> **Failure modes**: UAMI disabled → loud `CredentialUnavailableException` with a ≤24 h cached-token tail (accepted; writes fail closed as `InfraFault`/Resumable); systemuser or role removed → loud 401/403, restored in seconds by re-running the grant script; H13's live-probe set includes "L2 systemuser exists + role assigned". **Cross-tenant**: MI tokens are home-tenant-only — an *enforcement* of registry-writes-are-admin-env-only, not a limitation. The sanctioned future cross-tenant path (customer-owned-tenant Model 2 writes; secretless NG1 #3b) is **MI-as-FIC on a multitenant app-reg (Path Z, GA)** — noted for r2+, not built in r1.

### D-13 — §9A table: add row 15

> | 15 | **L2 control-plane UAMI + admin-env Dataverse App User** (`Spaarke Provisioning Registry` scoped role) | Spaarke platform sub (UAMI, Bicep-owned in L2 stamp) + admin Dataverse env (systemuser row) | `platform-controlplane.bicep` (UAMI) + one-time `Grant-ControlPlaneIdentity.ps1` (App User + role + Graph app-roles) | H13 control-plane self-probe (`systemusers` query + role check) + canary registry-write attribution | **None — platform-managed; no expiry cliff (the point of Path X)** | Identical in both models (registry lives only in the admin env) |

### D-14 — §11.2: append two rows

> | **NEW: `sprk-provisioning-jobs` queue (IaC-declared)** | `platform-controlplane.bicep` (child resource on the existing SB namespace via `existing` reference — NOT `modules/service-bus.bicep`, whose uniform properties are the wrong shape) | **NEW (v3.4, C5.4/C4.6)** | `requiresSession: true` + `requiresDuplicateDetection: true` (`PT1H`) + `lockDuration PT5M` + `maxDeliveryCount 10` + DLQ-on-expiry. Both properties create-time-only → live queue delete + Bicep recreate (drain-verify; RBAC survives). SB Data Sender + Receiver role assignments for the L2 UAMI land in Bicep alongside (C5.5, membership-topic.bicep pattern). |
> | **NEW: EXO sidecar image + sitecontainer** | ACR repo + `platform-controlplane.bicep` sitecontainer config + CI workflow stage | **NEW (v3.4, §4.2a)** | pwsh 7.4 + pinned ExchangeOnlineManagement + one script + HTTP listener; ≤250 MB ceiling, Trivy-gated; monthly rebuild cadence. |

### D-15 — §11.3: replace the table

> | Asset | Path | Disposition | Notes |
> |---|---|---|---|
> | `IJobHandler` + 13 production handlers + `ServiceBusJobProcessor` | BFF `Services/Jobs/`, `Services/Ai/Jobs/` | **REFERENCE ONLY (v3.4)** | Pattern exemplars for handler shape, idempotency, telemetry, and the dispatcher's BackgroundService+processor shape. **Never a compile-time or runtime dependency**: L2 defines `IProvisioningHandler` + its own `ProvisioningHandlerDispatcher`; the BFF processor drains a different queue and registers no provisioning handlers. |
> | `JobSubmissionService` | BFF `Services/Jobs/` | **RESOLVED: not used (v3.4 — closes the v3 "ASSESS")** | L2 has its own `ServiceBusHandlerEnqueuer` (deterministic MessageId incl. `attempt`, `SessionId=CustomerId`). Envelope mirrors the BFF's Subject/ApplicationProperties shape for observability parity only. |
> | `IdempotencyService` (Redis) | BFF `Services/Jobs/` | **PATTERN REUSE** | L2's `DispatchIdempotencyService` mirrors it at the dispatcher dequeue path (L2 of the 3-level scheme). |

### D-16 — §14 phasing: add row after Phase C' (line 1505)

> | **C'' (v3.4 NEW — Wave D-1/D-2 per DS-1b §7 + DS-2)** | **Execution engine + Option D ports.** Wave D-1: `ProvisioningHandlerDispatcher` (§4.2b) + freeze test + queue delete/recreate with sessions+dedup (C4.6/C5.4) + SB Receiver RBAC (C5.5) + C4.5 serializer fix + contract/seam tests + EXO sidecar (§4.2a) + 9 thin SDK swaps + H0/H2b/H5/H12a/H12b/H13 ports + H9 artifact re-scope + Path X grant script (§9.6) + `Deploy-ControlPlane.ps1` (C5.9). Wave D-2: H3/H6/H2a heavy ports with parity acceptance tests. **Ordering-critical**: C5.1/C5.2 Bicep config-key fixes land BEFORE any stamp redeploy (the appSettings array fully replaces live settings); C4.5 lands before any dispatcher testing above unit level (a working dispatcher with int-serialized status looks hung). | C | The component GA §C-1.1 identified as never-owned. Phase F re-runs after C''. |

### D-17 — §14A: two amendments

Append to §14A.1 table (after U3 row):

> | **U1-L2 — control-plane code + sidecar** (v3.4) | L2 App Service binaries via `Deploy-ControlPlane.ps1` (publish → zip-deploy → healthz + queue-property + config-fail-fast verification); EXO sidecar image via ACR tag bump (monthly rebuild cadence: pwsh patch + pinned EXO-module version bump; pin, never `latest`) | L2 code: as needed; sidecar: monthly | Low — L2 is fleet-internal; no customer maintenance window | Redeploy previous artifact / previous ACR tag |

Replace §14A.2 H9 upgrade-mode cell (line 1541), append: "…Artifact provenance (v3.4): upgrade-mode H9 resolves the artifact by target `{buildId}` from the version-compatibility matrix row; the deployed pair is recorded to `sprk_bffversion`/`sprk_solutionversion` (already §14A.3)."

### D-18 — §15: mirror S-16

- North-star paragraph (1604), append: "…Front-load lead-time items in preflight. **E2E is achieved via the Option D pipeline** (§4.2/§4.2a/§4.2b): session-serialized dispatch, pure-.NET collaborators, sidecar H14a — a run driven by manual script execution does not satisfy the E2E criterion."
- Replace SC 2 (1607), SC 3 (1608), SC 20 (1625) with the S-16 texts (numbering local to design §15).
- Add SC 23 with the S-16 SC 23 text.

### D-19 — §16: add v3.4 resolutions block (after line 1660)

> **v3.4 resolutions (2026-08-18 Wave A design studies — DS-1/1b, DS-2/2b, DS-5, DS-8)**:
>
> | Q | Question | Resolution | Locked in |
> |---|---|---|---|
> | **B6** | Handler runtime environment (GA C1.3) | **Option D hybrid**: stock code-deployed L2 App Service, zero shells in main site, SDK/REST collaborators (12 Class-A ports); minimal EXO sidecar sitecontainer for H14a only (~200–230 MB; the sole verified PowerShell-only residual — no Graph API for AAP or App-RBAC successor). Rejected: fat tools container (Option A). | §4.1b + §4.2a |
> | **B7** | Dispatcher + per-customer write safety (GA C1.1; DS-2 §2.3 re-examined adversarially in DS-2b vs 5 alternatives) | **Session-serialized dispatch**: `ServiceBusSessionProcessor`, `SessionId=CustomerId`, `MaxConcurrentCallsPerSession=1` (freeze-tested), keyed DI by `HandlerId`; Conflict arms retained (handler∥operator); flip path = conditional-patch append, not ETag-retry. Costs ~4% of E2E wall-clock; zero throughput cost at any scale. | §4.2b |
> | **B8** | L2 registry-write credential (GA C1.4 ↔ C5.3/5.6/5.7/5.8) | **Path X**: L2 UAMI as admin-env Dataverse App User, scoped role, `DefaultAzureCredential`; Path Y secrets never provisioned; L2 KV binding deleted, KV secret retained (BFF consumer). Path Z (MI-as-FIC) noted as the r2+ cross-tenant escape hatch. | §9.6 + FR-38 |
> | **B9** | H9 build-at-provision defect (DS-1b #19) | **Artifact-based deploy**: CI-published blob by `{buildId}`; provision-time `dotnet publish` forbidden; r3 gates run in CI. | §4.1 H9 row + spec FR-12 |
> | **B10** | Queue contract (C4.6/C5.4) + retry survivability | IaC-declared queue with `requiresSession` + `requiresDuplicateDetection` (create-time-only → live delete/recreate); `attempt` field in envelope + MessageId hash so L1 dedup never kills a §4C retry. | §4.2b + §4C + §11.2 |
> | **B11** | Run-doc serializer contract (C4.5, #19/#20 family) | Newtonsoft `StringEnumConverter` dual-attributes on `RunStatus`/`GateState`/`QuarantineState`; serializer-contract test + repository→scanner seam test. | §6.2 |

### D-20 — §20 CHANGELOG: add entry above v3.3

> ### v3.4 — 2026-08-18 (Wave A design-study integration)
>
> Root fix: §4.2 step 2 / spec FR-22 previously placed handler execution "in the BFF's existing `IJobHandler` infrastructure" — contradicting D8/D12, the MUST rules, and the implementation, and leaving the queue consumer unowned (the direct cause of Phase F closing without E2E; GA §C-1.1). v3.4 corrects the execution model to L2-owned Option D and integrates the six companion locked decisions. Changes: §4A tooling table rewritten for SDK/REST execution; §4.1 preamble contract naming (`IProvisioningHandler`); NEW §4.1b runtime classification; §4.1 H9 artifact re-scope; §4.2 concurrency two-halves + corrected execution model; NEW §4.2a runtime topology (stock App Service + EXO sidecar); NEW §4.2b session dispatcher + keyed resolution + queue contract; §4B T4 sidecar note; §4C `attempt` retry envelope; §5.1/§5.4 terminology + flip path; §6.2 serialization contract; §9.2 cross-ref + NEW §9.6 Path X; §9A row 15; §11.2 queue/sidecar IaC rows; §11.3 dispositions resolved; §14 Phase C''; §14A U1-L2 + H9 provenance; §15 SC 2/3/20 updates + SC 23 + north-star clause; §16 B6–B11. Evidence: notes/design-study-ds1b, ds2, ds2b, ds5, ds8, ds6 + r1-gap-analysis-2026-08-18.

---

## 4. New sections — recommended structure

**Add as new sections** (they carry genuinely new architecture that no existing section owns):

1. **design.md §4.1b Handler Runtime Classification** (D-4) — the Option D fact base; referenced by §4A, §4.2a, spec FR-22c.
2. **design.md §4.2a Runtime & Deployment Topology** (D-6) — main-site + sidecar. Keeping it a *subsection of §4.2* (not a top-level section) is deliberate: B2's hosting paragraph stays the anchor and §4.2a extends it, avoiding two competing "where does L2 run" authorities.
3. **design.md §4.2b Dispatcher & Handler Resolution** (D-6) — **merge the proposed §4.2c (keyed DI) into §4.2b** rather than a separate section: resolution is three sentences and belongs with the dispatcher that performs it; a standalone §4.2c would be a stub.
4. **design.md §9.6 L2 Control-Plane Identity** (D-12) — Path X. Placed in §9 (identity) not §4 (architecture) because it is an identity-surface fact, and §9A row 15 needs a depth anchor.

**Do NOT add**: a "§5.3a L2 Deployment Topology" — design.md §5.3 is ADR-017 status-granularity analysis, not deployment; topology lives in §4.2a and its IaC in §11.2 (D-14). Also do not split spec FR-22 into FR-22a/b/c as separate FRs — the lettered sub-bullets inside one FR (S-6) keep the FR numbering stable across 78 existing task POMLs that cite FR-22.

---

## 5. Owner-facing amendment summary (read before applying)

**What's changing**
- **FR-22 / design §4.2 step 2 — the root fix**: handler execution moves (on paper; the code already lives there) from "BFF's IJobHandler infrastructure" to **L2's own dispatcher**. The BFF's only provisioning role is the H0.5 consent callback. This kills the contradiction that let the queue consumer go unowned through 78 tasks (gap analysis §C-1.1) — the direct reason Phase F closed without E2E.
- **Runtime named for the first time**: stock code-deployed L2 App Service, zero shells, SDK/REST handlers (12 ports per DS-1b's per-collaborator matrix) + one ~200–230 MB EXO sidecar for H14a — the single operation Microsoft still gates behind Exchange PowerShell (verified against 2026 Learn docs, including the successor).
- **Dispatch semantics named**: session-serialized per customer (`MaxConcurrentCallsPerSession=1`, freeze-tested). Costs ~4% of a ~27 h E2E; buys structural impossibility of the branch-join write races that would otherwise turn every DAG join into §4C churn (DS-2b examined 5 alternatives).
- **Three latent defects codified as spec text**: queue recreated with sessions+dedup ON (both create-time-only); `attempt` field so dedup can't swallow §4C retries; Newtonsoft `StringEnumConverter` on run-doc enums so the reconciler can actually see `Running` runs.
- **Path X**: L2 gets its own auditable Dataverse identity (UAMI App User, scoped role, zero rotation) for registry writes; Path Y secret scaffolding deleted from Bicep.
- **H9 stops compiling the BFF at provision time**: deploys the CI artifact by `{buildId}`.

**What's NOT changing**
- Handler catalog H0–H14, the DAG, gates, §4B traps T1–T6, §4C's 4-class taxonomy, §4D invariants I1–I5, tenancy (D3/Model 1/Model 2), §9.1 app-reg model, the 7 env vars (H7), the 8 solutions (H6), canonical naming (FR-35/36), Phase G/H scope, the upgrade model's U1–U3 classes, the L3 skill architecture, cost envelopes. The Cosmos state model stays the single mutable run document (event sourcing evaluated and rejected — wrong write rate). ADR Tensions gain no new Path B — nothing here amends an ADR.
- The `Dataverse-ClientSecret` KV **secret** stays (BFF consumer; BINDING never-delete). Only L2's *binding* to it goes.

**Why each change is necessary** — each is grounded in a Wave A study: FR-22/§4.2 (GA reading-guide contradiction + owner clarification); runtime (DS-1b §1–§4: 1-collaborator residual vs 2 GB fat container); dispatch (DS-2 §2 + DS-2b §§1–9); queue/`attempt`/serializer (DS-5 C4.5/C4.6 — grep-verified live defects); Path X (DS-8: only ADR-028-compliant option, code headers pre-declare it); H9 (DS-1b #19: builds from repo source at provision time — unreproducible, un-versionable).

**What breaks if we DON'T apply**
- The spec keeps instructing implementers to build the dispatcher in the BFF, which the MUST rules forbid — the same deadlock that already ate Phase F recurs in every follow-on task.
- The moment C4.6's dedup goes live without the `attempt` amendment, **every §4C retry inside 1 h is silently swallowed** — failed runs become permanently stuck with no error.
- Without the serializer amendment as binding text, the reconciler scans zero rows: a fully-built dispatcher ships and E2E **silently hangs after H0**, indistinguishable from a broken dispatcher (DS-5: "it fails invisibly, poisoning every dispatcher test above the unit level").
- Without Path X in the spec, Wave C5 provisions real BFF secrets into L2 — a fresh ADR-028 violation with a permanent rotation runbook and false audit attribution.

---

## 6. Application order (main session applies; sub-agents cannot write spec.md/design.md's `.claude` siblings but CAN write these two files — apply in main session regardless, per the task boundary)

**Batch 1 — the load-bearing pair (must land together, atomically):**
1. S-6 (FR-22) + D-6 (§4.2 restructure incl. §4.2a/§4.2b). These are two statements of one fact; landing one without the other recreates the contradiction the amendment exists to kill.
2. S-12 MUST-rule replacements/additions (they cite FR-22's new text).

**Checkpoint 1** (run `context-handoff`): spec+design now agree on the execution model.

**Batch 2 — decisions that hang off Batch 1 (each independent of the others; any order):**
3. D-4 (§4.1b) + D-2 (§4A table) + D-3 (§4.1 preamble) — runtime fact base (referenced by FR-22c, so after Batch 1).
4. S-8 + D-7 (`attempt`) and S-10 (NFR-10) and D-14 (queue IaC rows) — the queue/retry cluster.
5. S-19 + D-10 (serializer contract).
6. S-add FR-38 + D-12 (§9.6) + D-13 (§9A row 15) + D-11 (§9.2 cross-ref) + S-7 (FR-23) — the Path X cluster (S-7 references FR-38, so FR-38 first within this cluster).
7. S-5 (FR-12) + D-5 (H9 catalog row) + D-17 (H9 provenance) — artifact cluster.

**Checkpoint 2**: all locked decisions have normative text.

**Batch 3 — bookkeeping and derived text (safe, orthogonal):**
8. S-2/S-3/S-4 (summary/scope/affected-areas), S-13, S-14, D-15 (dispositions), D-16 (Phase C''), D-17 U1-L2 row.
9. S-15 + S-11 (ADR tensions + ADR table row), S-16 + D-18 (success criteria), S-9 (NFR-01).
10. D-19 (§16 B6–B11), D-20 (CHANGELOG), D-1 (version header), S-1 (spec source line → v3.4). **Version bump last** — the header claims v3.4 only when the content is v3.4.

**Checkpoint 3 + final**: grep-verify zero remaining hits for `BFF's existing \`IJobHandler\`` / "IJobHandler infrastructure" in spec.md+design.md; grep `IJobHandler` in both docs and confirm every survivor is a deliberate "pattern exemplar / reference-only" mention (S-13, D-3, D-15, §11.3) — not an execution-model statement.

**Inter-file dependency note**: no design.md amendment depends on a spec.md amendment or vice versa except the Batch-1 pair and S-7→FR-38 ordering above. Task POMLs citing FR-22/FR-23/FR-24 (numbering unchanged) need no renumbering pass.

---

## 7. ADR check (per amendment cluster)

| ADR | Finding | Disposition |
|---|---|---|
| **ADR-004** (job contract) | Option D + session dispatch **remain within ADR-004**: handlers keep one-message/one-handler/one-outcome, deterministic keys, at-least-once idempotency; no Durable Functions; the ADR-004 Job Contract schema *literally includes `attempt`* — the FR-24 envelope converges with, not diverges from, the ADR. Sessions are transport ordering, not an exactly-once claim. The already-declared Path A (L2 orchestration as a new component) is unchanged in scope; its Rationale cell is only re-worded (S-15) for the `IProvisioningHandler` naming. **No new Path A/B needed.** | Existing Path A row amended (wording only) |
| **ADR-013** (AI facade) | No interaction: provisioning handlers inject no AI internals; H0.5 unchanged (pure consent capture); H13's sample-analysis probe, if it needs AI, still goes through `PublicContracts/` per the existing Path C row. | No change |
| **ADR-028** (auth) | **Path X re-verified against the MUSTs**: the operative rule — "MUST use `DefaultAzureCredential` (managed identity) for all server outbound — Graph app-only, Dataverse service identity, Cosmos, Key Vault. NOT `ClientSecretCredential`" — is *satisfied by Path X and would be newly violated by Path Y*; DS-8 §1 shows all r1-new L2 code is already DefaultAzureCredential-shaped. The H14a cert credential remains inside the existing documented Path A exception (spec line 331), whose row is amended (S-15) for the sidecar execution vehicle + `-Certificate` object; the exception's *scope does not grow* (same one operation, same platform constraint). HMAC (H0.5) and webhook rules untouched. Client-side MUSTs (useAuth etc.) out of scope — no client surface in these amendments. | Existing Path A row amended; Path X = compliance, recorded as FR-38 |
| **ADR-036** (background jobs) | **Verified: contains no "pure .NET handler" convention.** Its MUST rules govern *schedule-driven* (`IScheduledJob`/cron) work, and its scope text *explicitly excludes* "queue-consumer services (`ServiceBusJobProcessor` family) — different shape (event-driven, not schedule-driven)". The dispatcher is exactly that excluded family; the 5 s reconciler is a poll loop, not cron. The sidecar likewise touches nothing ADR-036 governs. → The task-brief's hypothesis of an ADR-036 sidecar tension is **disconfirmed**; recorded as the Path C verification row (S-15) + new spec ADR-table row (S-11) so the disposition is auditable rather than silent. | No exception needed; verification recorded |
| **ADR-038** (testing) | New tests, all in KEEP-path categories: (1) **session-freeze unit test** (`MaxConcurrentCallsPerSession == 1`) — correctness-invariant guard in the r3 forcing-function family; (2) **serializer-contract unit test** (Newtonsoft round-trip: `"id"`, string enums, no `ttl`); (3) **repository→scanner integration seam test** at `tests/integration/seam/**` (the ADR-038 E-40 category — DoD for dispatch-spine changes; the test that would have caught C4.5); (4) **sidecar-invocation contract test** (envelope round-trip against §4.2a's shape); (5) **no-cross-reference contract test** (L2 ↛ `IJobHandler`; BFF ↛ provisioning handlers). Bans respected: no `Mock<HttpMessageHandler>`, no DI-registration tests (C4.1's disposal test is framed as disposal *behavior*), no ctor-null tests. All test-modifying tasks trigger the unconditional code-review+adr-check override per root §8. | Compliant; tests enumerated in S-6/S-16 acceptance |
| **ADR-010 / ADR-032** (spot-check) | DI additions land in L2, not BFF (ADR-010 row unchanged). The sidecar client is unconditional (H14a always registered) — no `if (flag)` block, so §F.1/ADR-032 not triggered; if the sidecar is ever feature-gated, the Null-Object pattern applies per the existing row. | No change |

---

*Ready-to-apply amendment text. No spec.md, design.md, source, config, Azure state, or `.claude/**` files were modified by this study.*
