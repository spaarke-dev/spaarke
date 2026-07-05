# ADR-039: Grounded Execution & Closed Catalogs

- **Status**: Proposed (2026-07-05); accepted-in-principle by operator with the v0.4 converged target (`docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`). Moves to Accepted when migration phase P1 ships.
- **Deciders**: Operator + `spaarke-ai-code-audit-r1` convergence review (2026-07-05)
- **Concise version**: [`.claude/adr/ADR-039-grounded-execution-closed-catalogs.md`](../../.claude/adr/ADR-039-grounded-execution-closed-catalogs.md) (the operational MUST/MUST-NOT surface — binding)

## Context

The 2026-07-05 AI code audit (`projects/spaarke-ai-code-audit-r1/SPAARKE-AI-CODE-INVENTORY.md`) found **ten coexisting intent-detection mechanisms** in the chat path and **four parallel routing configuration surfaces**. The root cause was not any single bad decision — each mechanism was locally rational — but a governance vacuum: **no ADR ever governed dispatch, so nothing made the eleventh mechanism a violation.** By contrast, domains with strong principle-level ADRs (auth ADR-028, eventing ADR-030, governance ADR-015) stayed coherent across the same 27 projects.

The same review found that the two ADRs which DID misdirect (ADR-013's playbook-verb canon; ADR-037's engine-steering default) failed by codifying *mechanisms* rather than *principles* — they rotted as the mechanisms evolved. This ADR is therefore deliberately mechanism-light: it constrains what must always be true about dispatch, execution, and output grounding, not which class implements it.

Product context: Spaarke AI is a legal-operations platform. Ungrounded LLM output is a liability issue, not just a quality issue; and the platform's differentiating bet is multi-capability composition in one session (canonical doc §3.0), which requires a closed, auditable action space.

## Decision

Spaarke AI has exactly **one dispatch protocol** — three entry paths — over **two closed catalogs**, and every output is **grounded**:

1. **Three entry paths** (canonical doc §7): **Event** (manifest `on_event` rules — deterministic, bounded by cost cap / opt-out / bulk rules / explicit-command supersede), **Click** (`invoke(bindingId, args)` — deterministic; chips, ribbons, wizards, hard slashes), **Text** (one bounded function-calling agent turn — the ONLY probabilistic decider on the platform).
2. **Two closed catalogs** (canonical doc §6): **Actions + Bindings** (capabilities — prompt-controlled execution units × invocation configs) and **Tools** (typed primitives with declared `side_effect_class`, `permission_scope`, `budget_class`). Closed means: the LLM cannot invoke an unlisted tool (function-calling validation enforces this mechanically) and nothing dispatches to an uncataloged capability.
3. **Grounded execution invariant**: every platform output is one of (a) cataloged-capability output (prompt-controlled + schema-validated via structured outputs), (b) tool-composed answer with citations, (c) confirmation prompt, (d) honest refusal via the tenant's refusal capability. Free-form completion untethered from (a) or (b) has no code path.
4. **Control flow is code; behavior is data** (OQ-2 resolution): makers author prompts, schemas, scopes, bindings, chips, event rules, thresholds — never branches or loops. Composite capabilities are `coded` C# workflows (engineering deliverables) reading their prompts from Action rows. No maker-facing graph authoring exists.

The full MUST/MUST NOT surface is in the concise version and is binding.

## Alternatives considered

- **Purpose-built classifier stack** (embedding index over trigger phrases + LLM reranker + calibrated thresholds — the superseded v0.3 design): rejected per OQ-1. It is itself probabilistic, adds a permanently-tunable subsystem plus an L2↔L3 threshold seam, and duplicates what the bounded loop does natively at comparable per-turn cost. Its legitimate residue — dispatch-regression control — is delivered instead by the golden-utterance eval suite (deterministic CI gate); its legitimate scale aid — candidate narrowing at 100+ catalog entries — is permitted ONLY as deterministic pre-filtering of the tool list, never as a decision-maker.
- **Open tool surface** (let the model call arbitrary operations): rejected — violates the legal-ops liability posture and the tenant-security invariant (tools carry `permission_scope`).
- **Per-mechanism governance** (an ADR per dispatcher): rejected — that is how the drift happened; the constraint must be on the COUNT of mechanisms, not their shapes.

## Consequences

- Positive: the eleventh mechanism becomes an ADR violation caught at review; routing improvements become maker data edits + eval cases; audit trail (ledger tool chains, ADR-040) covers every probabilistic decision; refusal telemetry gives makers a backlog signal.
- Negative / accepted: no calibrated per-Binding confidence dial on the text path (overlay exception E-4 REJECTED by operator — risk classes + ask-when-uncertain + side-effect gating deliver D1's intent; the dial survives only on Event-path classify steps where a real classifier confidence exists). Dispatch decisions on the text path are model-judgment-with-audit rather than replayable scores — mitigated by the eval suite.
- Enforcement: code review + adr-check flag any new intent-matching code, any routing config outside the Binding table, and any tool-name-list gating. The golden-utterance suite is a KEEP-class test asset (ADR-038 `tests/integration/contract/**`).

## References

- Canonical target: `docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md` v0.4 §4.5, §7
- Clean-sheet rationale + review Q&A: `projects/spaarke-ai-code-audit-r1/GREENFIELD-CONCEPTUAL-DESIGN.md` (esp. §9 Q3-Q5)
- Evidence base: `projects/spaarke-ai-code-audit-r1/SPAARKE-AI-CODE-INVENTORY.md` §4 (the ten-mechanism census)
- ADR review that motivated this ADR: `projects/spaarke-ai-code-audit-r1/ADR-REVIEW-VS-GREENFIELD.md` §0, §4
