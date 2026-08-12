# Current Task State — spaarke-SPA-external-access-platform-r2

> **Last Updated**: 2026-08-11 (task 073 live UAT — iterating on TrackingFieldTrio PCF / Manage Access modal)
> **Recovery**: read Quick Recovery, then `notes/task-073-uat-punchlist.md` (the ORDERED open-items record).

## Quick Recovery

| Field | Value |
|-------|-------|
| **Status** | P2b access-write wave (070/071/072) deployed; **task 073 live UAT in progress**. Live UAT surfaced many latent bugs in the grant UI (never opened live in teams-app-r1). PCF **TrackingFieldTrio v1.0.16** on SPAARKE DEV 1; BFF @ HEAD on spaarke-bff-dev; external-spa SWA deployed with the me-client mock→real flip. |
| **NEXT ACTION** | Punch **#1–#6 all shipped** (v1.0.17/v1.0.18 on SPAARKE DEV 1). **#7 (org grant = everyone in the firm) — BFF CORE BUILT + committed (57e71d2bf), compiles.** Owner chose the junction model + created `sprk_contactorganization`. **BLOCKED on 2 OWNER PREREQUISITES before deploy/test:** (A) set `sprk_Contact` to **Optional** on `sprk_externalrecordaccess`; (B) confirm the junction's lookup logical names are `sprk_contact` / `sprk_organization`. REMAINING WORK after prereqs: BFF tests + adr-check/code-review/publish-size gates, PCF modal wiring (Add-organization writes org grant + Current Access shows org rows), then deploy BFF (worktree) + PCF and test together. Full design + build map: `notes/task-073-org-grant-design.md`. |
| **Punch list** | `notes/task-073-uat-punchlist.md` — 7 open items IN ORDER: (1) email crash, (2) standing-grant contacts in Current Access, (3) reusable 32px PCF title/header, (4) lookup-in-front-of-modal decision, (5) thin scrollbar standard, (6) Save/Cancel semantics, (7) +Organization scope confirm. Plus KEY FACTS (env-var auth values, `_X_value` read pattern, Xrm-lookup-vs-CodePage, org/standing/access-level semantics) + PCF version history (v1.0.13→v1.0.16) + rebuild recipe. |
| **Branch/sync** | `work/spaarke-SPA-external-access-platform-r2` — pushed (last commit ca7160ded), synced with master (0 behind). |

## ✅ 070 / 071 / 072 — done (reference)
`notes/task-070-deviations.md` (polymorphic grant-write + repaired path + sprk_organization),
`notes/task-071-deviations.md` (polymorphic grant UI + side-pane lookup), `notes/task-072-deviations.md`
(Tier-1 entitlement Option-B: resolver + /me/entitlements + tab sets). 073 deploy record: `notes/task-073-deploy-and-uat.md`.

## Task 073 — deploy done; interactive UAT iterating
BFF + PCF + SWA deployed. The remaining work is the **live-UAT punch list** (grant modal + trio PCF polish)
in `notes/task-073-uat-punchlist.md`, worked one item per round with the owner eyeballing each (owner
can't be automated-verified; agent can't see the rendered UI). Do NOT mark 073 ✅ until the owner signs off
the both-plane UAT + the punch list is cleared.

## Notes index
`notes/`: `task-073-uat-punchlist.md` (⭐ active), `task-073-deploy-and-uat.md`, `task-072-deviations.md`,
`task-071-deviations.md`, `task-070-deviations.md`, `polymorphic-grant-authoring-enhancement.md`,
`module-entitlement-schema-decision.md`.
