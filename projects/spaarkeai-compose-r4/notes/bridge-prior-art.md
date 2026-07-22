# R4 Bridge — Prior Art & Techniques (permissive, studiable)

> **Created**: 2026-07-22 (researcher pass, license-verified via GitHub API + official docs)
> **The "bridge"** = mapping editor edits (ProseMirror) → deterministic positions in the canonical OOXML (OpenXML SDK), anchored by STABLE IDs, applied surgically. This file catalogs the prior art so we don't reinvent it.
> **Headline**: the bridge is NOT novel — every one of its six sub-problems has strong, **permissively-licensed** prior art.
>
> **⚠️ CORRECTION 2026-07-22 (owner-caught, researcher-verified)**: the earlier "Eigenpal = closest whole-system analog, vendorable" claim was **WRONG**. **EigenPal is a workflow-automation company**; its official `eigenpal/docx-editor` repo (created 2026-07-20) is a **closed-engine facade** — the open `@docx-editor.dev/core` is *contract-only stubs that throw* (`parseDocx`/`serializeDocx`/`toJSON` all reject "no implementation"); npm is `0.0.1-placeholder`, ~306 dl/mo; the real parser/serializer/layout engine is **proprietary**. The **real Apache-2.0 engine survives ONLY in a frozen third-party fork — `sorenlouv/docx-editor`** (npm `@sqren/docx-editor@1.0.3`, v1.9.0-era, frozen 2026-06-29), which *does* contain `docx/{document,paragraph,run,numbering,comment}Parser.ts` + a `serializer/`. **Net effect on the plan**: projection layer = **build-our-own** (extend Phase-1 `ComposeDocxProjectionBuilder`), optionally **seeded by studying the frozen fork**; **Docxodus (server, MIT, active) is the surviving real vendor option.**

---

## Findings table

| # | Technique family | Best permissive reference | License | What to borrow | Pitfall |
|---|---|---|---|---|---|
| 1 | **Client op + position rebasing** | `prosemirror-transform` (Step/StepMap/Mapping), `prosemirror-changeset` | **MIT** | Invertible atomic `Step`s; `Mapping.map(pos,bias)`/`mapResult` rebases any saved position (selection, comment anchor, pending-edit marker) forward through concurrent local edits, reports if inside deleted content; `Transform.invert()` for reject/undo; `prosemirror-changeset` → minimal insert/delete set for redline rendering | PM positions are **absolute token offsets** — they die across server round-trips. In-session rebasing only; must re-anchor to stable IDs for persistence |
| 2 | **Stable-block-ID / op schema** | **Slate** (`ianstormtaylor/slate`); Lexical (`facebook/lexical`) | **MIT** | Slate's small closed discriminated-union op set `{type, path, offset, properties}` + `Path.transform` — the cleanest public schema to mirror for our `{op, paraId, offset}` | Slate `path` (array indices) is stable **within a session only**, not across reload — our `w14:paraId` is the durable key it lacks. Borrow the op *shape*, not the identity model. Notion = closed-source (pattern only) |
| 3 | **OOXML-as-truth / editor-as-projection** | ⚠️ official EigenPal repo = **closed facade** (unusable). Real engine = **frozen fork `sorenlouv/docx-editor`** / npm `@sqren/docx-editor@1.0.3` | Apache-2.0 (fork) | The **frozen fork** has the real `docx/{document,paragraph,run,numbering,comment}Parser.ts` + `serializer/` → **study-reference** for our own projection, or vendor-and-own. Official repo: only the *model types* (`types.ts`, `DocRange`/`Revision` anchoring) + fidelity docs are useful | Official = contract-only stubs that throw; npm `0.0.1-placeholder`, ~306 dl/mo. Fork = frozen/unmaintained → you inherit all maintenance; build-completeness **unverified (spike first)**. SuperDoc = AGPL (patterns only) |
| 4 | **CRDT position anchoring (theory)** | **Yjs `RelativePosition`** + `y-prosemirror`; Automerge `Cursor`; **Peritext** (`inkandswitch/peritext`) | **MIT** | The concept "a stable anchor is a position *relative to a durable identity*, not an integer count" — **transferable to paraId+offset WITHOUT adopting a CRDT**. Peritext essay = the theory | Naive intra-paragraph **char offset still shifts** if an earlier edit lands in the same paragraph → anchor to `(paraId, runIndex, run-local-offset)` and re-derive on apply. Peritext frozen since 2022 (research ref) |
| 5 | **Editor↔canonical incremental sync** | **LSP `textDocument/didChange`** (`TextDocumentContentChangeEvent`) | Spec (CC-BY) | Shape: client streams **ranged** deltas `{range, text}` against a server-canonical model + a **version number guards ordering**. Map `(line,char)`→`(paraId, run-offset)`; map LSP version int → our SPE eTag/If-Match | LSP ranges are `(line,char)` into **flat text** — OOXML has no flat char stream. Borrow the shape + version-guard, define our own coordinate system |
| 6 | **Server-side .NET OOXML surgical patch** | `dotnet/Open-XML-SDK`; **`JSv4/Docxodus`** (+ Python-Redlines); `Codeuctivity/OpenXmlPowerTools` (WmlComparer) | **MIT** (all) | Canonical recipe: match `w:p` by `w14:paraId` → walk runs accumulating text length until offset lands in run R → `Run.Clone()` split R preserving `RunProperties` → wrap inserts in `InsertedRun`(w:ins)/deletes in `DeletedRun`(w:del) w/ Author/Date/Id → **mutate the OpenXml DOM, never string-edit document.xml**. Docxodus = most directly studiable .NET redline engine | Paragraph-mark deletion (merge paragraphs) = w:del on the para-mark glyph in `w:pPr/w:rPr` — hardest edge. Numbering lives in a **separate** numbering.xml part. OfficeDev PowerTools archived 2019 → use live forks |

