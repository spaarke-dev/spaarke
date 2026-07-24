---
name: stable-position-anchoring-ooxml-editor-2026-07-22
description: Best-practice techniques for stable position anchoring / bidirectional position mapping between a browser rich-text editor (ProseMirror/TipTap) and underlying OOXML WordprocessingML nodes, for round-trip AI-insertion. Covers runs-vs-logical-text, bookmark/SDT/paraId anchors, ProseMirror Mapping, Yjs relative positions.
metadata:
  type: project
---

# Stable position anchoring: editor <-> OOXML (2026-07-22)

**Question**: Best-practice STABLE POSITION ANCHORING + bidirectional position mapping between a rendered rich-text editor and underlying OOXML, so an editor selection maps to a durable location in the source .docx even as it is edited. (Independent assessment of an AI drafting tool whose text-search insertion fails with "can't find the insertion point.")

**Findings**:

1. **Root cause = the runs-vs-logical-text problem.** In WordprocessingML the logical string a user sees is scattered across N `<w:r>` runs (Word splits on ANY formatting/proofing/rsid/bookmark/spellcheck boundary, and re-splits on every edit), across `<w:p>` paragraphs, and interleaved with non-text elements. So text-search insertion fails structurally: (a) target text spans multiple runs; (b) duplicate text -> which match?; (c) whitespace/normalization (tabs, `<w:tab/>`, `xml:space`, breaks) diverge from the visible string; (d) run boundaries drift after any edit; (e) rsid/proofing re-splits mean the same visible text has different run structure between saves. Canonical treatment: Eric White's search-and-replace articles; the standard workaround is split-all-runs-to-single-char, manipulate, then coalesce. Naive offset-into-XML addressing is non-durable because run structure is not stable.

2. **Anchoring taxonomy (tradeoffs):**
   - **`w14:paraId` / `w14:textId`** (Word-native per-paragraph IDs on `<w:p>`) — BEST low-cost anchor. Word writes + preserves them across edits; modern comments (`w15:commentEx`) reference paraId. Paragraph-granular only; not char-precise; can be duplicated on paragraph split (Word regenerates). Recommended as the coarse anchor layer.
   - **Bookmarks (`w:bookmarkStart`/`End`)** — invisible, range-based, survive edits inside range, id+name. BUT fragile under programmatic insert (some APIs delete the bookmark on replace), users can delete them, they can become "orphaned"/crossed on paragraph ops, and heavy bookmark use bloats/creates nesting bugs. OK for a bounded set of known insertion zones, poor as a universal per-selection anchor.
   - **Content controls / SDT (`w:sdt`)** — modern, structured, support data-binding, visibly bound region, harder for user to accidentally break than bookmarks; the "preferred modern approach" for programmatic insertion. Cost: they alter the document structure/appearance semantics, nest awkwardly, and you must own their lifecycle. Best when you control authoring and want durable named insertion slots.
   - **Stable IDs on structural elements + external map** — assign your own IDs (or reuse paraId) to paragraphs/runs and persist a map ID->node. Durable if you regenerate the map on every round-trip; the map is the thing that rots if not maintained.
   - **Canonical XML / XPath** — brittle: any structural edit invalidates the path; only viable against a canonicalized/normalized tree you control, not live Word output.
   - **Char-offset against a normalized text layer + index back to OOXML nodes** — build a flattened logical-text string with an offset->(node,intra-node-offset) index; do matching/anchoring in the normalized layer, translate back. This is the layer where fuzzy/mult-run matching actually works. Must be rebuilt after edits (offsets are not durable by themselves) — pair with stable IDs for the durable layer.
   - **CRDT/OT relative positions** — the durable answer for a LIVE editor (below).

3. **ProseMirror mapping model.** PM positions are integer token offsets into a flat address space (each node boundary/char counts). A **StepMap** produced by each atomic **Step** maps positions old->new; a **Mapping** is a pipeline of StepMaps (with lossless handling of inverted steps, which is what enables **rebasing** for collaboration/history). `map(pos, bias)` and `mapResult(pos, bias)` translate a position; **bias/assoc** (-1 left, +1 right) decides which side content inserted exactly at pos sticks to; **MapResult.deleted / deletedBefore / deletedAfter** flag whether the anchored content was removed. This gives stable-through-edits positions WITHIN a session but PM positions are NOT durable identifiers to persist — they are only meaningful against a specific doc version; you must map through every intervening step. To back-reference OOXML, the proven pattern is node **attrs** carrying round-trip data (see cardmirror: every textblock carries round-trip-only attrs incl. a verbatim OOXML spacing map + stable paragraph IDs).

