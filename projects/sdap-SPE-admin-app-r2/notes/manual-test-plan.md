# SPE Admin — manual test plan

> **Relocated 2026-08-27 by task 042** from the XML doc comment on
> `ManualIntegrationTestDocumentation` in
> `tests/unit/Sprk.Bff.Api.Tests/Integration/SpeAdmin/Phase2IntegrationTests.cs` (lines 1171–1319).
>
> The class carried this plan on a `[Fact(Skip = "Manual test — requires live Azure SPE environment")]`
> whose entire body was `true.Should().BeTrue("documentation marker test")`. Its own comment said it
> "exists only to make the manual test plan visible in the test runner." That is documentation wearing a
> test's clothes — ADR-038 §7 B10. **The knowledge is kept here; the fake test is deleted.**

---

## ⚠️ Two corrections applied during relocation

The plan was written before this project's work and has drifted. Both errors are corrected **inline
below**, with the original preserved in strikethrough so the drift is auditable.

1. **The BFF URL is stale.** The plan names `https://spe-api-dev-67e2xz.azurewebsites.net`. That App
   Service no longer exists — dev moved to **`spaarke-bff-dev`** (resource group `rg-spaarke-dev`).
   The old name caused a `ResourceNotFound` deploy incident on 2026-05-27, recorded in
   [`.claude/skills/bff-deploy/SKILL.md`](../../../.claude/skills/bff-deploy/SKILL.md) Failure Modes.
2. **Much of this plan is no longer manual.** Tasks 040 (WireMock contract tier) and 041 (LiveIntegration
   seam tier) automated a substantial share of it. Each section below is annotated with its current
   automation status so an operator does not hand-run what CI already proves.

---

## Environment

| | |
|---|---|
| BFF API | ~~`https://spe-api-dev-67e2xz.azurewebsites.net`~~ → **`https://spaarke-bff-dev.azurewebsites.net`** |
| Code Page | `sprk_speadmin` in Dataverse (`https://spaarkedev1.crm.dynamics.com`) |
| Container type | "Spaarke PAYGO 1" — `8a6ce34c-6055-4681-8f87-2f4f9f921c06` |

> ⚠️ **Destructive steps (CT-003 create, RB-002 restore, RB-003 permanent delete) must target a
> throwaway container**, per NFR-07 and `projects/sdap-SPE-admin-app-r2/CLAUDE.md` "Live-tenant safety".
> `tests/integration/seam/SpeAdmin/LiveIntegrationFixture.cs` provisions and tears one down automatically —
> prefer running that suite over hand-executing these steps.

---

## Container types

### CT-001: Container Type List
`GET /api/spe/containertypes?configId={valid-config-guid}`
Expected: 200 OK with `ContainerTypeListDto { items: [...], count: N }`
Verify: each item has `id`, `displayName`, `billingClassification`, `createdDateTime`

> **Automated** — `SpeAdminContainerTypeMappingTests` (contract tier) covers the mapping, including
> `billingClassification`, `billingStatus` wire casing, `owningAppId`, and typed trial expiry.
> Note: task 030 found **row selection had never worked** (DTO sent `id`, client read `containerTypeId`);
> fixed. Task 029 found `billingStatus` appeared in **zero** files repo-wide before that task.

### CT-002: Container Type Get by ID
`GET /api/spe/containertypes/{typeId}?configId={valid-config-guid}`
Expected: 200 OK with `ContainerTypeDto`; **404 for unknown typeId**

### CT-003: Container Type Create
`POST /api/spe/containertypes?configId={valid-config-guid}`
Body: `{ "displayName": "Test CT", "billingClassification": "standard" }`
Expected: 201 Created with new `ContainerTypeDto`
Verify: audit log entry written in `sprk_speauditlog`

> ⚠️ **Creates real, billable tenant state.** A trial container type **expires at 30 days** and
> **cannot be registered on another tenant** (task 030). There is **no container-type DELETE
> affordance** — see the open question in `current-task.md`. Do not run casually.

### CT-004: Container Type Settings Update
`PUT /api/spe/containertypes/{typeId}/settings?configId={valid-config-guid}`
Body: `{ "sharingCapability": "view", "isVersioningEnabled": true }`
Expected: 200 OK with `ContainerTypeSettingsResponseDto`

> **Automated** — `SpeAdminContainerTypeSettingsPatchTests` (14 tests). Task 023 proved the write path
> was broken at **three** independent points and a fourth defect was **defended by 10 tests**; the
> `etag` is a required **body** property (not the `If-Match` header). Task 025 proved
> `agent.chatEmbedAllowedHosts` is **fictional** and `sharingCapability` was the omitted real property.

