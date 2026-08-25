# Embedded objects through an edited paragraph — the evidence, and what was decided

> **Task 056** (`spaarkeai-compose-r8`, FR-A10 residual) · decided + measured 2026-08-25
> Owner decision 2026-08-25: *"if we can carry them that's the better solution"* — embedded objects are to
> be CARRIED, not reduced to a place-indicator and not deferred. This note records the empirical answer to
> the question that governed the design, and the decisions that followed from it.
>
> Code: `ComposeContentModel.ComposeEmbeddedObject` · `ComposeDocxProjectionBuilder.TryCarryEmbeddedObjects`
> · `ComposeDocumentRenderer.TryBuildCarriedObject` / `CarriedObjectRelationshipsResolve`
> · `ComposeBlockMerge.CarryEmbeddedObjects` / `IsCarryableEmbeddedObject`
> Tests: `tests/integration/seam/Compose/ComposeObjectCarrySeamTests.cs` (+ the four object rows in
> `ComposeResidualLossParityTests`) · `src/widgets/opaqueAtomNode.test.ts`

---

## 1. The load-bearing question, answered with evidence

**Question.** A `w:drawing` names its image by relationship id (`r:embed="rId7"`), resolved against the
**main document part** — the very part whose body this save replaces. `ComposeDocumentRenderer`'s remarks
said orphaned parts *"remain in the package as inert weight"*, which does not distinguish

* *present WITH its relationship* — carrying the subtree verbatim works; from
* *the relationship was pruned* — the carried drawing points at nothing, **Word reports the file as
  damaged**, and the "fix" is strictly worse than the honest drop it replaced.

**Answer: relationships SURVIVE. The body swap does not touch `word/_rels/document.xml.rels`.**

**How it was obtained** — a throwaway probe that rendered a save and then *opened the saved package* and
enumerated `MainDocumentPart.Parts` / `ExternalRelationships` / `HyperlinkRelationships`, rather than
reading the comment and believing it. Two arms per fixture, and the second is the one that settles it:

| Fixture | Edit position | `w:drawing`/`w:object` in saved body | Main-part relationships in the SAVED package | Dangling refs |
|---|---|---|---|---|
| `chart-embedded.docx` | prose block (chart UNTOUCHED → cloned) | 1 | `rIdChart → /word/charts/chart1.xml` | none |
| `chart-embedded.docx` | the chart's OWN block (dropped, pre-change) | 0 | `rIdChart → /word/charts/chart1.xml` — **still there** | none |
| `ole-embedded-object.docx` | prose block | 1 | `rIdImg → /word/media/image1.png`, `rIdOle → /word/embeddings/oleObject1.bin` | none |
| `ole-embedded-object.docx` | the object's OWN block | 0 | both — **still there** | none |

Two independent readings of the same fact, which is why it is trusted:

1. **Direct** — the relationship is present in the saved package even when *nothing in the body references
   it*. "Orphaned" therefore means *unreferenced but present with its relationship*, not *pruned*. The SDK
   rewrites the part's XML; it has no reason to touch the part's `.rels`, and it does not.
2. **By construction** — an untouched block containing a drawing is cloned **byte-verbatim**, carrying its
   original `r:id` into the new body, and those saves resolve cleanly today. Had relationships been pruned,
   every corpus document with an image outside the edited paragraph would already be corrupt.

**Neither escalation trigger fired.** The first is closed by the table above; the second by §5.

The renderer's remark has been corrected in place rather than left ambiguous — the same correction task 049
had to make to the stale *"the model does not carry bookmarks"* claim two paragraphs above it. Twice in one
project is not a coincidence: a comment that was true when written is the most expensive kind of wrong.

---

## 2. What was implemented, and why it is two halves

Both halves were built, because either one alone ships something that does not work.

| Half | Mechanism | Covers | Position |
|---|---|---|---|
| **Model carry** | `ComposeInlineRun.EmbeddedObject` — the subtree's `OuterXml` on the shipped opaque-carry contract | Every server-side model round trip: the projection, an AI edit batch, the merge's own baseline re-projection | **Exact** |
| **Base carry** | `ComposeBlockMerge.CarryEmbeddedObjects` — restore from the block's pre-edit base when the posted model does not carry it | A **keystroke edit from the browser**, where the editor's opaque atom contributes nothing to the rebuilt paragraph | Base content ordinal, clamped |

