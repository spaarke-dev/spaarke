# REPORT → redesign-r2 (core): Compose forcing-consumer validation PASSED — feeds your ADR-043 promotion gate

> **From**: spaarkeai-compose-r2 · **To**: spaarke-ai-architecture-redesign-r2 (core) · **Date**: 2026-07-09
> **Re**: the forcing-consumer validation you asked us to run after E-20 (`REPLY-to-compose-r2-e20-e30-forkc.md` + your ADR-043 §5 definition-of-done). **Result: green.**

## TL;DR
E-20 (`DispositionRoutability` single-source registry) unblocked compose dispatch **end-to-end through the real seam** — not just the router/unit layer. The compose forcing consumer now dispatches, routes, stores, and renders for real. **Your ADR-043 promotion gate's consumer-side evidence is satisfied.** No action required from you on this; two open loops (Fork-C, E-40) noted at the bottom.

## 1. What we validated (the false-green is gone)
Before E-20 the admit-gate at `SessionDispatchOrchestrator.cs` admitted only `Informational | WorkProduct`, so a `Compose`-disposition dispatch **422'd before reaching** the OutputRouter compose leg — our routing promotion had widened 2 of the 3 lists, the admit-gate was the un-widened 3rd. The unit/router tests were green while the seam was broken (the exact "shipped done at the contract-shape layer while the seam is red" failure ADR-043 §5 targets).

Post-E-20, `DispositionRoutability.IsAdmissible ⇔ IsRoutable` and `Compose` is `Routable=true`, so the admit-gate follows the registry. Verified:

| Evidence | What it proves | Result |
|---|---|---|
| `tests/integration/seam/Ai/DispositionRoutabilitySeamTests.DispatchAsync_ComposeDisposition_Admits_Routes_Stores_AndRenders` | **REAL** `SessionDispatchOrchestrator` + `ContextBinder` + `ActionRunner` + `OutputRouter` (only the LLM + catalog boundaries doubled): admit (no error frame) → route (pass-through) → store (ledger `"compose"`) → render (terminal `complete`). This IS the consumer-side vertical-slice seam. | ✅ |
| `tests/integration/contract/Api/Ai/ComposeDispositionContractTests` | ComposeDisposition v1 frame shape (ledger_ref not payload; store-before-render; supersession round-trip). | ✅ |
| `ComposeDraftDispositionTests` | FR-04 ledger-first consumer (BuildDraftOutput/BuildFrame/MaterializeDraft/ResolveCurrent; `{bindingId}@t{n}` provenance; fail-loud on truncation/absent-store). | ✅ |

**24/24 green.** The routing hand-patch we applied pre-E-20 was cleanly superseded by your 3-list collapse — no residue.

## 2. Note on task 084 (consumer vertical-slice seam test — the OTHER input to your gate)
Your promotion gate expected **016 re-verify + the 084 seam slice**. Heads-up: **084 is largely SATISFIED by your own `DispositionRoutabilitySeamTests`** — that suite already exercises the real admit→route→store→render slice for `Compose` (plus the loud-rejection path for the not-yet-routable dispositions and the "admission=routability for every disposition" structural invariant). We will confirm scope against ADR-043 §5/B6 before spending on 084 rather than author a duplicate. If you consider the E-20 seam suite sufficient for the gate, 084 may reduce to a scope-confirmation note.

## 3. Downstream compose work landed on the now-open seam (context, no action needed)
- **046 dispatch wiring** — landed with **ZERO new PaneEventBus discriminants**. En route we caught a **stale-POML divergence**: the 046 POML named two retracted "invented" discriminants (`compose_action_request` / `compose_edit_apply_request`) that Spike 0 + design §7.2 + `compose-contracts.ts` forbid. The agent stopped and escalated; we resolved via Path C (comply with the published contract). Dispatch trigger is the direct `dispatchConsumer` Click-path; apply leg emits the existing `workspace.compose_assistant_insert` (ledgerRef). **This is corroborating evidence that the published dispatch/ledger contract holds under a real consumer** — no new routes, no string-key routing.
- **062 cross-version persistence** — Compose sessions keyed by DocumentId+MatterId; consumes your compacted-digest mechanism (`ChatHistoryManager`) unchanged.

## 4. Residual / open loops (unchanged from prior handoffs)
- **AuditLog flake** — `AuditLogServiceTests.LogInteractionAsync_PartitionsByTenantId` still passes in isolation, fails under full-suite parallelism. Pre-existing, unrelated to compose/E-20 — flagged in `HANDOFF-to-core-e30-fixture-drift-flake.md` for your test-hygiene backlog.
- **Fork-C profile-analysis facade** — still surfaced to you in `HANDOFF-to-core-profile-analysis-facade.md`; awaiting accept-and-schedule or ack-to-own.
- **E-40 (vertical-slice-seam definition-of-done enforcement)** — the mechanism that would have caught the E-30 fixture drift before it shipped; noted for your standup.

*Contact: Ralph Schroeder.*
