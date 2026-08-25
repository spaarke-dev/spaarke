# Compose Write Fidelity — the residual loss list

> **What Compose does NOT preserve when you save, stated exactly.**
> Write-side companion to [`COMPOSE-READ-REFERENCE-FIDELITY.md`](COMPOSE-READ-REFERENCE-FIDELITY.md)
> (which covers the read path: one reader, text exactness, numbering, references, page/line).
> Governed by [`ADR-049`](../../.claude/adr/ADR-049-compose-shadow-document.md).
>
> **Status**: published 2026-08-23 by `spaarkeai-compose-r8` task 045 (FR-A10);
> **two rows retired 2026-08-24** by task 048 — tabs and symbols moved from §2 (lost) to §3 (carried);
> **the field row retired 2026-08-25** by task 049 — ordinary Word fields moved to §3, leaving only the
> nested/unterminated case in §2;
> **the embedded-object row retired 2026-08-25** by task 056 — images, charts, shapes and OLE embeds moved
> to §3, leaving only the text-carrying box in §2.
> **Owner sign-off**: ✅ **ACCEPTED 2026-08-25** — see [Sign-off](#sign-off).
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
| **Nested or unterminated field** — `{ IF { PAGE } = 1 … }`, or a field whose `begin`/`end` straddle paragraphs (a `TOC`, an `INDEX`). Ordinary fields are **carried** — see §3 | Flattened to the text it was displaying; stops updating | `field-flattened-to-text` |
| **Text box** (a `w:pict` / `w:drawing` wrapping `w:txbxContent`) — a floating callout or signature block. Text-free objects (images, charts, shapes, OLE embeds) are **carried** — see §3 | The box's **text is kept**, as ordinary prose at the box's position; the box itself — its frame, size, wrap and float — is not | `complex-object-dropped` (plus `text-box-flattened` at open) |
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
| **Fields** (`w:fldSimple`, `w:fldChar`) — `REF`, `PAGEREF`, `PAGE`, `DATE`, `SEQ`, `STYLEREF`, vendor instructions | Round-trip as their **instruction**, alongside the result Word last computed — so the save changes nothing on screen and the field is a field again. `w:fldLock` rides along, because the one way this could be worse than freezing is converting a field the author deliberately locked into a live one. The form the document used (`fldSimple` vs the `fldChar` run sequence) is re-emitted, not normalised. On a KEYSTROKE edit the result's bold/italic/underline are NOT carried (an opaque atom holds no marks), so a bold cross-reference in a plain paragraph returns plain — the field itself survives. **Not** nested or unterminated fields — those stay in §2 (fixed task 049; client half task 057) |
| **Embedded objects** (`w:drawing`, `w:object`, `w:pict`) — a picture, chart, shape or OLE embed | The object's own OOXML subtree is carried **verbatim**, so properties nobody enumerated survive for the same reason cloning an untouched block preserves them. The picture's bytes never travel: they stay in their own package part and only the reference moves, and the save **resolves that reference against the document before authoring it** — a subtree naming a relationship the package does not have is refused rather than written, because a file Word reports as *damaged* is worse than a missing picture. Works for a keystroke edit too, without the object's markup ever reaching the browser: when the posted content model does not carry it, the object is restored from the paragraph's pre-edit base (fixed task 056). **Not** a text box — that stays in §2 |
| **Content-control shell** | The control's identity and binding survive even when its inner content cannot be modelled |
| Paragraph + run properties | Inherited from the base paragraph rather than re-derived |
| Comments, tracked changes, hyperlinks | Carried on the content model itself |

> **What a carried object does *not* keep.** Its POSITION inside the paragraph, in one case. When the
> object round-trips through the content model — every server-side path, including an AI edit — the position
> is exact. When it is restored from the base instead (a keystroke edit, where the editor's opaque atom
> contributes nothing to the posted model) it is placed at the index it held among the paragraph's parts
> before the edit: **exact** for an object alone in its own paragraph, which is the shape a signature image,
> an exhibit chart or an embedded schedule almost always takes, and an approximation for one sitting
> mid-sentence in a paragraph the user rewrote. An approximate position is a smaller loss than deletion,
> which is what it replaces — said here rather than left to be discovered.

> **What a carried field does *not* keep.** The field comes back with its instruction, its cached result and
> its lock; the result text is re-authored with the bold / italic / underline the model carries, so run
> properties on the result beyond those three (`w:noProof`, a character style, a colour) are not restored,
> and a result that was several differently-formatted runs comes back as one. This is the same edited-block
> property tier every other run is subject to — it is stated here rather than left to be discovered, because
> "carried" should not be read as "byte-identical".

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

### Measured 2026-08-25 (task 056; the four object rows changed since the task-049 run)

| Family | Untouched block | Edited block | Code emitted |
|---|---|---|---|
| `fldSimple` | 1/1 kept | **1/1 kept** | *(none — carried, task 049)* |
| `fldChar` | 2/2 kept | **2/2 kept** | *(none — carried, task 049)* |
| `fldNested` — `{ IF { PAGE } = 1 … }` | 6/6 kept | 0/6 | `field-flattened-to-text` |
| `drawing` | 1/1 kept | **1/1 kept** | *(none — carried, task 056)* |
| `object` | 1/1 kept | **1/1 kept** | *(none — carried, task 056)* |
| `pict` | 1/1 kept | **1/1 kept** | *(none — carried, task 056)* |
| `pictTextBox` — a shape wrapping `w:txbxContent` | 1/1 kept | 0/1 | `complex-object-dropped` |
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

And on its fourth run the same mechanism did the work for embedded objects, in both directions at once.
Task 056 taught all three object forms to round-trip, and the check failed in the over-claim direction
until this document stopped calling them lost — while the `pictTextBox` row was added in the same change so
`complex-object-dropped` remains a code the renderer really does emit. That row is not a formality: a text
box's words are already preserved as prose, so carrying the box on top of them would have written the same
sentence into the document twice. The rule that prevents it is shared by both halves of the carry rather
than restated in each, because a boolean that drifts between two files would announce itself as a
duplicated paragraph in a saved agreement.

And on its third it did the same for fields. Task 049 taught both field forms to round-trip, and the
check failed in the over-claim direction until this document stopped calling them lost — while the
`fldNested` row was added in the same change precisely so `field-flattened-to-text` remains a code the
renderer really does emit. Retiring the last producer of a code without noticing would have left the
document naming a warning nothing can raise, which is direction B's failure mode arriving by the front door.

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
| Version | 2026-08-25 (objects carried; supersedes the 2026-08-23 first publication) |
| Measured against | `spaarkeai-compose-r8` @ task 056, corpus of 24 documents |
| Signed off by | **Project owner** — accepted in session, 2026-08-25 |
| Date | 2026-08-25 |


**What was accepted — and what was NOT.** The owner **declined** the original field and embedded-object
rows on 2026-08-25. Those were not signed off; they were **fixed** (tasks 049 + 057 carry fields, task
056 carries objects). This sign-off covers only the five rows remaining in §2:
nested/unterminated fields · text boxes · footnote references · endnote references · content controls.
Two of those five are narrower carve-outs created by the fixes, not pre-existing losses.

**What signing means**: that the losses in §2 are acceptable to ship *given* they occur only in the
paragraph a user edits and are reported by name every time. Declining any single item makes it a scope
question — fix it — rather than a documentation revision (ADR-049 / task 045 escalation).

## 7. Related

- [`COMPOSE-READ-REFERENCE-FIDELITY.md`](COMPOSE-READ-REFERENCE-FIDELITY.md) — the read path
- [`ADR-049`](../../.claude/adr/ADR-049-compose-shadow-document.md) — the governing decision + seven invariants
- `projects/spaarkeai-compose-r8/notes/` — `gate-decision.md`, `merge-mechanism-results.md`,
  `edited-block-loss.md`, `capability-gate-triggers.md`
