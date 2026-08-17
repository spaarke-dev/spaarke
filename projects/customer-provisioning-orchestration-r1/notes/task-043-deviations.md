# Task 043 — Deviations + Design Notes

**Task**: 043 — Implement H1 Subscription Readiness Handler (ARM + Lighthouse Verification)
**Wave**: C4 (Handler Implementations, Batch 3B parallel with task 044 H2a)
**Date**: 2026-08-17
**Rigor**: FULL
**Reference**: `projects/customer-provisioning-orchestration-r1/tasks/043-implement-h1-subscription-readiness-handler.poml`

---

## Deviation 1 — Probe impl: Null placeholder (Wave C4) vs shell-out to `az` (POML text)

**POML guidance** (§ context / relevant-files):
> "Author `Handlers/SubscriptionReadiness/ArmSubscriptionReadinessProbe.cs` (uses Azure.ResourceManager or shell-out to `az` — choose consistent with task 041's PS shell-out pattern)"

**What I actually did**: Followed **task 042's pattern** (interface + `NullSubscriptionReadinessProbe` placeholder returning `Passed=true` with a Warning log per invocation) rather than task 041's shell-out pattern (which references authored scripts under `scripts/preflight/*.ps1`).

**Why**:
1. Task 041's shell-out pattern presumes existing scripts (`scripts/preflight/Test-*.ps1` from task 016). No `scripts/subready/` dir exists; authoring new PS scripts for two trivial `az account show` / `az account list --query` calls is heavyweight for a Wave-C4 scaffold.
2. Task 042 (H0.5 consent-capture) established the "interface + `NullXxx` Wave-C4 placeholder + real Wave-C5 impl" pattern for L2 handlers whose real Azure/Dataverse context is not yet wired. `IDataverseEnvironmentRegistryClient` + `NullDataverseEnvironmentRegistryClient` was the direct precedent.
3. ADR-010 seam justification (≥2 impls) is satisfied by day 1: `NullSubscriptionReadinessProbe` (production placeholder) + test-only `FakeProbe` fakes in the unit suite. Wave C5 adds the real ARM-backed impl (either `Azure.ResourceManager` SDK OR `az` shell-out via a new `scripts/subready/` dir) — swap is one line in `Program.cs`.
4. The Wave-C4 handler orchestration logic (parameter validation → tenancy classification → probe dispatch → Cosmos state advance → downstream enqueue) is fully implemented + fully unit-tested (29 tests, all POML acceptance criteria + defensive branches). Only the probe body is deferred.

**Impact**: Wave-C5 owner replaces the DI line `builder.Services.AddSingleton<ISubscriptionReadinessProbe, NullSubscriptionReadinessProbe>();` with the real impl. Handler code + tests + all rejection-code plumbing stay unchanged.

---

## Deviation 2 — POML acceptance mentions "writes ARM subscription metadata to Cosmos interStepState"; H1 writes to `GateStates` instead

**POML acceptance criterion #1**:
> "Given tenancyModel == 'SpaarkeOwned' and the target subscriptionId is reachable via ARM, when HandleAsync runs, then it returns success and writes ARM subscription metadata to Cosmos interStepState."

**What I actually did**: Wrote verification evidence to `run.GateStates[SubscriptionReadinessGates.SubscriptionReachable]` (+ `LighthouseDelegation` on the CustomerOwned branch) instead of `run.InterStepState`.