## Recommended reading list (ranked)

1. **Frozen fork `sorenlouv/docx-editor` / `@sqren/docx-editor@1.0.3`** (Apache-2.0) — the ONLY place the real OOXML parser/serializer survives (official EigenPal repo is a closed facade). Study its `docx/*Parser.ts` + `serializer/` as the reference for our own projection; vendor-and-own only after a build spike.
2. **`prosemirror-transform` StepMap/Mapping** + the PM "Documents/Transforms" guide — the position-rebasing primitive verbatim.
3. **Slate `Operation` + `Path.transform`** (MIT) — the op-schema shape to mirror.
4. **JSv4/Docxodus + Python-Redlines** (MIT) — .NET/OpenXML split-run + w:ins/w:del engine for the server half; evaluate as a vendored starting point vs. building on Open-XML-SDK directly.
5. **Yjs `RelativePosition` + the Peritext essay** — the "anchor survives concurrent edits" theory behind paraId+offset.
6. **LSP `textDocument/didChange` spec section** — ranged-incremental-against-canonical-server framing + version guard.

## Design decisions this research forces (fold into spec)

- **DECISION — anchor coordinate = `(paraId, runIndex, run-local-offset)`, NOT `(paraId, char-offset)`.** CRDT-inspired insurance against intra-paragraph offset drift (an earlier same-paragraph edit shifts a bare char offset). Cheap, no CRDT dependency. Sharpens D2.
- **DECISION — projection layer = build-our-own** (extend Phase-1 `ComposeDocxProjectionBuilder`). The Eigenpal "vendor a maintained library" option is DEAD (official repo = closed facade). Optionally **study/vendor the frozen Apache-2.0 fork `sorenlouv/docx-editor`** as a reference/seed — after a build spike confirms it's complete + buildable. Do NOT take a runtime dependency on anything EigenPal ships today.
- **EVALUATE — vendor Docxodus (MIT, active) as the server patch-engine starting point** vs. building directly on Open-XML-SDK. It may already implement split-run + w:ins/w:del. This is the surviving real vendor option.
- **BORROW — Slate's op-schema shape** for the Phase-0 operation contract; **borrow ProseMirror `Mapping`** for the client op-log rebasing + the AI-generation bookmark.

## Caveats / unverified (spike before relying)

- Whether the **frozen fork `sorenlouv/docx-editor` is complete + buildable end-to-end** (parser+serializer dirs present, has dist build config; NOT compiled/verified) — build spike before betting on it.
- Whether the fork **anchors on `w14:paraId` or a synthetic import-time id** — determines direct reuse of its anchor. Requires a source read.
- Whether the **Docxodus WASM viewer** does client-side offset→run mapping we could reuse, or is render-only.
- **License-scan false positives**: Yjs + Codeuctivity/OpenXmlPowerTools report GitHub `NOASSERTION` but raw LICENSE files are verbatim **MIT** (confirmed). ProseMirror repos show "archived" = a **forge migration**, NOT abandonment; NPM packages remain MIT + maintained.

_Researcher memo: `../../spaarkeai-compose-r3/.claude/agent-memory/researcher/editor-server-bridge-primitives-2026-07-22.md`._
