# Current Task State — `spaarkeai-compose-r8`

> **Last Updated**: 2026-08-23 (context-handoff) · **Pushed**: PR #806, 0 unpushed, working tree CLEAN
> **Recovery**: read "Quick Recovery" first. Everything below is recoverable from files alone.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Phases 1–3** | ✅ COMPLETE. Architecture gate **PASSED**. ADR-049 third amendment **APPLIED**. |
| **Phase 4** | 040 ✅ · 041 ✅ · 042 ✅ · **044 🔄 OPEN (one requirement left)** · 043 🔲 not started · 045 🔲 |
| **Next Action** | **FR-A09** — the only thing left in task 044. See "What FR-A09 needs" below. |
| **Alternatives** | **043** (capability gate / "Edit a copy") · **051** (Track C — the *"wording differs slightly"* banner, independent of all Track A work) |
| **Gate status** | Server **10,916 / 0** · Client (Jest) **1,127 / 0** · NetArchTest **36/36** · publish **43.69 MB** (−1.27 vs 44.96; ceiling 60) · no vulnerable packages |

### The one thing to understand

**Untouched blocks are now preserved; the block you edit is mostly preserved; nothing is deployed.**

| | Control (master) | Now |
|---|---:|---:|
| Untouched-block preservation, lenient | 18.08% | **100.00%** (18/18 docs) |
| Near tier | 6.67% | **100%** (14/14 measurable) |
| Strict | 12.18% | **100% on 16 of 18** |
| **Edited block intact** | — | **12 of 18 documents** |

---

## What FR-A09 needs (the only thing left in 044)

> PDF-sourced documents do not track their synthesized file's version coordinates, so after a page
> **refresh**, save two cannot resolve its baseline and falls back to a full rebuild.

Not started. What a fresh session should establish first:

1. **Reproduce it.** The POML is explicit that it must be verified across a page **REFRESH** (real client
   state loss), not two saves in one session. Measure before building — on every task this project has
   found the POML's assumption was at least partly wrong.
2. **Trace the PDF intake path**: `ComposeService.cs` ~line 332 (`IsPdfSource` → `ProjectPdfToDocxAsync`
   → `SynthesizeDocument`), then what `HasBaselineVersionCoordinates(request)` reads on save two.
3. **Version-coordinate state MUST use `IDistributedCache`** (ADR-009). Never `IMemoryCache`.
4. **Do not conflate it with FR-A08** (just shipped): a PDF-sourced document's second save is now correctly
   treated as **Authored for warnings** via the durable marker, so its fidelity warnings are already
   suppressed. FR-A09 is about **baseline resolution**, not warnings.

**Also open in 044**: the criterion *"an Authored document STILL receives save-outcome warnings"* has **no
end-to-end test**. Two levers were tried and neither fired through the wire (mixed
`contentModel`+`operationLog` → rejected as an unsupported op shape before reaching the warning; live-eTag
change between load and save → the concurrency warning did not trigger from the metadata mock alone). The
property holds structurally — every save-outcome warning is constructed after the provenance capture — but
that is an argument from the code, not evidence from a run. Recorded in the test file and on the 045 list.

---

## What shipped since the last handoff

