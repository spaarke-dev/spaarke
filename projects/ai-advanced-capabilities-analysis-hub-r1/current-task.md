# Current Task

> Active-task tracker. History lives in `tasks/TASK-INDEX.md` + per-task `.poml`.

**Status**: in-progress
**Active task**: W4c — 031 (reopen from grid → rehydrate session + review + files), serial
**Next action**: 031 → W5 (050 → 052 → 053 entry) → W6 (retirement) → W7 (deploy/test/wrap). Done: 001 ✅ 010 ✅ 011–013 ✅ 020–025 ✅ 030 ✅ 040 ✅ 041 ✅ 051 ✅. Publish 46.16 MB.

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
