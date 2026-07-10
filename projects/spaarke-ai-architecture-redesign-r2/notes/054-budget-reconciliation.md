# ContextEnvelope Token Budgets — Reconciliation (Task AIR2-054, FR-B-05)

> **Purpose**: Fixes the binding per-slice + envelope-ceiling ContextEnvelope token budgets
> AGAINST the FR-P0-02 measured baseline (task AIR2-002,
> [`notes/prompt-assembly-baseline.md`](prompt-assembly-baseline.md)) — not a-priori. Closes the
> D-M2 measure-first loop: measured slice sizes → enforced budgets → breach-fails-eval gate.
>
> **Status**: Ratified. **Date**: 2026-07-10. **Depends on**: 002 (measurement), 053 (Binder + producers).

---

## 1. Binding budgets — design estimate → measured → ratified

| Slice (FR-B-05) | Design estimate | Measured (task 002) | **Binding** | Rationale for the binding value |
|---|---:|---:|---:|---|
| **Environment** (Workspace) | ≤50 | **111** (exact, every turn) | **150** | `BuildCurrentDateDirective` is unconditional and deterministic (added G-P3 UAT R5, post-estimate). The ≤50 estimate predated it. 150 = headroom above the measured every-turn floor. |
| **User** | ≤300 | ~40 (est.) | **300** | Measured tiny on typical turns; kept the D-M2 ceiling so long dictated messages have room. |
| **Business** | ≤1,200 | **1,118** (realistic 1,300+) | **1,500** | Two UNCONDITIONAL directives — `SideEffectHonestyDirective` 779 + compact-formatting 189 = **968** — form a "protocol floor" consuming 81% of a 1,200 budget BEFORE any playbook persona/knowledge/skills. Raised to 1,500 so ~530 tokens of playbook content fit above the 968 floor. (Both directives bypass the shared `IPromptBudgetTracker` — see §3.) |
| **Record memory** | ≤600 | 157 (this fixture) | **600** | Comfortable margin; kept. Interface docs target 200–500 for memory alone at higher fact density. |
| **Conversation** | ≤2,000 | ~620–970 normal / **~8,000 worst** | **2,000** | Kept at the D-M2 estimate. The structural worst case (`MaxContextOutputs=8 × MaxContextPayloadChars=4,000` ≈ 8,000 tokens) is now **gated by breach-fails-eval** rather than by tightening the byte-verbatim R1 constants that 053 froze. See §3 finding 3. |
| **Envelope ceiling** | ≤4,200 | ~2,025–2,410 normal | **4,200** | Kept. An **independent** bound, deliberately tighter than the 4,550 sum-of-per-slice-maxes (150+300+1,500+600+2,000) — slices rarely max together; normal turns sit at ~50–57% of the ceiling. |

**Net**: the integers are unchanged from the walking-skeleton v1 seed (`PlaceholderBudgets`) — the measurement CONFIRMED those values already carried correct headroom. What task 054 changes is (a) STATUS: provisional → **binding** (`SliceMeta.BudgetIsProvisional` flips to `false` on budgeted slices; class renamed `PlaceholderBudgets` → `EnvelopeBudget`), and (b) ENFORCEMENT: a breach is now a **hard eval failure** on the golden-utterance merge gate (FR-D-02), not just a logged number.

## 2. Enforcement surface

- **`EnvelopeBudget`** (`Services/Ai/PublicContracts/ContextEnvelope.cs`) — binding constants + `Evaluate(envelope, conversationTokens, recordMemoryTokens)` → `ContextBudgetReport` (per-slice `SliceBudgetLine` + envelope-ceiling line; counts only, NFR-07). The checker **never truncates** — it FLAGS a breach for the caller.
- **`ContextBinder.EvaluateAndLogBudget`** — every `BindAsync` evaluates the assembled envelope and emits the per-slice counts as per-turn telemetry (ids/counts only): `LogDebug` within budget, `LogWarning` on breach. The report rides `BoundInputs.BudgetReport`. Production does NOT throw on breach (a live turn must not 500) — it surfaces the breach; the eval gate is the pre-merge enforcer.
- **Breach-fails-eval** (`tests/integration/contract/Eval/ContextBudgetBreachEvalTests.cs`, `[Trait("Category","GoldenUtteranceEval")]`) — drives the REAL producers + `Evaluate`. A representative turn MUST be within budget; crafted Business, Conversation-structural, and ceiling overflows MUST be DETECTED (not silently truncated). A regression that bloats a slice past budget, or an evaluator that stops detecting an overflow, turns the `Category=GoldenUtteranceEval` gate RED (the trait IS the registration — no CI-YAML change; the existing `eval-gate` job runs it with no `continue-on-error`).

