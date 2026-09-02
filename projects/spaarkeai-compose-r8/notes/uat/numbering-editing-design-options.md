# Live numbering (UAT items 3 + 4) — technical approach

> 2026-09-02 · the R5 G3 escalation `composeNumberAtomExtension.ts` asks for, answered.
> Grounded in the existing code + ProseMirror maintainer guidance (sources at the end).

## The constraint everyone assumes is blocking — isn't

The current design refuses to make numbering editable, and the reason is stated in
`composeNumberAtomExtension.ts`:

> A decoration is NOT part of the ProseMirror document: it cannot be selected into, cannot be typed into,
> **cannot shift the paragraph's own text offsets** (the offset-addressing table the step
> interceptor/annotation-reanchor system relies on — task 011 — indexes RUN text only; an inline atom NODE
> inserted as literal paragraph content would have desynced that table).

Read precisely, that rules out **inline content**. It does **not** rule out a **node attribute** — and the
number already IS one: `data-computed-number` / `data-numbering-level`, registered via `addGlobalAttributes`
so it survives the `setContent` parse and round-trips through `getHTML()`. A node attribute changes no text
offset; only inline content does.

**So the rendering mechanism is already correct and does not need to change.** What is missing is a single
thing: **nothing ever recomputes the attribute after load.** The number is a load-time snapshot, which is
precisely why removing numbering doesn't renumber (item 3) and adding it paints nothing (item 4 — the native
`<ol>` marker is suppressed unconditionally, so with no server-computed number there is no number at all).

This narrows the question from *"can numbering be editable?"* to **"where does recomputation run?"**

## The mechanism: `appendTransaction`

This is the ProseMirror maintainer's own prescription for exactly this problem:

> When a list item is changed by a transaction, check its number, and if it is incorrect, append a
> transaction that fixes it.

It fits our constraints well:

- It runs **after** the user's transaction, so it never fights the edit.
- It writes **attributes**, not content — offsets stay stable, and the redline/reanchor table is untouched.
- Renumbering therefore produces **no tracked change**, which is correct: a computed number is not authored
  text and must never appear in a redline or in "summarise what changed".
- Performance is manageable on large documents by gating recomputation to changed ranges rather than
  rebuilding the whole decoration set per transaction.

## Where the computation lives — three options

### A. Client-side numbering engine

Mirror `NumberingComputationEngine` in TypeScript; `appendTransaction` recomputes affected list chains.

- ✅ **Instant.** Pressing "numbered list" numbers the paragraph immediately, which is the only behaviour a
  user reads as working.
- ❌ **A second numbering engine** — the two-engine drift this whole project exists to prevent.

### B. Server round-trip per structural change

Debounced call re-derives numbering server-side and returns the label map.

- ✅ **One engine**, so no drift by construction.
- ❌ **Latency on every structural edit** — add, remove, indent, outdent, paste, delete-a-paragraph all change
  numbering. Debouncing makes numbers visibly lag, and **a legal number that is briefly wrong is worse than
  one that is honestly static**: it still reads as authoritative while it is incorrect.
- ❌ Puts a chatty endpoint next to the save path.

### C. ✅ RECOMMENDED — client engine for immediacy, server authoritative, parity enforced

1. **Client engine** recomputes optimistically in `appendTransaction`. Numbers move instantly.
2. **Server stays authoritative.** The existing stateless `POST /api/compose/project` — already "the one
   reader" — re-derives numbering from real OOXML on every load/reproject, and the client adopts that result.
   The client engine is a *predictor*; the server is the *truth*.
3. **Parity is enforced mechanically, not by discipline** — the pattern we built and proved for the citation
   resolvers under #699 generalises exactly: a shared corpus
   (`tests/fixtures/compose-numbering-parity/cases.json`) executed by **both** engines, plus a source-level
   drift detector pinning the shared vocabulary (level formats, `numId` scoping, restart rules).

**Why C rather than A:** the difference is not the engine, it is the *forcing function*. Option A's drift risk
is real, and the reason it is acceptable here is that this repo now has a working, tested answer to it — one
that already caught a seeded one-sided change in CI. Adopting C without building that corpus is adopting A.

**Why C rather than B:** B trades a real, felt UX cost for a risk C has already neutralised.

## ~~The harder half, which is NOT the renumbering~~ — ❌ **THIS SECTION WAS WRONG. Corrected 2026-09-02.**

> **What it claimed**: that creating a list means building OOXML numbering authoring we do not have — "a
> new list has no `w:numPr` to inherit, the content model must carry list membership + level, and
> `ComposeDocumentRenderer` must emit `w:numPr`… this is where the OOXML fidelity risk actually sits."
>
> **Every one of those is already built.** I asserted the gap from the client symptom instead of reading the
> write path. Recording the claim rather than deleting it, because the *sizing* in this document was
> derived from it and anyone who read the earlier version needs to see it retracted.

