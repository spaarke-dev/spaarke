# Current Task

> Active-task tracker. History lives in `tasks/TASK-INDEX.md` + per-task `.poml`.

**Status**: in-progress
**Active task**: W4a — 040 (wizard) ∥ 051 (Matter/Project subgrid), parallel
**Next action**: W4a (040∥051) → W4b (030 hub ∥ 041 activeWorkType) → W4c (031 reopen). Session phase DONE. Done: 001 ✅ 010 ✅ 011–013 ✅ 020–025 ✅. Publish 46.16 MB.

> W4 sequencing: wizard (040) built BEFORE hub (030) so the hub's "Create new" cards wire to the real wizard (avoids a hub↔wizard integration race + register-workspace-widgets.ts contention). 051 (Dataverse forms) is fully independent → parallel with 040.

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
