# HANDOFF → redesign-r2 (core): AI Capability Execution Foundation

> **From**: spaarkeai-compose-r2 (Ralph Schroeder) · **To**: spaarke-ai-architecture-redesign-r2 (platform core)
> **Date**: 2026-07-09 · **Status**: core has ACCEPTED ownership of the fix.
> **Full analysis**: [`docs/assessments/spaarke-ai-capability-execution-assessment-2026-07-09.md`](../../../docs/assessments/spaarke-ai-capability-execution-assessment-2026-07-09.md) (platform R/P/S + governance). **Validation detail**: [`compose-dispatch-foundation-validation.md`](compose-dispatch-foundation-validation.md).
> **Purpose**: hand core (a) the Spaarke-wide problem/solution in brief, and (b) the exact Compose consumer requirements — so the foundation ships Compose-consumable *and* right for the long-term capability. Same spirit as the A0 contract-requirements handoff.

---

## Part A — Spaarke-wide (brief)

**Thesis**: Spaarke advertises one declarative AI-capability contract (Action + Binding + input schema + disposition), but the **canonical execution spine realizes only a narrow slice of it**, and governance owns contract *shapes*, not execution *wiring* — so the gap is invisible and unowned. Three structural findings:

1. **Two disjoint capability spines.** The declarative disposition spine (ADR-039 catalog → `OutputRouter`) only ever realized *informational* + two persistence legs. **Every real side effect** (record/email/notification/overlay/memory-write) lives on a *parallel* agent-loop tool-handler spine (`side_effect_class`). ADR-039's "one dispatch protocol" is half-built.
2. **The canonical engine is the input-poorest, and the intended unifier is unbuilt.** `ActionRunner` (the ADR-039-canonical engine) accepts document text and nothing else. `PromptSchemaRenderer` is a superset seam it under-uses (it ignores `runtimeInput`). The designed general input model — `ContextEnvelope` + `ContextBinder` — is a frozen v1 contract with **`ContextBinder` not built** (core task 053, not-started) and consumed by nothing on the hot path.
3. **Disposition capability is triplicated + drift-prone**, and orphaning is systemic. Admit-gate / router-switch / `ToLedgerValue` are three hand-maintained lists; the compose routing promotion updated two and left the admit-gate un-widened (the live 422). Governance tracks contract shapes not wiring → the promotion *"fell through the cracks"* (both teams confirmed), and a **second live orphan sits in the same seam** (SEAM-STATUS: gate-wiring UNASSIGNED). `FAILURE-MODES.md` AP-2/AP-4 are the same class.

**Solution shape** (detail in the assessment) — **NOT "unify the two engines"** (ADR-039 keeps them split; unify = R8+). Instead:
- **Move 1** — wire the canonical engine to the platform input-resolution model (min: `runtimeInput`; target: `ContextEnvelope`/`ContextBinder`).
- **Move 2** — single-source disposition capability (one registry; admit follows "router can route it").
- **Move 3** *(the fundamental call)* — reconcile the two-spine boundary; decide where **interactive/deterministic** capabilities (compose edit + retract) live. Compose recommends a distinct **document-mutation capability class**, but this is core's ADR decision.
- **Governance** — extend the proven CLAUDE.md §10/§11 template to the execution engine: a **named engine owner** (core), a **vertical-slice KEEP test category** (`tests/integration/seam/**`: consumer → dispatch → stored `SessionOutput` → render), and a **deferral re-parenting rule** (deferred cross-cutting slices filed against an owning task).

---

## Part B — What Compose requires from the foundation

Compose consumes these **as published — no local variants** (charter §3.4); **no new AI dispatch endpoints; no string-key routing** (ADR-039 §7.2). Split into what compose-r2 needs *now* vs. what the *ultimate* Compose capability needs, so core can sequence.

### B1. Compose disposition dispatchable end-to-end *(now)*
A Binding declaring `sprk_disposition = compose` (100000006) must dispatch through the **shipped seam on the Click AND Text/loop paths** (not only the Event path) → executor runs → `OutputRouter` compose case (already built) → `SessionOutput` written → `ComposeDispositionFrame` emitted. Requires: the admit-gate widened/single-sourced (Move 2) so the three lists can't drift. *Already on master (compose did these): `BindingDisposition.Compose`, `ToLedgerValue`, the `OutputRouter` pass-through case.*

### B2. Selection / open-document input on the canonical seam *(now — the load-bearing one)*
All 5 compose actions take **args-text, not uploaded files**. The engine must resolve these without requiring a session file (relax the no-file hard stop):