### CT-005: Container Type Registration
`POST /api/spe/containertypes/{typeId}/register?configId={valid-config-guid}`
Body:
```json
{ "appId": "{consuming-app-guid}",
  "sharePointAdminUrl": "https://contoso-admin.sharepoint.com",
  "delegatedPermissions": ["FileStorageContainer.Selected"],
  "applicationPermissions": [] }
```
Expected: 200 OK with `RegisterContainerTypeResponse`; audit log entry written

> 🔴 **Partly broken in production.** Task 041 proved the **Graph-based** consuming-app registration
> write path (`POST …/containerTypeRegistrations/{id}/applicationPermissionGrants`) returns
> **400 `apiNotFound` on both API versions**, while GET on the identical URL succeeds. Filed as
> **issue #834**. The UI's Register button uses a *separate* SharePoint-REST path
> (`PUT {sharePointAdminUrl}/_api/v2.1/storageContainerTypes/{id}/applicationPermissions`), which was
> **not** exercised live because no proven-reversible undo was located.

### CT-006: App Permissions View
`GET /api/spe/containertypes/{typeId}/appPermissions?configId={valid-config-guid}`
Expected: 200 OK with a list of `SpeContainerTypePermission` entries

> ⚠️ **Coverage gap.** No contract test covers this endpoint. Distinct from container-type *owner*
> grants (`SpeAdminContainerTypeOwnerTests`): `applicationPermissions` = which **apps** may access;
> `permissions` = which **people** own (task 027).

---

## Columns and custom properties

### COL-001: Column CRUD
```
CREATE: POST   /api/spe/containers/{containerId}/columns?configId={id}
        { "name": "Category", "columnType": "choice", "required": false }
READ:   GET    /api/spe/containers/{containerId}/columns?configId={id}
UPDATE: PATCH  /api/spe/containers/{containerId}/columns/{columnId}?configId={id}
        { "displayName": "Document Category" }
DELETE: DELETE /api/spe/containers/{containerId}/columns/{columnId}?configId={id}
```
Expected: 201 Created → 200 OK → 200 OK → 204 No Content

### PROP-001: Custom Property Read and Update
```
READ:   GET   /api/spe/containers/{containerId}/customproperties?configId={id}
UPDATE: PATCH /api/spe/containers/{containerId}/customproperties/{propId}?configId={id}
        { "value": "updated-value", "isSearchable": true }
```
Expected: 200 OK for both; verify the `isSearchable` flag persists

---

## Search

### SRCH-001: Container Search
`POST /api/spe/search/containers?configId={id}` · Body: `{ "query": "legal", "pageSize": 10 }`
Expected: 200 OK with `items` array and `count`
Verify: empty results return `{ items: [], count: 0 }` — **not** 404

### SRCH-002: Item Search (unscoped)
`POST /api/spe/search/items?configId={id}` · Body: `{ "query": "contract.pdf", "pageSize": 10 }`
Expected: 200 OK with DriveItem results

### SRCH-003: Item Search (scoped to container)
`POST /api/spe/search/items?configId={id}` · Body: `{ "query": "invoice", "containerId": "{container-id}", "pageSize": 10 }`
Expected: results scoped to the specified container only

> **Automated** — `SpeAdminSearchContractTests` (13 tests). Task 004 proved the original failure:
> `fileStorageContainer` is **not a valid `/search/query` entity type**; container search now uses a
> filtered list. Item search additionally required `region` (app-only Graph) and must **not** send
> `contentSources` (allowed only for `externalItem`).

---

## Recycle bin

### RB-001: Recycle Bin List
`GET /api/spe/recyclebin?configId={id}` → 200 OK with deleted containers list

> **Automated** — task 022 proved every row reported a **null deletion timestamp**: the value was
> dropped by a `rawDeletedAt is string` guard that can never match Kiota's `System.DateTime`. The
> `$select` was **removed**, not corrected.

### RB-002: Recycle Bin Restore
`POST /api/spe/recyclebin/{containerId}/restore?configId={id}`
Expected: 200 OK; container reappears in the active list; audit log entry written

### RB-003: Permanent Delete
`DELETE /api/spe/recyclebin/{containerId}?configId={id}`
Expected: 204 No Content; container gone from the recycle bin; audit log entry written

> 🚨 **Irreversible.** RB-002 and RB-003 are covered end-to-end by
> `ContainerLifecycleLiveTests.ThrowawayContainer_DeleteRestorePermanentDelete_…`, which provisions its
> own container and asserts the pre-existing ones are byte-identical afterwards. **Prefer that suite to
> hand-running these.**

