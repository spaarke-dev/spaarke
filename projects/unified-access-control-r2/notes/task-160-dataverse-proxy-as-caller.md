# Task 160 — the internal Dataverse proxy routes: DELETED (owner round 10 item 1)

> GitHub #1099 · route sweep findings #9 and #10 (both critical) · branch `task/uac-r2-160` off
> `work/unified-access-control-r2` @ `6b243f092`.

## 1. Outcome in one paragraph

`POST /api/dataverse/fetch` and `GET /api/dataverse/record/{entityLogicalName}/{id:guid}` were **deleted**,
not fixed. Both ran the caller's FetchXML / record id APP-ONLY behind `DataverseAuthorizationFilter`, which
only checks that the caller holds a table `prvRead*` privilege at any depth, so any signed-in workforce user
read every row and every column of the org (secure records, other business units, No-Access-walled rows,
field-level-secured columns). The POML's amendments 4 and 5 (binding, owner round 10 item 1) say: delete a
route that has no caller in the repo AND is in no published API description; and if both of these routes are
deleted and no production caller needs SDK impersonation, deliver neither the SDK impersonation entry point
nor its arch rule. Both conditions held (§2), so the impersonation fix described in the POML body (SDK
`CloneAsSystemUser`, `DataverseCallerFaultClassifier`, `ExecuteAsCallerAsync` / `GetRecordAsCallerAsync`,
`DataverseImpersonationSiteGuardTests`) was **not built**. The external module seam, which reuses
`FetchService` / `RecordService` app-only for contacts, is byte-identical.

## 2. Consumer inventory and the deletion evidence (POML step 1; amendment 4)

### 2.1 In-repo callers — none

Searched `src/**`, `tests/**`, `scripts/**`, `docs/**`, `config/**`, `infrastructure/**`, `.github/**`,
`.claude/**`, root `CLAUDE.md` (excluding `node_modules`, `dist`, `bin`, `obj`, `out`) for
`api/dataverse/fetch`, `api/dataverse/record`, `/api/dataverse`, `new BffDataverseClient(`,
`createBffDataService`, and string-built variants (`dataverse/${…}`, `'/fetch'`, `/record/${…}`).

| Hit | What it reaches | Caller of the deleted internal routes? |
|---|---|---|
| `src/client/shared/Spaarke.UI.Components/src/services/BffDataverseClient.ts:359-415` (`retrieveMultipleRecords`, `retrieveRecord`) | builds `{bffBaseUrl}/api/dataverse/fetch` and `/record/...` | Only through an instance. Its ONLY instance is below. |
| `src/client/external-spa/src/services/gridDataverseClient.ts:64,70` (`new BffDataverseClient`) | `bffBaseUrl = {BFF_API_URL}/api/v1/external` → `/api/v1/external/api/dataverse/fetch` and `/record/...` | **No** — the external module seam (`ExternalModuleDataEndpoints.cs:118-140`). |
| `src/client/shared/Spaarke.UI.Components/src/utils/adapters/bffDataServiceAdapter.ts:100` (`createBffDataService`) | builds `{base}/api/dataverse/{entity}/{id}` | **No** — matches no BFF route (the `/api/dataverse` groups map only `savedquery`, `savedqueries`, `metadata`, `gridconfigurations`, `fetch`, `record`). Its one instance, `external-spa/src/pages/PlaybookLibraryPage.tsx:128`, therefore reads nothing through either deleted route (a pre-existing defect of that page, recorded in §9). |
| `src/client/shared/Spaarke.UI.Components/src/services/__tests__/BffDataverseClient.test.ts` | unit test with a fake fetch | No (test). Passes unchanged (§7). |
| `tests/integration/Sprk.Bff.Api.IntegrationTests/Api/Dataverse/FetchEndpointTests.cs`, `RecordEndpointTests.cs` | the routes themselves | Tests of the deleted routes — deleted with them (§4). |
| `tests/integration/contract/Api/ExternalAccess/ExternalModuleDataContractTests.cs`, `tests/integration/auth/UnifiedAccessControl/ExternalModuleColumnAllowListTests.cs` | `/api/v1/external/api/dataverse/*` | No — external seam. Unchanged, pass. |
| Every in-repo internal DataGrid host (13 sites listed in the POML background, re-checked) | `XrmDataverseClient` (host `Xrm.WebApi`, Dataverse-enforced) | No. |
| `docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md:126`, `projects/set-regarding-and-field-mapping-resolver-r2/notes/task-012-notes.md` | say `createBffDataService` routes through `GET /api/dataverse/record/...` | No — that statement was wrong (see the `bffDataServiceAdapter` row). Doc corrected. |
| `projects/**` planning notes / POMLs (datagrid-framework-r1, SPA-external-access-r2, this project) | design history | No (not code). |

