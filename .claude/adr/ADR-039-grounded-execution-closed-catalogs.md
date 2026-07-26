# ADR-039: Grounded Execution & Closed Catalogs (Concise)

> **Amended 2026-07-25** (`ai-advanced-capabilities-nda-r1` task 001, CLAUDE.md §6.5 Path B):
> added **Output Determinism Modes** (`fact` vs `advisory`) refining grounded-execution
> invariant (a). See the "Amendment (2026-07-25)" section below. No prior MUST/MUST NOT
> weakened; the advisory mode adds obligations, it does not remove any.
>
> **Status**: Accepted (2026-07-05) — promoted Proposed → Accepted at migration
> P1 per the stated condition, by `spaarke-ai-architecture-redesign-r1` task 026
> (FR-P1-07). P1 evidence: chat-summarize via catalog (task 020), ledger
> write-before-render (021), Event path (022), Click path (023/023b), r7 stray
> dispatch surface closed (025), UC-A-1 eval family green with live dispatch
> assertions + `eval-gate` CI job merge-blocking (026, NFR-02). Full evidence:
> docs/adr twin "Acceptance evidence (P1)".
> **Domain**: AI platform — dispatch, execution, safety
> **Source**: `spaarke-ai-code-audit-r1` (ADR review A-4); encodes ratified
> decisions D5/D6/D7 + OQ-1/OQ-2 (canonical AI architecture doc v0.4 §4.5/§7.7).
> **Why this ADR exists**: the 2026-07 audit found TEN coexisting
> intent-detection mechanisms and FOUR routing config surfaces — accumulated
> precisely because no ADR governed dispatch. This ADR is deliberately
> **principle-level, not mechanism-shaped** (the property that kept ADR-028/030
> durable while mechanism-shaped ADRs rotted).

---

## Decision

Spaarke AI has exactly **one dispatch protocol** (three entry paths) over
**two closed catalogs**, and every output is **grounded**.

1. **Three entry paths, nothing else**: Event (manifest rules, deterministic),
   Click (`invoke(bindingId, args)`, deterministic), Text (ONE bounded
   function-calling agent turn — the only probabilistic decider).
2. **Two closed catalogs**: Actions + Bindings (capabilities) and Tools
   (typed primitives). The LLM never invokes an unlisted tool; nothing
   dispatches to an uncataloged capability.
3. **Grounded execution**: every platform output is one of (a) a cataloged
   capability's prompt-controlled, schema-validated output; (b) a tool-composed
   answer with citations; (c) a confirmation prompt; (d) an honest refusal.
4. **Control flow is code; behavior is data**: makers edit prompts, schemas,
   scopes, bindings, chips, event rules — never branches/loops. No
   maker-facing graph/workflow authoring.

## Constraints

### ✅ MUST
- **MUST** route every AI invocation through one of the three entry paths.
- **MUST** enforce a per-turn tool-call budget and per-user daily Event-path
  budget (ADR-016 alignment).
- **MUST** attach citations to every tool read the loop consumes; persist the
  full tool chain to the session ledger (ADR-040).
- **MUST** gate side effects via the ONE Confirmation Gate, driven by the
  tool's `side_effect_class` and the Binding's `risk` — declared in catalog
  data.
- **MUST** render off-catalog requests as the tenant's refusal capability +
  emit `dispatch_refused` telemetry.
- **MUST** cover every catalog/prompt change with the golden-utterance eval
  suite (dispatch regressions block merge).
- **MUST** author new composite capabilities as `coded` workflows (registered
  C# classes reading prompts from Action rows).

### ❌ MUST NOT
- **MUST NOT** add a second intent-detection mechanism ANYWHERE (regex,
  keyword map, vector classifier, reranker, routing middleware that selects
  capabilities). If dispatch accuracy needs help at scale (~100+ catalog
  entries), the ONLY permitted aid is deterministic pre-filtering of the tool
  list (context scoping; optionally embedding retrieval AS a pre-filter) —
  never a decision-maker in front of or instead of the loop.
- **MUST NOT** add a routing config surface outside the Binding table
  (no appsettings playbook/capability maps — the audit found four).
- **MUST NOT** gate writes by hardcoded tool-NAME lists (the
  CompoundIntentDetector failure mode) — gating is by declared
  `side_effect_class`, always.
- **MUST NOT** emit free-form LLM text untethered from a capability's prompt
  control or a cited tool chain.
- **MUST NOT** land new capability on the frozen node-graph engine (OQ-2;
  ADR-037 amendment 2026-07-05).
- **MUST NOT** expose maker-authored control flow (graphs, rule interpreters,
  "config tables with rules").

## Amendment (2026-07-25) — Output Determinism Modes (`fact` vs `advisory`)

> Path-B amendment by `ai-advanced-capabilities-nda-r1` task 001. Demand-pull: the first
> analysis/advisory vertical (NDA review) needs Claude/ChatGPT-level advisory reasoning depth
> that the *default* deterministic reading of invariant (a) discouraged. This refines invariant
> (a); it does not add a fourth output category, a new entry path, or a new mechanism.

**Grounding is mode-independent — the invariant BOTH modes share.** The determinism mode changes
*how* the system reasons and expresses an answer, never *whether* it must be grounded. In BOTH
modes, every assertion of fact MUST be demonstrably traceable to a confirmed source (a cited
document span and/or a grounding reference recorded in the ledger). **Hallucination — any factual
claim, legal authority, standard position, or citation that cannot be resolved to a confirmed
source — is prohibited in both modes and has no code path.** The `advisory` mode buys reasoning
depth and synthesis, paid for *entirely* by keeping every underlying fact grounded and every
recommendation traceable to those grounded facts. It is never a licence to assert, imply, or
recommend something the sources do not support.

**Refinement of grounded-execution invariant (a).** A cataloged capability's output (invariant
(a)) has a declared **output determinism mode**, carried as catalog **data** on the Action
(default `fact` when unstated). The mode governs the *determinism of expression and synthesis*,
never the *accuracy or auditability of facts*.

