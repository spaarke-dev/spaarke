# Current Task State — spaarke-SPA-external-access-platform-r2

> **Last Updated**: 2026-08-11 (task 073 DEPLOY DONE; interactive UAT + SPA workflow-deploy owner-pending)
> **Recovery**: read Quick Recovery. 070+071+072 complete/deployed. 073 deploy done; UAT is owner-driven.

## Quick Recovery

| Field | Value |
|-------|-------|
| **Status** | **P2b wave code-complete + deployed.** Task 073: BFF redeployed to spaarke-bff-dev @ HEAD (070 grant-write + 072 entitlement + master merge; 48.48 MB, health/SHA verified); TrackingFieldTrio PCF v1.0.12 live; **me-client mock→real flip committed** (`bffApiCall('/api/v1/external/me/entitlements')` + graceful fallback) and external-spa production build verified. `/me/entitlements` 401 live; grant rows verified (1 matter, 4 project). |
| **NEXT ACTION** | **OWNER**: (1) trigger the SPA deploy — `gh workflow run deploy-external-spa.yml --ref work/spaarke-SPA-external-access-platform-r2` (no worktree path for the SWA; see escalation in notes/task-073-deploy-and-uat.md); (2) run the interactive **both-plane UAT** per that note's checklist (workforce admin grants Matter/WA via side-pane lookup → external partner sees them + roll-ups + tab sets + negatives; FR-08 add-a-row live; grantedby under SSO). If green → flip TASK-INDEX 073 → ✅ and merge the branch to master. |
| **Branch/sync** | `work/spaarke-SPA-external-access-platform-r2` — synced with master (0 behind at deploy time), pushed. |
| **Deploys live** | BFF spaarke-bff-dev @ HEAD (070+072+master); PCF TrackingFieldTrio v1.0.12 @ SPAARKE DEV 1. SPA = owner-triggered workflow (built + ready, not yet uploaded). |

## Why 073 isn't auto-✅
073's acceptance criteria are an **interactive both-plane UAT** (real workforce-SSO + CIAM logins + a
browser to click the Manage Access side-pane lookup, verify tabs/roll-ups/negatives). The agent cannot
authenticate as those personas or drive the UI, and the SWA deploy needs a token only the workflow holds.
All machine-deployable + machine-verifiable parts are done; the human UAT + SWA workflow-deploy remain.
Full record + checklist: `notes/task-073-deploy-and-uat.md`.

## P2b wave (070→073) — reference
- 070 ✅ polymorphic grant-WRITE + repaired grant path + sprk_organization (`notes/task-070-deviations.md`)
- 071 ✅ polymorphic grant UI + side-pane Advanced Lookup, PCF v1.0.12 (`notes/task-071-deviations.md`)
- 072 ✅ Tier-1 entitlement Option-B (resolver + /me/entitlements + tab sets) (`notes/task-072-deviations.md`)
- 073 🔄 deploy done; UAT owner-pending (`notes/task-073-deploy-and-uat.md`)

## Notes index
`notes/`: `task-073-deploy-and-uat.md`, `task-072-deviations.md`, `task-071-deviations.md`,
`task-070-deviations.md`, `polymorphic-grant-authoring-enhancement.md`, `module-entitlement-schema-decision.md`.
