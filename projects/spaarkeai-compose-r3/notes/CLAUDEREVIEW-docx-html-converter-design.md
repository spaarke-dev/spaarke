# Review — Server-Side DOCX→HTML Conversion Design (`design-server-side-docx-html-conversion.md`)

> **Status**: APPROVED WITH FINDINGS — design accepted; Findings F-01 and F-02 must be resolved in the spec (with fixtures) **before** implementation begins. F-03 is a small in-scope addition. F-04 is a recorded UX decision that may be deferred.
> **Reviewed**: 2026-07-21. Branch `work/spaarkeai-compose-r3`.
> **Reviewer role**: senior full-stack AI developer, Microsoft stack.
> **Evidence tags**: `[Cited]` = grounded in the design note or named source files; `[Judgment]` = reviewer assessment; `[Open]` = requires decision or validation. All references to code not present in the design note carry `[VALIDATION NEEDED]`.

---

## 1. Overall verdict

**Accept the design.** `[Judgment]`

- The diagnosis is correct: position-indexed alignment between two independent flattening engines (server `ParaIdPreParser` walk vs. client mammoth flatten) is a structurally unfixable bug class. `ignoreEmptyParagraphs:false` patched one symptom of an unbounded set. `[Cited: design §1]`
- Single-walk, ids-aligned-by-construction is the durable fix. `[Judgment]`
- The alternatives analysis is sound. In particular, the rejection of a client-side OOXML walker is correct — it relocates the same drift class rather than eliminating it. `[Cited: design §2.1]`
- §4.1 (paraId extension `parseHTML` reads `data-paraid`, never renders it back) is the right kind of load-bearing verification to have confirmed pre-design. `[Cited: design §4.1; paraIdExtension.ts:77-80 — VALIDATION NEEDED]`
- Governance framing as a §6.5 Path A spike reversal (not an ADR violation) is appropriate: client-side conversion was a spike-1 decision, not an ADR MUST. NFR-01 (publish size, no SkiaSharp) and NFR-03 (no TipTap Pro / no AGPL) are honored. `[Cited: design §6]`

---

## 2. Findings

### F-01 (HIGH) — Walk-mirroring claim requires adversarial proof

**Problem.** The correctness guarantee rests on the converter's structural walk emitting paragraph blocks in *exactly* the order `ParaIdPreParser`'s `body.Descendants<Paragraph>()` enumerates them. `[Cited: design §3.1]` But `Descendants<Paragraph>()` recurses into constructs that a naive walk over `body` block children can silently skip or mis-order: `[Judgment]`

- **Structured document tags** (`w:sdt` content controls) — ubiquitous in legal documents (clause automation, form fields); may wrap whole paragraphs, table rows, or runs.
- **Text boxes** (`w:txbxContent` inside `w:drawing`) — contain `<w:p>` elements that `Descendants` reaches.
- **Nested tables** — `<w:tbl>` inside `<w:tc>`.

If the pre-parser counts a paragraph the converter's walk does not visit (or visits in a different order), count/order drift returns — the exact bug class this design exists to eliminate, now hidden behind a stronger correctness claim. `[Judgment]`

**Note.** The OOB subset being LOCKED constrains what the converter *renders*; it does not constrain what appears in customer documents that must be *counted*. Rendering may degrade; counting may not. `[Judgment]`

**Required actions.**
1. Spec must state the traversal rule explicitly and prove (by construction or by shared enumeration) that the converter's paragraph sequence is identical to the pre-parser's `Descendants<Paragraph>()` sequence — including inside `w:sdt`, `w:txbxContent`, and nested tables. Preferred: derive both from a **single shared enumeration** so identity is by construction, not by parallel maintenance. `[Judgment]`
2. Add adversarial fixtures to §8 (server golden tests), each asserting `data-paraid` block count == `ParaIdMap.Count` and id-order identity:
   - `fixture-sdt-wrapped-paragraphs.docx` (block-level and inline SDTs)
   - `fixture-nested-table.docx`
   - `fixture-textbox-content.docx`
   - `fixture-tracked-paragraph-mark-deletion.docx` (see F-02)

---

### F-02 (HIGH) — "Flattened to settled prose" is under-specified for paragraph-level revisions

**Problem.** Design §3.2 says `w:ins`/`w:del` are "flattened to settled prose … exactly as mammoth did." `[Cited: design §3.2]` Two issues: `[Judgment]`

