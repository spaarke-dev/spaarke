# ADR-043: AI Capability Execution Spine

- **Status**: **Proposed** (2026-07-09, authored at Phase-E kickoff of `spaarke-ai-architecture-redesign-r2`). **Promotion condition**: → **Accepted** at the **Phase-E gate** (foundation shipped + vertical-slice seam tests green). Do NOT mark Accepted before that gate. Mirrors the ADR-039/040/041 promotion-gate convention.
- **Deciders**: Operator + `spaarke-ai-architecture-redesign-r2` core, informed by the `spaarkeai-compose-r2` platform assessment (2026-07-09) + independent core code verification.
- **Concise version**: [`.claude/adr/ADR-043-ai-capability-execution-spine.md`](../../.claude/adr/ADR-043-ai-capability-execution-spine.md) (the binding MUST/MUST-NOT surface)
- **Builds on**: ADR-039 (one dispatch protocol, closed catalogs, `side_effect_class`) · ADR-040 (session ledger — the composition carrier + store-before-render) · ADR-041 (judgment/confirmation/completion — the gate + OutcomeCard)

## Context

ADR-039 promised one declarative AI-capability contract: an Action declares an input schema, an output schema, and an action kind; a Binding declares a disposition; "any Action+Binding dispatches uniformly through one protocol." ADR-040 gave the platform a ledger. But a 2026-07-09 assessment (compose-r2) plus core code verification found the **canonical execution engine realizes only a narrow slice of that contract**, and governance owned contract *shapes*, not execution *wiring* — so the gap was invisible and unowned:

1. **Two redundant completion engines, asymmetric.** `ActionRunner` (`Services/Ai/LinearConsumers/`, the ADR-039-canonical dispatch engine) accepts document text and nothing else. `AiCompletionNodeExecutor` (`Services/Ai/Nodes/`, the older playbook engine) already resolves a declarative `inputBinding` → `runtimeInput` → `PromptSchemaRenderer` `## Input`. Both are the same primitive ("resolve declared input → render prompt → LLM → structured output") built twice — and the *canonical* one is the *weaker* one.
2. **The declared contract's other dimensions are hardcoded.** The dispatch path reads only session `fileIds` (the Action's declared `selectionText`/`documentText` inputs are "accepted and ignored"); admits only `Informational` + `WorkProduct` dispositions (record/email/notification/overlay `OutputRouter` legs throw NotImplemented); rejects any non-`Prompted` action kind. The "P1/P2/P3 widen this envelope" comments are the tell — the envelope is widened by hand per phase.
3. **Disposition capability is triplicated + drift-prone.** The admit-gate (`SessionDispatchOrchestrator.cs:224`), the `OutputRouter` switch, and `ToLedgerValue` are three hand-maintained lists. The compose routing promotion updated two and left the admit-gate un-widened → a live 422. Governance tracked contract shapes, not wiring, so the promotion "fell through the cracks."

The unifying design (`ContextEnvelope` + `ContextBinder`) was specified (ADR-015 slice; envelope frozen as task 015) but **`ContextBinder` was never built** (task 053, not-started) and is consumed by nothing on the hot path. Net: the catalog looks like a general dispatch abstraction; the engine is a special-case whose abstraction leaks on every genuinely new capability shape.

## Decision

The AI capability execution spine is defined as **three execution surfaces with distinct roles, converging at one disposition→ledger→render layer**, fed by **one input-resolution model** and governed by **one disposition registry**, with a sanctioned home for deterministic/interactive capabilities.

### 1. Three execution surfaces + one convergence layer

| Surface | Role | Disposition |
|---|---|---|
| **Completion engine** (canonical) | Resolve declared input → render prompt → LLM → structured output. **ONE engine**: the two redundant completion engines (`ActionRunner` + `AiCompletionNodeExecutor`) **converge** onto one input-resolution model. Serves dispatch Actions **and** playbook nodes. | any declared |
| **Agent-loop tool spine** | The LLM calling closed-catalog tools within one bounded agent turn (ADR-039 Text path). **Kept separate** — a distinct concern (interactive reasoning-loop tool use), gated by the `ConfirmationPolicyEngine` (ADR-041). | via tool handlers |
| **Deterministic / transform surface** | Deterministic (non-`Prompted`) capabilities — transforms, retractions, supersession-writes — admitted through the declarative seam via a **deterministic `ActionKind`**. No LLM. | any declared |

**Convergence layer**: ALL three surfaces converge at **disposition → `OutputRouter` → ledger (`SessionOutput`) → `OutcomeCard`** — one rendering/persistence contract (ADR-040 store-before-render; ADR-041 OutcomeCard). This is the single point where "a capability produced an outcome" is realized, regardless of surface.

**Explicitly NOT decided here**: unifying the agent-loop tool spine into the completion engine. That split is legitimate (interactive tool use ≠ linear completion) and out of scope. Only the two redundant *completion* engines converge.

### 2. One input-resolution model — `ContextBinder` / `ContextEnvelope`