4. **Yjs relative positions ("sticky" positions) = the strongest durable anchor for collaborative/persisted use.** A RelativePosition anchors to the CRDT **item ID (client,clock)** of the character next to it, NOT a numeric index, plus an **`assoc`** (>=0 stick to char after / <0 to char before). Create: `Y.createRelativePositionFromTypeIndex(type,index,assoc)`; resolve: `Y.createAbsolutePositionFromRelativePosition(relPos,doc)` -> `{index}` or **null if the anchored content was deleted**. Serializable (binary via `encodeRelativePosition`, or JSON). Survives concurrent/remote edits and syncs to the same logical location on all clients. This is exactly the anchoring an AI-insertion feature wants: capture the selection as a relative position, insert later at the resolved index, handle null (content gone) explicitly. y-prosemirror bridges PM<->Yjs so PM selections become Yjs relative positions.

5. **Published round-trip patterns/libraries.** SuperDoc (AGPL+commercial; ProseMirror+Yjs, OOXML-native, production) is the reference architecture. **cardmirror** (ProseMirror, lossless .docx round-trip, stable paragraph IDs + round-trip-only attrs) is the cleanest small public example of node-attrs-carry-OOXML-provenance. mhurhangee/docx-editor (canonical OOXML + tracked changes + collab, "agent-ready"). docx-editor.dev (parse->model->edit->serialize pipeline). All share the consensus: parse to a model, keep OOXML provenance ON the model nodes (attrs/IDs), edit the model, re-serialize — NOT HTML round-trip, NOT text-search reinsertion.

**Recommended architecture for the tool being assessed**: (1) Stop text-search reinsertion. (2) Build a normalized logical-text layer with offset->OOXML-node index for matching. (3) Anchor durably via stable IDs (reuse Word `w14:paraId` for paragraphs) + intra-paragraph offset, OR if the editor is ProseMirror/Yjs-based, capture selections as Yjs relative positions and persist those. (4) Carry OOXML provenance as node attrs (cardmirror pattern). (5) Handle the deleted-anchor case explicitly (Yjs returns null; PM MapResult.deleted).

**Sources**:
- Eric White — Search-and-Replace / working-with-runs (runs-vs-logical-text canonical): ericwhite.com/blog/search-and-replace-text-in-an-open-xml-wordprocessingml-document, learn.microsoft.com/office/open-xml/word/working-with-runs
- ProseMirror mapping: marijnhaverbeke.nl/blog/collaborative-editing.html; github.com/ProseMirror/prosemirror-transform/blob/master/src/README.md; prosemirror.net/docs/guide (Mapping/StepMap/MapResult/rebase)
- Yjs relative positions: docs.yjs.dev/api/relative-positions; github.com/yjs/docs/blob/main/api/relative-positions.md; deepwiki.com/yjs/yjs/6.2-relative-positions; discuss.yjs.dev "keep RelativePositions up to date" 2653
- Anchors: officeopenxml.com / MS Learn on w:sdt content controls + bookmarks; lennilobel.wordpress.com (content controls); opendope.org conventions
- Round-trip libs: github.com/ant981228/cardmirror; github.com/mhurhangee/docx-editor; docx-editor.dev/docs word-fidelity; superdoc (github.com/superdoc-dev/superdoc); HN 48228411
- Prior Spaarke memos: [[openxml-docx-compose-r2-2026-06-29]], [[adeu-architecture-study-2026-06-29]], [[server-docx-authoring-numbering-2026-07-18]]

**Open questions**:
- Does the assessed tool already use ProseMirror/TipTap+Yjs (then relative positions are a small delta) or a bespoke editor (then it must build the normalized-layer + ID map itself)?
- Word regenerates `w14:paraId` on paragraph split/merge — need to confirm collision/duplication behavior for a same-paraId map before relying on it as sole key.
- cardmirror's exact attr schema for OOXML provenance — worth a code-level read if this becomes a Spaarke Compose concern.
