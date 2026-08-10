# Current Task State — spaarke-SPA-external-access-platform-r2

> **Last Updated**: 2026-08-10 (context-handoff)
> **Recovery**: read Quick Recovery, then "NEXT: execute task 028". Branch = master (fully merged).

## Quick Recovery

| Field | Value |
|-------|-------|
| **Status** | P1 complete (010–019 ✅) + merged to master. Grid-widget bug **fixed + deployed + UAT-verified both planes**. Polymorphic Tier-2 access model **designed + spec'd + tasked as 028** (not started). |
| **NEXT ACTION** | **Execute task 028 via `task-execute`** — `tasks/028-polymorphic-tier2-scoping.poml`. It generalizes Tier-2 scoping to polymorphic multi-root (Project/Matter/Work Assignment) + adds an internal-only Service Requests tab, and **supersedes** the partial documents-by-project fix. Binding spec: `notes/external-access-polymorphic-scoping-design.md`. |
| **Branch/master** | `work/spaarke-SPA-external-access-platform-r2` == `origin/master` == `d211b6705` (grid fix + merge). Planning artifacts for 028 committed on top (see below). |
| **Pre-conditions** | Deploy from worktree (NOT CI) — memory `deploy-from-worktree-not-ci.md`. Build client with `VITE_DEV_MOCK=false`. `/conflict-check` before the BFF PR. |

## ✅ Done since last handoff (2026-08-07 → 08-10)
- **Grid-widget "empty grids" bug** — diagnosed empirically (App Insights + live Dataverse), root-caused as THREE bugs, fixed:
  - Projects/Matters 500s → wrong FetchXML attribute names in `sprk_gridconfiguration` (fixed live: `sprk_name`→`sprk_projectname`, `sprk_referencenumber`→`sprk_projectnumber`, `sprk_status`→`statuscode`; `sprk_mattertitle`→`sprk_mattername`).
  - Documents/Invoices 0 rows → BFF fetched one unfiltered page then filtered in-memory. Fixed via **server-side FetchXML scope injection** (`Tier2ScopeFilterInjector`, commit `bff7e82e5`, deployed) + Documents pageSize→250.
  - Diagnosis: `notes/grid-widget-empty-diagnosis.md`.
- **UAT verified both planes** (2026-08-07): workforce = 16 projects / 49 docs / 5 WA / 0 matters; partner = 2 projects / 14 docs / 4 WA. Invoices deferred (matter-parented).
- **Merged to master** (safe: conflict-checked, FF, main repo synced) — `d211b6705`.

## 🔜 NEXT: Task 028 — polymorphic Tier-2 scoping (spec'd, not started)
**Why**: UAT + owner clarification proved the shipped single-parent scoping (015/016 + `bff7e82e5`) is incomplete — documents/invoices are polymorphic (matter OR project), Work Assignment is a first-class ROOT (grantable standalone with its own docs), Service Requests are internal-only.

**The model** (frozen, verified live — `notes/external-access-polymorphic-scoping-design.md`):
- Roots per caller: **P** projects, **M** matters, **W** work assignments (+ **S** = SRs I submitted, internal only).
- Partner = **grant-only** (`sprk_externalrecordaccess` by `sprk_recordtype`: Project/Matter/WorkAssignment). Internal = membership/assignment ∪ own-contact grants.
- Children scope by OR across parents: Documents [`sprk_project`|`sprk_matter`|`sprk_workassignment`], Invoices [`sprk_matter`|`sprk_project`].
- Service Requests tab: **internal-only**, `sprk_requestedby == caller contact`.
- **No Dataverse schema change** (grant table targets all 3 roots; `sprk_document.sprk_workassignment` + `sprk_servicerequest.sprk_requestedby` verified).

**Build (11 steps in the POML)**: extend `ExternalParticipationService` (all grant types) → extend `AccessibleRecordSetService` (matter/WA grant term) → carry root sets on `CallerPrincipal` → generalize `ExternalModuleDescriptor` to N scope dimensions → generalize `Tier2ScopeFilterInjector` to OR filter → rewire `ExternalAccessModule` registrations → update grid configs (+ new SR config) → add internal-only SR widget → tests → redeploy from worktree → verify both planes via App Insights.

**Files**: `Infrastructure/ExternalAccess/{ExternalParticipationService,AccessibleRecordSetService,ExternalModuleRegistry,CallerPrincipalResolver}.cs`, `Api/ExternalAccess/{ExternalModuleDataEndpoints,Tier2ScopeFilterInjector}.cs`, `Infrastructure/DI/ExternalAccessModule.cs`, `src/client/external-spa/src/widgets/` + `registry/widgetRegistry.ts`, tests in `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/`.

## Deployed state (dev) — unchanged from 08-07 + the 028-precursor injector
- **SWA** `swa-spaarke-external-spa-dev` = R2 client live. **BFF** `spaarke-bff-dev` = R2 + `bff7e82e5` (single-attr server-side scope injection). **Entra `1e40baad`** R2-owned + Teams SSO fix. Grant/test identities per `notes/grid-widget-empty-diagnosis.md`.

## Remaining (beyond 028)
- Admin: re-upload Teams package (domain-qualified `webApplicationInfo.resource`) for the desktop SSO fix to go live.
- ISS-018-1: 2 pre-existing `DataverseEntitySchemaTests` fails — `/defer` to Documents owner.
- P2 021/024 (entitlement resolver / workforce auth policy) — 028 builds the Tier-2 substrate they assume.
- P3 030–037: Service Request **creation** wizard + law-dept management (028 adds only the SR read tab).

## Notes index
`notes/`: `external-access-polymorphic-scoping-design.md` (028 binding spec), `grid-widget-empty-diagnosis.md` (+UAT verification), `task-018-deviations.md`, `task-019-deployment-record.md`, `access-model-systemuser-contact-grant-union.md`, `teams-sso-fix-and-entra-app-ownership.md`.
