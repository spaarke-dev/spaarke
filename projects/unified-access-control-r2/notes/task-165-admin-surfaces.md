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
> justification recorded. Still open: publish size (instructed to skip; pre-merge, §10.7), the manual live
> gates (§9), and **owner round 20 items 1-3** (per-container business-unit binding), owed by task 165 in a
> dedicated follow-up round run by the main session (§10.8).

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

1. ~~§4.1 environments~~ — done in fix rounds r1 + r2 (owner round 16 item 4; round 20 item 4), §10.
2. ~~§4.2 SearchItemsTests~~ — done in fix round r1 (owner round 16 item 5), §10.
3. **Container → business-unit binding** — container, item, permission and recycle-bin routes (and now the bulk
   job) check the config's business unit and container TYPE, not that a container belongs to the caller's unit;
   one type can serve several customers (Model 1). **Decided by owner round 20 items 1-3 and OWED BY TASK 165**:
   a dedicated follow-up round run by the main session (§10.8). No longer "not in this task".
4. **Dashboard per-config storage** — add `storageUsedByConfig` to `SpeDashboardSyncService.DashboardMetrics` when
   that service is migrated to `IScheduledJob` (ADR-052 §1, "migrates when next touched"). Under the owner's
   standing "never defer or sideline" directive (rounds 15/16) the main session files it as a GitHub issue — exact
   command in §10.9.
5. `GET /api/spe/bulk/{operationId}/status` is not bound to the caller who started the operation (low; random
   GUID, ~30 min retention). Not changed. The main session files it as a GitHub issue — §10.9. (Round 20 item 2
   names the bulk routes; the follow-up round may absorb it.)
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

Not measured — the workflow instruction for this round says skip. It is a **pre-merge** obligation for the main
session: publish `origin/master` and this branch each from a fresh SHORT-path worktree (e.g. `C:\wt165m`,
`C:\wt165b`), zip both with PowerShell `Compress-Archive` (as `scripts/Deploy-BffApi.ps1`), confirm equal file
counts, report both sizes and the delta (ceiling 60 MB). No package reference changed in r1 or r2, so there is no
CVE delta (`dotnet list package --vulnerable --include-transitive` unchanged).

### 10.8 Owner round 20 items 1-3 — OWED BY TASK 165 (follow-up round, main session)

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

### 10.9 Items for the main session to file (GitHub issues; not run here — no external writes from this round)

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