### 2.2 Published API descriptions — none name either route

`git ls-files` for `*openapi*`, `*swagger*`, `ai-plugin*`, `declarativeAgent*`, `*plugin*.json`,
`*manifest*.json`, `apispec`, `.apim`: the only Spaarke-owned API descriptions are
`src/solutions/CopilotAgent/spaarke-bff-openapi.yaml`, `spaarke-api-plugin.json`, `declarativeAgent.json`,
`appPackage/manifest.json`; plus `src/client/external-spa/appPackage/manifest.json` and the Outlook / Word
add-in manifests. **None contains the string `dataverse`.** (The `knowledge/**` hits are third-party samples.)
The BFF publishes no runtime OpenAPI document (no `AddOpenApi` / `MapOpenApi` / `AddSwaggerGen` /
`UseSwagger` under `src/server/api`).

### 2.3 Live telemetry (read-only, supporting evidence; the owner rule is repo-based)

Log Analytics workspace `spe-logs-dev-67e2xz` (retention 90 days), `AppRequests` over P90D:

| Route | Requests in 90 days |
|---|---|
| `POST /api/dataverse/fetch` (internal) | **0** |
| `GET /api/dataverse/record/{entityLogicalName}/{id:guid}` (internal) | **1** — 2026-08-17T21:23:56Z, `account/00000000-…`, 500, one second after a `GET /api/dataverse/metadata/account` 500, no CORS preflight: a hand probe, not a consumer |
| `POST /api/v1/external/api/dataverse/fetch` | 108 (82×200, 26×500) |
| `GET /api/v1/external/api/dataverse/record/...` | 146 |

So the external seam is the live consumer of `FetchService` / `RecordService`; the internal routes had none.

### 2.4 Escalation triggers checked

- Trigger 1 (a consumer depends on unscoped rows): **not fired** — no consumer.
- The POML's last trigger ("Step 1 finds no consumer … implement the fix anyway; deleting is an owner
  decision") is **superseded** by amendments 4 and 5, which record that owner decision (round 10 item 1
  names this proxy as a deletion candidate).
- Triggers 2-8 concern the impersonation build, which amendment 5 removed from scope.

## 3. Routes deleted (amendment 4: listed for the PR)

| Route | File deleted | No-caller evidence | Not-published evidence |
|---|---|---|---|
| `POST /api/dataverse/fetch` | `src/server/api/Sprk.Bff.Api/Api/Dataverse/FetchEndpoints.cs` (218 lines) | §2.1: no in-repo caller (the only `BffDataverseClient` instance targets `/api/v1/external`); §2.3: 0 requests in 90 days | §2.2: in no OpenAPI / plugin / agent / add-in manifest; no runtime OpenAPI |
| `GET /api/dataverse/record/{entityLogicalName}/{id:guid}` | `src/server/api/Sprk.Bff.Api/Api/Dataverse/RecordEndpoints.cs` (205 lines) | §2.1 (as above; `createBffDataService` never reached it); §2.3: one hand probe in 90 days | §2.2 (as above) |

`Program.cs`: `app.MapFetchEndpoints()` / `app.MapRecordEndpoints()` removed; registration comments updated.

## 4. What else went with them (amendment 4: "services, filters and options that only the deleted routes used")

