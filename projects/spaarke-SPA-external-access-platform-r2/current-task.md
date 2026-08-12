# Current Task State — spaarke-SPA-external-access-platform-r2

> **Last Updated**: 2026-08-11 (task 073 live UAT — iterating on TrackingFieldTrio PCF / Manage Access modal)
> **Recovery**: read Quick Recovery, then `notes/task-073-uat-punchlist.md` (the ORDERED open-items record).

## Quick Recovery

| Field | Value |
|-------|-------|
| **Status** | P2b access-write wave (070/071/072) deployed; **task 073 live UAT in progress**. Live UAT surfaced many latent bugs in the grant UI (never opened live in teams-app-r1). PCF **TrackingFieldTrio v1.0.16** on SPAARKE DEV 1; BFF @ HEAD on spaarke-bff-dev; external-spa SWA deployed with the me-client mock→real flip. |
| **NEXT ACTION** | **ALL 7 punch items SHIPPED.** #1–#6 (v1.0.17/v1.0.18). **#7 (org grant = everyone in the firm) DEPLOYED & LIVE:** BFF @ spaarke-bff-dev (write + Term-3 union via `sprk_contactorganization` + revoke; 48.48 MB, no size regression; 39/39 external-access tests pass) + PCF **v1.0.19** on SPAARKE DEV 1 (modal "Add organization" writes an org grant; Current Access shows "Everyone at {firm}", revocable). Owner prereqs A+B confirmed. **REMAINING = OWNER LIVE UAT of #7** — verify a firm member sees the record, a non-member / former-member does NOT, and revoking removes the whole firm. Optional follow-ups: formal code-review before master merge, org-scoped cache fan-out, pre-existing grant/read CacheVersion mismatch. Design + build map: `notes/task-073-org-grant-design.md`. |
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