- **`fact` (deterministic — default, unchanged)**: the capability's correctness is a factual
  claim about source material. Output is extractive / low-temperature, cites the exact source
  span for every claim, and performs no synthesis or recommendation beyond what the source
  states. This is the prior behavior and remains the default.
- **`advisory` (probabilistic)**: the capability's value is expert reasoning, synthesis,
  comparison, or recommendation over grounded source material (e.g. "review this NDA against our
  standard and advise"). The advisory mode PERMITS generative reasoning depth and a
  higher-capability / higher-temperature deployment (ADR-016 Reasoning tier), while remaining
  fully inside invariant (a): prompt-controlled, schema-validated, source-cited for every
  factual claim. `advisory` is a mode *of* (a), not an escape from it.

### ✅ MUST (advisory mode)
- **MUST** declare the mode as catalog data on the Action (`output_determinism: advisory`;
  default `fact`). The mode is DATA, never runtime LLM self-judgment — consistent with
  "risk is catalog-declared data" (ADR-041) and "behavior is data" (invariant #4).
- **MUST** cite the source span and/or grounding reference for every FACTUAL claim; a claim it
  cannot ground it MUST decline or mark explicitly as unverified — never fabricate.
- **MUST** ground the reasoning itself, not only the isolated facts: every recommendation, risk
  rating, or opinion MUST be derived from and traceable to the cited grounded material (the
  subject document + the firm standard / retrieved knowledge), MUST visibly distinguish grounded
  fact from reasoned judgment, and MUST NOT introduce facts, legal authorities, or standard
  positions absent from the grounded sources. Advisory judgment is *reasoning over grounded
  evidence*, never assertion beyond it.
- **MUST** carry a not-authoritative / advisory disclaimer in its output contract (for legal
  advisory: "not legal advice"), and surface high-risk findings for human review.
- **MUST** remain subject to EVERY other ADR-039 invariant — closed catalog, one of the three
  entry paths, budgets (ADR-016), the ONE confirmation gate on side effects (ADR-041),
  ledger store-before-render (ADR-040), and golden-utterance eval coverage.

### ❌ MUST NOT (advisory mode)
- **MUST NOT** use `advisory` to emit output untethered from a cataloged capability's prompt
  control — invariant #3's "free-form completion has no code path" still holds.
- **MUST NOT** relax citation or grounding obligations for FACTUAL claims. Advisory relaxes the
  determinism of *reasoning/expression*, never the accuracy or auditability of *facts*.
- **MUST NOT** apply `advisory` to a capability whose output is consumed as authoritative fact by
  downstream deterministic logic — those stay `fact`.

## Integration
ADR-013 (facade boundary; capability-invocation verb) · ADR-040 (ledger) ·
ADR-016 (budgets) · ADR-018 (capability-boundary flags; Binding `enabled` is
the finer-grained disable) · ADR-032 (kill-switch impl) · ADR-037-as-amended
(section streaming for composites) · ADR-038 (eval suite = integration/contract
class).

**Compose `confidence_band`** ([docs/architecture/COMPOSE-REDLINE-DERIVED-VIEWS.md](../../docs/architecture/COMPOSE-REDLINE-DERIVED-VIEWS.md)) — a client-side deterministic derivation over grounding evidence + live-doc resolvability; upholds "grounded, no false precision" without a model self-report and without a new dispatch path.

**Full ADR**: [docs/adr/ADR-039-grounded-execution-closed-catalogs.md](../../docs/adr/ADR-039-grounded-execution-closed-catalogs.md)