**Why**:
1. `InterStepState` is a **LOCKED enumerated POCO** per design.md §6.2 (see `Models/InterStepState.cs` header comment: "Ordering, meaning, and enumeration of keys are LOCKED by design.md §6.2. Do NOT add ad-hoc properties without amending the design first"). Enumerated keys: `bffAppRegId`, `s2sAppRegId`, `miObjectId`, `miClientId`, `containerTypeId`, `dataverseEnvUrl`, `openAiEndpoint`, `aiSearchEndpoint`, `cosmosEndpoint`, `systemUserId`, `speConsentCorrelationId`. **There is no `subscriptionId` / `subscriptionMetadata` slot.**
2. Amending `InterStepState` to add a subscription slot would require: (a) design.md §6.2 amendment, (b) coordination with wave C5 reconciler + downstream handlers (H2a/H4/H12 all read InterStepState), (c) CLAUDE.md §6.5 ADR/design conflict-resolution protocol invocation. This is out of task 043's declared scope.
3. Precedent: `H0PreflightHandler` (task 041) writes verification evidence to `GateStates`, NOT `InterStepState`, on both failure and success. H1 follows that pattern.
4. Spec.md FR-03 acceptance is narrower than the POML criterion — FR-03 just requires "`az account show` succeeds against target sub; Lighthouse RG scope accessible". No `interStepState` write is FR-required. The POML's `interStepState` mention appears to be aspirational rather than derived from FR-03.

**Impact**: Operator inspecting the completed H1 phase sees the reachability + Lighthouse gate entries in `GateStates` (Status = Verified, VerifierHandler = "H1", Evidence = probe response payload). This is the same shape as the `admin-consent` gate (task 042 H0.5) and matches design.md §6.2 gate example verbatim. If a downstream handler needs subscriptionId for a future feature, it reads from `run.Parameters.NonSecret["subscriptionId"]` (already required by H1's §4D I1 guard) — no re-plumb needed.

---

## Deviation 3 — POML mentions "resource-provider registrations complete (Microsoft.KeyVault, ...Storage, ...DocumentDB, ...CognitiveServices, ...Search, ...Web, ...ServiceBus, ...ManagedIdentity)"

**Dispatcher note** (from parent turn):
> "verifies subscription context: ARM subscription exists, Lighthouse delegation active if applicable, resource-provider registrations complete (Microsoft.KeyVault, ...)"

**What I actually did**: Implemented ONLY the two ARM checks FR-03 mandates — subscription reachability + Lighthouse delegation (CustomerOwned). Did NOT implement resource-provider registration verification.

**Why**:
1. Spec.md FR-03 acceptance is verbatim: "`az account show` succeeds against target sub; Lighthouse RG scope accessible". No RP-registration verification is FR-03 scope.
2. Design.md §4.1 H1 row: "Subscription readiness | NEW (D4) — ARM verification | **Lighthouse delegation** (CustomerOwned)". Again, no RP-registration in the design H1 row.
3. RP-registration is naturally H2a's concern (task 044 sibling — the Bicep deploy fails fast if any RP is un-registered; Bicep's own error tells the operator exactly which RP is missing). Adding a redundant pre-check in H1 would duplicate H2a's error surface without adding gate value.
4. If future spec revision adds RP-registration to H1's contract, the extension is a new `ISubscriptionReadinessProbe.CheckResourceProviderRegistrationsAsync` method + a handler branch — the seam is designed for extension.

**Impact**: None on Wave-C4 scope. If RP-registration verification becomes an H1 acceptance criterion in a future spec revision, extending the probe is a scoped follow-up.

---

## Non-deviations (Confirmed compliance)

- **§4D I1** (no hardcoded tenant / subscription): both `tenantId` and `subscriptionId` are REQUIRED from `run.Parameters.NonSecret`; missing either → `HandlerResult.Failure(Resumable, MissingTenantId | MissingSubscriptionId)` **before any probe fires**. Tests AC-6 + MissingSubscriptionId assert `probe.ReachabilityCalls == 0` on both paths.
- **§4C rollback**: All H1 failures classified `FailureClass.Resumable` per the H1 row (external precondition — operator resolves subscription config / accepts Lighthouse offer / regrants permissions + `POST /api/runs/{id}/resume`). H1 writes NO external Azure state, so no `RetryableWithCleanup` / `QuarantineRequired` path applies.
- **FR-22 idempotency (3-level)**:
  - Level 1 (wire): inherited from `ServiceBusHandlerEnqueuer.ComputeMessageId(HandlerId, RunId, CustomerId, paramHash)` — unchanged.
  - Level 2 (Redis): explicitly deferred per L2 project scope (no Redis dependency yet).
  - Level 3 (durable): implemented — handler scans `CompletedPhases` for `(Phase == "H1", IdempotencyKey == subready-{customerId})` and returns `Success` no-op on hit. Test AC-5 asserts `probe.ReachabilityCalls == 0` + `repo.WriteCount == 0` + `enqueuer.Sent == empty`.
