# ADR-043: AI Capability Execution Spine (Concise)

> **Status**: **Proposed** (2026-07-09, Phase-E kickoff of `spaarke-ai-architecture-redesign-r2`).
> **Promotion**: → **Accepted** at the Phase-E gate (foundation shipped + `tests/integration/seam/**` green + compose-r2 dispatching end-to-end). Do NOT mark Accepted before the gate.
> **Domain**: AI platform — the execution engine that realizes the ADR-039 catalog contract.
> **Builds on**: ADR-039 (one dispatch protocol, closed catalogs) · ADR-040 (ledger) · ADR-041 (gate + OutcomeCard)
> **Why this ADR exists**: the canonical execution engine realized only a narrow slice of the declarative catalog contract (input=files-only, disposition=2-of-6, kind=Prompted-only) and there were TWO redundant completion engines with the canonical one weaker. Governance owned contract *shapes*, not execution *wiring*, so the gap orphaned (the compose 422). Verified in code 2026-07-09.

---

## Decision

The execution spine = **three surfaces converging at one disposition→ledger→render layer**, fed by **one input model**, governed by **one disposition registry**, with a home for deterministic capabilities.

## Constraints

### ✅ MUST
- **MUST** resolve a completion's inputs via **one `ContextBinder`** into TWO distinct roles: grounding **CONTEXT** → `ContextEnvelope` slices (stable platform-standard shape, frozen task-015, unchanged); the Action's `sprk_inputschema`-typed **OPERAND** (`selectionText`/`changesText`/`documentText`/`ledger_resolution`) → the **`## Input` channel**. The operand is NOT an envelope slice (volatile + per-action-typed vs. the envelope's stable standard shape). Realizes ADR-040 read-by-reference. No engine reads hardcoded session state.
- **MUST** make **`## Input`** (PromptSchemaRenderer Layer 2) a **single-source producer** — all consumers (ActionRunner, node engine, and existing hand-replicas e.g. `DailyBriefingNarrator`) converge on it; its output format is a frozen contract with a golden `tests/integration/seam/**` assertion. Parity structural, not conventional.
- **MUST** converge the two redundant *completion* engines (`ActionRunner` + `AiCompletionNodeExecutor`) onto that one input-resolution model. One completion primitive serves dispatch Actions AND playbook nodes. **Design the shared seam against ALL consumers upfront** (anti-recurrence — do not fit it to the first consumer then retrofit); migrate in risk-isolated waves.
- **MUST** single-source disposition capability in one **`DispositionRoutability`** registry — admission = "can `OutputRouter` route it?" The admit-gate + router switch + `ToLedgerValue` follow the registry (collapse the 3 hand-maintained lists).
- **MUST** converge ALL surfaces at **disposition → `OutputRouter` → ledger (`SessionOutput`) → `OutcomeCard`** (ADR-040 store-before-render; ADR-041 OutcomeCard) — one rendering/persistence contract.
- **MUST** admit deterministic/interactive capabilities (compose edit, retraction) through the declarative spine via a **deterministic `ActionKind`** + a sanctioned **supersession-write leg** (retraction = superseding empty output, no LLM).
- **MUST** keep disposition + action-kind as **catalog-declared DATA** (ADR-039); no runtime model-judged disposition/kind.
- **MUST** gate a dispatch/execution change on a **`tests/integration/seam/**` vertical-slice test** (consumer → dispatch → stored `SessionOutput` → render/frame) — the definition-of-done. A green contract-shape test is NOT sufficient.
- **MUST** have a **named engine owner + intake** for the shared execution spine; deferred cross-cutting slices re-parent to an owning task.

### ❌ MUST NOT
- **MUST NOT** unify the agent-loop tool spine (LLM calling tools mid-turn) into the completion engine — that split is legitimate (interactive tool use ≠ linear completion); out of scope (R8+).
- **MUST NOT** ship a transitional `runtimeInput`-only input path and migrate later — build against the `ContextEnvelope` contract directly (no straddle).
- **MUST NOT** create a third parallel spine for deterministic/interactive capabilities — use the deterministic `ActionKind` on the declarative spine.
- **MUST NOT** assume "one turn = one dispatch" — the spine must not foreclose the future multi-step Action Engine (see below).

## Forward-compatibility — multi-step "Action Engine" (reserved, NOT built here)

A future multi-step LLM-orchestrated agent sequences these surfaces across turns. Operator-confirmed inputs (2026-07-09): (1) **hybrid authorization** — autonomous low-risk / confirm high-risk, via per-step risk to the ADR-041 `ConfirmationPolicyEngine` (its full tier×risk×origin capacity, retained by task 044, is the substrate); (2) **closed-catalog-bound** — composes cataloged Actions+Bindings, authors no novel actions; (3) **ledger-resident plan** — the plan lives in the ADR-040 ledger; (4) **framework-agnostic** — Microsoft Agent Framework a candidate, not a commitment. Every Phase-E move is substrate the Action Engine needs; none forecloses it.

## Integration
ADR-039 (dispatch, closed catalog, `side_effect_class`) · ADR-040 (ledger, `ledger_resolution`, store-before-render) · ADR-041 (gate + OutcomeCard convergence) · ADR-015 (`ContextEnvelope` slice / Tier mapping) · ADR-038 (adds the `seam/**` KEEP category). Realized by Phase E: E-00 (this ADR), E-10/E-11/E-12 (ContextBinder + engine convergence), E-20/E-21 (disposition registry + router legs), E-30 (deterministic kind), E-40/E-41/E-42 (governance + ConsumerTypes).

**Full ADR**: [docs/adr/ADR-043-ai-capability-execution-spine.md](../../docs/adr/ADR-043-ai-capability-execution-spine.md)
