# Current Task State — Spaarke Compose R5

> **Last Updated**: 2026-07-30 (by context-handoff — ALL impl+hardening done; AT deploy gate; pre-compaction)
> **Recovery**: Read "Quick Recovery" first. Branch `work/spaarkeai-compose-r5`. Working tree CLEAN, all pushed (tip `23b676a57`).
>
> **▶ NEXT ACTION (post-compaction):** 🔧 **PHASE 5 UAT REMEDIATION IN PROGRESS.** Task 042 deployed (operator); UAT surfaced 11 findings → 5 R5-owned (Phase 5 wave 050–054, see `notes/uat-remediation-r5.md`). Status:
> - **050 ✅** UAT #1A redline routing + origin fix (SEV-1) — committed `e5c82afe6`. Compose suite 822/822, byte-diff green, publish 46.84 MB.
> - **053 ✅** UAT #5 external-change on visibilitychange + Reload-from-source button — committed `1abd5785f`. Toolbar 46/46.
> - **054 ✅** UAT #9 profile-button (gate already correct; added spinner feedback) — committed `bf3104e86`. Toolbar 47/47.
> - **051 ⛔🔔 ESCALATED** UAT #1B persist computed numbering — feasibility investigation contradicts the premise; escalation trigger fired. **Awaiting owner decision** (options A/B/C in `notes/task-051-deviations.md`; recommend A = reduced scope, 050 already preserves numbering byte-identical).
> - **052 ✅** UAT #10/#11 honest Word co-authoring lock UX — committed `2ed296f0c`. Confirmed (Microsoft docs) NO Graph unlock for co-auth locks → honest 423 copy + Retry + Reload-from-Word (no fake Unlock). DEF-14 regression updated; banner 7/7; Compose 822/822.
> - **Directive #2 ✅** checkout-retirement plan drafted (`notes/checkout-retirement-plan.md`) — cross-cutting (10 files incl. non-Compose FileAccess/DocumentOperations); promote to its own project.
>
> **Re-verify (2026-07-30):** Compose suite **822/822**; full BFF **9544 passed / 3 failed / 101 skipped**. The **3 failures are PRE-EXISTING from the master merge** (`AnalysisEndpointsExecuteDispatchContractTests` — analysis-hub/ai-redesign/email dispatch; R5 hardening baseline was 9319/0/101 before the merges; R5 diff is 100% Compose). **NOT R5** — flag to those project owners (same family as out-of-scope UAT #7/#8). Publish ~46.84 MB (052 server delta = one message string). BFF build 0 errors.
>
> **NEXT:** (1) owner picks **051** option A/B/C (`notes/task-051-deviations.md`; recommend A). (2) Operator **re-deploys BFF + `sprk_spaarkeai` client together** → re-UAT the 4 fixes. (3) Then 090 wrap-up (/test-diet + review + merge-to-master) — the 3 pre-existing analysis failures should be resolved by their owners or accepted before the master-merge gate.
> **Out-of-scope UAT items (#2,3,4,7,8)** captured in `notes/uat-remediation-r5.md` (other projects; no issues filed per owner). **Do NOT deploy autonomously.**

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project** | spaarkeai-compose-r5 (Editing Completeness — additive on R4's Shadow Document Architecture) |
| **Progress** | Phase 0 ✅ · Phase 1 ✅ (010–014) · Phase 2 ✅ (020 G1, 021 G2, 022 G7) · Phase 3 ✅ (030 G8, 031 G9, 032 G11, 033 G5) · Phase 4: 040 ✅ (G10), 041 ✅ (hardening PASS). **NEXT: 042 deploy (operator gate) → 090 wrap-up.** |
| **Branch state** | tip `23b676a57`, pushed to `origin/work/spaarkeai-compose-r5`, working tree CLEAN. NOT merged to master (090 does that). |
| **Full-suite state** | byte-diff **24/24** · Compose C# **821/821** · full BFF **9319 passed / 0 failed / 101 skipped** · client suites green · ArchTests **3 pre-existing only** (ADR-007, ADR-010×2; Tier-1 no-AI passes) · publish **48.13 MB compressed incl PDBs** (≤60) · zero new runtime package. |
| **This-session tasks (022→041 + comm fix)** | 022 G7 dedup+split-button (`410b08669`) · 030 G8 banner+webhook seam (`9a9346dd9`) · 031 G9 scroll-sync (`26990ba10`) · 032 G11 redline-visibility lock (`3e5746690`) · 033 G5 hyperlinks both paths (`485ca406f`) · 040 G10 profile re-run (`d5bad935c`) · 041 hardening PASS (`6d2c514ac`) · checkpoint (`c5349d5cd`) · Communication stale-test fix (`23b676a57`). Each has a `notes/task-0NN-deviations.md`. |
| **Next Action** | **STOP — await operator go on 042 (deploy).** Then `task-execute 042`, then `task-execute 090` (/test-diet + review + merge-to-master). |
| **Deploy-together reminder** | G5 (hyperlink) + G7 (Link mark / transient key) extended the closed op catalog ADDITIVELY on `compose-ops-v2` (NO version bump — safe because client+server share the constant + deploy together). At deploy: ship BFF + `sprk_spaarkeai` client together (rationale: `notes/task-033-deviations.md`). |
| **Operator did (Dataverse gate, cleared)** | `sprk_composetransientkey` (Single-line text 100, Optional) + alt-key `sprk_composetransientkey_uk` on `sprk_document` — created + Active (G7/022 depends on it). |

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
