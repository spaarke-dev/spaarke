# ADR-043 Impact Review — spaarkeai-compose-r2 (closes obligation O6)

> **Date**: 2026-07-09 · **Author**: compose-r2 (Ralph Schroeder) · **Reviewed artifact**: ADR-043 "AI Capability Execution Spine" (Proposed) + Phase-E built code on master (`bad013d1d`).
> **Method**: four parallel read-only investigations (component-model, sequencing/deploy, handoffs/obligations, integration-risk) + first-hand read of ADR-043 (both versions), `IContextBinder`/`ContextBinder`/`PromptInputSection`, and `SessionDispatchOrchestrator` gates (verified file:line + git).
> **Purpose**: satisfy the ack-committed ADR-043 review; record how the spine impacts Compose's architecture, component model, and deployment; enumerate the integration risks + the wait/verify list.

## Verdict

ADR-043 is **sound and correct for Compose** — it fixes exactly the three foundation gaps our 2026-07-09 assessment surfaced, was authored *from* that assessment, and names compose-r2 as its promotion gate. **No architecture or component-model conflict.** The exposure is **timing + our own tracking**, not design.

## 1. Architecture — no conflict (HIGH confidence)
- One `ContextBinder` resolves two roles: grounding **CONTEXT** → `ContextEnvelope` (frozen, unchanged); **OPERAND** → the `## Input` channel. This is the resolution of the operand-home gap we flagged (core chose option **(b)** — operand is a per-turn `## Input` channel, NOT an envelope slice). Our 5 actions already declare their operand via `sprk_inputschema`, so they are **already consistent**.
- One `DispositionRoutability` registry collapses the 3 drift-prone disposition lists (E-20).
- Deterministic `ActionKind` + supersession-write leg is the sanctioned home for FR-17 undo (E-30).
- Two-completion-engine convergence onto the binder; agent-loop tool spine stays separate (correct, R8+).

## 2. Component model — our worry was unfounded (HIGH confidence)
- **"LinearConsumers"** = an existing folder (`Services/Ai/LinearConsumers/`) that already holds `ActionRunner`. Not a rename, not a new component. Binding *resolution* stays in `IConsumerRoutingService` (an earlier "supersede resolution" framing was retracted).
- **"R1 AI dispatch retirement"** deleted **Compose R1's own redundant plumbing** (a local `ComposeEndpoints.DispatchAction`, `IDocxTextExtractor`, `ComposeDocumentService`, …) — it removed a *competitor* to the shared dispatch, not the shared dispatch.
- Our chain — client `dispatchConsumer` → `POST /dispatch` → `SessionDispatchOrchestrator` → `ActionRunner` — is **intact and canonical; no symbol we import is renamed.** ADR-043 enhances `ActionRunner` internals *behind its existing interface*.