The POML chartered the model carry, and the acceptance criteria name it. On its own it would have been a
**producer with no consumer** — precisely the shape task 049 shipped for fields and task 057 had to finish.
The base carry is what makes a real user's edit keep its image *today*, and it is not an invention: it is
`CarryUnmodeledConstructs`, the mechanism task 041 already uses for bookmarks and content-control shells,
extended by one construct family (root §11: extend before you add).

**Why the client half is NOT "ship the OOXML to the browser".** `ComposeBlockMerge`'s own header records
task 041 making this exact call and states four reasons; a fifth is specific to this construct. An embedded
object references its image by relationship id, and the base block's ids are the **carrier's own**, so a
base-carried object resolves *by construction*. A client round trip would put DrawingML in the browser DOM
(ADR-049 I-2, where a field instruction was already the arguable edge) and hand back ids the server would
then have to distrust anyway. The carry gets safer, not weaker, by never leaving the server.

---

## 3. The two gates, and why parsing is not enough

`ComposeFormatChange.PreviousPropertiesXml`'s gate is reused **literally** — the same method, now named
`TryParseOpaqueCarry<T>` because a method called `…PreviousProperties` parsing a `w:drawing` is how stale
naming starts. It parses through the typed SDK class (whose generated ctor validates the root element's
name and namespace), schema-validates, and returns null on any failure. One cap
(`MaxOpaqueCarryXmlChars`, 32 KB) for one mechanism.

**That gate is necessary and not sufficient**, and the gap is the whole task. A subtree can parse and
validate *perfectly* while naming a relationship the package does not have. So the renderer applies a
second gate: every attribute in the **relationships namespace**, anywhere in the subtree, must resolve
against the carrier's main part. Keyed on the namespace rather than a list of names (`r:id`, `r:embed`,
`r:link`, `r:pict`, `r:dm`/`r:lo`/`r:qs`/`r:cs` …) because an allow-list would silently stop guarding the
first construct nobody thought of.

For a `SynthesizeDocument` (blank-package) render the set is **empty**, so a posted object naming any
relationship is refused rather than authored into a document that cannot resolve it. That is the correct
default, obtained by construction rather than by a special case.

### The outcome under hostile input is better than a drop, and that is asserted

Measured rather than assumed: when a client posts junk (`<script>`, malformed XML, a wrong-root element) or
a **forged** drawing naming `rIdNotInThisPackage`, the renderer refuses it and the base carry then restores
the document's **own** object. The saved package contains neither the junk nor the forged id, the real
object is still there, and **no** `complex-object-dropped` is reported — because nothing was lost. A client
cannot destroy an image by posting garbage in its place.

This is the opposite of what the tests originally asserted (they expected a drop). The assertions were
corrected to the measured behaviour, not the behaviour rewritten to the assertion.

---

## 4. What is NOT carried, and why the row could not be fully retired

A **text box** — a `w:pict`/`w:drawing` wrapping `w:txbxContent` — is excluded. Its visible text is already
accept-flattened into the paragraph as prose, so carrying the box on top of that would put the same
sentence in the document **twice**: a "fix" that corrupts the paragraph it was meant to protect.