What actually exists today:

| Piece the section called missing | Where it already lives |
|---|---|
| Content model carries list membership + level | `ComposeBlock { Kind = ListItem, Level, Ordered, StartsNewList, NumId }` — `ComposeContentModel.cs:355-419` |
| Client serializes editor `<ol>/<li>` into it | `docxBridge.ts:957` — and it **preserves the imported `numId`** when the loaded block was already a ListItem (`:1195`) |
| Renderer emits `w:numPr` | `ComposeDocumentRenderer.BuildListItem` (`:1021`) — direct `numPr` with `ilvl` + `numId` |
| Minting / merging `numbering.xml` | **`ComposeNumberingAuthor.cs`** (424 lines) — authors `abstractNum` + `num`, owns three abstract schemes, and **remaps ids to merge into an existing carrier's numbering part** |
| Removal emitting the *absence* of `w:numPr` | A non-list block renders via `BuildParagraph`, which never appends `numPr` |

Headings are deliberately different and this is not an oversight: `BuildHeading` emits **no** `numPr`
because the `Heading{level}` style carries its own (FR-27) — a direct `numId` there would double-number.

### So what is actually left

**The likely truth about UAT item 4 is that the DOCUMENT is already right and only the EDITOR is wrong** —
a list created in the editor probably saves as a genuinely numbered list, and the user simply cannot see a
number while editing, because `useStyles().editorSurface` suppresses the native `<ol>` marker
*unconditionally* and a new block has no server-computed number to paint.

**That is a claim, not a finding — and one cheap experiment settles it**: create a list in Compose on a
loaded document, save, open the result in Word. Numbered → item 4 is display-only and the remaining work
is small. Not numbered → the round trip has a real hole and the pre-correction sizing was closer.
**Run this before scoping anything else**; it is the highest-information hour available and it decides
whether the rest is small or medium.

### The one genuine design decision

Un-suppressing the native marker for editor-created lists collides with **invariant F-3**: a projected
paragraph whose `numId` could not be resolved is deliberately left unnumbered (*never fabricate a number*),
and un-suppressing would hand it a browser-invented one. Distinguishing "editor-created, browser may
number it" from "projected but unresolvable, must stay bare" needs a projection-emitted discriminator on
the `<ol>` itself. That is the decision worth a design gate — not the OOXML authoring, which is done.

### And it re-opens the option comparison

Option B (server round-trip) was rejected partly because numbering would visibly lag. But if new lists can
carry the **native browser marker** while *loaded* numbering stays server-computed, the "briefly wrong
number" objection weakens considerably — a native `<ol>` renumbers instantly and for free. That would
deliver items 3 and 4 **without a second numbering engine at all**, and therefore without needing the
parity corpus that Option C's step 1 exists to build.

Option C remains the owner-approved direction and this does not overturn it. But the verification
experiment above should be run **before** step 1 commits effort to a corpus that a cheaper shape may not
need.

## Suggested sequence

| Step | Work | Why this order |
|---|---|---|
| 1 | **Numbering parity corpus + drift detector** | Build the forcing function *before* the second engine, not after. Reuses the #699 pattern wholesale. |
| 2 | **Client numbering engine + `appendTransaction`** | Items 3 + 4 *visually* resolved; gated by step 1 from day one. |
| 3 | **Content-model list membership + `w:numPr` authoring/removal** | The real fidelity work; extends the residual-loss parity test. |
| 4 | **Reconcile-on-reproject** | Server result adopted over the client prediction; proves the client is a predictor, not a fork. |

## Also verify first (U-0)

Screenshots suggest removing numbering from *"1.2 Technical Field of the Invention"* also cost it its
**heading style**. If that reproduces it is a **separate and more serious defect** — a style loss on edit,
inside R8's own fidelity scope — and it should be fixed before any of the above. Reproduce; do not assume.

## Sources

- [Multi-level lists with indexes in the content — discuss.ProseMirror](https://discuss.prosemirror.net/t/multi-level-lists-with-indexes-in-the-content/5643) — the maintainer's `appendTransaction` prescription; node-attribute vs inline-content trade-off
- [ProseMirror Guide](https://prosemirror.net/docs/guide/) — `appendTransaction` semantics
- [What is the difference between decoration and NodeView in tiptap? — ueberdosis/tiptap #2865](https://github.com/ueberdosis/tiptap/discussions/2865) — decorations are visual and non-persisting; attributes/node views persist
- [Tiptap Extension API](https://tiptap.dev/docs/editor/extensions/custom-extensions/create-new/extension) — changed-range gating for decoration recomputation on large documents
