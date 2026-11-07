# Current Task State - spaarkeai-mcp-bff-api-r1

> **Last Updated**: 2026-11-07 (014 + 016 done and merged; 015 running; 002 with owner)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Pipeline Step 5 (option C). Done: 011, 017, 013, 014, 016 (016 merged at `40d924098c`; merged-tree suites green). **015 running** (sonnet, main worktree). **002** in the main session, waiting on the owner. |
| **Step** | 015 is running. 020+ are gated on 003 (T0/T13), which waits on 002 (connector). |
| **Status** | in-progress |
| **Next Action** | (1) When 015 returns: build, run the Signals unit + integration suites and the baselines, then send it to ontology for review. (2) 002: when the owner pastes the client id, verify with `az ad app show`, then guide runbook (b). The spike is LIVE on dev, so (c) the connector can follow (b) immediately. (3) Waiting on ontology: review of 014 and 016, and their answer on ISS-004 (#2628), commit without a prior plan read. (4) Owner: who reviews PR #2622. (5) Size-test widgets (1/3/6 MB) for 003 are not built yet; they go to uac-r2 as a new spike SHA when needed. |

## Active Lanes
- **015** (FR-04 `PrivilegeFlagged`, FR-05/11 paging): background implementer.
- **002**: owner-driven. Runbook: `notes/drafts/connector-registration-runbook.md`.
- **Spike on dev:** `22487c6537` is live (uac-r2 gates PASS, 20:06Z). The next window carries `6fbb3a7642` (= master `23567b2f93` + spike, CI green); deploying it waits on the owner's go in uac-r2's session. Phase 0 isn't done yet, so every deploy carries the spike.
- **ISS-002 / #2620**: fix is in draft PR #2622, waiting for the owner to name a reviewer. **ISS-003 / #2623**: filed, not ours. **ISS-004 / #2628**: question sent to ontology.

## State
- Wave 1:
  - 011 ✅ (`b9d8f19b47`).
  - 001 built: spike `5c9e301572`. Spike HEAD `22487c6537` = master `d53d67f604` + spike + guard commit `fdfd7b1d23`. uac-r2 approved it; the 021 condition (no Permanent waivers carried over) is in the POML.
  - 010 is now the MCP caller-path verification (Phase B); 012 is cancelled (option C).
- Spike deploy terms: every uac-r2 dev deploy = master + spike until we declare Phase 0 done; PR #2615 is never merged.
- ISS-002 → #2620 (labels `issue,now`), recorded in `notes/defer-issues.md` (`8c9dab7df6`).
- BFF split reassessment: no split now (`58ac89ed65`).

## Open Owner Questions / Blockers
- Task 002 needs the owner as Global Admin (connector app registration).
- Phase 0 live tests (003) wait until the spike is deployed.
- Billing review in Copilot (next slice) vs EB-D-194: does not block v1.

## Critical Context
- **STANDING (ontology condition):**
  - No dev BFF deploy from this branch until ontology's #2580/#2579 pair has deployed.
  - Phase 0 live tests run from the spike branch only.
  - Every deploy needs the owner's per-SHA go in uac-r2's session.
  - Our master PR merges after their pair.
- D-207 order and the replay guard are unchanged. The MCP commit goes only through `DecisionCommitService`.
- Standing directives: project `CLAUDE.md` §3. Decisions: `notes/decisions.md`. Peers: `spaarke-wt-spaarke-ontology-platform-r1-38`, `spaarke-wt-ebilling-r1-c7`, `spaarke-wt-spaarke-legal-ai-accelerator-80`, `spaarke-wt-unified-access-control-r2-20`.
