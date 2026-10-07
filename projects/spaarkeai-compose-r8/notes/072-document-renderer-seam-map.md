# Task 072 — `ComposeDocumentRenderer.cs` seam map

> **Analysed**: 2026-08-31 · **File**: `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocumentRenderer.cs`
> **Size at analysis**: **2,987 lines** (POML and TASK-INDEX both say 2,304 — same Track A drift as 070/071)

## Binding criterion

Same correction as 070/071, recorded again so this file stands alone: the God-class LOC ratchet was
**retired 2026-08-20** (commit `866f9c101`), so ~~"under 2,000 lines"~~ is not a gate and
~~`GodClassGuardTests.cs`~~ does not exist — there is no waiver to delete. Binding instead: extract each
cluster with its own reason to change (root CLAUDE.md §11.5).

**Here that correction does real work rather than being bookkeeping.** The POML's own escalation trigger
says the line target must never be met by extracting something that writes body children. With the
ratchet retired there is no line target left to tempt anyone — so ADR-049 I-5 is the *only* thing
steering the cut, which is the right way round.

The POML's step 1 ("if 040 already brought it under 2,000, just delete the waiver and verify") also does
not apply: measured **2,987**, so the file grew by 683 during Track A rather than shrinking.

---

## 1. The one constraint that shapes everything: ADR-049 I-5

> **Exactly one body author.** Collaborators may build elements, compute properties or decide strategy —
> only the renderer writes body children.

"Body children" means the direct `w:p` / `w:tbl` children of `w:body`. Building an element and returning
it is not authoring; appending runs into a `w:p` the renderer already owns is not authoring.

**Two clusters that looked extractable were deliberately left in place** because they cross that line:

| Left behind | Why |
|---|---|
| `ResolveHyperlinkRelationships` | Belongs with the run author by subject matter, but calls `child.Remove()` / `parent.InsertBefore(...)` / `hyperlink.Remove()` on the **live body tree**. It restructures authored content rather than building an element. |
| Table construction (`BuildTable`, `BuildTableCell`, …) | `BuildTableCell` calls `RenderBlocks(tableCell, …)` — it calls the author back. Extracting it would leave a collaborator driving the renderer, which adds a cycle and muddies the invariant for no cohesion gain. |

`AssignParaIds(Body body)` also stayed: it walks the body and sets attributes. That is not authoring
children, but it is close enough to the line that keeping it costs nothing and moving it would need an
argument.

**Verified, not assumed** — after extraction, body-write sites per file:

| File | `body.Append/Remove/Insert/Replace` |
|---|---|
| `ComposeDocumentRenderer.cs` | **8** |
| `ComposeRunAuthor.cs` | 0 |
| `ComposeNumberingAuthor.cs` | 0 |
| `ComposeStyleCatalog.cs` | 0 |

## 2. The equivalence oracle — and the two defects found while validating it

`tests/integration/seam/Compose/ComposeRenderEquivalenceOracle.cs`, temporary scaffolding on the same
contract as 071's (deleted on landing; resurrect from git). It captures **both** entry points —
`SynthesizeDocument` (born-in-editor) and `RenderIntoCarrier` (the R8 save path) — for all 25 corpus
documents, comparing the whole OOXML package part by part.

**Validating the instrument found two defects that would each have made the proof worthless while
looking green.** This is the entire reason the validation step exists.

### Defect 1 — nondeterminism the projection side did not have

The OpenXML SDK mints relationship ids (`R` + 16 hex, GUID-derived) when parts are added, so two runs of
*identical* code differed. Fixed by canonicalising them to `R#1`, `R#2`, … in order of first appearance.

Canonicalised, **not erased**, and the distinction matters: `document.xml` references these via `r:id`, so
renumbering consistently preserves the relationship *graph* — a hyperlink retargeted by a bad refactor
still moves the numbering and still fires the diff. Deleting the ids would have hidden exactly that.

*(ZIP entry timestamps are the other container-level nondeterminism; sidestepped by comparing part
CONTENT rather than container bytes. `AddCoreProperties` turned out to write no timestamp at all — only
creator and description — which was verified rather than assumed, since a normaliser that masks a real
difference is worse than no normaliser.)*

### Defect 2 — the carrier control covered nothing

Handing `RenderIntoCarrier` the model projected from the *same* bytes makes every block unchanged, so the
merge cloned all of them and the **render half never executed**.

