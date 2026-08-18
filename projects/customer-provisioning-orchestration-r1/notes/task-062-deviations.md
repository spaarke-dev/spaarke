# Task 062 — Deviations

**Task**: [`062-load-test-l2-rest`](../tasks/062-load-test-l2-rest.poml)
**Rigor**: FULL (TEST-MODIFYING override + FR-22/SC #20 acceptance-gate scope)
**Status**: Complete — 5/5 tests pass; report at [`notes/l2-load-test-2026-08-18.md`](l2-load-test-2026-08-18.md)

---

## Deviations from the POML

### D1. Framework — chose custom xUnit harness over NBomber

**POML permitted list** (`<role>`): "BenchmarkDotNet, k6, NBomber, or dotnet-based custom harness"
**Chosen**: dotnet-based custom harness (xUnit + `Parallel.ForEachAsync` + `WebApplicationFactory<Program>` + inline percentile computation).

**Rationale**: full argument in `l2-load-test-2026-08-18.md` §1. Summary:
- Zero new package footprint beyond `Microsoft.AspNetCore.Mvc.Testing` (already in sibling test projects).
- Native fit with the in-memory seams pattern from RunsEndpointsTests (task 057).
- ADR-038 alignment straightforward — interface seams, no `Mock<HttpMessageHandler>`.
- The invariants under test are architectural (100% 202 correctness, dedup contract, no-timeout) — not statistical distributions that would benefit from NBomber's DSL.

**In-scope choice**: the POML `<role>` explicitly enumerates "dotnet-based custom harness" as a permitted framework. This is not a rule deviation — it is a choice within the permitted set.

### D2. 30-minute handler duration compressed to 3-second wall-clock

**POML acceptance criterion 2** requires "a 30-min synthetic handler enqueued via POST /api/runs receives 202 immediately; GET /api/runs/{id} eventually reports status=Completed; no HTTP timeout observed on caller."

**Chosen**: wall-clock T=3 seconds in `LongHandlerScenario.cs`. Production T=30 minutes.

**Rationale**: POML `<compression strategy>` explicitly permits this — full equivalence argument in `l2-load-test-2026-08-18.md` §2. Summary: the L2 REST invariant is DURATION-INDEPENDENT — the caller's HTTP socket closes at the 202 return, so 27-minute-57-second of extra wall-clock at production is 27-minute-57-second of NO ACTIVITY on the caller's HTTP path by definition.

**Escalation check per POML `<escalation>` trigger**: no handler exceeded 30 min without completing — the scenario framework survives at both T=3s and T=30min by construction.

### D3. Reconciler execution path — BackgroundService public surface (not internal `RunTickAsync`)

**Situation**: The task POML forbids modifying `src/server/**` (including a second `InternalsVisibleTo` entry that would grant this project access to `StateReconcilerService.RunTickAsync`, an `internal` method).

**Chosen**: drive the reconciler through its PUBLIC `BackgroundService` surface — `StartAsync` → `ExecuteAsync` → `PeriodicTimer` → tick → `StopAsync` — with `PollInterval` at its 1-second `ReconcilerOptions.Validate` floor and a 1.5-second tick-wait budget per test. Each reconciler fires at least one tick during the budget window.

**Trade-off**: three tests × ~1.5s = ~4.5s wall-clock (vs sub-second if RunTickAsync were directly callable). The alternative (reflection on the internal method) is B8-banned by `tests/CLAUDE.md`. The BackgroundService route stays within the public API and is idiomatic for hosted-service testing.

**Semantic impact**: the dedup INVARIANT holds regardless of tick count — see `l2-load-test-2026-08-18.md` §5. Tests assert on the *distinct-MessageId cardinality* (which is invariant under the number of ticks fired) and only sanity-check the total-call count as `>= expected minimum` rather than `==`.

### D4. MessageId formula reproduced (not accessed via InternalsVisibleTo)

**Situation**: `ServiceBusHandlerEnqueuer.ComputeMessageId` is `internal static`. Sibling test project `Sprk.Provisioning.ControlPlane.Tests` has `InternalsVisibleTo` and calls it directly. LoadTests cannot (would require modifying `src/server/**/*.csproj`).

**Chosen**: reproduce the SHA256 formula inline as `DedupingHandlerEnqueuer.ComputeMessageIdParity` — 5 lines documented in file header of `LoadTestFixtures.cs`.

**Drift risk**: if the production formula changes, this test-side reproduction must be updated in the same PR. No automated drift-guard exists yet. **Follow-on candidate** (out of scope for task 062): add a drift-guard test under `Sprk.Provisioning.ControlPlane.Tests` (which has `InternalsVisibleTo`) that asserts byte-for-byte parity between the two implementations. Filed as design candidate in `l2-load-test-2026-08-18.md` §10.

### D5. In-memory execution — no live Cosmos or Service Bus dependency

**POML step 8 mentions "verify Cosmos test config: dev Cosmos endpoint OR Cosmos emulator availability"**. This LoadTests project deliberately does NOT reach live infrastructure — Cosmos and Service Bus are replaced by in-memory seams above the SDK layer.

**Rationale**: the SC #20 concurrency invariant (level-1 idempotency dedup) is a property of the reconciler's OWN envelope construction, not of the wire. The `DedupingHandlerEnqueuer` in this project reproduces the production wire-level dedup exactly (identical MessageId → retained once; different → retained separately). If the in-memory dedup passes AND `ComputeMessageIdParity` byte-matches the production formula (see D4), the wire dedup passes.

**Complementary coverage**:
- Wire-level Cosmos dedup + partition-key discipline: `Sprk.Provisioning.ControlPlane.Tests.CosmosSmokeTests` (env-guarded on `COSMOS_L2_SMOKE_ENDPOINT`).
- Wire-level Service Bus dedup + MessageId determinism: `Sprk.Provisioning.ControlPlane.Tests.ServiceBusSmokeTests` (env-guarded on `SERVICEBUS_L2_SMOKE_NAMESPACE`).
- Phase F E2E acceptance (task 089) exercises the full stack against live dev infrastructure.

Task 062's LoadTests scope is the L2 REST + reconciler ARCHITECTURAL invariants, not the wire.

### D6. Report filename — used ISO-date suffix `-2026-08-18.md` not `-2026-XX.md`

**POML placeholder**: `notes/l2-load-test-2026-XX.md`

**Chosen**: `notes/l2-load-test-2026-08-18.md` (concrete YYYY-MM-DD to match sibling notes convention — see `notes/r3-handoff.md`, `notes/resource-discovery-2026-08-16.md`).

---

## Non-deviations (points where I initially considered but stayed within the POML)

### N1. Adding `Xunit.SkippableFact` for env-guarded scenarios

Considered but not needed — the LoadTests scenarios are fully deterministic (no live infrastructure dependency) so no skip-guard is needed. Sibling project `Sprk.Provisioning.ControlPlane.NightlyTests` uses `SkippableFact` because its scenarios require live Graph API access; ours do not.

### N2. Adding NBomber for percentile reporting

Considered but rejected — see D1 rationale. The 20-LOC `LatencyStatistics.Percentile` helper is sufficient.

### N3. Adding an `InternalsVisibleTo` to grant access to `RunTickAsync` + `ComputeMessageId`

Considered and rejected — the POML `What NOT to touch` list explicitly forbids modifying `src/server/**`. Chose the BackgroundService route (D3) + formula reproduction (D4) instead.

### N4. Splitting scenarios into separate test projects (per-scenario `.csproj`)

Considered but rejected — three scenarios × two collaborators each fits comfortably in one project. Sibling `Sprk.Provisioning.ControlPlane.NightlyTests` uses the same one-project-multiple-scenarios pattern.

---

## Coordination checkpoint

No other Wave 4E subagents were active when this task ran (all committed by 088 at `d98d64ec3`).
No files under `.claude/`, `src/server/**`, `.github/workflows/**`, or other test projects were touched.
Add-only in the new LoadTests project + one `dotnet sln add` to `spaarke.sln`.
