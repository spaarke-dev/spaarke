# ADR-041: Judgment, Confirmation & Completion Policy (Concise)

> **Status**: **Proposed** (2026-07-09, spec time of `spaarke-ai-architecture-redesign-r2`).
> **Promotion**: → **Accepted** when gate **G-R2-A** passes (task 049 UAT). Do NOT mark
> Accepted before the gate.
> **Domain**: AI platform — judgment layer above dispatch (resourcefulness, confirmation, completion)
> **Builds on**: ADR-039 (grounded execution, ONE dispatch protocol) · ADR-040 (session ledger)
> **Why this ADR exists**: R1 steered resourcefulness, confirmation friction, and
> completion truthfulness with prompt text — which drifts (the R3-1 confirm-loop, the
> "claimed but never ran" fabrications). This ADR moves those three from directives to
> **codified doctrine + code-enforced invariants**.

---

## Decision

A three-part judgment policy sits on top of ADR-039 dispatch: **D-F0** resourcefulness
(preamble), **D-F1** confirmation (friction), **D-F2** completion (truthful outcome).
Enforcement is in code (gate engine, ledger, completion composer); prompt directives may
express the doctrine but are NOT the source of truth.

## Constraints

### ✅ MUST

- **MUST** treat reads/searches as always free and never gated; **writes/side effects are
  deterministic and gated** (the read/write safety asymmetry, D-F0(b)).
- **MUST** keep the D-F0 degradation ladder (**verify → act → degrade → refuse-with-affordance**)
  **entirely below the side-effect line** — degrading/approximating never weakens a
  confirmation gate or a hard block. A refusal MUST carry a concrete affordance.
- **MUST** classify risk from **catalog-declared DATA** (sub-tier + risk factors:
  reversibility, external visibility, deadline impact, confidentiality/privilege impact,
  record-of-truth impact) — the ADR-039 `side_effect_class` pattern extended. Never a
  runtime LLM risk judgment.
- **MUST** classify request origin **deterministically and fail-closed** (undecidable ⇒
  `inferred` ⇒ confirm): Click ⇒ explicit; document-content/tool-result ⇒ inferred;
  utterance naming the enumerated action + invocation ⇒ explicit; else inferred.
- **MUST** evaluate confirmation as (risk-tier × origin × completeness) with **overlay
  precedence** (strict, first-fires-decides): (1) injection-suspect → (2) safety-perimeter
  degradation (writes→confirm, reads fail-open) → (3) incomplete-args (elicit once, re-evaluate)
  → (4) origin → (5) tier row.
- **MUST** honor the six ruled edge cases E-1..E-6 (affirmation binds to a single complete
  proposal; explicitness survives model-only turns but any user turn resets; origin vs
  injection layered never merged; enumerated-set-only explicit; elicitation answer inherits
  origin; `dispatchUncertain` forces a dialog).
- **MUST** treat confirmation state as a **Gate-ledger property (ADR-040)** — a second ask
  for the same request is structurally impossible (kills the R3-1 loop). NOT prompt-enforced.
- **MUST** run gate **pre-suspend validation** — validate a doomed call BEFORE suspending
  into a dialog, so the user gets an honest refusal-with-affordance, not Confirm → ❌.
- **MUST** render every side-effect outcome as a structured **OutcomeCard**, composed
  **after** the ledger write from a stored `SessionOutput` key (store-before-render, ADR-040).
- **MUST** derive job-aware completion status from the **job aggregate** — the ONLY path to
  `Succeeded` is a fully-completed aggregate (a created-but-unindexed record renders
  `Partial`, never `Succeeded`).
- **MUST** confirm UI-action claims by a **client ack**, not a server-write assertion; absent
  the ack in a bounded window, fail honestly.

### ❌ MUST NOT

- **MUST NOT** add a runtime model-judged risk classification (second intent mechanism —
  ADR-039 violation).
- **MUST NOT** weaken a gate or hard block via any resourcefulness/degradation path.
- **MUST NOT** re-ask for a confirmation whose state is already recorded on the gate ledger.
- **MUST NOT** read document-derived content as a user utterance when classifying origin.
- **MUST NOT** render a completion claim before/without its stored ledger evidence, nor via a
  second rendering surface (composition rides the one disposition surface + gate-resume path).
- **MUST NOT** create a second completion/session-state store (ADR-040).

## Integration

ADR-039 (dispatch, closed catalogs, `side_effect_class`) · ADR-040 (gate-ledger confirmation
state; Output disposition for OutcomeCard; store-before-render) · ADR-032 (Null-Object
kill-switch — Null* readers where a gated service must stay registered) · ADR-015 (Tier
mapping — ToolChain/trace carry ids+counts only). Implemented by tasks 030 (D-F0), 032/033/034
(D-F1), 035/036/037/038 (D-F2).

**Open item (2026-07-09)**: the Policy v2 gate ENGINE (032) is a published PRODUCER with 0
core call-sites — the core live gate runs on the suspend floor + 034 pre-suspend validation;
042 reused the existing gated path. Wiring the engine into the core's own live gate is a
follow-up decision (see the full ADR). Does not affect the doctrine or the Compose-consumed seam.

**Full ADR**: [docs/adr/ADR-041-judgment-confirmation-completion-policy.md](../../docs/adr/ADR-041-judgment-confirmation-completion-policy.md)
