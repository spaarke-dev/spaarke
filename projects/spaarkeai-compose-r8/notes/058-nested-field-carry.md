# Carrying a NESTED field — the conditional merge block

> **Task 058** (`spaarkeai-compose-r8`, FR-A10 residual) · decided + measured 2026-08-26
> Owner question, 2026-08-25: *"we will be introducing templates and field-merge-codes, will these be
> supported?"* Simple merge codes already were (task 049). Conditional ones were not.
>
> Code: `ComposeField.SpanXml` · `ComposeDocxProjectionBuilder.TryCaptureFieldSpanXml` ·
> `ComposeDocumentRenderer.TryBuildCarriedFieldSpan` / `IsCarryableFieldSpan` ·
> `ComposeBlockMerge.CarryNestedFieldSpans` / `NestedFieldSpansIn`
> Tests: `tests/integration/seam/Compose/ComposeFieldCarrySeamTests.cs` (13 new) + the two field rows in
> `ComposeResidualLossParityTests`
> Fixture: `tests/fixtures/compose-corpus/nested-merge-fields.docx` (+ its generator)

---

## 1. The §2 verdict: task 049's reasoning is CORRECT, and it does not reach the conclusion

`049-field-carry-decisions.md` §3 says a nested field is unreconstructable:

> The scan folds an inner field into the outer span, so the recoverable instruction is a concatenation
> (` IF ` + ` PAGE ` + ` = 1 "…" "…" `). Re-emitting that would author a **different** field.

**Every word of that is true, and it is still true after this task.** Nothing here recovers an instruction
from a nested field, and nothing should: `FieldScanState.Instruction` is a `StringBuilder` fed by both the
outer and the inner code phases, and no amount of care makes one string describe two fields.

What the note establishes is that a nested field cannot be **RECONSTRUCTED**. What it does not establish —
and did not set out to — is that a nested field cannot be **CARRIED**. Those are different claims, and the
gap between them is the whole task. §2 enumerates two mechanisms (a keyword allow-list, and the
instruction+result scalar carry it chose) and correctly rejects the allow-list. A third was available and was
not on the table: **carry the span's own OOXML and never parse it**.

That third option is not novel here. It is the mechanism task 025 already used for `w:pPrChange`, and task
056 for `w:drawing` / `w:object` / `w:pict` — with the explicit rationale that *"a typed model of that would
silently discard every property it failed to enumerate"*. A nested field is precisely that case, one
construct larger: the tree is the payload, so any model of the tree loses whatever it did not enumerate.
**The field tree survives because nothing reads it.**

### The structural reason it works

The scan's own documented assumption is that a `w:fldChar` span is a sequence of **direct sibling runs**
(`FieldScanState` remarks). That is exactly the precondition a verbatim capture needs, and it was already
being relied on — it just had no consumer. `FieldScanState` gained one list (`SpanRuns`), accumulated on
every path where `TryAdvanceFieldScan` already returned `true`. Nothing else in the scan changed: the flat
scan the shipped classes depend on reads `Instruction`, `ResultRuns` and `MaxDepth`, and all three behave
identically.

Measured, on a document the OpenXML SDK round-trips: capture → re-parse → author into a fresh package →
reopen → re-capture is **string-identical**, and the schema validator reports zero errors on a standalone
holder paragraph containing an unbalanced-looking field-char sequence.

### Neither escalation trigger fired

- **"If preserving the field tree destabilises the flat scan, STOP."** It does not. The change is an
  append-only accumulation in a state object that already walked those runs, plus one new branch in
  `TryCarryField` reached only when `MaxDepth > 1` — a condition that today returns `false` immediately.
  Proven by the whole shipped suite staying green, and by an added control arm that exercises a plain
  `MERGEFIELD` in the *same document* as the conditionals.
- **"If a nested field can only be carried by reconstructing an instruction that is not byte-identical,
  STOP and keep flattening."** It is not reconstructed at all. The assertion that proves this is
  deliberately the strictest one available and the one a reconstruction cannot pass:
  `EditedParagraph_KeepsItsConditionalMergeField_ByteForByte` compares the field span's OOXML in the saved
  document against the source's, character for character.

---

## 2. What the carry is, in four parts

| Part | Where | What it does |
|---|---|---|
| **Capture** | `ComposeDocxProjectionBuilder.TryCaptureFieldSpanXml` | Clones the span's runs into a holder `w:p` and takes its `OuterXml`. Refuses a span whose runs are not consecutive siblings (see §3), or one over the shared 32 KB opaque-carry cap. |
| **Model** | `ComposeField.SpanXml` | The holder XML. Mutually exclusive with `Instruction`, which is left **empty** — so a refused carry falls through to a flatten rather than to a concatenated instruction. |
| **Re-emit** | `ComposeDocumentRenderer.TryBuildCarriedFieldSpan` | Three gates: the shared SDK parse+schema gate, relationship resolution, and — unique to this carry — a **structural** check that the payload IS a nested field. |
| **Base restore** | `ComposeBlockMerge.CarryNestedFieldSpans` | Restore-if-missing from the base block, for the keystroke path where the posted model carries no field at all. |

### Why the structural gate exists and no other carry needs one

Every other opaque carry in the renderer is root-gated by the SDK for free: a `ComposeEmbeddedObject.Xml`
payload is parsed as `Drawing`, and the generated constructor rejects anything else. A field span is a
*sequence*, so its holder is a `w:p` — and a `w:p` admits any paragraph content there is. Without
`IsCarryableFieldSpan`, `SpanXml` would be a general-purpose way to author arbitrary markup into a saved
legal document from a posted model. The gate requires a balanced span that opens on its first element,
closes on its last, holds nothing outside itself, and **nests at least once** — the last clause keeping
`SpanXml` scoped to the one class it exists for, so a non-nested field cannot acquire a second authoring
path that could drift from the first.

