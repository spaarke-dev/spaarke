# FR-A07 capability gate — the trigger list, derived from evidence

> Task 043. **The POML makes owner review a gate on this task** (`<escalation><trigger>`: *"Do not ship a
> gate on an unreviewed list"*). Owner directed 2026-08-23: **close the corpus gap first, then decide.**
> That is now done. Measured against the post-040/041/042 implementation and a corpus extended from 19 to
> 23 documents.

---

## Verdict

**No construct family requires a capability gate.** All six previously-untested families were measured;
none hard-fails, and every loss is already reported by name.

Per the owner's own selected path — *"if nothing does, 043 closes as superseded by 040/044 with the
evidence on record"* — **043 closes as superseded.** The evidence is below.

---

## What was missing, and what was done about it

The first pass at this list found six construct families with **zero corpus coverage**, so the gate's
headline "zero hard-fails" said nothing about them. Four new fixtures now cover five of the six
(`generators/make-untested-construct-families.py`):

| Fixture | Closes |
|---|---|
| `ole-embedded-object.docx` | OLE object (`w:object`) **and** embedded OLE binary |
| `chart-embedded.docx` | chart part (`word/charts/chart1.xml`) |
| `endnote-references.docx` | endnote reference (`w:endnoteReference` + `word/endnotes.xml`) |
| `embedded-font.docx` | embedded font (`w:embedRegular` + `word/fonts/font1.odttf`) |

`ComposeCorpusFixtureLocator` globs `*.docx`, so all four landed in every existing harness with no code
change. Suite went 10,920 → **11,044 passing, 0 failing**.

**Macros are deliberately not covered.** A `vbaProject.bin` makes the package a `.docm` — different
content type, different extension. Dropping one into a `.docx` would author a fixture that is invalid by
construction, the corpus locator would never enumerate a real `.docm`, and Compose's editable gate routes
on extension so a `.docm` does not reach the merge today. Written down rather than faked: a fixture that
is wrong the same way every run is worse than a gap that is recorded.

---

## Measurement 1 — the construct in an UNTOUCHED block

The standing harness (preservation oracle + edited-block loss), which edits the first prose run:

| Fixture | Overall (control → merge) | Strict | Edited block |
|---|---|---|---|
| `ole-embedded-object.docx` | 75.00% → **100.00%** | 25.00% → **100.00%** | INTACT |
| `chart-embedded.docx` | 75.00% → **100.00%** | 25.00% → **100.00%** | INTACT |
| `endnote-references.docx` | 33.33% → **100.00%** | 33.33% → **100.00%** | INTACT |
| `embedded-font.docx` | 100.00% → **100.00%** | 33.33% → **100.00%** | INTACT |

All four reach **100% strict**, and none emits a save warning. This is unsurprising once stated plainly:
an untouched block is cloned **byte-verbatim**, so a construct survives *precisely because the merge never
parses it*. Understanding a construct is not a precondition for preserving it.

## Measurement 2 — the construct in the block the user EDITS

Measurement 1 alone would have been the fourth near-vacuous measurement this project has had to catch: it
edits prose, not the construct's own paragraph. `ConstructFamilyCarryMeasurementTests` edits **the block
that carries the construct**, which is the only place a construct can actually be lost.

| Family | Element in body | Package part | Warning emitted | Saved doc |
|---|---|---|---|---|
| OLE object | `w:object` 1 → **0** | `oleObject1.bin` **survived** | `complex-object-dropped×1` | schema-valid |
| Chart | `w:drawing` 1 → **0** | `charts/chart1.xml` **survived** | `complex-object-dropped×1` | schema-valid |
| Endnote | `w:endnoteReference` 2 → **1** | `endnotes.xml` **survived** | `unrepresented-endnote-reference×1` | schema-valid |
| Embedded font | *(package-level)* | `fonts/font1.odttf` **survived** | *(none — nothing was lost)* | schema-valid |

Three facts, and all three matter:

1. **No hard fail.** Every save produced a schema-valid, readable document with the user's edit present.
   The validity of the *saved* document is asserted, not just that the bytes parsed — dropping an element
   while leaving a dangling relationship would still open and still be broken.
2. **Every loss is named.** Task 044's taxonomy already covers all of them. There is no silent loss to
   protect the user from with a gate.
3. **Package parts always survive.** The reference is dropped; the part is orphaned, not deleted. The
   document stays valid and the content is recoverable from version history.

---

## Why a document-level gate is the wrong instrument

It is not that the evidence merely failed to find triggers — the architecture explains why there are none
to find.

**Loss is per-edited-block, never per-document.** A 40-page agreement with an embedded chart on page 12 is
completely safe unless the user edits *that paragraph*. A gate at open time keyed on construct presence
would refuse editing on documents we demonstrably handle at 100% strict preservation — a false positive
**by construction**, which is exactly what the POML calls "the main risk".

The two families FR-A07's own example text names — *"3 embedded charts, 1 legacy form field"* — were both
already in the corpus, both carried, and are now both measured in the edited-block case too.

## Why "Edit a copy" has no trigger to attach to

Every read-only trigger that exists today is *"we cannot read this document at all"*:

| Code | Meaning |
|---|---|
| `empty-source` | zero bytes |
| `unreadable-source` | not a valid OOXML package |
| `resource-limit-paragraphs` | over the paragraph ceiling |
| `projection-error` | the projection threw |
| *(client)* non-docx bytes/extension | `.txt`, `.rtf`, image, un-intakeable `.pdf` |

A copy of a document we cannot read is not editable either. The fork presupposes a trigger of the form
"we can read it but must not write it" — and **exactly one exists: the PDF**, where the owner's pattern
already ships as create-on-save (and task 044 made it survive a refresh).

---

## The residual FR-A07 question, stated honestly

The warning arrives **at save**, after the edit. A user who edits the paragraph containing a chart is told
the chart was dropped once the save lands — not before they touch it. Version history holds the prior
version, so nothing is unrecoverable, but the consent is after the fact rather than before.

If FR-A07's intent is *informed consent*, the evidence-supported version of it is a warning **at the
edit**, on the specific block, reusing 044's taxonomy — narrow, and far smaller than the document gate the
POML describes. Recorded as an open option for task 045, not built here: the owner's chosen path closes
043 when nothing hard-fails, and nothing did.

## Open

- **Macros (`.docm`)** — untested, and not testable as a `.docx` fixture. Whether Compose should accept
  `.docm` at all is a product question, not a merge question. Carried to 045.
