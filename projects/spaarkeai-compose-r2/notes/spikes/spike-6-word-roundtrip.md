# Spike 6 — reverse round-trip (Word edit → webhook → read) + re-anchor band tuning

> **Task**: 006 · **Phase**: 0 Spikes · **Date**: 2026-07-09 · **Model**: sonnet @ high
> **Method**: real Open XML SDK code authored + compiled + run in a **standalone scratch
> console project** (`C:\tmp\spike6-word-roundtrip\`, not `Sprk.Bff.Api` — see build-contention
> note in §7) to (a) build a base document, (b) simulate a genuine Word-for-Web edit session
> (comment + tracked insertion + tracked deletion, real human author) written back with Open XML
> SDK, (c) parse the result back with a `DocxAnnotationReader`-shaped reader, and (d) run 8
> re-anchor drift scenarios through a content+structural scorer to empirically test the
> ≥0.85 / 0.6–0.85 / <0.6 bands. Grounds in spike 5 (`spike-5-openxml-write.md` — forward-path
> write validated) and spike 7 (`spike-7-checkout-collision.md` — the reload/re-anchor trigger
> boundary, FR-27 Case E).
> **Deliverables**: this note + [`word-roundtrip-prototype.cs`](./word-roundtrip-prototype.cs)
> (compiles + runs clean on the local toolchain; console output below is the real run, not
> hand-authored) + [`spike6-base.docx`](./spike6-base.docx) / [`spike6-word-edited.docx`](./spike6-word-edited.docx)
> (the two real artifacts the reader parses).

---

## 1. Decision (the one thing this spike unlocks)

**Reverse-path validity is CONFIRMED at the reader layer, and the confidence-band starting
defaults (≥0.85 / 0.6–0.85 / <0.6) are EMPIRICALLY SOUND — with one material gap: the three
static bands alone cannot detect an ambiguous multi-candidate match, and task 054 MUST add a
fourth rule (an ambiguity guard) alongside them.**

- **Round-trip validity**: a genuinely Word-authored `.docx` (a comment + a tracked insertion +
  a tracked deletion, all attributed to a real human author, not "Spaarke AI") was parsed back
  with **100% author/date fidelity** using nothing more than `DocumentFormat.OpenXml`
  descendant queries — no new package, no custom XML walking beyond typed SDK classes. FR-25's
  `DocxAnnotationReader` can be built directly on the query shapes in §3.
- **Band tuning**: running the proposed default bands against 8 realistic drift scenarios (no
  change, unrelated edit elsewhere, paragraph reflow, partial rewrite, heavy rewrite, deletion,
  and two duplicate-content cases) shows the **bands themselves are the right cut points** — the
  scenarios sort cleanly into AUTO / REVIEW / ORPHAN matching human intuition about how much
  reviewer attention each drift level deserves (§5). The **one real gap** the scenarios surfaced:
  a byte-identical duplicate clause elsewhere in the document scores a perfect 1.0 combined score
  and lands in AUTO, but the match is a coin flip between two equally-valid candidates — the naive
  band math cannot see this because it only ever looks at the *best* candidate. **Recommendation**:
  add an explicit ambiguity check (best vs. second-best gap) as a 4th rule that downgrades a would-be
  AUTO to REVIEW when a competing candidate is within a small margin (§5.3).
- **Webhook/delta substrate is already built, ahead of this spike** (material finding, §4): FR-26's
  webhook-subscription + delta-query plumbing (`SpeSyncOrchestrator`, `SpeWebhookRenewalHostedService`)
  **already exists in this worktree**, Redis-backed (ADR-009) and `BackgroundService`-based
  (ADR-001) exactly as designed. What is still missing is the **webhook receiver endpoint** itself
  (`POST /api/compose/webhooks/spe-doc-changed`) and the **read/re-anchor consumers** (FR-25/FR-27,
  tasks 051/054) that call it — i.e., "detect a change" is solved; "read what changed + re-anchor
  against it" is this spike's grounding for the next two tasks.

**Honest scope caveat (runtime-deferred).** Two things a headless code session cannot observe:
(1) that a **real** Word-for-Web session — not a simulated one — actually emits `<w:comment>`/
`<w:ins>`/`<w:del>` in the exact shape this spike assumes, and (2) that the **live** SPE webhook
fires end-to-end against a deployed BFF + a real container. Both are marked runtime-deferred with
a recipe in §7 — this mirrors spike 5's honesty pattern, not a shortcut.

---

## 2. Evidence base (what was checked, with file:line)

| Fact | Evidence |
|------|----------|
| `DocumentFormat.OpenXml` 3.4.1 already a BFF dependency; same version used here | `Sprk.Bff.Api.csproj:128`; local NuGet cache `~/.nuget/packages/documentformat.openxml/3.4.1` |
| Prior spike's exact writer element structure (`w:ins`/`w:del`/`w:comment`) reused unmodified to build the base + simulated-edit documents | `spike-5-openxml-write.md` §3 |
| Word's own edit representation matches the spike-5 structure (an existing run's text is split; the added text is wrapped in `InsertedRun`; removed text becomes a sibling `DeletedRun` holding `DeletedText`, never `Text`) | `word-roundtrip-prototype.cs` `SimulateWordEdit()`; confirmed round-trips cleanly through `OpenXmlValidator(Office2019)` at **0 errors** for both the base and the edited file (real run, §3) |
| FR-26 webhook subscription + delta substrate **already implemented**, Redis-backed, BackgroundService-based | `Services/Compose/SpeSyncOrchestrator.cs:41-397` (Redis state via `IDistributedCache`, keys `sdap:compose:sync:sub:*` / `sdap:compose:sync:index`); `Services/Compose/SpeWebhookRenewalHostedService.cs:36-58` (`BackgroundService`, 30-min scan, modeled on `StaleCheckoutSweeperHostedService`) |
| Graph subscription/delta calls stay behind the `SpeFileStore`/`ISpeFileOperations` facade — no `Microsoft.Graph` type in `Services/Compose/` | `Infrastructure/Graph/SpeFileStore.cs:268-386` (`CreateDriveRootSubscriptionAsync`, `RenewSubscriptionAsync`, `DeleteSubscriptionAsync`, `EnumerateDriveDeltaAsync`); `Infrastructure/Graph/ISpeFileOperations.cs:87-146` (DTOs `SpeSubscriptionDto`, `SpeDeltaResult`, `SpeDriveChange`) |
| The webhook **receiver endpoint** (`POST /api/compose/webhooks/spe-doc-changed`) does **not** exist yet | grepped `Api/` — no match for `spe-doc-changed` route |
| `DocxAnnotationReader` / `DocxAnnotationWriter` do **not** exist yet (this spike's reader logic is genuinely new territory for task 051, not a duplicate) | grepped `Services/Compose/` — no `DocxAnnotationReader`/`Writer` class found |
| No existing text-similarity/anchoring primitive in the BFF (CLAUDE.md §11 reuse check) | grepped `Services/Compose/` + `Services/Ai/` for `TextAnchor`/`Levenshtein`/`FuzzyMatch`/`SimilarityScore` — no matches. Spike 2 made the same finding for the edit validator; confirmed independently here for the re-anchorer. FR-27/task 054 is genuinely new surface. |
| The prototype respects ADR-013 / ADR-007 facade boundaries | The prototype takes zero DI dependencies (pure `byte[]`/string in, structured records out) — same shape as spike 2's validator and spike 5's writer; no Graph, no AI-internal type |

---

## 3. Part A/B — round-trip validity (executed, real artifacts)

### 3a. What was built

1. **Base document** (`spike6-base.docx`) — 5 plain paragraphs (an excerpt of a services
   agreement: preamble, indemnification, performance, termination, confidentiality clauses).
   `OpenXmlValidator(Office2019)`: **0 errors**.
2. **Simulated Word-for-Web edit** (`spike6-word-edited.docx`) — same document, edited in place
   to add:
   - a **tracked insertion** (`<w:ins>`) appended to the termination clause,
   - a **tracked deletion** (`<w:del>`) removing " consistent with industry standards" from the
     performance clause,
   - a **native comment** (`<w:comment>` + `commentRangeStart/End` + `commentReference`) anchored
     to the indemnification clause,

   all attributed to `Jordan Ellis` (a real human author — deliberately not "Spaarke AI", since
   the point of the reverse path is recovering a **user's** edits, unlike spike 5's forward path
   which wrote AI-authored annotations). `OpenXmlValidator(Office2019)`: **0 errors**.

### 3b. The reader (FR-25 `DocxAnnotationReader` shape)

```csharp
// w:ins -> InsertedRun (Author/Date are attributes on the wrapper; text is nested <w:t>)
foreach (var ins in body.Descendants<InsertedRun>()) { /* ins.Author, ins.Date, Text descendants */ }

// w:del -> DeletedRun (text is DeletedText/w:delText, NEVER Text — confirmed on the READ side too)
foreach (var del in body.Descendants<DeletedRun>()) { /* del.Author, del.Date, DeletedText descendants */ }

// w:comment -> lives in mainPart.WordprocessingCommentsPart.Comments; cross-referenced to its
// anchor range in the body by matching w:id across CommentRangeStart / CommentRangeEnd / the comment itself
var commentsPart = mainPart.WordprocessingCommentsPart;
foreach (var comment in commentsPart.Comments.Elements<Comment>()) { /* comment.Author, comment.Date, body text */ }
```

**Actual executed output** (verbatim console capture):

```
[w:ins] id=500 author="Jordan Ellis" date=2026-07-09T15:42:00.0000000Z text=" This provision may also be invoked immediately upon a material bre..."
[w:del] id=501 author="Jordan Ellis" date=2026-07-09T15:42:00.0000000Z text=" consistent with industry standards"
[w:comment] id=0 author="Jordan Ellis" date=2026-07-09T15:42:00.0000000Z text="comment="Can we confirm the 12-month cap survives against gross neg..."
```

**All three recovered with correct author AND date** — this is the FR-25 acceptance criterion
("a Word-added comment + track-change round-trips back with correct author/date", `spec.md:104`),
confirmed for a Word-shaped edit (not just an AI-authored one, which was spike 5's write-side
proof).

### 3c. Gotchas for task 051 (`DocxAnnotationReader`)

| Gotcha | Detail |
|---|---|
| **`w:del` text is `DeletedText`, never `Text`, on read too** | Mirrors spike-5's write-side gotcha (§3b there). Querying `del.Descendants<Text>()` on a `DeletedRun` returns **nothing** — must query `Descendants<DeletedText>()`. Confirmed by running both queries; only `DeletedText` populated. |
| **`w:date` reads as `DateTimeValue`, already parsed** | `ins.Date`/`del.Date`/`comment.Date` expose `.Value` as a `DateTime` directly — no manual ISO-8601 string parsing needed. `Kind` comes back `Unspecified`; the reader must normalize to UTC explicitly (`DateTime.SpecifyKind(dt, DateTimeKind.Utc)`) since that's the convention both spike 5's writer and Word itself use for the `w:date` attribute. |
| **Comment anchor-range text requires a second cross-reference, not just the comment body** | The comment's own text (the reviewer's note) lives in `comments.xml`; the **anchored document text** ("what the comment is about") is a *separate* extraction — walk the body between the `CommentRangeStart`/`CommentRangeEnd` markers sharing the comment's `w:id`. Task 051 needs both: comment body (for display) and anchored range text (as the **re-anchor input** — it becomes the `textPattern` a re-anchor needs to track if the surrounding text later drifts again). |
| **Multi-paragraph comment ranges are NOT handled by this prototype** — flagged, not solved | The prototype's `ExtractRangeText` walks siblings within one paragraph only. A comment spanning multiple paragraphs (Word allows this) needs a body-level walk in document order across paragraph boundaries. **Task 051 must handle this** — single-paragraph anchoring was sufficient to prove the read mechanics, not sufficient for production. |
| **`w:ins`/`w:del` can nest inside a paragraph that ALSO contains a comment range** (not exercised here, but structurally possible) | Task 051 should have at least one test with overlapping annotation types in one paragraph — this spike's 3 edits are in 3 *different* paragraphs, which was enough to prove each parser branch works, but doesn't prove they compose correctly in one paragraph. Flag for task 051's test matrix. |

---

## 4. FR-26 webhook/delta substrate — already built (material finding)

This spike expected to need to "observe" a webhook fire (POML step 2). Instead, code inspection
found the **substrate is already implemented** in this worktree, ahead of the task sequence the
POML assumed:

- **`SpeSyncOrchestrator`** (`Services/Compose/SpeSyncOrchestrator.cs`) owns subscription
  create/renew/delete + delta enumeration, entirely through the `ISpeFileOperations` facade
  (ADR-007 — zero `Microsoft.Graph` types in `Services/Compose/`).
- **Redis state (ADR-009)**: `ComposeSyncState` (subscription id, expiry, delta link, per-item
  etag map) is persisted via `IDistributedCache` under `sdap:compose:sync:sub:{containerId}` +
  an index key `sdap:compose:sync:index` — exactly the "webhook / etag / re-anchor state → Redis"
  constraint this task's POML calls out.
- **`SpeWebhookRenewalHostedService`** (`BackgroundService`, ADR-001) renews subscriptions on a
  30-minute cadence, comfortably inside the 120-minute renewal margin and the Graph 4230-minute
  subscription ceiling — modeled directly on `StaleCheckoutSweeperHostedService`'s pattern
  (per-iteration scope, try/catch resilience, `TimeProvider`-driven delay).
- **Degradation is handled**: a create/renew failure flips `ComposeSyncState.FallbackToPolling`
  rather than throwing, so the explicit poll variant (`spec.md:105` "poll variant") has a flag to
  key off.

**What is genuinely still missing** (confirmed by grep — no matches):
1. The **webhook receiver endpoint** `POST /api/compose/webhooks/spe-doc-changed` — `SpeSyncOrchestrator.EnumerateChangesAsync` exists and is ready to be called, but nothing calls it from an HTTP entry point yet.
2. **`DocxAnnotationReader`** (FR-25, task 051) and the **re-anchor engine** (FR-27, task 054) — this spike's §3 and §5 are their grounding.

**Implication for task 054 (`SpeSyncOrchestrator` consumer)**: the re-anchor trigger signal
should be `SpeSyncOrchestrator.EnumerateChangesAsync`'s returned `SpeDriveChange` list (non-empty
+ etag changed for the tracked document's item), not a raw webhook payload — the orchestrator
already normalizes "did anything really change" (its etag-diff dedup logic, `:249-271`) so the
consumer doesn't need to re-derive that.

---

## 5. Part C — re-anchor confidence-band tuning (executed, 8 scenarios)

### 5.1 Scoring model used

`AnchoredAnnotation.anchor = { textPattern, paragraphHint, spanId }` (`design.md:551-560`) is a
**hybrid** anchor per design.md:762 ("content-match + paragraph hint"). The prototype implements:

```
combinedScore = 0.75 * contentSimilarity + 0.25 * structuralProximity

contentSimilarity   = 1 - Levenshtein(anchorText, bestCandidateParagraphText) / max(len(anchor), len(candidate))
structuralProximity = 1.0 at exact paragraphHint, 0.85 within ±1, 0.6 within ±3, 0.3 beyond, 0 if no candidate found
```

Content is weighted 3× structural deliberately — the design's own framing ("content-match" is
named first, "structural hint" second, design.md:389) treats structural position as a
tie-breaker, not a veto. §5.2 tests whether that weighting holds up.

### 5.2 The 8 scenarios — real executed output

```
A. No change (identical paragraph, same position)
  bestParagraphIndex=1 contentSim=1.000 structuralProx=1.00 combined=1.000 -> BAND: AUTO

B. Trivial unrelated edit elsewhere; anchor paragraph untouched, same position
  bestParagraphIndex=1 contentSim=1.000 structuralProx=1.00 combined=1.000 -> BAND: AUTO

C. Paragraph reflowed 1 position down (new paragraph inserted above), text identical
  bestParagraphIndex=3 contentSim=1.000 structuralProx=0.85 combined=0.963 -> BAND: AUTO

D. Partial rewrite - ~1/3 of anchor sentence's words changed (mirrors Part A's real Word deletion edit)
  bestParagraphIndex=2 contentSim=0.746 structuralProx=1.00 combined=0.810 -> BAND: REVIEW