| Action | Input field | Source |
|---|---|---|
| compose-explain-clause | `selectionText` | selected clause |
| compose-compare-to-playbook | `selectionText` | selected clause |
| compose-draft-alternative | `selectionText` | selected clause (edit-producing → compose disposition) |
| compose-summarize-word-changes | `changesText` | DocxAnnotationReader output |
| compose-defined-terms | `documentText` | open-document text |

**Minimum acceptable**: forward dispatch args → `PromptSchemaRenderer.Render` as `runtimeInput` (`## Input` section) — the mechanism already exists and the playbook engine already uses it. **Target**: these become `ContextEnvelope` slices via `ContextBinder`. Compose is happy to be the **forcing consumer** that first wires input-resolution to the canonical engine.

### B3. Deterministic action kind / sanctioned supersession-write *(needed for FR-17 undo — near-term)*
Owner has chosen **durable undo (Path B)**: "undo that" writes a **superseding "retraction" compose output** so a refresh doesn't re-materialize the suggestion. Two blockers for core to resolve (this is Move 3's decision): the **ActionKind gate** (`SessionDispatchOrchestrator.cs:209`) rejects non-prompted actions, and a retraction has no LLM output. Compose needs *either* a deterministic action-kind admitted through the seam, *or* a sanctioned compose-supersession write that rides the same execute→route leg. "Replace" (try another approach) needs nothing special — it's a re-dispatch of draft-alternative once B1/B2 land.

### B4. Supersession semantics honored through dispatch *(now)*
ADR-040 highest-turn-wins must hold end-to-end: re-dispatch → higher-turn compose output supersedes prior; retraction → superseding "empty" output. The **client already re-materializes from current ledger state** (shipped: `usePendingRedline`, task 033) — it only needs the WRITE path (B1–B3) to produce those entries.

### B5. ConsumerTypes registration + health parity *(deploy gate)*
The 5 compose consumer types are **not** in `ConsumerTypes.cs`. Not needed for dispatch resolution (resolves by GUID), **but** the boot-reconciliation health check flips `/healthz` **Unhealthy** when Dataverse rows lack matching constants — a deploy gate for compose's catalog rows (047) and the flagship gate (082). ~10 lines; must land before/with the catalog deploy. Core's call whether this is foundation or compose work.

### B6. Vertical-slice acceptance bar *(process — Compose requests this)*
Please make the foundation's definition-of-done a **vertical-slice test**: a real compose action → dispatch → compose `SessionOutput` → `ComposeDispositionFrame`. That single test is the exact thing whose absence let 016/042 be "done" while 422-broken. Compose will add its own consumer-side vertical-slice test on top.

### Ultimate vs. now
- **Compose-r2 immediate**: B1 + B2 (via `runtimeInput` minimum) + B3 + B5 unblock the flagship (draft→pending→accept/reject→undo/replace) and the 4 read-only actions.
- **Ultimate Compose capability**: B2 via full `ContextEnvelope` (selection **plus** matter/entity context, prior-ledger outputs, and durable memory as input — e.g. "draft an alternative grounded in this matter's playbook + prior turns + org memory"), and Move-3's document-mutation class as the durable home for interactive edits. If core ships the `runtimeInput` minimum first, Compose accepts a later migration to the envelope — please flag the sequencing so we don't build against a shape that changes.

---

## What Compose commits in return
- Consume the foundation **as published**, no local variants; dispatch only through the shipped seam; zero new endpoints.
- Re-scope the Compose chain to consume the foundation: **016/046/034/047** become consumers of core's leg; **042 rows / 045 eval / 033 client redline / 061 ledger query** are already authored correctly and just need the runtime foundation beneath them.
- Ship eval cases (golden + dispatch ≥5/row — done, task 045) **and** a consumer-side vertical-slice test.
- Keep the owner-hygiene rules (no version suffix in action codes / Binding names / mirror filenames).

## What Compose asks of core
1. Confirm B1–B6 (or negotiate deltas) and publish which are foundation vs. compose work.
2. Sequence B2 minimum (`runtimeInput`) vs. target (`ContextEnvelope`/`ContextBinder` task 053) and tell us the migration boundary.
3. Decide Move 3 (where compose edit + retract live) — it gates B3.
4. Name the owner + intake for the shared execution engine so this can't re-orphan.

*Source of truth: the platform assessment + validation note linked above. Contact: Ralph Schroeder.*
