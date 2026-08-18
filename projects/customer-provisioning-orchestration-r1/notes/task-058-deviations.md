# Task 058 — State-Reconciler `BackgroundService` — Deviations

**Task**: 058 — `Implement state-reconciler BackgroundService (5s Cosmos poll + DAG advancement + 3-level idempotency)`
**Date**: 2026-08-17
**Author**: Wave 4 Batch 4C agent (opus / xhigh)
**Related**: [POML](../tasks/058-implement-state-reconciler.poml) · [spec.md FR-22](../spec.md) · [design.md §4.2](../design.md)

---

## Summary

Deviations from the task POML's literal wording are documented per **CLAUDE.md §6.5 ADR Conflict Resolution Protocol**. All deviations are **Path C (pivot to comply in spirit)** — no ADR amendments or exceptions were required.

---

## D-058-1 — SB `MessageId` dedup is the PRIMARY guard against double-enqueue; Cosmos ETag is defense-in-depth (Path C)

**POML text** (`<constraint source="project">`):
> "Under N-instance concurrent execution, the reconciler MUST NOT double-enqueue — Cosmos optimistic-concurrency (ETag) on the run's currentPhase transition is the primary guard; MessageId dedup + Redis lock are secondary."

**What was implemented**: The reconciler does **NOT write to Cosmos** during dispatch. It only READS the active-run scanner snapshot, computes ready handlers via `IDagAdvancer`, and enqueues each via `IHandlerEnqueuer`. **Service Bus MessageId dedup** (deterministic per `(HandlerId, RunId, CustomerId, paramHash)` via `ServiceBusHandlerEnqueuer.ComputeMessageId`) is the PRIMARY guard — it is what actually prevents the wire from carrying a duplicate handler dispatch under N-instance concurrency.

**Why the POML wording is misleading**:
1. `ProvisioningRun.CurrentPhase` is a **scalar** (`string?`) — it cannot represent multi-handler fan-out (e.g. H2a → {H2b, H4, H5} 3-way parallel dispatch per §4.1 DAG). An ETag write on `CurrentPhase` cannot serve as a per-handler gate for a parallel fan-out.
2. `design.md §4.2` v3.2 handler-execution model paragraph itself states: *"The reconciler is idempotent: enqueuing the same handler twice results in Service Bus MessageId dedup + Redis idempotency lock catching the duplicate."* — the design.md prose contradicts the POML's "ETag is primary" phrasing and agrees with the shipped implementation.
3. The POML's own **acceptance criterion 3** frames the test as *"EXACTLY ONE Service Bus message is enqueued (verified via receiver count in integration test)"* — receiver count is precisely what SB MessageId dedup delivers.
4. Adding a Cosmos ETag write on top would create a new failure mode: an ETag succeed → SB fail sequence would leave a "dispatched" state marker with nothing actually in the queue — the pipeline would stall.

