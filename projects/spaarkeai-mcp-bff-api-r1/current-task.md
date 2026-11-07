# Current Task State - spaarkeai-mcp-bff-api-r1

> **Last Updated**: 2026-11-07 (017 done; 013 running; 002 with owner)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Pipeline Step 5 (option C). 017 ✅ (`21882337f7`). **013 running** (background implementer, opus). **002 in progress in the main session**, owner-driven. |
| **Step** | 002 step 3: the owner is creating Entra app "Spaarke Copilot Connector – dev" (runbook (a)). Next is runbook (b), the Teams Developer Portal OAuth registration. (c) the connector and (d) T5 wait until the spike is live on dev. |
| **Status** | in-progress |
| **Next Action** | (1) When the owner pastes the client ID, verify read-only with `az ad app show --id <id>`: single tenant, `isFallbackPublicClient` true, delegated `user_impersonation` on `1e40baad…`, admin consent granted, client pre-authorized on `SDAP-BFF-SPE-API`, and no credentials. Then guide (b). If the Dev Portal demands a secret, STOP and ask the owner. (2) When 013 returns: build check, then dispatch 014 (and 016, which runs alongside it). (3) Spike `22487c6537` is approved by uac-r2; deploying it waits on the owner's go in uac-r2's session. |

## Active Lanes
- **013** (B-1 list + plan capability): background implementer.
- **002**: main session with the owner. Runbook draft: `notes/drafts/connector-registration-runbook.md`. Facts go to `notes/phase0-registration-facts.md` (not yet created).
- **ISS-002 / #2620**: CONFIRMED. Fix is in draft PR #2622 (`30b887c710`, worktree `C:/code_files/spaarke-wt-iss2620`). There is no live Compose session, so the owner decides who reviews it.
- **ISS-003 / #2623**: possible disposed-scope bug in `PrecedentAdminEndpoints.cs:294`. Found in passing; not ours.
- **017** needs ontology review (`21882337f7`). Ask ontology (`spaarke-wt-spaarke-ontology-platform-r1-38`).

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