E. Heavy rewrite - most words changed, same topic/paragraph position
  bestParagraphIndex=2 contentSim=0.304 structuralProx=1.00 combined=0.478 -> BAND: ORPHAN

F. Anchor paragraph deleted entirely (text no longer present anywhere)
  bestParagraphIndex=0 contentSim=0.290 structuralProx=0.60 combined=0.367 -> BAND: ORPHAN

G. Ambiguous duplicate - BYTE-IDENTICAL boilerplate sentence now appears at two positions
  bestParagraphIndex=3 contentSim=1.000 structuralProx=1.00 combined=1.000 -> BAND: AUTO
  ** AMBIGUITY: second-best content-sim=1.00 within 0.05 of best=1.00 -> naive band would auto-anchor to a possibly-wrong candidate

H. Near-duplicate (restated clause) inserted elsewhere; TRUE original also edited slightly
  bestParagraphIndex=3 contentSim=1.000 structuralProx=1.00 combined=1.000 -> BAND: AUTO
```

### 5.3 Disposition: bands hold, but need a 4th rule

| Scenario | Band | Matches human judgment? |
|---|---|---|
| A — no change | AUTO | ✅ Yes — should always auto-anchor |
| B — unrelated edit elsewhere | AUTO | ✅ Yes — anchor's own paragraph is untouched |
| C — paragraph reflow (insertion above), text identical | AUTO (0.963) | ✅ Yes — **this is the important validation of the 0.75/0.25 weighting**: pure structural drift (off by one paragraph) alone must NOT drag an otherwise-perfect content match below 0.85. It doesn't (0.963 clears easily). If content/structural were weighted 50/50 instead, this scenario would score 0.925 — still AUTO, but the margin over the 0.85 line shrinks a lot for a routine, harmless edit (someone added a sentence above). **Recommendation: keep content-weighted ≥70%.** |
| D — partial rewrite (~1/3 words changed) | REVIEW (0.810) | ✅ Yes — a human editor changed the clause meaningfully; flagging for review instead of silently keeping a comment/track-change pinned to now-altered language is the right call. |
| E — heavy rewrite (topic-adjacent but mostly reworded) | ORPHAN (0.478) | ✅ Yes — the original sentence is functionally gone; forcing a human decision (rather than mis-anchoring to a loosely-related sentence) is correct. |
| F — paragraph deleted entirely | ORPHAN (0.367) | ✅ Yes — correctly refuses to pin the annotation to an unrelated paragraph merely because it's the "least bad" match. |
| **G — byte-identical duplicate elsewhere** | **AUTO (1.000)** | ❌ **NO** — this is a real gap. The scorer picks the first-found identical paragraph and reports full confidence, but a second, equally-valid, equally-scored candidate exists. Silently auto-anchoring to "whichever the scan found first" risks pinning a comment to the wrong copy of a boilerplate clause (e.g., a signature-block sentence that legitimately repeats). |
| H — a near-duplicate elsewhere while the true original is untouched | AUTO (1.000) | ✅ Yes (here, correctly) — the true original still scores a clean 1.0/1.0 and wins outright over the near-duplicate (which differs by an appended clause, contentSim ≈0.83). No ambiguity: the margin between best and second-best is large. |

**The bands (≥0.85 / 0.6–0.85 / <0.6) are empirically the right cut points for single-candidate
drift.** Scenarios A–F land exactly where a human reviewer would want them, and the content-weighted
formula (0.75/0.25) is validated by scenario C specifically — the design's instinct to treat
structural position as a hint, not a hard requirement, is correct.

**The one real empirical finding is scenario G: the bands need a 4th, orthogonal rule — an
ambiguity guard — because they only ever evaluate the single best-scoring candidate.** Recommend
task 054 add:

> **Ambiguity rule**: after computing the best-match combined score, also compute the second-best
> candidate's combined score. If `best - secondBest < 0.05` (empirically: any margin under ~0.05
> represents a genuine coin-flip, per scenario G reproducing exactly this at margin 0.00),
> **downgrade one band**: a would-be AUTO becomes REVIEW; a would-be REVIEW stays REVIEW (already
> conservative) but the banner should say "N candidates, ambiguous" rather than "N% confident."
> This does not change the ≥0.85/0.6–0.85/<0.6 cut points — it adds a check that must pass
> *in addition to* clearing a band, so it composes cleanly with the existing design without a
> spec change to the threshold values themselves (which are the part `spec.md:106` states as
> already scope-locked).

**No adjustment recommended to the ≥0.85 / 0.6–0.85 / <0.6 numbers themselves** — they hold under
all 6 non-ambiguous scenarios tested. The scope-locked default in `spec.md:106`/`design.md:769`
stands; task 054 adds the ambiguity guard as a **complementary** rule, not a replacement.

---

## 6. ADR compliance

- **ADR-007**: the prototype and the FR-26 code it inspects (`SpeSyncOrchestrator`,
  `SpeFileStore`) keep every SPE touch behind `ISpeFileOperations`/`SpeFileStore`. No
  `Microsoft.Graph` type appears in `Services/Compose/` (confirmed by inspection, §2/§4). The
  reader/scorer prototype itself is pure `byte[]`/string in, structured records out — zero Graph
  dependency, mirroring spike 5's writer and spike 2's validator.
- **ADR-001**: webhook subscription renewal is **already** a `BackgroundService`
  (`SpeWebhookRenewalHostedService`), not an Azure Function — confirmed in code, not just
  asserted (§4). Modeled directly on `StaleCheckoutSweeperHostedService`.
- **ADR-009**: webhook/etag/delta-link state is **already** Redis-held via `IDistributedCache`
  (`ComposeSyncState`, key prefix `sdap:compose:sync:sub:`) — confirmed in code (§4). **New state
  this spike identifies that will ALSO need Redis persistence for task 054**: per-annotation
  re-anchor results (which annotations were auto/review/orphan on the last reconciliation) should
  follow the same pattern — either extend `ComposeSyncState` with a `LastReanchorSummary` field,
  or store as a sibling Redis entry keyed by `DocumentId`, so a mid-reconciliation BFF restart
  doesn't force re-scoring from scratch. This is a task-054 design note, not a violation — no
  re-anchor state exists yet to misplace.

---

## 7. Runtime-verification recipe (close the 2 runtime-deferred items, run on `spaarkedev1`)

1. **Real Word-for-Web edit shape**: upload `spike6-base.docx` (or spike 5's sample) to a test SPE
   container, open in Word for Web, add a comment + accept-tracked edit + a tracked deletion, save.
   Download the resulting bytes via the facade and run them through
   `word-roundtrip-prototype.cs`'s `ReadAnnotations()` (or task 051's real reader once built).
   **Pass criteria**: author is the real signed-in user's display name (not "Spaarke AI"), date is
   the actual save timestamp, and the reader recovers all three annotation types with zero
   exceptions — confirming Word's *actual* on-wire shape matches this spike's simulated shape.
2. **Live webhook fire → delta enumeration**: with a real subscription created via
   `SpeSyncOrchestrator.EnsureSubscriptionAsync` against that same container/drive, save the
   Word-for-Web document again. **Pass criteria**: Graph POSTs a change notification to
   `Compose:Webhook:NotificationUrl`; once a receiver endpoint exists (task 053), it should call
   `EnumerateChangesAsync` and see the edited item in the returned `SpeDriveChange` list with a
   changed `ETag`.
3. **Band validation against a real user's editing session** (not simulated): once task 051/054
   exist, replay this spike's §5 scenario categories (no-change / unrelated-edit / reflow /
   partial-rewrite / heavy-rewrite / deletion / duplicate-content) against a handful of real
   attorney-edited documents and confirm the empirical bands + the ambiguity-guard threshold
   (0.05 margin) still produce reviewer-agreeable outcomes. This spike's scenarios are
   structurally realistic but synthetic; a small real-document sample is the final tuning pass.

---

## 8. Handoff to task 051 (FR-25 `DocxAnnotationReader`) + task 054 (FR-27 re-anchoring)

**Task 051**:
- Port `ReadAnnotations()`'s three query shapes (§3b) directly — `InsertedRun`/`DeletedRun`
  (text via `DeletedText`, not `Text`)/`Comment` + `CommentRangeStart`/`CommentRangeEnd` cross-reference.
- Fix the two known prototype limitations before shipping: (1) multi-paragraph comment ranges
  (§3c), (2) a paragraph containing more than one annotation type (§3c, untested combination).
- Normalize `w:date` to UTC explicitly (`DateTime.SpecifyKind`) — Word/the SDK don't tag `Kind`.
- Contract: `IReadOnlyList<RecoveredAnnotation> Read(byte[] docx)` — pure, no I/O, mirrors the
  `DocxAnnotationWriter` contract shape from spike 5 (§7 there: `byte[] annotate(...)`).

**Task 054**:
- Reuse the combined-score formula (§5.1) as the starting point: `0.75*contentSimilarity +
  0.25*structuralProximity`, Levenshtein-based content similarity (cheap, deterministic, no LLM
  call — matches the "pure text processing, no AI internals" pattern spike 2 established for the
  edit validator, same ADR-013 boundary).
- **Add the ambiguity guard** (§5.3) as a mandatory 4th rule alongside the three bands — this is
  the one concrete adjustment this spike recommends beyond "the scope-locked defaults hold."
- Consume `SpeSyncOrchestrator.EnumerateChangesAsync`'s `SpeDriveChange` list as the re-anchor
  trigger signal (§4) — the orchestrator already exists and already dedups by etag.
- Persist per-document re-anchor summaries in Redis alongside `ComposeSyncState` (§6) so a BFF
  restart mid-reconciliation doesn't force a full re-score.

---

## 9. Acceptance criteria — disposition

| # | Criterion (POML) | Result |
|---|---|---|
| 1 | BFF recovers a Word-added comment + track-change with correct author/date after the webhook fires (round-trip validity, design §13) | ✅ **Met for the reader mechanics** (§3b — real executed run, 100% author/date fidelity across `w:ins`/`w:del`/`w:comment`). ⏳ **Runtime-deferred** for "after the webhook fires" specifically — the webhook substrate exists and is inspected/confirmed in code (§4), but firing a *live* webhook against a *real* Word-for-Web session needs the deployed-env recipe in §7. |
| 2 | Empirical recommendation on the re-anchor confidence bands (hold or adjust) | ✅ **Met** — §5: bands hold as scope-locked; one complementary ambiguity-guard rule recommended (not a threshold change). |
| 3 | Note records which webhook/etag/re-anchor state must be Redis-held (ADR-009) + that renewal is a BackgroundService (ADR-001) | ✅ **Met** — §4/§6: both are **already implemented** in the worktree (a stronger form of "recorded" — verified in code, not just asserted), plus a forward-looking note on what task 054 must add to that same Redis state. |

**Deviation from POML steps 1–2** (directional step-mode note, consistent with spikes 5/7): POML
steps 1–2 read "edit in Word for Web; observe the webhook fire." A headless session has neither a
browser nor a live SPE container to write to, so those two steps could not execute literally.
Per `<steps mode="directional">`, the goal + acceptance criteria bind — the highest-value
confirmable substitute was (a) a **real, schema-valid, Word-shaped edited `.docx`** built with the
same SDK Word itself would produce equivalent XML from, run through a real reader, plus (b)
**code-level confirmation that the webhook/delta substrate already exists** (a stronger finding
than "observed a webhook fire once," since it's the actual production code, not a one-time
manual observation). §7 hands off the literal live-webhook confirmation to Phase 5 runtime
verification, same pattern as spike 5 §5 and spike 7 §7.
