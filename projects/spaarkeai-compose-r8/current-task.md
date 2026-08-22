# Current Task State — `spaarkeai-compose-r8`

> **Last Updated**: 2026-08-21 (task 040 complete) · **Recovery**: read "Quick Recovery" first.
> Everything below is recoverable from files alone.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Phases 1–3** | ✅ COMPLETE. Gate **PASSED**. |
| **Task 040** | ✅ **COMPLETE** — the merge is the production save path. |
| **Task 041** | 🔄 **IN PROGRESS** — baseline measured, two defects fixed. FR-A05 atom carry NOT yet built. |
| **ADR-049 third amendment** | ✅ **APPLIED** 2026-08-21. Nothing pending. |
| **Next task** | **041** — FR-A04 opaque-atom carry + the edited block's residual formatting loss. **Not optional, not deferrable** (gate §7 condition 1). |
| **Alternative** | **051** — Track C anchor supply. Independent of everything above; kills the *"wording differs slightly"* banner. |
| **Next Action** | Implement **FR-A05 carry, bookmarks first** — see [`notes/edited-block-loss.md`](notes/edited-block-loss.md) "What FR-A05 must now deliver". |

### The one thing to understand

**The save now preserves untouched blocks and does not preserve the edited one.**

Untouched blocks are cloned verbatim: 100% overall and 100% near-tier on all 18 corpus documents, up from
18.08% / 6.67%. The block the user typed in is still rendered from the model. Task 040 added `pPr` +
dominant-`rPr` inheritance, which stops it collapsing to Normal — but a paragraph whose formatting varies
*mid-run* is levelled to its dominant formatting.

**Task 041 owns that, and the gate cannot see it** — the oracle excludes the edited block by construction.
It is now MEASURED: **10 of 18 corpus documents come through with the edited block intact**; the other 8 lose
bookmarks (2), a block-level `w:sdt` (1), soft breaks (1), run-level formatting variation (2) and run/text
boundaries (4). Full table + per-construct priorities: [`notes/edited-block-loss.md`](notes/edited-block-loss.md).

**Dropping `w:bookmarkStart`/`w:bookmarkEnd` is the worst of these and is not obvious**: it breaks
cross-references *elsewhere* in the document. The user edits paragraph 12; a `REF` field in paragraph 40
stops resolving. Build that carry first.

---

## Numbers of record (production implementation, not the prototype)

| | Control (master) | Production merge | Bar |
|---|---:|---:|---|
| Overall, lenient, block-weighted | 18.08% | **100.00%** | ≥95% |
| Near tier, lenient | 6.67% | **100%** (14/14 measurable) | 100% every doc |
| Overall, strict | 12.18% | **100% on 16 of 18** | ratchet only |

18 documents · 271 blocks · **253 cloned, 18 rendered** · 0 hard-fails · 0 honesty violations · flat 100%
over 5 round trips · +3.9 ms/doc · publish **43.69 MB** (−1.27) · no new NuGet · no new CVE ·
NetArchTest **36/36** · full BFF suite **10,792 / 0**.

---

## What task 040 actually changed

**New**: [`Services/Compose/ComposeBlockMerge.cs`](../../src/server/api/Sprk.Bff.Api/Services/Compose/ComposeBlockMerge.cs)
— a renderer collaborator, no DI registration. It produces a **plan**; it never appends to `w:body`
(ADR-049 I-5 — one body author, and it is the renderer).

**Changed**: `ComposeDocumentRenderer.RenderIntoCarrier` captures the base side before the swap and executes
the plan. `mergeUnchangedBlocks` now defaults to **true**.

**Five reconciliations against the gate findings** (all in `notes/merge-mechanism-results.md` §1):

1. **FR-A01 dropped** — the stamper promotion is unnecessary; the renderer already stamps every `w:p`.
2. **The comparison strips `ParaId`** — mandatory, not cosmetic. The client's `data-paraid` is a *minted*
   id for unstamped paragraphs while the content model reports `null`. Without this the merge scores 100%
   at the renderer and near 0% through the wire.
