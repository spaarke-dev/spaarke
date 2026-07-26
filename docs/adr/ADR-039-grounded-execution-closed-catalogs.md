# ADR-039: Grounded Execution & Closed Catalogs

- **Status**: **Accepted** (2026-07-05; **amended 2026-07-25**) — proposed 2026-07-05; accepted-in-principle by operator with the v0.4 converged target (`docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`); promoted Proposed → Accepted at migration phase P1 per this ADR's stated condition ("moves to Accepted when migration phase P1 ships") by `spaarke-ai-architecture-redesign-r1` task 026 (FR-P1-07). See "Acceptance evidence (P1)" below. **Amendment 2026-07-25** (`ai-advanced-capabilities-nda-r1` task 001, CLAUDE.md §6.5 Path B): added **Output Determinism Modes** (`fact` vs `advisory`) refining invariant (a) — see "Amendment (2026-07-25)" below.
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

## Acceptance evidence (P1 — 2026-07-05, task 026)

The promotion condition — P1 ships with the one-dispatch-protocol / two-closed-catalogs contract as the enforced state of the codebase — is met:

- **Catalog-routed capability live** (task 020, FR-P1-01): `chat-summarize` resolves exclusively via `IConsumerRoutingService.ResolveBindingAsync` to the SUM-CHAT@v1 prompted Action (`SessionSummarizeOrchestrator`); the pre-redesign dual-path dispatch (Linear-vs-engine conditional + `Workspace:ChatSummarizePlaybookId` config fallback) deleted, grep-zero — no engine or config fallback exists.
- **Event path live** (task 022, FR-P1-03): `EventRulesService` executes `document_uploaded` members declared in `sprk_playbookconsumer.sprk_oneventbindings` (chat-classify(1) → chat-summarize(2)) via `ResolveEventBindingsAsync`, under the four bounds + explicit-command supersede + M4 confidence policy.
- **Click path live** (tasks 023/023b, FR-P1-04): client `dispatchConsumer` + `POST /api/ai/chat/sessions/{id}/dispatch` resolving by Binding id via `GetBindingByIdAsync`; `executeSummarizeIntent`/`intentMatcher` grep-zero — ONE SSE loop client-wide.
- **Grounded-output invariant live** (task 021, FR-P1-02): every output ledger-written via `OutputRouter` BEFORE rendering (ADR-040 — itself Accepted at task 014), addressable as `{bindingId}@t{n}`.
- **Stray dispatch surface closed** (task 025, FR-P1-06): r7 branch closed; 492 lines of `linear_dispatch`/regex intent code deleted in place, grep-zero.
- **Eval merge gate active** (task 026, FR-P1-07 / NFR-02): UC-A-1 golden-utterance families green with LIVE dispatch assertions (`tests/integration/contract/Eval/`); dedicated `eval-gate` CI job (`dotnet test --filter "Category=GoldenUtteranceEval"`, no `continue-on-error`) fails the workflow on any eval regression.

Dispatcher-deletion of the remaining legacy chat text-path mechanisms is P2 scope and was NOT required for promotion — the ADR's stated condition is P1 shipping, which the above satisfies.

## Alternatives considered

- **Purpose-built classifier stack** (embedding index over trigger phrases + LLM reranker + calibrated thresholds — the superseded v0.3 design): rejected per OQ-1. It is itself probabilistic, adds a permanently-tunable subsystem plus an L2↔L3 threshold seam, and duplicates what the bounded loop does natively at comparable per-turn cost. Its legitimate residue — dispatch-regression control — is delivered instead by the golden-utterance eval suite (deterministic CI gate); its legitimate scale aid — candidate narrowing at 100+ catalog entries — is permitted ONLY as deterministic pre-filtering of the tool list, never as a decision-maker.
- **Open tool surface** (let the model call arbitrary operations): rejected — violates the legal-ops liability posture and the tenant-security invariant (tools carry `permission_scope`).
- **Per-mechanism governance** (an ADR per dispatcher): rejected — that is how the drift happened; the constraint must be on the COUNT of mechanisms, not their shapes.

## Consequences