---

## Security

### SEC-001: Security Alerts
`GET /api/spe/security/alerts?configId={id}`
Expected: 200 OK with `SecurityAlertsResponse { alerts: [...] }`; each alert has `id`, `title`,
`severity`, `status`, `createdDateTime`

> 🔔 **Known to fail, and not for a permissions reason.** `Security.Alerts_v2` returns
> **403 "Account is not provisioned"** — it needs a Microsoft 365 Defender workload in the tenant.
> Proof it is not permissions: legacy `/security/alerts` returns **200 with an empty array** on the same
> token, same tenant, same moment. **No broader permission fixes this**; granting one to silence it is
> the exact failure mode this project exists to remove. See `notes/security-grant-record.md`.

### SEC-002: Secure Score
`GET /api/spe/security/score?configId={id}` → 200 OK with `SecureScoreDto { currentScore, maxScore, activeUserCount }`

> ✅ Works since task 013 granted `SecurityEvents.Read.All` (was 403, now 200 with real data).
> ⚠️ **Coverage gap** — no contract test exists for either security endpoint.

---

## Authorization

### AUTH-001: Non-Admin User Access
Call any `/api/spe/*` endpoint with a token lacking the Admin or SystemAdmin app role.
Expected: **403 Forbidden** with ProblemDetails, `reasonCode` = `sdap.access.deny.role_insufficient`

### AUTH-002: Unauthenticated Access
Call any `/api/spe/*` endpoint with no Authorization header.
Expected: **401 Unauthorized**, `reasonCode` = `sdap.access.deny.unauthenticated`

> **Automated** — `tests/integration/auth/SpeAdmin/SpeAdminAuthorizationLayerTests` (13 tests) covers
> both, plus the distinction this project had to learn the hard way: **Entra directory roles are
> invisible to the BFF** (`groupMembershipClaims` unset → no `wids` claim), proven with a positive
> control against a confirmed role holder. So claim-absence ≠ role-absence, and layer 1 must never
> speak about directory roles (task 012).

---

## UI walkthroughs (genuinely manual — no automation replaces these)

### UI-001: ContainerTypesPage renders
Navigate to SPE Admin → Container Types.
Verify: grid loads with container types; toolbar has Create / Register / Refresh.
Verify: dark mode applies Fluent v9 tokens (ADR-021).

### UI-002: ContainerTypeDetail panel
Click a container type → detail panel slides in.
Verify: Settings tab shows the sharing/versioning/storage form.
Verify: Permissions tab loads app permissions on first click.
Verify: dirty-state indicator appears on a settings change.

### UI-003: RegisterWizard
Click Register → 4-step wizard opens.
1. Select container type (or pre-selected) · 2. Select delegated permissions (≥1 required) ·
3. Select application permissions · 4. Review and confirm.
Verify: success screen shows on completion.