| Removed | Why it is safe |
|---|---|
| `EntitySource.FromFetchXmlBody` and `EntitySource.FromRouteValueWithRecord` (`DataverseAuthorizationFilter.cs`) | Only `FetchEndpoints.cs:67` / `RecordEndpoints.cs:48` used them (Grep `src`, `tests`). |
| The filter's optional `IFetchXmlEntityExtractor` dependency, `FetchXmlParseException` catch, the multi-entity `GetReadableEntitiesAsync` branch, and the reflection-based `ExtractFetchXmlFromArguments` | Reachable only through `FromFetchXmlBody`. The surviving `FromRouteValue` path resolves exactly one entity and calls `HasReadPrivilegeAsync` — the same call, same 400/401/403 bodies as before for savedqueries / metadata / gridconfigurations. `IDataversePrivilegeChecker.GetReadableEntitiesAsync` itself is untouched (other consumers). |
| `tests/.../Api/Dataverse/FetchEndpointTests.cs` (333 lines), `RecordEndpointTests.cs` (185 lines) | Tests of the deleted routes, incl. the three link-entity-bypass tests of the deleted `FromFetchXmlBody` mode. The external seam's own FetchXML-guard tests (`FetchXmlGuardSelfJoinTests`, `ExternalModuleColumnAllowListTests`, `ExternalScopeCharacterizationTests`) still cover `FetchXmlEntityExtractor`. |
| `DataverseIntegrationTestFixture`: the real-extractor property + registration swap; `MockServiceClientFactory`: `ReturnRecord`, `ReturnRecordNotFound`, `GetReadableEntitiesCalls`, and the `GetReadableEntitiesAsync` setups in `GrantReadOn` / `DenyAllReads` | Only the deleted tests used them (Grep). |

**Kept (still used):** `FetchService`, `RecordService`, `FetchXmlEntityExtractor` / `IFetchXmlEntityExtractor`,
`FetchRequestDto` / `FetchResponseDto`, `AddDataverseFetchServices` / `AddDataverseRecordServices` — all are
consumed by `ExternalModuleDataEndpoints.cs` (`:180-203`, `:312-320`, `:630`). Their bodies are byte-identical;
only doc comments changed, and the false claims are gone:
`RecordService` no longer claims "row-level access is enforced … via the impersonated CallerId path" (it never
was); `FetchService` no longer says "every entity has been verified"; both now state, in 🔴, that they read
app-only and that scoping is the caller's job.

## 5. No SDK impersonation entry point (amendment 5)

No production caller needs SDK `ServiceClient` impersonation now. Tasks **159, 161, 162, 163, 164, 165 and 166**
all impersonate through the **Web API** path — `DataverseWebApiService`'s `impersonateSystemUserId`
(`MSCRMCallerID`, refuses `Guid.Empty` per task 104) and `RetrieveMultipleImpersonatedAsync` — per their own
POMLs (Grep of each POML for `impersonateSystemUserId` / `MSCRMCallerID` / `RetrieveMultipleImpersonatedAsync`
hits all seven; none asks for `ServiceClient.CallerId`). So:

- `src/server/shared/Spaarke.Dataverse/DataverseImpersonation.cs` — **unchanged** (no `CloneAsSystemUser`).
- `Services/Dataverse/Privileges/UserPrivilegeChecker.cs:147-148` — **unchanged**; it stays the only SDK
  `CallerId` assignment in `src/server`.
- No `DataverseCallerFaultClassifier`, no `DataverseImpersonationSiteGuardTests`, no
  `DataverseCallerFaultClassifierTests`, no edit to `DataverseImpersonationHelperTests.cs`.

The amendment's rule "never assign `ServiceClient.CallerId` / `CallerAADObjectId` outside
`DataverseImpersonation.cs`" is therefore **not yet enforced by an arch test**, and the one existing site
(`UserPrivilegeChecker`) is outside that file. The first task that genuinely needs SDK impersonation should add
the entry point, move `UserPrivilegeChecker` onto it, and add the site guard together (the design in the POML
body, constraints "impersonation mechanism" and acceptance criterion "rule 1", is ready to reuse).

## 6. Placement + justification (CLAUDE.md §10 / §11)

- **Placement:** in BFF, existing surface, **net removal**: two endpoint files and two filter modes deleted; no
  new service, registration, endpoint, option, job, column, package or OperationAccessPolicy key.
