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

## The harder half, which is NOT the renumbering

Item 4 needs more than a label. **Creating a list that did not exist means authoring OOXML numbering.**

- A new list has no `w:numPr` to inherit. The **content model must carry list membership + level**, and
  `ComposeDocumentRenderer` must emit `w:numPr` referencing a numbering definition — either reusing an
  existing `numId` in the document or minting an `abstractNum` + `num` pair in `numbering.xml`.
- **Removing** numbering must emit the *absence* of `w:numPr` (or an explicit override), not merely stop
  painting the label — otherwise the document still says "numbered" and Word renumbers it on open.
- Both are **write-path changes**, so both are governed by ADR-049's invariants and by the residual-loss
  parity test. This is where the OOXML fidelity risk actually sits, and it should be scoped and tested as
  its own piece rather than assumed to come along with the client work.

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
