# Current Task State — spaarke-SPA-external-access-platform-r2

> **Last Updated**: 2026-08-11 (task 070 COMPLETE — deployed + live-verified)
> **Recovery**: read Quick Recovery. Task 070 done; P2b continues with 071.

## Quick Recovery

| Field | Value |
|-------|-------|
| **Status** | Task **070 COMPLETE** — polymorphic external grant-WRITE (Project/Matter/WorkAssignment) deployed to spaarke-bff-dev + **live-verified** (matter/WA/project grants each bind the correct single typed lookup; no-root → 400). While verifying, found + fixed that the ENTIRE grant path was broken (never live in teams-app-r1): PascalCase `@odata.bind` nav names, grantedby oid→systemuserid, `sprk_expiresdate`, removed broken account bind. External-access suite 225 pass; publish 48.45 MB; gates PASS. |
| **NEXT ACTION** | **P2b continues** — task **071** (PCF/modal polymorphism + side-pane Advanced Lookup) → 072 (Tier-1 Option-B entitlement + widgetRegistry tab sets) → 073 (wave deploy + both-plane UAT). Say "work on task 071". |
| **Branch/sync** | `work/spaarke-SPA-external-access-platform-r2` — **0 behind / 16 ahead** of master. Worktree updated from master; **teams-app-r1 SSO-fallback SPA fix (`d636b6872`, App.tsx + AuthGuard.tsx) is now merged-in** and ready for the eventual external-spa deploy. Not yet merged branch→master. |
| **Deploy state** | BFF live on spaarke-bff-dev with all grant fixes. SPA (external-spa) NOT yet deployed — task 073 (needs 071/072); now includes the teams auth fix. |

## ✅ Task 070 — what shipped + verified
- **Polymorphic grant-write** + fail-closed root resolution + close-project `_sprk_project_value` fix.
- **Grant path fully repaired** (4 latent bugs, all live-verified fixed): nav names PascalCase, grantedby
  systemuserid resolution, expiry field, account-bind removed.
- Live UAT green (matter/WA/project 200 + correct binding; no-root 400). Test rows cleaned up.
- Docs: `notes/task-070-deviations.md` (full root-cause + UAT + /defer items).

## /defer items (owner intent needed — pending filing)
1. **Org-scoping**: add `sprk_organization` lookup to `sprk_externalrecordaccess`; DTO `AccountId → OrganizationId`; bind it (currently accepted-but-not-persisted; OOB `account` was wrong per owner).
2. **grantedby under SSO**: confirm systemuser resolution populates `sprk_grantedby` with a real workforce token (073 UAT).

## Remaining (project)
- **P2b**: 071 → 072 → 073 (073 also live-tests revoke + close-project + deploys the SPA incl. teams fix).
- **028 both-plane read UAT** (deployed, owner-pending).
- **Master integration** (branch→master) deferred; 16 ahead / 0 behind.

## Notes index
`notes/`: `task-070-deviations.md` (this task, full UAT), `polymorphic-grant-authoring-enhancement.md` (P2b design), `task-028-deviations.md`, `external-access-polymorphic-scoping-design.md`, `module-entitlement-schema-decision.md`, `grid-widget-empty-diagnosis.md`.