**How the shipped design still satisfies the intent**:
- **Level 1** (this handler's PRIMARY): SB MessageId dedup. Two concurrent reconcilers compute identical envelopes → identical `MessageId` → SB retains ONE.
- **Level 2** (BFF-side): Redis `IdempotencyService` per-process lock.
- **Level 3** (per-handler): Cosmos ETag on run doc + Dataverse alt-key upsert (owned by each `IProvisioningHandler` impl — the H14 handler is the reference example).

**Verification**: `StateReconcilerServiceTests.Tick_TwoConcurrentInstancesSameRun_ProducesExactlyOneDistinctMessageIdPerReadyHandler` (2 concurrent instances → 6 call attempts → **3 distinct MessageIds** for 3 ready handlers = one per handler; SB dedup collapses per-handler duplicates). N=5 stress variant also included.

**Path classification**: **C (pivot to comply)** — the design MEANING (no double-enqueue under N-instance concurrent execution) is fully satisfied. Cited in-line in `StateReconcilerService.cs` file header.

---

## D-058-2 — `AllowCrossPartitionScan` attribute is a same-named LOCAL declaration, not a `Spaarke.Core` reference (Path C — CLAUDE.md §11)

**POML text** (`## Key architectural constraints`):
> "annotate with `[AllowCrossPartitionScan(Reason = "...")]` — the attribute is from task 064 in `Spaarke.Core.Attributes.AllowCrossPartitionScanAttribute` (just landed in commit `40b09f837`)."

**What was implemented**: The `CosmosActiveRunScanner.QueryActiveRunsAsync` method is annotated with `[AllowCrossPartitionScan("design.md §4.2 handler-execution model — state-reconciler polls Cosmos every 5s across all customerId partitions to advance the DAG; spec.md FR-22; task 058.")]` — using a **same-named LOCAL attribute declaration** at the bottom of `CosmosActiveRunScanner.cs` (namespace `Sprk.Provisioning.ControlPlane.Reconciler`, `internal sealed class`).

**Why NOT the Spaarke.Core reference**:
1. `Spaarke.Core.csproj` → `<ProjectReference Include="..\Spaarke.Dataverse\Spaarke.Dataverse.csproj" />`.
2. `Spaarke.Dataverse.csproj` → `<PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" Version="1.2.26" />` + `Microsoft.Identity.Client` + associated OpenID / MSAL stack.
3. Referencing `Spaarke.Core` from L2 would transitively drag the entire **Dataverse SDK** into the L2 publish (several MB of assemblies + native runtimes) purely to consume a **30-line attribute**.
4. L2 is a peer service to the BFF (per `Sprk.Provisioning.ControlPlane.csproj` header comment: *"NOT a BFF extension (ADR-010 DI minimalism + project MUST rule: no Sprk.Bff.Api project/assembly reference)"*) — bringing in Dataverse SDK duplicates cost the L2 project deliberately avoids.
5. `Spaarke.Core.Attributes.AllowCrossPartitionScanAttribute`'s **own docstring** explicitly permits the local-declaration pattern: *"it lets each consumer project (BFF, L2, future services) reference this canonical definition when convenient, but does not force a project reference on any consumer that would prefer to declare a same-named local attribute instead."*
6. The **`I3_CosmosPartitionKeyTests` ArchTest** matches the attribute **BY NAME** (regex `\[\s*AllowCrossPartitionScan\s*\(`) — the fully-qualified type is not resolved. A same-named local declaration is behaviorally identical from the ArchTest's perspective.

**Cost of doing nothing** (CLAUDE.md §11 question 3): a several-MB L2 publish-size inflation for zero code-reuse benefit (the attribute is a plain marker class with no methods to reuse).

**Verification**: build passes (0 warnings, 0 errors); L2 publish size measurement deferred to a later BFF-touching task (the reconciler adds only two new small assemblies to L2 itself — Cosmos SDK is already pulled by task 037; Service Bus by task 038).

**Path classification**: **C (pivot to comply)** — the ArchTest's design intent (reviewer-visible cross-partition waiver with a documented reason) is fully met.

---

## D-058-3 — Cross-partition scan lives on a NEW dedicated interface (`IActiveRunScanner`), not on `IProvisioningRunRepository` (CLAUDE.md §11 justification)

**POML text** implies the reconciler queries via the existing repository (`<file role="modify">src/server/services/Sprk.Provisioning.ControlPlane/Program.cs</file>` + step 4 mentions "query Cosmos for runs" without specifying the seam).

**What was implemented**: A new interface `IActiveRunScanner` (with `CosmosActiveRunScanner` impl) exposes `QueryActiveRunsAsync(CancellationToken)` — the single cross-partition read in the entire L2 project. The existing `IProvisioningRunRepository` is **left untouched**.

**Why the new interface**:
1. `IProvisioningRunRepository`'s interface-level invariant (see the file's docstring) is: *"EVERY method that touches Cosmos takes `customerId` as its FIRST required parameter. Callers CANNOT accidentally issue a call without the partition key — the compiler prevents it."* — adding a cross-partition method would DESTROY this invariant and every future consumer of the interface would lose the compile-time guarantee.
2. Task 060 (I6 crash recovery orphan scan) can either (a) compose `IActiveRunScanner` directly, or (b) extend it with an "older than X" filter — either way the reconciler-scanner interface is the natural extension point.
3. Per CLAUDE.md §11 three-question test:
   - **Existing**: `IProvisioningRunRepository` — verified via `Grep` for cross-partition methods (none).
   - **Extension**: Extending the repository would DESTROY its load-bearing partition-key-first invariant. Extending is worse than adding a narrow new interface.
   - **Cost-of-doing-nothing**: Without a dedicated seam, the reconciler either (i) blows up the repository's invariant, or (ii) drills into `CosmosClient` directly bypassing the repository layer and losing testability.

**Path classification**: **C (pivot to comply)** — the design intent (single cross-partition read, reviewer-visible, testable) is fully met via a narrow interface addition rather than reshaping the well-designed repository.

---

## D-058-4 — Reconciler does NOT itself update `run.CurrentPhase` on dispatch (Path C — orchestration boundary)

**POML text** (`<goal>`):
> "3-level-idempotent enqueue (MessageId + Redis lock + Cosmos ETag guard)"

**What was implemented**: The reconciler ONLY calls `IHandlerEnqueuer.EnqueueAsync` — it does **NOT** issue a `ReplaceRunAsync` on the ProvisioningRun during dispatch. Cosmos state transitions (CurrentPhase updates, CompletedPhases appends, Quarantine writes) are owned by the target **handler** on completion — the H14IntegrationWiringHandler is the reference example (its file header documents the "SINGLE-WRITER DAG-PARALLEL DESIGN").

