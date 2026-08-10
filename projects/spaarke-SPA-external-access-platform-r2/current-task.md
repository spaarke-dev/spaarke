# Current Task State — spaarke-SPA-external-access-platform-r2

> **Last Updated**: 2026-08-10 (task 028 complete)
> **Recovery**: read Quick Recovery. Task 028 shipped + deployed; live both-plane UAT is owner-pending.

## Quick Recovery

| Field | Value |
|-------|-------|
| **Status** | Task **028 COMPLETE** — polymorphic Tier-2 scoping (Project/Matter/WorkAssignment roots + internal-only Service Requests) built, unit/seam/contract-tested (10259 pass), grid configs updated, BFF **deployed to spaarke-bff-dev** (health+SHA verified), client built. Supersedes `bff7e82e5`; amends 015/016. |
| **NEXT ACTION** | **Owner: live both-plane UAT** — log in as workforce (ralph@spaarke.com) + CIAM partner (ralph@hotmail) and confirm: Documents shows all accessible-root docs (project OR matter OR WA) in one tab; Matters/Invoices/Work Assignments populated; **Service Requests tab present for internal, ABSENT for partner**. Then choose the next P2 wave (020/021/024) — owner-gated. |
| **Task** | none (028 done; awaiting owner UAT + next-wave decision) |
| **Branch** | `work/spaarke-SPA-external-access-platform-r2` — synced to `origin/master`; 028 commits on top (not yet merged to master). |
| **Pre-conditions (next BFF task)** | Deploy from worktree (NOT CI) — memory `deploy-from-worktree-not-ci.md`. `/conflict-check` before any BFF PR. |

## ✅ Task 028 — what shipped
- **BFF**: `GetGrantSetAsync` reads all typed grant lookups (project/matter/WA), cache v2; `AccessibleRecordSetService` grant term spans matter/WA; `CallerPrincipal` + both plane strategies carry accessible matter/WA sets (CIAM grant-only; workforce membership ∪ grants); `ExternalModuleDescriptor` → N OR'd `ScopeDimensions` (single-attr shorthand retained); `Tier2ScopeFilterInjector` emits `<filter type="or">`; module registrations rewired (documents [project|matter|WA], invoices [matter|project], matters by M, work-assignments by W own-id, + internal-only service-requests).
- **Grid configs (live Dataverse)**: Documents +sprk_matter+sprk_workassignment; Invoices +sprk_matter; Matters real empty-state; **new Service Requests config `403e5d37-cb94-f111-b8db-00224835447a`** (added to the BFF grid-config allow-list).
- **Client**: `ServiceRequestsWidget` (internal-only, `planes:['workforce']`) + registry entry.
- **Verification**: 10259 unit tests pass; publish 48.44 MB (+0.15, ≤60); no CVE; code-review + adr-check clean.
- **Deployed**: spaarke-bff-dev (from worktree). Docs: `notes/task-028-deviations.md`, `notes/external-access-polymorphic-scoping-design.md`.

## Escalation trigger — did NOT fire
All access sources verified live via MCP before coding: grant table has sprk_project/sprk_matter/sprk_workassignment typed lookups; sprk_document/sprk_invoice have the needed parent lookups; sprk_servicerequest.sprk_requestedby exists. No Dataverse schema change.

## Remaining (project)
- **Owner**: live both-plane UAT of 028 (above); re-upload Teams package (domain-qualified `webApplicationInfo.resource`); merge 028 branch → master when ready.
- **P2**: 020 (entitlement schema) · 021 (entitlement resolver) · 022/023/026 (Group C) · 024/025 (workforce auth) · 027 (deploy P2).
- **P3+**: 030–037 (Service Request **creation** wizard + law-dept mgmt — 028 added only the SR read tab), NDA/Policy, spikes 033/050.
- **ISS-018-1**: 2 pre-existing `DataverseEntitySchemaTests` fails — `/defer` to Documents owner.

## Notes index
`notes/`: `task-028-deviations.md` (this task), `external-access-polymorphic-scoping-design.md` (028 binding spec), `grid-widget-empty-diagnosis.md` (+UAT), `task-018-deviations.md`, `task-019-deployment-record.md`, `access-model-systemuser-contact-grant-union.md`, `teams-sso-fix-and-entra-app-ownership.md`.
