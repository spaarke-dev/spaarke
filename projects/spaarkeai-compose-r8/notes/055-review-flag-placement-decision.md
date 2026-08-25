# Task 055 — where anchored review flags are placed, and why the two paths stay two

> **Status**: decision recorded (task step 2); implementation follows
> **Task**: `tasks/055-whole-document-anchored-placement.poml`
> **Decision**: **converge the RESOLUTION, keep the two SINKS separate** — justified per CLAUDE.md §11
> **Escalation triggers**: neither fired. Reasoning in §4.

---

## 1. The question the task poses

`ComposeEditor.placeAdvisoryComments` already resolves review findings deterministically — `sectionRef`
through the client `CitationResolver` mirror, with a fixed deterministic-then-legacy ordering. The task
asks whether the whole-document `comments[]` channel converges onto it, or stays separate, and forbids
leaving two overlapping mechanisms by default.

## 2. What the trace found — there are two SINKS, and the second is worse than the task knew

| | `placeAdvisoryComments` | `registerAiReviewComments` (the DEF-11 `comments[]` path) |
|---|---|---|
| Producer | NDA-REVIEW / agreement-review findings | `compose-revise-document` `comments[]` (the `flag-risks` intent's ENTIRE output) |
| Resolution | `sectionRef` → `CitationResolver` → strict-then-prefix text | **`textPattern` only — no deterministic anchor at all** |
| Sink | `useComposeCommentThreads` (gutter cards) | `AnchoredAnnotation` store (FR-29 session-annotations endpoint) |
| Survives reopen | via thread state | **yes — persisted server-side** |
| Idempotent | **no** — documented at `ComposeWorkspace.tsx:711` | **yes** — deduped by `ai-review:{ledgerRef}#{i}` |
| Word `w:comment` on Save | **yes** (task 040) | **no** — explicitly out of task 040's scope, tracked as a follow-on |

So the two paths are **not** near-duplicates competing for the same job. Each has a property the other
lacks: one exports to Word and has no idempotency; the other is idempotent and durably persisted but does
not export. Collapsing either into the other would destroy a shipped property.

## 3. The sixth dark-machinery instance — which is what actually resolves this

`AnchoredAnnotationAnchor` **already carries a `paraId` field**, added by R3 FR-11 (compose-r3 task 012),
whose own doc comment states the contract:

> *"PRIMARY anchor: the `w14:paraId` of the paragraph the anchor lives in… Resolution order is
> paraId-FIRST, then the `textPattern`/`paragraphHint` fuzzy fallback."*

The **consumer is live**: the return-from-Word re-anchor path (`PriorAnchorInput` →
`AnnotationReanchorService`) sends `paraId` to the BFF, which *"resolves by this FIRST and only falls back
to the fuzzy scorer when it is absent."*

**The producer is dark.** `registerAiReviewComments` writes:

```ts
anchor: { textPattern: flag.target, paragraphHint: -1, spanId: provenance.ledgerRef }
```

No `paraId`, and `paragraphHint: -1` — the "no structural hint" sentinel. So **every DEF-11 review flag
goes through the fuzzy scorer on return-from-Word**, even when the model named its paragraph exactly.
Machinery present on both the contract and the consumer, no producer — the sixth instance of this
project's recurring trap (046 hardBreak, 048 atoms, 051 FR-C01 bookmark controllers, 051 FR-C02
`CitationResolver`, the ADR-043 Amendment 1 chain, now this).

## 4. Decision, and why neither escalation trigger fired

**Converge the resolution; keep the sinks separate.**

- **§11 is satisfied where the overlap actually is.** The genuine duplication is *deterministic anchor
  resolution* — `resolveAnchoredSpans` does paraId → citation → refuse, and `placeAdvisoryComments`'
  internal step does citation → text. One resolver, one precedence, used by both.
- **The sinks are not overlapping components.** Answering §11's three questions for keeping both:
  *Existing* — they share no resolution once converged. *Extension* — neither can absorb the other without
  loss: threads have no idempotency (a re-materialize would duplicate every flag), and the annotation
  store has no Word export. *Cost-of-doing-nothing* — collapsing onto threads loses ledger-key dedup and
  server persistence for `flag-risks` (the highest-volume whole-document capability); collapsing onto the
  annotation store loses Word `w:comment` export for NDA-REVIEW.

**Trigger 1 — "would converging regress the advisory path's shipped behavior?"** No, because the advisory
path is not being moved. It *gains* `paraId` above `sectionRef` in its deterministic step; its
fixed deterministic-then-legacy ordering and its range-citation spanning are untouched, and no current
caller supplies a `paraId`, so NDA-REVIEW behaviour is byte-identical.

**Trigger 2 — "does a per-item-anchored whole-document payload breach the ADR-040 128 KB cap?"** No.
Measured in task 054: anchors add **3.50%** at realistic payload size (**40.4 KB**, under the cap). The
over-cap case at the schema's declared maxima is pre-existing and anchors are 0.09% of it.

## 5. What this task implements

1. `ComposeDraftComment` gains `target_para_id` / `target_ref`, mirroring `ComposeDraftEdit` (task 054
   already added both to the Action's `comments[]` output schema, so the model can supply them).
2. `registerAiReviewComments` resolves deterministically (paraId → citation → text) and **populates
   `anchor.paraId`** — closing the dark producer, so return-from-Word re-anchoring stops falling back to
   the fuzzy scorer for flags that named their paragraph exactly.
3. `placeAdvisoryComments` accepts a `paraId`, checked ABOVE `sectionRef` (additive).
4. Both route through one shared resolver, so the precedence cannot drift between them.

### A defect this surfaces, which must be fixed with (1)

`registerAiReviewComments` currently filters:

```ts
.filter(c => c.target.length > 0 && c.body.length > 0);
```

After task 054 a flag may carry a deterministic `target_para_id` and a weak or absent `target_text`. This
filter would **silently drop** exactly the best-anchored flags. The gate must become
"has a resolvable anchor OR a non-empty target_text", never target_text alone.

## 6. Constraints carried in from task 054 (`notes/054-…` §6)

- **L-1** — hard breaks collapse in `collectBlocks().text`, so a model-quoted `target_text` may not exist
  verbatim. This *raises* the value of anchoring flags by paraId rather than text.
- **L-2** — the provider-registration race is argued, not proven; degrades to the pre-054 dispatch.
- **UAT-21** — an unresolved anchor REFUSES; it must never fall back to a search and never report
  `applied` for an item that was not placed. Per-item failure isolation must hold.
