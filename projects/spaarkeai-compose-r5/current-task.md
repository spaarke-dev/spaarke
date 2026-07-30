# Current Task State — Spaarke Compose R5

> **Last Updated**: 2026-07-30 (by context-handoff — task 022 BLOCKED on human Dataverse gate; pre-compaction)
> **Recovery**: Read "Quick Recovery" first. Branch `work/spaarkeai-compose-r5`. Working tree CLEAN, all pushed (tip `92b99e0d4`).
>
> **▶ NEXT ACTION (autonomous run 2026-07-30):** Phase 2 COMPLETE — **020 ✅, 021 ✅, 022 ✅** (G7 transient-key dedup + Save split-button; commit `410b08669`; 810/810 Compose, byte-diff 24/24, publish 48.13 MB). **NOW running Phase 3 autonomously: 030 (G8) → 031 (G9) → 032 (G11) → 033 (G5) → 040 (G10) → 041 (hardening).** STOP before **042 (deploy — HOLDS for operator)**. Then 090 wrap-up (/test-diet). Each task via task-execute FULL rigor + Step 9.5 gates.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project** | spaarkeai-compose-r5 (Editing Completeness — additive on R4's Shadow Document Architecture) |
| **Progress** | Phase 0 ✅; Phase 1 ✅ (010–014); **Phase 2: 020 ✅ (G1 origin), 021 ✅ (G2 clean-apply, Candidate A).** NEXT: 022 (G7 save split). |
| **Status** | ✅ **task 014 DONE** — `table` op (6 kinds) added to closed catalog (server+client mirror, compose-ops-v2); engine applier emits FULL tracked table structure (w:trPr/w:ins+del rows, w:tcPr/w:cellIns+cellDel cols, w:tblGridChange, w:tblPrChange, in-cell w:del/w:ins), anchored by paraId ancestry walk (no text-search). Client classifyTableStep captures row/col add/delete + delete-table. Toolbar: row/col/delete-table EDIT commands round-trip on loaded docs; insert-NEW-table stays gated (out-of-catalog, surfaced §3). Seam slice `ComposeTableApplierSeamTests` (9 tests, OpenXmlValidator Word-valid). Build serial GREEN. Committed. See notes/task-014-deviations.md. |
| **Active task** | **022 (G7) — ⛔ BLOCKED on human Dataverse gate (Option B chosen 2026-07-30).** Operator adding `sprk_composetransientkey` (text 100, Optional) + alt-key `sprk_composetransientkey_uk` to the `sprk_document` table. FULL implementation plan + schema spec in `notes/g7-transient-key-schema.md`. **When field is Active → implement per that note** (client transient-key mint+send + Save split-button; server transient-key dedup in PromoteIfEphemeral + Save-New fork; seam incl. 8-duplicate; verify + gates). |
| **SCOPE DECISION (surface to operator)** | Table op faithful to task-004 catalog = 6 kinds for STRUCTURAL EDITS OF EXISTING tables. Whole-table CREATE (Insert-table on a loaded doc) is NOT a task-004 op kind (it is a whole-block author, not a structural edit) → Insert-table stays gated on loaded docs (honest disabled+tooltip, NOT silent loss). Delete-table = tracked deletion of all rows (in scope). Every currently-ENABLED loaded-doc table command round-trips or cleanly refuses (NFR-08 satisfied). |
| **Next Action** | Build catalog+engine+client+toolbar+seam; build serial; /conflict-check; commit --no-verify. Then Phase 2/3/4 → STOP before 042 deploy. |
| **Applier findings (carry forward)** | (010) w:pPrChange nested prev-props = `ParagraphPropertiesExtended`, and w:*Change types are schema-context-dependent. (011) reading the SDK numbering/styles DOM on the EDITABLE package re-serializes those parts → use a throwaway read-only probe for model reads (byte-surgical). (011) extended 022's buildAnchor/deriveOperation so new setBlockAttr ops aren't dropped by the save log. |

## Phase-0 decisions locked (implementers MUST follow)
- **002 — origin field** ✅: `sprk_composeorigin` Authored=100000000, Imported=100000001, default Imported, null→Imported (notes/g1-origin-field-asbuilt.md).
- **003 — G2 (R5-D2)** ✅: **Candidate A — engine clean-apply branch** (add `trackChanges`-off mode to ComposeShadowPatchEngine; ApplyInsertText→plain w:r, WrapRunAsDeleted→physical remove, ReplaceRange→remove+plain insert; reuse Resolve/Flatten/Split spine; keep I-7 no-text-search + Atom/TrackedChange refusals; do NOT merge byte-authors; do NOT delete docxBridge.ts). Contract: notes/g2-clean-apply-decision.md → task 021.
- **004 — op-schema** ✅: table via cell-paraId ancestry walk (no text-search); accept/reject by revisionId, Single/All scope, doc-preorder batch; bump `compose-ops-v2` at impl. notes/op-schema-extension-design.md → tasks 012/013/014/033.
- **005 — numbering** ✅: reference-in-place (nested internal already visible to engine; zero projection-byte risk). Compute contract → task 011.
- **001 — baseline** ✅ GREEN: 739/739 unit + 208/208 seam + 24/24 byte-diff (8-doc corpus); publish 46.70 MB excl PDBs.
- Deploy (042) HOLDS for human deploy-timing coordination (shared sprk_spaarkeai + spaarke-bff-dev).

## What exists
- ✅ `design.md`, `README.md` (gap ledger), `notes/COORDINATION-with-r4.5.md` (from setup).
- ✅ `spec.md` — 11 FRs + 9 NFRs; owner decisions captured (G1 Dataverse field, G4 full tracked tables, G12 single+batch); ADR-049/010 + code-grounded corrections.
- ✅ `plan.md` — 5-phase WBS + verified touchpoints + critical path.
- ✅ `tasks/` — 22 POMLs (001–006, 010–014, 020–022, 030–033, 040–042, 090) + `TASK-INDEX.md`. All XML-valid, canonical metadata, seam-DoD + no-text-search constraints, escalation triggers.
- ✅ `CLAUDE.md` (project context — carries binding rules + coordination).
- ✅ Portfolio Project #695 (Epic #421); `projects/INDEX.md` row (BFF=Y/SpaarkeAi=Y).

## Coordination facts the next session must NOT re-derive (full detail in CLAUDE.md §Coordination + TASK-INDEX)
- **R4.5 MERGED to master** — rebase 020/022/040 onto post-R4.5 `ComposeService.cs`; 020/022/030 onto post-R4.5 `ComposeWorkspace.tsx`. **NEVER delete `docxBridge.ts`.**
- **Reuse, don't fork:** 011 → `NumberingComputationEngine` (nested in `ComposeDocxProjectionBuilder.cs:1357`; see task 005 extract-vs-reference decision); 040 → `CitationResolver.cs`; 022 → R4.5 transient-mount identity.
- **NFR-09 analysis-hub-r1:** downstream consumer of Compose save/versioning/redline — 020/021/022/040 + G12 must not regress its reopen-restore / retirement parity. Shared `Spaarke.Compose.Components` + `ConversationPane`.
- **`/conflict-check` before EVERY BFF PR** (overlaps compose-r1/r2/r3 + ai-architecture-redesign-r2).
- **Discovery corrections (2026-07-28):** op catalog at `Services/Compose/Operations/`; G8 endpoints already registered (gap = delivery leg + client banner); G10 save-hook already exists (gap = reload + button); guard sites in `ComposeFormatToolbar.tsx`.

## Health
- Branch off master-with-R4.5 (merged origin/master 2026-07-29 → picked up ADR-049). BFF build green (pre-flight). BFF=Y, SpaarkeAi=Y. Publish baseline ~46.11 MB (≤60 ceiling). Zero new runtime package expected.

## How to resume
"where was I?" → this file + `TASK-INDEX.md`. To start: clear gates 002/003, then invoke `task-execute` for task 001.