Every completion resolves its Action's declared inputs (`selectionText` / `documentText` / `changesText` / `fileIds` / `ledger_resolution`) into `ContextEnvelope` slices via **`ContextBinder`**. This realizes ADR-040's "reads by `ledger_resolution` reference — no capability reads surface/screen state." The two completion engines converge on this single resolver; `runtimeInput` / `## Input` (`PromptSchemaRenderer`) is the single rendering seam. **No engine reads hardcoded session state.** There is no transitional "args-only" input path — the envelope is the contract from the start (Binder slices ship incrementally, but the consumed contract is stable).

### 3. One disposition registry — `DispositionRoutability`

Disposition capability is **single-sourced**: admission derives from "can `OutputRouter` route this disposition?" The three hand-maintained lists collapse to one registry. Adding or realizing a disposition is one change in one place; the admit-gate and `ToLedgerValue` follow it. Drift becomes structurally impossible.

### 4. Deterministic / interactive capability home

Interactive and deterministic capabilities (e.g. compose document-edit, retraction/undo) dispatch **through the declarative spine** via a **deterministic `ActionKind`** + a sanctioned **supersession-write leg** (a retraction is a superseding "empty" output that rides the same execute→route→ledger path, with no LLM call) — **NOT** a third parallel spine. A "document-mutation capability class" is realized as a *taxonomy* over the deterministic kind + the compose disposition, not a separate engine.

### 5. Governance (prevents recurrence)

- **Named engine owner + intake**: the AI execution spine has a named owner (core / `spaarke-ai-architecture-redesign-r2`, then a durable owner) and an intake path. No execution-wiring change is ownerless.
- **Vertical-slice KEEP test category** `tests/integration/seam/**`: consumer → dispatch → stored `SessionOutput` → render/frame. This is the **definition-of-done** for any dispatch/execution change (a green contract-shape test is NOT sufficient — that is exactly how 016/042 shipped "done" while 422-broken). Added to ADR-038 KEEP paths.
- **Deferral re-parenting rule**: a deferred cross-cutting slice is filed against an owning task, never left as a shape without wiring.

## Forward-compatibility — the multi-step "Action Engine" (reserved seam; NOT built here)

A future multi-step, LLM-orchestrated agent (working name "Spaarke Claw") will sequence these execution surfaces across turns. **This ADR does not build it, but the spine MUST NOT foreclose it** — in particular, no surface may assume "one turn = one dispatch." Confirmed design inputs (operator, 2026-07-09):

1. **Hybrid authorization** — the orchestrator runs low-risk steps autonomously and confirms high-risk steps. Realized by feeding **per-step risk** to the `ConfirmationPolicyEngine` (ADR-041); its full tier×risk×origin capacity (retained by task 044 even though the current single-turn UX feeds it a simplified confidence signal) is the substrate. The E-1..E-6 origin/authorization-provenance capacity is the latent per-step audit mechanism.
2. **Closed-catalog-bound** — the agent **composes cataloged Actions+Bindings**; it does not author novel actions. ADR-039's closed catalog holds; the foundation's building blocks ARE the agent's step vocabulary.
3. **Ledger-resident plan** — the multi-step plan lives in the ledger (ADR-040), likely a new Plan/step entry type; `ledger_resolution` feeds step N from step N-1's output.
4. **Framework-agnostic** — Microsoft Agent Framework is a *candidate* orchestrator, not a commitment. The reserved seam is not coupled to any framework. (When scoped, treat as a fast-moving Microsoft-platform topic per the `knowledge/` + researcher-subagent process.)

**Consequence**: the execution surfaces are composable building blocks; the ledger is the cross-step state + input source; the gate is per-step risk-adaptive. Every Phase-E move (converged input resolution, single-source disposition, deterministic kind, retained gate capacity) is *substrate* the Action Engine needs — none forecloses it.

## Consequences

- The canonical completion engine realizes the *full* declared contract (any declared input, disposition routed, deterministic kind admitted) — a maker declares a capability without hand-editing the engine.
- Compose-r2 (and any dispatch consumer) builds against the stable `ContextEnvelope` input contract — no transitional shape.
- `memory.write` (task 057) and every future side effect ride the single disposition registry — no fourth drift path.
- The vertical-slice KEEP test makes "vertical slice works" the definition-of-done platform-wide.

## Alternatives considered (rejected)

- **Unify all engines including the agent-loop tool spine** — rejected: the tool spine is a legitimately different concern; merging it is R8+ and unnecessary for a functioning system.
- **Incremental `runtimeInput`-now / envelope-later** — rejected: it *continues* the straddle (two input paths, a migration boundary). The operator directive is to fix it fully; converging on one input model now is the fix.
- **Runtime model-judged disposition or action-kind** — rejected: a second intent mechanism (ADR-039 violation). Disposition + kind are catalog-declared DATA.
- **A third parallel spine for interactive/deterministic capabilities** — rejected: it re-creates the two-spine drift; a deterministic `ActionKind` on the declarative spine is the home.

## Promotion note

Authored **Proposed** at Phase-E kickoff. The **Accepted** flip is gated on the **Phase-E gate**: the foundation shipped (converged completion input, single-source disposition, deterministic kind + supersession, governance) with the `tests/integration/seam/**` vertical-slice tests green, and compose-r2 dispatching its 5 actions end-to-end through the shipped seam.