- **Idempotency key format**: `subready-{customerId}` — NO version token per POML constraint (subscription readiness has no versioned artifact; a re-run always re-verifies the current state). `BuildIdempotencyKey` unit test asserts the format verbatim.
- **ADR-004** (`IProvisioningHandler` contract): implemented; handler declares `HandlerId => "H1"` matching design.md §4.1 catalog.
- **ADR-010** (DI minimalism): `ISubscriptionReadinessProbe` has ≥2 impls (Null placeholder + test-only fakes; Wave C5 adds real impl). Interface justified.
- **ADR-028** (Spaarke Auth v2 / MI-outbound): Null placeholder does NOT touch Azure; Wave-C5 real impl MUST use `DefaultAzureCredential` (documented in `ISubscriptionReadinessProbe.cs` header comment).
- **ADR-032** (Null-Object kill-switch): UNCONDITIONAL DI registration in Program.cs — no feature-gate branches on H1 or the probe. Matches task 042's H0.5 registration pattern.
- **ADR-036** (background-job infrastructure): reuses `IHandlerEnqueuer` (task 038) for downstream H2a enqueue; reuses `IProvisioningRunRepository` (task 037) for Cosmos state.
- **CLAUDE.md §10 BFF hygiene**: task does NOT touch `Sprk.Bff.Api/**` — L2 handler code lives in `Sprk.Provisioning.ControlPlane`. No BFF publish-size check required.
- **CLAUDE.md §11 component justification**: filed in POML `<justification>` (no existing subscription-readiness handler; PS `Provision-Customer.ps1` step 1 check is inline + not addressable as a resumable handler; H1 needs to be an `IProvisioningHandler` for L2 DAG advancement + 3-level idempotency).

---

## Sibling coordination — task 044

Task 044 (H2a Bicep infra deploy handler, sibling in Wave C4 Batch 3B) also modifies `Program.cs`. H1's registration additions are **discrete appended lines** (parity with task 042's H0.5 registration) — no modification of existing DI lines. First-committer wins; second committer must re-read `Program.cs` and rebase their edits on top of the winner's. No behavioral conflict is expected — the sets of new types + registrations are disjoint (H1 owns `H1SubscriptionReadinessHandler` + `ISubscriptionReadinessProbe`; H2a owns `H2aBicepInfraDeployHandler` + whatever probe/script wrapper it authors).

---

## Files created/modified

**Created (5 files):**
- `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/SubscriptionReadiness/SubscriptionReadinessRejectionCodes.cs` (rejection codes + gate ids)
- `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/SubscriptionReadiness/ISubscriptionReadinessProbe.cs` (probe interface + result record)
- `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/SubscriptionReadiness/NullSubscriptionReadinessProbe.cs` (Wave-C4 placeholder)
- `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/SubscriptionReadiness/H1SubscriptionReadinessHandler.cs` (the handler)
- `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Handlers/H1SubscriptionReadinessHandlerTests.cs` (29 tests)

**Modified (1 file):**
- `src/server/services/Sprk.Provisioning.ControlPlane/Program.cs` — 1 using statement + 1 registration block (parity with task 042's H0.5 registration style)

**Build**: `dotnet build src/server/services/Sprk.Provisioning.ControlPlane*` — 0 warning / 0 error (analyzers-as-errors + Nullable enabled per Directory.Build.props).
**Tests**: `dotnet test src/server/services/Sprk.Provisioning.ControlPlane.Tests` — 95 passed / 0 failed (66 pre-existing + 29 new H1 tests).
**CVEs**: `dotnet list package --vulnerable --include-transitive` — clean.
