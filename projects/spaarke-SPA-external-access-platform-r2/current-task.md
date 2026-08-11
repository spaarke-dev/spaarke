# Current Task State — spaarke-SPA-external-access-platform-r2

> **Last Updated**: 2026-08-11 (task 070 COMPLETE + org-scoping done; task 071 STARTING)
> **Recovery**: read Quick Recovery. 070 is done/deployed/verified. 071 is set up (conflict-check clean) but
> its heavy implementation should run in a fresh context — resume with "work on task 071".

## Quick Recovery

| Field | Value |
|-------|-------|
| **Status** | Task **070 COMPLETE** (deployed + live-verified: polymorphic grant-write + repaired the fully-broken grant path + `sprk_organization` scoping). **Task 071 NOT STARTED** — rigor declared (FULL, sonnet@high, directional), `/conflict-check` CLEAN (no PR touches TrackingFieldTrio / AccessGrantModal / Spaarke.UI.Components). |
| **NEXT ACTION** | **Resume task 071 in a fresh context** — say "work on task 071". It's a 1–2 day PCF+shared-lib task: (1) parameterize `TrackingFieldTrio/index.ts` by host entity (drop `GRANT_RECORD_ENTITY='sprk_project'`; per-entity role-fields; grant-read filter `_sprk_{root}_value`; `retrieveRecord` host entity) — fixes the Matter/WA "Failed to load access data"; (2) `AccessGrantModal`: 3 `projectId` body keys → `{recordType,recordId}` (task-070 DTO), section-2 Combobox → side-pane Advanced Lookup via injected `INavigationService.openLookup` (reuse `createXrmNavigationService`, NO new abstraction); **also add an `sprk_organization` picker → send `organizationId`** (070 wired the backend); (3) Access-Permission gate reads `sprk_accesspermission` on host entity; (4) side-pane-over-modal spike (step 6); (5) tests + PCF version bump (4 places) + `npm run build:prod` + solution import; (6) Step 9.5 gates. |
| **Branch/sync** | `work/spaarke-SPA-external-access-platform-r2` — pushed, 0 unpushed. ~18 behind / 19 ahead of master (master moving; teams-app-r1 SSO SPA fix already merged-in earlier). Update-from-master again at 071 start is fine (071 touches PCF/shared-lib; check for overlap). |
| **BFF deploy** | Live on spaarke-bff-dev with ALL grant fixes + org bind. SPA (external-spa) NOT deployed yet (task 073). |

## ✅ Task 070 — done (reference)
Grant path was fully broken (never live in teams-app-r1); fixed + live-verified: PascalCase `@odata.bind`
nav names (`sprk_Contact/sprk_Project/sprk_Matter/sprk_WorkAssignment/sprk_GrantedBy`), grantedby
oid→systemuserid (omit-if-unresolved), `sprk_expiresdate`, and grantee firm/org → **`sprk_Organization`**
lookup (created on the grant table + DTO `AccountId→OrganizationId` + bound + live-verified). Polymorphic
root {recordType,recordId} + fail-closed + close-project `_sprk_project_value` fix. External-access suite
227 pass. Full detail: `notes/task-070-deviations.md`.

## 071 context already loaded (reuse, don't re-derive)
- Inventory of the 3 change-places + side-pane helper: `notes/polymorphic-grant-authoring-enhancement.md`.
- Side-pane Advanced Lookup = `INavigationService.openLookup` (`Spaarke.UI.Components/src/types/serviceInterfaces.ts:363`) → `createXrmNavigationService` (`utils/adapters/xrmNavigationServiceAdapter.ts:92`, cleanGuid). Adopt as-is (§11).
- Grant DTO now: `{contactId, recordType, recordId, accessLevel, organizationId?, expiryDate?}`; legacy `projectId` still works.

## Follow-ups
1. **Org picker in modal** — surface it in 071 (backend ready: send `organizationId` = a `sprk_organization` id).
2. **grantedby under SSO** — owner confirms during 073 UAT (non-blocking).

## Notes index
`notes/`: `task-070-deviations.md`, `polymorphic-grant-authoring-enhancement.md`, `task-028-deviations.md`, `external-access-polymorphic-scoping-design.md`, `module-entitlement-schema-decision.md`, `grid-widget-empty-diagnosis.md`.
