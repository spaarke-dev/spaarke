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
> tests + seeds, the widened body-marker guard, and the publish-size measurement. Still open: only the manual live
> gates (§9, §11.16 — the backfill `-Apply` among them).

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
| round 20 item 1 | `POST /api/spe/containers`; secure-record provisioning | every BFF creation path stamps `spaarkeBusinessUnitId` (custom property), reads it back, removes the container if it did not land; backfill script (dry run / `-Apply` / `-Verify`) for existing containers | done — f1, §11.2 (backfill `-Apply` = manual gate §11.16 (h)) |
| round 20 item 2 | 31 container routes, 4 list/search routes, bulk delete/permissions/status | `SpeAdminTenantScopeFilter` per-container rule (own unit or descendant; unbound → root only; unreadable → 503; one 404); list trims; per-item check in the bulk job; status bound to its starter | done — f1, §11.3 |
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