So `complex-object-dropped` keeps a producer, and the published list keeps the row — reshaped and narrower
(the box's words survive; its frame, size, wrap and float do not). The `pictTextBox` family was added to
`ComposeResidualLossParityTests` in the same change, for exactly the reason `fldNested` was added in task
049: retiring the last producer of a code would leave the document naming a warning nothing can raise —
direction B's failure mode arriving through the front door.

The exclusion rule is **structural** (does the subtree contain a `txbxContent`/`textbox`?) rather than
"does it have text in it right now", and it lives in **one** place — `IsCarryableEmbeddedObject`, called by
both the projection and the merge. Two copies of this boolean could drift, and the way that drift would
announce itself is a duplicated paragraph in a signed agreement.

### Other honest limits

* **Position on a keystroke edit** is the base's content ordinal, clamped. Exact for an object alone in its
  own paragraph — the shape a signature image, an exhibit chart or an embedded schedule almost always takes
  — and an approximation for one mid-sentence in a rewritten paragraph. An approximate position is a
  strictly smaller loss than the deletion it replaces. Published on the residual list rather than left to be
  discovered.
* **A transitional mixed run** (own `w:t` text *alongside* the object) emits text first, object second,
  regardless of their order inside the source run. Interleaving exactly would need a second content walk in
  the hottest method in the projection, for a shape the corpus does not contain.
* **All-or-nothing per run.** A run with two objects where only one is carryable falls back whole, because
  the merge's count would otherwise report one loss with no way to say which.
* `mc:AlternateContent`-wrapped objects are unchanged — a different handler, a different family.

---

## 5. Payload — measured against the ADR-040 128 KB inline cap

The escalation trigger was: if a realistic payload passes the cap, `ProjectComposeOutputs` **skips** the
entry, so the save would vanish from the read projection rather than degrade.

| Fixture | Model without the carry | With it | Delta | % of the 128 KB cap |
|---|---|---|---|---|
| `inline-image.docx` | 1,933 B | 3,782 B | +1,849 B | **2.9%** |
| `ole-embedded-object.docx` | 1,952 B | 2,912 B | +960 B | **2.2%** |
| `chart-embedded.docx` | 1,951 B | 2,878 B | +927 B | **2.2%** |

Corpus-wide, the largest non-text-box object subtree is **2,436 bytes**. The reason the growth is small is
structural and worth stating: **the picture's bytes never travel.** The image stays in its own package part;
only the reference moves. A document would need on the order of 60 embedded objects in a single payload to
approach the cap. Asserted continuously by `CarriedObjectPayload_StaysWellInsideTheAdr040InlineCap`, which
also asserts the carry is actually *in* the model — an arm that measured nothing would report generous
headroom forever.

**Trigger did not fire.**

---

## 6. A client defect found on the way, and fixed

Task 057 flagged its `data-atom-display` fix as *"correct for `sdt` and `object` by construction, but only
exercised for `field`"*, and asked for confirmation. **It was not correct for `object`.**

The attribute was re-emitted only when the display text was *truthy*. The server emits an `object` atom's
span with **no content at all**, so there was nothing to re-emit; the next parse read the placeholder's own
label back as display text, and the pass after that compounded it: `Object` → `Object: Object` →
`Object: Object: Object`. Reachable, not theoretical — `ComposeEditor.getDraftHtml` persists `getHTML()` to
the local draft store and the FR-03 recovery path re-mounts exactly that HTML, so a user recovering a draft
saw it. An `sdt` whose content resolves to nothing has the same defect.

Fixed by having an **opaque** atom always emit the attribute, empty when it has no display text — "there is
none", said explicitly, which is what stops the placeholder from answering the question instead. A
*renderable* atom (tab, symbol) is deliberately untouched: its rendered content **is** its display text,
with no label to absorb. Observed failing before the fix.

---

## 7. Observation recorded, not fixed

While probing `interior-text-boxes.docx`, editing block **1** lost a `w:pict` with **no** warning, while
editing block **2** reported `complex-object-dropped` correctly. That fixture has two paragraphs whose
projected text is byte-identical, so `ComposeBlockMerge.Plan`'s alignment pairs the edited block against no
base (`BaseIndex < 0`), and `CarryUnmodeledConstructs` — which is what reports the loss — is skipped
entirely for an unpaired block.

It is a **pairing** artifact on duplicate-text paragraphs, not a general silent loss, and it predates this
task. Out of scope here (a different family, a different mechanism, and a fix would change merge alignment
on the whole corpus) but written down rather than left in a terminal, because "an edited block with no base
counterpart reports no construct loss" is a real gap in the never-silent contract and someone should own it.

---

## 8. Measured

`ComposeResidualLossParityTests`, 2026-08-25:

| Family | Untouched block | Edited block | Code emitted |
|---|---|---|---|
| `drawing` | 1/1 kept | **1/1 kept** | *(none — carried)* |
| `object` | 1/1 kept | **1/1 kept** | *(none — carried)* |
| `pict` | 1/1 kept | **1/1 kept** | *(none — carried)* |
| `pictTextBox` | 1/1 kept | 0/1 | `complex-object-dropped` |

Corpus arm, through the real renderer, asserting on the **saved package**: `inline-image.docx`,
`chart-embedded.docx` and `ole-embedded-object.docx` each edited at the object's own paragraph — the object
present and **every relationship resolved** in all three; and with the object stripped from the posted model
(the keystroke shape) — same result. Every edit position of every fixture checked for dangling references:
none, anywhere.

`inline-image.docx` was added to the corpus by this task. The corpus covered a chart (`c:chart r:id`) and an
OLE embed (VML `v:imagedata r:id`) but had **no plain inline picture** — `w:drawing` > `pic:pic` >
`a:blip r:embed`, the single most common embedded object in a legal document and the exact shape the
relationship question is about. Proving the carry on constructs the corpus happened to contain, while the
canonical one was absent, would have proved the wrong thing.