- Positive: the eleventh mechanism becomes an ADR violation caught at review; routing improvements become maker data edits + eval cases; audit trail (ledger tool chains, ADR-040) covers every probabilistic decision; refusal telemetry gives makers a backlog signal.
- Negative / accepted: no calibrated per-Binding confidence dial on the text path (overlay exception E-4 REJECTED by operator — risk classes + ask-when-uncertain + side-effect gating deliver D1's intent; the dial survives only on Event-path classify steps where a real classifier confidence exists). Dispatch decisions on the text path are model-judgment-with-audit rather than replayable scores — mitigated by the eval suite.
- Enforcement: code review + adr-check flag any new intent-matching code, any routing config outside the Binding table, and any tool-name-list gating. The golden-utterance suite is a KEEP-class test asset (ADR-038 `tests/integration/contract/**`).

## Amendment (2026-07-25) — Output Determinism Modes (`fact` vs `advisory`)

- **Amended by**: `ai-advanced-capabilities-nda-r1` task 001 (CLAUDE.md §6.5 Path B — ADR amendment).
- **Deciders**: Operator + `ai-advanced-capabilities-nda-r1`.

### Context

The `ai-advanced-capabilities-*` program's first analysis/advisory vertical (NDA review) has an
explicit north star: deliver **Claude/ChatGPT-level reasoning and generative advisory output** —
better than a strong general LLM used online — for interactive, human-verified advisory tasks. A
naive reading of grounded-execution invariant (a) plus the "no free-form completion" MUST NOT
discouraged the reasoning/synthesis depth such advisory output requires: reviewers read invariant
(a) as *extractive-and-verbatim-only*. That reading is stricter than the invariant actually needs
to be. Invariant (a) requires output to be **prompt-controlled and schema-validated** — it never
required output to be *non-synthesizing*. The liability posture that motivates ADR-039 is about
**ungrounded** output (fabricated facts, uncited claims), not about **reasoned** output over
grounded facts.

### Decision

**The shared invariant first: grounding is mode-independent.** Spaarke AI supports two output/action
modes — **deterministic** (fact-bound) and **advisory** (informed by probabilistic reasoning). The
mode changes *how* the system reasons and expresses an answer; it never changes *whether* the answer
must be grounded. In BOTH modes, every assertion of fact MUST be demonstrably traceable to a
confirmed source (a cited document span and/or a grounding reference in the ledger), and **no
output — deterministic or advisory — may contain a hallucinated fact, legal authority, standard
position, or citation that cannot be resolved to a confirmed source.** Advisory mode buys reasoning
*depth*, paid for entirely by keeping every underlying fact grounded and every recommendation
traceable to those grounded facts; it is never a licence to assert beyond the sources.

Refine invariant (a): a cataloged capability declares an **output determinism mode** in catalog
data — `fact` (default) or `advisory`. This governs the *determinism of expression and synthesis*,
never the *accuracy or auditability of facts*.

- **`fact` (deterministic, default, unchanged)** — correctness is a factual claim about source
  material; extractive, low-temperature, source-span-cited, no synthesis beyond the source.
- **`advisory` (probabilistic)** — value is expert reasoning/synthesis/recommendation over
  *grounded* source material; permits generative depth and a Reasoning-tier deployment (ADR-016)
  while remaining inside invariant (a) — prompt-controlled, schema-validated, and source-cited for
  every factual claim, with a not-authoritative disclaimer and human-review surfacing for
  high-risk findings. The full MUST/MUST NOT surface is in the concise version and is binding.

The mode is **DATA** on the Action (not runtime LLM self-judgment) — the same discipline as
"risk is catalog-declared data" (ADR-041) and "behavior is data, control flow is code"
(invariant #4). No new entry path, no new output category (still one of (a)/(b)/(c)/(d)), no new
mechanism. Every other ADR-039 invariant — closed catalog, three entry paths, budgets, the ONE
confirmation gate, ledger store-before-render, golden-utterance eval coverage — continues to hold
unchanged for advisory capabilities.

### Why principle-level (consistent with this ADR's philosophy)

This ADR deliberately constrains *properties that must always be true*, not the classes that
implement them (Context §11). The amendment adds a declared *property of the output* (its
determinism mode) as catalog data — it names no model, class, temperature, or dispatcher. It is a
refinement of an existing invariant, not a bolt-on mechanism, and therefore does not risk the
"mechanism-shaped ADRs rot" failure the original ADR was written to avoid.

### Consequences

- Positive: a sanctioned lane for high-value advisory verticals (NDA review, and the analysis/
  advisory pattern generally) without weakening grounding, citation, audit, or the closed catalog.
  The distinction between "the facts are wrong/uncited" (always a violation) and "the reasoning is
  synthetic" (permitted in advisory mode) is now explicit at review time.
- Negative / accepted: advisory-mode output is model-judgment-with-audit for its *reasoning*
  (mitigated by citation-required-for-facts + decline-if-unverifiable + the advisory-quality eval
  rubric + human-in-the-loop). Advisory mode MUST NOT be applied to capabilities whose output is
  consumed as authoritative fact by downstream deterministic logic.
- Enforcement: code review + adr-check verify (1) `output_determinism` is declared data (not
  inferred), (2) advisory capabilities still cite every factual claim and carry the disclaimer,
  (3) advisory mode is not used to smuggle free-form completion untethered from a capability.

## References

- Canonical target: `docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md` v0.4 §4.5, §7
- Clean-sheet rationale + review Q&A: `projects/spaarke-ai-code-audit-r1/GREENFIELD-CONCEPTUAL-DESIGN.md` (esp. §9 Q3-Q5)
- Evidence base: `projects/spaarke-ai-code-audit-r1/SPAARKE-AI-CODE-INVENTORY.md` §4 (the ten-mechanism census)
- ADR review that motivated this ADR: `projects/spaarke-ai-code-audit-r1/ADR-REVIEW-VS-GREENFIELD.md` §0, §4
