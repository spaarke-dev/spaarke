# Zero content loss — scoping the remaining constructs for R8

> Owner direction, 2026-08-24: *"we cannot have the Compose editor to just 'lose' content; and pushing to
> r9 is just semantic because we are not going to release the product without these addressed."*
>
> This note sizes every remaining loss and says what each one actually costs. Written after task 046
> shipped the first fix, which changed my estimate of the others — see §1.

---

## 1. What task 046 taught, and the correction it forces

I called `br` / `tab` / `sym` a "cheap tier". **That was right for `br` and optimistic for the other two**,
and the reason matters for everything below.

The soft line break was cheap because **three of the four pieces already existed**: the editor already
carried it as a TipTap `hardBreak`, and the HTML projection already emitted `<br>`. Only the model field
was missing. Four small edits, all mirroring `IsPageBreak`.

`tab` and `sym` do **not** have that head start. The HTML projection renders a tab as
`<span class="compose-tab"> </span>` and a symbol as its *resolved glyph character*. Once in the editor
both are indistinguishable from ordinary text, so there is nothing for the mapper to preserve. They need a
**custom TipTap node** — a schema addition, parse rule, serializer, and caret/selection behaviour.

**So the real axis is not "how exotic is the construct" — it is "does the editor already have a node for
it".** That is what the sizing below is built on.

## 1b. The editor's node inventory — and a SECOND loss direction nobody had measured

Owner question, 2026-08-24: *"shouldn't we look at what TipTap provides as existing nodes? this would
help ensure we have coverage for all features the TipTap editor offers."*

Yes — and it found something the residual list does not cover at all.

### What the editor actually has

The locked extension set (`ComposeEditor.tsx`) provides: Document, Paragraph, Text, Bold, Italic, Strike,
Code, **CodeBlock**, Heading 1–6, BulletList, OrderedList, ListItem, **Blockquote**, **HardBreak**,
**HorizontalRule**, Underline, Link, **Image** (`allowBase64`), Table/Row/Header/Cell, **TaskList**,
**TaskItem**, TextAlign.

The content model has **four** block kinds: `Paragraph`, `Heading`, `ListItem`, `Table`. The mapper's
`BLOCK_NODE_TYPES` is `{paragraph, heading}`, and `forEachBlock` only calls back for those — anything else
contributes whatever paragraphs happen to be nested inside it, and nothing else.

### Measured, not assumed

`docxBridge.editorNodeCoverage.test.ts`:

| Editor node | Blocks out | The user's content |
|---|---|---|
| **Image** | 2 (just the bracketing paragraphs) | **GONE** — no text, no placeholder, no warning |
| **HorizontalRule** | 2 | **GONE**, no warning |
| **CodeBlock** | 2 | **the typed TEXT is LOST** |
| Blockquote | 3 | text kept; quote structure lost |
| TaskList / TaskItem | 3 | text kept; checkbox lost |

### Why this is the worse direction

The published residual list is about *imported* constructs we cannot carry back out. This is content the
user **created in our own editor** — they typed it, they saw it on screen, and it is not in the saved
file. `CodeBlock` loses **visible text**, not formatting.

All three total-loss cases are reachable **by paste**, which needs no toolbar button: pasting from a web
page or a Word selection brings images and rules along. Two are also reachable by accident through
StarterKit's input rules — `---` + Enter makes a horizontal rule, ` ``` ` makes a code block.

### What it changes about the plan

**Images get much cheaper.** The editor already has an `Image` node with `allowBase64` — the same head
start `hardBreak` had, which is what made task 046 four small edits. So the picture case of `w:drawing`
does **not** need a new editor node; it needs a model field and a projection that maps `w:drawing` ↔
`Image`. That pulls the most common embedded-object case from **L to M** and separates it cleanly from
charts and OLE objects, which are genuinely opaque and belong on base-carry.

**And it adds work that was not on any list**: horizontal rule, code block, blockquote and task list have
no OOXML representation in either direction. They are editor features that quietly do not survive a save.

## 2. The four pieces every fix needs

| Piece | Where |
|---|---|
| **A. Model field** | `ComposeInlineRun` / `ComposeBlock` (server) + `compose-contracts.ts` (client) |
| **B. Read** | `ComposeDocxProjectionBuilder` — capture it into the model, and into the HTML the editor mounts |
| **C. Write** | `ComposeDocumentRenderer.BuildRun` — emit it |
| **D. Editor** | A TipTap node/mark so the user sees it and `buildRunsFromNode` can re-emit it |

