# Current Task State — spaarke-ai-architecture-redesign-r1

> **Last Updated**: 2026-07-06 ~02:00 (P1 closed; W-P2-A dispatching — operator asleep, autonomous overnight run per operator directive)
> **Recovery**: Read "Quick Recovery" first. Full context: this file + tasks/TASK-INDEX.md + plan.md + notes/g-p1-uat-round2-findings.md.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | W-P2-A: 030 (agent-turn loop contract) + 031 (ONE Confirmation Gate) — parallel agents |
| **Step** | P1 COMPLETE (027 ✅ operator-directed close after round-2 fixes `9ee30e672` deployed). P2 running. |
| **Status** | autonomous overnight execution (operator directive 2026-07-06: "move on to P2 while i sleep") |
| **Next Action** | On W-P2-A agents' return: main session flips TASK-INDEX rows, build + suite triage vs KNOWN list, commit (`git pull --rebase` first), push, then dispatch W-P2-B (032, 033) → 034 (HARD CUTOVER, serial) → 035+036 → 037. STOP at gate 038 (operator browser UAT — NEVER auto-pass, NFR-11). |

### P1 close summary (2026-07-06)
- G-P1 ran TWO UAT rounds. Round-2 findings + fixes: `notes/g-p1-uat-round2-findings.md` — RD-1 chip strip stranded at top → SprkChat `aboveInputSlot` prop, chips now above input zone; RD-2 "Summarize again" frozen to original fileIds → transition chips carry NO args, dispatch-time FR-08 default-all; RD-3 latent composer-strip gating → session-level count (promotedChipIds ∪ composer-ready).
- Fix commit `9ee30e672`; BFF redeployed (healthz 200) + sprk_spaarkeai republished (4781 KB, id 5206a442-3451-f111-bec7-7ced8d1dc988).
- Legacy "Open Library" no-match reply on typed NL is EXPECTED until task 034 — verify gone at gate 038.
- 027 close is operator-directed conditional pass: chip-placement visual spot-check folds into gate 038.

### Session commits (all pushed; CI bot pushes format commits — ALWAYS `git pull --rebase` before push)
`c061c621b` init · `091ed29d6` budget · `1aa317b35` W-P0-A · `ef771def1` W-P0-B · `a93bd6dce` W-P0-C · `70d599348` gate 014 · `bdfcb06ba` W-P1-A · `a34bb877a` 021 · `6f7622f3c` W-P1-C · `d1d2a1c06` 023b · `9a4270a5b` 022b+026+ADR-039 · `befcaa5da` round-1 fixes · `efef0c398` handoff · `9ee30e672` round-2 fixes

### Critical Context
- **Progress**: 27/51 ✅ (P0 + P1 complete; Track-B 4/4). Portfolio Issue #550 (Epic #421); Tasks Completed sync to 27 (GraphQL: project PVT_kwHODW0Pv84BEgWu, item PVTI_lAHODW0Pv84BEgWuzgxza1E, field PVTF_lAHODW0Pv84BEgWuzhWPlLY). Draft PR #551.
- **ADR-039 + ADR-040 ACCEPTED** — binding for all remaining phases.
- **Known pre-existing test failures (NOT ours)**: ExecutorConfigSchemas, KnowledgeDeploymentConfig, DailyBriefingCollector resolver, TemplateContextBuilder TextOnly, SessionFilesCleanup (5) + AuditLogService & PlaybookDispatcherPhaseB latency flakes under parallel load (pass in isolation) + NetArchTest 5 (ADR-010 ceiling 129 vs 76).
- **Task 030 extra acceptance criterion**: orphan-handler health dimension Degraded→Unhealthy escalation (14 orphan handlers currently Degraded-by-design on /healthz/catalog).
- **F-1 ruling**: accept-until-cutover; P2 tasks 035/036 delete most legacy legs; task 044 deletes InvokePlaybookHandler/AnalysisQueryHandler/WorkingDocumentHandler AND must re-home the E-2 EngineOutputLedgerAdapter (040 re-points BindingId=playbookId first).
- **Deferrals on record**: ADR-040 size-cap → 047; ConversationPane batch-state extraction + concurrency-safe manifest append + Task.Delay→TimeProvider (readiness probe) → /defer or task 045; Seed-JpsActions.ps1 sweep needs main session (073); ConversationPane ≤300-line budget at 045 with escalation valve.
- **Operator TODO**: mark eval-gate CI job REQUIRED when branch protection returns.

## Wave protocol (unchanged — keep doing this)
1. Parallel task agents (max 6/wave) via Agent tool: POML path, task-execute protocol, rigor declaration, file-ownership boundaries, known-failures list, no-commit/no-TASK-INDEX/no-.claude boundaries.
2. Stagger intra-wave deps. 3. Wave end: main session flips TASK-INDEX, dotnet build + suite triage, commit/push, portfolio sync. 4. Verify-dead-first on inventory items. 5. `.claude/` edits main-session only. 6. `/goal` conditions in TASK-INDEX wave headers (operator-side pilot).

## P2 sequence (this overnight run)
- W-P2-A: 030 + 031 (parallel) → W-P2-B: 032 + 033 (parallel; 032 deps 030+031, 033 deps 030) → W-P2-C: 034 HARD CUTOVER (serial) → W-P2-D: 035 + 036 (dispatcher-stack + Chat/Tools deletions) → 037 (eval + injection hardening) → **gate 038 STOP** (operator browser UAT: four-outcome contract, session memory, confirmed writes, chip placement spot-check, legacy no-match reply gone).

## Decisions This Session (operator-ratified where noted)
1. Stop-after-init; Target 2026-08-15. 2. ConversationPane ≤300-line budget + valve. 3. F-1 accept-until-cutover. 4. /healthz split; dup-detection keys sprk_toolid; orphans Degraded until 030. 5. ComposeSummarize/ChatClassify constants. 6. OBO mcp.tools spike FAIL-with-path. 7. Upload UX: auto-classify + chip-offered summarize (catalog data). 8. **027 operator-directed close + autonomous P2 overnight (2026-07-06)**.

## Parallel Execution

W-P2-A dispatching: agents for tasks 030 + 031 (see TASK-INDEX wave header for the /goal condition — operator-side; agents follow task-execute).
