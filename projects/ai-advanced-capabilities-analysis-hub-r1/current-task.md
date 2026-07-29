# Current Task

> Active-task tracker. History lives in `tasks/TASK-INDEX.md` + per-task `.poml`.

**Status**: CODE COMPLETE (24/28) — awaiting go-ahead on the live deploy phase (W7)
**Active task**: none (paused at deploy checkpoint)
**Next action**: HUMAN CHECKPOINT before W7 (070 BFF deploy → 071 client/code-page deploy + ribbon XML + grid-config seed + Dataverse env WR deletion → 072 e2e → 090 wrap). These are outward-facing/irreversible (Azure deploy + Dataverse artifact deletion); 063 already flagged env deletes need per-call human approval. Recommended: review/merge PR #694 first, then deploy from master. Done: Phases 0–6 (001, 010–013, 020–025, 030/031, 040/041, 050–053, 051, 060–064) ✅. Publish 47.51 MB.

> **064 scope**: drop `sprk_chathistory` READ from GET/save (FR-22); assess /export reader (062 note: DeserializeChatHistory feeds save/export/GET) — repoint to Cosmos or drop; then complete 062 hand-off (write cleanup: UpdateChatHistoryAsync impl, AnalysisResultPersistence if dead, 3-file column plumbing) — escalation-guarded if it balloons. Generalize NDA hooks→work-type; shared widgets survive.
> **071 (deploy) owes 3 items**: (1) hub `sprk_gridconfiguration` seed (notes/hub-grid-config-deployment.md); (2) ribbon XML for AnalysisRecordLaunch (Matter/Project buttons); (3) Dataverse env web-resource/canvasapp deletion w/ exact GUIDs (notes/task-063-env-deletion-deferred-to-071.md).
**Next action**: W6 060 → 061 → 062 → 063; 064 after 062 (repoint BEFORE delete, §13.5) → W7 (070–072, 090). Done: 001 ✅ 010 ✅ 011–013 ✅ 020–025 ✅ 030 ✅ 031 ✅ 040 ✅ 041 ✅ 050 ✅ 051–053 ✅ (19/28). Phases 1–5 COMPLETE. Publish 46.16 MB.

> **W6 is the ordered/prescriptive retirement (§13.5)** — MUST run serially: repoint server deep-links (060) → repoint client launch points (061) → retire legacy session path (062, escalation-guarded on sprk_chathistory provenance) → delete web resources + tree (063); 064 (migrate save/get + generalize hooks + fold 3-pane) after 062. Nothing dangles.
> **071 (deploy) owes**: hub sprk_gridconfiguration seed (notes/hub-grid-config-deployment.md) + ribbon XML wiring for AnalysisRecordLaunch (Matter/Project buttons).

> **050 (entry matrix) is the integration keystone — 3 threads converge there:**
>  1. Wizard deep-service wiring (`dataService`/`authenticatedFetch`/`navigationService`) — deferred from 030 (interim "Connecting to workspace services…").
>  2. Wizard-finish → three-pane **execution launch** — deferred from 040 (currently status-flip + file-load only).
>  3. Connect Agreement-Review launch → Compose with `activeWorkType='agreement-analysis'` — plumbing READY from 041; no live dispatch site yet.
> **071 (deploy) owes**: seed the hub `sprk_gridconfiguration` row + 4 saved queries (recipe: notes/hub-grid-config-deployment.md).
> **launch-resolver.ts** now touched by 041; 050 + 052 also touch it (serial in W5, no conflict).

> W3 tail (024, 025) stays SERIAL — both touch the SpaarkeAi conversation/session client surface (ConversationPane hotspot); real fan-out resumes at W4.

## Pipeline progress (2026-07-28)

- [x] Step 0.3 pre-flight (branch/tree/build ✅; worktree fast-forwarded to origin/master `8f4a7b4ab`)
- [x] Step 0.5 master staleness (logged; other worktrees' unmerged work is expected)
- [x] Step 1 spec validated (22 FRs / 7 NFRs; ADR Tensions present)
- [x] Step 1.7 ADR tensions accepted (ADR-040 → Path A; ADR-013/§10 → deferred to UQ-1)
- [x] Step 2 resource discovery (5 agents) + hot-path warning + UQ-1 recommendation (Option B)
- [x] Step 2 artifacts: README.md, PLAN.md, CLAUDE.md, current-task.md
- [x] UQ-1 confirmed by owner → Option B (new BFF `POST /api/ai/analysis/fork`); ADR-013/§10 Path A exception recorded
- [x] Step 3 task decomposition — 28 POMLs + TASK-INDEX; `Validate-TaskPoml.ps1` PASS (0 errors)
- [ ] Step 4 commit project artifacts
- [x] Step 5 task execution — started: task 001 (green-baseline gate) ✅ complete 2026-07-28

## Session Notes — Key Learnings (task 001)

- All 12 pre-existing e2e failures were test-infra drift (stale assertions / missing test-harness provider),
  NOT product regressions — no escalation fired. See `notes/green-baseline.md` for full root-cause detail
  and before/after evidence. Full SpaarkeAi Jest portfolio (76 suites / 673 tests) green post-fix.

## Next action

Task 010 (schema preflight) — MINIMAL rigor, read-only Dataverse verification gate (W1). Blocks all
Phase-1/Phase-2 data + session code (011, 012, 013, 020…). `projects/.../notes/schema-prerequisites.md`
already has an owner-verified-present note from 2026-07-28 (Dataverse MCP `describe` output) that task 010
should confirm/consume.

## Open decisions

- **Archive durability** (AIPL-054 stub): decided at task 022 execution (Cosmos-authoritative vs summary-GUID caching).
- **Archive durability** (AIPL-054 stub): accept Cosmos/Redis-authoritative archive OR implement summary-GUID caching — scoped as task 2.3.
