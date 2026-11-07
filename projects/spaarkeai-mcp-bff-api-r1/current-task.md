# Current Task State - spaarkeai-mcp-bff-api-r1

> **Last Updated**: 2026-11-07 (013 done; 014 + 016 running; 002 with owner)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Pipeline Step 5 (option C). Done: 011, 017, 013 (ontology OK on 017 and 013). **Running now:** 014 in the main worktree and 016 in `C:/wt016` (branch `task/spaarkeai-mcp-016`). **002** is in the main session, waiting on the owner. |
| **Step** | 014 and 016 are running in parallel as background implementers (opus). |
| **Status** | in-progress |
| **Next Action** | (1) When 016 returns: merge `task/spaarkeai-mcp-016` into the work branch (after 014 has pushed, or carefully alongside it), build, run the Signals unit and integration suites plus the baselines, push, then remove `C:/wt016` with `scripts/worktree/Remove-WorktreeSafe.ps1`. (2) When 014 returns: send 014 and 016 to ontology for review. Next wave: 015 and 020 (020 needs Phase 0 T0: check its gate). (3) 002: when the owner pastes the client id, verify with `az ad app show`, then guide runbook (b). (4) Check Tier 2 on PR #2587 (the 013 full-suite failures were likely caused by machine load). |

## Active Lanes
- **014** (commit capability) runs in the main worktree. Binding requirements: the D-17 `DecisionToolError.Partial` payload (ontology), and an empty `DecisionCommitService.cs` diff.
- **016** (lookup search + REST route) runs in `C:/wt016`. It pushes its own branch; the main session merges it.
- **002**: owner-driven. Runbook: `notes/drafts/connector-registration-runbook.md`.
- **Owner decisions pending:** who reviews PR #2622 (the ISS-002 Compose fix); the go for spike `22487c6537` in uac-r2's session (uac-r2 has approved it).
- **ISS-003 / #2623** is filed and not ours.

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
