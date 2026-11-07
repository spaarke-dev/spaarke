# Current Task State - spaarkeai-mcp-bff-api-r1

> **Last Updated**: 2026-11-07 (option C approved; 3 background agents running)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Pipeline Step 5. Owner approved **option C** on 2026-11-07: defer `ICallerContext`, amend FR-10, rework task 010 into a verification task, cancel 012, then 017 → 013. |
| **Step** | Three background agents are running (see Active Lanes). The main session waits for them, then dispatches 017 and then 013. |
| **Status** | in-progress |
| **Next Action** | (1) When 017 returns, run the build check, then dispatch 013. (2) When lane C returns green, SendMessage uac-r2 (`spaarke-wt-unified-access-control-r2-20`) the new spike HEAD SHA, with the diff vs master = 16 spike files + the ArchTests files. (3) When lane B returns, relay the ISS-002 verdict and PR to the owner and coordinate the Compose owners' review (worktrees `spaarke-wt-070/071/072`, branches `work/compose-r8-*`). |

## Active Lanes (background agents)
- **A. rework-010-012** ✅ `f44fcd9696`. FR-10 is amended, 010 is now `tasks/010-mcp-caller-path-verification.poml` (deps 020/021/023/024, precedes 025), and 012 is cancelled. Lint shows 28/28 clean with no drift. plan.md and README were fixed in the main session.
- **B. fix-iss-2620-compose** (opus) is running in its own worktree `C:/code_files/spaarke-wt-iss2620`, branch `fix/iss-2620-compose-dispatcher-httpcontext`. It will produce a repro test, a fix if the defect is real, a sibling scan, a review and a DRAFT PR.
- **C. spike-mapmcp-guard** (opus) is running in `C:/code_files/spaarke-wt-spaarkeai-mcp-phase0-spike`. It adds a MapMcp scanner guard as one ArchTests-only commit, then runs PR #2615 CI.
- **Task 017** (B-2 result mapper) is dispatched via task-execute. Next: 013 (deps 011 and 017).

## State
- Wave 1:
  - 011 ✅ (`b9d8f19b47`).
  - 001 built: spike `5c9e301572`, notes `00c4ced7e3`. Spike HEAD `72e300c40b` = master `cc11294300` + the spike. Publish delta +0.706 MB; no vulnerable packages.
  - 010 is now the MCP caller-path verification (Phase B); 012 is cancelled (option C).
- Spike deploy: uac-r2 deployed master `cc11294300` ALONE because the spike failed the Router ArchTests (run 42344104365). We acked on 2026-11-07. Terms: every uac-r2 dev deploy = master + spike until we declare Phase 0 done; PR #2615 is never merged.
- ISS-002 → #2620 (labels `issue,now`), recorded in `notes/defer-issues.md` (`8c9dab7df6`).
- BFF split reassessment: no split now (`58ac89ed65`).

## Open Owner Questions / Blockers
- Task 002 needs the owner as Global Admin (connector app registration).
- Phase 0 live tests (003) wait until the spike is deployed.
- Owner to drop the leftover `stash@{0}` "lint-staged automatic backup".
- Billing review in Copilot (next slice) vs EB-D-194: does not block v1.

## Critical Context
- **STANDING (ontology condition):**
  - No dev BFF deploy from this branch until ontology's #2580/#2579 pair has deployed.
  - Phase 0 live tests run from the spike branch only.
  - Every deploy needs the owner's per-SHA go in uac-r2's session.
  - Our master PR merges after their pair.
- D-207 order and the replay guard are unchanged. The MCP commit goes only through `DecisionCommitService`.
- Standing directives: project `CLAUDE.md` §3. Decisions: `notes/decisions.md`. Peers: `spaarke-wt-spaarke-ontology-platform-r1-38`, `spaarke-wt-ebilling-r1-c7`, `spaarke-wt-spaarke-legal-ai-accelerator-80`, `spaarke-wt-unified-access-control-r2-20`.