- **New surface:** one test file only — `tests/integration/Sprk.Bff.Api.IntegrationTests/Api/Dataverse/DataverseProxyRoutesRemovedTests.cs`.
  (1) Existing: the deleted `FetchEndpointTests` / `RecordEndpointTests` drove these routes and are gone;
  the route guard's census (`TheEndpointFileCensusIsPinned`) only counts FILES and would not notice a route
  re-added inside an existing file. (2) Extension: no existing test asserts these paths are unmapped.
  (3) Cost of doing nothing: re-adding either app-only route (in any file) would pass every suite silently.
  It is the deny test for the ledger (§8).
- No Dataverse plugins (ADR-002); fails closed (the route no longer exists → 404 before any read).

## 7. Tests

| Suite | Result |
|---|---|
| Affected: `Sprk.Bff.Api.IntegrationTests` `Api.Dataverse.*` | 15/15 passed (savedquery, metadata, gridconfiguration + the 2 new removal tests) |
| Affected: `Spaarke.ArchTests` `RouteAuthorizationGuardTests` | 15/15 passed (census 120 → 118) |
| Affected: `Spaarke.UI.Components` `BffDataverseClient.test.ts` | 22/22 passed (header comment only changed; `eslint` clean on the file) |
| Full `tests/unit/Sprk.Bff.Api.Tests` | 14212 passed, 54 skipped, 0 failed (20 m 41 s, shared machine) |
| Full `tests/Spaarke.ArchTests` | 346/346 passed |
| Full `tests/integration/Sprk.Bff.Api.IntegrationTests` | 90/90 passed |
| Full `tests/integration/Spe.Integration.Tests` | 403 passed, 25 skipped, 0 failed |

### 7.1 Other gates

- BFF build: 0 warnings, 0 errors.
- No `.csproj` / package change; `dotnet list package --vulnerable --include-transitive`: no vulnerable packages.
- Publish size: skipped per the orchestrator's instruction (net removal of two files; no package change).
- `/conflict-check`: no open PR touches any file in this diff (18 open PRs checked); master has no change to
  these files since the branch base. Soft warn: task 167 (parallel, same project) edits
  `RouteAuthorizationGuardTests.cs` — the census constant will conflict textually (§8).
- External-seam tests ExternalModuleDataContractTests, FetchXmlGuardSelfJoinTests, ExternalModuleColumnAllowListTests,
  ExternalScopeCharacterizationTests: unmodified and passing (in the unit suite run).
- Step 9.5: code-review — no Critical, no Warning that needed a change (one Suggestion: the filter's structured-log
  field names became singular, `deniedEntity` / `entity`, matching the savedquery handler; no query or alert in the
  repo reads the old names). adr-check — ADR-001/002/007/008/010/019/028/038 compliant; no violation, no §6.5 path
  needed. The new test drives the real host (no ADR-038 B1-B8 pattern).

### 7.2 Bite proof (the one new guard)

Seeded in `Program.cs` (then removed, file touched):

```csharp
app.MapPost("/api/dataverse/fetch", () => Results.Ok()).RequireAuthorization(); // SEED-160
app.MapGet("/api/dataverse/record/{entityLogicalName}/{id:guid}", () => Results.Ok()).RequireAuthorization(); // SEED-160
```

Both `DataverseProxyRoutesRemovedTests` facts FAILED:

```
Failed …DataverseProxyRoutesRemovedTests.PostFetch_IsNotMapped_ForAnAuthenticatedCallerWithReadOnEveryEntity
  Expected response.StatusCode to be HttpStatusCode.NotFound {value: 404} because POST /api/dataverse/fetch was
  deleted (task 160); an app-only FetchXML passthrough must not come back, but found HttpStatusCode.OK {value: 200}.
Failed …DataverseProxyRoutesRemovedTests.GetRecord_IsNotMapped_ForAnAuthenticatedCallerWithReadOnEveryEntity
  Expected response.StatusCode to be HttpStatusCode.NotFound {value: 404} because GET /api/dataverse/record/
  {entityLogicalName}/{id} was deleted (task 160); an app-only read-any-record-by-id route must not come back,
  but found HttpStatusCode.OK {value: 200}.
```

Restored; both pass. The census bite is inherent: with the two files gone and the constant left at 120,
`TheEndpointFileCensusIsPinned` fails ("expected 120, found 118"), which is why amendment 4 requires the bump.

Bite proofs (a)-(i) of the POML belong to the impersonation build that amendment 5 removed; they have no
subject.