**D is the cost.** A, B and C are hours. Anything needing a new D is days, because it has to be visible,
selectable, deletable, and undo-able — and because a node the user can corrupt is worse than one they
cannot see.

## 2b. THE OPAQUE-ATOM PIPELINE ALREADY EXISTS — and this collapses the plan

Following the owner's "look at what the editor already provides" instinct one step further found the
thing that reorders everything. **There is already a complete opaque-atom pipeline, built in R4 task
012/021, wired into the editor today, and it is only missing its save half.**

| Half | State |
|---|---|
| Server → HTML | ✅ `ComposeDocxProjectionBuilder` emits `<span class="compose-atom" data-atom-kind="field" contenteditable="false">…</span>` for **fields, inline SDTs and complex objects** (`AppendAtom`), and a block form for block-level SDTs (`EmitBlockAtom`) |
| Editor node | ✅ `opaqueAtomNode.ts` — inline + block ProseMirror leaf nodes, `atom: true`, non-editable, registered as `COMPOSE_R4_OPAQUE_ATOMS` in `ComposeEditor.tsx` |
| **Editor → model** | ❌ **`docxBridge.ts` does not mention the atom node at all.** `collectSegments` / `buildRunsFromNode` never see it, so on an edited paragraph it is simply gone |
| Model | ❌ no `ComposeInlineRun` field for it |
| Model → OOXML | ❌ nothing to emit |

So the piece I sized as the expensive one — **D, the editor node** — is **already built for fields, SDTs
and complex objects**. The user already sees these as non-editable chips in the editor. They vanish only
because nobody wired the return path.

### What that does to the sizing

This is the same shape as task 046, at larger scale: the editor and the read path were done; only the
model round trip was missing. And crucially it is **ONE mechanism, not five** — an atom kind is a
discriminator on a single run field, so wiring the round trip once covers every kind that uses it, and
each new construct becomes a new *kind* rather than a new node.

`ComposeAtomKind` is today `'sdt' | 'field' | 'object' | 'unknown'`. **Tab and symbol should become atom
kinds too** (root §11 — extend the existing mechanism rather than author two more nodes). They fit the
contract exactly: discrete, deletable, with no interior cursor position. Styling is per-kind — a tab
renders as whitespace of the right width, a symbol as its glyph — which is a CSS concern, not an
architectural one.

### Revised plan

1. **Wire the atom round trip once** — `ComposeInlineRun.Atom { Kind, DisplayText, Payload }`, mapper
   emission, renderer re-emission. This alone fixes **fields, inline SDTs and complex objects**.
2. **Add `tab` and `symbol` as atom kinds** — read side emits them as atoms instead of a styled span /
   resolved glyph; everything downstream already works.
3. Images, and the editor-native nodes from §1b, stay separate — they are real content with their own
   editor nodes, not opaque placeholders.

**That is roughly two pieces of work where §3 lists six.** The sizing table below is kept as written
because it records what was believed at each step, but rows 1, 2, 3, 5 and 6b are all subsumed by this.

## 2c. What building it actually taught (task 048, 2026-08-24) — **tab + symbol are DONE**

The plan above said to wire the round trip generically first and add the two kinds second. **Implementation
inverted that, and the inversion was right.** Tab and symbol turned out not to need the generic mechanism at
all, because of a distinction that only became visible while writing it:

> **A tab and a symbol are SELF-DESCRIBING. A field, an inline SDT and a complex object are not.**

A `w:tab` has no payload whatsoever. A `w:sym` has exactly two scalars — a font name and a four-hex code
point — and neither is document prose. Round-tripping those is **not** "the client handling OOXML" (ADR-049
I-2): no markup crosses the wire, only the same two values any font picker would hold. A field or a content
control is the opposite: reconstructing one needs its original subtree, which is exactly what task 041
declined to ferry through the client and solved with **base-carry** instead.

So the two halves split cleanly, and the split is the ADR-049 I-2 line, not a convenience:

| | Carries | Mechanism |
|---|---|---|
| **tab, symbol** | Nothing / two scalars | **Marker runs** — `IsTab`, `Symbol { Font, CharCode }`, alongside `IsPageBreak` / `IsLineBreak` / `CommentAnchor` |
| **field, inline SDT, object** | Their own OOXML subtree | **Base-carry** (task 041's path) — the server takes the bytes from the base block; the client never sees them |

### The three things that were not obvious until it was built

1. **Run properties had to survive.** The two break markers return early from `BuildRun` — bold on a page
   break is meaningless. Copying that would have been wrong here: **an underlined tab is the fill-in leader
   line on a signature block**, and a symbol run carries bold/italic like any other. So tab and symbol swap
   the run's *text child* instead, keeping `w:rPr`. Returning early would have shipped a silent
   formatting loss inside the fix for a silent content loss.
2. **The coordinate space had to not move — and it didn't.** A tab was already an em space (U+2003) and a
   symbol already its resolved glyph; the server's offset table already counted **1** for each. A ProseMirror
   inline atom is also 1. So the atom carries *the same character it always did* and every offset, diff
   coordinate and baseline text is byte-identical to before. This is why the change is small.
3. **`.compose-atom` styles an atom as a dashed, filled, italic CHIP** — right for "a content control was
   here", very wrong for a tab or a §. Without a reset the fidelity fix would have drawn a visible dashed box
   around every tab in the document. Added `compose-atom-renderable`, and pinned it with a test.

### Also settled here

- **`w:ptab`** degrades to a plain `w:tab` (it did before too, via the same flatten). Its absolute-position
  attributes are not modeled — noted rather than silently implied to round-trip.
- **A pre-existing divergence, found and deliberately NOT fixed here**: an OPAQUE inline atom (a field)
  contributes **0** characters to the client's text coordinate space while the server's offset table counts
  its display length. Real, older than this task, and orthogonal — folding a second fix into this change
  would have made both harder to review. Pinned by a test so it cannot drift further, and left for whoever
  wires the opaque round trip.

### Where that leaves the plan

Rows 1 and 2 of §3 are **done and measured** — both flipped from §2 (lost) to §3 (carried) in the published
residual list, enforced in both directions by `ComposeResidualLossParityTests`. Remaining, unchanged:

1. **Wire the atom round trip for the opaque kinds** (field, inline SDT, object) — or extend base-carry to be
   position-aware, which may now be the better answer given how cleanly the self-describing/opaque line held.
2. **The §1b editor-native losses** (code block, horizontal rule, image) — still the worse direction, still
   step 0.

## 3. Sizing

| # | Construct | Needs | Size | Why this order |
|---|---|---|---|---|
| ✅ | **Soft line break** `w:br` | A only | **done (046)** | Editor node existed |
| 1 | **Tab** `w:tab` | A, B, C, **D** | **M** | Aligns signature blocks, defined-term lists, schedules. Visible damage on the most-edited paragraphs. Node is simple (atom, no payload) |
| 2 | **Symbol** `w:sym` | A, B, C, **D** | **M** | § and ¶ are everywhere in legal prose. Node carries `font` + `char`; renders as the resolved glyph so it *looks* unchanged |
| 3 | **Inline content control** `w:sdt` | A, B, C, **D** | **M–L** | Party names, effective dates. The block-level shell carry already exists (041) — this extends that idea inline rather than inventing one |
| 4 | **Footnote / endnote reference** | A, B, C, **D** | **L** | Marker node is trivial; the cost is keeping `footnotes.xml` / `endnotes.xml` numbering and ordering coherent when references move or are deleted |
| 5 | **Field** `w:fldSimple` / `w:fldChar` | A, B, C, **D** | **L** | A *range*, not a point (begin / instrText / separate / end). Needs the instruction preserved and a decision on whether an edited field stays live or is deliberately frozen |
| 6a | **Image** `w:drawing` wrapping a picture | A, B, C — **editor node EXISTS** | **M** | The common case by far. TipTap `Image` with `allowBase64` is already configured; needs a model field plus `w:drawing` ↔ `Image` mapping and part/relationship handling |
| 6b | **Chart / OLE object** `w:object`, `w:pict`, chart parts | base-carry (§4) | **M** | Genuinely opaque — nothing to show in an editor. Lift from the base paragraph rather than model it |
| 7 | **Editor-native, no OOXML at all**: horizontal rule, code block, blockquote, task list | A, B, C (nodes exist) | **M** | Found by the §1b survey. Blockquote and task list keep their text today; horizontal rule and code block do not |

**Rough total: 7 medium pieces of work** (was "5 medium-to-large" before the §1b survey moved images off the hard pile and added the editor-native row). Each is a vertical slice — server model + projection +
renderer + editor node + parity row — so they can be done one at a time, each ending with its row flipping
from "lost" to "carried" in `COMPOSE-WRITE-RESIDUAL-LOSS.md` and its parity test row going to a null code.

## 4. A cheaper alternative worth considering for 3–6

There is a second mechanism that avoids **D** entirely, and it is already proven in this codebase: the
**base-carry** used by task 041 for bookmarks and the block-level SDT shell. Instead of teaching the editor
about a construct, the *renderer* lifts it from the base paragraph and splices it into the rendered one.

- **Where it works**: constructs that attach to the paragraph as a whole, or whose position can be
  re-derived — the SDT shell, and plausibly embedded objects (an anchored object's position within the
  paragraph is usually not semantically load-bearing).
- **Where it does not**: anything whose meaning depends on its exact offset in edited text. A tab between
  two words the user just rewrote has no defensible new position. **This is the R4 anchor-rebasing problem
  in miniature, and it is why base-carry cannot be the answer for everything.**

Recommendation: **use base-carry for #3 and #6**, which would pull both from L to S–M and remove the two
riskiest editor nodes. Keep real editor nodes for #1, #2 and #5, where position is the meaning.

## 5. Sequencing recommendation

0. **Code block + horizontal rule + image (§1b)** — reconsider these FIRST. A code block loses the
   user's own typed **text**, and an image loses everything, both silently, both reachable by paste. Losing
   content the user created in our editor is worse than failing to carry something we imported, and the
   editor nodes already exist so the cost is model + projection only.
1. **#1 tab + #2 symbol** together — one editor-node pattern established, used twice. Highest
   damage-per-day among the IMPORT direction: both hit ordinary prose constantly and both are visibly
   broken today.
2. **#3 inline content control via base-carry** — extends 041's existing mechanism rather than adding one.
3. **#6 embedded object via base-carry** — the same mechanism again, plus relationship rewriting.
4. **#5 fields** — needs the live-vs-frozen product decision first (see §6).
5. **#4 footnote / endnote references** — last, because its cost is in a part the merge does not otherwise
   touch, and it is the rarest of the six in contracts (common in briefs).

## 6. Cross-references: answered — stay live

**Answer (2026-08-24): NOT difficult — recommend STAY LIVE.**

The owner's rule was "determine how difficult to support — if not difficult then stay live, otherwise ok
to freeze". It is not difficult, for two reasons that only became clear after §2b:

1. **The editor node already exists and already treats a field as an atom** — the user sees the field's
   cached result (e.g. "Section 4") as a non-editable chip. They cannot corrupt the interior, because an
   atom has no interior cursor position.
2. **"A range, not a point" dissolves when the field is one atom.** I sized fields as **L** on the
   assumption we would have to model `begin / instrText / separate / result / end` as separate runs and
   keep them aligned through an edit. We do not: capture the **instruction** plus the **cached result
   text** on one atom, and re-emit the canonical five-run form on save. The displayed text is *generated*,
   so the user should never be editing it anyway — which is exactly what an atom enforces.

So a cross-reference survives an edit to its paragraph and keeps updating in Word. Nested fields (a field
inside a field) are rare and can degrade to a flattened atom with the existing warning.

Original framing kept below for the record.

**When a user edits a paragraph containing a cross-reference field, should the field stay live?**

- **Stay live** — preserve the field so it keeps updating. Correct for a document that will keep being
  edited in Word, but the user may have just edited *the text the field generated*, and their edit is then
  silently discarded on the next Word refresh.
- **Freeze deliberately** — flatten to text on purpose, and say so. Honest, keeps the user's words, loses
  the automation.

Today it freezes *by accident* and warns. Either answer is defensible; the current state is only defensible
because it is reported.

## 7. What "zero loss" will mean when this is done

Not that Compose understands every OOXML construct — nothing does. It will mean:

> **Nothing is lost from a paragraph the user did not edit** *(already true — measured 100% across all
> twelve families)*, **and nothing is lost from a paragraph they did edit either.**

The residual list should then be empty except for the genuinely-unreadable cases in §4 of the published
document, which are refusals to open rather than losses on save.

---

## Related

- [`docs/architecture/COMPOSE-WRITE-RESIDUAL-LOSS.md`](../../../docs/architecture/COMPOSE-WRITE-RESIDUAL-LOSS.md) — the published list this shrinks
- `tests/integration/seam/Compose/ComposeResidualLossParityTests.cs` — the forcing function; each fix flips one row
- [`capability-gate-triggers.md`](capability-gate-triggers.md) — why a gate is not the answer to any of this