## 3. Deployment constraints we must respect
- **ConsumerTypes ↔ Binding-row parity is a hard `/healthz` gate.** `RoutingConsumerTypeHealthCheck` flips the whole BFF **Unhealthy** on drift → task 047 must seed Binding rows **atomically** with their `ConsumerTypes` constants.
- **`sprk_disposition` must carry `compose=100000006`** before any compose Binding deploys.
- **GitOps Model-1 seeds** — author through seed JSON, never hand-edit live rows.
- No new Phase-E signing key. Our `Compose:Webhook:SigningKey` (DEF-03 / #602) is unrelated and still required for FR-26.

## 4. Current-state finding — the compose slice is 422-broken on master TODAY
Verified against code + git (not narrative):

| Gate | `SessionDispatchOrchestrator.cs` | State | Fixed by |
|---|---|---|---|
| **Disposition admit-gate** | `:229` — `is not (Informational or WorkProduct)` → 422 | ❌ **NOT fixed** (Compose never added; `git log -S "BindingDisposition.Compose"` on this file = empty; line last touched by a WorkProduct commit `37ae4b6be`) | **E-20** (🔲 not started) |
| **File-fetch hard-stop** | `:276` — now branches on `_contextBinder.HasStructuredOperand(...)` | ✅ **FIXED** (E-10 landed) — args-text actions skip the session-file fetch | E-10 ✅ |
| **ActionKind gate** | `:214` — `!= Prompted` → 422 | ❌ not fixed, but **only** blocks FR-17's no-LLM retraction (all 5 actions are Prompted → unaffected) | **E-30** (🔲) |

**Precise readiness:**
- ✅ The **4 Informational actions** (explain / compare / summarize-changes / defined-terms) dispatch end-to-end **now** (E-10 fixed their input path; disposition+kind already passed).
- ❌ **draft-alternative** (compose disposition) → **422 at the admit-gate until E-20**.
- ❌ **draft-alternative** dispatch → 422 at the admit-gate until **E-20**.
- ⚠️ **FR-17 undo (034)** → gated on **E-20 ONLY** (CORRECTED by core, REPLY §2): the supersession-write mechanism is **already shipped in Phase A0** (`ComposeDisposition.BuildFrame`/`ResolveCurrent`, locked by `ComposeDispositionContractTests`) — **not** E-30. E-30's deterministic `ActionKind` is for coded chat-loop actions, orthogonal. Build 034's write mechanism against the A0 contract now; it dispatches once E-20 admits `Compose`.

**Our routing promotion is half-landed / already drifting:** we updated `OutputRouter` case + `ToLedgerValue` (2 of 3 lists) but not the admit-gate (3rd). The OutputRouter compose leg is therefore **dead code on the dispatch path** until E-20. Core has **accepted** the promotion; E-20 will **delete** our 2 hand-added switch entries and fold them into the registry — so those are transitional (the enum member stays).

## 5. Integration risks (ranked)
1. **[HIGH]** Compose vertical slice 422-broken on master; the fix (E-20) is not started. Every compose-disposition dispatch task fails end-to-end until E-20.
2. **[HIGH]** False "done" on **016** (and 033/042 framing) — verified only at the router/unit layer, 422 through the full seam. Exactly the failure E-40's vertical-slice KEEP test exists to catch.
3. **[MED]** Merge-collision hazard on `OutputRouter.cs` / `Binding.cs` before E-20 → **freeze compose edits to those two files**.
4. ~~**[MED]** 034 (FR-17 undo) scoped against an undecided contract~~ **RESOLVED by core (REPLY §2)**: 034's supersession mechanism is already shipped (A0); it is gated on **E-20 only**, not E-30. Buildable against the A0 contract now; dispatches once E-20 lands.
5. **[LOW-MED]** 047 deploying the compose Binding row adds live drift → gate the compose row on E-20; the 4 informational rows are safe.

## 6. Actions taken / open
- **Tracking corrected**: 016/033/042 annotated "shipped-but-gated-on-E-20 (not end-to-end)"; project CLAUDE.md "UNBLOCKS 042/033/034" overstatement corrected.
- **O3 seam test**: new task authored (consumer-side vertical-slice `tests/integration/seam/**`), gated on E-20/E-40.
- **Re-ping core**: `HANDOFF-to-core-e20-timing-and-forkc-reping.md` — E-20 landing window, E-30/Move-3 mechanism decision, Fork-C profile facade (unanswered), and confirmation core owns the disposition-list collapse.

## 7. Verify with core before resuming the dispatch chain
1. **E-20 landing date** + that `DispositionRoutability` admits `Compose` as both *routable* AND *dispatch-admittable*.
2. **E-30 / Move-3 mechanism** decision before 034.
3. Core owns E-20's collapse of our 2 switch entries — compose contributes nothing further there.
4. E-40's seam test lands before we re-run flagship 082.

## 8. Compose commitments (unchanged, on track)
Consume-as-published; zero new AI dispatch endpoints; be the forcing consumer for the input seam (E-10) — validate the 4 informational actions end-to-end now; add the consumer-side vertical-slice seam test on E-40's category; owner-hygiene (no version suffix).
