# ADR-039: Grounded Execution & Closed Catalogs (Concise)

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

## Integration
ADR-013 (facade boundary; capability-invocation verb) · ADR-040 (ledger) ·
ADR-016 (budgets) · ADR-018 (capability-boundary flags; Binding `enabled` is
the finer-grained disable) · ADR-032 (kill-switch impl) · ADR-037-as-amended
(section streaming for composites) · ADR-038 (eval suite = integration/contract
class).

**Full ADR**: [docs/adr/ADR-039-grounded-execution-closed-catalogs.md](../../docs/adr/ADR-039-grounded-execution-closed-catalogs.md)
