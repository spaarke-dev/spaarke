# Current Task State - spaarkeai-mcp-bff-api-r1

> **Last Updated**: 2026-11-08 (018 done; Phase A complete; 002 waits on owner)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Pipeline Step 5 (option C). Done: 011, 017, 013, 014, 016, 015, 018 (privilege derivation, owner option A, `6aec16b753`). The Phase A BFF rework is complete. Blocked on **002**: the connector app registration does not exist yet. |
| **Step** | Waiting on the owner for 002 step (a). The owner asked "what application id do you need?" and was told the app must still be created: either they click through the six Entra steps, or they say "yes, run it" and the main session creates it with the az CLI as the owner (single-tenant public client, no secret; `user_impersonation` on BFF `1e40baad…` plus admin consent; pre-authorize on the BFF app). |
| **Status** | blocked on owner |
| **Next Action** | (1) 002 (a) DONE 2026-11-08: the connector app is `04f37fe4-5714-46f9-8796-5363a7a52244`, created via az on the owner's "yes, run it", with admin consent and BFF pre-auth; facts in `notes/phase0-registration-facts.md`. Next, guide (b), the Teams Developer Portal OAuth registration; STOP if it demands a secret. Then (c), the connector "Spaarke" pointing at `https://spaarke-bff-dev.azurewebsites.net/mcp/legal-operations` (the spike `6fbb3a7642` is live). Then 003 live tests; tell uac-r2 before the test window. (2) Ontology APPROVED 018 (2026-11-08, their D-212); notify them when 018 reaches master (rides PR #2587) — their DI-143 #2630 builds on WorkItemPrivilege. (3) The owner reviews PR #2622 (Compose fix, marked ready 2026-11-08). |

## Active Lanes
- None running. Runbook: `notes/drafts/connector-registration-runbook.md`.
- **Spike on dev:** `6fbb3a7642` (= master `23567b2f93` + spike), live since 2026-11-08 04:00Z. Phase 0 is not done, so every uac-r2 deploy carries the spike.
- **Issues:** #2620 (PR #2622, ready for the owner's review); #2623 (not ours); #2628 (closed).

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
- Standing directives: project `CLAUDE.md` §3. Decisions: `notes/decisions.md`. Peers: `spaarke-wt-spaarke-ontology-platform-r1-38`, `spaarke-wt-ebilling-r1-c7`, `spaarke-wt-spaarke-legal-ai-accelerator-80`, `uac-r2` (Claude Code desktop session since 2026-11-08; owner approves held messages there).