## 3. Escalated findings (baseline §4) — how each is resolved

1. **Environment exceeds its estimate every turn** → resolved by ratifying Environment at **150** (headroom over measured 111). No prompt change.
2. **Business at/over ceiling, dominated by two unconditional tracker-bypassing directives** → resolved by ratifying Business at **1,500** and documenting the 968-token protocol floor as an accepted reserved sub-budget. (Wiring the two directives INTO `IPromptBudgetTracker` is NOT done here — out of scope, and 053 froze their byte-verbatim production; recorded as a candidate follow-on.)
3. **Conversation structurally unbounded to ~8,000 (most consequential)** → **path chosen: gate the overflow, do not tighten the shipped constants.** The Conversation budget stays 2,000; the structural worst case is now a mechanical eval failure (`CraftedConversationOverflow_StructuralWorstCase_IsDetectedAsBreach_NotClipped` renders the real 8×4,000 worst case via `ConversationContextProducer` and asserts it breaches 2,000 + the ceiling, un-clipped). Rationale: 053 moved `BuildLedgerOutputsContext` + its `MaxContextOutputs`/`MaxContextPayloadChars` VERBATIM (byte-pinned prompt); reducing them is a prompt-behavior change with eval-rebaseline blast radius, deferred. If a future task adopts recommendation (b) (tighten the constants so the block can't exceed budget), this eval still passes (worst case within budget = no breach). The alternative recommendation (a) — wire the ledger-context caller into `IPromptBudgetTracker` — is recorded as the follow-on for when the ledger context is composed into dispatch prompts (053 boundary: envelope→dispatch-prompt rendering is a post-054 follow-on).

**ADR-040 references-not-copies**: the Conversation content is rendered OUTSIDE the envelope (the envelope's `Memory.Conversation` carries `LedgerEntryReference`s with `EstimatedTokens=0`); its token count is measured from the separately-rendered producer output and passed to `Evaluate`. Full document text is never copied into an envelope slice — the operand (`ResolvedOperand.Document`) is the only place document text travels, and only when the invoked capability requires it. Asserted by `LedgerConversation_TravelsAsReferences_ContentMeasuredOutsideTheEnvelope`.

## 4. BFF hygiene / governance (`.claude/constraints/bff-extensions.md`)

- **Placement Justification**: No NEW component/service/endpoint/DI/package. Changes are (1) in-zone additive enforcement on the existing `EnvelopeBudget` (was `PlaceholderBudgets`) budget contract in `Services/Ai/PublicContracts/`, (2) an additive `EvaluateAndLogBudget` method + `BoundInputs.BudgetReport` field on the existing in-zone `ContextBinder` (ADR-013 latency-coupled turn assembly), (3) one new KEEP-path eval test. §11 three questions: *existing* — the budget constants + `SliceMeta` already existed (task 015); *extension* — extended the existing contract in place, no new type family beyond the report DTO the enforcement inherently needs; *cost-of-doing-nothing* — without enforcement a ~8,000-token conversation (finding 3) ships silently and context bloat is caught only in UAT.
- **Publish-size delta**: measured `dotnet publish -c Release` — reported in the task summary; additive C# only, expected ≈0 delta vs the ~45.87 MB (excl. PDB) baseline; well under the ≤60 MB ceiling and the ≥+5 MB single-task escalation threshold.
- **New HIGH CVE**: none — zero package references added or changed.
- **NFR-07 (no-content telemetry)**: `ContextBudgetReport` / `SliceBudgetLine` carry enums + ints only (no string/content member); `RenderSummary()` emits `Slice=actual/budget` pairs, asserted content-free (`BudgetReport_RenderSummary_CarriesCountsOnly_NeverSliceContent`). The Binder's breach log carries slice ids + counts only.
- **ADR-038**: the eval test is on the KEEP path `tests/integration/contract/**`, pure + DI-free, no `Mock<HttpMessageHandler>`, no DI-registration/ctor-null tests.