## 8. Route authorization ledger input (amendment 3 — task 167 has NOT landed on this branch)

`RouteAuthorizationGuardTests.cs` on this branch has no `GovernedFiles` entry and no waiver for `Api/Dataverse/*`
(task 167 not landed). The only edit to that file is the census constant (120 → 118) with its dated comment,
which amendment 4 requires. No waiver, ledger entry or GovernedFiles entry was added.

| Route key (as the guard spells it) | Mechanism that now decides | Deny test |
|---|---|---|
| `POST /api/dataverse/fetch` | **DELETED** (route removed; the request 404s before any filter or Dataverse read) | `Sprk.Bff.Api.IntegrationTests.Api.Dataverse.DataverseProxyRoutesRemovedTests.PostFetch_IsNotMapped_ForAnAuthenticatedCallerWithReadOnEveryEntity` |
| `GET /api/dataverse/record/{entityLogicalName}/{id:guid}` | **DELETED** (as above) | `Sprk.Bff.Api.IntegrationTests.Api.Dataverse.DataverseProxyRoutesRemovedTests.GetRecord_IsNotMapped_ForAnAuthenticatedCallerWithReadOnEveryEntity` |

**For the main session at 167 integration:** if 167 added `Api/Dataverse/FetchEndpoints.cs` /
`RecordEndpoints.cs` to `GovernedFiles` (or any waiver for these two route keys), DELETE those entries — the files
no longer exist and `ScanFile` treats a missing governed path as unparseable (fail). The census constant on
167's side must also drop by 2 for these files (whatever 167's own count is, minus 2). The surviving siblings
`SavedQueryEndpoints.cs`, `MetadataEndpoints.cs`, `GridConfigurationEndpoints.cs` keep
`.AddDataverseAuthorizationFilter(EntitySource.FromRouteValue)`; note for 167's classification that this filter
is ENTITY-LEVEL ONLY (its class remarks now say so) — those routes return schema/view definitions, not record
data, which is why they were not in the sweep's confirmed class.

## 9. Observations for other owners (not changed here)

1. **`ExternalModuleDataEndpoints.cs:30-44`** (owned by tasks 134/157; this task must not edit it) says its
   stripping lives there "because those are shared with the internal /api/dataverse surface". After this task
   the internal fetch/record surface is gone, so the external seam is the sole consumer of `FetchService` /
   `RecordService`. Its scoping need not move; only the sentence's rationale is stale.
2. **`createBffDataService` (`bffDataServiceAdapter.ts`)** builds `/api/dataverse/{entity}/{id}`, which matches
   no BFF route. Its one instance, `external-spa/src/pages/PlaybookLibraryPage.tsx:128`, therefore cannot read or
   write anything through it. Pre-existing, unrelated to this deletion (it never reached the deleted routes).
3. `DataGrid.tsx:99,110`, `LookupMultiFilterChip.tsx:123`, `ReconciliationWorkspace.tsx:264`,
   `ReconciliationGrid.tsx:207` doc comments still suggest `BffDataverseClient` for internal non-MDA hosts. The
   authoritative statements (the `BffDataverseClient.ts` header and the DataGrid architecture doc §4) now say an
   internal host needs a caller-scoped read path first; those component comments are left for the DataGrid owners.

No `.claude/**` file references these routes (Grep), so no main-session `.claude` edit is needed.

## 10. Live gate

None for the deletion itself (no Dataverse or Entra change). After deploy, an optional read-only confirmation
for the main session:

```bash
# expect 404 for both (authenticated workforce token), and the external seam unaffected
curl -s -o /dev/null -w "%{http_code}\n" -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"entityName":"sprk_project","fetchXml":"<fetch><entity name=\"sprk_project\"/></fetch>"}' \
  https://spaarke-bff-dev.azurewebsites.net/api/dataverse/fetch
curl -s -o /dev/null -w "%{http_code}\n" -H "Authorization: Bearer $TOKEN" \
  https://spaarke-bff-dev.azurewebsites.net/api/dataverse/record/sprk_project/00000000-0000-0000-0000-000000000001
```

The POML's live-gate items (a)-(j) tested impersonation and do not apply. Item (g) (external SPA grids still load
rows) is worth one look after deploy, since `FetchService` / `RecordService` registrations moved only in comments.
