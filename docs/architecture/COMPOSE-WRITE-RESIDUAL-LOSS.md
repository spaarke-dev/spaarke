# Compose Write Fidelity — the residual loss list

> **What Compose does NOT preserve when you save, stated exactly.**
> Write-side companion to [`COMPOSE-READ-REFERENCE-FIDELITY.md`](COMPOSE-READ-REFERENCE-FIDELITY.md)
> (which covers the read path: one reader, text exactness, numbering, references, page/line).
> Governed by [`ADR-049`](../../.claude/adr/ADR-049-compose-shadow-document.md).
>
> **Status**: published 2026-08-23 by `spaarkeai-compose-r8` task 045 (FR-A10);
> **two rows retired 2026-08-24** by task 048 — tabs and symbols moved from §2 (lost) to §3 (carried).
> **Owner sign-off**: ⏳ *pending* — see [Sign-off](#sign-off).
> **Enforced by**: `tests/integration/seam/Compose/ComposeResidualLossParityTests.cs`. This document is
> not maintained by hand-review; a test measures each family through the real renderer and fails if this
> list and the code disagree **in either direction**.

---

## 1. The scope rule — read this before the table

**Loss is per-edited-block, never per-document.**

Compose saves by re-projecting the document you opened and **cloning every block you did not touch,
byte-for-byte**. A block that was not edited is not re-authored, so nothing in it can be degraded —
whether or not Compose understands what is in it. An embedded spreadsheet survives *precisely because*
the save never parses it.

That is why the table below is short, and why it is scoped the way it is:

> Everything in the table is preserved **unless you edit the paragraph it sits in**.

A 40-page agreement with a chart on page 12 is untouched by a save, no matter how much you edit
elsewhere. There is **no per-construct preservation logic and there must not be** (ADR-049) —
preservation is a consequence of not rewriting, not a feature list.

## 2. What is lost, and what you are told

When you edit the paragraph a construct sits in, that paragraph is re-authored from the editor's content
model — and the model does not carry these. Each loss is **reported by name** on the save; none is
silent.

| Construct | What happens when you edit its paragraph | Warning code |
|---|---|---|
| Field (`w:fldSimple`, `w:fldChar`) — page numbers, cross-references, TOC entries | Flattened to the text it was displaying; stops updating | `field-flattened-to-text` |
| Embedded object / image / chart (`w:drawing`, `w:object`, `w:pict`) | Removed from the paragraph. The underlying part stays in the file | `complex-object-dropped` |
| Footnote reference (`w:footnoteReference`) | Reference removed; the footnote text remains in `footnotes.xml` | `unrepresented-footnote-reference` |
| Endnote reference (`w:endnoteReference`) | Reference removed; the endnote text remains in `endnotes.xml` | `unrepresented-endnote-reference` |
| Content control (`w:sdt`) — party name, effective date, dropdown | Flattened to plain text. A **block-level** control keeps its shell where it can be reconstructed; an **inline** one does not | `hard-tier-sdt-flattened` |

**The prior version is always recoverable.** Every save creates a new SPE version, so a paragraph you did
not mean to flatten can be retrieved from version history.

## 3. What survives an edit to its own paragraph

Not everything in an edited paragraph is at risk. These are carried explicitly (task 041), and this half
of the list is enforced too — if a future change starts losing one, the parity test fails:

| Construct | Why it is carried |
|---|---|
| **Bookmarks** (`w:bookmarkStart`/`End`) | Dropping one breaks cross-references **elsewhere in the document** — a silent, non-local failure |
| **Soft line breaks** (`w:br`) | Round-trip as a marker run. Address blocks, party blocks and signature blocks are held together by these, so the paragraphs users edit most were the ones collapsing (fixed task 046) |
| **Tabs** (`w:tab`) | Round-trip as a marker run. Definitions lists, signature blocks and table-of-contents lines are held in alignment by exactly these; flattening one to a space is invisible in a diff and obvious on the page (fixed task 048) |
| **Symbols** (`w:sym`) — §, ¶, Wingdings glyphs | Round-trip as their **font + code point**, not as the glyph the reader resolved for display. § in a legal document is usually Symbol-font `F0A7`, so re-authoring the resolved look-alike would quietly change the character the document contains — and for a code point we cannot resolve, it would have written the on-screen placeholder into the file as content (fixed task 048) |
| **Content-control shell** | The control's identity and binding survive even when its inner content cannot be modelled |
| Paragraph + run properties | Inherited from the base paragraph rather than re-derived |
| Comments, tracked changes, hyperlinks | Carried on the content model itself |

## 4. Known gaps that are not construct loss

Recorded here because they are real and users may hit them, but they are not per-construct degradations:

| Gap | Effect |
|---|---|
| Documents Compose cannot read at all | Open **read-only** with the reason (empty file, not a valid `.docx`, over the paragraph ceiling, unreadable projection, or a non-Word file). Nothing is written. |
| PDF sources | Open as a projection and save as a **new Word document** — the PDF is never written to. Reflow, page chrome and layout approximation are reported at open time as `pdf-intake-*` facts. |
| `.docm` (macro-enabled) | Not accepted. Whether Compose should accept it is an open product question. |

---

## 5. Parity — why you can trust this list

The POML for this deliverable required the parity to be **demonstrated, not asserted**, and a list that
drifts from the code is a promise nobody is keeping. So `ComposeResidualLossParityTests` measures each
family through the real renderer, twice — once with the construct in an untouched block, once in the
edited block — and holds this document to the result in **both** directions:

- every family the renderer degrades **must** appear here, with the exact code it emits;
- every code named here **must** be one the renderer actually produces — this catches a list that drifts
  by accretion, where codes retired from the code are left behind in the document and quietly turn the
  contract into fiction;
- every family the renderer **preserves** must NOT be listed as lost. A list that over-claims is not
  "safely conservative" — it tells you we damage things we do not, which is how a document stops being
  read.

### Measured 2026-08-24 (task 048; `sym` + `tab` rows changed since the 2026-08-23 publication)

| Family | Untouched block | Edited block | Code emitted |
|---|---|---|---|
| `fldSimple` | 1/1 kept | 0/1 | `field-flattened-to-text` |
| `fldChar` | 2/2 kept | 0/2 | `field-flattened-to-text` |
| `drawing` | 1/1 kept | 0/1 | `complex-object-dropped` |
| `object` | 1/1 kept | 0/1 | `complex-object-dropped` |
| `pict` | 1/1 kept | 0/1 | `complex-object-dropped` |
| `footnoteReference` | 1/1 kept | 0/1 | `unrepresented-footnote-reference` |
| `endnoteReference` | 1/1 kept | 0/1 | `unrepresented-endnote-reference` |
| `br` | 1/1 kept | **1/1 kept** | *(none — carried, task 046)* |
| `sym` | 1/1 kept | **1/1 kept** | *(none — carried, task 048)* |
| `tab` | 1/1 kept | **1/1 kept** | *(none — carried, task 048)* |
| `sdt` (inline) | 1/1 kept | 0/1 | `hard-tier-sdt-flattened` |
| `bookmarkStart` | 1/1 kept | **1/1 kept** | *(none — carried)* |

**Untouched-block preservation is 100% for every family without exception.** That is the scope rule in
§1, measured rather than argued.

### The check earned its keep on its first run

The inline `w:sdt` row above was **found by this parity check, not written into it**. Content controls
inside a paragraph were being dropped in complete silence — `edited: 0/1 kept · codes: (none)` — because
only the *block-level* control had a carry, and the inline one was on no taxonomy list. That is the exact
failure this project exists to end, sitting in the save path unnoticed, and a hand-written residual list
would have inherited the same blind spot: you cannot document a loss you do not know you have.

Fixed in task 045 by adding `sdt` to the reportable set and reusing the code whose client copy already
said the right thing.

And on its second run it caught the document going stale in the other direction: task 046 taught soft
line breaks to round-trip, and the parity check failed because this document still listed
`edited-paragraph-line-break-dropped` as a loss that no longer happens. That is the accretion failure the
both-directions rule exists for — a list that keeps claiming losses the code has already fixed looks
maintained while quietly becoming fiction.

Corroborating corpus evidence (23 documents, `tests/fixtures/compose-corpus/`): 100% overall and 100%
near-tier preservation, 100% strict on 16 of 18 of the original set and on all four of the construct
fixtures added in task 043; zero hard-fails.

---

## 6. Sign-off

FR-A10 requires owner sign-off, and an unsigned list does not complete the task.

| Field | Value |
|---|---|
| Version | 2026-08-23 (first publication) |
| Measured against | `spaarkeai-compose-r8` @ task 045, corpus of 23 documents |
| Signed off by | ⏳ *pending* |
| Date | ⏳ *pending* |

**What signing means**: that the losses in §2 are acceptable to ship *given* they occur only in the
paragraph a user edits and are reported by name every time. Declining any single item makes it a scope
question — fix it — rather than a documentation revision (ADR-049 / task 045 escalation).

## 7. Related

- [`COMPOSE-READ-REFERENCE-FIDELITY.md`](COMPOSE-READ-REFERENCE-FIDELITY.md) — the read path
- [`ADR-049`](../../.claude/adr/ADR-049-compose-shadow-document.md) — the governing decision + seven invariants
- `projects/spaarkeai-compose-r8/notes/` — `gate-decision.md`, `merge-mechanism-results.md`,
  `edited-block-loss.md`, `capability-gate-triggers.md`
