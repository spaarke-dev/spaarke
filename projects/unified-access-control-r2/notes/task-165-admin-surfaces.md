# Task 165 — Admin surfaces: operator policy and SPE admin tenant scope

> **Task**: `tasks/165-admin-surfaces-operator-policy-and-tenant-scope.poml` (GitHub #1104)
> **Branch**: `task/uac-r2-165` from `work/unified-access-control-r2` @ `e6dd48b43`
> **Date**: 2026-10-03 · **Rigor**: FULL · **Model**: Opus 5.5
> **Outcome**: code-complete for the nine sweep routes and all four amendment items. The two escalations
> that stopped round 1 (§4.1 environments scoping, §4.2 three `SearchItemsTests`) were answered by **owner
> round 16 items 4 and 5**, implemented in fix round r1 (saved uncommitted, then committed by the main session
> on `task/uac-r2-165-r1` as `1712af73b`) and verified, corrected and committed in fix round r2 (branch
> `task/uac-r2-165-r2`, **§10**): a fail-open seed residue removed, the environment rule folded into the
> existing `SpeAdminTenantScopeFilter` (owner **round 20 item 4**), seeding, test justifications and the §11
> justification recorded. **Follow-up round f1** (branch `task/uac-r2-165-f1`, **§11**) closes **owner round 20 items
> 1-3** (every SPE container stamped with its owning business unit at creation + backfill script; per-container
> authorization on every container, item, column, custom-property, permission, recycle-bin, list, search and bulk
> route; app-only container-type routes guarded; app-identity check narrowed for Model 1), **round 25 item 5**
> (per-config dashboard storage; `SpeDashboardSyncService` migrated to `IScheduledJob`), the two unproven guards'
> tests + seeds, the widened body-marker guard, and the publish-size measurement. **Follow-up round f2** (branch
> `task/uac-r2-165-f2`, **§12**) closes **owner round 35 items 1-5** — every container-creation path stamps (the L2 H8
> handler, `New-BusinessUnitContainer.ps1`, `Provision-Customer.ps1`, `Create-NewContainerType.ps1`; ONE constant per
> side; guard over `src/` and `scripts/`), an UNBOUND container is reachable by no admin route (logged `reason unbound`;
> backfill `-Bind` + `-Verify` listing every unbound container; the pre-deploy / onboarding gate in the onboarding guide),
> `sprk_keyvaultsecretname` allow-listed (`spe-owning-app-`; 400 / 409 / read guards; `-Verify` script), security alerts
> and secure score platform-operator-only, container-type permission/consumer reads under the every-config rule — plus the
> verifier's items 4, 5 (the two unbitten guards) and 7 (the business-unit list projected). **Follow-up round f2-v1**
> (branch `task/uac-r2-165-f2-v1`, **§13**) closes **owner round 41 items 1-5** — H8 records what it created and RESUMES
> with it (the replication-pending path no longer orphans an unbound container, no second container type; the container is
> handed to H7 only once bound; H7 waits for H8), unbound containers in no dashboard view, the three dev test containers'
> `-Bind` decided, the dev config's secret-name repair as a dry-run/`-Apply`/`-Verify` script, the container-search total
> guard proven, the creation guard following every route (the verifier's seeds H1/H2 now RED), the `\z` anchor, and the
> H5/H8 root-business-unit read settled from code and put in live gate (d). Still open: only the manual live gates (§13.9).

---

## 0. Summary

| Finding / item | Route(s) | Mechanism now | Status |
|---|---|---|---|
| #47, #48, #77 | `POST /api/admin/record-matching/sync`, `POST …/sync-incremental`, `GET …/status` | group `.RequireAuthorization("SystemAdmin")`; 500s via `ProblemDetailsHelper.Explain` | done |
| #73 | `GET /api/spe/configs/{configId:guid}` | `SpeAdminTenantScopeFilter` (route value; was `{id}`, never read) | done |
| #45 | `PUT /api/spe/configs/{configId:guid}` | filter + body checks `SpeAdminTenantScope.DecideConfigWriteAsync` (business unit; changed identity fields) | done |
| #75 | `DELETE /api/spe/configs/{configId:guid}` | filter | done |
| #74 | `POST /api/spe/configs` | business unit required (400) + `DecideConfigWriteAsync` (business unit; app identity) | done |
| #44, #72 | `POST /api/spe/bulk/delete`, `POST /api/spe/bulk/permissions` | filter reads the bound body (`ISpeAdminConfigScopedRequest`); job refuses per item a container not of the config's type | done |
| amendment | SystemAdmin policy scope-substring branch (`AuthorizationModule.cs`) | branch removed; app role only | done |
| amendment | `SpeAdminTenantScope` fail-open | tri-state `SpeAdminScopeDecision`; fault → 503 | done |
| amendment | dashboard routes without business-unit scoping | `GET /api/spe/dashboard/metrics`, `POST …/refresh` project the aggregate onto reachable configs | done |
| amendment | `containertypes/{typeId}/register` app-only token to a caller-chosen host | host must be `{tenant}-admin.sharepoint.com`, https, default port, no user info | done |
| amendment | environment routes without business-unit scoping | the `/api/spe` group's `SpeAdminTenantScopeFilter` applies the environment rule to the routes marked `SpeAdminEnvironmentOperation` (GET-by-id = Read; POST / PUT / DELETE = Write); list trimmed in-handler; config POST/PUT environment link judged by `DecideConfigWriteAsync` (owner round 16 item 4, option (a); round 20 item 4: no second filter class) | done — fix rounds r1 + r2, §10 |
| trigger 5 | `POST /api/spe/search/items` — three `SearchItemsTests` | tests seed an in-scope config through `FakeDataverseTables`; not-found asserts the uniform 404; no real outbound Dataverse call (owner round 16 item 5) | done — fix round r1, §10 |
| round 20 item 1 + round 35 item 1 | every container-creation path: BFF admin plane, secure-record provisioning, L2 H8, `New-BusinessUnitContainer.ps1`, `Provision-Customer.ps1`, `Create-NewContainerType.ps1` | each stamps `spaarkeBusinessUnitId`, reads it back, removes the container if it did not land; ONE constant per side; guard over `src/` + `scripts/`; backfill (dry run / `-Apply` / `-Verify` / `-Bind`) for existing containers | done — f1 §11.2 (BFF), f2 §12.2 (the rest); backfill = manual gate §12.9 (a) |
| round 20 item 2 + round 35 item 2 | 31 container routes, 4 list/search routes, bulk delete/permissions/status | `SpeAdminTenantScopeFilter` per-container rule (own unit or descendant; **unbound / malformed → nobody**, logged with its reason; unreadable → 503; one 404); list trims; per-item check in the bulk job; status bound to its starter | done — f1 §11.3, f2 §12.3 |
| round 35 item 3 | every configId route; `POST`/`PUT /api/spe/configs`; the vault reads | `sprk_keyvaultsecretname` must start `spe-owning-app-`: 400 on write, 409 at the filter, refused before any cache and before the vault; `Test-SpeConfigSecretNames.ps1 -Verify` | done — f2 §12.4; dev config rename = manual gate §12.9 (b) |
| round 35 item 4 | `GET /api/spe/security/alerts`, `GET …/score` | platform-operator-only (filter, the environment-write check) | done — f2 §12.5 |
| round 35 item 5 | `GET /containertypes/{typeId}/permissions`, `GET …/consumers` | the every-config rule, reads included | done — f2 §12.6 |
| verifier f1 item 7 | `GET /api/spe/businessunits` | projected onto the caller's reach | done — f2 §12.7 |
| round 20 item 3 | `POST`/`PUT /api/spe/configs`; app-only `/containertypes/{typeId}/…` routes | only per-customer identity (consuming app + its secret) exclusive; type routes: type = config's, writes need every config of the type | done — f1, §11.3-11.4 |
| round 25 item 5 | `GET /api/spe/dashboard/metrics`, `POST …/refresh` | per-config storage; `SpeDashboardSyncService` → `IScheduledJob` | done — f1, §11.5 |

---

## 1. Task 167 state

**167 has NOT landed on this branch.** `RouteAuthorizationGuardTests.cs` on `work/unified-access-control-r2` @
`e6dd48b43` holds no waiver owned by `"165"`. Per the binding amendment (POML `<amendments>` item 3) this task
therefore adds **no** waivers, ledger entries or Permanent waivers and does **not** add files to
`GovernedFiles` (the amendment overrides the acceptance criterion's option (b)). The rows the main session
records in 167's ledger at integration are in §6. **167 not landed; main session reconciles waivers.**

Note for 167: the GET/PUT/DELETE config template changed from `{id:guid}` to `{configId:guid}`, so a Pending
waiver keyed on `/api/spe/configs/{id:guid}` must be **deleted**, not renamed.

---

## 2. Read-only live facts (dev, 2026-10-03; manual-gate item (e))

Read through the Dataverse MCP (`read_query`) and `az ad app list` — no writes.

| Fact | Value |
|---|---|
| `sprk_specontainertypeconfig` rows | **2** — "Spaarke SPE Model 1 Owner" (`68f9a952…`) and "Spaarke PAYGO 1" (`c3a25b9a…`) |
| rows with NO business unit | **0** |
| business units holding configs | **1** — both configs are in the ROOT unit "Spaarke" (`06fbf21c…`) |
| configs in different leaf units sharing any of the five identity fields | **0** (container types `fb3817a8…` / `8a6ce34c…`, owning apps `bfac7f6e…` / `170c98e1…` — all distinct; no consuming app values) |
| business units | root "Spaarke" + children Secure Record, Spaarke Business Unit 1, Spaarke Demo, Spaarke Dev 1, Spaarke Test 1 |
| `sprk_speenvironment` rows | **1** — "Spaarke Dev" (`df502cb9…`, default), owned by the root unit; both configs link it |
| `businessunit.sprk_containerid` | root and "Spaarke Business Unit 1" hold the SAME container id (`b!vzGD…`); Demo holds another — not a unique container → unit map |
| SPE admin outside the root unit | none known. The dev BFF registration `SDAP-BFF-SPE-API` (`1e40baad…`) defines app role **`Admin`** only (no `SystemAdmin`), delegated scopes `access_as_user`, `access_as_external_user`, `SDAP.Access`, `user_impersonation` — **none contains "admin"**. The owner-round-11 test user `uac.child.user@demo.spaarke.com` (Business Unit 1) holds no app role. |

Escalation triggers 1 and 7 checked against these: **not fired** (§4).

One data oddity, recorded only: config `68f9a952…` stores `sprk_keyvaultsecretname` = the literal string `"null"`.

---

## 3. What changed

### 3.1 Change map

| File | Change |
|---|---|
| `Api/Admin/RecordMatchingAdminEndpoints.cs` | group `.RequireAuthorization()` → `.RequireAuthorization("SystemAdmin")`; `.ProducesProblem(403)` on all three; the three 500 `detail`s → `ProblemDetailsHelper.Explain(<title>, ex)` |
| `Infrastructure/DI/AuthorizationModule.cs` | "SystemAdmin" policy: the `hasAdminScope` branch (scope claim CONTAINS "admin") removed; admits the Admin / SystemAdmin app role only |
| `Services/SpeAdmin/SpeAdminTenantScope.cs` | `CanAccessConfigAsync` (bool, fail-open) → `DecideConfigAccessAsync` (tri-state); `ResolveConfigBusinessUnitAsync` tells "does not exist" from "no business unit"; new `DecideConfigWriteAsync` (body business unit + app identity), `GetReachableConfigIdsAsync` (dashboard), `IdentityColumns`, whole-table config read (top 5000, no caller value in `$filter`); new public enum `SpeAdminScopeDecision` |
| `Api/Filters/SpeAdminTenantScopeFilter.cs` | reads configId from query (every value), route value and bound body (`ISpeAdminConfigScopedRequest`); disagreeing slots → 400 `spe.admin.deny.config_id_ambiguous` before any read; unknown and out-of-scope → ONE 404 (`ConfigNotFound`); unverifiable → 503 `spe.admin.deny.scope_unverifiable` (`ScopeUnverifiable`) |
| `Api/SpeAdmin/ConfigEndpoints.cs` | `{id:guid}` → `{configId:guid}` on GET/PUT/DELETE; the four handler 404s via `SpeAdminTenantScopeFilter.ConfigNotFound`; POST requires a non-empty `businessUnitId` (400); POST/PUT call `DecideConfigWriteAsync` (PUT judges only identity fields that CHANGE vs the stored row); 403 `spe.admin.deny.business_unit_out_of_scope` / `spe.admin.deny.config_identity_out_of_scope`; 503 on unverifiable |
| `Models/SpeAdmin/BulkOperationModels.cs` | new `ISpeAdminConfigScopedRequest`; `BulkDeleteRequest` and `BulkPermissionsRequest` implement it |
| `Services/SpeAdmin/BulkOperationService.cs` | per-item `DeleteContainerOfConfigTypeAsync` / `GrantOnContainerOfConfigTypeAsync` (read container → compare `containerTypeId` as GUIDs → write, or ONE refusal text); the job loops call only these; `TrackedOperationCount` (internal, read-only) |
| `Api/SpeAdmin/DashboardEndpoints.cs` | both routes project the cached aggregate onto `GetReachableConfigIdsAsync`; 503 when the scope cannot be read |
| `Api/SpeAdmin/ContainerTypeEndpoints.cs` | register: `sharePointAdminUrl` host must match `{tenant}-admin.sharepoint.com` (https, default port, no user info) → else 400 `spe.containertypes.register.sharepoint_url_not_admin_host` |
| `src/server/shared/Spaarke.Dataverse/DataverseWebApiClient.cs` | `DeleteAsync` made `virtual` (test seam only) |
| tests | `tests/integration/auth/SpeAdmin/{AdminSurfaceHostFixture, SpeAdminConfigAndBulkTenantScopeTests, BulkOperationContainerTypeTests}.cs`, `tests/integration/auth/Admin/RecordMatchingAdminPolicyTests.cs` (also holds `SystemAdminPolicyGroupTests`), `tests/Spaarke.ArchTests/SpeAdminConfigScopedBodyGuardTests.cs` |

`SpeDashboardSyncService.cs` was deliberately **not** touched: it is ADR-052 ratchet-listed ("migrates when
next touched"), and touching it would pull an IScheduledJob migration into this task. See §8.

### 3.2 Placement (CLAUDE.md §10, `.claude/constraints/bff-extensions.md`)

**In the BFF.** Every change is to an existing BFF route, the existing `/api/spe` group filter, the existing
`SpeAdminTenantScope` service, the existing `BulkOperationService` processor or the existing "SystemAdmin"
policy. No new endpoint, filter class, policy, service, DI registration, option, job, package or Dataverse
column. No AI type is touched (ADR-013). No plugin (ADR-002). The decision for a config a request NAMES stays
in the one group filter (ADR-008); the only in-handler checks are POST/PUT config body values (allowed by the
task constraint, `ListConfigsAsync` precedent) and the dashboard projection (a list-style trim of an
aggregate, same precedent). Publish size: not measured (instructed to skip). CVE: no package change.

### 3.3 New types and members — three-question justification (CLAUDE.md §11)

| New | Existing (grep) | Extension? | Cost of doing nothing |
|---|---|---|---|
| `enum SpeAdminScopeDecision` (Permitted / NotFoundOrOutOfScope / BusinessUnitOutOfScope / IdentityOutOfScope / Unverifiable) | `CanAccessConfigAsync` returned `bool`; no other scope result type in `Services/SpeAdmin` (grep `ScopeDecision`) | a bool cannot carry "could not be evaluated" — that is exactly why it failed open; one enum serves all three decisions instead of three result types | a Dataverse blip on the filter's read lets a cross-business-unit configId through (the fail-open defect) |
| `interface ISpeAdminConfigScopedRequest` | the two bulk records were the only `Models/SpeAdmin` types with `ConfigId` (grep) | the filter could name the two concrete types, but then the NEXT body record bypasses the boundary silently, as these two did | sweep #44/#72 recur on the next body-carried configId; `SpeAdminConfigScopedBodyGuardTests` fails the build instead |
| `SpeAdminTenantScopeFilter.ConfigNotFound` / `ScopeUnverifiable` (static) | five 404 sites built the problem inline with different extension keys (`correlationId` vs `errorCode`+`traceId`) | added to the existing filter class, no new type | the handler 404 and the filter 404 differ → existence oracle |
| `SpeAdminTenantScope.DecideConfigWriteAsync`, `GetReachableConfigIdsAsync`, `IdentityColumns`, `WholeTableReadLimit`, `ConfigBusinessUnitLookup` (internal record struct) | `ListConfigsAsync`'s intersection; `LoadBusinessUnitHierarchyAsync` shape | extend the existing boundary class rather than a new service | POST/PUT store any unit or another unit's app identity (#74, #45); the dashboard leaks every customer's counts |
| `BulkOperationService.DeleteContainerOfConfigTypeAsync`, `GrantOnContainerOfConfigTypeAsync`, `TrackedOperationCount`, `ContainerNotOfConfigTypeError` | the job loops called the Graph writes inline; the class is sealed | extracted from the loops (one call site per write) | "no write was sent" / "nothing was enqueued" not assertable; a caller-chosen container of another type is written app-only |
| `ContainerTypeEndpoints.IsSharePointAdminHost` (internal static) | URL validated only as absolute https | added to the existing handler file | the BFF sends an app-only bearer token for `{host}/.default` to a host the caller chooses |
| `DataverseWebApiClient.DeleteAsync` `virtual` | Retrieve/Create/Update/Query already virtual | — | "DeleteAsync never called" cannot be asserted at the class boundary (ADR-038 §4) |

---

## 4. Escalation triggers

| # | Trigger | Result |
|---|---|---|
| 1 | business-unit-less configs in a multi-customer environment / need to change the BU-less rule | **not fired** — 0 BU-less rows (§2); the rule is unchanged; POST now refuses to create one |
| 2 | a shipped consumer breaks | **not fired** — `ContainerTypeConfig.tsx:521` requires a business unit; the bulk client sends `configId` in the body with no query string (`speApiClient.ts:1409-1424`, unchanged and still accepted); the PUT client re-sends unchanged values, which are no longer judged; `register` is called by the client with neither `appId` nor `sharePointAdminUrl` (`speApiClient.ts:549-557`) — a PRE-EXISTING 400 this task does not change |
| 3 | an automated caller of `/api/admin/record-matching/*` without the role | **not fired** — grep of `scripts/`, `.github/workflows/`, `infrastructure/`: none (only docs, tests and the endpoint file) |
| 4 | per-container ownership needs a container → business-unit map | **not fired** — the bulk fix binds a container to the config's container TYPE (the mandated check). `businessunit.sprk_containerid` is not a unique map (root and BU1 share one id, §2), so no business-unit binding was invented. The residual (one type serving several customers, topology §3 Model 1) is §8 item 3 |
| 5 | the fail-closed 503 / does-not-exist 404 changes an existing test or shipped flow outside the six routes | **FIRED — §4.2; answered by owner round 16 item 5; fixed in §10** |
| 6 | an ADR exception | **not fired** — the SystemAdmin policy change is the owner's amendment, not an ADR deviation; no per-handler `CanAccessConfigAsync` |
| 7 | shared app identity across leaf units | **not fired** — 0 (§2) |

### 4.1 STOPPED in round 1 — environment routes business-unit scoping (owner amendment item) → RESOLVED: owner round 16 item 4 chose (a); implemented in §10

🔔 **Human Input Required.** The amendment says "also fix … the SPE environments … routes without
business-unit scoping". Not implemented: the *how* is a product decision the existing rules do not answer.

- **Situation.** `sprk_speenvironment` (tenant id, tenant name, root site URL, is-default) has **no business-unit
  column**; its owning unit is whoever created it (the BFF writes app-only, so effectively the app user's unit).
  An environment is tenant-level SPE infrastructure: in Model 1 one environment is shared by every customer's
  configs (dev: one environment, root-owned, linked by both configs). `GET/POST /api/spe/environments`,
  `GET/PUT/DELETE /api/spe/environments/{id:guid}` (`EnvironmentEndpoints.cs:47-101`) let ANY SPE admin read,
  edit and delete every environment, and `POST/PUT /api/spe/configs` may link any `environmentId`.
- **Options.**
  - **(a) RECOMMENDED — environments are platform infrastructure.** Writes (POST/PUT/DELETE) only for an admin
    whose OWN business unit is the root (no parent) — a Spaarke operator; anyone else 403. Reads: a root admin
    sees all; a leaf admin sees only environments linked by a config they can reach (others: uniform 404,
    omitted from the list). Config POST/PUT may link only an environment the caller can read. Consequence: a
    leaf admin with NO config yet cannot pick an environment, so first-config creation for a customer is an
    operator/provisioning step (consistent with `customer-provisioning-orchestration-r1` creating configs).
  - **(b)** add a `sprk_businessunit` lookup to `sprk_speenvironment` (schema change, backfill) and scope exactly
    like configs. Heavier; a shared Model-1 environment would still need the root unit.
  - **(c)** leave environments readable by every SPE admin, restrict only writes to root admins.
- **Recommendation**: (a). It needs no schema change and fails closed.
- **Impact if chosen**: one follow-up change to `EnvironmentEndpoints.cs` + the config POST/PUT environment
  link check + tests; no client change for root operators; leaf admins lose environment edit (they should
  never have had it).

### 4.2 STOPPED in round 1 — `SearchItemsTests` outcomes changed by the fail-closed filter (trigger 5) → RESOLVED: owner round 16 item 5; implemented in §10

Route: **`POST /api/spe/search/items?configId=…`** (outside this task's six). Tests (unit suite,
`tests/unit/Sprk.Bff.Api.Tests/SpeAdmin/SearchItemsTests.cs`, NOT a KEEP path):

| Test | Asserted | Now |
|---|---|---|
| `SearchItems_WithToken_EmptyQuery_Returns400` | 400 | 503 |
| `SearchItems_WithToken_WhitespaceQuery_Returns400` | 400 | 503 |
| `SearchItems_WithToken_ValidConfigIdNotFound_Returns400` | 400 or 500 for a config that does not exist | 503 |

**Why.** These tests run the real `DataverseWebApiClient` against `test.crm.dynamics.com` (a real outbound
call, as `projects/INDEX.md` records for `SearchItemsTests`). The filter's config read failed and the old filter
**allowed** the request, so the handler's 400 answered. Fail-closed now answers 503. With a reachable Dataverse
the third test would get the uniform 404 — it asserts the very oracle this task removes ("an unknown config
answers differently from an out-of-scope one").

**Not resolved here** (trigger 5 says stop and report, never restore fail-open). **Recommended resolution** for
the main session: (1) give `SearchItemsTests` the `AdminSurfaceHostFixture` Dataverse fake (or the
`FakeDataverseTables` mock) seeded with an in-scope config, so Empty/Whitespace keep asserting the handler's 400
on a config the caller CAN reach; (2) change `ValidConfigIdNotFound_Returns400` to assert the uniform 404
(`errorCode` `spe.admin.deny.config_out_of_scope`); (3) this also removes the suite's real outbound Dataverse
call. Alternatively move them to a KEEP path at the same time (the r3 seed lists this as owed work).

No other existing test changed outcome: full results in §5.

---

## 5. Tests

### 5.1 New tests (all through the real host unless stated)

- `SpeAdminConfigAndBulkTenantScopeTests` (69): GET uniform 404 + no Retrieve; in-scope 200; GET/PUT/DELETE id
  substitution → 400 with no config call; PUT 404 / BU 403 (×2) / identity 403 (×3) / full-body unchanged 200;
  DELETE 404 ×2 / 204; POST missing BU 400 (×2) with zero Dataverse calls, BU 403 (×2), unresolvable caller 403,
  201, identity 403 (×7 incl. trim/case and cross-column), BU-less/reachable value 201 (×2), identity-read
  fault 503, full-page 503; bulk 404 (×4) count unchanged, 202 (×2) count +1, ambiguous 400 (×2), handler's own
  400 (×2); fail-closed 503 (5 routes × 2 faults); POST hierarchy fault 503; list keeps 500; non-admin 403 +
  anonymous 401 (×6); another config-scoped route still reaches its handler; dashboard leaf projection, root
  unchanged, scope fault 503; register host 400 (×5) and pass.
- `BulkOperationContainerTypeTests` (9; real `SpeAdminGraphService` + WireMock Graph): another type, 404, 500 →
  one text, no DELETE / no permissions POST; same type differing in case → write sent; source check that each
  Graph write has one call site, inside its per-item method.
- `RecordMatchingAdminPolicyTests` (16): User role 403 + zero service calls (×3); delegated no-role 403 (×3);
  SystemAdmin and Admin 200 (×3); anonymous 401 (×3); 500 detail == `Explain(title, ex)` with no secret (×3);
  group built with `"SystemAdmin"` and no bare `RequireAuthorization()`.
- `SystemAdminPolicyGroupTests` (12): one route per group sharing the policy (`/api/admin/jobs`,
  `/api/admin/membership/…`, `/api/ai/rag/admin/…`, `/api/admin/record-matching/status`): Admin and SystemAdmin
  admitted; User refused; a scope containing "admin" with no role refused.
- `SpeAdminConfigScopedBodyGuardTests` (ArchTests, 1).

Additional-test justifications (beyond the POML's closed list): `SystemAdminPolicyGroupTests` — proves the
amendment's "every group that shares the policy admits a real administrator and refuses a non-admin";
dashboard and register tests — the amendment items; `SpeAdminConfigScopedBodyGuardTests` — the guard for the
marker interface's stated cost-of-doing-nothing; the cross-column identity case — the kind-grouping decision
(§7 D3).

### 5.2 Seeding proofs (each guard removed, named tests RED, source restored and touched)

| # | Seed | RED |
|---|---|---|
| 1 | record-matching group back to bare `RequireAuthorization()` | 7 — `RecordMatchingAdminPolicyTests.ACallerWithAUserRoleOnly…` ×3, `ADelegatedCallerWithNoAppRole…` ×3, `TheGroupIsBuiltWithTheSystemAdminPolicy…` |
| 2 | GET route parameter back to `{id:guid}` (`[FromRoute(Name="id")]`) | `Get_OutOfScopeConfig_AnswersTheSame404AsAnUnknownConfig_AndReadsNothing` |
| 3 | filter body-argument read removed | 6 — `Bulk_WithAnOutOfScopeOrUnknownBodyConfigId…` ×4, `Bulk_WhoseQueryAndBodyConfigIdsDisagree…` ×2 |
| 4 | disagreeing-configId 400 removed | 5 — `ConfigRoute_WhoseQueryConfigIdDiffersFromTheRoute…` ×3 (GET/PUT/DELETE), `Bulk_WhoseQueryAndBodyConfigIdsDisagree…` ×2 |
| 5 | does-not-exist → Permitted (filter passes unknown configs) | 3 — `Get_OutOfScopeConfig…`, `Bulk_WithAnOutOfScopeOrUnknownBodyConfigId…` (unknown) ×2 |
| 6 | unverifiable → allow (both the config read and the hierarchy read) | 10 — `FilteredRoute_WhenTheScopeCannotBeRead_Is503…` (5 routes × 2 faults) |
| 7 | POST business-unit intersection removed | 3 — `Post_IntoAnUnreachableBusinessUnit…` ×2, `Post_ByACallerWithNoResolvableBusinessUnit…` |
| 8 | PUT business-unit intersection removed | 2 — `Put_ThatMovesAnInScopeConfigToAnUnreachableUnit…` ×2 |
| 9a | identity check removed from POST | 9 — `Post_BorrowingAnIdentityValue…` ×7, `Post_WhenTheIdentityReadFaults…`, `Post_WhenTheIdentityReadReturnsAFullPage…` |
| 9b | identity check removed from PUT | 3 — `Put_ThatRepointsAnIdentityField…` ×3 |
| 9c | PUT judges an UNCHANGED owning app (over-block) | `Put_TheShippedClientsFullBody_WithUnchangedValues_IsServed…` |
| 10a | per-item type check removed — delete path | 3 — `Delete_OfAContainerOfAnotherType…`, `Delete_WhenTheContainerCannotBeRead…` ×2 |
| 10b | per-item type check removed — permissions path | 3 — `Grant_OnAContainerOfAnotherType…`, `Grant_WhenTheContainerCannotBeRead…` ×2 |
| 10c | delete job loop calls `SoftDeleteContainerAsync` directly | `TheTwoGraphWrites_HaveExactlyOneCallSiteEach_InsideThePerItemMethods` |
| 11 | SystemAdmin scope-substring branch restored | 4 — `SystemAdminPolicyGroupTests.ADelegatedScopeThatMerelyContainsAdmin_IsRefused` (all 4 groups) |
| 12 | register host check disabled | 5 — `Register_ToAHostThatIsNotASharePointAdminHost_Is400` ×5 |
| 13 | dashboard GET returns the unprojected aggregate | `Dashboard_ForALeafAdmin_ShowsOnlyTheirConfigs…` |
| 14 | `BulkDeleteRequest` no longer implements the marker | `SpeAdminConfigScopedBodyGuardTests` (ArchTests) |

Seed 4 first run was invalid (the seed did not compile — `CS0162` — so the stale build ran); re-seeded with a
compiling form and re-run; the table shows the valid run. After every seed the sources were restored from a
byte copy and touched; `git diff --stat` matched the pre-seed state.

### 5.3 Suites

Run 2026-10-03 on this branch (after all seeds restored):

| Suite | Result |
|---|---|
| affected: the 4 new classes | 106 / 0 failed |
| affected: `SpeAdmin|TenantScope|BulkOperation|RegisterContainerType|Admin|Dashboard|RecordMatching` | 503 passed / **3 failed** (the §4.2 `SearchItemsTests`) |
| full BFF unit suite `tests/unit/Sprk.Bff.Api.Tests` | **14,315 passed / 3 failed / 54 skipped (14,372)** — the 3 failures are exactly §4.2; nothing else changed outcome |
| NetArchTest `tests/Spaarke.ArchTests` | 347 passed / 0 failed / 0 skipped |
| `tests/integration/Sprk.Bff.Api.IntegrationTests` (full) | 104 passed / 0 failed / 0 skipped |
| `tests/integration/Spe.Integration.Tests` (full, incl. `Phase2RecordMatchingTests`) | 403 passed / 0 failed / 25 skipped (428) |

Regression classes named by the POML — `SpeAdminContainerItemRouteGateTests`, `SpeAdminAuthorizationLayerTests`,
`TenantScopeHierarchyTests`, `BulkOperationTests`, `SpeAdminClientRouteAgreementTests`,
`SpeWriteSinkContainerProvenanceGuardTests`, `Phase2RecordMatchingTests` — all green. No package change, so no
CVE delta; publish size not measured (instructed).

### 5.4 Step 9.5 quality gates

**code-review** (all changed files): no Critical. W1 — Model 1 shared container type vs the identity check
(recorded as D5b). W2 — `POST /api/spe/dashboard/refresh` projection is covered only through the shared
`ProjectToReachableConfigs` exercised by the GET test (refresh triggers a sync the fake host cannot run).
Suggestions: `SpeAdminTenantScope` grew to 560 lines (mostly doc comments) but stays one responsibility (the admin-plane scope
decision); `ConfigEndpoints` grew by the write checks (~+90). Catch-alls in `IsContainerOfConfigTypeAsync` and
the scope decisions are deliberate fail-closed conversions, not log-rethrow. No interface-with-one-impl smell
(the marker is a contract the filter reads, guarded by an arch test).

**adr-check**: ADR-001 (Minimal API, no new routes) ✓ · ADR-002 (no plugin) ✓ · ADR-003 (fail closed; machine
deny codes; no decision cached — the scope class stays uncached) ✓ · ADR-007 (Graph types stay in internal
service members; NetArchTest green) ✓ · ADR-008 (the config the request NAMES is decided in the one group
filter; in-handler checks only judge POST/PUT body values and trim the dashboard aggregate, per the task
constraint and the `ListConfigsAsync` precedent) ✓ · ADR-010 (no DI registration) ✓ · ADR-019
(ProblemDetails) ✓ · ADR-038 (no banned pattern; ban guards green) ✓ · ADR-052 (no timer touched; the ratchet-
listed `SpeDashboardSyncService` deliberately untouched) ✓. No §6.5 path needed.

---

## 6. Route authorization ledger input (for task 167, main session)

| Route key (guard spelling) | Mechanism that now decides | Deny test |
|---|---|---|
| `POST /api/admin/record-matching/sync` | admin policy `"SystemAdmin"` on the group | `Sprk.Bff.Api.Tests.Auth.Admin.RecordMatchingAdminPolicyTests.ACallerWithAUserRoleOnly_IsForbidden_AndTheSyncServiceIsNeverCalled` |
| `POST /api/admin/record-matching/sync-incremental` | admin policy `"SystemAdmin"` on the group | same test (theory row) |
| `GET /api/admin/record-matching/status` | admin policy `"SystemAdmin"` on the group | same test (theory row) |
| `GET /api/spe/configs/{configId:guid}` | filter `SpeAdminTenantScopeFilter` (route value) on the `/api/spe` group, after `SpeAdminAuthorizationFilter` | `Sprk.Bff.Api.Tests.Auth.SpeAdmin.SpeAdminConfigAndBulkTenantScopeTests.Get_OutOfScopeConfig_AnswersTheSame404AsAnUnknownConfig_AndReadsNothing` |
| `PUT /api/spe/configs/{configId:guid}` | filter (route value) + handler decision on body values `SpeAdminTenantScope.DecideConfigWriteAsync` | `…SpeAdminConfigAndBulkTenantScopeTests.Put_OutOfScopeConfig_IsTheUniform404_AndNothingIsWritten`; `…Put_ThatMovesAnInScopeConfigToAnUnreachableUnit_Is403_AndNothingIsWritten`; `…Put_ThatRepointsAnIdentityFieldToAnotherUnitsValue_Is403_AndNothingIsWritten` |
| `DELETE /api/spe/configs/{configId:guid}` | filter (route value) | `…SpeAdminConfigAndBulkTenantScopeTests.Delete_OutOfScopeAndUnknownConfigs_AreTheUniform404_AndNothingIsDeleted` |
| `POST /api/spe/configs` | handler decision on body values `SpeAdminTenantScope.DecideConfigWriteAsync` (business unit + app identity); group role filter | `…SpeAdminConfigAndBulkTenantScopeTests.Post_IntoAnUnreachableBusinessUnit_Is403_AndNothingIsCreated`; `…Post_BorrowingAnIdentityValueFromAnUnreachableUnit_Is403_AndNamesNeitherConfigNorUnit` |
| `POST /api/spe/bulk/delete` | filter (bound body `ISpeAdminConfigScopedRequest`) + per-item container-type check in the job | `…SpeAdminConfigAndBulkTenantScopeTests.Bulk_WithAnOutOfScopeOrUnknownBodyConfigId_IsTheUniform404_AndNothingIsEnqueued`; `…BulkOperationContainerTypeTests.Delete_OfAContainerOfAnotherType_IsRefused_AndNoDeleteIsSent` |
| `POST /api/spe/bulk/permissions` | filter (bound body) + per-item container-type check | `…Bulk_WithAnOutOfScopeOrUnknownBodyConfigId_IsTheUniform404_AndNothingIsEnqueued` (theory rows); `…BulkOperationContainerTypeTests.Grant_OnAContainerOfAnotherType_IsRefused_AndNoPermissionIsPosted` |
| `GET /api/spe/dashboard/metrics` (amendment) | handler decision: aggregate projected onto `GetReachableConfigIdsAsync` | `…SpeAdminConfigAndBulkTenantScopeTests.Dashboard_ForALeafAdmin_ShowsOnlyTheirConfigs_WithTotalsRecomputed` |
| `POST /api/spe/dashboard/refresh` (amendment) | same projection; 503 when the scope cannot be read | `…SpeAdminConfigAndBulkTenantScopeTests.DashboardRefresh_ForALeafAdmin_IsProjectedExactlyLikeTheRead`; `…DashboardRefresh_WhenTheScopeCannotBeRead_Is503_NeverTheUnprojectedAggregate` (fix round r1, §10.4) |
| `POST /api/spe/containertypes/{typeId}/register` (amendment) | filter (`?configId`) + host allow-list | `…SpeAdminConfigAndBulkTenantScopeTests.Register_ToAHostThatIsNotASharePointAdminHost_Is400` |
| `GET /api/spe/environments` (round 16 item 4) | handler decision: list trimmed to `SpeAdminTenantScope.GetEnvironmentReachAsync` (platform operator = all; otherwise environments linked by a reachable config); 503 when unverifiable | `Sprk.Bff.Api.Tests.Auth.SpeAdmin.SpeAdminEnvironmentScopeTests.List_ForALeafAdmin_HoldsOnlyTheEnvironmentsTheirReachableConfigsLink` |
| `GET /api/spe/environments/{id:guid}` (round 16 item 4) | filter `SpeAdminTenantScopeFilter` on the `/api/spe` group, environment rule (route marked `SpeAdminEnvironmentOperation.Read`) | `…SpeAdminEnvironmentScopeTests.GetById_ForALeafAdmin_AnswersTheSame404ForAnUnreadableAndAnUnknownEnvironment_AndReadsNeither` |
| `POST /api/spe/environments` (round 16 item 4) | filter `SpeAdminTenantScopeFilter`, environment rule (marked `Write`: root business unit only) | `…SpeAdminEnvironmentScopeTests.Write_ByALeafAdmin_IsOne403_AndNothingIsReadOrWritten` (theory row POST) |
| `PUT /api/spe/environments/{id:guid}` (round 16 item 4) | filter `SpeAdminTenantScopeFilter`, environment rule (marked `Write`) | same test (theory rows PUT) |
| `DELETE /api/spe/environments/{id:guid}` (round 16 item 4) | filter `SpeAdminTenantScopeFilter`, environment rule (marked `Write`) | same test (theory rows DELETE) |

`POST /api/spe/configs` and `PUT /api/spe/configs/{configId:guid}` (rows above) now also judge the linked
`environmentId` in `DecideConfigWriteAsync`: deny test
`…SpeAdminEnvironmentScopeTests.PostConfig_LinkingAnEnvironmentTheLeafAdminCannotRead_IsOne403_AndNothingIsCreated`
and `…PutConfig_RelinkingToAnUnreadableEnvironment_Is403_AndNothingIsWritten`. The environment route templates are
unchanged (`{id:guid}`), so no waiver key moves.

None of these is a list whose query runs as the caller. No Permanent waiver is proposed for any of them.

**Routes deleted: none.** All nine have callers or are operator routes in published use: the config/bulk routes
are called by the shipped SpeAdminApp (`speApiClient.ts`); the record-matching routes are operator routes
documented and called by hand (`projects/ai-spaarke-platform-enhancments-r3/tasks/003-populate-records-index.poml`)
and appear in deployed smoke baselines — so round 10 item 1 (delete only when no caller AND not published) does
not apply.

---

## 7. Decisions

- **D1 — PUT judges only CHANGED identity fields** (trimmed, case-insensitive vs the stored row); the business
  unit is judged whenever present. The shipped client re-sends every field on save.
- **D2 — the uniform 404 helper lives on the filter** (`ConfigNotFound`), used by the filter and the four config
  handler not-found sites; those four drop their `correlationId` extension (`errorCode` + `traceId` only).
- **D3 — identity values compared within a KIND across columns** (app ids: owning + consuming; secret names:
  owning + consuming; container type alone). Stricter than "same field": naming unit B's owning app as one's
  consuming app is the same borrowing. Live data has no collision (§2).
- **D4 (RETIRED in f1, §11.5 — storage is now per config) — dashboard storage in a partial view reports 0 bytes from 0 reporting containers.** The cache holds only
  a cross-config sum; reporting a share of it would leak other customers' storage. The metrics contract already
  reads a reporting count below the total as "a floor". The per-config split needs `SpeDashboardSyncService`
  (ADR-052 ratchet-listed) — §8 item 4.
- **D5 — a BU-less config may be claimed by PUT into the caller's unit.** It is visible to every admin under the
  compatibility rule; the BU check judges the target unit only. Recorded; unchanged rule (trigger 1).
- **D5b (SUPERSEDED by owner round 20 item 3, implemented in f1 §11.4) — Model 1 observation (code-review W1, for the owner).** Topology §3 Model 1 is "one container type,
  one container per customer". If several customers' configs in different leaf units are meant to share one
  `containerTypeId` / owning app, a LEAF admin's POST naming that shared type is refused by the identity check
  (a root operator, who reaches every unit, is not refused). That is trigger 7's option (a) — keep the check —
  and live data has no such sharing today (§2). If Model 1 provisioning is to be done by leaf admins rather than
  operators, the owner chooses trigger 7 option (b) (narrow to `containerTypeId` + `owningAppId`) or keeps
  provisioning operator-side.
- **D6 — the SystemAdmin policy keeps `RequireAuthenticatedUser()` + the four role checks** (IsInRole and the
  raw `roles` claim, Admin or SystemAdmin) — the same signal `SpeAdminAuthorizationFilter` checks. Dev defines
  only the `Admin` role and no "admin"-containing scope (§2), so no legitimate token loses access.

---

## 8. Not in this task (for the main session to file or route)

1. ~~§4.1 environments~~ — done in fix rounds r1 + r2 (owner round 16 item 4; round 20 item 4), §10.
2. ~~§4.2 SearchItemsTests~~ — done in fix round r1 (owner round 16 item 5), §10.
3. ~~Container → business-unit binding~~ — done in follow-up round f1 (owner round 20 items 1-3), §11.2-11.4.
4. ~~Dashboard per-config storage~~ — done in f1 (owner round 25 item 5), §11.5; D4 retired.
5. ~~`GET /api/spe/bulk/{operationId}/status` not bound to its starter~~ — done in f1, §11.3 (Bulk).
6. `RouteAuthorizationGuardTests.ClaimOnlyFilters["SpeAdminTenantScopeFilter"]` describes the filter as deciding
   "from claims" — inaccurate (it reads Dataverse through `SpeAdminTenantScope`). The file belongs to task 167
   this round (amendment); 167 should correct the description.
7. Pre-existing: the SpeAdminApp `register` call sends neither `appId` nor `sharePointAdminUrl`, so it already
   gets 400 (`speApiClient.ts:549-557`). Unchanged; the host allow-list does not affect it.

No `.claude/**` edit is needed by this task.

---

## 9. Manual live gate (main session; dev; owner approves write probes by batch)

Ids redacted to 8 characters. `$T` = an access token for the BFF (`api://1e40baad-…/.default`) of the named user;
`$BFF` = the dev BFF base URL.

- **(a)** Check the flag: `az webapp config appsettings list -g <rg> -n <bff-app> --query "[?name=='DocumentIntelligence__RecordMatchingEnabled'].value"`. If true: as a NON-admin test user
  `curl -i -H "Authorization: Bearer $T_user" $BFF/api/admin/record-matching/status` → **403**;
  `curl -i -X POST -H "Authorization: Bearer $T_user" -H "Content-Type: application/json" -d "{}" $BFF/api/admin/record-matching/sync` → **403** (refused, no side effect);
  as an admin (`Admin` app role) `GET …/status` → **200**. If false: record and skip.
- **(b)** Needs an SPE admin OUTSIDE the root unit (none exists — §2). Ask the owner to grant the `Admin` app
  role on `SDAP-BFF-SPE-API` to a user in "Spaarke Business Unit 1" (e.g. `uac.child.user@…`, owner round 11) —
  an Entra write, owner-approved only. Then: `GET $BFF/api/spe/configs/68f9a952-…` (root-unit config) → **404**,
  byte-identical apart from `traceId` to `GET $BFF/api/spe/configs/<random guid>`.
- **(c)** WRITE probes — owner approval required, throwaway config + throwaway containers only. As the same
  leaf admin: `PUT`, `DELETE` on the root config → **404**; `POST $BFF/api/spe/bulk/delete` and
  `…/bulk/permissions` with body `configId` = the root config → **404**, and no operation id;
  `POST $BFF/api/spe/configs` with `businessUnitId` = BU1 and `containerTypeId` = `fb3817a8-…` (the root config's
  type) → **403** `spe.admin.deny.config_identity_out_of_scope`. Expect zero Dataverse/Graph change.
- **(d)** As an in-scope (root) admin in the SpeAdminApp: list, open and edit their own config (PUT with the
  form's full body → **200**); start a bulk delete on a THROWAWAY container of that config's type → completes;
  the dashboard shows the whole aggregate.
- **(e)** done during development — §2.
- **(g)** round 16 item 4 (environments), read-only part: as the leaf admin of (b), `GET $BFF/api/spe/environments` →
  only environments linked by a config of their unit (dev today: none → `[]`, since both dev configs are root-unit);
  `GET $BFF/api/spe/environments/df502cb9-…` → **404** `spe.admin.deny.environment_out_of_scope`, byte-identical apart
  from `traceId` to a random GUID. As a ROOT admin: the list holds "Spaarke Dev", GET → 200. WRITE part (owner
  approval; throwaway only): as the leaf admin, `POST $BFF/api/spe/environments` with
  `{"name":"probe","rootSiteUrl":"https://example.sharepoint.com"}` → **403**
  `spe.admin.deny.environment_write_requires_platform_operator`, and `PUT`/`DELETE` on `df502cb9-…` → the same 403;
  expect zero Dataverse change.
- **(f)** amendment: `POST $BFF/api/spe/containertypes/<type>/register?configId=<own config>` with
  `sharePointAdminUrl: "https://example.com"` → **400** `spe.containertypes.register.sharepoint_url_not_admin_host`
  (read-only effect: refused before any token is acquired).

---

## 10. Fix rounds r1 + r2 (owner rounds 16 and 20; adversarial verifier re-verification 2026-10-04)

### 10.0 How the round reached the branch

- **r1** implemented owner round 16 items 4 (environments) and 5 (`SearchItemsTests`) plus the first verifier's
  findings 6-8 (non-canonical GUID spellings in the identity check; dashboard completeness concern and refresh
  projection; the negative-twin route test) — but left everything **uncommitted** in its worktree, and its seeding
  run was killed mid-seed, leaving seed **E3** ("env list untrimmed") in `EnvironmentEndpoints.cs:174`:
  `.Where(r => reach.CanRead(r.Id) || r.Id != Guid.Empty)` — a **fail-open** that lists every environment to any
  leaf admin who reaches one. The main session saved that worktree as `1712af73b` on `task/uac-r2-165-r1`
  (the seed included; marked UNVERIFIED).
- **r2** (this round, branch `task/uac-r2-165-r2`): fast-forward-merged `task/uac-r2-165-r1` (`5c8192f11`),
  **restored `.Where(r => reach.CanRead(r.Id))`** (verified: seed E3 below goes RED against the restored line, and
  no other seed string from r1's script is present in any source file), folded the environment rule into the
  existing filter (round 20 item 4, §10.2), re-ran every seed and every suite, and wrote this section. The main
  session's coordination file `NOTE-FROM-MAIN.md` is not committed.

### 10.1 What r1 changed (kept, reviewed)

| File | Change |
|---|---|
| `Services/SpeAdmin/SpeAdminTenantScope.cs` | `GetEnvironmentReachAsync` (platform operator = own unit is the root → reads all, writes; anyone else reads the environments linked by a config they reach; THROWS on a fault or a full 5000-row page); `DecideConfigWriteAsync` gains `environmentId` (order: business unit → environment → identity; `Guid.Empty` / an unresolvable caller → `EnvironmentOutOfScope`); identity values compared in canonical form (`CanonicalIdentityValue` / `CanonicalIdentityColumnValue`: a GUID-valued container type / app id compared as its `D` form); `GetReachableConfigIdsAsync` also returns `ReachesEveryConfig` judged over the WHOLE table; private `LoadCallerScopeAsync` / `CallerScope`; `ConfigScopeRow.EnvironmentId` (`_sprk_environment_value` added to the existing whole-table select); new enum member `SpeAdminScopeDecision.EnvironmentOutOfScope`; new record `SpeAdminEnvironmentReach` |
| `Api/SpeAdmin/ConfigEndpoints.cs` | POST/PUT pass `request.EnvironmentId`; 403 `spe.admin.deny.environment_out_of_scope` "The SPE environment is not one you can use."; PUT `Changed()` compares in the canonical form |
| `Api/SpeAdmin/EnvironmentEndpoints.cs` | list trimmed to the reach (503 when unverifiable, `[]` when the caller reaches nothing); the handlers' not-found paths use the shared environment 404 |
| `Api/SpeAdmin/DashboardEndpoints.cs` | full view only when the caller reaches EVERY config in the table; the tenant-wide `CompletenessConcern` dropped from a projection; storage kept only when every counted config is reachable; refresh uses the same projection |
| tests | `SearchItemsTests` moved onto `AdminSurfaceHostFixture` + `FakeDataverseTables` (no outbound Dataverse call; empty/whitespace assert the handler's 400 on an in-scope config; not-found asserts the uniform 404); new `SpeAdminEnvironmentScopeTests`; new tests in `SpeAdminConfigAndBulkTenantScopeTests` and `RecordMatchingAdminPolicyTests` (§10.4) |

### 10.2 r2: the environment rule folded into `SpeAdminTenantScopeFilter` (owner round 20 item 4)

r1 added a second filter class, `SpeAdminEnvironmentScopeFilter`, against the POML's CLAUDE.md §11 constraint
"No new filter class". Round 20 item 4 decides it: **fold into the EXISTING SPE-admin scope filter.** Done:

- `SpeAdminEnvironmentScopeFilter.cs` **deleted** (with its `AddSpeAdminEnvironmentScopeFilter` extension).
- `SpeAdminTenantScopeFilter` (already on the `/api/spe` group, after `SpeAdminAuthorizationFilter`) reads the
  endpoint's `SpeAdminEnvironmentOperation` metadata. A marked route gets the environment rule FIRST: **Write** →
  one 403 `spe.admin.deny.environment_write_requires_platform_operator` unless the caller is a platform
  operator, decided before any environment read; **Read** → the route `id` must be an environment the caller can
  read, else one 404 (`EnvironmentNotFound`, byte-identical to the handlers' not-found); reach unreadable → 503
  `spe.admin.deny.scope_unverifiable`; a Read mark on a route without `id` → the same 503 (mis-wiring, fail
  closed). Then the existing configId rule runs unchanged (environment routes carry no configId, so it passes;
  a `?configId=` smuggled onto them is still judged).
- `EnvironmentEndpoints` marks GET-by-id `Read` and POST / PUT / DELETE `Write` with
  `.WithSpeAdminEnvironmentScope(...)` (= `WithMetadata`, the endpoint-metadata pattern `ComposeSaveEndpoints` /
  `ComposeMountEndpoints` already use). The list carries no mark and trims itself (list precedent).
- `SpeAdminEnvironmentOperation` moved into `SpeAdminTenantScopeFilter.cs`; the two environment deny codes and
  `EnvironmentNotFound` are members of the existing filter class. Behaviour, codes, texts and tests are unchanged
  — the 38 `SpeAdminEnvironmentScopeTests` pass without edit except one doc comment.

### 10.3 New surface — three-question justification (CLAUDE.md §11)

| New | Existing (grep evidence) | Extension? | Cost of doing nothing |
|---|---|---|---|
| ~~`SpeAdminEnvironmentScopeFilter` class~~ | — | **removed in r2**: folded into `SpeAdminTenantScopeFilter` (round 20 item 4) | — |
| `enum SpeAdminEnvironmentOperation` (Read / Write), in the filter's file | no operation/mode type on any `/api/spe` route (grep `Operation` in `Api/Filters`: only this) | the existing filter needs to know whether a route reads one environment by `id` or writes; a bool would not name the route value it reads | without it the filter cannot tell a by-id read (404 rule) from a write (403 rule); environments stay writable by every leaf admin (round 16 item 4 unmet) |
| `SpeAdminTenantScopeFilterExtensions.WithSpeAdminEnvironmentScope` (on the EXISTING extensions class) | `WithMetadata` used for route marks in `ComposeSaveEndpoints.cs` / `ComposeMountEndpoints.cs` | a one-line named wrapper over `WithMetadata`, so the mark reads as authorization at the route | an anonymous `WithMetadata(enum)` is easy to drop unnoticed; seeds F2/F3 show the tests catch a dropped mark either way |
| `SpeAdminTenantScopeFilter.EnvironmentNotFound` + `EnvironmentNotFoundCode` / `EnvironmentWriteDeniedCode` | the config twin `ConfigNotFound` / `DenyCode` on the same class | added to the existing class, mirroring `ConfigNotFound` | the handlers' not-found and the filter's denial would differ → an existence oracle for other customers' environments (seed F4 RED) |
| `record SpeAdminEnvironmentReach` (IsPlatformOperator, LinkedEnvironmentIds; `CanRead` / `CanWrite`) | grep `Reach` / `PlatformOperator` in `Sprk.Bff.Api`: none before r1 | the list handler, the filter and the config write check share one answer; returning a bare set loses "platform operator reads all, including environments no config links" | three hand-rolled copies of the rule; any divergence lets a list show what a by-id read refuses |
| `SpeAdminTenantScope.GetEnvironmentReachAsync` (on the EXISTING scope class) | `GetAccessibleBusinessUnitsAsync`, `GetReachableConfigIdsAsync` on the same class | extends the existing boundary class; no new service, no DI registration | environment routes have no business-unit scoping (the amendment's defect stays) |
| `SpeAdminScopeDecision.EnvironmentOutOfScope` (member of the EXISTING enum) | the enum already carries `BusinessUnitOutOfScope` / `IdentityOutOfScope` | one more member, same switch in `ConfigEndpoints.WriteRefusal` | config POST/PUT may link any environment (round 16 item 4, third clause) |
| `SpeAdminTenantScope.CanonicalIdentityValue` / `CanonicalIdentityColumnValue` (internal static) | other `Canonical*` helpers in the BFF (Office/Compose/Idempotency/Communication) canonicalize unrelated values (hashes, URLs); none compares GUID spellings | added to the existing scope class next to `IdentityColumns` | `N`/`B`/`P` spellings of another unit's container type or app id pass the identity check (seed G1 RED); a client's re-send of its own GUID in another spelling is refused (seed G2 RED) |
| `DashboardEndpoints.CompletenessConcern` (internal const) | the string lived only inside `SpeDashboardSyncService` | named once where the projection drops it; the sync service is ADR-052 ratchet-listed and not touched | a leaf admin sees the tenant-wide count of skipped config records (seed D1 RED) |
| `GetReachableConfigIdsAsync` return gains `ReachesEveryConfig`; private `LoadCallerScopeAsync` / `CallerScope`; `ConfigScopeRow.EnvironmentId` | existing members, extended | modification of existing surface | the full dashboard view would be decided by the configs the aggregate happens to name (seed D3 RED); the environment rule needs the caller's root-ness and each config's environment |

No new service, DI registration, endpoint, policy, option, job, package or Dataverse column. Placement
(CLAUDE.md §10): unchanged from §3.2 — in the BFF, existing routes, existing group filter, existing scope class.

### 10.4 Tests added in r1 — one-line justifications (test-scope criterion)

- `SpeAdminEnvironmentScopeTests` (38, real host + `FakeDataverseTables`): the round 16 item 4 rule, each clause —
  list trimmed; by-id uniform 404 with no Retrieve; by-id 200 for a reachable link (×2); leaf and unresolvable
  writes one 403 with nothing read or written (×7 each); unresolvable caller lists nothing; root lists, reads and
  writes everything; reach-fault 503 on every route (×7); config POST link refused (×4 incl. unknown and
  `Guid.Empty`) / allowed (×2); PUT relink refused; PUT re-sending its own environment served (the shipped client's
  full-body save); root may link any environment; link-read fault 503. Justification: round 16 item 4 is a new
  amendment criterion, and each test is the deny/allow/fault twin of one clause.
- `Post_BorrowingAnotherUnitsGuid_InANonCanonicalSpelling_Is403` (theory), `Put_RepointingToAnotherUnitsGuid_InANonCanonicalSpelling_Is403`
  (theory), `Post_WhenTheOtherUnitStoredItsGuidNonCanonically_TheCanonicalSpellingIsStill403`: the first verifier's
  finding that `N`/`B`/`P` spellings bypassed the #74/#45 identity check — the bypass and its stored-side mirror.
- `Put_ReSendingItsOwnStoredGuid_InANonCanonicalSpelling_IsNotAChange_AndIsServed`: the over-block twin — the
  canonical compare must not refuse a client's unchanged re-send (D1).
- `Dashboard_ForALeafAdmin_WhoReachesEveryConfigTheAggregateNames_StillDoesNotSeeTheTenantWideCount`: the full view
  must be decided over the whole table, not over the names in the aggregate (a skipped config is named nowhere).
- `DashboardRefresh_ForALeafAdmin_IsProjectedExactlyLikeTheRead` and
  `DashboardRefresh_WhenTheScopeCannotBeRead_Is503_NeverTheUnprojectedAggregate`: close code-review W2 (§5.4) — the
  refresh route was asserted only through the GET.
- `AnotherConfigScopedRoute_ForAnOutOfScopeConfig_NeverReachesItsHandler`: the negative twin of the regression
  criterion's "another config-scoped route still reaches its handler" — without it that test passes even if the
  filter were detached from the route.
- `SystemAdminPolicyGroupTests.ARealAdministrator_IsAdmitted_AndReachesTheHandler` (rewrite of
  `ARealAdministrator_IsAdmitted`): proves admission by an answer only the handler produces, instead of "not
  401/403" (which a routing 404 or an unrelated 500 also satisfies).
- `SearchItemsTests` (rewritten, owner round 16 item 5): not new behaviour — the existing class moved onto the fake
  so it no longer makes a real outbound Dataverse call; the not-found case asserts the uniform 404.

No `Mock<HttpMessageHandler>`, no DI-registration test, no constructor null-check test (ADR-038). r2 added no test.

### 10.5 Seeding proofs (r2, on the final code)

Script `seed165r2.py` (session scratchpad): each seed applied as an exact one-occurrence replacement, the five
classes `SearchItemsTests | SpeAdminEnvironmentScopeTests | SpeAdminConfigAndBulkTenantScopeTests |
SystemAdminPolicyGroupTests | RecordMatchingAdminPolicyTests` (166 tests) run, then the source restored from a byte
copy in a `finally` and touched. After the run all five touched sources matched their pre-seed MD5.

| # | Seed | Failed / 166 | RED (theory rows collapsed) |
|---|---|---|---|
| E1 | filter environment write check always passes | 14 | `SpeAdminEnvironmentScopeTests.Write_ByALeafAdmin_IsOne403_AndNothingIsReadOrWritten` (×7), `…Write_ByACallerWithNoResolvableBusinessUnit_IsTheSame403` (×7) |
| E2 | filter by-id read check always passes | 2 | `…GetById_ForALeafAdmin_AnswersTheSame404ForAnUnreadableAndAnUnknownEnvironment_AndReadsNeither`, `…ACallerWithNoResolvableBusinessUnit_ListsNothing_AndGetsThe404` |
| E3 | list untrimmed (the r1 residue) | 1 | `…List_ForALeafAdmin_HoldsOnlyTheEnvironmentsTheirReachableConfigsLink` |
| E4 | config environment-link check removed | 4 | `…PostConfig_LinkingAnEnvironmentTheLeafAdminCannotRead_IsOne403_AndNothingIsCreated` (×3 non-empty ids), `…PutConfig_RelinkingToAnUnreadableEnvironment_Is403_AndNothingIsWritten` |
| E5 | reach fault → allow (filter) and → platform operator (list) | 7 | `…EnvironmentRoute_WhenTheReachCannotBeRead_Is503_AndNoEnvironmentIsReadOrWritten` (×7) |
| F1 | filter ignores the environment mark (r2 fold) | 21 | `…Write_ByALeafAdmin…` (×7), `…Write_ByACallerWithNoResolvableBusinessUnit…` (×7), `…GetById_ForALeafAdmin…`, `…ACallerWithNoResolvableBusinessUnit_ListsNothing_AndGetsThe404`, `…EnvironmentRoute_WhenTheReachCannotBeRead…` (by-id + write rows) |
| F2 | PUT environment route unmarked (= verifier M1) | 7 | `…Write_ByALeafAdmin…` (PUT rows), `…Write_ByACallerWithNoResolvableBusinessUnit…` (PUT rows), `…EnvironmentRoute_WhenTheReachCannotBeRead…` (PUT row) |
| F3 | GET-by-id environment route unmarked | 4 | `…GetById_ForALeafAdmin…`, `…ACallerWithNoResolvableBusinessUnit_ListsNothing_AndGetsThe404`, `…EnvironmentRoute_WhenTheReachCannotBeRead…` (GET-by-id rows ×2) |
| F4 | environment 404 text differs (helper) | 3 | `…GetById_ForALeafAdmin…`, `…ACallerWithNoResolvableBusinessUnit_ListsNothing_AndGetsThe404`, `…ARootAdmin_ListsAndReadsEveryEnvironment` |
| G1 | identity compare not GUID-canonical | 19 | `SpeAdminConfigAndBulkTenantScopeTests.Post_BorrowingAnotherUnitsGuid_InANonCanonicalSpelling_Is403`, `…Put_RepointingToAnotherUnitsGuid_InANonCanonicalSpelling_Is403`, `…Post_WhenTheOtherUnitStoredItsGuidNonCanonically_TheCanonicalSpellingIsStill403` |
| G2 | PUT `Changed()` raw compare | 1 | `…Put_ReSendingItsOwnStoredGuid_InANonCanonicalSpelling_IsNotAChange_AndIsServed` |
| D1 | completeness concern kept in a projection | 3 | `…Dashboard_ForALeafAdmin_ShowsOnlyTheirConfigs_WithTotalsRecomputed`, `…Dashboard_ForALeafAdmin_WhoReachesEveryConfigTheAggregateNames_StillDoesNotSeeTheTenantWideCount`, `…DashboardRefresh_ForALeafAdmin_IsProjectedExactlyLikeTheRead` |
| D2 | refresh returns the unprojected aggregate | 1 | `…DashboardRefresh_ForALeafAdmin_IsProjectedExactlyLikeTheRead` |
| D3 | full view decided by the names in the aggregate | 1 | `…Dashboard_ForALeafAdmin_WhoReachesEveryConfigTheAggregateNames_StillDoesNotSeeTheTenantWideCount` |
| SI | does-not-exist → Permitted | 4 | `SearchItemsTests.SearchItems_WithToken_ValidConfigIdNotFound_IsTheUniform404_SameAsAnOutOfScopeConfig`, `SpeAdminConfigAndBulkTenantScopeTests.Get_OutOfScopeConfig_AnswersTheSame404AsAnUnknownConfig_AndReadsNothing`, `…Bulk_WithAnOutOfScopeOrUnknownBodyConfigId_IsTheUniform404_AndNothingIsEnqueued` (×2) |

All 16 RED; none failed to compile. Not seeded, recorded: the "both marks → Write" precedence and the "Read mark
with no `id`" 503 are defensive branches no current route can reach (every marked route has exactly one mark;
the by-id route template is `{id:guid}`).

**Verifier's independent seeds (2026-10-04, recorded from the re-verification report, on the r1 code with E3
reverted):** E3, E4, E5, G1, G2, D1, D2, D3, SI all RED; and M1 (PUT env route filter removed), M2 (every resolved
caller treated as root), M3 (PUT config environment link unchecked), M4 (POST config environment link unchecked),
M5 (bulk container-type read fault treated as allow), M6 (SystemAdmin scope-substring branch re-added →
`ADelegatedScopeThatMerelyContainsAdmin_IsRefused`), M8 (record-matching raw `ex.Message`), M12 (register host
allow-list removed), M13 (write business-unit intersection removed), M15 (filter body-configId read removed) all
RED. The verifier found no guard without a biting test. (M1 is re-proven on the folded code as F2.)

### 10.6 Suites (r2, on the final code, after all seeds restored)

| Suite | Result |
|---|---|
| affected: `SpeAdminEnvironmentScopeTests` / `SpeAdminConfigAndBulkTenantScopeTests` / `SearchItemsTests` / `RecordMatchingAdminPolicyTests` + `SystemAdminPolicyGroupTests` | 38 / 93 / 7 / 28 passed, 0 failed (166) |
| full BFF unit suite `tests/unit/Sprk.Bff.Api.Tests` | **14,380 passed / 0 failed / 54 skipped (14,434)** — the three §4.2 `SearchItemsTests` failures of the committed round-1 tip are gone; no load-timeout failure this run |
| NetArchTest `tests/Spaarke.ArchTests` | 347 passed / 0 failed / 0 skipped |
| `tests/integration/Sprk.Bff.Api.IntegrationTests` (full) | 104 passed / 0 failed / 0 skipped |
| `tests/integration/Spe.Integration.Tests` (full) | 403 passed / 0 failed / 25 skipped (428) |

Run 2026-10-04 in this worktree after the seeds were restored (MD5 checked). These match the verifier's figures for
the r1 code with E3 reverted (14,379 + 1 load timeout that passed in isolation).

### 10.7 Publish size and CVE (CLAUDE.md §10)

**Measured in follow-up round f1 — §11.8.** (r2 text, kept for the record:) Not measured — the workflow instruction
for this round says skip. It is a **pre-merge** obligation for the main session: publish `origin/master` and this branch each from a fresh SHORT-path worktree (e.g. `C:\wt165m`,
`C:\wt165b`), zip both with PowerShell `Compress-Archive` (as `scripts/Deploy-BffApi.ps1`), confirm equal file
counts, report both sizes and the delta (ceiling 60 MB). No package reference changed in r1 or r2, so there is no
CVE delta (`dotnet list package --vulnerable --include-transitive` unchanged).

### 10.8 Owner round 20 items 1-3 — DONE in follow-up round f1 (§11)

Round 20 (2026-10-04) decided the Model 1 cross-customer container gap (§8 item 3). Item 4 is done here (§10.2).
Items 1-3 are **owed by task 165** and run in a dedicated follow-up round with its own verify/fix loop, scheduled
by the main session immediately after this workflow (main-session answer recorded in its coordination note):

1. Stamp every SPE container with its owning business unit at creation (a `fileStorageContainer` custom property,
   e.g. `spaarkeBusinessUnitId`, written by the BFF's container-creation path), plus a backfill script (dry run /
   `-Apply` / `-Verify`; `-Apply` is a main-session manual gate) that stamps existing containers from the
   authoritative records that created them and lists any whose owner cannot be derived.
2. The container, item, permission and bulk routes authorize PER CONTAINER: a leaf-unit admin only containers
   bound to its own unit or a descendant; an unbound container only a ROOT-unit admin; an unreadable binding fails
   closed (uniform 404/403 per this task's contract).
3. Narrow the app-identity duplicate check so configs of different customers may share a `containerTypeId` /
   owning app (Model 1) once isolation is per container (supersedes D5b).
   Tests + seeds per part: cross-customer container refused; own subtree allowed; unbound → root only; binding read
   fault → closed.

### 10.9 Items for the main session to file — WITHDRAWN: both are done in f1 (§11.3 bulk status, §11.5 storage). Do NOT file them.

```
gh issue create --title "SPE admin dashboard: per-config storage split (storageUsedByConfig) when SpeDashboardSyncService moves to IScheduledJob" --label backlog --body "From unified-access-control-r2 task 165 note §8 item 4 / D4. A leaf admin's projected dashboard reports storage as not reported because the cached aggregate holds only a cross-config sum. Add storageUsedByConfig to SpeDashboardSyncService.DashboardMetrics when the service migrates to IScheduledJob (ADR-052 §1)."
gh issue create --title "SPE admin: GET /api/spe/bulk/{operationId}/status is not bound to the caller who started the operation" --label backlog --body "From unified-access-control-r2 task 165 note §8 item 5 (low; random GUID, ~30 min retention). Bind the status read to the starting caller (or its business unit), uniform 404 otherwise. May be absorbed by task 165's round-20 per-container follow-up round (bulk routes)."
```

§8 item 6 (`ClaimOnlyFilters` description) is task 167's.

### 10.10 Verified facts carried into the record

- **Client consequence (round 16 item 4, verifier finding 9).** `ContainerTypeConfig.tsx:522` requires an
  environment, and a leaf admin may link only an environment already linked by a config they reach, so a leaf admin
  with NO config cannot create their first config: a root operator (or provisioning) does it. This is the
  consequence stated in option (a) of §4.1, which round 16 item 4 chose; no client change. `EnvironmentConfig.tsx`
  surfaces the write 403 through `describeApiError`; `speApiClient`'s permission-banner classifier keys only on
  `role_insufficient`, `entra_role_required` and `unauthenticated`, so the new codes do not mis-trigger it.
- **Route deletion: none** (round 10 item 1), confirmed by the verifier: no OpenAPI / plugin manifest publishes
  these routes (the only manifests are `knowledge/` samples), but the record-matching routes are the only trigger of
  `IDataverseIndexSyncService` and a documented operator procedure (ai-spaarke-platform-enhancments-r3 task 003),
  and the config / bulk / environment routes are called by `speApiClient.ts`.
- **/conflict-check (r2, before editing `SpeAdminTenantScopeFilter.cs` / `EnvironmentEndpoints.cs`):** no open PR
  touches any SpeAdmin filter, endpoint or scope file (`gh pr list --state open` over changed paths: none); in the
  last 45 days only this task's branches and older, merged commits touched them. sdap-SPE-admin-app-r3's seeded
  file `SpeAdminGraphService.cs` is not edited. Clean.
- **Task 167:** still not landed on `work/unified-access-control-r2`; amendment 3 applies (no GovernedFiles /
  waiver edits). The five environment rows are in §6. Merge of this branch into the work branch: to be re-checked by
  the main session at integration (the verifier found `task/uac-r2-165-r1` merge-tree clean).
- `.claude/**`: no edit needed.

---

## 11. Follow-up round f1 — owner round 20 items 1-3, round 25 item 5, verifier items 4-7 (2026-10-04)

### 11.0 How the round reached the branch

- Branch **`task/uac-r2-165-f1`** from `task/uac-r2-165-r2` @ `87d098ba4` (the name was free). No push, PR or merge.
- `NOTE-FROM-MAIN.md` (binding) read first: it points at owner rounds 15/16/19/21 on `work/unified-access-control-r2`.
  This round's scope is round 20 items 1-3 and round 25 item 5 (both binding, both read from the same file). The
  work branch now also carries rounds 26-29; none changes a 165 item. **Round 25 item 6 (task 166) consumes this
  round's stamp**: "a ROOT-owned document's accepted containers are only those stamped by the root unit itself" — 166
  reads it through `SpeContainerBusinessUnitStamp.Read` / `PropertyName` (= `spaarkeBusinessUnitId`), never a second
  copy of the name. `NOTE-FROM-MAIN.md` is not committed.
- `/conflict-check`: none of the 21 open PRs touches a changed path; `git merge-tree` of this branch into
  `work/unified-access-control-r2` @ `73296ce14` is clean.

### 11.1 Read-only live facts (dev, 2026-10-04)

| Fact | Value |
|---|---|
| Where Graph returns `customProperties` | **only** on a single-container `GET …/containers/{id}?$select=id,containerTypeId,customProperties`. On the containers **collection** `$select=customProperties` is accepted, echoed in `@odata.context`, and dropped from every row (beta and v1.0; 5 containers of type `8a6ce34c…`, read app-only as owning app `170c98e1…`, secret read from the Key Vault and never printed). So every binding decision is a per-container GET — a list read would report every container as unbound. |
| Backfill **dry run** (`scripts/Backfill-SpeContainerBusinessUnitStamp.ps1`, no `-Apply`) | 5 containers: **ToStamp 2** — "Spaarke Inc" `b!yLRd…` → Spaarke Demo (`businessunit.sprk_containerid`), "Spaarke Dev Container 2" `b!vzGD…` → **Spaarke (root)** (claimed by both root and Business Unit 1 → nearest common ancestor); **Underivable 3** — "API Test 2025-09-30 14:43:59" `b!DcvT…`, "Full Flow Test 2025-09-30 14:51:26" `b!rAta…`, "Test New Container 8-20-2026" `b!c8YR…` (no authoritative record claims them; they stay unbound = root-unit admins only); AlreadyStamped / Mismatch / Foreign / Malformed / Unreadable 0. |
| Backfill **-Verify** today | exit **1** — ToStamp 2, and **ConfigsSkipped 1**: config `68f9a952…` ("Spaarke SPE Model 1 Owner", type `fb3817a8…`, owning app `bfac7f6e…`) stores `sprk_keyvaultsecretname` = the literal `"null"` (the §2 oddity), so neither the BFF nor the script can list its containers. In the vault `sprk-prod-kv` the only secret whose name matches `spe`/`owning`/`container`/`sharepoint`/`model`/`bfac` is `spe-owning-app-secret` (names listed, no value read). |

The dry run found a fail-open in the script's own `-Verify`: a config whose containers could not be listed was only
logged, so `-Verify` could pass without having seen them. Fixed in this round: such a config is counted
(`ConfigsSkipped`), listed as `SKIPPED-CONFIG … its containers were NOT examined`, and fails `-Verify`.

### 11.2 Item 1 — every container is stamped with its owning business unit at creation (round 20 item 1)

- **The binding** is ONE class, `Services/SpeAdmin/SpeContainerBusinessUnitStamp.cs`: property
  **`spaarkeBusinessUnitId`**, value the canonical `D` GUID, `isSearchable: false`. `Read(customProperties)` →
  `SpeContainerBinding` = **bound** (one non-empty GUID; name matched trimmed and case-insensitively), **unbound** (no
  stamp) or **malformed** (not one GUID, or two spellings of the name carrying different units — read as unbound, so
  only a root admin reaches it). `IsReserved(name)` names it for the custom-property route.
- **Every BFF creation path stamps, reads back, and removes the container if the stamp did not land** (never an
  unbound container left behind silently):
  - SPE admin plane, `POST /api/spe/containers` → `SpeAdminGraphService.CreateContainerAsync(…, owningBusinessUnitId)`:
    create, `PATCH …/containers/{id}/customProperties` (the property map is the body ROOT — Graph's merge semantics),
    single-container read-back; on failure soft-delete and throw `ContainerBindingException` (removed → **503**; removal
    also failed → **500** naming the container id; both `errorCode` `spe.containers.business_unit_stamp_failed`).
    `Guid.Empty` is refused before any call. The owner is `SpeAdminTenantScope.ResolveContainerOwnerAsync`: the
    config's own unit; for a unit-less config (compatibility rule) the creating admin's own unit; neither → **403**
    `spe.admin.deny.container_owner_unresolved`, nothing created; a read fault → **503**, nothing created. The audit row
    records the unit.
  - Secure-record provisioning, `ProvisionProjectEndpoint` → `SpeFileStore.CreateContainerAsync(…, owningBusinessUnitId)`
    → `ContainerOperations.CreateContainerAsync`: stamped with the **Secure Record unit** the record is isolated into
    (`secureBuId`), same read-back, on failure `DELETE` + throw (provisioning's existing failure path answers).
  - **CORRECTED in f2 (§12.2):** this round's claim that no other path creates a container held for the BFF only. Three
    in-repo paths outside the BFF created UNSTAMPED containers — the L2 H8 handler, `scripts/New-BusinessUnitContainer.ps1`
    (the documented onboarding step) and `scripts/Provision-Customer.ps1` — plus `scripts/Create-NewContainerType.ps1
    -CreateTestContainer`. All four stamp now, and the guard scans all of `src/` and `scripts/`.
  - No other BFF path creates a container: `SpeAdminContainerBindingGuardTests.EveryContainerCreationPath_BindsTheNewContainer`
    scans `src/server/api/Sprk.Bff.Api` for every container `POST` and requires the binding call in the same method
    (seeds S1/S2).
- **Server-owned**: `PUT /api/spe/containers/{containerId}/customproperties` refuses to set or change the stamp —
  **403** `spe.admin.deny.container_binding_server_owned`, nothing patched, root admins included (stamping an unbound
  container is the backfill's job, from records). The shipped `CustomPropertyEditor` re-sends the FULL map on save, so
  an **unchanged** stamp in the body is accepted and stripped from the write (never rewritten) — refusing it would have
  broken every custom-property save on a stamped container (D9).
- **Backfill** `scripts/Backfill-SpeContainerBusinessUnitStamp.ps1` (dry run default / `-Apply` / `-Verify` /
  `-RevertManifest` / `-MaxWritesPerRun` / `-WhatIf` / `-ConfigId`). Owner derived ONLY from authoritative records:
  (1) `businessunit.sprk_containerid`; (2) a SECURE root's own `sprk_containerid` (`sprk_matter` / `sprk_project` /
  `sprk_workassignment`, `sprk_issecure = true`) → the root's owning unit (what provisioning stamps); (3) an
  `sprk_speauditlog` `CreateContainer` 201 row → the row's unit, else its config's. Several claimants → their nearest
  common ancestor; a claimant outside the hierarchy → underivable. `sprk_defaultcontainerid` is NOT a source (typed by
  an admin). Never overwrites a stamp: MISMATCH / FOREIGN / MALFORMED are LISTED. Write-ahead reversal manifest,
  read-back per write, idempotent. `-Apply` is **not run** — manual gate §11.16 (h).

### 11.3 Items 1(2) and 3 — per-container authorization (round 20 item 2)

**The rule** (`SpeAdminCallerScope.CanReach`, one place): a container bound to the caller's own unit or a descendant;
an **unbound or malformed** container only for a **root-unit admin** (platform operator); a binding that cannot be
READ fails closed (**503** `spe.admin.deny.scope_unverifiable`, nothing acted on). A container Graph does not find,
one whose reported type is not the config's, and one bound outside the caller's reach answer **ONE 404**
(`spe.admin.deny.container_out_of_scope`, "Container '{id}' was not found." — the recycle-bin routes "… was not found
in the recycle bin.", same code); the handlers' own not-found paths use the same helpers
(`SpeAdminTenantScopeFilter.ContainerNotFound` / `RecycleBinContainerNotFound`), so the filter's denial and a container
that vanishes between filter and handler are byte-identical apart from the trace id.

**Where it is decided**: in the EXISTING `/api/spe` group filter `SpeAdminTenantScopeFilter`, after the configId rule
(ADR-008: the container a request NAMES is decided in the one filter). The container is the route value
`{containerId}` or a bound body implementing the new marker `ISpeAdminContainerScopedRequest` (`SearchItemsRequest`);
route and body naming different containers → **400** `spe.admin.deny.container_id_ambiguous` before any read. The
binding is read with the config's own client; routes acting on a soft-deleted container are marked
`.WithSpeAdminContainerLocation(SpeAdminContainerLocation.RecycleBin)` and read `deletedContainers/{id}`. A route that
names a container but no usable configId never reaches a container: the handler's own 400 answers first (tested).
The nine `ContainerItemEndpoints` routes were `{id}`; they are now `{containerId}` (URLs unchanged) so the filter reads
them — **the 167 waiver keys move** (§11.12).

**Routes** (31 = 29 active + 2 recycle-bin, every one in `SpeAdminPerContainerScopeTests`' route tables, census-checked against the mapped
endpoints): containers GET-by-id / PATCH / activate / lock / unlock / archive / unarchive; custom properties GET / PUT;
columns GET / POST / PATCH / DELETE; permissions GET / POST / PATCH / DELETE; items list / versions / thumbnails / share
/ content / preview / DELETE / folders / upload; recycle-bin restore / permanent delete (deleted location); container
recycle-bin items list / restore / delete; `POST /search/items` with a body `containerId`.

**Lists and searches are trimmed** with the same rule (`SpeAdminTenantScope.TrimToReachableContainersAsync`, bounded
concurrency 8): `GET /containers`, `GET /recyclebin` (deleted location), `POST /search/containers` (`totalCount` is
null unless a platform operator saw nothing trimmed — a total would count other customers' containers), `POST
/search/items` (hits whose container the caller cannot reach are dropped; an item's container is `containerId` or, when
Graph omits it, `parentReference.driveId`). Any binding read fault → 503, never the untrimmed list.

**Bulk** (`BulkOperationService`): the caller's scope is captured at enqueue (unreadable → **503**, nothing
enqueued) and EVERY item is checked inside the job before its Graph write — type reported AND equal to the config's,
AND `DecideContainer` permits under the captured scope (`IsContainerInScopeAsync`); otherwise ONE refusal text and no
write. `GET /bulk/{operationId}/status` is bound to the caller who started it (`CallerResolution.ResolveObjectId`);
anyone else gets the unknown-id 404 (closes §8 item 5).

**App-only container-TYPE routes** — the consequence of item 3 (Model 1 configs now share a type and owning app, and
these routes act app-only AS that shared owning app on settings every customer of the type shares; a consuming-app
registration grants an app every container of the type): marked `.WithSpeAdminContainerTypeScope(Read|Write)` —
`GET /containertypes/{typeId}/permissions` (Read), `GET|POST /containertypes/{typeId}/consumers`,
`PUT|DELETE …/consumers/{appId}`, `POST /containertypes/{typeId}/register` (Write). Rule
(`SpeAdminTenantScope.DecideContainerTypeAccessAsync`): the route's `typeId` must BE the config's type (else **404**
`spe.admin.deny.container_type_out_of_scope`); a Write needs the caller to reach EVERY config carrying the type (else
**403** `spe.admin.deny.container_type_shared`); read fault or a full 5000-row page → 503. The five `{typeId}` routes
that call Graph with the CALLER's delegated token (`GET /containertypes/{typeId}`, `PUT …/settings`, `GET|POST|DELETE
…/owners`) are decided by Graph against the caller's own Entra role and are listed by name in the guard's allow-list
(D14).

### 11.4 Item 1(3) — the app-identity duplicate check narrowed (round 20 item 3; supersedes D5b)

Configs of different customers may now share `containerTypeId`, `owningAppId` and `keyVaultSecretName` (the owning
app's secret). Only the **per-customer** values stay exclusive: a config may not name, as ANY app-id field, another
unreachable unit's `consumingAppId`, nor as any secret field its `consumingAppKvSecret` (`ConfigScopeRow.ExclusiveValuesOfKind`;
kind grouping across columns, D3, kept for the exclusive values). Canonical GUID compare (r1) unchanged. Sharing is
safe only because containers are now authorized per container (§11.3) and type-level writes need every customer of
the type (§11.3, type routes).

### 11.5 Round 25 item 5 + verifier item 2 — per-config dashboard storage; `SpeDashboardSyncService` → `IScheduledJob`

- **Migration (ADR-052 §1 / ADR-036)**: `SpeDashboardSyncService` is an `IScheduledJob` (`spe-dashboard-sync`) on the
  existing `ScheduledJobHost`, registered `AddScheduledJob<SpeDashboardSyncService>(BuildCronSchedule(options))` — cron
  from `SpeAdmin:DashboardSyncIntervalMinutes` (default 15; one run per schedule across instances through the host's
  lease). The `AddSingleton` + `AddHostedService` pair, the `PeriodicTimer` loop and the refresh `Channel` are gone.
  `POST /dashboard/refresh` → `ScheduledJobHost.TriggerNowAsync` (busy → waits for the running sync), then
  `WaitForMetricsNewerThanAsync` (≤30 s). `GET /dashboard/metrics` with nothing cached starts a run (the job no longer
  runs at startup) and answers 204. `WorkloadPlacementGuardTests` timer allow-list: entry removed, baseline **13 → 12**
  (the ratchet only shrinks). `docs/architecture/background-workers-architecture.md` updated.
- **Attribution** (`AttributeContainer`, pure static): each container goes to the config of its type whose unit is the
  container's unit or its NEAREST ancestor (ties: lowest config id, deterministic); a unit-less config never receives a
  bound container; unbound / malformed → **unattributed**; a unit the hierarchy does not hold → excluded; binding
  unreadable → excluded + per-config concern "Container business-unit bindings (config X)"; hierarchy unreadable → only
  exact-unit matches + a hierarchy concern. Each container is counted ONCE (before, two configs of one type each
  counted all of it).
- **Metrics**: `StorageUsedInBytesByConfig`, `StorageReportingContainerCountByConfig`, `UnattributedContainerCount`,
  `UnattributedStorageUsedInBytes`, `UnattributedStorageReportingContainerCount` (existing fields kept; totals = per-config
  sums + unattributed). **Projection** (`DashboardEndpoints.ProjectToReachableConfigs`): the unchanged aggregate only for a
  platform operator who reaches every config; anyone else gets their configs' counts AND storage (a leaf admin now sees
  its own storage, not 0 — D4 retired) with unattributed zeroed and the completeness concern kept only when they reach
  every config.

### 11.6 Verifier items 5 and 6 — the two unproven guards now have tests + seeds

- `GetReachableConfigIdsAsync` full-page guard: `SpeAdminDashboardScopeTests.WhenTheConfigReadMayBeTruncated_TheDashboardIs503_NeverTheTenantWideFigures`
  (5000 config rows → 503 on both dashboard routes, the tenant-wide figures never returned). Seed **S24** (guard
  removed) RED.
- Handler-side uniform-404 wiring: `SpeAdminConfigAndBulkTenantScopeTests.AConfigGoneBetweenTheFilterAndTheHandler_GetsTheFiltersExact404`
  (theory: GET with a null Retrieve and with a Web API 404, PUT, DELETE — the config passes the filter, vanishes, and the
  handler's 404 must equal the filter's byte-for-byte apart from the trace id) — seed **S25** RED; the container twins
  `SpeAdminPerContainerScopeTests.AContainerGoneBetweenTheFilterAndTheHandler_GetsTheFiltersExact404` /
  `…ADeletedContainerGoneBetweenTheFilterAndTheRestore…` — seed **S26** RED.

### 11.7 Verifier item 7 — the body-marker guard widened

`SpeAdminConfigScopedBodyGuardTests` now scans every complex parameter of every `IResult`-returning handler in
`Sprk.Bff.Api.Api.SpeAdmin` (so a record nested in an endpoint class, e.g. `SearchItemsEndpoints.SearchItemsRequest`, is
reached) plus the public `*Request` types of `Models.SpeAdmin`: a `ConfigId` member requires `ISpeAdminConfigScopedRequest`,
a `ContainerId` member requires `ISpeAdminContainerScopedRequest`. `TheScan_ReachesRecordsNestedInAnEndpointClass` makes
it non-vacuous. Seeds **S13** (nested `SearchItemsRequest` loses the container marker) and **S31** (scan back to the
models namespace only) RED.

### 11.8 Item 4 — publish size (CLAUDE.md §10, hazards 1-4)

Each side exported with `git archive` into a SHORT path, restored and published `-c Release` exactly as
`scripts/Deploy-BffApi.ps1`, zipped with PowerShell **`Compress-Archive -CompressionLevel Optimal`** over
`deploy/api-publish/*`, PDBs included:

| Side | Commit | Path | Zip | Files | MSB3030 |
|---|---|---|---|---|---|
| fresh `origin/master` (fetched 2026-10-04) | `e7f1129ec` | `C:\wt165m` | **45.65 MB** | 212 | 0 |
| task base (`task/uac-r2-165-r2`) | `87d098ba4` | `C:\wt165p` | **45.67 MB** | 212 | 0 |
| this branch | `2d6e4c4c0` (all `src/` of the final commit; later commits touch only docs, scripts, the note and the POML) | `C:\wt165b` | **45.70 MB** | 212 | 0 |

Exact bytes: master 47,871,261 · base 47,887,240 · branch 47,915,269. Delta vs fresh master **+0.04 MB** (+44,008 B);
this round's own contribution (branch − base) **+0.03 MB** (+28,029 B). Ceiling 60 MB. Equal file counts (212) on all
three sides, no MSB3030, so all three publishes are complete. No `PackageReference` changed (no CVE delta).

### 11.9 Placement (CLAUDE.md §10) and new surface (CLAUDE.md §11)

**Placement: in the BFF**, on existing surfaces: the existing `/api/spe` group filter, the existing `SpeAdminTenantScope`
boundary class, the existing `SpeAdminGraphService` / `ContainerOperations` creation paths, the existing
`BulkOperationService`, the existing dashboard routes, and the existing `ScheduledJobHost` (ADR-036 — the migration
ADR-052 §1 requires). No new endpoint, filter class, policy, service class registered in DI, option, package, Dataverse
column or plugin (ADR-002). The binding lives on the SPE container itself (Graph custom property) because the container
is the thing authorized and Graph is the only store every creation path already writes; a Dataverse column would need a
container→row map that does not exist (§4 trigger 4: `businessunit.sprk_containerid` is not unique). No AI type touched
(ADR-013).

| New | Existing (grep evidence) | Extension? | Cost of doing nothing |
|---|---|---|---|
| `SpeContainerBusinessUnitStamp` (static) + `SpeContainerBinding` (record struct) + `SpeContainerBindingRead` (record) | grep `spaarkeBusinessUnitId` / `BusinessUnitId` in `Services/SpeAdmin` + `Infrastructure/Graph`: no container→unit binding anywhere; `CustomPropertyDto` exists (reused) | one owner of the name + parse rule, read by the filter, trims, bulk, dashboard, creation paths and 166 — a constant alone leaves five parse copies | round 20's defect stays: a type-bound route lets one customer's admin reach another's containers |
| `ISpeAdminContainerScopedRequest` (marker) | `ISpeAdminConfigScopedRequest` (same file, same pattern) | sibling of the existing marker; the filter reads it | a body-named container (search items) bypasses the per-container rule (seed S13) |
| `SpeAdminCallerScope` (public record, `CanReach`, `Nobody`), replacing the private `CallerScope` | private `CallerScope` in the same class | promoted so the filter, the job and the dashboard share ONE rule | three copies of "unbound → root only" (bulk seed S5 shows the rule is load-bearing in each) |
| `SpeAdminConfigReach` (record; replaces the tuple return of `GetReachableConfigIdsAsync`) | the r1 tuple | adds `IsPlatformOperator`, which the dashboard needs for unattributed containers | a leaf admin who reaches every config would see unbound containers (seed S29) |
| `SpeAdminContainerTrim` (record) | none for container lists | the trim's answer carries `IsPlatformOperator` for `totalCount` | search would report a total that counts other customers' containers |
| `SpeAdminTenantScope.DecideContainerAccessAsync`, `DecideContainer` (internal static), `FilterReachableContainersAsync`, `TrimToReachableContainersAsync`, `ResolveContainerOwnerAsync`, `DecideContainerTypeAccessAsync`, `SameGuid` (internal), `LoadBusinessUnitHierarchyAsync` (now internal static), `GetCallerScopeAsync` (public) | `DecideConfigAccessAsync`, `GetReachableConfigIdsAsync` on the same class | extends the existing boundary class; no new service | no per-container decision (round 20 item 2 unmet) |
| `SpeAdminScopeDecision.ContainerTypeShared` (member) | the enum's existing refusal members | one more member | type-level writes on a shared Model 1 type by one customer (seed S20) |
| `SpeAdminContainerLocation` / `SpeAdminContainerTypeOperation` enums + `WithSpeAdminContainerLocation` / `WithSpeAdminContainerTypeScope` (existing extensions class) | `SpeAdminEnvironmentOperation` + `WithSpeAdminEnvironmentScope` (r2) | same endpoint-metadata pattern | recycle-bin routes would read the active container (always 404, seed S8); type routes unguarded (seed S21) |
| Filter helpers `ContainerNotFound`, `RecycleBinContainerNotFound`, `ContainerTypeNotFound` + codes `container_out_of_scope`, `container_id_ambiguous`, `container_type_out_of_scope`, `container_type_shared` | `ConfigNotFound` / `EnvironmentNotFound` | members of the existing filter class | existence oracle between the filter's 404 and the handlers' (seed S26) |
| Codes `spe.admin.deny.container_owner_unresolved`, `spe.admin.deny.container_binding_server_owned`, `spe.containers.business_unit_stamp_failed` | existing deny/error codes in the same handlers | constants on the existing endpoint classes | an unbound create or an admin re-binding a container into another customer's view (seeds S14/S15/S32) |
| `SpeAdminGraphService.ContainerBindingException`, `GetContainerBindingAsync` / `ReadContainerBindingAsync` / `GetContainerBindingForConfigAsync`, `WriteBusinessUnitStampAsync`, `BindNewContainerAsync`, `ReadCustomProperties` (extracted) | `GetContainerAsync`, `UpdateCustomPropertiesAsync` in the same class | the existing Graph class; the list read cannot carry the stamp (§11.1) | creation could not stamp or prove the stamp; the per-container rule could not read it |
| `SpeAdminGraphService.UseClientForConfig` (internal test seam) | `ResolveConfigAsync`'s client cache | lets the real class run against WireMock (ADR-038: no `Mock<HttpMessageHandler>`) | the stamp PATCH body shape and the soft-delete-on-failure path would be untestable |
| `ContainerOperations.CreateContainerAsync` / `SpeFileStore.CreateContainerAsync` gain `owningBusinessUnitId` (required) | the existing methods | parameter added; one caller (`ProvisionProjectEndpoint`) | provisioning creates unbound containers (seed S3) |
| `BulkOperationService` `IsContainerInScopeAsync`, `…InScopeAsync` per-item methods (renamed), `ContainerNotInScopeError`, job `Scope`, `StartedBy` | r1's per-item type methods | extended in place | bulk delete/grant on another customer's container of the shared type (seeds S16/S17); status readable by anyone holding the GUID (seed S18) |
| Dashboard metrics fields (5), `AttributeContainer` + `AttributableConfig` / `ContainerAttribution` / `ContainerAttributionKind`, `BuildCronSchedule`, `WaitForMetricsNewerThanAsync`, `JobIdConstant` | the r1 aggregate; ADR-036 `IScheduledJob` | extended in place; migration required by ADR-052 §1 | leaf admins see 0 storage (D4); double counting across configs of one type (seed S27) |
| `AddScheduledJob<SpeDashboardSyncService>` (replaces `AddSingleton` + `AddHostedService`) | `AddScheduledJob` used by the other ADR-036 jobs | the existing scheduling host | the ratchet-listed timer stays (round 25 item 5) |
| `scripts/Backfill-SpeContainerBusinessUnitStamp.ps1` | the dry-run / `-Apply` / `-Verify` / reversal-manifest pattern of the existing backfill scripts (e.g. task 148's `Invoke-SecureChildBackfill.ps1`) | a new script is the only way to stamp existing containers (Graph data, no BFF route should write a stamp) | existing containers stay unbound = invisible to their own leaf admins |
| Test fixtures: `AdminSurfaceHostFixture` Graph WireMock, `GraphWireMockFixture.StubGetExact` / `StubPostExact` / `Reset` | the existing fixtures | extended | prefix stubs collided between collection and item paths |

`.claude/**`: no edit needed (ADR-052 names the ratchet test, not a count).

### 11.10 Tests added this round — one-line justifications (test-scope criterion)

All through the real host unless stated; no `Mock<HttpMessageHandler>`, no DI-registration test, no constructor
null-check test (ADR-038). Counts from the final run (§11.12).

- `SpeAdminPerContainerScopeTests` (226 incl. theory rows; real host + `FakeDataverseTables` + WireMock Graph):
  round 20 item 2 clause by clause — foreign container 404 with the handler never run (active and deleted), own unit
  and descendant reach the handler, unbound → leaf 404 / root reaches, malformed → 404, a unit unknown to the
  environment → 404 even for root, absent and other-type answer exactly like foreign, binding fault 503 on every route,
  scope fault 503 with no Graph call, no-configId 400 with nothing read, the two race twins (§11.6), search-items body
  container 404, route/body disagreement 400, list/recycle-bin/search trims (leaf, root, fault), custom-property stamp
  (refused, refused on unbound for root, unchanged re-send saved without rewrite), create stamps + reads back /
  stamp failure removes + 503 / owner unresolvable 403 / owner read fault 503, type rule (not the config's type 404,
  shared write 403, own read, root write, sole-owner write, fault 503), and the route census (every mapped container
  route is in the table). The three fault tests go through the unit-less Config N so that only the branch under test can
  answer (§11.11).
- `SpeAdminDashboardScopeTests` (10): round 25 item 5 — leaf sees its own storage (not 0); operator sees the
  aggregate incl. unattributed; leaf who reaches every config sees completeness but never unattributed; truncation 503
  (§11.6); scope fault 503 on both routes; the REAL job run through `TriggerNowAsync` (leaf subtree; operator counts
  each container once, unbound unattributed, no foreign; binding fault counted for nobody + concern); first empty view
  starts a sync. Replaces the six dashboard tests moved out of `SpeAdminConfigAndBulkTenantScopeTests`.
- `SpeAdminBulkPerContainerTests` (4): the bulk routes end-to-end — leaf deletes its own, refuses foreign and
  unbound; grants only on its own; scope fault 503 nothing enqueued (through a unit-less config, §11.11); status only
  for the starter.
- `BulkOperationContainerTypeTests` (15, rewritten; real `SpeAdminGraphService` + WireMock): per item —
  other type / foreign same-type / unbound-or-malformed by leaf refused with no write; unbound by root sent; read fault
  refused with the same text; case-differing type sent; the binding read asks for type + customProperties; one call
  site per Graph write.
- `ContainerBindingRuleTests` (21, pure domain): the stamp parse rules, `CanReach`, `DecideContainer`, and the
  dashboard attribution rules (nearest ancestor, root vs unit-less, type match, unattributed, excluded, no hierarchy,
  deterministic tie, cyclic hierarchy terminates).
- `ContainerCreationStampContractTests` (9, contract, WireMock): the exact Graph wire shape — stamp PATCH body
  root, read-back, soft-delete / DELETE on failure for both creation paths, `Guid.Empty` refused, deleted-container
  read from `deletedContainers`, null on 404 / throw on 500.
- `SpeAdminContainerBindingGuardTests` (6, ArchTests): every creation path binds; every app-only type route is marked
  (allow-list of the five delegated routes, stale entries fail); the backfill script names the BFF's property and
  sources; each analyser bites on a seeded sample.
- `SpeAdminConfigScopedBodyGuardTests` (3, ArchTests, widened — §11.7).
- `SpeAdminConfigAndBulkTenantScopeTests`: narrowing theories rewritten (per-customer identity 403 on POST/PUT; shared
  type / owning app / its secret served on POST/PUT — Model 1), and the handler-404 theory (§11.6).
- `SecureNamedOwnerTeamProvisioningTests`: asserts each provisioned container is created with the Secure Record unit.
- `SpeAdminContainerItemRouteGateTests`: `{id}` → `{containerId}` in its expected template (no new test).

### 11.11 Seeding proofs (on the final code; scripts `seed165f1.py`, `seed165f1b.py`, `seed165f1c.py`, session scratchpad)

Each seed: an exact one-occurrence replacement, the named classes run, the source restored from a byte copy,
MD5-checked and touched; the working tree showed no `src/` or `tests/` change after every pass. **All 40 RED; none failed
to compile.** Three passes: (1) S1-S35; (2) the four seeds whose first form was a constant `if (false)` — `CS0162` under
TreatWarningsAsErrors, so the first run built nothing and proved nothing (S7, S14, S20, S35) — re-seeded with a
compiling never-true condition, plus the ArchTests half of S1, S2, S13, S21, S31 re-run with a failure parser that reads
display-named facts (the first parser missed them); (3) S36-S40, the read-fault branches. Pass 3 found that three
fault tests could not reach the branch they named: with a config that HAS a business unit, the configId rule reads the
hierarchy first and answers the 503 itself. Those tests (`WhenTheCallersScopeCannotBeRead_AContainerRouteIs503…`,
`ATypeRoute_WhenTheScopeCannotBeRead_Is503_AndNothingIsSent`, `BulkDelete_WhenTheCallersScopeCannotBeRead…`) now go
through a UNIT-LESS config, which the configId rule admits without the hierarchy — so only the branch under test can
answer — and the create path gained `CreateContainer_WhoseOwnerCannotBeRead_Is503_AndCreatesNothing`. Passes 1-2 ran
before that change; none of their RED tests was edited by it.

| # | Seed | Failed | RED (theory rows collapsed) |
|---|---|---|---|
| S1 | admin-plane create does not stamp (`BindNewContainerAsync` call removed) | 8 | `ContainerCreationStampContractTests.AdminPlaneCreate_StampsTheUnit_AsTheBodyRootOfACustomPropertiesPatch_AndReadsItBack`, `ContainerCreationStampContractTests.AdminPlaneCreate_WhoseStampDidNotLand_SoftDeletesTheContainer_AndThrows` (×2), `Every SPE container the BFF creates is bound to its business unit in the creating method`, `SpeAdminPerContainerScopeTests.CreateContainer_StampsTheOwningBusinessUnit_AndReadsItBack` (×2), `SpeAdminPerContainerScopeTests.CreateContainer_WhoseStampCannotBeWritten_IsRemovedAgain_And503` (×2) |
| S2 | provisioning create does not stamp | 3 | `ContainerCreationStampContractTests.ProvisioningCreate_StampsTheUnit_AndReadsItBack`, `ContainerCreationStampContractTests.ProvisioningCreate_WhoseStampDidNotLand_DeletesTheContainer_AndThrows`, `Every SPE container the BFF creates is bound to its business unit in the creating method` |
| S3 | provisioning stamps a random unit instead of the Secure Record unit | 1 | `SecureNamedOwnerTeamProvisioningTests.Provision_EachRootType_EndsOwnedByTheNamedTeam_SharedToTheCreator_WithItsOwnContainer` |
| S4 | filter skips the per-container rule | 14 | `SpeAdminPerContainerScopeTests.AContainerGoneBetweenTheFilterAndTheHandler_GetsTheFiltersExact404`, `SpeAdminPerContainerScopeTests.AContainerThatDoesNotExist_AndOneOfAnotherType_AnswerExactlyLikeAnotherCustomersContainer`, `SpeAdminPerContainerScopeTests.ADeletedContainerGoneBetweenTheFilterAndTheRestore_GetsTheFiltersExact404`, `SpeAdminPerContainerScopeTests.ALeafAdmin_OnAContainerWithAMalformedStamp_GetsTheUniform404`, `SpeAdminPerContainerScopeTests.ALeafAdmin_OnADescendantUnitsContainer_ReachesTheHandler`, `SpeAdminPerContainerScopeTests.ALeafAdmin_OnAnUnboundContainer_GetsTheUniform404`, `SpeAdminPerContainerScopeTests.ALeafAdmin_OnAnotherCustomersContainerOfTheSharedType_GetsTheUniform404_AndTheHandlerNeverRuns`, `SpeAdminPerContainerScopeTests.ALeafAdmin_OnItsOwnDeletedContainer_ReachesTheHandler`, `SpeAdminPerContainerScopeTests.ALeafAdmin_OnItsOwnUnitsContainer_ReachesTheHandler`, `SpeAdminPerContainerScopeTests.ARequestWhoseRouteAndBodyNameDifferentContainers_Is400_BeforeAnyContainerIsRead`, `SpeAdminPerContainerScopeTests.ARootAdmin_OnAContainerBoundToAUnitThisEnvironmentDoesNotKnow_GetsTheUniform404`, `SpeAdminPerContainerScopeTests.ARootAdmin_OnAnUnboundContainer_ReachesTheHandler`, `SpeAdminPerContainerScopeTests.SearchItems_ScopedToAnotherCustomersContainer_IsTheUniform404_AndNoSearchRuns`, `SpeAdminPerContainerScopeTests.WhenTheContainersBindingCannotBeRead_EveryRouteIs503_AndNothingIsActedOn` |
| S5 | unbound container reachable by everyone (`CanReach`) | 7 | `BulkOperationContainerTypeTests.Delete_OfAnUnboundOrMalformedContainer_ByALeafAdmin_IsRefused`, `ContainerBindingRuleTests.ALeafAdmin_ReachesItsOwnSubtree_Only`, `ContainerBindingRuleTests.Nobody_ReachesNothing`, `SpeAdminBulkPerContainerTests.BulkDelete_ByALeafAdmin_DeletesItsOwnContainer_AndRefusesAnotherCustomersAndAnUnboundOne`, `SpeAdminPerContainerScopeTests.ALeafAdmin_OnAContainerWithAMalformedStamp_GetsTheUniform404`, `SpeAdminPerContainerScopeTests.ALeafAdmin_OnAnUnboundContainer_GetsTheUniform404`, `SpeAdminPerContainerScopeTests.ContainerList_ForALeafAdmin_HoldsOnlyItsSubtreesContainers` |
| S6 | binding read fault → Permitted | 1 | `SpeAdminPerContainerScopeTests.WhenTheContainersBindingCannotBeRead_EveryRouteIs503_AndNothingIsActedOn` |
| S7 | a reported container type that is not the config's is accepted | 2 | `ContainerBindingRuleTests.DecideContainer_RefusesAReportedTypeThatIsNotTheConfigs_AndAnAbsentContainer`, `SpeAdminPerContainerScopeTests.AContainerThatDoesNotExist_AndOneOfAnotherType_AnswerExactlyLikeAnotherCustomersContainer` |
| S8 | recycle-bin location mark removed from restore | 2 | `SpeAdminPerContainerScopeTests.ADeletedContainerGoneBetweenTheFilterAndTheRestore_GetsTheFiltersExact404`, `SpeAdminPerContainerScopeTests.ALeafAdmin_OnAnotherCustomersDeletedContainer_GetsTheUniform404_AndTheHandlerNeverRuns` |
| S9 | `GET /containers` untrimmed | 1 | `SpeAdminPerContainerScopeTests.ContainerList_ForALeafAdmin_HoldsOnlyItsSubtreesContainers` |
| S10 | `GET /recyclebin` untrimmed | 1 | `SpeAdminPerContainerScopeTests.RecycleBinList_ForALeafAdmin_HoldsOnlyItsOwnDeletedContainers_ReadFromTheRecycleBin` |
| S11 | `POST /search/containers` untrimmed | 1 | `SpeAdminPerContainerScopeTests.SearchContainers_ForALeafAdmin_DropsOtherCustomersHits_AndReportsNoTotal` |
| S12 | `POST /search/items` hits untrimmed | 1 | `SpeAdminPerContainerScopeTests.SearchItems_ForALeafAdmin_DropsHitsInOtherCustomersContainers` |
| S13 | nested `SearchItemsRequest` loses the container marker | 3 | `Every SPE admin request body naming one ContainerId implements ISpeAdminContainerScopedRequest`, `SpeAdminPerContainerScopeTests.ARequestWhoseRouteAndBodyNameDifferentContainers_Is400_BeforeAnyContainerIsRead`, `SpeAdminPerContainerScopeTests.SearchItems_ScopedToAnotherCustomersContainer_IsTheUniform404_AndNoSearchRuns` |
| S14 | custom-property stamp-change refusal removed | 3 | `SpeAdminPerContainerScopeTests.CustomProperties_StampingAnUnboundContainer_Is403_ForARootAdminToo`, `SpeAdminPerContainerScopeTests.CustomProperties_ThatWouldWriteTheStamp_Are403_AndNothingIsPatched` (×2) |
| S15 | an unchanged stamp re-send is written back (not stripped) | 1 | `SpeAdminPerContainerScopeTests.CustomProperties_TheEditorsFullMapWithTheUnchangedStamp_IsSaved_WithoutRewritingTheStamp` |
| S16 | bulk per-item check: type only, binding ignored | 5 | `BulkOperationContainerTypeTests.Delete_OfAnUnboundOrMalformedContainer_ByALeafAdmin_IsRefused`, `BulkOperationContainerTypeTests.Delete_OfAnotherCustomersContainerOfTheSameType_IsRefused_AndNoDeleteIsSent`, `BulkOperationContainerTypeTests.Grant_OnAnotherCustomersContainerOfTheSameType_IsRefused_AndNoPermissionIsPosted`, `SpeAdminBulkPerContainerTests.BulkDelete_ByALeafAdmin_DeletesItsOwnContainer_AndRefusesAnotherCustomersAndAnUnboundOne`, `SpeAdminBulkPerContainerTests.BulkPermissions_ByALeafAdmin_GrantsOnlyOnItsOwnContainer` |
| S17 | bulk delete job runs with a platform-operator scope | 1 | `SpeAdminBulkPerContainerTests.BulkDelete_ByALeafAdmin_DeletesItsOwnContainer_AndRefusesAnotherCustomersAndAnUnboundOne` |
| S18 | bulk status not bound to its starter | 1 | `SpeAdminBulkPerContainerTests.TheStatusOfAnOperation_IsOnlyForTheCallerWhoStartedIt_AnyoneElseGetsTheUnknownIds404` |
| S19 | type route: a type that is not the config's is accepted | 1 | `SpeAdminPerContainerScopeTests.ATypeRouteNamingATypeThatIsNotTheConfigsOwn_IsTheUniformTypeNotFound` |
| S20 | type route: write to a shared type allowed | 4 | `SpeAdminPerContainerScopeTests.ALeafAdmin_WritingAContainerTypeSharedWithAnotherCustomer_Is403_AndNothingIsSent` (×4) |
| S21 | `POST /containertypes/{typeId}/consumers` unmarked | 3 | `Every app-only /containertypes/{typeId} route is marked with the container-type rule`, `SpeAdminPerContainerScopeTests.ALeafAdmin_WritingAContainerTypeSharedWithAnotherCustomer_Is403_AndNothingIsSent`, `SpeAdminPerContainerScopeTests.ATypeRouteNamingATypeThatIsNotTheConfigsOwn_IsTheUniformTypeNotFound` |
| S22 | narrowing reverted (shared type / owning app / secret exclusive again) | 2 | `SpeAdminConfigAndBulkTenantScopeTests.Post_SharingAnotherUnitsContainerTypeOwningAppOrItsSecret_IsCreated_Model1`, `SpeAdminConfigAndBulkTenantScopeTests.Put_ThatSharesAnotherUnitsContainerTypeOrOwningApp_IsServed_Model1` |
| S23 | per-customer identity (consuming app + secret) no longer exclusive | 5 | `SpeAdminConfigAndBulkTenantScopeTests.Post_BorrowingAnotherUnitsGuid_InANonCanonicalSpelling_Is403`, `SpeAdminConfigAndBulkTenantScopeTests.Post_BorrowingAnotherUnitsPerCustomerIdentity_Is403_AndNamesNeitherConfigNorUnit`, `SpeAdminConfigAndBulkTenantScopeTests.Post_WhenTheOtherUnitStoredItsGuidNonCanonically_TheCanonicalSpellingIsStill403`, `SpeAdminConfigAndBulkTenantScopeTests.Put_RepointingToAnotherUnitsGuid_InANonCanonicalSpelling_Is403`, `SpeAdminConfigAndBulkTenantScopeTests.Put_ThatRepointsAnIdentityFieldToAnotherUnitsPerCustomerIdentity_Is403_AndNothingIsWritten` |
| S24 | `GetReachableConfigIdsAsync` full-page (truncation) guard removed | 1 | `SpeAdminDashboardScopeTests.WhenTheConfigReadMayBeTruncated_TheDashboardIs503_NeverTheTenantWideFigures` |
| S25 | GET config handler builds its own not-found 404 | 1 | `SpeAdminConfigAndBulkTenantScopeTests.AConfigGoneBetweenTheFilterAndTheHandler_GetsTheFiltersExact404` |
| S26 | GET container handler builds its own not-found 404 | 1 | `SpeAdminPerContainerScopeTests.AContainerGoneBetweenTheFilterAndTheHandler_GetsTheFiltersExact404` |
| S27 | dashboard attributes a container to the first config of its type (binding ignored) | 4 | `SpeAdminDashboardScopeTests.Refresh_ForAPlatformOperator_CountsEveryContainerOnce_UnboundAsUnattributed_AndNoForeignContainer`, `SpeAdminDashboardScopeTests.Refresh_RunsTheJob_AndALeafAdminSeesOnlyItsSubtreesContainersAndStorage`, `SpeAdminDashboardScopeTests.Refresh_WhenAContainersBindingCannotBeRead_CountsItForNobody_AndNamesTheConcern`, `SpeAdminDashboardScopeTests.TheFirstViewWithNothingCached_StartsASync_AndALaterViewSeesItsResult` |
| S28 | projection keeps the unattributed count | 3 | `SpeAdminDashboardScopeTests.ALeafAdmin_SeesOnlyItsConfigs_WithItsOwnStorage_NotZero`, `SpeAdminDashboardScopeTests.ALeafAdmin_WhoReachesEveryConfig_SeesTheCompletenessCount_ButNeverUnattributedContainers`, `SpeAdminDashboardScopeTests.Refresh_RunsTheJob_AndALeafAdminSeesOnlyItsSubtreesContainersAndStorage` |
| S29 | full dashboard view for any caller who reaches every config | 1 | `SpeAdminDashboardScopeTests.ALeafAdmin_WhoReachesEveryConfig_SeesTheCompletenessCount_ButNeverUnattributedContainers` |
| S30 | dashboard: unreadable binding counted as unbound | 1 | `SpeAdminDashboardScopeTests.Refresh_WhenAContainersBindingCannotBeRead_CountsItForNobody_AndNamesTheConcern` |
| S31 | body-marker guard scans the models namespace only | 2 | `Every SPE admin request body naming one ContainerId implements ISpeAdminContainerScopedRequest`, `The body scan reaches records nested in an /api/spe endpoint class` |
| S32 | unresolvable container owner → a fixed unit | 1 | `SpeAdminPerContainerScopeTests.CreateContainer_WhoseOwnerCannotBeDetermined_Is403_AndCreatesNothing` |
| S33 | admin-plane stamp failure: container not removed | 2 | `ContainerCreationStampContractTests.AdminPlaneCreate_WhoseStampDidNotLand_SoftDeletesTheContainer_AndThrows`, `SpeAdminPerContainerScopeTests.CreateContainer_WhoseStampCannotBeWritten_IsRemovedAgain_And503` |
| S34 | provisioning stamp failure: container not deleted | 1 | `ContainerCreationStampContractTests.ProvisioningCreate_WhoseStampDidNotLand_DeletesTheContainer_AndThrows` |
| S35 | route/body container disagreement accepted | 1 | `SpeAdminPerContainerScopeTests.ARequestWhoseRouteAndBodyNameDifferentContainers_Is400_BeforeAnyContainerIsRead` |
| S36 | create: owner read fault → proceed with an arbitrary unit | 1 | `SpeAdminPerContainerScopeTests.CreateContainer_WhoseOwnerCannotBeRead_Is503_AndCreatesNothing` |
| S37 | per-container rule: caller-scope read fault → Permitted | 1 | `SpeAdminPerContainerScopeTests.WhenTheCallersScopeCannotBeRead_AContainerRouteIs503_AndNoGraphCallIsMade` |
| S38 | list trim: binding read fault → every listed container shown | 1 | `SpeAdminPerContainerScopeTests.ContainerList_WhenOneBindingCannotBeRead_Is503_NeverTheUntrimmedList` |
| S39 | container-type rule: scope read fault → Permitted | 2 | `SpeAdminPerContainerScopeTests.ATypeRoute_WhenTheScopeCannotBeRead_Is503_AndNothingIsSent` (×2) |
| S40 | bulk: caller-scope read fault → enqueued with an empty scope | 1 | `SpeAdminBulkPerContainerTests.BulkDelete_WhenTheCallersScopeCannotBeRead_Is503_AndNothingIsEnqueued` |

Not seeded, recorded: the "both type marks → Write wins" precedence (no route carries both marks) and the
`SameGuid` false-on-non-GUID branch (exercised by the domain tests' other-type rows, not a guard of its own).

### 11.12 Suites (once, at the end, after every seed was restored)

Run 2026-10-04 on `51f8e3743` (the last commit touching `src/` or `tests/`), after every seed was restored, each suite
once and sequentially. The machine was shared with other sessions' full test runs during the unit suite.

| Suite | Result |
|---|---|
| affected: SpeAdmin / bulk / binding / creation-stamp / provisioning / search / dashboard / tenant-scope classes | **773 passed / 0 failed** — `SpeAdminPerContainerScopeTests` 226, `SpeAdminConfigAndBulkTenantScopeTests` 91, `SpeAdminEnvironmentScopeTests` 38, `SecureNamedOwnerTeamProvisioningTests` 22, `ContainerBindingRuleTests` 21, `BulkOperationContainerTypeTests` 15, `SpeAdminDashboardScopeTests` 10, `ContainerCreationStampContractTests` 9, `SearchItemsTests` 7, `SpeAdminBulkPerContainerTests` 4, others |
| affected ArchTests (`SpeAdmin*`, `WorkloadPlacement*`) | **134 passed / 0 failed** (`SpeAdminContainerBindingGuardTests` 6, `SpeAdminConfigScopedBodyGuardTests` 3, `WorkloadPlacementGuardTests` 17) |
| full BFF unit suite `tests/unit/Sprk.Bff.Api.Tests` | **14,646 passed / 8 failed / 54 skipped (14,708)**. The 8 are request timeouts (`TaskCanceledException`, 1 m 41 s – 3 m 38 s each) in unrelated areas — `ComposeUploadProjectionSeamTests`, `DocumentProfileContractTests`, `CommunicationCreateRecordThreadContractTests`, `ScopePersonasEndpointTests`, `OfficeQuickCreateContractTests`, `OfficeQuickCreateProjectContractTests`, `InsightEndpointsContractTests`, `InsightsSearchEndpointContractTests` — and **all 8 pass on an isolated re-run (14 / 14 incl. theory rows, 21 s)**: contention, not this change. +274 tests vs r2's 14,434. |
| NetArchTest `tests/Spaarke.ArchTests` | **355 passed / 0 failed / 0 skipped** (r2: 347; +8 = the two guard classes) |
| `tests/integration/Sprk.Bff.Api.IntegrationTests` (full) | **104 passed / 0 failed / 0 skipped** |
| `tests/integration/Spe.Integration.Tests` (full) | **403 passed / 0 failed / 25 skipped (428)** |

Code review / adr-check at this round's close (FULL rigor, self-review against the changed files): ADR-001 (no new
route; nine templates renamed, URLs unchanged) ✓ · ADR-002 (no plugin) ✓ · ADR-003 (every read fault refuses: binding,
caller scope, type rule, list trim, bulk capture, create owner — each seeded S6/S36-S40) ✓ · ADR-007 (Graph types stay in
`SpeAdminGraphService` / `ContainerOperations`; NetArchTest green) ✓ · ADR-008 (the container a request names is decided
in the one group filter; handlers only trim lists, judge POST bodies and run the job's per-item check) ✓ · ADR-010 (one
registration REPLACED two; no new DI-registered class) ✓ · ADR-019 (ProblemDetails, machine codes) ✓ · ADR-036/ADR-052
(the ratchet-listed timer migrated to `IScheduledJob`; baseline 13 → 12) ✓ · ADR-038 (no banned pattern; the Graph
seam is WireMock, not `Mock<HttpMessageHandler>`) ✓. No §6.5 path needed. Complexity (CLAUDE.md §11.5):
`SpeAdminTenantScope` grew (the admin-plane scope decision, one responsibility: who may act on which config, container,
type or environment); `SpeDashboardSyncService` grew by the attribution rule, extracted as a pure static so the domain
tests cover it.

### 11.13 Route authorization ledger input (task 167; 167 still not landed — amendment 3, no waiver edits here)

- **Template changes** (a Pending waiver keyed on the old spelling must be DELETED, not renamed): the nine
  `ContainerItemEndpoints` routes `/api/spe/containers/{id}/items…`, `…/{id}/folders`, `…/{id}/items/upload` are now
  `/api/spe/containers/{containerId}/…` (URLs unchanged).
- **Mechanism for the 31 container routes**: filter `SpeAdminTenantScopeFilter` (configId rule, then the per-container
  rule on the route value / body marker; recycle-bin location metadata on restore / permanent delete). Deny test:
  `Sprk.Bff.Api.Tests.Auth.SpeAdmin.SpeAdminPerContainerScopeTests.ALeafAdmin_OnAnotherCustomersContainerOfTheSharedType_GetsTheUniform404_AndTheHandlerNeverRuns`
  (theory over the 29 active-container routes); deleted location (2 routes) `…ALeafAdmin_OnAnotherCustomersDeletedContainer_GetsTheUniform404_AndTheHandlerNeverRuns`.
- **Lists** `GET /api/spe/containers`, `GET /api/spe/recyclebin`, `POST /api/spe/search/containers`, `POST
  /api/spe/search/items`: handler trim — `…ContainerList_ForALeafAdmin_HoldsOnlyItsSubtreesContainers`,
  `…RecycleBinList_ForALeafAdmin_HoldsOnlyItsOwnDeletedContainers_ReadFromTheRecycleBin`,
  `…SearchContainers_ForALeafAdmin_DropsOtherCustomersHits_AndReportsNoTotal`, `…SearchItems_ForALeafAdmin_DropsHitsInOtherCustomersContainers`.
- **`POST /api/spe/containers`**: handler owner resolution + stamp — `…CreateContainer_WhoseOwnerCannotBeDetermined_Is403_AndCreatesNothing`.
- **Bulk** `POST /api/spe/bulk/delete|permissions`: filter (body configId) + per-item per-container check in the job —
  `…SpeAdminBulkPerContainerTests.BulkDelete_ByALeafAdmin_DeletesItsOwnContainer_AndRefusesAnotherCustomersAndAnUnboundOne`;
  `GET /api/spe/bulk/{operationId}/status`: bound to the starter — `…TheStatusOfAnOperation_IsOnlyForTheCallerWhoStartedIt_AnyoneElseGetsTheUnknownIds404`.
- **Type routes** `GET /containertypes/{typeId}/permissions`, `GET|POST /containertypes/{typeId}/consumers`,
  `PUT|DELETE /containertypes/{typeId}/consumers/{appId}`, `POST /containertypes/{typeId}/register`: filter, container-type
  rule — `…SpeAdminPerContainerScopeTests.ALeafAdmin_WritingAContainerTypeSharedWithAnotherCustomer_Is403_AndNothingIsSent`,
  `…ATypeRouteNamingATypeThatIsNotTheConfigsOwn_IsTheUniformTypeNotFound`.
- **Dashboard** rows (§6) unchanged in mechanism; deny tests moved to `SpeAdminDashboardScopeTests` (`ALeafAdmin_SeesOnlyItsConfigs_WithItsOwnStorage_NotZero`,
  `WhenTheScopeCannotBeRead_BothRoutesAre503_NeverTheUnprojectedAggregate`).
- §8 item 6 (`ClaimOnlyFilters` description) stays 167's.

### 11.14 Decisions (this round)

- **D7 — the binding lives on the container** (Graph custom property), not in Dataverse: the container is what is
  authorized, every creation path already talks to Graph, and no unique container→row map exists (§4 trigger 4).
- **D8 — owner at creation**: the config's unit; a unit-less config → the creating admin's unit; neither → 403, never
  unbound. Provisioning → the Secure Record unit the record is isolated into.
- **D9 — the stamp is server-owned, but an unchanged re-send is not an attempt to change it** (the shipped editor's
  full-map save). Changing, adding or removing it → 403 for everyone; unbound containers are stamped by the backfill
  only, from records.
- **D10 — "not found" covers absent, other type, foreign** (one 404 per route family); a container type Graph does not
  REPORT is judged by the binding alone on the routes (the config's own client read it); the bulk job, which acts
  app-only without a per-request filter, requires the type to be reported AND equal.
- **D11 — the binding read is per container** (§11.1), concurrency 8 in trims; a fault refuses the whole list (503) —
  never shows an unverified container and never silently drops one it could not judge.
- **D12 — search totals** are withheld (`null`) unless a platform operator saw nothing trimmed.
- **D13 — identity narrowing** per §11.4 (supersedes D5b).
- **D14 — type routes**: app-only routes get the type rule; the five delegated `{typeId}` routes are Graph's decision
  on the caller's own Entra role (SharePoint Embedded Administrator is tenant-wide by Entra's design); the arch guard
  lists them by name so a sixth cannot slip in unmarked.
- **D15 — bulk** judges each item under the scope captured when the operation is accepted (the request's caller), not
  a later re-read: the job has no caller of its own. A change to the caller's unit applies to their next operation.
- **D16 — dashboard attribution** per §11.5; unattributed containers are visible only in the platform operator's full
  view (they are exactly the containers only a root admin reaches).
- **D17 — `-Verify` fails on a config it could not list** (§11.1): a pass must not claim containers it never saw.

### 11.15 Findings outside the 15 items — complete fixes proposed (not implemented here; for the main session)

> **Superseded by follow-up round f2 (§12):** (a), (b) and (c) are IMPLEMENTED under owner round 35 items 1-4 — they were
> scope discovered during execution and belonged in this task (constraint 21; round 15). (d) is now listed by
> `scripts/Test-SpeConfigSecretNames.ps1 -Verify` and refused by the BFF with 409 until it is renamed (manual gate §12.9).

- **(a) D-12 Model 1, shared consuming tenant.** Customers have dedicated Dataverse environments but share the SPE
  consuming tenant and container type. "Unbound → root-unit admin" is evaluated per environment, so the ROOT admin of
  ANY environment whose config names the shared type reaches every UNBOUND container of that type — including another
  environment's. Also, the control plane's H8 handler (`Sprk.Provisioning.ControlPlane`, not a BFF path) creates
  containers without the stamp. **Complete fix**: (1) H8 stamps the container it creates with the customer's root
  business unit (same property, same read-back-or-remove rule; the 167/ledger owner is customer-provisioning-orchestration);
  (2) before a second environment is onboarded onto a shared type, run the backfill `-Apply` in every environment that
  already uses the type and require `-Verify` exit 0, so no container of the type is unbound; (3) the backfill already
  refuses to stamp FOREIGN / underivable containers, so one environment never claims another's. Today dev has one
  environment, so the exposure is nil in dev.
- **(b)** `sprk_keyvaultsecretname` can name ANY secret in the BFF's Key Vault (pre-existing; the BFF reads it
  app-only). **Complete fix**: an allow-list prefix (e.g. `spe-`) validated on config POST/PUT and at read, 400 otherwise.
- **(c)** The security alerts / secure-score routes call Graph tenant-wide through any config's app (pre-existing).
  **Complete fix**: platform-operator-only (root unit), same check as the environment write rule.
- **(d)** Dev config `68f9a952…` stores the secret name `"null"` (§2, §11.1): neither the BFF nor the backfill can list
  its containers. **Fix** (Dataverse write, main session): set the owning app `bfac7f6e…`'s real secret name (none is in
  `sprk-prod-kv` today), or deactivate the config if it belongs to the control plane only.

### 11.16 Manual gates (main session; dev; write probes only with the owner's approval, throwaway data only)

- **(h) Backfill** (Graph writes). From the repo root, signed in with `az login` as an operator who can read the dev
  Dataverse and `sprk-prod-kv`:
  1. dry run (read-only; expect ToStamp 2 / Underivable 3 as §11.1):
     `pwsh -File scripts/Backfill-SpeContainerBusinessUnitStamp.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -KeyVaultName sprk-prod-kv`
  2. sample: `… -Apply -MaxWritesPerRun 1` — then open that container in the SpeAdminApp as a root admin (still
     visible) and confirm the reversal manifest path printed;
  3. full: `… -Apply`;
  4. verify: `… -Verify -ConfigId c3a25b9a-e81f-f111-88b2-7c1e525abd8b` → exit 0 (the whole-environment `-Verify` stays
     exit 1 until finding (d) is fixed — that is the script refusing to vouch for containers it cannot list);
  5. undo if needed: `… -RevertManifest <manifest.csv> -Apply`.
  The three underivable test containers stay unbound (root admins only) — delete or claim them by hand if wanted.
- **(i) Deleted-container binding read** (read-only): the `deletedContainers/{id}` read with `$select=customProperties` is
  modelled on the active read but unverified live. With a throwaway stamped container in the recycle bin:
  `GET https://graph.microsoft.com/beta/storage/fileStorage/deletedContainers/{id}?$select=id,containerTypeId,customProperties`
  as the owning app → `customProperties.spaarkeBusinessUnitId` present. If Graph drops it there, restore/permanent-delete
  of a stamped deleted container would read as unbound (root only) — fail closed, never open.
- **(j) Per-container probes** (after deploy + (h); needs the Business Unit 1 SPE admin of §9 (b)): as that leaf admin,
  `GET $BFF/api/spe/containers?configId=c3a25b9a-…` → only containers stamped Business Unit 1 or below (none today → `[]`);
  `GET $BFF/api/spe/containers/b!vzGD…?configId=c3a25b9a-…` (root-stamped) → **404** `spe.admin.deny.container_out_of_scope`,
  byte-identical apart from `traceId` to `…/containers/b!doesnotexist…`; as a root admin both → 200 / 404 as usual. WRITE
  part (owner approval): `POST $BFF/api/spe/containers` as the root admin on a throwaway config → 201, and
  `GET …/customproperties` shows `spaarkeBusinessUnitId` = the config's unit; `PUT …/customproperties` changing it →
  **403** `spe.admin.deny.container_binding_server_owned`.
- **(k) Dashboard** (read-only): as a root admin `GET $BFF/api/spe/dashboard/metrics` → `storageUsedInBytesByConfig` and
  `unattributedContainerCount` present after the first scheduled run (or `POST …/refresh`).

---

## 12. Follow-up round f2 — owner round 35 items 1-5 and the verifier's items 2-7, 13-15 (2026-10-04)

### 12.0 How the round reached the branch

- Branch **`task/uac-r2-165-f2`** from `task/uac-r2-165-f1` @ `206470948` (the name was free). No push, PR or merge.
- No `NOTE-FROM-MAIN.md` in the worktree. Binding inputs: owner/main-session rounds 1-35 read from
  `work/unified-access-control-r2` (round 35 answers this task's five open questions) and the POML `<amendments>`.
- `.claude/**`: no edit needed (no `.claude` file names the stamp, the container-type marks or the secret column).
- 167 still not landed on this branch: amendment 3 — no waiver / ledger / GovernedFiles edits; ledger input in §12.10.

### 12.1 Read-only live facts (dev, 2026-10-04)

| Fact | Value |
|---|---|
| Live configs' `sprk_keyvaultsecretname` (the source of the pinned prefix) | `c3a25b9a…` "Spaarke PAYGO 1" → **`spe-owning-app-secret`**; `68f9a952…` "Spaarke SPE Model 1 Owner" → the literal **`"null"`**. The SPE admin app's own placeholder is `spe-owning-app-secret`; `config/spaarke-resources.yaml` and `HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md` name the same secret. |
| The BFF's dev Key Vault (`spaarke-bff-dev` app setting `KeyVaultUri`) | `spaarke-spekvcert` — **19 secrets** (names listed only, no value read): `AzureOpenAI-ApiKey`, `Communication-WebhookClientState`, `Communication-WebhookSigningKey`, `DATAVERSE-URL`, `MANAGED-IDENTITY-CLIENT-ID`, `Redis-ConnectionString`, `SPE-ContainerTypeId`, `SPRK-DEV-DATAVERSE-URL`, `SPRK-MANAGED-IDENTITY-CLIENT-ID`, `UAMI-ClientId`, `ai-openai-endpoint`, `ai-openai-key`, `ai-search-endpoint`, `application-insights-key`, `bff-api-tenant-id`, `communication-trackingfooter-signingkey`, `footer-hmac-key`, `office-addin-client-id`, `spe-owning-app-secret`. Before this round any config could name any of them. |
| Prefix chosen | **`spe-owning-app-`** — admits exactly `spe-owning-app-secret` in both vaults (`spaarke-spekvcert`, `sprk-prod-kv`). A shorter `spe-` would also admit `SPE-ContainerTypeId` (Key Vault names are case-insensitive). |
| `scripts/Test-SpeConfigSecretNames.ps1 -Verify` (read-only, run on dev) | Configs 2, conforming 1, **NOT conforming 1**: `68f9a952…` (`'null'`) → exit **1**. Rename = manual gate §12.9 (b). |
| `scripts/Backfill-SpeContainerBusinessUnitStamp.ps1 -Verify` (read-only, run on dev with the new code) | exit **1**; "Containers still UNBOUND (5)": the 3 underivable (`API Test 2025-09-30 14:43:59`, `Full Flow Test 2025-09-30 14:51:26`, `Test New Container 8-20-2026` — "needs -Bind"), and the 2 derivable-unstamped (`Spaarke Inc` → Spaarke Demo, `Spaarke Dev Container 2` → Spaarke root); `SKIPPED-CONFIG 68f9a952…` (its secret name `null`). |
| Backfill `-Bind` (dry run, read-only) | invalid input (`'b!x=not-a-guid'`, a unit outside the hierarchy, `'novalue='`) → exit **2** before any container is read; a valid `-Bind` for an underivable container → `PLAN … [operator -Bind]`; a `-Bind` that disagrees with the records → `BIND-CONFLICT … Not written`; a `-Bind` for a container no config lists → `BIND-UNUSED`. |

### 12.2 Round 35 item 1 — EVERY container-creation path stamps (verifier items 2 and 13)

**ONE constant on each side.** C#: `src/server/shared/Contracts/SpeContainerBusinessUnitBinding.cs`
(`internal const PropertyName = "spaarkeBusinessUnitId"`), compiled into BOTH the BFF and
`Sprk.Provisioning.ControlPlane.Core` with `<Compile Include … Link>` — L2 may not reference the BFF, and a
`Spaarke.Core` reference would drag `Spaarke.Dataverse` into the L2 publish; `SpeContainerBusinessUnitStamp.PropertyName`
now refers to it. PowerShell: `$SpeContainerStampProperty` in `scripts/common/SpeContainerBinding.ps1`, with
`Get-SpeContainerBinding`, `Set-SpeContainerStamp` and **`Invoke-SpeContainerBindOrRemove`** (stamp → single-container
read-back → `DELETE` the container if the stamp did not land, for any reason; throws, saying whether it was removed).

| Creation path | Owner it stamps | Change |
|---|---|---|
| BFF admin plane `POST /api/spe/containers` | config's unit / creating admin's | unchanged (f1) |
| BFF secure-record provisioning | Secure Record unit | unchanged (f1) |
| **L2 H8** `GraphContainerTypeProvisioner` (root container) | the new environment's **root** business unit | H8 now depends on **H3 and H5** (`DagAdvancer`); before anything is created it requires `InterStepState.DataverseEnvUrl` and reads the root unit (`IDataverseRootBusinessUnitReader`, same query/token idiom as H10; none / two / fault → Resumable, nothing created); after the app-only verification it calls `ISpeContainerTypeProvisioner.BindRootContainerAsync` (PATCH body-root stamp, read-back, DELETE on failure). Bind failure → QuarantineRequired (`spe-container-binding-failed` / `…-not-removed` / `…-infra-fault`), the container id is never handed to H7, no KV write. Bound **after** verification on purpose: an SPE container may be unaddressable for up to 24h, and binding earlier would delete healthy containers during that documented window. Recorded for that project in `projects/customer-provisioning-orchestration-r1/notes/uac-r2-165-h8-container-stamp.md` (with one pre-existing H8 resume defect observed, not changed). |
| **`scripts/New-BusinessUnitContainer.ps1`** (the documented onboarding step) | `-BusinessUnitId` (validated as a GUID before anything) | bind-or-remove after the create; `sprk_containerid` is written only for a bound container |
| **`scripts/Provision-Customer.ps1`** step 10 | the root business unit | the owner is resolved FIRST (no Dataverse URL / token / unique root → nothing created; it used to create first and only then discover it could not record it); a root unit that already has its container → no-op (it used to create a second, unused container); bind-or-remove; then `sprk_containerid` |
| **`scripts/Create-NewContainerType.ps1 -CreateTestContainer`** (a fourth path, found by the guard) | new required `-TestContainerBusinessUnitId` | refused before anything is created without it; bind-or-remove after the create |

**Guard** (`Spaarke.ArchTests/SpeAdminContainerBindingGuardTests`, rewritten): every `FileStorage.Containers.PostAsync(`
in ALL of `src/` binds in its method or is a listed deferred binder whose step is verified (H8: verify → bind → KV write →
complete); every script under `scripts/` that POSTs to the containers collection calls `Invoke-SpeContainerBindOrRemove`
after the create in the same function (or top-level script) and dot-sources the module; the literal appears in exactly
one `.cs` file under `src/` and one script under `scripts/`, and they are equal; the backfill uses the module's constant;
each analyser bites on seeded snippets (PS: bound / unbound / a GET is not a create / no module).

**Manual behaviour check of the PowerShell function** (the repo runs no pwsh in CI; a throwaway local fake Graph,
`scratchpad/f2/fakegraph.py`, never committed): `ok` → BOUND, no DELETE; stamp not read back → DELETE, "it was removed";
PATCH 500 → DELETE; PATCH and DELETE both 500 → "could NOT be removed … UNBOUND … -Bind"; a non-GUID unit → no PATCH,
DELETE. The PATCH body is the BFF's shape: `{"spaarkeBusinessUnitId":{"value":"<unit>","isSearchable":false}}`.

**Docs:** the onboarding guide (`SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`: H8 row, DAG, §7.5, the interim runbook's
`New-BusinessUnitContainer.ps1` call — whose `-CustomerId` parameter never existed — and the new **Phase 5b binding
gate** and **Phase 5c secret-name gate**), `HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md` (its manual curl recipe now
binds or removes), the topology doc, `scripts/README.md`, and §11.2 of this note (its "no other path" claim corrected).

### 12.3 Round 35 item 2 — an UNBOUND container is reachable by NO admin route (amends round 20 item 2)

- `SpeAdminTenantScope.ClassifyContainer` (new; `DecideContainer` delegates to it) names the refusal —
  `absent` / `other_type` / **`unbound`** / `malformed` / `out_of_scope` — and refuses unbound and malformed containers
  for EVERY caller; `SpeAdminCallerScope.CanReach` likewise. The caller still sees ONE 404; the reason is logged
  (`… refused through config … — reason unbound.`), and a list leaves an unbound container out with a warning.
- Every consumer of the rule follows: the 31 container routes (filter), the lists and searches (trim), the bulk job
  (per item). The custom-property route's "stamping an unbound container" now never reaches the handler (404).
- **Search totals (scope discovered, D12 amended):** Graph's total is forwarded only to a platform operator from whose page
  nothing was removed AND only when there is no further page — a later page may hold unbound or another environment's
  containers, which no admin reaches. Container search never had a Graph total (the collection reports none).
- **Dashboard (decision D18 — REVERSED by owner round 41 item 2, §13.2):** ~~the aggregate `unattributedContainerCount` /
  storage stays in a platform operator's full view~~. Unbound containers are now in no dashboard view at all.
- **Backfill:** `-Bind '<containerId>=<businessUnitId>'` (repeatable; split at the last `=`; validated against the
  hierarchy before any container is read; records win — a disagreeing `-Bind` is `BIND-CONFLICT`; never overwrites; unused
  → `BIND-UNUSED`); `-Verify` lists every still-unbound container ("Containers still UNBOUND (n)") and exits 1 on any, on a
  mismatch, an unreadable binding, a skipped config, a bind conflict or an unused bind.
- **Gate:** written into the onboarding guide (Phase 5b) and the topology doc — `-Apply` then `-Verify` exit 0 in every
  environment BEFORE 165's BFF is deployed there, and in every environment already using a container type before a further
  environment is onboarded onto it. Manual gate §12.9 (a).

### 12.4 Round 35 item 3 — `sprk_keyvaultsecretname` is allow-listed (verifier items 3, 14)

`Services/SpeAdmin/SpeConfigSecretNamePolicy` (static): `RequiredPrefix = "spe-owning-app-"`, then Key Vault's own rule
(letters, digits, hyphens; 127 in all), case-insensitive, never trimmed; reason code
**`spe.admin.deny.config_secret_name_not_allowed`**; `EnsureAllowed` throws `SpeConfigSecretNameNotAllowedException`
(message led by the code, so a handler's `Explain` carries it).

- **POST / PUT** `/api/spe/configs` → **400** with that `errorCode` (after the existing format check; PUT whenever the
  field is sent — an unchanged re-send of a pre-rule name is refused too, so saving forces the rename).
- **Every configId route that uses the config's credential** → **409** with that code, from the existing
  `SpeAdminTenantScopeFilter` (the secret name rides the same single config read as the business unit) — judged only AFTER
  the scope, so another customer's config is still the uniform 404; before the container rule and any handler, so nothing
  is read with it. Exempt (marker `SpeAdminConfigCredentialUnused`): GET / PUT / DELETE `/configs/{configId}` and
  `GET /audit` — so a misconfigured config can be seen, corrected and deleted. A census test pins exactly those four.
- **At read** the vault is never called with a non-conforming name: `GetClientForConfigAsync` (before its client cache),
  `GetClientForOwningAppAsync` (before the OBO client cache), `SpeAdminTokenProvider.AcquireOwningAppTokenAsync` (before the
  token cache), and both services' vault reads (the register path, `FetchOwningAppSecretAsync` and the startup validation
  reach the vault only through them). The dashboard job reports such a config as a failed concern carrying the code.
- **`scripts/Test-SpeConfigSecretNames.ps1`** (read-only; `-Verify` exits 1 on any non-conforming config, active or
  inactive — the BFF does not filter by state). Its prefix is pinned equal to the C# constant by an ArchTest.
- `sprk_consumingappkvsecret` is stored but never read by the BFF (grep: only `ConfigEndpoints` / `SpeAdminTenantScope`
  identity comparisons) — nothing to allow-list until something resolves it, and then it must go through the policy.

### 12.5 Round 35 item 4 — security alerts and secure score are platform-operator-only (verifier items 3, 14)

The `/api/spe/security` group carries `RequireSpeAdminPlatformOperator()`; the filter refuses every caller whose own unit
is not the root with ONE 403 **`spe.admin.deny.platform_operator_required`**, FIRST — before the config is even read, so
naming one's own config or a nonexistent one gets the same answer. The check is `RequirePlatformOperatorAsync`, now ALSO
the environment write rule's (that rule used to go through the environment reach, which also read the config table;
writes depend only on the caller's unit and the hierarchy, as its tests already said). Scope fault → 503. A census test
pins exactly the two routes.

### 12.6 Round 35 item 5 — the container-type permission and consumer READS get the every-config rule (item 6)

The read/write split is gone: `SpeAdminContainerTypeOperation` → the marker `SpeAdminContainerTypeScope`
(`.WithSpeAdminContainerTypeScope()` on all six app-only type routes); `DecideContainerTypeAccessAsync` lost its `write`
flag — the type must be the config's own AND every config carrying it reachable. `ALeafAdmin_ReadingItsOwnSharedContainerType_ReachesTheHandler`
is replaced by `ALeafAdmin_ReadingAContainerTypeSharedWithAnotherCustomer_Is403_AndNothingIsSent` plus
`AnAdminWhoReachesEveryConfigOfTheType_ReadsItsPermissionsAndConsumers` (a root admin; a leaf admin whose type no other
customer carries).

### 12.7 Verifier items 4, 5 and 7

- **Item 4 (V4):** `SpeAdminPerContainerScopeTests.SearchItems_AHitThatNamesNoContainer_IsDropped_EvenForARootAdmin` —
  a hit with neither `containerId` nor `parentReference.driveId`; seed B20 (the verifier's V4) RED.
- **Item 5 (V6):** `BulkOperationContainerTypeTests.Delete_/Grant_OnAContainerWhoseTypeGraphDoesNotReport_IsRefused_EvenInTheCallersOwnUnit`
  — bound to the caller's own unit, so only the "type reported and equal" clause can refuse; seed B22 (V6) RED. The bulk
  refusal is now logged with its reason (`type_not_reported` among them).
- **Item 7:** `GET /api/spe/businessunits` is projected onto the caller's reach (own unit + descendants, from the token;
  a platform operator sees all; unresolvable → `[]`; hierarchy fault → the shared 503). The SpeAdminApp's picker comment
  "(all BUs)" (`BuContextPicker.tsx`) is now stale wording only — the list it shows is exactly what POST/PUT accept; no
  client change was needed or made.

### 12.8 Tests added / changed this round — one-line justifications (test-scope criterion)

All through the real host unless stated; no `Mock<HttpMessageHandler>`, no DI-registration test, no constructor
null-check test (ADR-038).

- `SpeAdminPerContainerScopeTests`: root-unit admin on an unbound container 404 on all 29 active routes and both
  recycle-bin routes (round 35 item 2); the log names `reason unbound` / `reason absent` while the 404s are identical;
  malformed 404 for leaf AND root; root list leaves unbound out; search drops a container-less hit (item 4) and reports
  Graph's total only with no further page; the custom-property stamp on an unbound container is the 404; the type-read
  refusing counterpart + the every-config positive (item 6). Fixture: `RecordingLoggerProvider` (the reason is only
  observable in the log).
- `BulkOperationContainerTypeTests`: unbound/malformed by ROOT refused (delete + grant); type not reported refused (item 5).
- `SpeAdminConfigSecretNameTests` (new, 19 incl. rows): 409 on six credential routes with no handler/Graph call; unit-less
  409; another customer's still 404; conforming config served; the four exempt routes usable; census of the exemption;
  POST 400 (five names), PUT 400, conforming (any case) created.
- `SpeConfigSecretNameReadGuardTests` (new; real `SpeAdminGraphService` / `SpeAdminTokenProvider`, the vault substituted at
  the `SecretClient` class boundary): each read guard refuses with zero vault calls — before the client cache, the OBO
  client cache, the token cache, and in both vault reads (register, `FetchOwningAppSecretAsync`, startup validation).
- `SpeConfigSecretNamePolicyTests` (new, domain): allowed / refused names (incl. path and query characters), the 127 limit,
  the exception's code.
- `SpeAdminOperatorRouteAndBusinessUnitScopeTests` (new): security routes 403 for leaf (own and nonexistent config, nothing
  read), 403 for an unresolvable caller, root reaches the handler, 503 on a scope fault, census; business-unit list for
  leaf / root / unresolvable / fault.
- `SpeAdminDashboardScopeTests`: a non-conforming config is a failed concern carrying the code; others still counted.
- `ContainerBindingRuleTests` (domain): root reaches no unbound/malformed container; `ClassifyContainer` reasons.
- Seeds updated to conforming names: `SpeAdminPerContainerScopeTests`, `SpeAdminDashboardScopeTests`,
  `SpeAdminBulkPerContainerTests`, `SpeAdminEnvironmentScopeTests`, `SpeAdminConfigAndBulkTenantScopeTests` (its consuming
  secret too, so naming it as one's own secret still reaches the identity rule), `SearchItemsTests`.
- L2: `H8SpeContainerTypeHandlerTests` AC1 + AC23-AC29, AC22 asserts no bind; `GraphContainerTypeProvisionerBindTests`
  (new, real `GraphServiceClient` over a hand-written fake transport — the project's pattern); `DagAdvancerTests` (H8 waits
  for H5).
- ArchTests: `SpeAdminContainerBindingGuardTests` rewritten (§12.2), incl. the secret-prefix agreement.

### 12.9 Manual gates (main session; dev; live writes only with the owner's approval)

> **SUPERSEDED by §13.9** (round 41: gate (a) step 3 is decided, gate (b) is a script and never deletes the config,
> gate (d) also checks the root-business-unit read). Kept for the record.

- **(a) Container binding gate — BLOCKS deploying this branch's BFF to an environment.** From the repo root, `az login`
  as an operator who can read Dataverse and the vault:
  1. `pwsh -File scripts/Backfill-SpeContainerBusinessUnitStamp.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -KeyVaultName sprk-prod-kv` (dry run; expect §12.1's plan);
  2. `… -Apply -MaxWritesPerRun 1` (sample), then `… -Apply` (stamps `Spaarke Inc` → Spaarke Demo, `Spaarke Dev Container 2` → Spaarke);
  3. the three underivable test containers: decide the owner (or delete them) — e.g. `… -Bind 'b!DcvTfUkibESq94RyGJFs-UhqWZU646tBrEagKKMKiOcv-7Yo7739SKCuM2H-RPAy=06fbf21c-1872-f011-b4cb-7c1e52671ad0','b!rAta3Ht_zEKl6AqiQObblUhqWZU646tBrEagKKMKiOcv-7Yo7739SKCuM2H-RPAy=06fbf21c-1872-f011-b4cb-7c1e52671ad0','b!c8YRuu4xIUeN9wgIynL2y0PsOov3osVDvGM3TRxZDy7RXoLluiTER4gojqVY3YZ6=06fbf21c-1872-f011-b4cb-7c1e52671ad0' -Apply` (root unit `06fbf21c…` = "Spaarke");
  4. after (b): `… -Verify` → **exit 0** (it stays 1 while config `68f9a952…` cannot be listed);
  5. undo: `… -RevertManifest <manifest.csv> -Apply`.
  The same gate in every further environment, and before onboarding an environment onto a type another already uses.
- **(b) Config secret-name rename (dev config `68f9a952…`, stored `"null"`).** Either store owning app `bfac7f6e…`'s client
  secret under a conforming name and point the config at it —
  `az keyvault secret set --vault-name spaarke-spekvcert --name spe-owning-app-model1-owner --value <secret from the app owner>`
  (and the same in `sprk-prod-kv` for the backfill), then
  `$t = az account get-access-token --resource https://spaarkedev1.crm.dynamics.com --query accessToken -o tsv; Invoke-RestMethod -Method Patch -Uri "https://spaarkedev1.crm.dynamics.com/api/data/v9.2/sprk_specontainertypeconfigs(68f9a952-44bf-f111-a05b-3833c5e9614d)" -Headers @{ Authorization = "Bearer $t"; 'Content-Type' = 'application/json' } -Body '{"sprk_keyvaultsecretname":"spe-owning-app-model1-owner"}'`
  — or delete the config if it belongs to the control plane only. Then
  `pwsh -File scripts/Test-SpeConfigSecretNames.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify` → **exit 0**.
- **(c) Probes after deploy (read-only unless stated):** as a Business Unit 1 (leaf) SPE admin —
  `GET $BFF/api/spe/security/alerts?configId=c3a25b9a-…` → **403** `spe.admin.deny.platform_operator_required`;
  `GET $BFF/api/spe/containertypes/8a6ce34c-6055-4681-8f87-2f4f9f921c06/permissions?configId=c3a25b9a-…` → **403**
  `spe.admin.deny.container_type_shared` only if another unreachable config carries the type, else served;
  `GET $BFF/api/spe/businessunits` → Business Unit 1 and its descendants only; as a root admin, an unbound container
  (`GET …/containers/b!c8YR…?configId=c3a25b9a-…` before (a) step 3) → **404**, and App Insights shows
  `reason unbound`; `GET …/containers?configId=68f9a952-…` → **409** `spe.admin.deny.config_secret_name_not_allowed` until (b).
- **(d) H8 bind (L2)** — verified on the next real L2 provisioning run: the run's T6 gate evidence carries
  `owningBusinessUnitId` = the new environment's root unit, and `GET …/containers/{root}?$select=customProperties`
  shows the stamp.
- Still open from f1: §11.16 (i) deleted-container binding read, (k) dashboard per-config fields.

### 12.10 Route authorization ledger input (task 167; still not landed — amendment 3, no waiver edits)

| Route key | Mechanism now | Deny test |
|---|---|---|
| `GET /api/spe/security/alerts`, `GET /api/spe/security/score` | filter: platform-operator mark (`RequireSpeAdminPlatformOperator`), before the configId rule | `Sprk.Bff.Api.Tests.Auth.SpeAdmin.SpeAdminOperatorRouteAndBusinessUnitScopeTests.ALeafAdmin_OnTheSecurityRoutes_GetsOne403_WhateverConfigItNames_AndNothingIsRead` |
| `GET /api/spe/containertypes/{typeId}/permissions`, `GET …/consumers` | filter: container-type rule, every config of the type reachable (reads too) | `…SpeAdminPerContainerScopeTests.ALeafAdmin_ReadingAContainerTypeSharedWithAnotherCustomer_Is403_AndNothingIsSent` |
| `GET /api/spe/businessunits` | handler projection onto the caller's reach (list precedent) | `…SpeAdminOperatorRouteAndBusinessUnitScopeTests.TheBusinessUnitList_ForALeafAdmin_HoldsOnlyItsOwnUnitAndItsDescendants` |
| every configId route except the four exempt | filter: secret-name rule (409) | `…SpeAdminConfigSecretNameTests.ACredentialRoute_OnAConfigWhoseSecretNameIsNotAllowed_Is409_BeforeAnyHandlerOrGraphCall` |
| the 31 container routes, lists, bulk | filter / trim / job: unbound reaches nobody | `…SpeAdminPerContainerScopeTests.ARootAdmin_OnAnUnboundContainer_GetsTheUniform404_AndTheHandlerNeverRuns` |

No route was added, renamed or deleted this round.

### 12.11 Placement (CLAUDE.md §10) and new surface (CLAUDE.md §11)

**Placement: in the BFF**, on existing surfaces — the existing `/api/spe` group filter `SpeAdminTenantScopeFilter`, the
existing `SpeAdminTenantScope`, `SpeAdminGraphService`, `SpeAdminTokenProvider`, `BulkOperationService`, and the existing
config / security / business-unit / search endpoints. **In L2** (`customer-provisioning-orchestration-r1`'s control
plane) for H8 — the handler that creates the container, extending its existing provisioner seam. **In `scripts/`** for
the operator paths. No new endpoint, filter class, policy, BFF DI registration, option, package, Dataverse column, job
or plugin (ADR-002). One new L2 DI registration (the root-unit reader, typed HttpClient — the H10 pattern). No AI type
touched (ADR-013).

| New | Existing (grep evidence) | Extension? | Cost of doing nothing |
|---|---|---|---|
| `src/server/shared/Contracts/SpeContainerBusinessUnitBinding.cs` (source-linked `internal const`) | `SpeContainerBusinessUnitStamp.PropertyName` (BFF only); grep `spaarkeBusinessUnitId` in `src/server/services`: none | L2 cannot reference the BFF or `Spaarke.Core` (which pulls `Spaarke.Dataverse`); a linked file is ONE literal compiled into both (round 35 item 1 "ONE constant on each side") | two literals drift; H8 stamps a name the BFF never reads — every L2-provisioned root container unreachable |
| `SpeConfigSecretNamePolicy` + `SpeConfigSecretNameNotAllowedException` | `ConfigEndpoints.ValidateKeyVaultSecretName` / `KeyVaultSecretNameRegex` (private, format only) | the format check is kept; the rule must be shared by the endpoints, the filter, two services and the job — a private endpoint helper cannot be | a config names `AzureOpenAI-ApiKey` (or any of the vault's 19 secrets) and the BFF reads it app-only (B8, B11-B17) |
| `SpeContainerRefusal` enum + `ClassifyContainer` + `RefusalReason` | `DecideContainer` (collapses every refusal) | `DecideContainer` now delegates; one rule | round 35 item 2's "log reason unbound" is impossible (B4) |
| `SpeAdminScopeDecision.SecretNameNotAllowed` | the enum's refusal members | one member | the filter cannot tell 409 from 404 (B8) |
| markers `SpeAdminPlatformOperatorOnly`, `SpeAdminConfigCredentialUnused`; `SpeAdminContainerTypeScope` (REPLACES the enum `SpeAdminContainerTypeOperation`); extensions `RequireSpeAdminPlatformOperator`, `WithSpeAdminConfigCredentialUnused`, `WithSpeAdminContainerTypeScope()` | `SpeAdminEnvironmentOperation` + `WithSpeAdminEnvironmentScope` (same file, same endpoint-metadata pattern) | same pattern; the type enum's Read/Write split is removed, not added to | leaf admins read the tenant's security data (B5); a misconfigured config could not be repaired through the app (B9) |
| filter `RequirePlatformOperatorAsync`, `ConfigSecretNameNotAllowed`, code `platform_operator_required` | the environment write rule (now shares the check) | the env write rule refactored onto it ("the same check", round 35 item 4) | two copies of "own unit is the root" |
| `ConfigEndpoints.SecretNameNotAllowed` (400 helper with `errorCode`) | `ValidationProblem` (no `errorCode`) | sibling helper | the 400 carries no machine code |
| test seams `SpeAdminGraphService.UseOwningAppClientForConfig`, `SpeAdminTokenProvider.UseTokenForConfig` | `UseClientForConfig` (f1 precedent) | same pattern | the cache-front guards cannot be proven (B15, B16) |
| L2 `IDataverseRootBusinessUnitReader` + `DataverseWebApiRootBusinessUnitReader` (+ `AddHttpClient` in the Worker) | H10's private `DataverseWebApiAppUserCreator.FindRootBusinessUnitIdAsync` | making H8 depend on H10's app-user creator couples two handlers' collaborators for one GET; same query + token idiom | H8 cannot know the owner → its root container unbound (L2, L3) |
| L2 `ISpeContainerTypeProvisioner.BindRootContainerAsync` + `SpeContainerBindRequest` / `SpeContainerBindOutcome`; `GraphContainerTypeProvisioner.BindNewContainerAsync` | the provisioner seam (same T6 identity) | extends the existing seam, no second Graph collaborator | H8 containers unstamped (round 35 item 1; L4-L7) |
| L2 rejection codes (5); DAG `H8 ← {H3, H5}` | `SpeContainerTypeRejectionCodes`; `DagAdvancer.HandlerDependencies` | members / one dependency | H8 runs before the environment exists (L1) |
| `scripts/common/SpeContainerBinding.ps1` | the backfill's private `Read-ContainerBinding` / `Write-ContainerStamp` (moved here) | one module for four scripts | each script spells its own name and PATCH (A1-A4) |
| `scripts/Test-SpeConfigSecretNames.ps1` | none reads config secret-name conformance (grep `sprk_keyvaultsecretname` in `scripts/`: the backfill only) | the backfill is about containers, not configs; round 35 item 3 asks for "a -Verify script" | non-conforming live configs found only by a 409 in production (A8 pins its prefix) |
| backfill `-Bind`; `Create-NewContainerType.ps1 -TestContainerBusinessUnitId` | the scripts | parameters added | underivable containers cannot be bound (round 35 item 2); the test container cannot be stamped |
| test fixture `RecordingLoggerProvider` | none in the host fixtures | fixture extension | the refusal reason (log only) is unobservable (B4) |

**Complexity (CLAUDE.md §11.5):** `SpeAdminTenantScopeFilter` grew by the platform-operator and secret-name branches — one
responsibility (decide whether this admin may act on what the request names); `SpeAdminTenantScope` grew by
`ClassifyContainer` (the per-container rule, now with its reason) — both stay cohesive. H8's handler grew by two steps of
its own orchestration.

**Publish size / CVE:** §12.14. No `PackageReference` changed (BFF or L2).

### 12.12 Seeding proofs (on the final code; `scratchpad/f2/seed165f2.py` + `seed-ps.py`, session scratchpad)

Each seed: ONE exact single-occurrence replacement (anchors pre-checked: every count was 1), the named classes run
(BFF: every `Auth.SpeAdmin` / `Domain.SpeAdmin` / `SearchItemsTests` class; ArchTests: `SpeAdminContainerBindingGuardTests`;
L2: H8 / bind / DAG), the source restored from a byte copy, MD5-checked and touched. **All 37 RED; none failed to compile
(no `error CS` in any run); every file restored** (`git status` showed no `src/`, `tests/` or `scripts/` change afterwards).
Verdict counts include theory rows.

| # | Seed | Failed | RED |
|---|---|---|---|
| B1 | a root-unit admin reaches an UNBOUND container again (ClassifyContainer) | 37 | `BulkOperationContainerTypeTests.Delete_OfAnUnboundOrMalformedContainer_ByARootAdmin_IsRefused_AndNoDeleteIsSent`, `BulkOperationContainerTypeTests.Grant_OnAnUnboundContainer_ByARootAdmin_IsRefused_AndNoPermissionIsPosted`, `ContainerBindingRuleTests.ClassifyContainer_NamesEveryRefusalReason_TheCallerSeesOne404ForAll`, `SpeAdminPerContainerScopeTests.ARootAdmin_OnAnUnboundContainer_GetsTheUniform404_AndTheHandlerNeverRuns`, `SpeAdminPerContainerScopeTests.ARootAdmin_OnAnUnboundDeletedContainer_GetsTheUniform404`, `SpeAdminPerContainerScopeTests.AnUnboundContainer_IsRefusedWithReasonUnbound_InTheLog_WhileTheCallerSeesTheUniform404`, `SpeAdminPerContainerScopeTests.ContainerList_ForARootAdmin_HoldsEveryBoundContainer_ButNoUnboundOne`, `SpeAdminPerContainerScopeTests.CustomProperties_StampingAnUnboundContainer_IsTheUniform404_ForARootAdminToo_AndNothingIsPatched` |
| B2 | a root-unit admin reaches a MALFORMED-stamp container | 3 | `BulkOperationContainerTypeTests.Delete_OfAnUnboundOrMalformedContainer_ByARootAdmin_IsRefused_AndNoDeleteIsSent`, `ContainerBindingRuleTests.ClassifyContainer_NamesEveryRefusalReason_TheCallerSeesOne404ForAll`, `SpeAdminPerContainerScopeTests.AContainerWithAMalformedStamp_IsTheUniform404_ForEveryCaller` |
| B3 | CanReach: a platform operator reaches any binding | 3 | `ContainerBindingRuleTests.ARootAdmin_ReachesEveryBoundUnit_ButNoUnboundOrMalformedContainer_AndNoUnitItDoesNotKnow`, `ContainerBindingRuleTests.ClassifyContainer_NamesEveryRefusalReason_TheCallerSeesOne404ForAll`, `SpeAdminPerContainerScopeTests.ARootAdmin_OnAContainerBoundToAUnitThisEnvironmentDoesNotKnow_GetsTheUniform404` |
| B4 | the refusal log no longer names reason 'unbound' | 2 | `ContainerBindingRuleTests.ClassifyContainer_NamesEveryRefusalReason_TheCallerSeesOne404ForAll`, `SpeAdminPerContainerScopeTests.AnUnboundContainer_IsRefusedWithReasonUnbound_InTheLog_WhileTheCallerSeesTheUniform404` |
| B5 | the security group loses its platform-operator mark | 7 | `SpeAdminOperatorRouteAndBusinessUnitScopeTests.ACallerWithNoResolvableBusinessUnit_GetsTheSame403`, `SpeAdminOperatorRouteAndBusinessUnitScopeTests.ALeafAdmin_OnTheSecurityRoutes_GetsOne403_WhateverConfigItNames_AndNothingIsRead`, `SpeAdminOperatorRouteAndBusinessUnitScopeTests.ExactlyTheTwoSecurityRoutes_ArePlatformOperatorOnly` |
| B6 | the platform-operator check admits any resolvable admin | 11 | `SpeAdminEnvironmentScopeTests.Write_ByALeafAdmin_IsOne403_AndNothingIsReadOrWritten`, `SpeAdminOperatorRouteAndBusinessUnitScopeTests.ALeafAdmin_OnTheSecurityRoutes_GetsOne403_WhateverConfigItNames_AndNothingIsRead` |
| B7 | the container-type rule skips GET (reads) | 5 | `SpeAdminPerContainerScopeTests.ALeafAdmin_ReadingAContainerTypeSharedWithAnotherCustomer_Is403_AndNothingIsSent`, `SpeAdminPerContainerScopeTests.ATypeRouteNamingATypeThatIsNotTheConfigsOwn_IsTheUniformTypeNotFound`, `SpeAdminPerContainerScopeTests.ATypeRoute_WhenTheScopeCannotBeRead_Is503_AndNothingIsSent` |
| B8 | the filter's secret-name check never fires | 7 | `SpeAdminConfigSecretNameTests.ACredentialRoute_OnAConfigWhoseSecretNameIsNotAllowed_Is409_BeforeAnyHandlerOrGraphCall`, `SpeAdminConfigSecretNameTests.AUnitLessConfig_WhoseSecretNameIsNotAllowed_Is409Too` |
| B9 | the credential-unused exemption is ignored | 1 | `SpeAdminConfigSecretNameTests.TheConfigRecordRoutesAndTheAuditLog_StayUsable_SoTheConfigCanBeSeenCorrectedAndDeleted` |
| B10 | the secret-name check runs BEFORE the scope (state oracle) | 2 | `SearchItemsTests.SearchItems_WithToken_ValidConfigIdNotFound_IsTheUniform404_SameAsAnOutOfScopeConfig`, `SpeAdminConfigSecretNameTests.AnotherCustomersConfig_WhoseSecretNameIsNotAllowed_IsStillTheUniform404` |
| B11 | POST /configs accepts any secret name | 5 | `SpeAdminConfigSecretNameTests.Post_WithASecretNameOutsideThePrefix_Is400_WithTheRulesCode_AndNothingIsCreated` |
| B12 | PUT /configs accepts any secret name | 1 | `SpeAdminConfigSecretNameTests.Put_WithASecretNameOutsideThePrefix_Is400_AndNothingIsWritten` |
| B13 | GetClientForConfigAsync: no guard before the client cache | 4 | `SpeAdminDashboardScopeTests.Refresh_AConfigWhoseSecretNameIsNotAllowed_IsAFailedConcernCarryingTheRulesReasonCode`, `SpeConfigSecretNameReadGuardTests.GetClientForConfigAsync_RefusesANonConformingName_WithoutReadingTheVault_EvenWithACachedClient` |
| B14 | the graph service's vault read: no guard (register path) | 1 | `SpeConfigSecretNameReadGuardTests.RegisterContainerTypeAsync_RefusesANonConformingName_BeforeTheVault` |
| B15 | GetClientForOwningAppAsync: no guard before the OBO client cache | 1 | `SpeConfigSecretNameReadGuardTests.GetClientForOwningAppAsync_RefusesANonConformingOwningAppSecret_EvenWithACachedOboClient` |
| B16 | token provider: no guard before the token cache | 1 | `SpeConfigSecretNameReadGuardTests.AcquireOwningAppTokenAsync_RefusesANonConformingOwningAppSecret_EvenWithACachedToken` |
| B17 | token provider's vault read: no guard | 1 | `SpeConfigSecretNameReadGuardTests.TheTokenProvidersVaultRead_RefusesANonConformingName_OnEveryPathThatReachesIt` |
| B18 | the business-unit list is not projected | 1 | `SpeAdminOperatorRouteAndBusinessUnitScopeTests.TheBusinessUnitList_ForALeafAdmin_HoldsOnlyItsOwnUnitAndItsDescendants` |
| B19 | a hierarchy fault on the business-unit list is not the 503 | 1 | `SpeAdminOperatorRouteAndBusinessUnitScopeTests.TheBusinessUnitList_WhenTheHierarchyCannotBeRead_Is503_NeverTheWholeTable` |
| B20 | V4: search keeps hits that name no container | 1 | `SpeAdminPerContainerScopeTests.SearchItems_AHitThatNamesNoContainer_IsDropped_EvenForARootAdmin` |
| B21 | search forwards Graph's total although a further page exists | 1 | `SpeAdminPerContainerScopeTests.SearchItems_ForARootAdmin_ReportsGraphsTotal_OnlyWhenThereIsNoFurtherPage` |
| B22 | V6: bulk no longer requires the type to be REPORTED | 2 | `BulkOperationContainerTypeTests.Delete_OfAContainerWhoseTypeGraphDoesNotReport_IsRefused_EvenInTheCallersOwnUnit`, `BulkOperationContainerTypeTests.Grant_OnAContainerWhoseTypeGraphDoesNotReport_IsRefused_EvenInTheCallersOwnUnit` |
| A1 | New-BusinessUnitContainer.ps1 creates without binding | 1 | `Every SPE container a script creates is bound to its business unit, or removed` |
| A2 | Provision-Customer.ps1 no longer dot-sources the binding module | 1 | `Every SPE container a script creates is bound to its business unit, or removed` |
| A3 | Create-NewContainerType.ps1 creates its test container without binding | 1 | `Every SPE container a script creates is bound to its business unit, or removed` |
| A4 | the backfill spells the property name again | 2 | `The backfill script stamps the property the BFF reads, from the sources the BFF writes`, `The stamp's property name is ONE constant in C# and ONE in PowerShell, and they agree` |
| A5 | the BFF spells the property name again | 1 | `The stamp's property name is ONE constant in C# and ONE in PowerShell, and they agree` |
| A6 | H8 no longer calls the bind step | 1 | `The L2 H8 root container is bound by the handler after verification, before the KV write and the H7 handoff` |
| A7 | the BFF admin-plane create no longer binds | 1 | `Every SPE container created anywhere in src/ is bound to its business unit` |
| A8 | the secret-name -Verify script checks another prefix | 1 | `The secret-name -Verify script checks the prefix the BFF enforces` |
| L1 | the DAG no longer makes H8 wait for H5 | 1 | `DagAdvancerTests.ComputeReadyHandlers_AfterH3_UnlocksH9_ButH8WaitsForH5` |
| L2 | H8 proceeds without the environment URL | 1 | `H8SpeContainerTypeHandlerTests.AC24_MissingDataverseEnvUrl_FailsResumable_BeforeAnythingIsCreated` |
| L3 | H8 proceeds when the environment reports no root unit | 1 | `H8SpeContainerTypeHandlerTests.AC25_AnEnvironmentWithNoRootBusinessUnit_FailsResumable_AndCreatesNothing` |
| L4 | H8 ignores a bind failure | 2 | `H8SpeContainerTypeHandlerTests.AC27_ABindFailure_IsQuarantined_AndTheContainerIsNeverHandedToH7` |
| L5 | H8 binds to no unit | 2 | `H8SpeContainerTypeHandlerTests.AC1_HappyPath_AllSeamsGreen_SucceedsAndAdvancesState`, `H8SpeContainerTypeHandlerTests.AC23_TheRootContainer_IsBoundToTheEnvironmentsRootBusinessUnit_AfterVerification_BeforeTheKvWrite` |
| L6 | the provisioner does not remove an unbound container | 6 | `GraphContainerTypeProvisionerBindTests.AStampThatDoesNotReadBack_RemovesTheContainer`, `GraphContainerTypeProvisionerBindTests.AStampWriteThatFails_RemovesTheContainer`, `GraphContainerTypeProvisionerBindTests.NoOwner_WritesNoStamp_AndRemovesTheContainer`, `GraphContainerTypeProvisionerBindTests.WhenTheRemovalAlsoFails_TheOutcomeSaysTheContainerIsLeftUnbound` |
| L7 | the provisioner does not read the stamp back | 3 | `GraphContainerTypeProvisionerBindTests.AStampThatDoesNotReadBack_RemovesTheContainer` |

**PowerShell `Invoke-SpeContainerBindOrRemove`** (no pwsh runs in CI, so these two were seeded against the throwaway
local fake Graph, `fakegraph.py`, and the same restore/MD5 discipline): **P1** the DELETE removed → no DELETE is sent in
any failure scenario and the "PATCH and DELETE both fail" case falsely reports "it was removed" — RED against the
unseeded run (which sends DELETE in all four failure scenarios and reports "could NOT be removed" when it cannot);
**P2** the read-back skipped → a stamp that does not read back is reported BOUND — RED.

Seeds that also reddened neighbours, recorded because they show reach: B6 (the shared platform-operator check) also
reddened the environment-write tests; B10 (secret check before scope) also reddened `SearchItemsTests.…ValidConfigIdNotFound…`
(its out-of-scope config has no secret name, so the oracle would have shown); B3 (`CanReach` admits every operator) also
reddened `ARootAdmin_OnAContainerBoundToAUnitThisEnvironmentDoesNotKnow`.

### 12.13 Suites (once, at the end, after every seed was restored)

Run 2026-10-04 21:12-21:43 on `1cf7603a1` (every later commit touches only this note and the POML), sequentially, each
suite in full, on a machine other sessions were also using.

| Suite | Result |
|---|---|
| affected, during development (`Auth.SpeAdmin`, `Domain.SpeAdmin`, `SearchItemsTests`, ArchTests `SpeAdmin*`) | green before seeding: 809 / 0 (BFF classes) and 154 / 0 (ArchTests `SpeAdmin*` / `WorkloadPlacement*` / `RouteAuthorization*`) |
| full BFF unit suite `tests/unit/Sprk.Bff.Api.Tests` | **14,728 passed / 0 failed / 54 skipped (14,782)** — 20 m 1 s. No contention failure this time (f1 had 8 timeouts that passed isolated). +74 vs f1's 14,708 total. |
| NetArchTest `tests/Spaarke.ArchTests` | **360 passed / 0 failed / 0 skipped** (f1: 355; +5 = the guard's new facts) |
| `tests/integration/Sprk.Bff.Api.IntegrationTests` (full) | **104 passed / 0 failed / 0 skipped** |
| `tests/integration/Spe.Integration.Tests` (full) | **403 passed / 0 failed / 25 skipped (428)** |
| L2 `src/server/services/Sprk.Provisioning.ControlPlane.Tests` (full — this round changes L2) | **1,583 passed / 0 failed / 1 skipped (1,584)** |

Code review / adr-check at this round's close (FULL rigor, self-review against the changed files): ADR-001 (no new
route) ✓ · ADR-002 (no plugin) ✓ · ADR-003 (every new decision fails closed: an unbound container reaches nobody; a scope
fault on the security routes, the business-unit list and the platform check is 503; the secret-name rule refuses before
any read; H8 creates nothing without an owner and removes what it cannot bind — seeds B6, B19, L2, L3, L6) ✓ · ADR-007
(Graph types stay in `SpeAdminGraphService` / the L2 provisioner) ✓ · ADR-008 (the platform-operator, secret-name and
type decisions are the one group filter's; only the list projections are in-handler, the list precedent) ✓ · ADR-010
(no BFF registration; one L2 typed-HttpClient registration for a two-implementation seam) ✓ · ADR-019 (ProblemDetails,
machine codes: `platform_operator_required`, `config_secret_name_not_allowed`) ✓ · ADR-028 (L2 reader uses
`DefaultAzureCredential` with an explicit tenant, the H10 idiom) ✓ · ADR-038 (no banned pattern; the vault substituted at
the `SecretClient` class boundary, Graph at WireMock / a hand-written transport) ✓. No §6.5 path needed.

### 12.14 Publish size and CVE (CLAUDE.md §10, hazards 1-4)

Each side exported with `git archive` into a SHORT path, `dotnet restore` + `dotnet publish -c Release --no-restore` exactly
as `scripts/Deploy-BffApi.ps1`, zipped with PowerShell **`Compress-Archive`** (Optimal) over `deploy/api-publish/*`, PDBs
included:

| Side | Commit | Path | Zip | Files | MSB3030 |
|---|---|---|---|---|---|
| fresh `origin/master` (fetched 2026-10-04 21:44) | `c2ef1857b` | `C:\wt165m` | **45.65 MB** (47,871,652 B) | 212 | 0 |
| task base (`task/uac-r2-165-f1`) | `206470948` | `C:\wt165p` | **45.70 MB** (47,915,064 B) | 212 | 0 |
| this branch | `1cf7603a1` | `C:\wt165b` | **45.70 MB** (47,919,707 B) | 212 | 0 |

This round's own contribution (branch − base): **+4,643 B (+0.00 MB)**. Branch vs fresh master: **+0.05 MB** (+48,055 B);
`c2ef1857b` is NOT an ancestor of this branch (merge-base `b8026dfa8`), so that figure also carries master's own drift
since the work branch's base. Ceiling 60 MB. Equal file counts, no MSB3030 — all three publishes complete. The three export
directories (this round's and the f1 executor's `C:\wt165m/p/b`, verifier item 12) were removed afterwards.
`dotnet list … package --vulnerable --include-transitive` (BFF): **no vulnerable packages**; no `PackageReference` changed in
the BFF or L2 (the only csproj changes are the two `<Compile … Link>` items).

**Conflict check:** `git merge-tree` of this branch into `work/unified-access-control-r2` @ `a908c5954` — clean. Open PRs
(22) touching a changed path: the dependabot package bumps #876-#881, #883, #947 (`Sprk.Bff.Api.csproj` /
`Sprk.Provisioning.ControlPlane.Core.csproj` version lines — this round adds a separate `<ItemGroup>`) and #1286
(`scripts/README.md`, another section) — line-disjoint.

### 12.15 Decisions (this round)

- **D18 — REVERSED by owner round 41 item 2 (§13.2).** It said the unattributed aggregate stays in a platform operator's
  full view. Under Model 1 the root admin of ANY environment is that environment's platform operator, so an aggregate over
  a shared type's unbound containers counted other customers' containers. Unbound containers now leave every view.
- **D19 — H8 binds after verification**, not at creation inside `ProvisionAsync` (the 24h addressability window).
  **Corrected in f2-v1 (owner round 41 item 1):** as first written ("the bind is still before anything durable consumes the
  container — KV write, H7 handoff, CompletedPhase") this was FALSE on the replication-pending path — the unbound root
  container's id was written to `InterStepState.SpeContainerId` (H7's hand-off) before any bind, and a resume re-created
  the container type and root container, orphaning the first one unbound. Since §13.1 it is true on every path: H8 RECORDS
  its creation at once (`InterStepState.ContainerTypeId` + the T6 gate naming the root container — not the hand-off),
  resumes with it, writes `SpeContainerId` only on completion after the bind and the KV write, and H7 depends on H8.
- **D20 — the secret-name rule is enforced in four layers** (POST/PUT 400; filter 409 on credential routes; the
  cache-front guards; the vault reads) and each is proven alone (§12.12) — the filter cannot cover the background job, and
  the vault-read guard cannot cover a cached client.
- **D21 — the config record routes and the audit log are exempt** from the 409 (they never use the credential) so a
  misconfigured config can be repaired through the product; every other configId route gets the rule by default.
- **D22 — `-Verify` of the secret-name script fails on inactive configs too**: the BFF resolves a config by id
  regardless of its state.
- **D23 — search totals** (amends D12): Graph's total only for a complete result (no further page).
- **D24 — `Provision-Customer.ps1` step 10 resolves the owner before creating** and is a no-op when the root unit already
  has its container (both were orphan-container defects of the old order).

### 12.16 Not closed

Only the manual live gates of §12.9 (live writes: the backfill `-Apply` / `-Bind`, the dev config rename, the
post-deploy probes, the next real H8 run) and f1's §11.16 (i)/(k). ~~One H8 behaviour outside this task is recorded for
customer-provisioning-orchestration-r1~~ — **corrected by owner round 41 item 1: it was this task's scope (round 35 gave
task 165 the H8 change) and deferring it contradicted the "scope discovered is added to this task" constraint and round
15. It is FIXED in §13.1.**

## 13. Follow-up round f2-v1 — owner round 41 items 1-5 (the verification of `task/uac-r2-165-f2`) (2026-10-05)

### 13.0 How the round reached the branch

- Branch **`task/uac-r2-165-f2-v1`** from `task/uac-r2-165-f2` @ `a05e4de96` (the name was free). No push, PR or merge.
- No `NOTE-FROM-MAIN.md`. Binding inputs: owner/main-session rounds 1-43 read from `work/unified-access-control-r2` —
  **round 41** is the main session's decision on this task's f2 verification (items 1-5 below); the verifier's 14 items
  map onto it (1, 2, 11, 12 → round 41 item 1; 3, 13 → item 5 first bullet; 4, 14 → item 5 second bullet; 5 → item 5
  third bullet; 6 → item 5 fourth bullet; 7-10 verified, nothing to do).
- `.claude/**`: no edit needed (no `.claude` file names H8's resume, the dashboard aggregate, the secret-name module or
  the creation guard).
- 167 still not landed on this branch: amendment 3 — no waiver / ledger / GovernedFiles edits; no route added, renamed or
  deleted this round (§13.10).

### 13.1 Round 41 item 1 — H8's replication-pending path stamps or removes; it is not handed to another project (verifier 1, 2, 11, 12)

**The defect, re-traced from code (worse than reported).** `MarkWaitingOnGateAsync` wrote `InterStepState.SpeContainerId`
(H7's hand-off) for a container that was still UNBOUND, and a later entry ran `HandleAsync` from the top, where step (6)
called `ProvisionAsync` unconditionally. The reconciler dispatches every incomplete H8 whose dependencies are done
(`DagAdvancer.ComputeReadyHandlers`); the dispatcher drops a repeat of the same deterministic MessageId only while its
processed marker lives (`DispatchIdempotencyService.ProcessedMarkerTtl` = 24h) — so after the replication wait H8 runs
again and created a second container type and root container (a container type cannot be deleted and is capped per
tenant), leaving the first root container orphaned unbound. H7 depended only on H6, so it could run during the wait and
write the unbound id into `sprk_SharePointEmbeddedContainerId`. The same orphaning followed ANY post-creation
quarantine (verification, bind, KV write) that an operator cleared and resumed.

**The fix (L2, this task's — round 35 gave task 165 the H8 change; round 41 confirmed it):**

| Where | Change |
|---|---|
| `H8SpeContainerTypeHandler` (5a) | `InterStepState.SpeContainerId` is withdrawn at the start of every incomplete entry, before any write — no persisted state of an incomplete H8 hands a container to H7 (a run left by the pre-round-41 path loses its unbound hand-off). |
| (5c) `ReadRecordedCreation` | What this run's H8 already created: `InterStepState.ContainerTypeId` (H8 is its only writer) + the root container named by the `h8-t6-verified` gate's evidence. A recorded root container → NO `ProvisionAsync`, resume at (7) with it; only a type recorded → `ProvisionAsync` with `ExistingContainerTypeId` (a root container in that type only); a root container recorded without a type → QuarantineRequired `spe-creation-record-inconsistent`, nothing created. The pre-round-41 pending path wrote exactly these fields, so its runs resume too. |
| (6b) `RecordCreationAsync` | The creation is persisted IMMEDIATELY after `ProvisionAsync`, before verification — and a provisioner failure that already created the type records the type. On a concurrent write the record is merged over the current document (H8-owned fields only; 5 attempts; never over another creation's record). If it cannot be persisted → QuarantineRequired `spe-creation-record-not-persisted`, the diagnostic naming both ids (bind with `-Bind` or remove), and nothing further happens. |
| (7b) `MarkWaitingOnGateAsync` | No longer writes `SpeContainerId`; the record keeps the ids. |
| (7c) bind failure | A bind that REMOVED the container keeps the type on record and drops the container (so a resume never verifies a deleted container — a 404 reads as the replication wait — and creates only a new root container in the same type). A bind that could not remove keeps the container on record (the resume re-verifies and re-binds it). |
| (9) `MarkCompleteAsync` | The ONLY writer of a container into `SpeContainerId` — after the bind and the KV write (an ArchTest pins it). |
| `GraphContainerTypeProvisioner` | `ProvisionAsync` = cert + client, then `internal CreateAsync(graph, request)`: skips the container-type POST when `ExistingContainerTypeId` is set (the 409-tolerant registration is repeated); a Failure reports `CreatedContainerTypeId`. |
| `DagAdvancer` | **`H7 ← {H6, H8}`** — H7 writes H8's container, so it waits for H8's completion. H9 and the other branches still advance during the wait. |
| `H7DataverseEnvVarValuesHandler` | Refuses (`MissingUpstreamState`, Resumable) a `SpeContainerId` from a run where H8 has not completed — the handler's own fail-closed check behind the DAG edge. |

So on every path the root container is stamped and read back before anything durable consumes it (KV write, H7
hand-off, `CompletedPhase`), or removed. While the run waits out the replication window the container exists unbound
INSIDE an incomplete H8 that names it in the run and will bind or remove it. Recorded for that project in
`projects/customer-provisioning-orchestration-r1/notes/uac-r2-165-h8-container-stamp.md` (rewritten: "Observed, not
changed" → "FIXED").

> **CORRECTED in §14.1 (owner round 49 item 2).** The paragraph above held for the in-memory run only. The record named the
> root container in the T6 gate's `JsonElement` evidence, which the Cosmos SDK's Newtonsoft serializer stores as
> `{"valueKind":1}` — in production every re-entry found only the type, created a SECOND root container and left the first
> UNBOUND. The record is now typed (`InterStepState.SpeContainerCreation`); see §14.

**Docs corrected (verifier item 2):** note D19 (§12.15, the false claim marked and corrected), §12.16, the onboarding
guide (`SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`: H8 row, the DAG — `H8 → H7`, the binding paragraph now explains the record
and the resume, "only a bound container is handed to H7" is now true), the topology doc (the creation bullet names the
H8 wait).

### 13.2 Round 41 item 2 — unbound containers leave every dashboard view (D18 reversed)

`SpeDashboardSyncService.AttributeContainer`: an unbound or malformed container is **Excluded** — counted in no view, the
platform operator's `unattributedContainerCount` / storage included. "Unattributed" now means only a container bound to a
unit of THIS environment under which no config of its type sits (this customer's own). Docs: the class comments, the
`DashboardMetrics` field docs, `DashboardEndpoints.ProjectToReachableConfigs`' remarks, the topology doc. The alarm for
unbound containers is the backfill's `-Verify` (it lists every one to the operator who runs it) and the pre-deploy /
onboarding gate. No client reads the field (grep `unattributed` in `src/solutions/SpeAdminApp`: none).

### 13.3 Round 41 item 3 — the three dev test containers are bound to the root unit

Decided: `b!DcvT…`, `b!rAta…`, `b!c8YR…` → `06fbf21c…` ("Spaarke"), with `-Bind`, as a manual live step (binding is
reversible — the backfill's revert manifest — deleting is not). Gate §13.9 (a) step 3 carries the exact command.

### 13.4 Round 41 item 4 — dev config `68f9a952` ("Spaarke SPE Model 1 Owner", stored `"null"`)

**Read-only investigation (2026-10-05):**

| Question | Evidence | Answer |
|---|---|---|
| Is the config used only by the control plane? | `Grep sprk_specontainertypeconfig` over `src/server/services` (L2): **no match**. L2's H8 takes its owning app from H3's `InterStepState.BffAppRegId` and its cert from the customer vault — never a config row. The row's `createdby` / `modifiedby` = `# mi-bff-api-dev` (created 2026-10-03 16:06 through the BFF, i.e. `POST /api/spe/configs` from the SPE admin app). Its readers are the BFF's SPE admin plane (`SpeAdminGraphService.ResolveConfigAsync`, the dashboard job), the backfill and the secret-name scripts. | **No** — it is a BFF SPE-admin config for container type `fb3817a8…` ("Spaarke Model 1", owned by app `Spaarke SPE Model 1 Owner` `bfac7f6e…`). It is made conforming anyway (round 41). |
| Is the owning app's secret anywhere? | `az ad app show --id bfac7f6e…`: **no password credentials, no certificates**. Vault names (no values read): `spaarke-spekvcert` (the BFF's) and `sprk-prod-kv` hold only `spe-owning-app-secret` (config `c3a25b9a…`'s app `170c98e1…`). | **No** — no secret exists to store; one must be minted. |

**Delivered:** `scripts/Repair-SpeConfigSecretName.ps1` (dry run / `-Apply` / `-Verify`; never deletes the config, never
overwrites a stored secret, never prints a value). `-Apply`: the value comes from a vault already holding `-SecretName`
(all holders must agree — so a repeat run mints nothing), else `-MintClientSecret` (Graph `addPassword` on the owning app —
an Entra write, existing credentials kept, keyId printed) or `-SourceKeyVaultName`/`-SourceSecretName`; it is stored where
missing (Key Vault REST, so the value is never on a command line), then the config is PATCHed with If-Match on its ETag.
`-Verify` exits 0 only when the config names the secret, every vault holds the same value, and that value authenticates
as the owning app (a client-credentials token request). Run read-only on dev: the dry run prints the plan (STORE in both
vaults, PATCH `'null'` → `spe-owning-app-model1-owner`); `-Verify` exits **1** (config names `'null'`, both vaults lack the
secret); a non-conforming or trailing-newline `-SecretName` exits **2** before anything is read. The manual gate is
§13.9 (b).

**Scope discovered and added (round 15):** the PowerShell allow-list was spelled inside `Test-SpeConfigSecretNames.ps1`
only, and the container-binding backfill read a config's secret from the vault WITHOUT judging its name (it would have
sent, say, the Redis connection string to the token endpoint as a client secret for whatever app a config named). Now:
ONE PowerShell copy of the rule, `scripts/common/SpeConfigSecretNamePolicy.ps1` (`$SpeConfigSecretNamePrefix`,
`Test-SpeConfigSecretNameAllowed`, `\z`-anchored), dot-sourced by `Test-SpeConfigSecretNames.ps1`,
`Repair-SpeConfigSecretName.ps1` and the backfill, which now refuses a non-conforming name BEFORE `az keyvault secret show`
(SKIPPED-CONFIG "the vault was NOT read" — run read-only on dev: `68f9a952…` now reports exactly that). An ArchTest pins
the module equal to the C# rule, every vault-reading script to it, and the backfill's judge-before-read order.

### 13.5 Round 41 item 5 — the verifier's LOW items

**(a) The platform-operator `TotalCount` guard (verifier 3, 13).** Root cause of the green seed V14: the containers
collection reports no total — `SpeAdminGraphService.SearchContainersAsync` returns `TotalCount: null` by design — so no
request through the host could ever observe the guard at `SearchContainersEndpoints.cs:133`. Fixed by structure, not by a
host test that cannot bite: the rule is ONE method, `SpeAdminContainerTrim.ReportableGraphTotal` (platform operator ∧
nothing removed ∧ no further page), used by BOTH searches (the item search's copy is gone); the container endpoint's
response is built by `internal SearchContainersEndpoints.BuildResponse(page, trim)`, which a test drives with a page that
DOES carry a total. Seeds: the rule's three clauses each RED (B1-B3; B1 = V14 in the shared rule, also reddening the item
search's host test), and V14 at the container call site (B4: forward `page.TotalCount` raw) RED.

**(b) The creation guard's holes (verifier 4, 14).** `SpeAdminContainerBindingGuardTests` now follows every route to the
containers collection, failing closed on what it cannot judge:
- **C# (all of `src/`):** every `.FileStorage` member access must chain straight into `.Containers` / `.ContainerTypes` /
  `.ContainerTypeRegistrations` / `.DeletedContainers` (a held FileStorage builder is refused); `FileStorage.Containers`
  not indexed must chain straight into `.GetAsync(` / `.PostAsync(` (optionally via `.WithUrl(…)`) — a collection builder
  held in a variable, passed along, or anything else is refused; `ContainersRequestBuilder` named anywhere is refused; the
  collection URL spelled in a string, in a method that POSTs (`PostAsync`, `HttpMethod.Post`, `Method.POST`), is a create.
  Every create must bind in its method (or be the verified deferred binder). Seeded snippets (each flagged): unbound,
  **held collection (the verifier's H2)**, held FileStorage, passed along, constructed, raw HTTP, Kiota; passing: bound,
  bound via `WithUrl`, the read forms, a namespace-only reference.
- **PowerShell (`scripts/` and any `.ps1` under `src/`):** the Graph SDK cmdlet `New-Mg[Beta]StorageFileStorageContainer`
  is a create anywhere; in a script that names the collection (the URL, or a `…/containers` string in a script that
  mentions fileStorage), every POST — `-Method Post`, a splat's `Method = 'Post'`, `az rest --method post`, `curl -X
  POST`, a helper's positional `'Post'`, `[…]::Post` — is a create unless its URI is provably another endpoint (a literal,
  or a variable every assignment of which is a literal); **a URI held in a variable (the verifier's H1)** that resolves to
  the collection, a variable it cannot resolve, a splat or a function's result counts as a create. Seeded snippets (each
  flagged): unbound, no module, **URI in a variable (H1)**, URI built from a base, splatted, URI from a function, `az
  rest`, the SDK cmdlet; passing: bound, the existing token / Dataverse / item-URL POSTs, a GET of the collection.
- **No binder outside C# / PowerShell:** a TypeScript / JavaScript / Python / shell file under `src/`, or a shell / Python /
  JavaScript / pipeline-YAML / cmd file under `scripts/`, that names the collection fails the build.
- Real-file seeds (A4-A8): the verifier's exact H1 as a new script, H2 as a new `.cs` under `src/`, a `.ts` under `src/`, a
  YAML under `scripts/`, the SDK cmdlet — all RED.

**(c) The allow-list anchor (verifier 5).** `SpeConfigSecretNamePolicy.Allowed` ends at `\z` (and the PowerShell module).
Tests: the domain rule refuses `"spe-owning-app-secret\n"` and `"…\r\n"`; `POST /api/spe/configs` with
`"spe-owning-app-acme\n"` answers 400 with the rule's code and creates nothing (the endpoint's own format check,
`^[a-zA-Z0-9-]{1,127}$`, is left as it is: the policy runs right after it on every POST/PUT and refuses the same names
with the rule's machine code — tightening it too would change no outcome, so it could not be proven); the ArchTest pins
`'}\z'` in the module. Seeds B5 (C# `$`) and A1 (PowerShell `$`) RED; the repair script refuses a trailing-newline name
(exit 2, run on dev).

**(d) The H5/H8 root-business-unit read (verifier 6).** Settled from code as far as code can settle it, and put in live
gate (d). `DataverseWebApiRootBusinessUnitReader` uses `DefaultAzureCredential(TenantId)` against `{env}/.default` —
the credential, tenant and audience of H5's WhoAmI probe, which is H5's completion gate (`H5DataverseEnvCreationHandler`
completes only on `Reachable`); H8 is dispatched only after H5 completed, and H10 later WRITES app users with the same
credential. The comments that say the "MI-Dataverse App User (H10)" does not exist yet concern the identity H10 REGISTERS
(`InterStepState.MiClientId`); the project's own comments disagree on whether that is L2's identity, and if it were, H5's
gate and H10's writes could not work either. So H8 is no worse placed than H5 and H10, and a 401/403 surfaces as Resumable
`spe-root-business-unit-unresolved` naming the status — never a silent stall. What a WhoAmI 200 does not prove is a
security role that reads `businessunit` — gate (d) checks it on the first real run. Recorded in the reader's header and
the customer-provisioning note.

### 13.6 Placement (CLAUDE.md §10) and new surface (CLAUDE.md §11)

**Placement:** in the BFF on existing surfaces (`SpeAdminTenantScope`'s trim record, the search endpoints, the dashboard
job and projection, `SpeConfigSecretNamePolicy`); in L2 on H8's existing handler and provisioner seam, the existing DAG
map and H7's existing guard block; in `scripts/` for the operator paths. No new endpoint, filter, policy, DI registration
(BFF or L2), option, package, Dataverse column, job or plugin (ADR-002). No AI type touched (ADR-013).

| New | Existing (grep evidence) | Extension? | Cost of doing nothing |
|---|---|---|---|
| `SpeContainerTypeProvisionRequest.ExistingContainerTypeId`; `…Outcome.Failure.CreatedContainerTypeId` | the provisioner seam (`ISpeContainerTypeProvisioner`), ONE implementation + the tests' fake | members of the existing records | a resume after a failed bind / a mid-creation failure makes a second, undeletable container type (L12, L13, L6, L7) |
| `GraphContainerTypeProvisioner.CreateAsync` (internal) | `ProvisionAsync` (cert + Graph in one method) | split of the same method, the `BindNewContainerAsync` precedent | the existing-type path and the failure report unprovable without live Graph |
| H8 `ReadRecordedCreation` / `RecordCreationAsync`; codes `CreationRecordInconsistent` / `CreationRecordNotPersisted`; three evidence-status constants | `MarkWaitingOnGateAsync` (wrote the ids but nobody read them back); `SpeContainerTypeRejectionCodes` | handler-private steps over fields H8 already owns; no new `InterStepState` key (design.md §6.2 locks keys) | the verifier's item 1: every re-entry re-creates, the first container orphaned unbound (L1, L2, L5, L8-L11) |
| DAG edge `H7 ← H8`; H7's H8-completed check | `DagAdvancer.HandlerDependencies`; H7's upstream guards | one dependency; one guard in the existing block | H7 writes an unbound container into `sprk_SharePointEmbeddedContainerId` (L14, L15) |
| `SpeAdminContainerTrim.ReportableGraphTotal` | two inline copies (item and container search) | one method on the existing trim record; the copies removed | the container-search guard unprovable (V14); two copies drift (B1-B3) |
| `SearchContainersEndpoints.BuildResponse` (internal) | the inline response construction | extraction | V14 stays unobservable (B4) |
| `scripts/common/SpeConfigSecretNamePolicy.ps1` | the rule inside `Test-SpeConfigSecretNames.ps1` (moved here) | ONE module for three scripts, the `SpeContainerBinding.ps1` precedent | the backfill reads any secret a config names; a third spelling drifts (A1-A3) |
| `scripts/Repair-SpeConfigSecretName.ps1` | `Test-SpeConfigSecretNames.ps1` is read-only by name and contract (the onboarding gate relies on it); the backfill is about containers | a writer cannot live in the read-only check | round 41 item 4's script; dev config `68f9a952` stays refused, its containers unlisted, the binding gate's `-Verify` stuck at 1 |

**Complexity (§11.5):** `H8SpeContainerTypeHandler` grew by its own record-and-resume steps (one responsibility: drive
H8's creation to a bound, persisted container exactly once); `GraphContainerTypeProvisioner` was split, not grown.

### 13.7 Tests added / changed this round — one-line justifications (test-scope criterion)

All L2 tests are this project's pure handler tests with hand-written fakes / a fake Graph transport; BFF host tests go
through the real host. No `Mock<HttpMessageHandler>`, no DI-registration test, no constructor null-check test (ADR-038).

- `H8SpeContainerTypeHandlerTests` **AC30-AC40** (11): wait then resume — ONE creation, the same container verified, bound
  and handed off (the verifier's missing "pending followed by resume" test); a resume during the wait creates nothing;
  the record precedes verification (order `provision, write, verify`) so a quarantined verify resumes with the same
  container; a bind that removed the container resumes with a new root container in the SAME type; a provisioner failure
  after the type records the type; a pre-round-41 run resumes with its container (AC35, quarantined path) and loses its
  unbound hand-off even when the entry stops early (AC40); an inconsistent record is quarantined; the record survives a
  concurrent write; it never overwrites another creation's record (AC39); an unrecordable creation is quarantined naming
  both ids. **AC22** now asserts `SpeContainerId` is null during the wait and the record names the container.
- `GraphContainerTypeProvisionerBindTests` (+2): an existing type is reused (no container-type POST; the container POST
  carries it); a failure after the type reports it.
- `DagAdvancerTests` (+2): after H6, H7 waits for H8; after H6 and H8, H7 is ready.
- `H7DataverseEnvVarValuesHandlerTests` (+1, fixture: `BuildRun` records H8's completion): a container id from a run where
  H8 has not completed is never written.
- `ContainerBindingRuleTests` (domain): unbound / malformed → Excluded (renamed, was Unattributed); the shared Graph-total
  rule's clauses; the container-search response builder with a page that carries a total.
- `SpeAdminDashboardScopeTests`: the refresh counts no unbound container — a `c-rootless` container (bound to the root,
  no config of its type there) is the remaining unattributed one.
- `SpeConfigSecretNamePolicyTests` (+2 rows), `SpeAdminConfigSecretNameTests` (+1 row): the trailing-newline names.
- `SpeAdminContainerBindingGuardTests` (ArchTests): the C# and PowerShell analysers rewritten (§13.5 b) with seeded
  snippets; `NoOtherSourceUnderSrc_AddressesTheContainersCollection`, `NoOtherScriptType_AddressesTheContainersCollection`
  (new); `TheDeferredBinderIsReal` extended (read record → create → record → verify → bind → KV → complete;
  `SpeContainerId` given a container only in `MarkCompleteAsync`); `TheSecretNameVerifyScript_AgreesWithTheBff` now pins the
  module, the `\z` anchor, every vault-reading script and the backfill's judge-before-read order.

### 13.8 Seeding proofs (on the final code; `scratchpad/f2v1/seed.py` + `seed_ps.py`, session scratchpad)

Each seed: ONE exact single-occurrence replacement (anchor counts checked = 1) or ONE added file, the named classes run
(L2: H8 / DAG / H7 / provisioner; BFF: every `Auth.SpeAdmin` / `Domain.SpeAdmin` / `SearchItemsTests` class; ArchTests:
`SpeAdminContainerBindingGuardTests`), the source restored from a byte copy, MD5-checked and touched (added files
deleted). `git status` was clean afterwards. **All 30 in the table RED in their final form, plus P1 below; none failed
to compile in its final form.** Three first forms were reworked and are recorded honestly: **L9** (`return` inside the merge loop) did not
compile (CS0162) → re-seeded as **L9b**, RED; **L4** (withdrawal removed at (5c)) stayed GREEN because every write of
that entry already cleared the field — the withdrawal was MOVED to (5a), before any write, AC35/AC40 now pin it, and
**L4b** is RED; **A2** (`$false -and` in the backfill's judge) stayed GREEN because the ArchTest only looked for the call
text — the test now pins the exact "if not allowed, throw" shape, A2 and **A2b** (warn instead of throw) are RED.

| # | Seed | RED tests |
|---|---|---|
| L1 | H8 re-entry ignores a recorded root container (re-creates) | `H8SpeContainerTypeHandlerTests.AC30_…`, `AC31_…`, `AC32_…`, `AC35_…` |
| L2 | the creation is not recorded before verification | `AC32_…`, `AC33_…`, `AC37_…`, `AC38_…`, `AC39_…` |
| L3 | the replication wait hands the UNBOUND container to H7 again | `AC22_…`, `AC30_…`, `AC31_…`; ArchTest `The L2 H8 root container is bound by the handler after verification, before the KV write and the H7 handoff` |
| L4b | an incomplete entry keeps a pre-round-41 unbound hand-off | `AC35_…`, `AC40_AnUnboundHandOff_IsWithdrawn_EvenWhenTheEntryStopsBeforeReadingItsRecord` |
| L5 | a bind that removed the container keeps it on record | `AC33_…` |
| L6 | a provisioner failure's created type is not recorded | `AC34_…` |
| L7 | the handler never passes the recorded type to the provisioner | `AC33_…`, `AC34_…` |
| L8 | an inconsistent record is not refused | `AC36_…` |
| L9b | a concurrent write loses the record (retries the stale document) | `AC37_…` |
| L10 | the merge overwrites another creation's record | `AC39_…` |
| L11 | an unrecordable creation proceeds as if recorded | `AC38_…` |
| L12 | the provisioner ignores `ExistingContainerTypeId` | `GraphContainerTypeProvisionerBindTests.AnExistingContainerType_IsReused_OnlyARootContainerIsCreatedInIt` |
| L13 | the provisioner's failure does not report the type it created | `…AFailureAfterTheTypeWasCreated_ReportsTheType_SoTheHandlerRecordsIt` |
| L14 | the DAG lets H7 run before H8 | `DagAdvancerTests.ComputeReadyHandlers_AfterH6_H7WaitsForH8_WhichHandsOffOnlyABoundContainer` |
| L15 | H7 consumes a container id from a run where H8 has not completed | `H7DataverseEnvVarValuesHandlerTests.ASpeContainerIdWrittenBeforeH8Completed_IsNeverConsumed_NoWriterCall` |
| B1 | the shared Graph-total rule forwards despite a further page (the verifier's **V14**) | `SpeAdminPerContainerScopeTests.SearchItems_ForARootAdmin_ReportsGraphsTotal_OnlyWhenThereIsNoFurtherPage(moreResults: True…)`, `ContainerBindingRuleTests.GraphsSearchTotal_…`, `…TheContainerSearch_ReportsGraphsTotal_OnlyThroughTheSharedRule` |
| B2 | the rule forwards to any admin | `ContainerBindingRuleTests.GraphsSearchTotal_…`, `…TheContainerSearch_…` |
| B3 | the rule forwards for a trimmed page | `SpeAdminPerContainerScopeTests.SearchItems_AHitThatNamesNoContainer_IsDropped_EvenForARootAdmin`, `ContainerBindingRuleTests.GraphsSearchTotal_…`, `…TheContainerSearch_…` |
| B4 | the container search forwards Graph's total unguarded (**V14 at the call site**) | `ContainerBindingRuleTests.TheContainerSearch_ReportsGraphsTotal_OnlyThroughTheSharedRule` |
| B5 | the C# allow-list ends at `$` again | `SpeConfigSecretNamePolicyTests.ANameOutsideTheRule_IsRefused(name: "spe-owning-app-secret\n")`, `SpeAdminConfigSecretNameTests.Post_WithASecretNameOutsideThePrefix_Is400_…(name: "spe-owning-app-acme\n")` (the `\r\n` row stays green under `$` by nature — `$` only forgives a final `\n`) |
| B6 | unbound containers counted in the operator's aggregate again | `SpeAdminDashboardScopeTests.Refresh_ForAPlatformOperator_CountsEveryContainerOnce_AndNoUnboundOrForeignContainer`, `…Refresh_WhenAContainersBindingCannotBeRead_…`, `ContainerBindingRuleTests.UnboundAndMalformedContainers_AreCountedNowhere_NotEvenInTheOperatorsAggregate` |
| A1 | the PowerShell allow-list ends at `$` again | `The PowerShell secret-name rule is ONE module that agrees with the BFF, and every script that judges a name uses it` |
| A2 | the backfill's judge weakened (`$false -and …`) | same |
| A2b | the backfill warns instead of refusing | same |
| A3 | the repair script spells its own prefix | same |
| A4 | **the verifier's H1** as a new script (`$uri = "…/fileStorage/containers"; Invoke-RestMethod -Uri $uri -Method Post`) | `Every SPE container a script creates is bound to its business unit, or removed` |
| A5 | **the verifier's H2** as a new `.cs` under `src/` (`var containers = graph.Storage.FileStorage.Containers; await containers.PostAsync(…)`) | `Every SPE container created anywhere in src/ is bound to its business unit` |
| A6 | a TypeScript file under `src/` posting to the collection | `No other source under src/ addresses the SPE containers collection` |
| A7 | a pipeline YAML under `scripts/` posting to the collection | `No other script type under scripts/ addresses the SPE containers collection` |
| A8 | a script creating through `New-MgStorageFileStorageContainer` | `Every SPE container a script creates is bound to its business unit, or removed` |

The analysers' own seeded-snippet tests (`TheCreationAnalyser_…`, `TheScriptCreationAnalyser_…`) hold 7 C# and 8 PowerShell
flagged shapes plus the passing forms (§13.5 b), so a regression in an analyser reddens too.

**PowerShell, behaviourally** (`seed_ps.py`; no pwsh runs in CI): **P1** the module's `\z` reverted to `$` — the repair
script, given `"spe-owning-app-x\n"` as `-SecretName` on dev (read-only), proceeds to its dry run (exit 0); unseeded it
refuses before anything is read (exit 2). RED.

### 13.9 Manual gates (main session; dev; live writes only with the owner's approval) — SUPERSEDED by §14.9

> §14.9 is the current list: gate (d) as written here expected evidence fields that were not persisted (round 49), and
> round 49 item 1 adds gate (e) (the operator-environment marker).

From the repo root, `az login` as an operator who can read Dataverse and the vaults:

- **(b) first — Config secret-name repair (round 41 item 4; dev config `68f9a952…`).** Never delete the config.
  1. `pwsh -File scripts/Repair-SpeConfigSecretName.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -ConfigId 68f9a952-44bf-f111-a05b-3833c5e9614d -SecretName spe-owning-app-model1-owner -KeyVaultName spaarke-spekvcert,sprk-prod-kv` (dry run — expect: STORE in both vaults, PATCH `'null'` → `spe-owning-app-model1-owner`);
  2. `… -MintClientSecret -Apply` (adds a client secret to app `bfac7f6e…` "Spaarke SPE Model 1 Owner" — it has none — an Entra write; record the printed keyId);
  3. `… -Verify` → **exit 0**; then `pwsh -File scripts/Test-SpeConfigSecretNames.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify` → **exit 0**.
  Undo, if ever needed: remove the credential by its keyId (`az ad app credential delete --id bfac7f6e-9fa0-4664-8492-c7a1dfe73d5e --key-id <keyId>`) and the two secrets.
- **(a) Container binding gate — BLOCKS deploying this branch's BFF to an environment.**
  1. `pwsh -File scripts/Backfill-SpeContainerBusinessUnitStamp.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -KeyVaultName sprk-prod-kv` (dry run; after (b) it also lists config `68f9a952…`'s containers);
  2. `… -Apply -MaxWritesPerRun 1` (sample), then `… -Apply` (stamps `Spaarke Inc` → Spaarke Demo, `Spaarke Dev Container 2` → Spaarke, and whatever (b) made listable);
  3. **decided (round 41 item 3) — the three underivable dev test containers are bound to the root unit "Spaarke":**
     `pwsh -File scripts/Backfill-SpeContainerBusinessUnitStamp.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -KeyVaultName sprk-prod-kv -Bind 'b!DcvTfUkibESq94RyGJFs-UhqWZU646tBrEagKKMKiOcv-7Yo7739SKCuM2H-RPAy=06fbf21c-1872-f011-b4cb-7c1e52671ad0','b!rAta3Ht_zEKl6AqiQObblUhqWZU646tBrEagKKMKiOcv-7Yo7739SKCuM2H-RPAy=06fbf21c-1872-f011-b4cb-7c1e52671ad0','b!c8YRuu4xIUeN9wgIynL2y0PsOov3osVDvGM3TRxZDy7RXoLluiTER4gojqVY3YZ6=06fbf21c-1872-f011-b4cb-7c1e52671ad0' -Apply` (nothing is deleted);
  4. `… -Verify` → **exit 0**;
  5. undo: `… -RevertManifest <manifest.csv> -Apply`.
  The same gate in every further environment, and before onboarding an environment onto a type another already uses.
- **(c) Probes after deploy** — as §12.9 (c), plus: `GET $BFF/api/spe/dashboard/metrics` as a root admin →
  `unattributedContainerCount` counts no unbound container (after (a) there are none; before (a) the five unbound dev
  containers are NOT in it).
- **(d) The next real L2 provisioning run** — (1) H8 completes WITHOUT `spe-root-business-unit-unresolved` (if it fails
  there, the run's error names the HTTP status — then L2's identity lacks a role that reads `businessunit` in the new
  environment; round 41 item 5); (2) the T6 gate evidence carries `owningBusinessUnitId` = the environment's root unit and
  `GET …/containers/{root}?$select=customProperties` shows the stamp; (3) if the run hits the replication wait: the run is
  `WaitingOnGate` with `interStepState.containerTypeId` set, the `h8-t6-verified` gate Pending naming the root container,
  `interStepState.speContainerId` EMPTY and H7 not dispatched; after the wait exactly ONE container type owned by the customer's
  BFF app exists (`GET /storage/fileStorage/containerTypes` with a DELEGATED SharePoint Embedded admin token — app-only
  answers 403) and ONE root container in it, and only then is `speContainerId` set and H7 dispatched.
- Still open from f1: §11.16 (i) deleted-container binding read, (k) dashboard per-config fields.

### 13.10 Route authorization ledger input (task 167; still not landed — amendment 3, no waiver edits)

No route was added, renamed or deleted this round. `POST /api/spe/search/containers` keeps its mechanism (filter + trim);
its Graph total now goes through `SpeAdminContainerTrim.ReportableGraphTotal` — deny-side proof
`Sprk.Bff.Api.Tests.Domain.SpeAdmin.ContainerBindingRuleTests.TheContainerSearch_ReportsGraphsTotal_OnlyThroughTheSharedRule`.

### 13.11 Suites (once, at the end, after every seed was restored)

Run 2026-10-05 03:31-04:00 on `20792c2bc` (every later commit touches only this note, the POML and the onboarding
guide's diagram), sequentially, each suite in full, on a machine other sessions were also using.

| Suite | Result |
|---|---|
| affected, during development (`Auth.SpeAdmin`, `Domain.SpeAdmin`, `SearchItemsTests`; ArchTests `SpeAdminContainerBindingGuardTests`; L2 H8 / DAG / H7 / provisioner) | green before seeding: 575 / 0 (BFF classes), 13 / 0 (guard), 41 / 0 (H8) |
| full BFF unit suite `tests/unit/Sprk.Bff.Api.Tests` | **14,733 passed / 0 failed / 54 skipped (14,787)** — 19 m 25 s. No contention failure. (+5 vs f2's 14,782 total.) |
| NetArchTest `tests/Spaarke.ArchTests` | **362 passed / 0 failed / 0 skipped** (f2: 360; +2 = the two binder-less-language guards) |
| `tests/integration/Sprk.Bff.Api.IntegrationTests` (full) | **104 passed / 0 failed / 0 skipped** |
| `tests/integration/Spe.Integration.Tests` (full) | **403 passed / 0 failed / 25 skipped (428)** |
| L2 `src/server/services/Sprk.Provisioning.ControlPlane.Tests` (full) | **1,599 passed / 0 failed / 1 skipped (1,600)** (f2: 1,583 / 1,584; +16 = AC30-AC40, 2 DAG, 1 H7, 2 provisioner) |

Code review / adr-check at this round's close (FULL rigor, self-review against the changed files): ADR-001 (no new
route) ✓ · ADR-002 (no plugin) ✓ · ADR-003 (fail closed: an incomplete H8 hands nothing to H7; an unrecordable or
inconsistent creation is quarantined, never re-created; H7 refuses an unbound container; an unbound container counts in no
view; the backfill refuses a non-conforming name before the vault; the guard refuses what it cannot judge) ✓ · ADR-004
(H8 stays idempotent per its key; a re-entry resumes instead of re-creating) ✓ · ADR-007 (Graph types stay in the
provisioner / `SpeAdminGraphService`) ✓ · ADR-008 (no new filter; the total rule is a list projection, the list
precedent) ✓ · ADR-010 (no new registration) ✓ · ADR-019 (no new HTTP code) ✓ · ADR-038 (no banned pattern; L2 over
fakes and a hand-written transport; BFF through the host; the response builder and the trim rule are pure) ✓. No §6.5
path needed.

### 13.12 Publish size and CVE (CLAUDE.md §10, hazards 1-4)

Each side exported with `git archive` into a SHORT path, `dotnet restore` + `dotnet publish -c Release --no-restore`
exactly as `scripts/Deploy-BffApi.ps1`, zipped with PowerShell **`Compress-Archive`** (Optimal) over the publish folder,
PDBs included:

| Side | Commit | Path | Zip | Files | MSB3030 |
|---|---|---|---|---|---|
| fresh `origin/master` (fetched 2026-10-05) | `293fcd4c8` | `C:\wt165vm` | **45.65 MB** (47,864,338 B) | 212 | 0 |
| task base (`task/uac-r2-165-f2`) | `a05e4de96` | `C:\wt165vp` | **45.70 MB** (47,919,795 B) | 212 | 0 |
| this branch | `20792c2bc` (src = the final commit's) | `C:\wt165vb` | **45.70 MB** (47,919,718 B) | 212 | 0 |

This round's own contribution (branch − base): **−77 B (0.00 MB)**. Branch vs fresh master: **+0.05 MB** (+55,380 B);
`293fcd4c8` is NOT an ancestor of this branch, so that figure also carries master's own drift since the work branch's
base. Ceiling 60 MB. Equal file counts, no MSB3030 — all three publishes complete. The three export directories were
removed afterwards. `dotnet list … package --vulnerable --include-transitive` (BFF): **no vulnerable packages**; no
`.csproj` / `.props` changed this round (BFF or L2).

**Conflict check:** `git merge-tree` of this branch into `work/unified-access-control-r2` @ `83460442d` — clean. Open PRs
(24) touching a changed path: #1286 (`scripts/README.md`, another section) — line-disjoint.

### 13.13 Decisions (this round)

- **D25 — H8's creation record is H8's own existing state** (`InterStepState.ContainerTypeId` + the T6 gate naming the root
  container), not a new `interStepState` key (design.md §6.2 locks the keys) and NOT the H7 hand-off. The pre-round-41
  pending path wrote exactly these fields, so its runs resume without a shim. **SUPERSEDED by D33 (round 49 item 2): the
  gate's evidence did not survive Cosmos, so the "existing state" was not a record at all in production; and the
  pre-round-41 runs' evidence was lost too — their record is `SpeContainerId`, moved at (5a).**
- **D26 — the record is persisted immediately after creation**, before verification: only then can no failure, wait,
  crash or lost write make a re-entry create again. A creation that cannot be recorded is quarantined with both ids.
  **Corrected by D34: "persisted" was true of the in-memory fake only; the record is now typed and the tests persist through
  the production serializer. A fault with no answer is now recorded too (D35).**
- **D27 — H7 depends on H8 in the DAG** (and checks H8's completion itself): H7 writes H8's container, which H8 hands off
  only once bound. During the replication wait H7, H10 and what follows wait; H9 and the other branches advance.
- **D28 — unbound containers are in no dashboard view** (round 41 item 2; reverses D18). "Unattributed" = bound to this
  environment's unit with no config of its type above it.
- **D29 — the Graph-total rule is ONE method** used by both searches, and the container search's response builder is
  proven directly — a host test cannot bite while the containers collection reports no total.
- **D30 — the creation guard fails closed on what it cannot judge** (a held builder, an unresolvable POST URI in a script
  that names the collection), and no binder-less language may name the collection at all. **REWORDED by D37 (round 49
  item 2): as written this was not true — five shapes passed (an absolute URL whose `//` the comment stripper ate, a
  class-level constant, a generic `PostAsJsonAsync<T>`, a cross-file PowerShell variable, `-Meth Post`). §14.3 states
  exactly what the guard now judges and what it cannot.**
- **D31 — the PowerShell secret-name rule is ONE module**, and every script that reads a config's secret judges the name
  first (the backfill did not).
- **D32 — the endpoint's format check keeps `$`**: the policy (`\z`) runs right after it and decides; tightening the format
  check too would change no outcome and so could not be proven.

### 13.14 Not closed

Only the manual live gates of §13.9 (live writes: the config repair with a minted credential, the backfill `-Apply` /
`-Bind`; the post-deploy probes; the next real L2 run) and f1's §11.16 (i)/(k). Nothing is deferred to another project.

## 14. Follow-up round f2-v2 — owner round 49 (the re-verification of `task/uac-r2-165-f2-v1`) (2026-10-05)

### 14.0 How the round reached the branch

- Branch **`task/uac-r2-165-f2-v2`** from `task/uac-r2-165-f2-v1` @ `a25463b55` (the name was free). No push, PR or merge.
- No `NOTE-FROM-MAIN.md`. Binding inputs: owner/main-session rounds 1-50 read from `work/unified-access-control-r2`.
  **Round 49** is the main session's decision on this task's f2-v1 re-verification: item 1 (tenant-/type-wide routes only
  in a Spaarke operator environment — §14.4) and item 2 (the verifier's criteria — §14.1-§14.3, §14.5). The verifier's
  items map: 1, 8, 9 (H8 record lost in Cosmos) → §14.1; 2, 9 (non-OData faults) → §14.2; 3, 11 (guard holes) → §14.3;
  10 (docs) → §14.5; 4-7 verified, nothing to do.
- `.claude/**`: no edit needed (no `.claude` file names H8's record, the gate-evidence converter, the creation guard or the
  operator marker).
- 167 still not landed on this branch: amendment 3 — no waiver / ledger / GovernedFiles edits. No route added, renamed or
  deleted; eight routes changed mechanism (§14.10).

### 14.1 Verifier item 1 (HIGH) — the H8 resume record survives the production Cosmos serializer

**Confirmed from code, exactly as reported.** `CosmosModule` builds the `CosmosClient` with
`CosmosSerializationOptions { CamelCase }` and no custom serializer, so runs are persisted by the SDK's Newtonsoft-based
default. `GateEntry.Evidence` is a `System.Text.Json.JsonElement?`; Newtonsoft writes the struct's one public property —
`{"valueKind":1}` — and reads that back as a DEFAULT element (`ValueKind` Undefined). Measured through the SDK's own
serializer (`BuildCosmosClient(…).ClientOptions.Serializer`, the production instance — not an equivalent): the evidence
stored is `{"valueKind":1}`; `ReadRecordedCreation` returned (type, `b!root`) before the round trip and (type, null) after.
Every re-entry therefore created a second root container and left the first unbound — round 41 item 1 was not met in
production. The H8 tests passed only because their `FakeRepository` handed back the in-memory object.

**Two further losses of the same root cause, found by this round's production-serializer test and fixed (scope found —
the POML's "scope discovered during execution is added to this task"):**
1. **Every gate's evidence** was lost the same way, and a run read back from Cosmos could not be serialized: STJ throws
   `InvalidOperationException` writing an Undefined `JsonElement`, so **`GET /api/runs/{id}` failed for every run that had
   passed a gate with evidence** (measured: STJ throws on the round-tripped run).
2. **Dictionary keys were camel-cased.** The SDK's camelCase option is Newtonsoft's `CamelCasePropertyNamesContractResolver`
   (`ProcessDictionaryKeys = true`): `HandlerRetryAttempts["H9"]` persisted as `"h9"`, so `HandlerOutcomeApplier` read 0 and
   re-sent attempt 1 on every auto-retry — the same MessageId, which Service Bus duplicate detection drops (task 107's
   counter never counted); `RunParameters.NonSecret` ("persisted verbatim") was not, for an upper-case key.

**The fix (L2):**

| Where | Change |
|---|---|
| `Models/SpeContainerCreationRecord.cs` (new) + `InterStepState.SpeContainerCreation` | H8's record as TYPED fields: `rootContainerId`, `additionalContainerIds`, `containerTypeInDoubtSince`, `rootContainerInDoubtSince`, `owningBusinessUnitId` (completion), `status`, `updatedAt`. The type stays `InterStepState.ContainerTypeId`. Round 49: "persisted as typed fields — never as JsonElement evidence". |
| `H8SpeContainerTypeHandler.ReadRecordedCreation` | Reads ONLY the typed fields (an ArchTest forbids `Evidence` / `JsonElement` in it). |
| (5a) `AdoptPreRound41HandOff` | A pre-round-41 run's evidence was lost too; its only record of the root container is the (unbound) `SpeContainerId` it wrote. That id is MOVED into the typed record (as the root, or as a further container to bind when the record names a different root) and withdrawn, before any write of the entry. |
| `RecordCreationAsync` / `ApplyCreationRecord` / `MarkWaitingOnGateAsync` / `MarkCompleteAsync` | Write the typed record (completion: `status = bound`, `owningBusinessUnitId`); the record write uses `CancellationToken.None` (something was created). The merge-over-a-concurrent-write rule now refuses a document naming another type OR another root container than the one this entry started from. A bind that REMOVED the root container is recorded through this merge-safe write (before: a plain write a conflict could lose, leaving a deleted container on record — whose 404 reads as the replication wait). |
| `Models/NewtonsoftJsonElementConverter.cs` (new) on `GateEntry.Evidence` | Evidence is written as its JSON and read back as that JSON (date-shaped strings stay strings). Old documents read back as `{"valueKind":1}` — a valid object. Evidence is the operator's record; nothing resumes from it. |
| `Models/NewtonsoftVerbatimKeysDictionaryConverter.cs` (new) on `GateStates`, `HandlerRetryAttempts`, `RunParameters.NonSecret`/`.Secrets` | Keys persist verbatim; `HandlerRetryAttempts` compares case-insensitively so a run stored before the fix (`"h9"`) still counts. |
| `Modules/CosmosModule.BuildCosmosClient` (internal) | THE construction of the client; DI calls it; tests take its `ClientOptions.Serializer` as the production serializer (building opens no connection). |
| Tests | `Models/ProductionCosmosSerializer` (test helper over that serializer); the H8 `FakeRepository` stores the JSON the production serializer writes and every read deserializes a fresh run — the verifier's proof (AC30, AC31, AC32, AC35, AC36 red under round-41 code) is now the suite's own: seed L1 (the record field not persisted) reddens 17 tests. |

### 14.2 Verifier item 2 (MEDIUM) — no fault after the container type exists leaves an orphan type or an unbound root

**Confirmed:** `CreateAsync` caught only `ODataError`; an `HttpRequestException` (or `LinkedTimeout`'s
`OperationCanceledException`, which the handler's `when (ex is not OperationCanceledException)` did not catch either)
escaped after the type was created, the handler recorded nothing and called it "no confirmed external side effect", and a
resume created a second undeletable type. A container POST that timed out after Graph created the container left it
unrecorded and unbound.

**The fix:**
- **`GraphContainerTypeProvisioner.CreateAsync`** tracks the Graph call in flight and returns EVERY fault as a `Failure`,
  never an exception: an `ODataError` is Graph's own answer (nothing in doubt); anything else — a dropped connection, a
  client-side timeout, the caller's cancellation, a 2xx without an id — marks the write in flight
  `ContainerTypeInDoubt` / `RootContainerInDoubt` and reports the type it created. `ProvisionAsync` can now throw only
  before any Graph call (cert load), which the handler's diagnostic says.
- **A resume with a recorded type** LISTS the type's containers (`$filter=containerTypeId eq …`, every page; a 404 — the
  type not visible yet — is "none") and **adopts** one already there (the oldest; `Adopted`) instead of creating another;
  the others (`AdditionalContainerIds`) are bound to the same root unit — or removed — in the bind step (7d), before the KV
  write; one that is neither stays on record and quarantines the run. A failed list creates nothing (Resumable).
- **A root container in doubt** (`rootContainerInDoubtSince`): within the replication window (`RootContainerInDoubtWindow`
  = 24h) the provisioner creates nothing while the type lists no container (`RootContainerNotYetVisible` → WaitingOnGate);
  after it, a container that never appeared was not created, and one is created.
- **A container TYPE in doubt** (`containerTypeInDoubtSince`, no type recorded): QuarantineRequired
  `spe-container-type-creation-in-doubt` (new code), and H8 creates no type — the owning app cannot LIST container types
  app-only (403) and cannot delete one, so only an operator with a DELEGATED SharePoint Embedded admin token can tell. The
  diagnostic is the procedure: `GET /storage/fileStorage/containerTypes`, look for the owning app; record a type found in
  `interStepState.containerTypeId` (H8 then creates only a root container in it); clear the quarantine — the clear
  (`ClearedAt` after the in-doubt time, H8's quarantine) is the confirmation, and a re-delivered dispatch before it creates
  nothing.
- **`BindNewContainerAsync`**: a DELETE answering 404 is "removed" (the container is gone), so a resume never loops on
  binding a container that no longer exists.

### 14.3 Verifier items 3 / 11 — the creation guard's five surviving shapes, and D30 made true to what is enforced

All five bite now — as seeded snippets in the analyser tests AND as real files (seeds A1-A5, §14.8):

| Shape | Why it passed | What the guard does now |
|---|---|---|
| Absolute URL in a C# raw POST (`"https://graph…/fileStorage/containers"`) | `StripComments` (`//[^\n]*`) erased everything after `https:` | A small C# LEXER (`LexCSharp`): regular, verbatim, interpolated (nested holes), raw (`"""`, `$$` holes) and char literals, line and block comments. A `//` inside a string is not a comment; comments are blanked with offsets kept. |
| The URL in a class-level `const`, POSTed from a method | `EnclosingMethodBody` found no method around a field | Files are split into MEMBERS (`ParseMembers`: methods, ctors, properties, fields, constants, top-level statements as ONE body). A member holding a collection literal that does not POST but PROVIDES it as a value — a field / constant / property, or a method that is expression-bodied or RETURNS it — is a provider; every member in ANY file referencing a provider's name holds the collection too (to a fixpoint). |
| `PostAsJsonAsync<object>(url, …)` | the generic argument defeated `CSharpPostSignal` | the POST signal accepts balanced generic arguments. |
| A PowerShell URL variable defined in a dot-sourced helper | the posting script never named the collection | each script's context includes the files it dot-sources / imports (`. (Join-Path $PSScriptRoot '…')`, `. "$PSScriptRoot/…"`, `Import-Module ./…`, resolved and recursive); and a variable ANY script assigns the bare collection URL is the collection wherever it is used unassigned. |
| `-Meth Post` | `-Method` was matched literally | any `-Me…` / `-Cu…` prefix (PowerShell binds an unambiguous prefix: `-Me` is `-Method`, `-Cu` `-CustomMethod`), `-Ur…` for `-Uri` (with a space or a colon), and an HttpClient `.PostAsync(`. |

Also closed while there: a `using var r = await http.PostAsync("…/containers", c);` statement was blanked as if it were a
using DIRECTIVE (only directives are now blanked); a collection URL spelled outside any member is OPAQUE; a holder that
CALLS a member that POSTs (the URL passed along) is a create; a path that is just `"/containers"` / `$"{base}/containers"`
in a file whose strings name fileStorage is the collection (the C# twin of the PowerShell `"$base/containers"` rule).
Test projects (`*.Tests` under `src/`) are not scanned — they drive fake Graph transports that POST to the collection; the
guard never scanned `tests/`.

**D30, reworded to exactly what is enforced (D37):** the C# guard judges every `.cs` file under `src/` outside test
projects: (1) every SDK `FileStorage` builder must chain straight into a member, and the containers COLLECTION builder only
into `.GetAsync(` / `.PostAsync(` (optionally via `.WithUrl(…)`) — held, passed or constructed is refused; (2) a member
that carries the collection URL — a string literal spelling `…fileStorage/containers` (or `/containers` alone in a file whose
strings name fileStorage), or a reference to a member that provides it as its value, in any file — and POSTs, or calls a
member that POSTs, is a create; (3) every create must call `BindNewContainerAsync(` in the same member, or be the verified
H8 deferred binder; a collection URL outside any member is refused. The PowerShell guard judges every `.ps1`/`.psm1` under
`scripts/` and `src/`: in a script that names the collection — itself, through a file it dot-sources, or through a variable
some script assigns it — every POST whose URI is not provably another endpoint is a create, and must be followed by
`Invoke-SpeContainerBindOrRemove` in the same function with the binding module dot-sourced; the Graph SDK cmdlet is a create
anywhere. No other language under `src/` or `scripts/` may name the collection. **What the guard cannot see** (stated, not
claimed): a URL assembled at run time from fragments none of which spells the collection (`…fileStorage/containers`, or
`/containers` alone in a file that names fileStorage); a POST whose verb is not spelled in the member that carries the URL
nor in a `src/` member it calls (an HTTP method read from configuration, a third-party client's own method name);
reflection, dynamic dispatch, `Invoke-Expression`, code generated at build time. Each of those would need the collection
spelled nowhere the guard reads — which is why the binder-less-language bans and the per-script fail-closed rule remain.

### 14.4 Round 49 item 1 — tenant-wide and type-wide SPE admin routes only in a Spaarke operator environment

- **`SpeAdminOptions.PlatformOperatorEnvironment`** (the existing options class, existing `ValidateOnStart` chain): default
  `false`; a non-boolean value stops the host at startup (test).
- **`SpeAdminTenantScopeFilter`** (the existing group filter; no new filter): a route marked `SpeAdminPlatformOperatorOnly`
  (security alerts, secure score) OR `SpeAdminContainerTypeScope` (container-type permissions, consumers ×4, register) is
  refused FIRST — with the deployment flag read before any I/O — unless the deployment is a Spaarke-operated environment
  AND the caller's own business unit is the root: ONE `403 spe.admin.deny.platform_operator_required` for every other
  caller (a customer environment's root admin; a leaf admin anywhere), nothing read. The existing type rule then still
  requires the type to be the config's own and every config of it reachable (round 35 item 5's every-config rule keeps a
  reach for a root admin: a config whose unit is outside the hierarchy — test).
- **The other routes** whose answer could be read as tenant-wide were inventoried: every remaining `/api/spe` route is
  per-config / per-container (trimmed by binding) or per-environment (this environment's Dataverse rows); the delegated
  container-type routes (list / get / create types, settings, owners) act with the CALLER's own token — Graph authorizes them
  by the caller's SharePoint Embedded administrator role and the BFF lends no identity — so they are not gated (recorded in
  the topology doc).
- **Deployment surface:** `model2-full.bicep` `param speAdminPlatformOperatorEnvironment bool = false` (the setting is
  emitted only when true — a customer stamp does not carry it at all; compiled `model2-full.json` regenerated);
  `dev.bicepparam` sets it true. `config/environments.json`: `dev` declares `speAdminPlatformOperatorEnvironment: true`, the
  `_template` (customer) `false`. `scripts/Deploy-BffApi.ps1` (pre-deploy): declared true → the App Service setting is made
  true on the slot(s) it deploys to (a swap carries it); declared false/absent and a live `true` → the deploy FAILS (a
  customer environment must never carry it). `SpeAdminOperatorEnvironmentMarkerGuardTests` (ArchTest): the marker is named
  only by the BFF and this deployment surface (never by the L2 control plane, the canonical app-settings catalog, the
  customer / Model 1 Bicep), the registry declares it only for `dev`, the stack defaults to false and only `dev.bicepparam`
  sets it.
- **Docs:** topology doc (the routes, the marker, customer environments never carry it, the delegated routes) and the
  customer deployment guide §6.5.4.
- **Not decided here — named for the main session:** which environment is "Spaarke's own operator environment". Only `dev`
  is known to be Spaarke-operated; the stopped `spaarke-bff-prod` ("demo" in `config/environments.json`) is the candidate.
  Until it is named, it does not carry the marker (fail closed: its tenant-/type-wide routes refuse). Gate (e) carries the
  exact change. **→ Decided by round 57 item 1 (option c): only `dev` for now — §15.1.**
- Observed, not changed (another project's): `az bicep build-params` on `dev.bicepparam` fails BCP332 on
  `customerId = 'spaarkedev1'` (11 > the 8-char standard of `3293ee421`) — pre-existing on the base; the file's own header
  says this stack does not describe the running dev environment.

### 14.5 Verifier item 10 — the docs now say what the code does

§13.1 (correction box), D25, D26, D30 (marked, superseded by D33-D37), the onboarding guide (H8 row; §7.5 "The replication
wait and every other resume" rewritten: typed record, list-and-adopt, in-doubt handling, the operator procedure; change
log), the topology doc (the H8 creation bullet; the guard's shapes), and
`projects/customer-provisioning-orchestration-r1/notes/uac-r2-165-h8-container-stamp.md` (round 49 header, the change
table, the tests, a "Correction (round 49)" section saying what the code still cannot see, gate (d) reads the typed
record). Gate (d) is rewritten in §14.9.

### 14.6 Placement (CLAUDE.md §10) and new surface (CLAUDE.md §11)

**Placement:** BFF — on existing surfaces only: the existing `/api/spe` group filter (`SpeAdminTenantScopeFilter`), the
existing options class (`SpeAdminOptions`, existing `ValidateOnStart` chain), a doc comment on `SecurityEndpoints`. No new
endpoint, filter class, policy, service, DI registration, job, package, Dataverse column or plugin (ADR-002). ONE new option
member, `SpeAdmin:PlatformOperatorEnvironment`, decided by round 49 item 1 ("a BFF DEPLOYMENT setting … validated at
startup … default false"). No AI type touched (ADR-013). L2 — H8's existing handler and provisioner seam, L2's existing
models and `CosmosModule`. Deployment surface — the existing Bicep stack, `config/environments.json`,
`scripts/Deploy-BffApi.ps1`. No csproj / props / package change (BFF, L2 or tests).

| New | Existing (grep evidence) | Extension? | Cost of doing nothing |
|---|---|---|---|
| `SpeContainerCreationRecord` + `InterStepState.SpeContainerCreation` | the T6 gate's evidence (lost in Cosmos); `SpeContainerId` (H7's hand-off — must never hold an unbound container); `ContainerTypeId` (type only) | a controlled extension of the existing enumerated `InterStepState` (the `ImportedSolutions` / `SpeContainerId` precedent); no existing typed field can carry an unbound root, further containers and in-doubt markers | every H8 resume creates a second root container and leaves the first unbound (verifier item 1; seed L1: 17 red) |
| `NewtonsoftJsonElementConverter` | none — `grep JsonConverter` over `Models/`: only `StringEnumConverter` | an attribute on the existing property | every gate's evidence is `{"valueKind":1}` in Cosmos and `GET /api/runs/{id}` throws on a read-back run (seed L2: 7 red) |
| `NewtonsoftVerbatimKeysDictionaryConverter<T>` | none | attributes on the existing dictionary properties | the retry counter never counts (`"H9"` → `"h9"`), every auto-retry is dropped as a duplicate; run-parameter keys lowered (seeds L15, L16) |
| `CosmosModule.BuildCosmosClient` (internal) | the inline builder in `AddCosmosModule` | extraction of the same code | the tests cannot use the production serializer — the reason item 1 went unseen |
| provision request `RootContainerCreationInDoubt`; outputs `AdditionalContainerIds`, `Adopted`; failure `ContainerTypeInDoubt`, `RootContainerInDoubt`; outcome `RootContainerNotYetVisible` | the provisioner seam's records (`ISpeContainerTypeProvisioner`), ONE implementation + the tests' fake | members of the existing records / one case of the existing outcome union | an orphan undeletable type or an unbound root after a fault with no answer (verifier item 2; seeds L6-L11, L18) |
| `GraphContainerTypeProvisioner.ListContainersOfTypeAsync` (private) | the BFF's `SpeAdminGraphService.ListContainersPageAsync` (another assembly — L2 cannot reference the BFF) | private step of the existing `CreateAsync` | a resume cannot adopt a container created by a lost answer (seed L9) |
| code `spe-container-type-creation-in-doubt` | `SpeContainerTypeRejectionCodes` | one constant in the existing class | a type that may exist is created again — undeletable, capped per tenant (seed L5) |
| H8 `AdoptPreRound41HandOff`, `QuarantineClearedSince`, `ContainerTypeInDoubtDiagnostic`, `BindAdditionalContainersAsync`, `ApplyCreationRecord`, records `CreationUpdate` / `RecordedCreation`, `RootContainerInDoubtWindow` | the handler's own record/resume steps | handler-private | pre-round-41 runs lose their container (L3); adopted containers stay unbound (L12); the in-doubt rules cannot be applied |
| `SpeAdminOptions.PlatformOperatorEnvironment` | `SpeAdminCallerScope.IsPlatformOperator` (root of THIS environment — every Model 1 customer has one) | a member of the existing options class (not a new options class) | a customer's root admin reads the whole tenant's security data and every customer's consuming apps of a shared type (round 49 item 1; seeds B1-B6) |
| `SpeAdminTenantScopeFilter.RequireSpaarkeOperatorAsync`, `SpaarkeOperatorRequiredDetail`, ctor parameter | `RequirePlatformOperatorAsync` (kept, reused) | one private step in the existing filter | as above |
| Bicep `speAdminPlatformOperatorEnvironment`; `environments.json` key; `Deploy-BffApi.ps1` pre-deploy block | the stack's `appSettings`; the registry's per-environment keys; the deploy script's CORS gate (the precedent) | parameters / keys / a step of the existing surfaces | round 49 requires the marker set "through the deployment scripts and Bicep"; without the check a customer environment could carry it unnoticed |
| test helpers: `ProductionCosmosSerializer`; fixtures `AdminSurfaceCustomerEnvironmentHostFixture` / `…Unmarked…` / `…MalformedMarker…` (subclasses of the existing host) | `AdminSurfaceHostFixture` (unsealed, one virtual property) | subclasses | the marker's three states are unprovable through the real host |
| ArchTest `SpeAdminOperatorEnvironmentMarkerGuardTests` | `CorsOriginRegistryTests` (registry precedent) | new guard class | customer provisioning could start emitting the marker unseen (seeds A12-A14) |

**Complexity (§11.5):** `H8SpeContainerTypeHandler` grew by its own record/resume steps (one responsibility: drive H8's
creation to a bound, persisted container exactly once, through every fault); `GraphContainerTypeProvisioner.CreateAsync`
grew by the list-and-adopt step and the fault classification of the same calls. The creation guard grew a lexer and a member
parser — test code, one responsibility (find every route to the collection).

### 14.7 Tests added / changed this round — one-line justifications (test-scope criterion)

All L2 tests: handler tests with hand-written fakes, the provisioner over a hand-written fake Graph transport, the
serializer tests over the production serializer. BFF tests through the real host. No `Mock<HttpMessageHandler>`, no
DI-registration test, no constructor null-check test (ADR-038).

- **L2 `H8SpeContainerTypeHandlerTests`**: the `FakeRepository` persists through the production serializer (fresh object per
  read; `StoredJson`; `Mutate` for an operator's write); AC22 asserts the typed record; AC35 uses the document a
  pre-round-41 run really has (`{"valueKind":1}` evidence); AC36 typed + legacy; AC40 asserts the moved record; **AC41-AC50**
  (§ customer-provisioning note): the record is typed in the stored JSON; root POST with no answer → recorded, the resume
  waits; past the window → creates; adoption binds root + further containers before the hand-off; a further container
  neither bound nor removed quarantines and is bound on resume; type in doubt → quarantined, a re-delivered dispatch creates
  nothing, only the operator's clear lets a type be created; a type the operator recorded is resumed; a pre-round-41
  hand-off next to a different typed root is bound too; a removal record survives a concurrent write; an unrecordable
  in-doubt record is quarantined.
- **L2 `GraphContainerTypeProvisionerBindTests`** (+10, one changed: the reuse test now lists the type first): the
  verifier's probe (HttpRequestException on the root POST) → Failure with the type and the container in doubt; a
  client-side timeout and the caller's cancellation → the same; a type POST with no answer (drop / 2xx without id) → type
  in doubt; an `ODataError` → nothing in doubt; adoption of one / several across pages (oldest first); in-doubt root not
  listed (200 empty / 404) → `RootContainerNotYetVisible`; a failed list creates nothing; a DELETE 404 is "removed".
- **L2 `Models/ProvisioningRunProductionSerializerTests`** (8): fully-populated run round-trips unchanged; evidence
  survives; a read-back run serializes for GET; a pre-converter document reads back; the typed record survives; the retry
  counter keeps `"H12b"`; a legacy `"h9"` still counts; run-parameter keys verbatim.
- **BFF `SpeAdminOperatorEnvironmentMarkerTests`** (4 classes, 34 cases): customer environment (marker false) — a root
  admin gets the uniform 403 on all 8 routes, nothing read, not even `systemusers`; marker missing — the same; operator
  environment — a leaf admin gets the 403 on all 8, a root admin reaches all 8 handlers; exactly these 8 routes carry the
  rule; a non-boolean marker stops the host at startup.
- **BFF changed**: `SpeAdminPerContainerScopeTests` (leaf admins on type routes now get the operator 403; the type-not-found
  rule judged for a root admin; NEW `ARootAdmin_OnATypeAConfigOutsideTheHierarchyAlsoCarries_IsStill403Shared` keeps round
  35 item 5's every-config rule provable; the leaf "passes the type rule" test is now its refusing counterpart);
  `SpeAdminConfigSecretNameTests` (the type-wide route proven for a root admin — NEW
  `ATypeWideCredentialRoute_ForARootAdminOfAnOperatorEnvironment_…_Is409`); `SpeAdminConfigAndBulkTenantScopeTests` (the
  register host check judged for a root admin, and the positive test now asserts no SPE admin rule refused it — it had
  passed vacuously). `AdminSurfaceHostFixture` starts as an operator environment (dev); three subclasses.
- **ArchTests `SpeAdminContainerBindingGuardTests`**: lexer + member parser + providers + generic POST + PowerShell
  includes / collection variables / abbreviations (§14.3); seeded snippets: +7 C# (absolute URL, class-level constant,
  generic POST, URL from a helper, relative to a base, `using` declaration, top-level statements; strings-are-not-comments
  and a nextLink caller that POSTs elsewhere pass), across-files, +4 PowerShell (`-Meth`, `-Ur:`/`-Me:`, `-CustomMethod`,
  HttpClient) and the cross-file dot-sourced variable (resolved and unresolved); `TheDeferredBinderIsReal` pins the order
  incl. the adopted-container bind and forbids `Evidence`/`JsonElement` in `ReadRecordedCreation`.
- **ArchTests `SpeAdminOperatorEnvironmentMarkerGuardTests`** (3): who may name the marker; the registry declares it only
  for `dev` (template false); the stack defaults false and only `dev.bicepparam` sets it.

### 14.8 Seeding proofs (on the final code; `scratchpad/f2v2/seed.py`, session scratchpad)

Each seed: ONE exact single-occurrence replacement (anchor found exactly once, LF or CRLF) or ONE set of added files; the
named classes run (L2: H8 / provisioner / production-serializer / serializer-contract / DAG / H7 / outcome-applier; BFF:
every `Auth.SpeAdmin` class; ArchTests: `SpeAdminContainerBindingGuardTests` + `SpeAdminOperatorEnvironmentMarkerGuardTests`);
the source restored from a byte copy, MD5-checked and touched (added files deleted). `git status` was clean afterwards.
**All 39 RED; none failed to compile.** Counts are DISTINCT failing test names (theory cases whose display names xUnit
truncates with `···` collapse into one).

| # | Seed | RED |
|---|---|---|
| L1 | the typed creation record does not survive the production serializer (`[Newtonsoft.Json.JsonIgnore]` on `SpeContainerCreation`) — **the verifier's item 1, as a field that does not persist** | 17: H8 AC22, AC30, AC31, AC32, AC35, AC36(typed), AC40-AC48; serializer: fully-populated round trip, typed record |
| L2 | gate evidence stored as `{"valueKind":1}` again (converter removed) | 7: H8 AC1, AC22, AC41; serializer: pre-converter document, round trip, GET serialization, evidence |
| L3 | a pre-round-41 hand-off withdrawn without being moved into the record | 4: AC35, AC36(legacy), AC40, AC48 |
| L4 | a root container in doubt never waited for (the handler passes false) | AC42 |
| L5 | a container type in doubt created again without the operator's clear | AC46 |
| L6 | the provisioner does not report a container type in doubt | `AContainerTypePostWithNoAnswer_…(drops: True)` |
| L7 | a non-OData fault escapes `CreateAsync` (only `ODataError` caught) — **the verifier's item 2** | 4: non-OData fault, client timeout, caller cancellation, type POST drop |
| L8 | the provisioner does not report a root container in doubt | 3: non-OData fault, timeout, cancellation |
| L9 | a container already in the recorded type is not adopted | 2: adopt one; several across pages |
| L10 | the provisioner creates while a root container in doubt may still appear | 2: 200-empty / 404 |
| L11 | a failure's type / in-doubt state is not recorded | 4: AC34, AC42, AC46, AC50 |
| L12 | adopted further containers are not bound | 3: AC44, AC45, AC48 |
| L13 | the removal record is not merge-safe | AC49 |
| L14 | completion does not record the owning business unit | AC44 |
| L15 | the retry counter's keys lowered again (verbatim converter removed) | 2: fully-populated round trip; `"H12b"` kept |
| L16 | the retry counter case-sensitive (a legacy `"h9"` misses) | legacy-key test |
| L17 | a DELETE that finds the container gone is not "removed" | `ARemovalThatFindsTheContainerAlreadyGone_CountsAsRemoved` |
| L18 | the handler ignores `RootContainerNotYetVisible` | AC42 |
| L19 | `ReadRecordedCreation` also reads gate evidence | ArchTest `TheDeferredBinderIsReal` |
| B1 | the deployment marker is not checked | 12 distinct (16 cases): customer-environment and marker-missing root admins on all 8 routes |
| B2 | the container-type routes are not under the operator rule | 19 distinct: customer / missing / leaf on the 6 type routes; the per-container leaf-admin type tests |
| B3 | the filter is not given the deployment setting (`… || true`) | 12 distinct (16 cases) |
| B4 | a missing marker defaults to true | 6 distinct (8 cases): marker-missing root admins |
| B5 | the root check is skipped in an operator environment | 19 distinct: every leaf / unresolvable-unit refusal on the 8 routes |
| B6 | the SpeAdmin options not validated on start | `AMarkerThatIsNotABoolean_StopsTheHostAtStartup` |
| A6 | the lexer no longer knows string literals (a URL's `//` is a comment again) | C# analyser seeded snippets |
| A7 | the C# guard no longer follows a provider | C# analyser seeded snippets |
| A8 | the C# POST signal no longer accepts generic arguments | C# analyser seeded snippets |
| A9 | the PowerShell guard does not read dot-sourced files | script analyser (cross-file, resolved) |
| A10 | the PowerShell guard ignores variables another script assigns the collection | script analyser (cross-file, unresolved) |
| A11 | the PowerShell POST signal no longer knows `-Meth` / `-CustomMethod` | script analyser |
| A12 | the registry declares a second operator environment (`demo` true) | registry guard |
| A13 | the Bicep stack defaults the marker to true | stack guard |
| A1 | **the verifier's absolute-URL raw POST** as a new `.cs` under `src/` | `Every SPE container created anywhere in src/ is bound …` |
| A2 | **the verifier's class-level constant URL** POSTed from a method, as a new `.cs` | same |
| A3 | **the verifier's `PostAsJsonAsync<object>`** as a new `.cs` | same |
| A4 | **the verifier's cross-file PowerShell variable** (a helper defining `$ContainersUri` + a script dot-sourcing it and POSTing `-Uri $ContainersUri`) as new scripts | `Every SPE container a script creates is bound …` |
| A5 | **the verifier's `-Meth Post`** as a new script | same |
| A14 | the canonical app-settings catalog names the operator marker | `The operator-environment marker is named only by …` |

### 14.9 Manual gates (main session; dev; live writes only with the owner's approval) — the CURRENT list

From the repo root, `az login` as an operator who can read Dataverse and the vaults:

- **(b) first — config secret-name repair** (round 41 item 4; dev config `68f9a952…`): unchanged from §13.9 (b).
- **(e) NEW — the Spaarke-operator marker (round 49 item 1) — BEFORE deploying this branch's BFF to dev** (otherwise dev's
  root admins get the 403 on the 8 tenant-/type-wide routes until it is set — fail closed, not harmful):
  1. read-only: `az webapp config appsettings list -g rg-spaarke-dev -n spaarke-bff-dev -o json` and
     `… --slot staging -o json` — filter for `SpeAdmin__PlatformOperatorEnvironment` in PowerShell (not a `--query` with
     parentheses: the az.cmd shim breaks it — the Deploy-BffApi.ps1 note);
  2. write (Entra-free, App Service config — the app restarts):
     `az webapp config appsettings set -g rg-spaarke-dev -n spaarke-bff-dev --settings SpeAdmin__PlatformOperatorEnvironment=true`
     and the same with `--slot staging` — or simply the next `scripts/Deploy-BffApi.ps1` run for `dev`, which now sets it
     (dev is declared in `config/environments.json`);
  3. verify: step 1 shows `true` on both slots.
  4. **Spaarke's own operator environment** — DECIDED by round 57 item 1 (§15.1): only `dev` carries the marker for now; the
     change that STANDS UP Spaarke's production operator environment (the stopped `spaarke-bff-prod`, `demo` in the registry,
     now declared `false`) is the change that does what follows — it is not a gate of this task: add `"speAdminPlatformOperatorEnvironment": true`
     to its `config/environments.json` entry (the key `Deploy-BffApi.ps1 -Environment` resolves), `param
     speAdminPlatformOperatorEnvironment = true` to its `.bicepparam`, its name to
     `SpeAdminOperatorEnvironmentMarkerGuardTests.SpaarkeOperatedEnvironments`, and set the App Service setting as in 2.
  5. **Every customer BFF** (none deployed today besides the stopped prod/demo): step 1 must show NO such setting; a deploy
     through `Deploy-BffApi.ps1` now fails if one carries it undeclared.
- **(a) container binding gate — BLOCKS deploying this branch's BFF**: unchanged from §13.9 (a) (backfill dry run →
  `-Apply` → the decided `-Bind` of the three dev test containers → `-Verify` exit 0).
- **(c) probes after deploy** — as §13.9 (c), plus: as a root admin of dev, `GET $BFF/api/spe/security/score?configId=…`
  does NOT answer `403 spe.admin.deny.platform_operator_required`; as a leaf admin it does.
- **(d) the next real L2 provisioning run** — REWRITTEN (the evidence the old wording read did not persist):
  0. the L2 deploy carrying this branch precedes the run (the typed record, the evidence and dictionary converters);
     runs already in Cosmos read back unchanged (old evidence reads as `{"valueKind":1}`, old retry keys still count);
  1. H8 completes WITHOUT `spe-root-business-unit-unresolved` (a 401/403 names the status — then L2's identity lacks a role
     reading `businessunit` in the new environment);
  2. `GET /api/runs/{id}?customerId=…` answers 200 (it threw on a run with gate evidence before round 49) and shows
     `interStepState.speContainerCreation` = `{ status: "bound", rootContainerId: <= interStepState.speContainerId>,
     owningBusinessUnitId: <the environment's root unit> }`; `GET …/storage/fileStorage/containers/{root}?$select=customProperties`
     shows the stamp;
  3. if the run hits the replication wait: `WaitingOnGate`, `interStepState.containerTypeId` set,
     `speContainerCreation.rootContainerId` = the root container and `status = "replication-pending"`,
     `interStepState.speContainerId` EMPTY, H7 not dispatched; after the wait exactly ONE container type owned by the
     customer's BFF app (`GET /storage/fileStorage/containerTypes` with a DELEGATED SharePoint Embedded admin token — app-only
     answers 403) and ONE root container in it, and only then is `speContainerId` set and H7 dispatched;
  4. if the run is ever quarantined `spe-container-type-creation-in-doubt`: follow the diagnostic (delegated list of
     container types for the owning app → record a found type in the run document's `interStepState.containerTypeId` →
     `POST /api/runs/{id}/clear-quarantine?reason=…` → resume).
- Still open from f1: §11.16 (i) deleted-container binding read, (k) dashboard per-config fields.

### 14.10 Route authorization ledger input (task 167; still not landed — amendment 3, no waiver edits)

No route added, renamed or deleted. Eight routes now answer to the Spaarke-operator rule FIRST (mechanism: the existing
filter `SpeAdminTenantScopeFilter`; the deployment marker, then the root check; the configId and container-type rules after):

| Route | Mechanism | Deny tests |
|---|---|---|
| `GET /api/spe/security/alerts`, `GET /api/spe/security/score` | filter — Spaarke-operator rule (marker + root) | `Sprk.Bff.Api.Tests.Auth.SpeAdmin.SpeAdminOperatorRoutes_InACustomerEnvironment_Tests.ARootAdminOfACustomerEnvironment_GetsTheUniform403_BeforeAnythingIsRead`, `…SpeAdminOperatorRoutes_InASpaarkeOperatedEnvironment_Tests.ALeafAdminOfAnOperatorEnvironment_GetsTheUniform403` |
| `GET /api/spe/containertypes/{typeId}/permissions`, `GET`/`POST /api/spe/containertypes/{typeId}/consumers`, `PUT`/`DELETE /api/spe/containertypes/{typeId}/consumers/{appId}`, `POST /api/spe/containertypes/{typeId}/register` | filter — Spaarke-operator rule, then the container-type rule | the same two, plus `…SpeAdminPerContainerScopeTests.ARootAdmin_OnATypeAConfigOutsideTheHierarchyAlsoCarries_IsStill403Shared` |

### 14.11 Suites (once, at the end, after every seed was restored)

Run 2026-10-05 sequentially on the final source (`dcef29099`; later commits touch only this note, the POML and a test's doc
comment), each suite in full, on a machine other sessions were also using. No contention failure.

| Suite | Result |
|---|---|
| affected, during development (L2 H8 / provisioner / serializer / DAG / H7 / outcome-applier; BFF `Auth.SpeAdmin` + `SearchItemsTests`; ArchTests creation + marker guards) | green before seeding: 155 / 0 (L2 classes), 829 / 0 (BFF), 13 + 3 / 0 (ArchTests guards) |
| full BFF unit suite `tests/unit/Sprk.Bff.Api.Tests` | **14,769 passed / 0 failed / 54 skipped (14,823)** — 22 m. (+36 vs f2-v1's 14,787: the 34 marker cases + 2 new tests.) |
| NetArchTest `tests/Spaarke.ArchTests` | **365 passed / 0 failed / 0 skipped** (f2-v1: 362; +3 marker guard) |
| `tests/integration/Sprk.Bff.Api.IntegrationTests` (full) | **104 passed / 0 failed / 0 skipped** |
| `tests/integration/Spe.Integration.Tests` (full) | **403 passed / 0 failed / 25 skipped (428)** |
| L2 `src/server/services/Sprk.Provisioning.ControlPlane.Tests` (full) | **1,630 passed / 0 failed / 1 skipped (1,631)** (f2-v1: 1,599 / 1,600; +31 = AC41-AC50 + the AC36 theory row, 12 provisioner cases, 8 serializer tests) |

Code review / adr-check (FULL rigor, self-review against the changed files): ADR-001 (no new route; Minimal API untouched)
✓ · ADR-002 (no plugin) ✓ · ADR-003 (fail closed: a missing marker refuses; a non-boolean stops the host; a type in doubt
creates nothing until an operator acts; a root in doubt is waited for; an unrecordable in-doubt record is quarantined;
every fault after a Graph write is recorded, never thrown; the guards refuse what they cannot follow) ✓ · ADR-004 (H8 stays
idempotent per its key; a re-entry resumes, lists and adopts instead of creating) ✓ · ADR-007 (Graph SDK types stay in the
provisioner) ✓ · ADR-008 (the operator rule is in the existing group filter, metadata-driven; no middleware, no
per-handler check) ✓ · ADR-010 (no new DI registration; one options member on the existing options class) ✓ · ADR-019
(the 403 is `ProblemDetailsHelper.Forbidden`, the existing shape) ✓ · ADR-028 (no auth flow changed; the BFF lends no
identity to the delegated container-type routes) ✓ · ADR-038 (no banned pattern; the persistence proof uses the production
serializer, not a hand-picked equivalent; BFF through the real host; L2 over hand-written fakes / transport) ✓ · ADR-052 (no
background work added) ✓. No §6.5 path needed.

### 14.12 Publish size and CVE (CLAUDE.md §10, hazards 1-4)

Each side exported with `git archive` into a SHORT path (`src/server`, `config` — the BFF embeds
`config/secure-record-owner-role.json` — and the root build files), no prior build output, `dotnet restore` + `dotnet
publish -c Release --no-restore` exactly as `scripts/Deploy-BffApi.ps1`, zipped with PowerShell **`Compress-Archive`**
(Optimal) over the publish folder, PDBs included:

| Side | Commit | Path | Zip | Files | MSB3030 |
|---|---|---|---|---|---|
| fresh `origin/master` (fetched 2026-10-05) | `b4b58a361` | `C:\wt165xm` | **45.65 MB** (47,864,525 B) | 212 | 0 |
| task base (`task/uac-r2-165-f2-v1`) | `a25463b55` | `C:\wt165xb` | **45.70 MB** (47,919,789 B) | 212 | 0 |
| this branch | `dcef29099` (the final source) | `C:\wt165xn` | **45.70 MB** (47,920,676 B) | 212 | 0 |

This round's own contribution (branch − base): **+887 B (0.00 MB)**. Branch vs fresh master: **+0.05 MB** (+56,151 B);
`b4b58a361` is NOT an ancestor of this branch, so that figure also carries master's drift since the work branch's base.
Ceiling 60 MB. Equal file counts, no MSB3030 — all three publishes complete. The three export directories were removed
afterwards. `dotnet list … package --vulnerable --include-transitive`: **no vulnerable packages** (BFF and L2 Core); no
`.csproj` / `.props` changed this round.

**Conflict check:** `git merge-tree` of this branch into `work/unified-access-control-r2` @ `0caca7e3e` — clean. Open PRs
(23) touching a path this round changed: **#1298** (`work/customer-provisioning-orchestration-r1` → master) also edits
`SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` and `SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md`, and a trial merge with its head also
conflicts in `infrastructure/bicep/stacks/model2-full.bicep` / `.json` / `dev.bicepparam` — besides files earlier 165 rounds
changed (`ConfigEndpoints.cs`, `SpeAdminGraphService.cs`, …): the two work branches have diverged. Whichever integrates
second resolves; for `model2-full.json`, resolve the `.bicep` and regenerate it (`az bicep build --file
infrastructure/bicep/stacks/model2-full.bicep --outfile infrastructure/bicep/stacks/model2-full.json`); keep this round's
`speAdminPlatformOperatorEnvironment` parameter and its `union(…)` on the BFF `appSettings`.

### 14.13 Decisions (this round)

- **D33 — H8's resume record is TYPED** (`InterStepState.SpeContainerCreation`, a controlled schema extension) and H8 never
  resumes from gate evidence; a pre-round-41 run's record is the `SpeContainerId` it wrote, moved at (5a). Supersedes D25.
- **D34 — persistence is proven through the PRODUCTION serializer** (`CosmosModule.BuildCosmosClient(…).ClientOptions.Serializer`),
  not an equivalent: the H8 fake repository stores what Cosmos would and every read is a fresh deserialization. Corrects D26.
- **D35 — every fault after a Graph write is a recorded outcome.** Graph's `ODataError` = nothing in doubt; anything else =
  the write in flight is in doubt. A TYPE in doubt is QuarantineRequired until an operator's delegated check (app-only can
  neither list nor delete container types — so H8 never creates a second one on a guess); a ROOT in doubt is waited for
  through the 24h window; a recorded type is listed and a container already in it adopted (the others bound or removed)
  before a root container is created.
- **D36 — gate evidence and dictionary keys persist as written** (Newtonsoft converters on `GateEntry.Evidence` and the
  run's dictionaries) — scope found by the production-serializer test: `GET /api/runs/{id}` threw on any run with gate
  evidence, and the reconciler's retry counter never counted.
- **D37 — D30 reworded to exactly what is enforced** (§14.3), naming what the guard cannot see; test projects under `src/`
  are outside the C# guard (`tests/` always was).
- **D38 — tenant-wide and type-wide SPE admin routes: a root admin of a Spaarke-operated environment only** (deployment
  marker `SpeAdmin:PlatformOperatorEnvironment`, default false, validated on start; the uniform 403 first). The delegated
  container-type routes are not gated (Graph decides by the caller's own role; the BFF lends no identity). Round 35 item 5's
  every-config type rule stays (it still reaches a config whose unit is outside the hierarchy). **→ ACCEPTED by round 57
  item 2 (option a) — D40, §15.2.**
- **D39 — the marker is set only through Spaarke's deployment surface** (Bicep param default false; `environments.json`
  declaration; `Deploy-BffApi.ps1` sets it when declared and FAILS a deploy to an undeclared environment carrying it); an
  ArchTest keeps customer provisioning from naming it.

### 14.14 Not closed

Only the manual live gates of §14.9 — (e) the marker on dev's two slots (an App Service configuration write), (b) the config
repair with a minted credential, (a) the backfill `-Apply` / `-Bind`, (c) the post-deploy probes, (d) the next real L2 run —
and f1's §11.16 (i)/(k). One decision input for the main session, not a code gap: which environment is "Spaarke's own operator
environment" (only `dev` is known) — until it is named it does not carry the marker, so its tenant-/type-wide routes refuse
(fail closed); gate (e) step 4 is the complete change. Nothing is deferred to another project. **→ SUPERSEDED by §15
(round 57): the decision input is answered (only `dev`), and §15.9 is the current "not closed".**

## 15. Final fix round h — round 57 (the final re-verification of `task/uac-r2-165-f2-v2`), under owner round 56 (2026-10-05)

### 15.0 How the round reached the branch

- Branch **`task/uac-r2-165-h`** from `task/uac-r2-165-f2-v2` @ `fd8ae6c23` (the name was free). No push, PR or merge.
- No `NOTE-FROM-MAIN.md`. Binding inputs read from `work/unified-access-control-r2`: rounds 1-55 (kept, nothing reverted),
  **owner round 56** (classify every finding; fix (a)-(c) in the lane, record (d)-(f) as known limits; the over-engineering
  check; the round cap — this is task 165's ONE further round, so whatever an (a)-(c) item leaves goes to the main session)
  and **round 57** items 1-4 (this section, one sub-section each). Nothing else was changed.
- `.claude/**`: no edit needed (no `.claude` file names the operator marker, its registry key or the deploy script's parse).
- 167 still not landed on this branch: amendment 3 — no waiver / ledger / GovernedFiles edits. No route added, renamed,
  deleted, or given a different mechanism (§15.8).

### 15.1 Round 57 item 1 — only `dev` carries the operator marker (option c)

The code already held the smallest fail-closed choice — only `dev` declared, the stack default `false`, only
`dev.bicepparam` setting it, the guard's list `{ "dev" }` — so this item makes every surface SAY so and replaces the open
"which environment?" question (§14.4, gate (e) step 4, §14.14) with the decision:

| Surface | Now says |
|---|---|
| `config/environments.json` | `dev`: true, "today the ONLY one", must be a JSON boolean. **`demo`** (the stopped `spaarke-bff-prod`, the candidate): declared **`false`** explicitly, with the stand-up recipe (registry `true`, `.bicepparam` line, the guard's list, the App Service setting). `_template`: "today only dev; the production operator environment adds it in the change that stands it up". |
| `infrastructure/bicep/stacks/dev.bicepparam` | dev is the ONLY parameter file that sets it today; the production operator environment adds the line to its own file when stood up. (`stacks/prod.bicepparam` is untouched: it does not name the marker, so it is `false` — and naming it there in a comment would only widen the guard's allow-list.) |
| `SpeAdminOperatorEnvironmentMarkerGuardTests.SpaarkeOperatedEnvironments` | `{ "dev" }` (unchanged); its doc comment states round 57's rule and what the stand-up change adds. |
| `SpeAdminOptions.PlatformOperatorEnvironment` doc; `Deploy-BffApi.ps1` block comment | today only dev; the production operator environment from the change that stands it up. |
| Topology doc (§ tenant-wide routes); customer deployment guide §6.5.4 (+ change-log row) | today only `dev`; `demo` declares `false`; the four things the stand-up change adds; until then every other environment refuses the eight routes. |

The model2-full.bicep parameter description ("TRUE ONLY for a Spaarke-operated environment …") is unchanged: it states who
may set it, not who does, and editing it would force a regenerated `model2-full.json` into the #1298 conflict for no change in
meaning.

### 15.2 Round 57 item 2 — D38 accepted (option a)

**D40:** the DELEGATED container-type routes (list / get / create types, settings, owners) stay outside the operator marker.
Graph authorizes them with the caller's OWN token and the BFF lends no identity, so the caller's SharePoint Embedded
administrator role is the boundary; gating them would only remove legitimate screens. D38 is marked accepted (§14.13); the
topology doc now says the delegated routes are ungated "by decision (round 57 item 2)". No code change.

### 15.3 Round 57 item 3 (class a) — the deploy script's marker parse failed OPEN

**The defect, reproduced** (P1, §15.6): `Deploy-BffApi.ps1` set `$markerDeclared = [bool]$markerEntry.speAdminPlatformOperatorEnvironment`.
In PowerShell `[bool]"false"` is `$true` (every non-empty string is), so a registry entry declaring the STRING `"false"`
deployed `SpeAdmin__PlatformOperatorEnvironment=true` — the tenant-wide and type-wide SPE admin routes opened on that
environment for its root admin. The task-base block, run verbatim against `"dev": { …: "false" }`, prints `markerDeclared=True`.

**The fix** — no new machinery beyond the one seam a test needs:

- **`scripts/common/SpeAdminOperatorMarker.ps1`** — THE parse, one function `Get-SpeAdminOperatorMarkerDeclaration
  -RegistryPath -Environment`: returns the value when it is a `[bool]` (exactly what `ConvertFrom-Json` makes of a JSON
  `true`/`false`), `$false` when the registry, the environment or the key is absent (unchanged behaviour), and THROWS for
  every other type — a string, a number, `null`, an array, an object — naming the type and value.
- **`scripts/Deploy-BffApi.ps1`** dot-sources it and, on the throw, prints `OPERATOR MARKER DECLARATION INVALID — …` and
  `exit 1` before any App Service call. The script no longer reads the key itself.
- **The registry ArchTest** (`TheEnvironmentRegistry_DeclaresTheMarkerOnlyForSpaarkeOperatedEnvironments`) now first
  refuses any present declaration that is not a JSON `true`/`false`, naming the environments.

**Tests (both bite — §15.6):**

- `TheRegistryCheck_RefusesAStringFalse` — the registry check run over an in-memory registry: `"false"`, `"true"`, `0` and
  `null` are named; `true`, `false` and an absent key are not.
- `TheDeployScriptsMarkerParse_RefusesAStringFalse_AndReadsOnlyAJsonBoolean` — runs THE parse in a real PowerShell host
  (`pwsh`; `powershell.exe` as the Windows fallback — the ubuntu-latest CI runner ships `pwsh`; no host is a FAILURE, never a
  skip) against a temporary registry: `"false"`, `"true"`, `1`, `null`, `[false]` → refused; `true` → `Boolean:True`;
  `false`, an absent key and an unknown environment → `Boolean:False`. It also pins that `Deploy-BffApi.ps1` dot-sources the
  module, takes `$markerDeclared` from it and (comments stripped) never names the key itself. Also verified by hand under
  Windows PowerShell 5.1 (same answers).

### 15.4 Round 57 item 3 (class b) — security fail-closed branches that had no biting test

`ATypeRoute_WhenTheScopeCannotBeRead_Is503_AndNothingIsSent` faults `businessunits`; since round 49 the operator rule reads
the caller's scope FIRST, so that fault is met (and answered 503) there, and the container-type rule's own branches were
reachable by no test. Each now has a test that reaches it ITSELF, through the real host:

| Branch | Test | How it reaches the branch |
|---|---|---|
| VB5 — the filter's `DecideContainerTypeAsync`: the type rule's `Unverifiable` → 503 | both tests below | each ends in that case; each asserts 503 `spe.admin.deny.scope_unverifiable`, the handler never ran, no Graph request |
| VB6 — `SpeAdminTenantScope.DecideContainerTypeAccessAsync`: the scope read fault → `Unverifiable` | `SpeAdminPerContainerScopeTests.ATypeRoute_WhenTheTypeRulesOwnConfigTableReadFaults_Is503_AndNothingIsSent` (GET / POST consumers) | a ROOT admin of the operator host passes the operator rule and the configId rule (both read filtered rows only); only the type rule reads the WHOLE config table — the fixture now faults just that read (`FakeDataverseTables.FaultUnfilteredQueriesOn`, test fixture only). Asserts the type rule's own log line. |
| VB7 — the same method: a full page (5000 rows) of the config table → `Unverifiable` | `…ATypeRoute_WhenTheConfigTableReadIsAFullPage_Is503_NeverJudgedOnATruncatedTable` (GET permissions / POST consumers) | 5000 config rows with Config A first, and the 5001st — the one the cap drops — is a config of the same type in a unit outside the hierarchy: judged on the 5000 it sees, the root admin would be Permitted. Asserts the full-page log line. |
| VL7 — `H8SpeContainerTypeHandler.QuarantineClearedSince`'s `clearedAt >= since` | L2 `H8SpeContainerTypeHandlerTests.AC51_AnH8QuarantineClearedBeforeTheTypeWentInDoubt_IsNotTheOperatorsCheck_NoTypeIsCreated` | a run whose type went in doubt an hour ago carries an H8 quarantine an operator cleared BEFORE that: still `QuarantineRequired spe-container-type-creation-in-doubt`, the provisioner never called, the in-doubt marker kept. (AC46 already proves a clearance after the doubt lets H8 proceed.) |

The existing operator-rule fault test is unchanged (it still proves that path).

### 15.5 Known limits (owner round 56 classes (d)-(f) — one line each; repeat in the PR description)

- **(d)** The creation guard does not see a `System.Net.WebClient` upload written by deliberately unusual script code (round 57 item 4).
- **(d)** The creation guard's other stated blind spots stand as written in §14.3 (D37): a URL assembled at run time from fragments none of which spells the collection; a POST verb read from configuration or a third-party client's own method name; reflection, dynamic dispatch, `Invoke-Expression`, build-time generated code.
- **(d)** `TheDeployScriptsMarkerParse_…`'s source check (comments stripped) cannot see the registry key read by indirection in `Deploy-BffApi.ps1` (a computed property name); THE parse module is the one reader by construction, and its behaviour is what the test runs.
- **(e)** `SpeAdminTenantScope.GetEnvironmentReachAsync`'s full-page (5000-row) throw has no biting test (probe X1, §15.6); a truncated read can only narrow a non-operator's environment reach, so the branch fails closed without it.
- **(d)** (round 62) A case-variant spelling of the registry key (`SpeAdminPlatformOperatorEnvironment` vs `speAdmin…`) is read by the deploy script's parse (PowerShell property lookup is case-insensitive) but not by the ArchTest, which matches the key case-sensitively.
- **(f)** (round 62) No test proves the deploy EXITS on an invalid declaration; the parse's throw is tested and the script exits on it, so it fails closed anyway.

### 15.6 Seeding proofs (on the final code; `scratchpad/165h/seed.py`, session scratchpad)

Each seed: ONE exact single-occurrence replacement (anchor found exactly once, LF or CRLF); the named tests run (BFF:
`SpeAdminPerContainerScopeTests.ATypeRoute*`; L2: `H8SpeContainerTypeHandlerTests`; ArchTests:
`SpeAdminOperatorEnvironmentMarkerGuardTests`); the source restored from a byte copy, MD5-checked and touched. `git status`
was clean afterwards. **All 11 RED; none failed to compile.** The verifier's four (VB5, VB6, VB7, VL7) are seeded at the
branch itself; VB5 and VB6 each in two forms (fail open / answer the 404 instead).

| # | Seed | RED |
|---|---|---|
| VB5 | the filter lets the type rule's `Unverifiable` through (`return null`) | 4: both new type-route tests × 2 routes (the pre-existing operator-rule fault test stays GREEN — the verifier's finding, reproduced) |
| VB5b | the filter's `Unverifiable` case removed (the type 404 answers) | the same 4 |
| VB6 | the type rule's scope read fault → `Permitted` | 2: `ATypeRoute_WhenTheTypeRulesOwnConfigTableReadFaults_…` (GET / POST) |
| VB6b | the type rule's scope read fault → `NotFoundOrOutOfScope` | the same 2 |
| VB7 | the type rule's full-page refusal removed (it logs and continues) | 2: `ATypeRoute_WhenTheConfigTableReadIsAFullPage_…` (GET permissions / POST consumers) |
| VL7 | `QuarantineClearedSince` without the time clause (`clearedAt >= DateTimeOffset.MinValue`) | `AC51_AnH8QuarantineClearedBeforeTheTypeWentInDoubt_…` |
| A1 | `Deploy-BffApi.ps1` casts the declaration itself again (`[bool](…).speAdminPlatformOperatorEnvironment`) | `Deploy-BffApi.ps1's marker parse refuses the STRING "false" …` |
| A2 | THE parse casts instead of refusing (`return [bool]$declaration.Value`) | the same (behavioural: `string-false=Boolean:True`) |
| A3 | the registry declares `dev` as the STRING `"true"` | `config/environments.json declares the marker true only for …` |
| A3b | the registry declares `demo` as the STRING `"false"` — the round 57 defect's own input | the same |
| A4 | the registry check accepts any non-null declaration | `The registry check refuses a marker declared as the STRING "false" …` |

**P1 — the deploy script's own block, behaviourally** (`scratchpad/165h/p1/p1.ps1`, pwsh 7): the pre-deploy marker block is
extracted verbatim from the committed script and from the task base and run against a registry whose `dev` declares the
STRING `"false"`. **Base: `markerDeclared=True`, exit 0 — the defect reproduced.** Branch: `OPERATOR MARKER DECLARATION
INVALID — … not String 'false'.`, **exit 1**, before any `az` call. Branch with JSON `false` → `False`, JSON `true` → `True`.

**X1 — a coverage probe beyond round 57** (not a round 57 item; recorded as a known limit, §15.5): removing
`GetEnvironmentReachAsync`'s full-page throw turns NO `Auth.SpeAdmin` test red. Not fixed, by classification: a truncated
config read can only DROP rows, so a non-operator's linked-environment set can only shrink — that branch fails closed by
construction (class (e)).

### 15.7 Placement (CLAUDE.md §10) and new surface (CLAUDE.md §11) — the over-engineering check (owner round 56 item 2)

**Placement:** no BFF code changed except a doc comment (`SpeAdminOptions`). No new endpoint, filter, policy, service, DI
registration, option, job, package, Dataverse column or plugin; no csproj / props change. Deployment surface: the existing
registry and deploy script. Tests: existing classes and fixtures.

| New | Existing (grep evidence) | Extension? | Cost of doing nothing |
|---|---|---|---|
| `scripts/common/SpeAdminOperatorMarker.ps1` (one function) | the parse lived inline in `Deploy-BffApi.ps1`, which cannot be dot-sourced without running a deploy; `scripts/common/` already holds `SpeConfigSecretNamePolicy.ps1` (the round 41 precedent: one rule, dot-sourced, run by tests) | it IS the extraction of the existing block (8 lines → one function), not a new mechanism | the fail-open parse is fixed but unprovable: nothing can run it, so a cast reintroduced later passes every check (seeds A1, A2) |
| `FakeDataverseTables.FaultUnfilteredQueriesOn` (test fixture) | `FaultQueriesOn(entitySet)` faults every read of the set — including the configId rule's filtered read, which then answers first | a sibling of the existing fault switch | the container-type rule's own read-fault branch is reachable by no test (VB6) |
| tests: 2 BFF (4 cases), 1 L2, 2 ArchTests; the registry check's `NonBooleanDeclarations` helper; `RunPowerShell` / `FindPowerShellHost` | `ComposeMergeIntegrityTests` starts an external process (LibreOffice) the same way | — | round 57 item 3's four branches and the parse stay unproven |

Simpler alternatives considered and rejected: a C# re-implementation of the parse in the ArchTest (proves C#, not the
script); a `-SelfTest` switch on `Deploy-BffApi.ps1` (widens a deploy script's interface for a test); asserting only
the registry (a hand-edited or locally changed registry would still deploy `true`).

### 15.8 Route authorization ledger input (task 167; still not landed — amendment 3, no waiver edits)

No route added, renamed, deleted or given a different mechanism this round. New deny tests for the six container-type
routes' existing mechanism (the filter's container-type rule), for 167's `ProofTest` column if wanted:
`Sprk.Bff.Api.Tests.Auth.SpeAdmin.SpeAdminPerContainerScopeTests.ATypeRoute_WhenTheTypeRulesOwnConfigTableReadFaults_Is503_AndNothingIsSent`
and `…ATypeRoute_WhenTheConfigTableReadIsAFullPage_Is503_NeverJudgedOnATruncatedTable`.

### 15.9 Suites, publish size, conflict check — and what is not closed

**Suites** (once, at the end, after every seed was restored; sequential, on a machine other sessions were also using):

| Suite | Result |
|---|---|
| affected, during development | new type-route tests: `ATypeRoute*` 12 / 0; L2 `AC46`, `AC5*` 4 / 0; ArchTests `SpeAdminOperatorEnvironmentMarkerGuardTests` 5 / 0 |
| full BFF unit suite `tests/unit/Sprk.Bff.Api.Tests` | **14,771 passed / 2 failed / 54 skipped (14,827)** — 28 m. (+4 vs f2-v2's 14,823: the two new type-route tests × 2 routes.) The 2 failures are CONTENTION, not this round: `AttachmentFilterServiceTests.Filter_LogoImage_Excluded("logoSmall.gif")` (the service's regex has a 1-second match timeout and treats a timeout as no match) and `OfficeVersionSaveRevertTests.EmailSave_ResentWithNoClientKey_IsStillAnsweredDuplicate_AndReadsNoFile` (`TaskCanceledException` after 2 m 24 s). **Isolated re-run: 4 / 0** (the three logo cases + the save test). Neither file nor anything it reaches was touched this round. |
| NetArchTest `tests/Spaarke.ArchTests` | **367 passed / 0 failed / 0 skipped** (f2-v2: 365; +2: the registry string check and the deploy-script parse) |
| `tests/integration/Sprk.Bff.Api.IntegrationTests` (full) | **104 passed / 0 failed / 0 skipped** |
| `tests/integration/Spe.Integration.Tests` (full) | **403 passed / 0 failed / 25 skipped (428)** |
| L2 `src/server/services/Sprk.Provisioning.ControlPlane.Tests` (full) | **1,631 passed / 0 failed / 1 skipped (1,632)** (f2-v2: 1,630 / 1,631; +1 = AC51) |

**Code review / adr-check** (FULL rigor, self-review against the changed files): ADR-002 (no plugin) ✓ · ADR-003 (a
non-boolean declaration now STOPS the deploy instead of opening the routes; the type rule's fault and full-page branches
answer 503 and are proven to) ✓ · ADR-008 (no filter or per-handler check added) ✓ · ADR-010 (no DI change) ✓ · ADR-038 (BFF
tests through the real host; L2 over the existing hand-written fakes; the ArchTest runs the real PowerShell parse; no
`Mock<HttpMessageHandler>`, DI-registration or ctor null-check test) ✓. No §6.5 path needed.

**Publish size** (CLAUDE.md §10, hazards 1-4): each side exported with `git archive` (`src/server`, `config`, the root build
files) into a SHORT path, `dotnet restore` + `dotnet publish -c Release --no-restore` as `Deploy-BffApi.ps1`, zipped with
PowerShell **`Compress-Archive`** (Optimal), PDBs included:

| Side | Commit | Path | Zip | Files | MSB3030 |
|---|---|---|---|---|---|
| fresh `origin/master` (fetched 2026-10-05) | `b4b58a361` | `C:\wt165hm` | **45.65 MB** (47,864,529 B) | 212 | 0 |
| task base (`task/uac-r2-165-f2-v2`) | `fd8ae6c23` | `C:\wt165hb` | **45.70 MB** (47,920,681 B) | 212 | 0 |
| this branch | `e147c7b10` (the final source; later commits touch only this note and the POML) | `C:\wt165hn` | **45.70 MB** (47,920,628 B) | 212 | 0 |

This round: **−53 B (0.00 MB)** — only a doc comment changed in the BFF. Branch vs fresh master **+0.05 MB** (+56,099 B);
`b4b58a361` is not an ancestor, so that also carries the work branch's drift. Ceiling 60 MB. The export directories were
removed afterwards. `dotnet list package --vulnerable --include-transitive` (BFF): **no vulnerable packages**; no
`.csproj` / `.props` change.

**Conflict check:** `git merge-tree` of this branch into `work/unified-access-control-r2` @ `6532bb494` — clean. The #1298
overlap recorded in §14.12 is unchanged in kind (this round adds edits to `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`,
`SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md` and `dev.bicepparam` — comment/prose only; resolve by keeping both sides' text).

**Not closed:** the manual live gates of §14.9 only — (e) steps 1-3 (the marker on dev's two slots, an App Service
configuration write, BEFORE deploying this BFF; step 4 is now the production operator environment's own stand-up change,
not a gate), (b) the config repair with a minted credential, (a) the backfill `-Apply` / `-Bind` / `-Verify` (BLOCKS
deploying this BFF), (c) the post-deploy probes, (d) the next real L2 run — and f1's §11.16 (i)/(k). No (a)-(c) finding
is left for escalation under owner round 56's cap; the (d)/(e) items are in §15.5.

## 16. Batch-4 integration (main-session rounds 62 and 65) — merged into `integ/uac-r2-batch4`

### 16.1 Master `bb8ba7251` and the secret-name allow-list (round 65 item 1)

Master moved SPE Admin onto the BFF's own managed identity (2026-10-04): `SpeAdminTokenProvider` and every Key Vault read of
an owning-app secret are gone, and `keyVaultSecretName` is optional. **SPE Admin authenticates as the BFF identity; the
`sprk_keyvaultsecretname` column is retained only as data.** The merge took master's shape in `SpeAdminGraphService` and
deleted `SpeAdminTokenProvider.cs` (modify/delete → delete). It then re-applied this task's other work on top: container
binding and stamping, unbound containers reachable by no admin route, the operator marker, and the type-route and
security-route rules. The backfill keeps `-Bind`.

- **Removed:**
  - the read guard (`SpeConfigSecretNamePolicy.EnsureAllowed` and `SpeConfigSecretNameNotAllowedException`) with the
    reads it guarded;
  - the filter's 409 (`SpeAdminScopeDecision.SecretNameNotAllowed`, `SpeAdminConfigCredentialUnused` and its four route
    markers, `ConfigSecretNameNotAllowed`);
  - `SpeConfigSecretNameReadGuardTests` and the 409 tests.

  A config whose STORED name does not conform (dev's Model 1 config stores the literal `"null"`) is now served and synced
  like any other. Pinned by `SpeAdminConfigSecretNameTests.AConfigWhoseStoredSecretNameDoesNotConform_IsServed_…` and
  `SpeAdminDashboardScopeTests.Refresh_AConfigWhoseStoredSecretNameDoesNotConform_IsStillCounted`.
- **Reader grep** (`src/`, `scripts/`, `infrastructure/`): the BFF reads the column only as data — the tenant scope's
  identity-value comparison and the DTOs; no secret is fetched by it. **One reader remains:**
  `scripts/Backfill-SpeContainerBusinessUnitStamp.ps1`, which fetches the named secret to list a config's containers as
  its owning app.
- **So, per round 65:** the **400** on a SUPPLIED name outside `spe-owning-app-` stays (config POST/PUT; the name is
  optional). The backfill's own refusal of a non-conforming stored name stays: it guards that script's own vault read.
  `Test-SpeConfigSecretNames.ps1` and `Repair-SpeConfigSecretName.ps1` stay as the backfill's tools. They are no longer a
  BFF deploy gate (README, deployment guide Phase 5c and the topology doc are updated).
- **Also retired with what it guarded:** the register route's `sharePointAdminUrl` host check. It existed because the
  register route sent an APP-ONLY token to that host. Master made registration a delegated Graph grant that ignores the URL,
  so nothing is sent to any host the caller names. Its tests became one test that a root admin passes every SPE admin rule
  and the URL is not consulted.
- **Test harness:** the host fixture's per-config client seams (`UseClientForConfig`, `UseOwningAppClientForConfig`) are
  gone with the client caches. The host builds `SpeAdminGraphService` over a fake `IGraphClientFactory` whose `ForApp()` is
  the WireMock Graph. Configs are linked to an environment carrying the BFF tenant, because master's tenant guard refuses a
  config without one.
- **Known gap for the main session:** the topology doc now tells operators to leave `keyVaultSecretName` blank, but the
  binding backfill needs the owning app's secret to LIST a config's containers. For a config without a usable name, the
  only path is `-Bind` with container ids found some other way. Listing as the BFF identity would close it; that is not built
  here.

### 16.2 Round 62 items 1–3

1. `Assert-SpeAdminOperatorMarkerTarget` (`scripts/common/SpeAdminOperatorMarker.ps1`). When the marker is declared true,
   `Deploy-BffApi.ps1` refuses unless `-AppServiceName` / `-ResourceGroupName` equal that registry entry's
   `appServiceName` / `resourceGroup`, compared case-insensitively. It also refuses an entry that names neither. Pinned by
   `ADeclaredMarker_IsRefused_UnlessTheDeployTargetsItsRegistryEntrysAppService`, which runs the module through the
   existing PowerShell driver: 6 cases, plus a source check that the deploy calls it before setting the marker.
2. Guide §6.5.4, the `demo` entry comment in `config/environments.json` and its CORS comment now say that standing up
   `demo` must also add it to the deploy script's `-Environment` `ValidateSet`. Until then no deploy reads that entry.
3. The two known limits are in §15.5.

### 16.3 Ledger (task 167)

All nine Pending waivers are deleted, and each finding is resolved by credit:
- **S-44, S-72, S-45, S-73, S-74, S-75:** the SPE admin AdminOnlyRoutes groups, with the tenant-scope filter's per-config
  rule. S-45, S-73 and S-75 are re-keyed to `{configId:guid}` (§1).
- **S-47, S-48, S-77:** a new SystemAdmin AdminOnlyGroup for `RecordMatchingAdminEndpoints.cs`.

The ProofTests are §6's deny tests.

Two classification changes followed:
- `AddSpeAdminTenantScopeFilter`'s non-deciding pin follows the new pass-through spelling (no configId named).
- `RequireSpeAdminPlatformOperator` is classified NotAuthorization: it is endpoint metadata the filter reads.

ADR-010's ceiling went 158 → 159 for `ISpeAdminContainerScopedRequest`, a request contract the filter reads, with its
justification in the test.

## 17. Master merge (2026-10-05): H8 ported onto master's single-container H8

Master's `customer-provisioning-orchestration-r1` task 214 DELETED the H8 this task had changed
(`Handlers/SpeContainerType/`, which created a container TYPE and a container). It REPLACED it with
`Handlers/SpeContainer/`: `H8SpeContainerHandler`, `GraphContainerProvisioner` and `ISpeContainerProvisioner`, which create
ONE container in a pre-existing container type. Creating a type is now delegated operator work. At the integration merge
of origin/master, this task's binding H8 intent was ported onto the new files. The sections above name the old files; read
them as history.

- **Kept, on the new files:**
  - Every container H8 creates is stamped with the environment's ROOT business unit, read back, and removed if the stamp
    did not land (round 35 item 1). `DataverseRootBusinessUnitReader` moved to `Handlers/SpeContainer/`.
  - H8 records what it created, in the typed `interStepState.speContainerCreation`, and resumes with it (rounds 41 + 49).
    The record survives the production Cosmos serializer.
  - The container is handed to H7 only once bound. `DagAdvancer` gives H8 the dependencies H3 and H5, and H7 depends on H8.
- **Two gaps in master's new H8, closed in the port:**
  - A timeout after the create was sent is reported as "a container may exist". It no longer creates a second container.
  - A container whose activation failed is re-activated on resume.
- **In-doubt creation (ACCEPTED by the main session).** A create whose answer was lost is QuarantineRequired
  `spe-container-creation-in-doubt`, and H8 NEVER auto-adopts a container it finds by listing. Master's container type is
  shared across customers, so adopting what a listing finds could adopt another customer's container (class (a));
  quarantining fails closed.
  - H8 writes the run id into every container's description.
  - The operator lists the type, finds the container whose description names this customer and run, then records its id
    (`interStepState.speContainerCreation.rootContainerId`) or removes it, and clears the quarantine.
- **Dropped as moot:**
  - everything about container-TYPE creation: the type-creation in-doubt quarantine, reuse of an existing type, the type
    registration;
  - the KV write of the container id (master's H8 writes no Key Vault secret).
- **Tests:**
  - L2: `H8SpeContainerHandlerTests`, `GraphContainerProvisionerBindTests`, plus H7 and DagAdvancer. They replace
    `H8SpeContainerTypeHandlerTests`.
  - ArchTests: `SpeAdminContainerBindingGuardTests` names `Handlers/SpeContainer/GraphContainerProvisioner.cs` as the
    deferred binder, and checks the order read record → create → record → verify → bind → bind further → complete. The KV
    step is gone.
- **Also from master:**
  - `stacks/model2-full.bicep` and its parameter files were deleted (task 249). No Bicep template names the operator marker
    now; `NoBicepTemplate_EmitsTheOperatorMarker` replaces the Bicep-stack test.
  - `demo` is in `Deploy-BffApi.ps1`'s `-Environment` set (T242b), which answers round 62 item 2.

