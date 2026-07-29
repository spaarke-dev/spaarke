# Current Task State — Spaarke Compose R5

> **Last Updated**: 2026-07-29 (pipeline complete — spec + plan + 22 tasks generated; execution HELD on Phase-0 human gates)
> **Recovery**: Read "Quick Recovery" first. Branch `work/spaarkeai-compose-r5`.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project** | spaarkeai-compose-r5 (Editing Completeness — additive on R4's Shadow Document Architecture) |
| **Progress** | Phase 0 ✅; **Phase 1: 010 ✅ (alignment), 011 ✅ (heading/list). NOW: 012 (G12 accept/reject single-by-id).** |
| **Status** | 🚀 **EXECUTING task 012** — FULL rigor, opus/xhigh. Add acceptRevision/rejectRevision ops (revisionId-addressed, Single scope, compose-ops-v2 bump); engine resolves by revision id (accept-ins strips w:ins, accept-del removes run, reject inverse); fix importedRevisions end-of-para re-anchor; ET-2 seam on CIPO (accept-then-save no 422). |
| **Active task** | 012 (G12 accept/reject single-by-id). Then 013 (batch) → 014 (tables). |
| **Next Action** | After 012: build-verify + commit + mark ✅ → dispatch 013 (accept-all/reject-all batch, dep 012) → 014 (G4 tables, dep 004). Then Phase 2 (020→022, 021), Phase 3 (030/031/032/033), Phase 4 (040→041) → STOP before 042 deploy. |
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
