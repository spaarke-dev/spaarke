# G2 Clean-Apply Decision (R5-D2) — Phase-0 Spike

> **Task**: 003 (`spaarkeai-compose-r5`) — decides R5-D2, **gates task 021** (G2 implementation).
> **Authored**: 2026-07-29 · **Rigor**: FULL · **Model**: opus @ xhigh
> **Spec**: FR-02 (G2 clean, non-tracked apply for AUTHORED docs) · design R5-D2 · Owner Clarifications (R5-D2 = Phase-0 spike).
> **Status**: ✅ DECIDED — **Candidate A (engine clean-apply BRANCH)**. No escalation (both criteria satisfiable; A satisfies all three).

---

## Decision

**A — add a clean-apply BRANCH (mode flag) to `ComposeShadowPatchEngine` that emits plain runs for an
AUTHORED doc's own edits (no `w:ins`/`w:del`), resolving by `(paraId, runIndex, offset)` exactly as the
tracked path does.** Reject **B (re-author-from-content-model each save)**.

The winner produces ZERO tracked-change markup for an authored doc's own edits AND keeps the corpus
byte-diff harness green (untouched parts + untouched paragraph subtrees byte-identical), on **real bytes**,
across **all 9 corpus docs + a born-in-editor fixture** — proven below. It does NOT merge the two
byte-authors: the clean branch stays *inside* the engine (still the delta-applier over retained bytes); the
renderer stays the from-scratch author. The renderer-clean / engine-delta split (R4 036/037, ADR-049
D5/I-5 path-A exception) is untouched.

---

## The two options (R5-D2)

| | **A — engine clean-apply branch** | **B — re-author from content model** |
|---|---|---|
| Mechanism | A `trackChanges:false` mode on `ComposeShadowPatchEngine.Apply`. `ApplyInsertText` emits a plain `w:r` (not `InsertedRun`/`w:ins`); `WrapRunAsDeleted` physically removes the run (not `DeletedRun`/`w:del`); `ApplyReplaceRange` = remove + plain insert. Everything else (resolve-by-id, split-run-at-offset, atom refusal, I-7 no-text-search) is the SHIPPED tracked spine, unchanged. | Each save, rebuild the whole `.docx` from the editor `ComposeContentModel` via `ComposeDocumentRenderer.SynthesizeDocument` (`BuildRun:366` / `AssembleParagraph:346`) — the clean byte-author. |
| Substrate | The **retained authoritative bytes** (surgical DOM mutation; only `document.xml` re-serialized; all other parts copied verbatim by the SDK). | **No retained substrate** — the package is re-derived from the (lossy) content-model projection. |
| Tracked markup | Zero (plain runs). | Zero (renderer never emits track changes). |
| I-1/I-2/I-4 | Honored — no re-derivation, untouched subtrees byte-identical. | **Violates I-4** ("untouched OOXML subtrees MUST remain byte-identical") and the I-1/I-2 "MUST NOT re-derive the `.docx` from the editor model on save" prohibition. |

