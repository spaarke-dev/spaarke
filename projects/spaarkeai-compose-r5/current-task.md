# Current Task State — Spaarke Compose R5

> **Last Updated**: 2026-07-29 (pipeline complete — spec + plan + 22 tasks generated; execution HELD on Phase-0 human gates)
> **Recovery**: Read "Quick Recovery" first. Branch `work/spaarkeai-compose-r5`.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project** | spaarkeai-compose-r5 (Editing Completeness — additive on R4's Shadow Document Architecture) |
| **Progress** | Phase 0 done; task 010 (alignment applier) shipped. **NOW: task 011 (G3 heading/list applier).** |
| **Status** | 🚀 **EXECUTING task 011** — FULL rigor, opus. Tracked w:pPrChange for Style/ListOrdered/ListLevel; client classifyStep emits heading/list; re-enable heading/list toolbar controls; reuse NumberingComputationEngine (reference-in-place, no fork). |
| **Active task** | 011 (G3 heading/list applier). |
| **Next Action** | Impl: (1) engine Style applier (pStyle in pPrChange), (2) engine List applier (numPr in pPrChange, reuse NumberingModel/engine), (3) client classifyStep emit Style/ListOrdered/ListLevel, (4) re-enable heading/list toolbar (keep table gated for 014), (5) seam slices, (6) build+test+byte-diff+publish, (7) code-review+adr-check, (8) /conflict-check, commit. |
| **011 design notes** | Mirror task 010's ApplySetBlockAttrAlignment for Style (snapshot prior pStyle → pPrChange). List: reference existing suitable numId from NumberingModel; author minimal ordered/bullet abstractNum+instance ONLY if none (NFR-08 no-error). w:pPrChange nested prev-props = `ParagraphPropertiesExtended` (010 finding). Tracked path only; structure for 021 clean branch (like 010). |

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
