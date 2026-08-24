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

## 6. One product decision needed before #5

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