Both candidates pass **criterion 1** (zero tracked markup). The decision turns on **criterion 2** (corpus
byte-diff harness green) and **criterion 3** (don't merge the byte-authors).

---

## Byte-diff evidence

**Method.** A throwaway spike (`tests/integration/seam/Compose/_Spike003G2CleanApply.cs`, since reverted)
drove the PRODUCTION engine + renderer over real bytes and compared with the SAME production comparer the
corpus harness uses (`ComposeOoxmlPackagePartComparer`). A born-in-editor fixture was authored via
`ComposeDocumentRenderer` (a clause heading + two body paragraphs + a restart-scoped ordered list —
exercising the style-linked numbering keystone). The engine gained a temporary `trackChanges` flag for
Candidate A; it was reverted after evidence capture (mainline unchanged). 11/11 spike assertions passed.

### Candidate A — engine clean branch (`trackChanges:false`)

**Born-in-editor doc, one authored insert at the first paragraph:**
```
tracked markup (w:ins/w:del) AFTER edit : 0          ✅ criterion 1
all untouched parts byte-identical       : True       ✅ criterion 2 (NFR-01)
untouched-para OuterXml identical        : True       ✅ criterion 2 (I-4)
```

**Full corpus (all 9 fixtures), one clean insert each** — every row `trackedBefore=0 trackedAfter=0`,
`untouchedPartsIdentical=True`, `untouchedParasIdentical=True`:
```
[A/corpus] 01 - Test Matter Create Fields Only.docx            trackedAfter=0 untouchedPartsIdentical=True untouchedParasIdentical=True
[A/corpus] PAT 109270W-1 ... CLAIMS track changes ...docx      trackedAfter=0 untouchedPartsIdentical=True untouchedParasIdentical=True
[A/corpus] Engagement Letter.docx                              trackedAfter=0 untouchedPartsIdentical=True untouchedParasIdentical=True
[A/corpus] heading-style-numbering.docx                        trackedAfter=0 untouchedPartsIdentical=True untouchedParasIdentical=True
[A/corpus] line-numbered-pleading.docx                         trackedAfter=0 untouchedPartsIdentical=True untouchedParasIdentical=True
[A/corpus] multilevel-1-1-1.docx                               trackedAfter=0 untouchedPartsIdentical=True untouchedParasIdentical=True
[A/corpus] nda-interrupted-clauses.docx                        trackedAfter=0 untouchedPartsIdentical=True untouchedParasIdentical=True
[A/corpus] symbol-section-mark.docx                            trackedAfter=0 untouchedPartsIdentical=True untouchedParasIdentical=True
```
> The comparer's whole-document `DocumentXmlStructurallyFaithful=False` line is EXPECTED and correct — it
> counts the *edited* paragraph as a divergence. The harness gate is *untouched* parts + *untouched*
> paragraph subtrees byte-identical (exactly what `ComposeShadowPatchEngineByteDiffSeamTests.InteriorInsert…`
> asserts), and both are `True` for every doc.

The shipped byte-diff harness (`ComposeShadowPatchEngineByteDiffSeamTests`, default tracked path) stayed
**16/16 green** with the additive flag present — the clean branch is opt-in; the tracked default is untouched.

### Candidate B — re-author from content model

**Idempotency (re-author the SAME born model twice, NO edit):**
```
document.xml byte-identical               : True
all paragraphs OuterXml identical         : True
all untouched parts byte-identical        : True
```
**Honest finding:** for a born-in-editor doc whose entire content is expressible in `ComposeContentModel`
AND whose paraIds are all client-carried, the renderer is byte-**idempotent**. So B is *not* disqualified by
byte drift on the trivial born case. B is disqualified by the two facts below.

**(1) Vocabulary ceiling — the decisive corpus failure.** Inventory of the richest corpus doc
(`PAT 109270W-1 … CLAIMS`):
```
package parts total          : 16 (document.xml + 15 related)
headers                      : 3
footers                      : 3        (carry the page-number building-block SDT + PAGE field)
has StyleDefinitionsPart     : True
has NumberingDefinitionsPart : True
sectPr (sections)            : 1
total paragraphs             : 108
```
`ComposeContentModel` can express **Paragraph / Heading / ListItem / Table + bold/italic/underline +
alignment** and nothing else. It has **no vocabulary** for headers, footers, fields, SDT content controls,
section properties, custom styles, `w:rsid*`, colors/fonts/sizes, comments, or hyperlinks. A re-author save
therefore **drops or regenerates** those 15 related parts + the per-paragraph rsid/section markup — so the
corpus byte-diff harness (which requires **untouched parts byte-identical**) goes **RED** the moment an
authored doc contains any construct outside that narrow set. Real authored legal docs will, and later R5
gaps (G3 numbering, G5 hyperlinks) add still more constructs the content model can't carry.

**(2) ADR-049 I-1/I-2/I-4 prohibition.** B *is* "re-derive the `.docx` from the editor model on save" —
verbatim the R1–R3 fidelity-loss root cause the ADR forbids. Byte-idempotency on a trivial doc does not cure
the architectural violation; it just hides it until the first non-representable construct appears.

---

## Why A wins (mapped to the three criteria)

1. **Zero tracked markup for an authored doc's own edits** — PROVEN, 0 across born doc + all 9 corpus docs.
2. **Corpus byte-diff harness green** — PROVEN, untouched parts + untouched paragraph subtrees byte-identical
   across all 9 corpus docs + born doc. A reuses the shipped surgical spine (retained bytes, only
   `document.xml` re-serialized, all other parts verbatim), so I-4/NFR-01 hold by construction. B cannot
   clear this bar on rich docs.
3. **Two byte-authors NOT merged** — A is a *mode on the existing engine*, not a new synthesizer and not a
   fold of the renderer into the engine. Renderer = from-scratch clean author; engine = delta applier over
   retained bytes (now with a clean branch). The R4 036/037 split + ADR-049 D5/I-5 path-A exception stand.
   B would have *expanded* the renderer into a general save-path author — more ADR tension, not less.

**Escalation trigger did NOT fire:** A keeps the harness green for authored round-trips, so there is no
"both approaches lossy" condition. Proceed under existing ADR-049 (no amendment, no new exception needed).

---

## Placement Justification (CLAUDE.md §10 — BFF=Y)

- **Where the code lives:** `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeShadowPatchEngine.cs`
  (existing file). G2 is a **modification** of the existing byte-author, not a new service/endpoint/DI
  registration — no Component Justification (§11) required (rule applies to NEW surface only).
- **Purity intact:** engine stays `byte[]`-in/`byte[]`-out; no `IOpenAiClient`/routing type (ADR-013
  Tier-1 NetArchTest), no `Microsoft.Graph` above `SpeFileStore` (ADR-007), stateless concrete singleton
  (ADR-010 — the flag is a per-`Apply` argument threaded to the per-call `PatchSession`, no instance state).
- **Publish-size:** zero new runtime package (the flag + branch are pure C#). No size delta. (No committed
  engine change in THIS spike — task 021 will add ~40 LOC to an existing file; still zero package delta.)
- **Seam DoD (ADR-038):** task 021 adds a born-in-editor clean-apply seam slice under
  `tests/integration/seam/Compose/**` (see fixture note below). No `Mock<HttpMessageHandler>`, DI-registration,
  or ctor-null tests.

---

## Implementation contract for task 021 (G2)

Task 021 MUST implement Candidate A exactly as prototyped and byte-proven here:

1. **Engine surface.** Add a clean-apply mode to `ComposeShadowPatchEngine.Apply(...)`. Prototype used a
   `bool trackChanges = true` trailing parameter threaded to `PatchSession`; task 021 MAY instead route it
   through the op-log / a `ComposeApplyOptions` record if cleaner — but the DEFAULT MUST remain the tracked
   path (backward compatible; the shipped 16/16 harness must stay green unchanged).
2. **Clean appliers (the only behavioral change).** When clean mode is on:
   - `ApplyInsertText` → insert a **plain `w:r`** at the anchor (mirror `InsertInsAt` for a `Run` instead of
     wrapping in `InsertedRun`). No `w:ins`.
   - `WrapRunAsDeleted` → **physically `run.Remove()`** the covered run(s). No `w:del`/`w:delText`.
   - `ApplyReplaceRange` → remove covered runs + insert a plain run at range start. No redline.
   - Structural ops (task 031 `split`/`merge`/`insert`/`deleteParagraph`) in clean mode: apply the node
     change WITHOUT the tracked para-mark revision (`MarkParagraphMark` no-op / direct node edit). G2's MVP
     is text/mark clean-apply; if G3/G4 clean structural editing is in 021's scope, apply the same
     "mutate node, skip the `w:ins`/`w:del` mark" rule. If out of scope, keep clean mode text/mark-only and
     refuse clean structural ops explicitly (do not silently emit tracked markup).
3. **Invariants that MUST hold (unchanged from the tracked path):**
   - **I-7 / NFR-02:** resolve strictly by `(paraId, runIndex, offset)`. **No text-search.** Reuse the exact
     `Resolve` / `FlattenEditorRuns` / `SplitParagraphAtEditorOffset` spine — do NOT add a content-match path.
   - **I-4 / NFR-01:** only `MainDocumentPart.Document` is opened/saved; every other package part stays
     verbatim; untouched paragraph subtrees stay byte-identical. (Proven for clean mode over all 9 corpus
     docs.)
   - **Atom / tracked-region refusal:** the shipped `AtomTargeted` and
     `TrackedChangeReconciliationUnsupported` refusals still apply in clean mode (an authored doc editing
     *into* a pre-existing imported redline is still the escalation boundary — clean mode does not relax it).
4. **Routing (who calls clean mode).** The AUTHORED-origin save path calls `Apply(..., clean)`; the
   imported-origin save path keeps the tracked default. Origin is the durable `sprk_composeorigin` marker
   (R5-D1 / task 002 / G1 task 020) — **not** inferred. G2 (021) consumes the origin decision; it does not
   re-derive origin. Rebase onto post-R4.5 `ComposeService.cs` `LoadAsync`/`SaveAsync` (coordination note §2).
5. **MUST NOT:** do not merge the byte-authors; do not route authored origination through the renderer as a
   general save author; do not delete `docxBridge.ts` (G1/G2/G7 depend on `buildContentModel`/`stampParaIds`);
   do not add a second synthesizer.

### Born-in-editor fixture (for the 021 seam slice)

The spike authored its fixture in-process via `ComposeDocumentRenderer.SynthesizeDocument` (a renderer-authored
doc IS a born-in-editor doc). Task 021 SHOULD either (a) reuse that in-process pattern in the seam test, or
(b) drop a committed `born-in-editor-authored.docx` into `tests/fixtures/compose-corpus/` — the
`ComposeCorpusFixtureLocator` glob picks it up automatically (no code change) so the existing corpus
byte-diff harness also exercises it. Prefer (a) for the clean-apply seam slice (deterministic, no LFS asset),
and add (b) only if an owner-supplied authored worst-offender is wanted in the standing corpus.

---

## Files referenced (all absolute)
- Engine (tracked appliers — clean branch target): `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeShadowPatchEngine.cs` (`ApplyInsertText:314`, `WrapRunAsDeleted:942`, `ApplyReplaceRange:363`)
- Clean byte-author (candidate B reference): `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocumentRenderer.cs` (`SynthesizeDocument:102`, `BuildRun:366`, `AssembleParagraph:346`)
- Content model (candidate B input): `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeContentModel.cs`
- Byte-diff comparer + harness: `tests/integration/seam/Compose/ComposeOoxmlPackagePartComparer.cs`, `tests/integration/seam/Compose/ComposeShadowPatchEngineByteDiffSeamTests.cs`
- Corpus locator + fixtures: `tests/integration/seam/Compose/ComposeCorpusFixtureLocator.cs`, `tests/fixtures/compose-corpus/`
- ADR: `.claude/adr/ADR-049-compose-shadow-document.md` · Coordination: `projects/spaarkeai-compose-r5/notes/COORDINATION-with-r4.5.md`
