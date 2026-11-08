# Current Task State - spaarkeai-mcp-bff-api-r1

> **Last Updated**: 2026-11-07 (Phase A done except FR-04 privilege source; owner inputs pending)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Pipeline Step 5 (option C). Done: 011, 017, 013, 014, 016, 015 (with review fixes `3eccab0c81` and `42a3a35efa`). The Phase A BFF rework is complete except for the **privilege derivation (FR-04)**, which waits on the owner's ruling. **002** is in the main session, waiting on the owner. |
| **Step** | Waiting on the owner. 020+ are gated on 003 (T0/T13), which waits on 002 (connector). |
| **Status** | blocked on owner inputs |
| **Next Action** | (1) **Owner ruling on FR-04 privilege source.** The recommendation is "A widened" (ontology agrees): at read time, as the caller, batched per page, read the subject communication's `sprk_privilegeclassification` plus its witness communications. Privileged or Potentially Privileged = flagged; a read fault or an unreadable record = flagged; None or null = not flagged. Once ruled, create a follow-up task (for example 018) that runs before 022, amend spec FR-04, and update 022. (2) 002: when the owner pastes the client id, verify it with `az ad app show`, then guide runbook (b). The spike is live on dev. (3) Ontology is reviewing 015. (4) The owner names a reviewer for PR #2622. (5) The owner gives the go for spike `6fbb3a7642` in uac-r2's session. (6) The owner deletes `C:\wt015z` (2 leftover zips; the safety check blocked the delete). |

## Active Lanes
- **002**: owner-driven. Runbook: `notes/drafts/connector-registration-runbook.md`.
- **Spike on dev:** `22487c6537` is live. `6fbb3a7642` (= master `23567b2f93` + spike, CI green) is queued for the next window. Phase 0 is not done.
- **Issues:** ISS-002 #2620 (fix PR #2622, needs a reviewer); ISS-003 #2623 (not ours); ISS-004 #2628 (closed: intended, per ontology).

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
