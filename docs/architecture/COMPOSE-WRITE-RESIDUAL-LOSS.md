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
> to §3, leaving only the text-carrying box in §2;
> **no row changed 2026-08-26** by task 047b — the table below is exactly as it was signed, but the promise
> the table rests on was repaired: two save paths could drop a construct **without naming it**. See
> [§5, "The hole under the list"](#the-hole-under-the-list-found-2026-08-25-closed-2026-08-26);
> **the nested-field half retired 2026-08-26** by task 058 — a conditional merge block
> (`{ IF { MERGEFIELD State } = … }`, the shape a template is built from) moved to §3, leaving only the
> **unterminated** field in §2.
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
model — and the model does not carry these. Each loss is **reported by name** on the save — at **every**
block position, including the awkward ones where the save cannot tell two of your paragraphs apart
([§5](#the-hole-under-the-list-found-2026-08-25-closed-2026-08-26)) — with a single known exception,
called out explicitly in the last row of the table rather than left for someone to discover.

| Construct | What happens when you edit its paragraph | Warning code |
|---|---|---|
| **Unterminated field** — one whose `begin` and `end` straddle paragraphs (a `TOC`, an `INDEX`). Ordinary and **conditional/nested** fields are carried — see §3 | Flattened to the text it was displaying; stops updating | `field-flattened-to-text` |
| **Text box** (a `w:pict` / `w:drawing` wrapping `w:txbxContent`) — a floating callout or signature block. Text-free objects (images, charts, shapes, OLE embeds) are **carried** — see §3 | The box's **text is kept**, as ordinary prose at the box's position; the box itself — its frame, size, wrap and float — is not | `complex-object-dropped` (plus `text-box-flattened` at open) |
| Footnote reference (`w:footnoteReference`) | Reference removed; the footnote text remains in `footnotes.xml` | `unrepresented-footnote-reference` |
| Endnote reference (`w:endnoteReference`) | Reference removed; the endnote text remains in `endnotes.xml` | `unrepresented-endnote-reference` |
| Content control (`w:sdt`) — party name, effective date, dropdown | Flattened to plain text. A **block-level** control keeps its shell where it can be reconstructed; an **inline** one does not | `hard-tier-sdt-flattened` |
| **Hyperlink display attributes** — `w:docLocation`, `w:tgtFrame`, `w:tooltip`, `w:history`. The link's **target** is carried (§3); these four are not | The link still works and still points where it did; a custom hover tooltip, a target frame, a sub-document location, and the visited-state flag are dropped | **none — this one IS silent** (see the exception below) |

> **The one exception to "nothing is silent" (recorded 2026-08-26, D-1).** The four hyperlink display
> attributes above are dropped without a warning code. They are scalars and carrying them is
> straightforward — this is a known gap, not a designed loss, and it is listed here rather than omitted
> precisely because an unlisted silent loss is the failure mode §5 exists to prevent. Everything else in
> this table is reported by name.

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
| **Fields** (`w:fldSimple`, `w:fldChar`) — `REF`, `PAGEREF`, `PAGE`, `DATE`, `SEQ`, `STYLEREF`, `MERGEFIELD`, vendor instructions | Round-trip as their **instruction**, alongside the result Word last computed — so the save changes nothing on screen and the field is a field again. `w:fldLock` rides along, because the one way this could be worse than freezing is converting a field the author deliberately locked into a live one. The form the document used (`fldSimple` vs the `fldChar` run sequence) is re-emitted, not normalised. On a KEYSTROKE edit the result's bold/italic/underline are NOT carried (an opaque atom holds no marks), so a bold cross-reference in a plain paragraph returns plain — the field itself survives. **Not** unterminated fields — those stay in §2 (fixed task 049; client half task 057) |
| **Conditional / nested fields** — `{ IF { MERGEFIELD State } = "California" "…" "…{ MERGEFIELD State }…" }`, the shape a merge template is built from | Round-trip as the field's **own OOXML, character-for-character** — outer instruction, every inner field, both branches, the cached result, `w:fldLock` and `w:dirty`. This one is carried by *not being taken apart*: a nested field is a tree and an instruction is a scalar, so any attempt to recover "the instruction" yields a concatenation of two fields that would author neither (task 049's finding, which stands). The span is captured and re-emitted whole instead, so what nobody parsed cannot be lost — including run properties the ordinary field carry does drop, such as the `w:noProof` Word writes on every merge result. Works for a keystroke edit too, without the field's markup ever reaching the browser: the conditional's atom deliberately carries no payload, so when the posted model does not hold it the span is restored from the paragraph's pre-edit base (fixed task 058) |
| **Embedded objects** (`w:drawing`, `w:object`, `w:pict`) — a picture, chart, shape or OLE embed | The object's own OOXML subtree is carried **verbatim**, so properties nobody enumerated survive for the same reason cloning an untouched block preserves them. The picture's bytes never travel: they stay in their own package part and only the reference moves, and the save **resolves that reference against the document before authoring it** — a subtree naming a relationship the package does not have is refused rather than written, because a file Word reports as *damaged* is worse than a missing picture. Works for a keystroke edit too, without the object's markup ever reaching the browser: when the posted content model does not carry it, the object is restored from the paragraph's pre-edit base (fixed task 056). **Not** a text box — that stays in §2 |
| **Content-control shell** | The control's identity and binding survive even when its inner content cannot be modelled |
| Paragraph + run properties | Inherited from the base paragraph rather than re-derived |
| Comments, tracked changes | Carried on the content model itself |
| **Hyperlinks** — external (`r:id`) and internal cross-references (`w:anchor`) | Carried on the content model as the run's target. An **internal** cross-reference ("see Section 4.2") is carried as its bookmark name: a self-contained scalar, so there is nothing to re-derive and nothing that can dangle — the bookmark it names survives independently, by clone or by `CarryUnmodeledConstructs`. **This row previously read "hyperlinks · carried on the content model itself" and was FALSE for internal links** — the projection nulled the anchor while the read walk still emitted a live `#anchor` href into the editor, so `formattingUnchanged` could never match and a paragraph holding a cross-reference was re-authored **on every save even when untouched**, taking any footnote ref / inline `w:sdt` / text box in that paragraph with it. That is the §1 untouched-block guarantee, not a §3 edited-block loss, which is why it is called out here rather than quietly corrected (found by UAT 2026-08-26 D-1; fixed same day, pinned by the `hyperlinkInternal` parity family). **Not** the link's `w:docLocation`, `w:tgtFrame`, `w:tooltip` or `w:history` — those are dropped on any re-authored hyperlink, external ones included, and are named in §2 |

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
>
> **The CONDITIONAL field is the exception, and it is worth understanding why.** It *is* byte-identical —
> `w:noProof`, character styles, colours, multi-run results and all — for the same reason an untouched block
> is: nothing re-authored it. The ordinary field carry is a *reconstruction* from an instruction plus a
> result, so it can only restore what the model holds. The conditional carry is not a reconstruction at all,
> which makes it the more faithful of the two. That is not an argument for reconstructing less elsewhere: a
> non-nested field's instruction is a scalar the client can safely hand back, and a subtree is not, which is
> exactly why the ordinary field survives a keystroke edit through the client and the conditional survives it
> through the base instead.
>
> **What the conditional carry does *not* keep.** Its POSITION inside the paragraph, in one case — the same
> case, with the same trade, as a carried embedded object. Through the content model (every server-side path,
> including an AI edit) the position is exact. Restored from the base instead (a keystroke edit) it is placed
> at the index it held among the paragraph's parts before the edit: exact for a conditional alone in its
> paragraph, which is the shape a governing-law or signature-conditional clause almost always takes, and an
> approximation for one mid-sentence in a paragraph the user rewrote. A base restore also cannot tell a user
> who **deleted** the conditional chip from a client that never sent it, so it restores — the conservative
> direction, and the same one bookmarks, content-control shells and embedded objects already take.

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

### Measured 2026-08-26 (task 058; the two field rows changed since the task-056 run)

| Family | Untouched block | Edited block | Code emitted |
|---|---|---|---|
| `fldSimple` | 1/1 kept | **1/1 kept** | *(none — carried, task 049)* |
| `fldChar` | 2/2 kept | **2/2 kept** | *(none — carried, task 049)* |
| `fldNested` — `{ IF { PAGE } = 1 … }` | 6/6 kept | **6/6 kept** | *(none — carried, task 058)* |
| `fldUnterminated` — a `begin` whose `end` is in another paragraph | 2/2 kept | 0/2 | `field-flattened-to-text` |
| `drawing` | 1/1 kept | **1/1 kept** | *(none — carried, task 056)* |
| `object` | 1/1 kept | **1/1 kept** | *(none — carried, task 056)* |
| `pict` | 1/1 kept | **1/1 kept** | *(none — carried, task 056)* |
| `pictTextBox` — a shape wrapping `w:txbxContent` | 1/1 kept | 0/1 | `complex-object-dropped` |
| `pictTextBoxTwin` — the same box, in a document where the edited block has an identically-projecting twin | 2/2 kept | 1/2 | `complex-object-dropped` |
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

And on its fifth run it retired that very row, by the same mechanism, one task later. Task 058 taught the
conditional merge block to round-trip, `fldNested` went from `0/6` to `6/6`, and the check failed in the
over-claim direction until this document stopped calling it lost — while `fldUnterminated` was added in the
same change to keep `field-flattened-to-text` a code the renderer can still raise. That is now three
consecutive retirements where the row added to preserve a warning code was itself retired by the next task,
which is what a shrinking list is supposed to look like.

**And it caught something the row did not name.** The verbatim carry landed and the field came back complete
— and every one of its runs came back **bold**, because `ComposeBlockMerge` donates the base paragraph's
dominant run properties to every rendered run, and the dominant run here was the outer `IF`'s result. Applied
to content that was *carried* rather than re-authored, that repair becomes a mutation: both inner merge
values were silently bolded by the fix for a silent loss. Property inheritance is now exempt for runs it did
not author, and the boundary is pinned by a test — the run the user actually typed still inherits, because
that one really was rebuilt from a model that dropped its formatting.

And on its second run it caught the document going stale in the other direction: task 046 taught soft
line breaks to round-trip, and the parity check failed because this document still listed
`edited-paragraph-line-break-dropped` as a loss that no longer happens. That is the accretion failure the
both-directions rule exists for — a list that keeps claiming losses the code has already fixed looks
maintained while quietly becoming fiction.

### The hole under the list (found 2026-08-25, closed 2026-08-26)

Everything above measures whether a construct **survives**. This section is about the other half of the
promise — that when one does not survive, you are **told** — because for a while, in two places, you were
not, and a list that under-reports is worse than no list precisely because it is trusted.

Neither hole was found by the parity check. Both were found by looking: the first by task 056 probing a
corpus fixture by hand, the second by task 047b auditing the rest of the save path after it. Both are
closed, and both now have a test standing on them.

**1 — an edited block whose paragraph has a twin.** Compose decides what to re-author and what to clone by
aligning your document against the copy it opened. When two of your paragraphs are *indistinguishable* to
that alignment — consecutive empty paragraphs, repeated signature lines, two callout boxes with the same
words — there is more than one way to line the two versions up, and the one Compose used to pick could pair
your edited paragraph with **no** original at all. Everything downstream of that pairing then had nothing to
work from: no formatting to inherit, nothing to restore, and nothing to compare against, so a text box that
was dropped was dropped in **complete silence**. Editing the first of two identical boxes said nothing;
editing the second reported it correctly. Measured across the corpus at every block position — 294 edits of
24 documents — it happened five times, and **four of those were in a real signed agreement**, on consecutive
empty paragraphs.

It also cost fidelity, not just honesty: the paragraph you did **not** touch was cloned from the wrong
original, so the saved file held the first box's bytes twice and the second box's not at all — the one thing
§1 promises cannot happen to a block you never edited.

**2 — a block that wrote nothing at all.** A table posted with no rows is skipped by the renderer, and the
report was skipped with it: there was no output element to inspect, so the code returned before counting.
"Nothing was written" is a perfectly countable output — it is zero of everything — and treating it as a
reason not to count made a whole block leave the document without a word.

**What holds them closed.** The `pictTextBoxTwin` row in the table above is the first case, measured through
the real renderer at the block position where it used to fail: the row asserts the loss is **reported**, so a
regression fails the parity check rather than going quiet again. Alongside it,
`ComposeMergeSeamTests` sweeps every corpus document at **every** block position and fails if a single-block
edit leaves any paragraph without its original, and asserts the untouched twin is cloned from its own bytes.
The second case has its own test on the same file. All four were observed failing before the fix.

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
056 carries objects). This sign-off covered five rows in §2:
nested/unterminated fields · text boxes · footnote references · endnote references · content controls.
Two of those five were narrower carve-outs created by the fixes, not pre-existing losses.

**One of the five has since been narrowed again, in the owner's favour (task 058, 2026-08-26).** Answering
"we will be introducing templates and field-merge-codes, will these be supported?", the nested half of the
first row was **fixed rather than signed**: a conditional merge block now round-trips byte-for-byte, and what
remains on that row is only the **unterminated** field — a `TOC` or an `INDEX`, whose `begin` and `end` sit in
different paragraphs so there is no complete field to carry in the first place. That is a strictly smaller
promise than the one accepted, so it does not re-open the sign-off; it is recorded because a list that
quietly gets *better* is as much a drift from its signature as one that gets worse.

**What signing means**: that the losses in §2 are acceptable to ship *given* they occur only in the
paragraph a user edits and are reported by name every time. Declining any single item makes it a scope
question — fix it — rather than a documentation revision (ADR-049 / task 045 escalation).

**The 2026-08-26 repair does not re-open the sign-off, and does not silently ride on it either.** Task 047b
changed no row: the five losses accepted above are the same five, occurring in the same place, reported with
the same codes. What it changed is that the second half of the sentence above — *"and are reported by name
every time"* — is now true in two situations where it was not
([§5](#the-hole-under-the-list-found-2026-08-25-closed-2026-08-26)). A signature given on a condition is
worth what the condition is worth, so the repair is recorded here rather than left in a commit message.

## 7. Related

- [`COMPOSE-READ-REFERENCE-FIDELITY.md`](COMPOSE-READ-REFERENCE-FIDELITY.md) — the read path
- [`ADR-049`](../../.claude/adr/ADR-049-compose-shadow-document.md) — the governing decision + seven invariants
- `projects/spaarkeai-compose-r8/notes/` — `gate-decision.md`, `merge-mechanism-results.md`,
  `edited-block-loss.md`, `capability-gate-triggers.md`
