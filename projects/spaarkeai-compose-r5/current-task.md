# Current Task State — Spaarke Compose R5

> **Last Updated**: 2026-07-30 (by context-handoff — Phase 5 UAT COMPLETE + synced to master; NEXT = fix 4 master-inherited test failures)
> **Recovery**: Read "Quick Recovery" first. Branch `work/spaarkeai-compose-r5`. Working tree CLEAN, all pushed (tip `f99f790d7` = merge of origin/master).
>
> **▶ NEXT ACTION (post-compaction):** 🔧 **FIX 4 MASTER-INHERITED TEST FAILURES**, then prong 1 → operator re-deploy → 090. All R5 UAT work (050–055) is DONE, green on R5's own surface, committed + pushed. The branch is synced to master (merge `f99f790d7`), which brought in 4 FAILING tests from OTHER projects (analysis-hub/ai-redesign + messaging-r3/email) — NOT R5 code, but the owner wants them GREEN before merge-to-master/deploy. Fix these next:
>
> **FAILURE GROUP A — `AnalysisEndpointsExecuteDispatchContractTests` (3 fails)** — `tests/**/Api/Ai/…` (analysis-hub-r1 / ai-redesign):
>   - `Post_NoDocumentProfileBindingRow_FallsThroughToEnginePath`, `Post_PlaybookIdMatchesDocumentProfileBinding_DispatchesLinearBranch`, `Post_PlaybookIdDoesNotMatchBinding_FallsThroughToEnginePath`.
>   - Error: `System.InvalidOperationException: Failure to infer one or more parameters … sessionManager | UNKNOWN`. The analysis EXECUTE/dispatch endpoint handler has a `sessionManager` parameter ASP.NET Minimal API cannot bind (not a registered service, not a body param). **Root cause = asymmetric DI registration** (endpoint mapped but `sessionManager`'s service not registered / not in the test fixture) — the RB-T028 / BFF §10 "endpoints mapped unconditionally must have unconditional service registration" class. Fix: register the missing service (or correct the handler signature). Grep the AnalysisEndpoints Execute/dispatch map + its `sessionManager` type; check `Program.cs`/the relevant `*Module.cs` DI.
>
> **FAILURE GROUP B — `CommunicationWorkspaceReadSeamTests.QueryCommunicationsAsync_AllFiveFacetsComposedTogether_AndsThreadChannelDateRangeAndParticipant` (1 fail)** — `tests/integration/seam/Communication/…` (messaging-r3 / email-r5):
>   - Error: `Expected …Matches(messageQuery, " and ").Count to be 4 … but found 5`. The composed OData filter now has an EXTRA ` and ` (5 vs expected 4). A merge interaction (email-r5 + messaging-r3 both touched the Communication query builder) added a facet/AND. Fix: inspect the query composition vs the test — either the query has a spurious extra AND (fix the builder) OR a legitimately-added 6th facet means the test's expected count is stale (update to 5). Determine which by reading the query builder + the 5-facet test setup.
>
> **After the 4 fixes are green** (re-run full BFF suite → expect 9648-ish / 0 failed / 101 skipped):
> - (1) **Prong 1 (055 fast-follow, DEFERRED):** keep-edits recovery — on an anchor-refusal batch, best-effort apply resolvable ops + surface unresolvable (reuse `ReanchorStaleSaveAsync` AUTO/REVIEW/ORPHAN model). Safety net; prong 2 already fixed the root cause.
> - (2) **Operator re-deploys BFF + `sprk_spaarkeai` client together** → re-UAT (esp. the 422 flow on a FRESH upload + Open-Document modal).
> - (3) **090 wrap-up** (/test-diet + /code-review + /adr-check + /merge-to-master).
>
> **Do NOT deploy autonomously.** Out-of-scope UAT items (#2,3,4,7,8) captured in `notes/uat-remediation-r5.md` (other projects; no issues filed per owner).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project** | spaarkeai-compose-r5 (Editing Completeness — additive on R4's Shadow Document Architecture) |
| **Progress** | Phases 0–4 ✅ (010–041); 042 deploy ✅ (operator). **Phase 5 UAT remediation ✅** — 050 #1A redline routing, 051 #1B numbering (opt A), 052 #10/#11 Word-lock UX, 053 #5 external-change, 054 #9 profile feedback, 055 422 anchor paraOffset + Open-Document modal + diagnostic. All committed + pushed; synced to master (`f99f790d7`). **NEXT: fix 4 master-inherited test fails (Groups A+B above) → prong 1 → re-deploy → 090.** |
| **Branch state** | tip `f99f790d7` (merge of origin/master), pushed, working tree CLEAN, behind master 0. |
| **Test state** | R5's own surface GREEN (Compose 827/0, byte-diff 24/24). **4 known fails, ALL inherited from the master merge, NOT R5**: 3× `AnalysisEndpointsExecuteDispatchContractTests` (DI `sessionManager` param-inference) + 1× `CommunicationWorkspaceReadSeamTests…FacetsComposedTogether` (extra ` and ` in composed filter). Fix next (details in the ▶ NEXT ACTION block). |
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