3. **LCS alignment, not document order** — positional pairing gives **zero** preservation on insert/delete.
   The prototype never measured either.
4. **One shared list-run cursor** — the prototype's cursor was *destroyed* at every clone boundary, not
   merely un-advanced.
5. **Basic FR-A04 inheritance done here**; 041 keeps opaque-atom carry + character-level re-association.

---

## Traps (all live)

- **`w:pPr` / `w:rPr` are `xsd:sequence` — child ORDER is schema, not style.** Task 040's inheritance appended
  and produced invalid output; it now inserts at the ECMA-376 position. Any future code that adds a child to
  either element must respect the order tables in `ComposeBlockMerge`.
- **Corpus fixtures are now held schema-valid** by `CorpusFixture_IsSchemaValidWordprocessingML`. Nine
  project-authored fixtures were repaired 2026-08-22. **Never "fix" a real-world fixture** — their quirks are
  the test case, and all four were already valid.

- **`mergeUnchangedBlocks` is a TEST SEAM, not a feature flag.** Bound to no configuration. It exists so the
  measurement can run a control arm through the same renderer — the anti-vacuity evidence the gate rests on.
- **Three seam tests are pinned to `mergeUnchangedBlocks: false`** (hyperlink/comment re-authoring, interior
  `sectPr` flattening, revision-id minting). They target the *render* path; with the merge on they post an
  unmodified projection, so everything clones and the behaviour under test never runs. Reason is at each call site.
- **Do NOT "fix" the `mc:AlternateContent` paraId re-mint** without reading `merge-mechanism-results.md` §4.3.
  It was implemented, measured (takes 2 documents from 66.67%/95.92% to 100% strict) and **deliberately
  reverted** — it breaks task 011's uniqueness guarantee, and strict is a ratchet, not a gate.
- `git checkout <commit> -- <path>` writes the **INDEX**; the safe A/B form is `git show <commit>:<path> > <path>`.
- **Run the FULL test project before closing a task**, never `--filter`.
- **Warm up before measuring performance** — the first pass is JIT noise.
- Bash heredocs mangle escapes inside quoted Python — write patch scripts to the scratchpad and run them.
- `w14:paraId` must be **8 hex digits, non-zero, ≤ `0x7FFFFFFF`**.
- SpaarkeAi is Vite and aliases shared-lib SOURCE — clear `dist/ node_modules/.vite/ .vite/` before every build.
- Use `pwsh`, not `powershell`, for the deploy hash-verify step.

---

## Owner-visible banners in dev

| Banner | Track | Owner | Status |
|---|---|---|---|
| "Some formatting was simplified when saving" | **A** | 040–044 | 040 done; **044 must narrow the taxonomy** — cloned blocks must stop warning |
| "wording differs slightly from this document" | **C** | **051–053** | untouched, startable now |

## Not yet deployed

Nothing from Phases 3–4 is live. Task 017's banner fix and the merge both ship with the next paired
BFF + `sprk_spaarkeai` deploy (NFR-05).

## Tasks complete

**Phase 0** 001 · 002 — **Phase 1 (Track S)** 010 · 011 · 012 · 013 · 014 · 015 · 016 · 017 · 018 —
**Phase 2** 020 · 021 · 022 · 023 — **Phase 3** 030 · 031 — **Phase 4** 040 — **Phase 5** 050

**Blocked**: 074 ⛔ (`ComposeShadowPatchEngine` subsumption NOT-CONFIRMED — gate-decision §5).

## Evidence trail

`projects/spaarkeai-compose-r8/notes/` — `track-s-uat.md` · `gate-contract.md` · `control-measurement.md` ·
`merge-prototype-results.md` · `gate-decision.md` · **`merge-mechanism-results.md`** ·
`adr-049-third-amendment-draft.md` · `honest-failure-set.md` · `document-size-ceilings.md`
