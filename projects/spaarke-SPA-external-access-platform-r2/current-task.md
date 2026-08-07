# Current Task State — spaarke-SPA-external-access-platform-r2

> **Last Updated**: 2026-08-07 (context-handoff)
> **Recovery**: read Quick Recovery, then "Deployed state" + "NEXT: R2 grid-widget bug". Branch = master (fully merged).

## Quick Recovery

| Field | Value |
|-------|-------|
| **Status** | P1 complete (010–019 ✅) + merged to master; Teams SSO fix applied. **Grid-widget bug DIAGNOSED + FIXED (2026-08-07, commit `bff7e82e5`, BFF redeployed).** Awaiting user retest. |
| **NEXT ACTION** | **User retest** the deployed grids (Projects should show 16; Documents should show project-linked docs; Matters clean "coming soon"; Work Assignments already worked). Then verify via App Insights `[EXT-MODULE] Fetch … server-side filtered`. **Invoices deferred** to matter-access work (invoices link to `sprk_matter`, not `sprk_project`). Full diagnosis: `notes/grid-widget-empty-diagnosis.md`. |
| **Branch/master** | `work/spaarke-SPA-external-access-platform-r2` == `origin/master` == `e9da94467` (0 behind/ahead; main repo synced). Clean tree. |
| **Pre-conditions** | Deploy from worktree (NOT CI) — see memory `deploy-from-worktree-not-ci.md`. Build client with `VITE_DEV_MOCK=false` + full CI VITE_* env (`.env.local` has mock=true; must override). |

## 🐞 NEXT: R2 grid-widget bug (the one open item)
**Symptom**: In the deployed R2 shell, every `<DataGrid>` widget (Projects/Matters/Documents/Invoices/Work Assignments) shows empty on BOTH the CIAM (Partner) and workforce (My organization) logins. **Not** an auth/BFF problem — proven:
- BFF `/api/v1/external/me` (workforce token) returns **16 projects**; the **old SPA** shows the CIAM contact's **2 projects** (Project 1 + PRJT.10007.02). So resolution + participations + Part-2 union all work server-side.
- **All grids empty at once ⇒ a COMMON cause in the shared R2 grid path**, not a per-widget issue.
**Investigate (client-first)**:
1. Browser Network tab on a grid open: are the `GET {host}/api/v1/external/api/dataverse/*` calls firing? Status? Response body? (Look for the `sprk_gridconfiguration` config fetch FIRST — D-016-2: each grid fetches its config record before data; if that 403s/errors, NO grid renders.)
2. Files: `src/client/external-spa/src/widgets/GridWidgetBody.tsx`, `ProjectsWidget.tsx` (+ siblings), `services/gridDataverseClient.ts` (check `bffBaseUrl` = `{host}/api/v1/external`), and the shared `<DataGrid configId>` from `@spaarke/ui-components`.
3. BFF side: `Api/ExternalAccess/ExternalModuleDataEndpoints.cs` + `Infrastructure/ExternalAccess/ExternalModuleRegistry.cs` — the module-data read seam + the grid-configuration allow-list (`OutsideCounselGridConfigurationIds` in `ExternalAccessModule.cs`).
4. Compare to how the **old SPA** fetched (it worked) — old path likely `ExternalDataService`/a different endpoint vs R2's module-data seam.
Likely suspects (all-grids-empty): the `sprk_gridconfiguration` fetch failing, `gridDataverseClient` base URL/auth, or the DataGrid response→rows mapping.

## Deployed state (dev)
- **SWA** `swa-spaarke-external-spa-dev` (https://green-dune-0c4f1221e.7.azurestaticapps.net) = **R2 client live** (shell markers verified; mock OFF; domain-qualified manifest). Old teams-r1 build replaced.
- **BFF** `spaarke-bff-dev` = R2 (Part-2 access model + 018 cleanup + 015/016 framework). Config added this session: `ExternalAccess__PortalUrl=<SWA url>` (was missing → blocked invite), `AzureAd__ValidAudiences__2=api://green-dune-…/1e40baad-…` (Teams SSO fallback). `/api/v1/collab/me`→404 (018 live), `/healthz`→200.
- **Entra `1e40baad` (SDAP-BFF-SPE-API)** — **R2 now owns it** (teams-r1 archived, handed off). Added domain-qualified identifierUri `api://green-dune-…/1e40baad-…` (additive; scopes+preAuth incl. broker preserved). Ratified teams-r1's broker pre-auth `29d9ed98` (Option A, NAA desktop).
- **Provisioned test identities**: hotmail contact `2e419a4f` (CIAM account provisioned via invite; oid `06646385` bound), grants → Project 1 (`b12496d1`) + PRJT.10007.02 (`3e34a21a`). spaarke.com contact `8e9918a9` (= ralph systemuser `1d02f31c` `sprk_primarycontact`), grants → Project 1 + 3e34a21a + more.

## Done this session (all committed + pushed + merged to master)
- **018** — deleted inert `ExternalCallerAuthorizationFilter` + `/api/v1/collab` group (net -963 LOC); build 0-err, 10155 tests pass (2 pre-existing `DataverseEntitySchemaTests` fails = ISS-018-1, inherited, unrelated).
- **019** — deployed P1 (BFF + client) from the worktree; smoke green.
- **Part-2 access model** (§6.5 Path-B, owner-directed) — workforce **systemuser sees membership ∪ own contact grants** (`AccessibleRecordSetService`, `WorkforcePrincipal.Email` + email fallback). Tests +3. Note: `notes/access-model-systemuser-contact-grant-union.md`.
- **Dev-mock leak fix** — worktree builds must pass `VITE_DEV_MOCK=false` (`.env.local` gotcha; memory saved).
- **Teams desktop SSO fix** — domain-qualified App ID URI + manifest + BFF ValidAudiences (Option B) + ratified Option A + R2 owns the Entra app. Note: `notes/teams-sso-fix-and-entra-app-ownership.md`.
- **Merged to master twice** (P1 + Teams fix); worktree-synced.

## Remaining (beyond the widget bug)
- **Admin (not worktree-doable)**: re-upload the Teams app **package** with the domain-qualified `webApplicationInfo.resource` (live Teams app is still teams-r1's GUID-form package) → then SSO-fallback fix is live; desktop NAA retest; admin consent via portal/az (not bare `/adminconsent`).
- **ISS-018-1**: 2 pre-existing `DataverseEntitySchemaTests` failures (Documents `UpdateDocumentRequest` schema drift from the master merge) — `/defer` to Documents owner.
- **P2 (020+)**: entitlement layer — real `/me` (task 022, replaces the mocked `me-client.ts`), Dataverse schema (owner sign-off), workforce-plane auth policy (024). Owner wave-gated.
- Fold the Teams NAA/SSO Entra recipe into `docs/guides/auth-deployment-setup.md` §3 (+ `ExternalAccess__PortalUrl`).

## Notes index
Per-task/finding detail in `notes/`: `task-018-deviations.md`, `task-019-deployment-record.md` (incl. UAT rounds 1-2), `access-model-systemuser-contact-grant-union.md`, `teams-sso-fix-and-entra-app-ownership.md`.