Measured: a seeded mutation in shared code (`ApplyAlignment`) appeared in **3 `.synth` captures and zero
`.carrier` captures**. The control read as though it covered the save path while proving only that
cloning works — and it would have gone on reading that way through the whole task.

Fixed by editing one block before rendering, which is also the shape the real R8 save takes (ADR-049
invariant 2: untouched blocks are preserved). Every carrier capture is now `rendered=1, cloned=2..48`, so
clone, render, and the property-inheritance step that pairs a rendered block with its baseline all fire.

*(Also: `ComposeMergeStats` does not override `ToString()`, so the dump was the type name and every merge
looked identical. Captured field-by-field instead.)*

### Controls

| Control | Result |
|---|---|
| Deterministic | ✅ after the relationship-id fix |
| Non-vacuous | ✅ 34,515 lines captured, **0** throws, merge exercises clone AND render |
| Sensitive — shared code (`ApplyAlignment`) | ✅ hits **both** `.synth` (3) and `.carrier` (2) |
| Sensitive — carrier-only (`ObserveClonedBlock` suppressed) | ⚠️ **survived the oracle** — see below |
| Restores | ✅ reproduces baseline byte-for-byte |

### The survivor, and why it is not a hole

The carrier-only seed produced **zero** oracle differences. Per task 070's lesson, a survivor is a
**coverage statement, not a licence to proceed** — so it was escalated to the full Compose suite, where it
**died (1 failure)**.

So `ObserveClonedBlock` is covered — by an existing test, not by the oracle. That is the correct outcome
and worth stating plainly: **the instrument for 072 is the oracle AND the suite**, not the oracle alone.
The oracle proves *this refactor changed no output*; the suite proves *the behaviour is still right*.
Neither substitutes for the other, and 070 learned the same thing from the other direction.

## 3. What was extracted

| Component | Reason to change | LOC |
|---|---|---|
| `ComposeRunAuthor.cs` | how a model run becomes OOXML — character formatting, Word fields (simple + `fldChar` sequences), tracked-change wrappers, hyperlinks, carried opaque spans/objects | 570 |
| `ComposeNumberingAuthor.cs` | the write-side `numbering.xml` author + carrier numbering scan; the mirror of task 071's read-side `ComposeNumbering` | 424 |
| `ComposeStyleCatalog.cs` | what a Spaarke-authored document should LOOK like (Normal, Heading1-6, ListParagraph, the heading→numbering link) | 99 |
| `ComposeDocumentRenderer.cs` (remainder) | **the one body author** — assembling `w:body` | **1,997** |

`ComposeDocumentRenderer.cs`: **2,987 → 1,997**.

Ownership of shared vocabulary was assigned rather than duplicated: the three abstract-num ids and the
num-instance ids moved to `ComposeNumberingAuthor`; `NormalStyleId` / `ListParagraphStyleId` to
`ComposeStyleCatalog`; `MaxFieldInstructionChars` to `ComposeRunAuthor`. `MaxHeadingLevel` is genuinely
shared by all three and stayed on the renderer as `internal`.

### Verification

| Check | Result |
|---|---|
| Render equivalence, 25 docs × both entry points | ✅ byte-identical after **each** extraction |
| **ADR-049 I-5 — one body author** | ✅ 8 body writes, **all** in the renderer; 0 in every collaborator |
| Build | ✅ 0 errors, 0 warnings |
| BFF suite / ArchTests | see PR |
| **NEGATIVE** — no second body author | ✅ verified by reading every extraction AND by the per-file count above |
| **NEGATIVE** — no `body.Descendants<Paragraph>()` introduced | ✅ count unchanged |
| **NEGATIVE** — no DI-registration or ctor-null tests added | ✅ net new tests: zero |
| DI registration count | ✅ `git diff` on `Program.cs` + `Infrastructure/DI/` empty |

## 4. Defects found while decomposing

**No production defect was found.** As in 071, stating the "none" explicitly — it means the code was read
closely enough to classify by reason-to-change and nothing was wrong, which is different from nothing
having been looked for.

One **process** defect was found and fixed, in my own tooling rather than the product: the 071 extraction
script wrote files with Python's `utf-8-sig`, which *adds* a BOM on write, while all 39 Compose files on
master are BOM-less. Eight files were affected. Fixed before the merge rather than after, and worth
recording because `dotnet format` in the pre-commit hook did **not** catch it.
