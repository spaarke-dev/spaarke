# ContextEnvelope v1 — Contract Note (task AIR2-015)

> **Status**: Published (walking skeleton). **Date**: 2026-07-08. **Spec**: FR-A0-01. **Design**: D-M2 / FR-B-04.
> **Code**: `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ContextEnvelope.cs`
> **Contract test**: `tests/integration/contract/Api/Ai/ContextEnvelopeContractTests.cs` (12 tests, all green)

The canonical per-turn context contract the Context Binder (FR-B-04, task 053) assembles ONCE per turn
and everything downstream consumes. This task freezes the SHAPE (slice set + stability classes +
per-slice budget fields) so the Binder (053), the budgets (054), the MemoryItem contract (016) and the
Business-slice determinism verdict (003) all bind to a stable target. Shipped WITH a thin reference
producer + consumer + contract test — not a paper spec. **Budgets are placeholders; task 054 (FR-B-05)
fixes them against the FR-P0-02 measured baseline.**

## Six canonical slices → R1 primitive map

| Slice | Stability | R1 primitive it generalizes (D-M2 migration map) | v1 placeholder budget |
|---|---|---|---:|
| **User** | StablePrefix | current-turn message + caller-contact resolution (FR-B-07, `claims→contact`) | 300 |
| **Workspace** | StablePrefix | `BuildCurrentDateDirective` (clock/tz) + host workspace/record context (design calls this "Environment facts") | 150 |
| **Business** | StablePrefix* | host-record identity line + Dataverse schema card + per-table write contracts | 1,500 |
| **Memory** | VolatileTail | `BuildLedgerOutputsContext` → `Conversation` (ledger facade) + Record/User memory-item refs | 2,000 (Conversation) |
| **Organizational** | SemiStable | inbound provider interface only in r2 (Work IQ candidate) — empty | null (0 in r2) |
| **Semantic** | SemiStable | provider interface over Azure AI Search / SPE — empty; retrieval carries own provenance (D-M3) | null (0 in r2) |

\* Business stays in the stable prefix **conditionally** — task 003 (FR-P0-03) verified the two render
sites are byte-deterministic (CONFIRMED DETERMINISTIC), satisfying the D-M2 determinism gate.

## Canonical assembly order (NFR-04)

`User → Workspace → Business → Organizational → Semantic → Memory`

Stable-prefix slices precede the volatile ledger tail (`Memory.Conversation`) for prompt-cache
stability. `ContextEnvelope.CanonicalAssemblyOrder` encodes this; the contract test asserts every
StablePrefix slice sorts before the VolatileTail slice, and that Memory is last.

## Placeholder budgets accommodate the MEASURED baseline (task 002)

`notes/prompt-assembly-baseline.md` §4 measured the REAL as-built assembly and found the a-priori D-M2
estimates understate reality. v1 placeholders (`PlaceholderBudgets`) are seeded with **headroom above
measurement**, not the a-priori estimates, so the walking skeleton never trips a placeholder ceiling
the real assembly already exceeds:

| Slice | D-M2 a-priori | Measured (baseline §4) | v1 placeholder | Note for task 054 |
|---|---:|---:|---:|---|
| Workspace/Environment | ≤50 | ~111 (exceeds on every turn) | 150 | raise binding budget above ~111 |
| Business | ≤1,200 | ~1,118 (at/over on realistic turns) | 1,500 | 2 unconditional directives = 968 of the floor |
| Memory.Conversation | ≤2,000 | **structurally unbounded to ~8,000** | 2,000 | **most consequential — reconcile the structural ceiling, not just the number** |

Every slice budget is flagged `SliceMeta.BudgetIsProvisional = true`. Task 054 replaces the constants
and clears the flag.

## Key invariants (test-locked)

1. **ADR-040 ledger facade** — `Memory.Conversation` is `IReadOnlyList<LedgerEntryReference>`; the
   reference type exposes NO content/payload/text member, so copying ledger content into the envelope
   cannot be expressed. A negative-control test proves the detector catches a content-bearing ref.
2. **NFR-07 counts-only** — `SliceMeta` carries counts/enums/bools only (no string member); the
   consumer presence summary emits identifiers + counts, never slice content.
3. **Tolerant reader** — camelCase + case-insensitive `ContextEnvelope.JsonOptions`; unknown
   slices/fields ignored; slices nullable → partial/missing-slice states deserialize cleanly.
4. **Versioned** — `Version = "context-envelope/v1"`; additive-only evolution.

## Wiring the Binder will need (task 053) — NOT done here (parallel-safety)

- The reference producer/consumer are pure static walking-skeleton impls in `PublicContracts/`. Task
  053 supersedes them with the real per-turn Context Binder that assembles from the live
  `PlaybookChatContextProvider` + `SprkChatAgentFactory` + `ChatHistoryManager.BuildLedgerOutputsContext`
  seam (per baseline §7 anchors — NOT the dead-code `OrchestratorPromptBuilder`).
- No DI registration was added; the Binder task decides the DI shape.
- The trace fingerprint (FR-A1-09, task 013/038) consumes `MetaFor(kind)` counts (identifiers/counts
  only, NFR-07).

## Placement Justification (bff-extensions.md)

- **Existing**: no existing type unifies the six R1 context primitives — they are scattered point fixes
  (verified: grep for `ContextEnvelope`/`ContextSliceKind`/`LedgerEntryReference` returned zero hits).
- **Extension**: N/A — this is the FIRST governed context contract; it is the extension target the six
  primitives converge onto.
- **Cost-of-doing-nothing**: without a frozen shape, the Binder (053), budgets (054), MemoryItem (016)
  and determinism verdict (003) each bind to a different slice model → the whole Binder wave rebuilds.
- **Placement**: `Services/Ai/PublicContracts/` per ADR-013 (consumers reach it only via the facade).
- **Publish-size**: 45.69 MB compressed excl PDB vs 45.87 MB baseline → ~0 delta (below baseline noise);
  far under the 60 MB ceiling. No new package; no NEW HIGH CVE (Kiota-Abstractions High advisory is a
  pre-existing transitive dep).
