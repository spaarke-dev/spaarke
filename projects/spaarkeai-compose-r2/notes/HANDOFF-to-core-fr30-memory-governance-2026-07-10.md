# HANDOFF → core (redesign-r2): FR-30 memory-governance — dispatched-action capture path + untrusted-origin gate

> From compose-r2, 2026-07-10. Origin: task 063 §6.5 escalation. Tracking: **[issue #629](https://github.com/spaarke-dev/spaarke/issues/629)**.
> This is the **correct long-term realization of FR-30** — not an interim. Two pieces, both core-owned.

## Why this comes to you
FR-30 (compose-r2 design.md §8) requires Compose's durable AI-derived insights to persist as workspace-scope `MemoryItem`s via a **gated** `memory.write`, with an untrusted-origin gate (memory-poisoning prevention). The design intent is correct and unchanged. What blocks it is that core's shipped `memory.write` (task 057) is **chat-invocation-context-only** and the **untrusted-origin gate was deferred** — and Compose cannot fork either (ADR-013 facade rule + charter §3.4 no-local-variant). Compose's insights come from **dispatched catalog Actions** (Click-path/Playbook context), which 057 explicitly rejects.

## Ask 1 — dispatched-action / gated-promotion capture path (the blocker)
Provide a governed capture into the durable memory tier, reachable from a **dispatched-action completion** (non-chat), consumable by `Services/Compose` **through a `PublicContracts` facade** (Compose injects no memory internals). Contract:
- **Input**: distilled insight (text + structured fields) + provenance envelope `{ source: "ai-derived", bindingId, ledgerRef ({bindingId}@t{n}), sessionId, tenantId }`. `ledgerRef` references the `SessionOutput` — never a duplicated payload (ADR-040).
- **Scope**: workspace → the canonical `Record` scope keyed by the host matter/document entity (per `MemoryItem.cs`'s own note that "workspace-scope memory = the SAME concept under the canonical Record name").
- **Idempotency**: upsert-by-(Type,Key) supersession (as 057 already does for chat writes).
- **Output**: the persisted `MemoryItem` (or ref) so Compose can surface it with attribution.

Shape options (your call): a new `InvocationContextKind` support on `MemoryWriteHandler`; OR a sibling handler; OR a service-level `IMemoryPromotion`-style facade in `PublicContracts`. Compose only needs a **facade-reachable, provenance-carrying, gated** entry point.

## Ask 2 — untrusted-origin governance gate (FR-30 correctness; your deferred governance project)
AI-derived (untrusted-origin) memory writes MUST be a **governed, Policy-v2-visible** side effect (deliberate promotion, not silent auto-write). This is FR-30's memory-poisoning-prevention requirement and design.md §8's explicit rationale (*"un-gated, AI-derived content writing itself into persistent memory is the memory-poisoning vector"*). Core deferred it (operator ruling 2026-07-08 → Tier-1 silent Execute) to "a separate governance project." **Ask 1 without Ask 2 is a poisoning surface** — please sequence the gate as part of this, or tell us the governance-project timeline so we can gate compose-side capture behind it.

## What compose-r2 owns (task 063), ready to build on delivery
Insight-distillation (select genuinely-durable knowledge — recurring deviations, defined-term canon, key decisions — NOT raw action output), invoke the facade with the envelope + scope mapping, and the CAPTURE→RECALL eval. Blocked only on Asks 1 & 2.

## No interim substitute
Tasks **061** (action-history over the ledger) and **062** (cross-version persistence) are their own shipped FRs (FR-31/FR-33). They give users the action-history *view*; they are **not** FR-30 — they do not recall insights into future reasoning and are not governed memory. We are **not** re-labeling them as an FR-30 stand-in. FR-30's user value waits on Asks 1 & 2.

— compose-r2, 2026-07-10