| Task | Result |
|---|---|
| **ADR-049 3rd amendment** | APPLIED — concise + a NEW `docs/adr/` twin + both ADR INDEXes + **root CLAUDE.md §17** (all three pointer surfaces still described R4's byte-patch as the current save contract) + `.claude/CHANGELOG.md` |
| **040** merge in production | 18.08% → 100%. `ComposeBlockMerge.cs` — a renderer collaborator, no DI registration. **LCS alignment**, not document order (positional pairing gives ZERO preservation on insert/delete). Comparison **strips `ParaId`** — mandatory: the client's `data-paraid` is minted, the content model's is null. |
| **041** baseline + FR-A05 carry | Edited block 10 → **12 of 18 intact**. Carries **bookmarks** (dropping one breaks cross-references *elsewhere in the document*) and the **content-control shell** — from the BASE block, not a client payload. |
| **042** FR-A11 integrity | Four of five criteria already satisfied *structurally* by 040. **FR-G05 now RUNS** — headless LibreOffice opens four merged documents and the edit is still there. |
| **044 (part)** warning taxonomy | The false banner was on the **client**, folding load-time flatten warnings into the save. No longer folded. And the **silent loss now warns**: `Engagement Letter` → `edited-paragraph-line-break-dropped ×2`. |
| **044 FR-A08** | Authored ≠ Imported for warnings, read from the durable marker, scoped **by provenance** rather than a code list. |

---

## Defects found and fixed beyond task scope

1. **Schema-invalid output from 040's own inheritance** — `w:pPr`/`w:rPr` are `xsd:sequence`; it appended.
   Now inserts at the ECMA-376 position.
2. **10 of 18 corpus fixtures were schema-invalid** (5 missing `xmlns:w14`, 2 out-of-order children, 1
   duplicate VML id). Nine repaired — all project-authored; the four real-world documents were already
   valid and were left exactly as received. `CorpusFixture_IsSchemaValidWordprocessingML` now holds the
   corpus to this standard, and caught the next new fixture's defect within a day.
3. **The op-log patch path re-serialized `word/comments.xml` on every save**, breaking its own
   byte-identity guarantee — `commentsPart.Comments` materializes the DOM. Invisible for the whole project
   because **no corpus fixture had a comments part**. Fixed with an `XmlReader` scan.
4. **The corpus had ZERO comment ranges** — and FR-A11 is entirely about comment ranges, so 18 green rows
   were evidence of nothing. New fixture `comment-ranges-multiparagraph.docx` (checked-in generator).
5. **Two near-vacuous tests corrected** — revision-id uniqueness asserted over an empty set on a document
   whose filename says "track changes" but which contains none; duplicate-paraId asserted the same
   property for two fixtures carrying different collisions (body-level vs cross-part).

---

## Experiments implemented, measured, and REVERTED (do not repeat)

Both are recorded at their call sites, so the next reader finds the measurement rather than the temptation.

| Change | Looked right because | Measured |
|---|---|---|
| Exclude opaque regions from `AssignParaIds` | Stops mutating cloned `mc:AlternateContent` subtrees | Takes 2 docs to 100% strict — but breaks task 011's paraId-uniqueness guarantee. **Strict is a ratchet, not a gate**; trading a safety invariant for a non-gating number is exactly what the ADR-049 paired MUST forbids. |
| Emit `xml:space="preserve"` only when needed | Matches what Word "should" do | **Markedly worse**: intact fell 12 → 2. Word emits it far more liberally. The residual `p/r/t` class is attribute presence, not text loss. |

---

## Residual for task 045

- **`w:br` soft breaks** (1 doc) and **run-level `rPr` variation** (2 docs) on the edited block — both are
  *projection* (read-side) gaps that base-carry structurally cannot reach.
- **Reorder** yields no merge benefit (LCS matches never cross).
- **`mc:AlternateContent` paraId re-mint** — 2 docs below 100% strict; the reverted experiment above.
- **The untested FR-A08 outcome-warning criterion.**

---

## Traps (all live)

- **`w:pPr` / `w:rPr` are `xsd:sequence`** — child ORDER is schema, not style. Use the order tables in
  `ComposeBlockMerge`.
- **The I-7 source audit scans source TEXT including comments.** A comment containing the banned membership
  call trips it. Move the code, not the guard.
- **`mergeUnchangedBlocks` is a TEST SEAM, not a feature flag** — bound to no configuration; it exists so
  the measurement can run a control arm through the same renderer.
- **Three seam tests are pinned to `mergeUnchangedBlocks: false`** — they target the *render* path.
- **Corpus fixtures are held schema-valid.** Never "fix" a real-world fixture — their quirks are the test case.
- **Compose.Components uses Jest, not vitest.**
- **Run the FULL test project before closing a task**, never `--filter`.
- Bash heredocs mangle escapes inside quoted Python — write patch scripts to the scratchpad and run them.
- `git checkout <commit> -- <path>` writes the **INDEX**; the safe A/B form is `git show <commit>:<path> > <path>`.
- `w14:paraId` must be **8 hex digits, non-zero, ≤ `0x7FFFFFFF`**.
- **CI pushes auto-format commits to this branch** — fetch and rebase before pushing (happened twice).
- SpaarkeAi is Vite and aliases shared-lib SOURCE — clear `dist/ node_modules/.vite/ .vite/` before building.
- Use `pwsh`, not `powershell`, for the deploy hash-verify step.

---

## Owner-visible banners

| Banner | Track | Status |
|---|---|---|
| "Some formatting was simplified when saving" | A | **Should now be gone** for documents that lose nothing — but **undeployed** |
| "wording differs slightly from this document" | **C** | Untouched. **051–053**, independent of everything above |

## Not deployed

**Nothing from Phases 3–4 is live.** Task 017's banner fix, the merge, the carry and the taxonomy all ship
with the next paired **BFF + `sprk_spaarkeai`** deploy (NFR-05). Never build from a net8 tree.

## Tasks complete

**Phase 0** 001 · 002 — **Phase 1 (Track S)** 010–018 — **Phase 2** 020 · 021 · 022 · 023 —
**Phase 3** 030 · 031 — **Phase 4** 040 · 041 · 042 — **Phase 5** 050

**Blocked**: 074 ⛔ (`ComposeShadowPatchEngine` subsumption NOT-CONFIRMED — gate-decision §5).

## Evidence trail

`projects/spaarkeai-compose-r8/notes/` — `gate-contract.md` · `control-measurement.md` ·
`merge-prototype-results.md` · `gate-decision.md` · `merge-mechanism-results.md` · `edited-block-loss.md` ·
`merge-integrity-results.md` · `adr-049-third-amendment-draft.md` · `track-s-uat.md` ·
`honest-failure-set.md` · `document-size-ceilings.md`