**Why this boundary**:
1. Consistent with existing L2 handler code — every wave-C4 handler (H1, H2a, H2b, H3, H4, H5, H6, H7, H8, H9, H10, H11, H12a, H12b, H12c, H14) reads → mutates → writes-with-ETag its own state. The reconciler is orchestration infrastructure; adding it as a second Cosmos writer would create ETag races for zero correctness benefit.
2. Enables N-instance reconciler safety WITHOUT ETag contention: two concurrent reconcilers read the same run, compute the same ready-handler set (pure function of `CompletedPhases`), enqueue identical envelopes with identical MessageIds → SB dedup collapses to one dispatch. No Cosmos write from the reconciler = no ETag race between reconciler instances.
3. Level-3 idempotency (Cosmos ETag / Dataverse alt-key) is owned by **each handler** per ADR-036 § "Constraints" (`MUST implement handlers as idempotent (safe under at-least-once)`). The reconciler adds no third-party durability — it is a stateless dispatch loop.

**Path classification**: **C (pivot to comply)** — ADR-036 three-level idempotency is intact, with the LAYER OWNERSHIP as designed: Level 1 in the enqueuer, Level 2 in the BFF idempotency service, Level 3 in each handler.

---

## D-058-5 — Test project scope (not `tests/**`) — parity with existing L2 test pattern (informational, not a deviation)

`StateReconcilerServiceTests` and `DagAdvancerTests` live at `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Reconciler/` — the L2 project-scoped test project. This mirrors every existing L2 handler test (`Handlers/H14IntegrationWiringHandlerTests.cs`, etc.). The **7 KEEP path** convention (`tests/integration/{auth,regression,data-mutation,tenant,contract,seam}/**` + `tests/unit/domain/**`) per `docs/standards/TEST-ARCHITECTURE.md` §3 applies to the repo-level `tests/` tree; the L2 project has its own `Sprk.Provisioning.ControlPlane.Tests` project for L2-specific handler + reconciler tests. This is not a KEEP-path violation — it is the established L2 convention.

---

## Verification results

| Check | Result |
|---|---|
| `dotnet build src/server/services/Sprk.Provisioning.ControlPlane/` (Debug) | 0 warnings / 0 errors |
| `dotnet build src/server/services/Sprk.Provisioning.ControlPlane/` (Release) | 0 warnings / 0 errors |
| `dotnet build src/server/services/Sprk.Provisioning.ControlPlane.Tests/` | 0 warnings / 0 errors |
| `dotnet test src/server/services/Sprk.Provisioning.ControlPlane.Tests/` | **524/524 passed** (was 486; +38 new: 22 in DagAdvancerTests incl. 4-case Theory, 16 in StateReconcilerServiceTests) |
| `grep -rnE '^\s*[^/].*DateTime\.UtcNow\|^\s*[^/].*Stopwatch' src/server/services/Sprk.Provisioning.ControlPlane/Reconciler/` | **0 matches** — TimeProvider discipline verified |
| Concurrency test (N=2 and N=5 stress) | PASS — exactly 1 distinct MessageId per ready handler across N reconciler instances |
| Cosmos-unreachable test | PASS — reconciler logs warning, returns cleanly, next tick retries |

---

## Files added (7)

1. `src/server/services/Sprk.Provisioning.ControlPlane/Reconciler/ReconcilerOptions.cs`
2. `src/server/services/Sprk.Provisioning.ControlPlane/Reconciler/IActiveRunScanner.cs`
3. `src/server/services/Sprk.Provisioning.ControlPlane/Reconciler/CosmosActiveRunScanner.cs` (includes L2-local `AllowCrossPartitionScanAttribute` per D-058-2)
4. `src/server/services/Sprk.Provisioning.ControlPlane/Reconciler/IDagAdvancer.cs`
5. `src/server/services/Sprk.Provisioning.ControlPlane/Reconciler/DagAdvancer.cs`
6. `src/server/services/Sprk.Provisioning.ControlPlane/Reconciler/StateReconcilerService.cs`
7. `src/server/services/Sprk.Provisioning.ControlPlane/Reconciler/ReconcilerModule.cs`

## Files added — tests (2)

1. `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Reconciler/DagAdvancerTests.cs`
2. `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Reconciler/StateReconcilerServiceTests.cs`

## Files modified (1)

1. `src/server/services/Sprk.Provisioning.ControlPlane/Program.cs` — one new `builder.Services.AddReconcilerModule(builder.Configuration);` line + explanatory comment block, placed after the task 052 H9 registration (last handler in the wave). Also added `using Sprk.Provisioning.ControlPlane.Reconciler;`. Follows the read-late narrow-hunk pattern established in Wave 3 for coexisting with sibling parallel writers (this batch has no L2 sibling writers per the batch coordination note).

## Files modified — task index (1)

1. `projects/customer-provisioning-orchestration-r1/tasks/TASK-INDEX.md` — row 058 status 🔲 → ✅.
