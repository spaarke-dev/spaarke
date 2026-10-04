# Task 165 — Admin surfaces: operator policy and SPE admin tenant scope

> **Task**: `tasks/165-admin-surfaces-operator-policy-and-tenant-scope.poml` (GitHub #1104)
> **Branch**: `task/uac-r2-165` from `work/unified-access-control-r2` @ `e6dd48b43`
> **Date**: 2026-10-03 · **Rigor**: FULL · **Model**: Opus 5.5
> **Outcome**: code-complete for the nine sweep routes and three of the four amendment items;
> **two escalations fired and stopped** (§4.1 environments scoping, §4.2 three `SearchItemsTests`).

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
| amendment | environment routes without business-unit scoping | — | **STOPPED — escalation §4.1** |

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
| 5 | the fail-closed 503 / does-not-exist 404 changes an existing test or shipped flow outside the six routes | **FIRED — §4.2** |
| 6 | an ADR exception | **not fired** — the SystemAdmin policy change is the owner's amendment, not an ADR deviation; no per-handler `CanAccessConfigAsync` |
| 7 | shared app identity across leaf units | **not fired** — 0 (§2) |

### 4.1 STOPPED — environment routes business-unit scoping (owner amendment item)

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

### 4.2 STOPPED — `SearchItemsTests` outcomes changed by the fail-closed filter (trigger 5)

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
| `POST /api/spe/dashboard/refresh` (amendment) | same projection | (same code path; refresh triggers a sync and is not exercised in the fake host — the GET test covers the projection) |
| `POST /api/spe/containertypes/{typeId}/register` (amendment) | filter (`?configId`) + host allow-list | `…SpeAdminConfigAndBulkTenantScopeTests.Register_ToAHostThatIsNotASharePointAdminHost_Is400` |

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
- **D4 — dashboard storage in a partial view reports 0 bytes from 0 reporting containers.** The cache holds only
  a cross-config sum; reporting a share of it would leak other customers' storage. The metrics contract already
  reads a reporting count below the total as "a floor". The per-config split needs `SpeDashboardSyncService`
  (ADR-052 ratchet-listed) — §8 item 4.
- **D5 — a BU-less config may be claimed by PUT into the caller's unit.** It is visible to every admin under the
  compatibility rule; the BU check judges the target unit only. Recorded; unchanged rule (trigger 1).
- **D5b — Model 1 observation (code-review W1, for the owner).** Topology §3 Model 1 is "one container type,
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

1. **§4.1 environments** — owner decision, then a follow-up change.
2. **§4.2 SearchItemsTests** — main-session decision; recommended resolution given.
3. **Container → business-unit binding** — container, item, permission and recycle-bin routes (and now the bulk
   job) check the config's business unit and container TYPE, not that a container belongs to the caller's unit;
   one type can serve several customers (Model 1). Needs an authoritative container → unit map, which does not
   exist (`businessunit.sprk_containerid` is not unique, §2).
4. **Dashboard per-config storage** — add `storageUsedByConfig` to `SpeDashboardSyncService.DashboardMetrics` when
   that service is migrated to `IScheduledJob` (ADR-052 §1, "migrates when next touched").
5. `GET /api/spe/bulk/{operationId}/status` is not bound to the caller who started the operation (low; random
   GUID, ~30 min retention). Not changed.
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
- **(f)** amendment: `POST $BFF/api/spe/containertypes/<type>/register?configId=<own config>` with
  `sharePointAdminUrl: "https://example.com"` → **400** `spe.containertypes.register.sharepoint_url_not_admin_host`
  (read-only effect: refused before any token is acquired).