> ⚠️ See CT-005 — the Graph write path behind this is **filed broken** (#834).

### UI-004: SearchPage
Navigate to SPE Admin → Search. Enter a query → results populate in `ContainerResultsGrid`.
Toggle to Item Search → `ItemResultsGrid` shows.
Verify: pagination controls appear when `totalCount > pageSize`.

### UI-005: RecycleBinPage
Navigate to SPE Admin → Recycle Bin.
Verify: deleted containers listed with deletion date.
Restore a container → confirmation dialog → success toast.
Permanently delete → DESTRUCTIVE warning dialog → success toast.

### UI-006: SecurityPage
Navigate to SPE Admin → Security.
Verify: Secure Score card shows (`currentScore` / `maxScore`).
Verify: Security Alerts grid shows recent alerts with severity badges.

> 🔔 Per SEC-001, the alerts grid will be **empty or errored** in Spaarke Dev until a Defender
> workload exists. That is the correct, honest behaviour — not a bug to "fix" by widening permissions.

---

# Phase 3 — consuming tenants, bulk operations, multi-app

> **Relocated 2026-08-27 by task 042** from a block comment (lines 930–1056) in
> `Phase3IntegrationTests.cs`, and a second block (lines 466–514) in `MultiAppSupportTests.cs`. Both
> files were retired — `MultiAppSupportTests.cs` in full, because **all 24 of its tests exercised code
> with zero callers in `src/`** (verified by grep, not inferred).

## SPE-082: consuming-tenant management

### Scenario 1 — list consuming tenants, empty
`GET /api/spe/containertypes/{typeId}/consumers?configId={id}` → 200 OK with `{ items: [], count: 0 }`

### Scenario 2 — register / update / remove (full CRUD)
```
POST   .../consumers?configId={id}      { "appId": "…", "delegatedPermissions": ["readContent"] }   → 201
GET    .../consumers?configId={id}                                                                   → consumer appears
PUT    .../consumers/{appId}?configId={id}  { "delegatedPermissions": ["writeContent"], … }          → 200
DELETE .../consumers/{appId}?configId={id}                                                           → 204
GET    .../consumers?configId={id}                                                                   → consumer gone
```

> 🔴 **The POST leg is broken.** Task 041 proved it returns **400 `apiNotFound` on both API versions**
> while GET on the identical URL succeeds — **issue #834**. `ContainerLifecycleLiveTests
> .ConsumingAppRegistration_WritePath_ReturnsApiNotFound_ADocumentedGraphDefect` pins this and will
> **fail loudly when it is fixed**, which is the intent. Do not "fix" that test by deleting it.

### Scenario 3 — duplicate registration → 409
Register the same `appId` twice; second POST → 409 with `spe.containertypes.consumers.already_registered`.

### Scenario 4 — non-admin → 403
Any consuming-tenant endpoint with a non-admin token → 403, `sdap.access.deny.role_insufficient`.

> **Automated** — see AUTH-001 above.

## SPE-083: bulk operations

### Scenario 5 — bulk delete with progress tracking
`POST /api/spe/bulk/delete` `{ containerIds: [...], configId: "…" }` → **202** with `{ operationId, statusUrl }`
Poll `GET /api/spe/bulk/{operationId}/status` until `isFinished: true`; verify `completed + failed = total`.

### Scenario 6 — bulk permissions
`POST /api/spe/bulk/permissions` `{ containerIds: [...], configId: "…", userId: "…", role: "reader" }` → 202

### Scenario 7 — partial failure
Mix valid and invalid container IDs; verify per-container `errors[]` and `isFinished: true` despite failures.

### Scenario 8 — unknown operation id → 404
`GET /api/spe/bulk/{random-guid}/status` → 404

> ⚠️ **Bulk operations have no automated coverage at all.** Task 042 found the existing unit tests
> re-implemented the validation rules locally (500-item cap, user/group mutual exclusion, role
> allow-list) rather than calling the endpoint — so a production regression would not have failed them.
> The three rule-bearing tests were **retained and marked AMBIGUOUS** pending a real contract test.
> The file's own docstring claimed to cover `BulkOperationService`; **no test ever constructed it.**

## SPE-084: multi-app registration (⛔ historical only — do not run)

Five scenarios described an owning-app OBO exchange: multi-app token exchange, cache isolation, Key Vault
secret rotation, startup validation warning, single-app backward compatibility.

**Task 010 returned UNWORKABLE and the shape is structurally impossible**, not merely unimplemented:
`api://{owningAppId}/.default` fails `AADSTS500011` (the app exposes no scopes), and MSAL OBO requires
the incoming assertion's audience to equal the confidential client — it is always the BFF. Task 011
moved container types onto the BFF's existing OBO exchange (`IGraphClientFactory.ForUserAsync`).

Retained here only so nobody re-derives it. Full record: `notes/obo-spike-findings.md`,
`notes/app-registration-topology.md`, TASK-INDEX "Historical — the task-010 blocking record".

## Deployment verification

### Scenario 12 — BFF health after deploy
```
curl https://spaarke-bff-dev.azurewebsites.net/healthz   → "Healthy" (200)
curl https://spaarke-bff-dev.azurewebsites.net/ping      → "pong"
```
> ⚠️ Corrected from the original `spe-api-dev-67e2xz.azurewebsites.net` — see the environment note at
> the top of this file.

### Scenario 13 — Code Page dark mode
Open the SPE Admin Code Page → toggle dark mode → verify no white flash, Fluent v9 tokens applied
(ADR-021), and no console errors.

### Scenario 14 — Phase 3 sections present
Consuming Tenants navigable · Bulk Operations shows delete + permissions forms · Config shows multi-app
fields where present.

> ⚠️ Scenario 14's "multi-app fields" refer to the SPE-084 owning-app config above — **inert**. The
> underlying `SpeAdminTokenProvider` / `GetClientForOwningAppAsync` / `ContainerTypeConfig.OwningApp*`
> surface has **no callers**, yet is still DI-registered and shipped in the BFF publish. Flagged as a
> CLAUDE.md §11 dead-code removal candidate; **not** actioned by task 042 (test-scope only).
