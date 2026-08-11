# Current Task State — spaarke-SPA-external-access-platform-r2

> **Last Updated**: 2026-08-11 (task 071 COMPLETE — deployed to SPAARKE DEV 1; task 072 is next)
> **Recovery**: read Quick Recovery. 070 + 071 are done/deployed/verified. 072 not started.

## Quick Recovery

| Field | Value |
|-------|-------|
| **Status** | Task **071 COMPLETE** — polymorphic grant UI: TrackingFieldTrio host derives the bound entity, grant-read filter `_sprk_{root}_value` (fixes Matter/WA "Failed to load access data"), AccessGrantModal sends `{recordType,recordId}` + adopts the shared side-pane Advanced Lookup (`INavigationService.openLookup`) for the contact picker + an optional `sprk_organization` picker. **PCF v1.0.12 built + imported to SPAARKE DEV 1 + published.** 25 modal tests pass; live matter-scoped grant read verified. |
| **NEXT ACTION** | **Task 072** — Tier-1 entitlement Option-B wiring. Say "work on task 072". Amends 021 (resolver reads `sprk_approlemodulemap` App-Role→module; blanket-entitle CIAM plane) + 022 (`/me`) + `external-spa` widgetRegistry tab config per the owner tab lists (internal defaults = Service Requests + Policy Library; CIAM defaults = Work Assignments/Projects/Matters/Invoices/Documents; drop `requiredEntitlement:'assigned-work'` on CIAM widgets). See `notes/polymorphic-grant-authoring-enhancement.md` §020/§072 + `notes/module-entitlement-schema-decision.md`. `sprk_approlemodulemap` already created+seeded by owner. |
| **Branch/sync** | `work/spaarke-SPA-external-access-platform-r2` — commit 071 work + push. ~behind/ahead master (master moving; teams SSO fix already merged-in). Re-sync from master before the 073 deploy wave. |
| **BFF deploy** | Live on spaarke-bff-dev with ALL grant fixes + org bind (task 070). |
| **PCF deploy** | TrackingFieldTrio **v1.0.12** live on SPAARKE DEV 1 (task 071). external-spa SPA NOT deployed yet (task 073). |

## ✅ Task 071 — done (reference)
Full detail: `notes/task-071-deviations.md`. Key: host-entity-derived `recordType` (drops `sprk_project`
hardcode); `_sprk_{root}_value` polymorphic read filter; shared `AccessGrantModal` gains injected
`pickContact`/`pickOrganization` callbacks (side-pane Advanced Lookup, no new abstraction §11) + stays
Xrm-free; `{recordType,recordId}` + `organizationId` grant bodies; revoke root-agnostic. Deviation D-1:
inline Combobox retained as the non-injected fallback (production PCF path uses openLookup). Step-6
side-pane-over-modal spike = owner-pending browser check in the 073 UAT (sound by construction).

## ✅ Task 070 — done (reference)
`notes/task-070-deviations.md`. Repaired the fully-broken grant path + polymorphic write + `sprk_organization`.

## Remaining P2b tasks
- **072** Tier-1 Option-B entitlement wiring (resolver + /me + widgetRegistry tabs). NEXT.
- **073** Deploy + both-plane UAT (BFF + PCF + entitlement live; owner runs the browser UAT incl. the
  071 person-icon→side-pane→grant click-path on Matter/WA, and confirms grantedby under SSO).

## Notes index
`notes/`: `task-071-deviations.md`, `task-070-deviations.md`, `polymorphic-grant-authoring-enhancement.md`,
`task-028-deviations.md`, `external-access-polymorphic-scoping-design.md`, `module-entitlement-schema-decision.md`.
