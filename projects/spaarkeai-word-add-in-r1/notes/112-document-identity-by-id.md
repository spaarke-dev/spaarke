# Task 112 — `GET /api/documents/{documentId}/identity` (UAT round 11 item 4)

Owner approved the server addition 2026-10-07. Record: `notes/042-uat-round11-2026-10-07.md` item 4.

## Why

A document the pane knows only by its stamp (e.g. after Quick Save) reaches the pane with `relatedRecord: null` and empty names. The pane cannot tell "unfiled" from "record unknown", so it can neither show the record nor offer filing. This route returns the document's actual identity from the server, given its id.

## Contract

`GET /api/documents/{documentId}/identity` → `DocumentIdentityResponse`, the same DTO `POST /api/documents/resolve-identity` returns:
- `resolved: true`, `documentId` (bare lowercase), `documentName`, `fileName`, `reason: null`;
- `relatedRecord` comes from the same four direct slots in the same priority (matter > project > invoice > work assignment), with the same `name` / `displayName` / `number` semantics. It is `null` when the document is unfiled.

| Situation | Result |
|---|---|
| Caller may read the document | 200 with the identity |
| Caller may NOT read it | 403 problem+json from `DocumentAuthorizationFilter`, with no id, name or record in the body. The handler never runs and Dataverse is never read. |
| **Unknown id** | **403**, the same answer as "not yours". Dataverse grants no rights on a row that does not exist, so the filter denies. This is deliberate: the route is not an existence oracle. |
| Allowed, but the row is gone by the time the handler reads it | 404 `document_not_found` (the sibling `{documentId}` routes use the same code) |
| Allowed, but Dataverse cannot answer | 503 `identity_resolution_unavailable`, never treated as "absent" |
| Allowed, but the id is not a GUID | 400 `invalid_id`. In practice the filter denies a non-GUID first. |
| No credentials | 401 |

**Why 404 and not `resolved:false`.** resolve-identity's `resolved:false` reasons describe a URL, and the pane reads them as "a new document". For an id the pane already holds, "new document" is the wrong conclusion. 404 is how every id-keyed route in this group reports a missing row, and the pane keeps its "record unknown" state on any non-200.

## Authorization

- `.AddDocumentAuthorizationFilter("read")` on the route's `documentId`: the same filter and operation as resolve-identity and `/open-links`. No new rule, and the filter is unchanged.
- The handler does no access check of its own (ADR-008). The related record's display fields follow the existing rule that record access is equivalent to document access (`notes/026-slot-scope-decision.md` §6).
- The row read is app-only through `IGenericEntityService`, as on the URL path. It runs only after the filter has allowed the caller.
- Registered unconditionally (no feature flag). resolve-identity's behaviour and response are unchanged: both handlers now call one extracted helper, `BuildIdentityResponseAsync`, and both resolution paths build the row through one `ToResolution`.

## Code

- `Api/FileAccessEndpoints.cs`:
  - adds the route mapping and the `GetDocumentIdentity` handler;
  - extracts `BuildIdentityResponseAsync` out of the `ResolveIdentity` handler, which now calls it.
- `Services/Documents/DocumentUrlIdentityResolution.cs`:
  - `ResolveByIdAsync`: a by-id retrieve of the same `LookupColumns`. It returns null on `0x80040217`, matched by error code anywhere in the inner-exception chain. Any other failure is 503, including the alternate-key code `0x80060891`, which a by-id read never legitimately returns. Caller cancellation propagates.
  - `ToResolution`: shared by both paths.
  - `IsByIdNotFound`: the not-found check above.
- `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs`: the governed-file description now says eleven routes instead of ten.

## Tests

- Unit: `tests/unit/Sprk.Bff.Api.Tests/Services/Documents/DocumentIdentityByIdResolutionTests.cs` (14 tests). Covers each slot type, slot priority, the unfiled case, the requested columns, absent (direct and wrapped), four indeterminate failures, and cancellation.
- Contract: `tests/integration/contract/Api/Documents/DocumentIdentityByIdContractTests.cs` (11 tests). Covers 200 with the related record for all four slot types, 200 unfiled, 403 with no metadata and no Dataverse read, unknown id → 403, 401, 404, 503 and 400.
- Both new files sit in namespaces the CI "Server tests (office scope)" filter already selects (`…Api.Documents`, `…Services.Documents`).

## Gates (2026-10-07)

- **Build:** `dotnet build src/server/api/Sprk.Bff.Api/` → 0 warnings, 0 errors.
- **Targeted tests:** identity, authorization-filter and new tests (filter `DocumentIdentityByIdContractTests | DocumentIdentityByIdResolutionTests | DocumentIdentityContractTests | RelatedRecordCardContractTests | DocumentUrlIdentity | Issue975 | DocumentAuthorizationFilter`) → **110 passed, 0 failed**.
- **CI office-scope selection:** the exact `TEST_FILTER` from `office-addins-tests.yml` → **4739 passed, 0 failed, 22 skipped (4761 total)**.
- **ArchTests:** `--filter FullyQualifiedName~RouteAuthorizationGuard` → **86 passed, 0 failed**.
- **Publish size (root CLAUDE.md §10):**
  - Method: fresh detached worktrees at short paths. `C:\wt112m` = `origin/master` @ `e34c7c0e2`. `C:\wt112b` = branch HEAD `4fd272446` plus this task's two server files; the branch has no other `src/server` difference from master.
  - Each side: `dotnet restore` + `dotnet publish -c Release`, then PowerShell `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, the same method as `Deploy-BffApi.ps1`.
  - Master **37,946,919 B (36.19 MB)** vs branch **37,948,518 B (36.19 MB)** → delta **+1,599 B (≈ +0.002 MB)**. Sizes include PDBs.
  - File counts 192 = 192 (4 PDBs each); no MSB3030 on either side. Both worktrees were removed afterwards.
- **CVE:** `dotnet list package --vulnerable --include-transitive` reports "no vulnerable packages" on both sides. No package or csproj change.
- **Overlap check:**
  - Across all 214 `git worktree list` branches and every `origin/work/*` branch, only UAC-r2 task branches differ from master in these files: `task/uac-r2-166-*` (6 branches), `integ/uac-r2-batch4` and `task/uac-r2-171`.
  - The 166 / batch4 hunks are the pointer check in the content/download handlers plus a `using` reorder. They are stale and superseded by merged #1312 / #1333.
  - `task/uac-r2-171` has a two-dot diff of 0 against master (already merged).
  - None of them touches the resolve-identity mapping or handler, or `DocumentUrlIdentityResolution.cs` / `FileOperationModels.cs`.

## Placement Justification (bff-extensions.md §A)

**In the BFF, as an extension of an existing surface.** It is a synchronous, sub-second request/response read behind the BFF's per-document authorization filter, with no background work. ADR-052 places nothing outside the BFF for this. It extends the existing `/api/documents` group, its filter, its DTO and its slot logic:
- no new service class, DI registration, package or config;
- no CRUD→AI dependency;
- Minimal API (ADR-001), endpoint-filter authorization (ADR-008), ProblemDetails errors (ADR-019).

**Three questions (CLAUDE.md §11):**
- **Existing:** resolve-identity (URL → identity).
- **Extension:** yes. Only the id source differs (route instead of URL).
- **Cost of doing nothing:** a stamp-identified document can never show its record or be filed from the pane.
