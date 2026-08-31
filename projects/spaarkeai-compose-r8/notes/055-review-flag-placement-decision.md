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

---

## 7. Implementation record (what actually landed, 2026-08-25)

> Status: implemented. Client only — no `.cs` touched, no Action/output schema touched.

### 7.1 The shared resolver

`src/client/shared/Spaarke.Compose.Components/src/widgets/composeAnchorResolution.ts` — new, pure, ~35
lines of logic. `resolveAnchorParaIds({ paraId, ref }, referenceMap)` returns
`none | resolved(paraIds[]) | not_found | ambiguous(matchCount)`. It owns the PRECEDENCE only; each
consumer keeps its own **span policy**, which is what stopped this being a false convergence:

| Consumer | Span policy it keeps |
|---|---|
| `usePendingRedline.resolveAnchoredSpans` | exactly ONE paragraph — a range is refused, not narrowed; the paraId must be in the LIVE document |
| `ComposeEditor.placeAdvisoryComments` | a range legitimately SPANS first→last clause |
| `ComposeWorkspace.registerAiReviewComments` | an annotation anchor holds ONE paraId — a range hangs at its first clause |

Its §11 three-question justification is in the module header.

### 7.2 The dark producer, closed

`registerAiReviewComments` now resolves deterministically and writes `anchor.paraId`. Return-from-Word
re-anchoring (`AnnotationReanchorService`) stops falling back to the fuzzy scorer for a flag whose model
named its paragraph exactly. **It never fabricates**: an unknown citation, or a paraId and citation that
disagree, leaves `paraId` unset and the flag keeps its prose fallback.

This is deliberately NOT the UAT-21 "refuse" case, and the reasoning is worth recording: nothing is being
*placed in the document* at this moment. The annotation anchor is a hint resolved later by the re-anchor
service, so an unresolved anchor has no wrong position to land on — it degrades to exactly the pre-055
behaviour. UAT-21's refusal applies where a span is being chosen (the edit path, and the advisory path's
new `paraId` field), and it does.

### 7.3 The defect (§5) is fixed

The gate is now `body.length > 0 && (resolvableAnchor || target_text.length > 0)`. A flag carrying a
deterministic anchor and no prose — which L-1 makes *likely*, not exotic — is kept. A flag with neither
is skipped, as before.

### 7.4 The advisory path's asymmetry, stated

`AdvisoryCommentInput` gained `paraId`, checked ABOVE `sectionRef`. The two anchors behave differently
on failure and that is intentional:

- `sectionRef` that fails to resolve → falls through to the legacy text leg. This is agreements-r1 task
  011's shipped fixed ordering and escalation trigger 1 protects it. Unchanged, with five tests pinning
  it (citation-only, range spanning, unresolvable-citation fall-through, prose-only, prose-absent).
- `paraId` that fails to resolve → REFUSES (`not_found` / `ambiguous`). It is a new field with no
  caller, so nothing shipped moves, and UAT-21 is the right rule for it.

### 7.5 The tripwire needed a module boundary

Step 4 asks for the `ThrowIfTextSearched` pattern. ts-jest compiles this package to CommonJS, where a
same-module call to `resolveTargetSpans` is a direct local reference — no spy, no `jest.mock`, no
`jest.spyOn` on the exports object can intercept it. So the prose-matching leg was MOVED verbatim to
`hooks/redlineTextSearch.ts` (nothing changed, nothing retired; `usePendingRedline` re-exports it so
every importer is untouched) purely to create the seam. `usePendingRedline.wholeDocument.test.tsx` then
swaps it for a throwing/recording double.

**The tripwire was mutation-verified.** With `resolveAnchoredSpans` stubbed to `return null`, both ARMED
tests fail with the tripwire's own message. The first ARMED test additionally gives every anchored change
a `target_text` — without that it would pass vacuously (an anchor-less change with no prose takes the
insertion-at-cursor branch and never reaches a search). That vacuous pass was observed and closed.

### 7.6 Step 1 — verified, not assumed

`materializeMany`'s task-051 anchor branch DOES hold for a real whole-document payload: a ten-paragraph
agreement, eleven changes (four paraId, two citation, two prose, one dead paraId, one dead citation, one
absent prose), mixed ordering. Eight applied, three refused, per-item isolation intact, sub-keys
index-aligned, banner `failedCount: 3 / totalCount: 11`. Every pending redline corresponds to an
`applied` status and nothing else — the UAT-21 "never report applied for something not placed" check.

### 7.7 Step 6 — nothing retired

`target_text`, `match_mode` / `RedlineMatchMode` / `normalizeMatchMode`, and `resolveTargetSpans` are all
present and exported through the same public surface (`hooks/index.ts` re-export unchanged). Task 052's
scope is untouched.

### 7.8 ADR-040 128 KB cap — this task adds ZERO bytes

`target_para_id` was added to `comments[]` by task 054, not here; task 055 adds no payload field. The
054 model applied to a realistic `flag-risks` payload (30 flags at ~600 B): 18,000 B base, +840 B of
anchors (4.7%), **18.8 KB** — 15% of the cap. The schema-maxima exposure (`target_text` 16,000 +
`comment` 1,500 per item) is pre-existing and unchanged. Escalation trigger 2 did not fire.

### 7.9 Honest gaps

1. **`target_ref` has no producer.** The `compose-revise-document` output schema declares
   `additionalProperties: false` and lists only `target_text` / `comment` / `target_para_id` on
   `comments[]` (and `target_text` / `new_text` / `match_mode` / `target_para_id` on `edits[]`). No
   client capture path sets `target_ref` either. So the citation branch is CAPABILITY, not a live path —
   the same status it has had on the `edits[]` path since task 051 (FR-C02). Live today:
   `target_para_id` on both channels. Adding `target_ref` to the Action schema is a server/infra change
   this task's boundary excludes.
2. **`registerAiReviewComments` still does not export a Word `w:comment`.** Task 040 wired the FR-23
   session threads and the NDA-REVIEW advisory threads into the Save `comments` field; these FR-29
   `AnchoredAnnotation`s remain a separate source and are still out of scope. Unchanged by this task;
   restated so it is not mistaken for something 055 closed.
3. **`target_para_id` is not validated against the reference map.** A bare paraId is treated as the
   address (matching `resolveAnchoredSpans` and the server `ComposeAnchorResolver`), so a hallucinated
   id becomes a hint the re-anchor service will fail to match and then fuzzy-resolve — i.e. exactly the
   pre-055 behaviour. Validating would have rejected legitimately client-minted ids absent from the
   load-time map.
4. **No live UAT.** Everything here is proven in jest against a real headless TipTap editor and the real
   `ComposeWorkspace`/`ComposeEditor`; nothing was exercised against a real model response or a real
   BFF.