Six hostile payloads are asserted against it, each carrying a distinctive token so the "must not appear"
assertion is real for every row rather than vacuously true for four of six.

---

## 3. Contiguity — the one place this could have lied

The scan consumes **runs**, but the container it walks can hold other children between them: a
`w:bookmarkStart`, a `w:commentRangeStart`, a `w:proofErr`, a `w:hyperlink`. Each is emitted by its own arm
of `ProjectInline`, at its own position.

Capturing just the runs of such a span would produce a `SpanXml` **the source document never contained** —
an element silently omitted, presented as the field's own OOXML, verbatim. That would falsify the single
claim this design rests on. So the capture verifies each run is the `NextSibling()` of the previous one and
refuses otherwise; the field then takes the base-carry path, which claims nothing about interior position
and therefore cannot be wrong about it. The interleaved bookmark survives on its own through task 041's
carry, widened to the paragraph as that carry documents.

---

## 4. What this found that the task did not ask for

### 4.1 Property inheritance was mutating carried content

`ComposeBlockMerge.InheritRunProperties` donates the base paragraph's **dominant** run properties to every
rendered run. That is right for a re-authored run — the model dropped its formatting, and inheritance is the
repair. Applied to a run that was **carried**, it is not a repair but a mutation.

Measured on the new fixture: the dominant run is the outer `IF`'s result (the longest), and it is bold. Every
one of the span's 17 runs came back carrying `w:b` — so both inner `MERGEFIELD` values were silently bolded.
**A fidelity loss introduced by the fix for a fidelity loss**, and one that would have shipped looking
correct, because the field itself was complete and the parity check counts element names.

The rule underneath it is general and is now stated where it lives: *inheritance exists because a re-authored
run lost its properties at projection time; a run that was never re-authored has nothing to repair.* The
exemption is scoped to the nested span deliberately — a non-nested field's result IS re-authored, and the
published list already promises it the ordinary edited-block property tier. Widening the exemption to every
field would silently change shipped behaviour, so a test pins **both** halves: the span keeps exactly its own
properties, and the run the user actually typed still inherits.

### 4.2 A refused payload now costs nothing

An unforeseen and welcome consequence of shipping both halves together. When the render gate refuses a posted
span — hostile, over-cap, not a field — the rendered paragraph has no field, and `CarryNestedFieldSpans` then
restores the base's own. So a refusal does not degrade to a flatten; the document keeps the field it actually
had. The flatten remains the outcome only where there is no base to fall back on (a newly created block, or
the R6 fail-open path), and that case has its own test.

### 4.3 `grep` reports `ComposeBlockMerge.cs` as binary

Two `\0` bytes, both pre-existing and both deliberate — `$"\0{side}{index}"`, a sentinel that cannot collide
with a real block key. Harmless, but `grep` needs `-a` on this file and a plain `grep` silently reports
nothing. Worth knowing before someone concludes a symbol is absent from the merge.

### 4.4 The corpus manifest has been drifting since task 043

`corpus-manifest.md` documents fixtures through §1.8 (task 022). The construct fixtures added by tasks 043
and 056 — including `inline-image.docx`, `chart-embedded.docx`, `ole-embedded-object.docx` — have no rows.
The generators' docstrings carry the record instead, which is where a reader will actually look, but the
manifest's own claim to be a catalog is now partly untrue. Recorded rather than fixed wholesale: this task
added its §1.9 row, and the backfill for 043/056 is somebody's small deliberate task, not a side effect of
this one.

---

## 5. Measured

`ComposeResidualLossParityTests`, 2026-08-26:

| Family | Untouched block | Edited block | Code emitted |
|---|---|---|---|
| `fldSimple` | 1/1 kept | **1/1 kept** | *(none — carried, task 049)* |
| `fldChar` | 2/2 kept | **2/2 kept** | *(none — carried, task 049)* |
| `fldNested` | 6/6 kept | **6/6 kept** | *(none — carried, task 058)* |
| `fldUnterminated` | 2/2 kept | 0/2 | `field-flattened-to-text` |

The `fldUnterminated` row was added in the same change, for the same reason `fldNested` itself was added by
task 049: retiring the last producer of a code would leave the published document naming a warning nothing
can raise. That is now three consecutive tasks where the row added to keep a code alive was retired by the
next one.

Corpus arm, `nested-merge-fields.docx` through the real renderer: the standalone conditional's own paragraph,
the mid-sentence conditional's own paragraph, and the plain `MERGEFIELD`'s own paragraph each edited in turn;
the conditional's span byte-identical in every case, `w:noProof` and `w:b` counts unchanged, the plain field
still carried as a scalar.

---

## 6. What this still does NOT carry

- **An unterminated field** (`TOC`, `INDEX`) — its `begin` and `end` are in different paragraphs, so no
  container ever sees a complete field. Unchanged, and now the only field case on the published list.
- **A non-contiguous span** — refused by the capture (§3) and covered by the base restore instead, which
  means the user still gets the field but its interior position is the base's rather than the model's.
- **Interior position on a keystroke edit** — the base restore places the span at the content ordinal it
  held before the edit: exact for a conditional alone in its paragraph, approximate mid-sentence. Same
  trade, same justification, as the embedded-object restore.
- **The distinction between "the client dropped it" and "the user deleted it"** — the base restore cannot
  see one, so it restores. The conservative direction, and the one bookmarks, content-control shells and
  embedded objects already take.
