# Current Task State — spaarke-SPA-external-access-platform-r2

> **Last Updated**: 2026-08-11 (task 072 COMPLETE — BFF entitlement deployed; task 073 is next & final P2b)
> **Recovery**: read Quick Recovery. 070 + 071 + 072 are done/deployed/verified. 073 not started.

## Quick Recovery

| Field | Value |
|-------|-------|
| **Status** | Task **072 COMPLETE** — Tier-1 module entitlement (owner Option B). NEW `ModuleEntitlementResolver` (workforce App-Role `roles` ∩ `sprk_approlemodulemap`; CIAM blanket `['assigned-work']`) + `GET /api/v1/external/me/entitlements` (`{displayName,email,plane,entitlements[]}`) **deployed to spaarke-bff-dev** (401 live, health OK). external-spa widgetRegistry set to owner tab sets (internal defaults = Service Requests + Policy Library + QS pinned; CIAM blanket, dropped `requiredEntitlement`). 12 resolver tests + 239 external-access tests pass. **Discovery: 021/022 were never built — 072 is the primary impl** (marked SUPERSEDED-BY-072 in TASK-INDEX). |
| **NEXT ACTION** | **Task 073** (final P2b) — Deploy + both-plane UAT. Say "work on task 073". It: (1) **flips `me-client.ts` mock→real** `bffApiCall('/api/v1/external/me/entitlements')` (Deviation D-1, deferred from 072); (2) deploys the external-spa SPA (SWA — NOT yet deployed; includes teams-app-r1 SSO fix + 028 serviceRequests + 072 tab sets); (3) owner runs the dual-plane UAT: workforce FrontDoorUser → entitlements `['legal-front-door','policy-library']` + correct tabs; CIAM → `['assigned-work']` + 5 outside-counsel tabs; grant a Matter/WA to an external contact (071 UI) → visible in SPA with children rolling up (028 read); FR-08 add-a-map-row live; grantedby under SSO (070 follow-up). Reconcile from master before deploying. |
| **Branch/sync** | `work/spaarke-SPA-external-access-platform-r2` — commit 072 + push. ~behind master; re-sync from master before the 073 SPA/BFF deploy wave. |
| **BFF deploy** | Live on spaarke-bff-dev: ALL grant fixes + org bind (070) + `/me/entitlements` (072). |
| **PCF deploy** | TrackingFieldTrio **v1.0.12** live on SPAARKE DEV 1 (071). |
| **SPA deploy** | external-spa NOT deployed yet (task 073). |

## ✅ Task 072 — done (reference)
`notes/task-072-deviations.md`. Option B: internal = App-Role→`sprk_approlemodulemap`→codes (no Contact),
external = blanket outside-counsel. New resolver + `/me/entitlements` endpoint (ADR-009 map cache, ADR-010
concrete, app-only). widgetRegistry owner tab sets. **D-1: client mock→real flip deferred to 073** (needs
live dual-plane tokens); mock is value-aligned with the server so the flip is one line.

## ✅ 070 + 071 — done (reference)
`notes/task-070-deviations.md` (polymorphic grant-write + repaired path + `sprk_organization`);
`notes/task-071-deviations.md` (polymorphic grant UI + side-pane Advanced Lookup, PCF v1.0.12).

## Notes index
`notes/`: `task-072-deviations.md`, `task-071-deviations.md`, `task-070-deviations.md`,
`polymorphic-grant-authoring-enhancement.md`, `module-entitlement-schema-decision.md`,
`external-access-polymorphic-scoping-design.md`, `task-028-deviations.md`.