1. A fully deleted paragraph (`w:del` on the paragraph mark, deleted runs) is still a `<w:p>` that `Descendants<Paragraph>()` enumerates — the pre-parser presumably assigns it an id. If "settled" means *deletions accepted*, the converter emits nothing for that paragraph and block count no longer equals `ParaIdMap.Count`. Drift returns.
2. The `ImportedRevisions` overlay re-applies deletions as client-side marks. `[Cited: design §5]` A deletion mark needs the deleted text **present in the base HTML** to anchor to. Therefore "settled" almost certainly must mean **"all text present, revision wrappers stripped to plain runs"** — i.e., deleted text emitted as normal text — *not* "revisions accepted."
3. "Exactly as mammoth did" is not a spec: mammoth's revision behavior is precisely the dependency being removed. The rule must be stated normatively, independent of mammoth. `[Judgment]`

**Required actions.**
1. Spec must define the flattening rule per revision type:
   - `w:ins` runs → emit text (wrapper stripped).
   - `w:del` runs (`w:delText`) → emit text (wrapper stripped). `[Open — confirm this matches what `applyImportedRevisions` expects as its anchor base; VALIDATION NEEDED against the overlay pipeline]`
   - Paragraph-mark deletion → paragraph **emitted** with its `data-paraid` (possibly empty), preserving count alignment. `[Open — confirm pre-parser ids these paragraphs]`
2. Fixture: document containing an inserted paragraph, a fully deleted paragraph, and a paragraph with both inline `w:ins` and `w:del` — assert count alignment and that the overlay pipeline re-applies marks correctly end-to-end.

---

### F-03 (MEDIUM) — Promote the alignment invariant from test-time to runtime

**Problem.** The §8 golden test asserts `data-paraid` block count == `ParaIdMap.Count`, which is the right invariant — but only for fixtures someone thought to write. F-01's construct list cannot be exhaustive; unknown OOXML constructs in customer documents remain the residual risk. `[Judgment]`

**Required actions.**
1. In `ComposeService.LoadAsync` (or the converter), assert the invariant at **runtime on every load**: emitted `data-paraid` count == `ParaIdMap.Count`.
2. On mismatch: emit a telemetry metric (counts only — no document content; Tier-1/Tier-3 privacy posture preserved `[Cited: design §3.1 Privacy]`), then degrade per the existing best-effort contract (empty `Html`, client falls back). `[Judgment]`
3. Rationale: converts residual unknown-construct risk from "silent drift discovered at save time by a user" into "observed at load time by engineering." Zero new dependencies; a counter and a comparison. `[Judgment]`

---

### F-04 (LOW) — "Never a save failure" hides a review-integrity gap; record the UX decision

**Problem.** Because save is delta-onto-retained-original keyed by paraId, a paragraph the converter fails to emit is preserved untouched in the saved DOCX — no data loss. `[Cited: design §5, §7 row 1]` Good property. But it also means a user can review and approve a document in Compose **without seeing content that exists in the file**. For a legal review surface this is a trust problem, not a rendering problem. `[Judgment]`

**Required actions.**
1. Record the UX decision now, even if implementation is deferred: when the F-03 runtime invariant fails (or rendering degraded), surface a banner — e.g., *"This document contains content Compose can't display — open in Word to review fully"* — riding the existing FR-12 `Open in Word` escape hatch. `[Cited: design §7 row 1 for FR-12]`
2. This aligns with the platform's no-fabrication / trust-surface principles: the surface must not present an incomplete document as complete. `[Judgment]`

---

## 3. Minor confirmation (not a finding)

- **Hyperlinks require the relationship part.** Emitting `<a href>` from `<w:hyperlink>` requires resolving `r:id` against `MainDocumentPart.HyperlinkRelationships`. Trivially available since the package is already open, but the spec's "pure `byte[]`-in / `string`-out" framing should not be read as body-XML-only. Add one line to §3.1 stating the converter operates on the opened `WordprocessingDocument` / `MainDocumentPart`. `[Judgment]`

---

## 4. Implementation order (amends design §9)

1. Resolve F-01 and F-02 in the spec (traversal rule + revision-flattening rule) and add the four adversarial fixtures + revision fixture. **Gate: spec sign-off.**
2. Implement converter with shared-enumeration traversal (F-01 action 1 preferred form).
3. Add F-03 runtime invariant + telemetry metric in `LoadAsync`.
4. Proceed with design §9 steps 2–6 as written (contract field, client rewire, mammoth removal, publish-size check, coordinated deploy, CIPO re-UAT).
5. F-04 banner: implement with, or immediately after, the F-03 invariant — decision recorded now either way.

---

## 5. Out of scope for this review

- The delta-save synthesizer (`ComposeParagraphRedlineSynthesizer`) — untouched by the design and by this review. `[Cited: design §5]`
- Multi-level list degradation behavior (spike §3.2 reference) — accepted as-is. `[Cited: design §3.2]`
- Removal timing of the graceful-degradation net (`3fd00afad`) — keep until F-03 telemetry shows zero mismatches over a meaningful UAT window. `[Judgment]`
