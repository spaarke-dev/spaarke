# CICD-088 Gap: FileAccessEndpoints Requires Significant SpeFileStore Extension

**Filed**: 2026-06-26 during task CICD-088 (ADR-007 Graph isolation mechanical refactor)

## What was done

17 of 18 target endpoint files refactored to remove direct `using Microsoft.Graph*`
imports and replace `catch (ODataError ...)` with `catch (SpaarkeStorageException ...)`,
wrapping graph calls in `GraphCallScope.Run(...)`.

Foundation files unchanged (per brief):
- `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpaarkeStorageException.cs`
- `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphErrorTranslator.cs` (extended
  with `ToProblemDetails()` so endpoints don't depend on `ProblemDetailsHelper.FromGraphException(ODataError)`,
  which itself still references Microsoft.Graph)
- `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphCallScope.cs`

## What was NOT done — file 17 of 18

**`src/server/api/Sprk.Bff.Api/Api/FileAccessEndpoints.cs`** — SKIPPED.

### Why

This file does NOT follow the EASY pattern of "catch ODataError + call graphService.X(graphClient, ...)".
It injects `IGraphClientFactory graphFactory` directly and calls the Graph SDK's request
builder fluent API inline, including:

- `graphClient.Drives[driveId].Items[itemId].Preview.PostAsync(previewRequest, ct)`
  where `previewRequest` is `PreviewPostRequestBody` (from
  `Microsoft.Graph.Drives.Item.Items.Item.Preview` namespace — ODT request DTO)
- `graphClient.Drives[driveId].Items[itemId].GetAsync(req => req.QueryParameters.Select = ...)`
  returning `DriveItem` (from `Microsoft.Graph.Models`)
- `graphClient.Drives[driveId].Items[itemId].Content.GetAsync(ct)` returning `Stream`

Six methods affected: `GetPreviewUrl`, `GetPreview`, `GetContent`, `GetOffice`, `GetOpenLinks`, `GetViewUrl`.
`GetDownload` is already SpeFileStore-mediated and would refactor cleanly.

### What's needed for proper fix (separate task)

Add Microsoft.Graph-isolating facade methods to `SpeFileStore` (or a new
`SpeDocumentAccessService` in `Infrastructure.Graph`) that hide both the Graph
request builder fluent API and the response DTOs:

1. `Task<string?> GetPreviewUrlAsync(string driveId, string itemId, bool chromeless, string? viewer, CancellationToken ct)`
   — returns just the URL string (caller hashes it / adds nb=true)
2. `Task<SpeDriveItemSummary?> GetDriveItemMetadataAsync(string driveId, string itemId, IEnumerable<string> selectFields, CancellationToken ct)`
   — `SpeDriveItemSummary` is a Spaarke-domain record exposing only `Id`, `Name`,
   `WebUrl`, `WebDavUrl`, `Size`, `LastModifiedDateTime`, `ParentReferencePath`,
   `MimeType` (the fields FileAccessEndpoints actually consumes)
3. `Task<Stream?> GetItemContentStreamAsync(string driveId, string itemId, CancellationToken ct)`
   (or reuse `DownloadFileAsync` from existing SpeFileStore)

Note: SpeFileStore already has `DownloadFileAsync` (app-only), but the OBO variant
for these endpoints needs adding (`DownloadFileAsUserAsync` exists for OBOEndpoints —
might be reusable here with HttpContext flow).

### Estimated effort

- 3 new SpeFileStore methods (~30 min each = 1.5h)
- Refactor 6 FileAccessEndpoints handlers to use facade (~30 min)
- Test pass + DI verification (~30 min)
- Total: ~2.5h

Exceeds the 1-2h ceiling the brief set for in-task escalation. Per brief
explicit guidance: "If the cost exceeds 1-2h, STOP and escalate."

## ADR-007 arch test impact

`EndpointsShouldNotReferenceGraphSdk` will STILL fail after CICD-088 with at
minimum `Sprk.Bff.Api.Api.FileAccessEndpoints` in the failing list, plus any
endpoint files where the IL still references `GraphServiceClient` via local
variable inference (e.g., `var graphClient = await graphService.GetClientForConfigAsync(config, ct);`).

The brief was scoped on the assumption that removing the `using` directive +
swapping catch types + wrapping calls in `GraphCallScope.Run` would be sufficient.
In practice the arch test's `HaveDependencyOn("Microsoft.Graph")` predicate
inspects ALL IL (locals, method call targets, generic params) — not just method
signatures. So any local of type `Microsoft.Graph.GraphServiceClient`
(produced by `var graphClient = await graphService.GetClientForConfigAsync(...)`)
will still trigger the test failure.

The structural fix — out of scope for CICD-088 — is to either:

- **A**: Add ContainerTypeConfig-taking overloads to every `SpeAdminGraphService.XxxAsync(GraphServiceClient, ...)` method so endpoints never call `GetClientForConfigAsync` (effort: 50+ overloads, ~4-6h)
- **B**: Inline-resolve the Graph client inside `GraphCallScope.Run` itself by passing `(ContainerTypeConfig, Func<...>)` and having the helper resolve+invoke (~30 min change in GraphCallScope, but Endpoints still need plumbing rework to pass config rather than graphClient — ~2-3h)

Recommendation: separate task CICD-088b to do Path B before re-running ADR-007
arch tests as a true PASS gate.

## Files refactored cleanly in CICD-088 (17 of 18)

EASY (15):
1. `Api/SpeAdmin/ConsumingTenantEndpoints.cs` (5 catches)
2. `Api/SpeAdmin/ContainerColumnEndpoints.cs` (6 catches)
3. `Api/SpeAdmin/ContainerCustomPropertyEndpoints.cs` (4 catches)
4. `Api/SpeAdmin/ContainerEndpoints.cs` (7 catches + lifecycle helper closure refactor)
5. `Api/SpeAdmin/ContainerTypePermissionEndpoints.cs` (1 catch)
6. `Api/SpeAdmin/RecycleBinEndpoints.cs` (5 catches)
7. `Api/SpeAdmin/SearchContainersEndpoints.cs` (1 catch)
8. `Api/SpeAdmin/SearchItemsEndpoints.cs` (1 catch)
9. `Api/SpeAdmin/SecurityEndpoints.cs` (2 catches)
10. `Api/ContainerItemEndpoints.cs` (9 catches + ProblemDetailsHelper.FromGraphException swap)
11. `Api/DataverseDocumentsEndpoints.cs` (1 catch + 1 graph call wrap)
12. `Endpoints/SpeAdmin/ContainerTypeEndpoints.cs` (3 catches)
13. `Endpoints/SpeAdmin/ContainerTypeSettingsEndpoints.cs` (1 catch)
14. `Api/UploadEndpoints.cs` (2 catches + `using Microsoft.Graph` removal)
15. `Api/DocumentsEndpoints.cs` (8 catches + `using Microsoft.Graph` removal)

MEDIUM (2):
16. `Api/OBOEndpoints.cs` (7 catches + `using Microsoft.Graph;`/`Microsoft.Graph.Models;` were stray and removed; no actual typed-DTO usage in method bodies)
17. `Endpoints/SpeAdmin/ContainerPermissionEndpoints.cs` (6 catches + GraphApiProblem helper retype; `Microsoft.Graph.Models` was stray)

SKIPPED (1):
18. `Api/FileAccessEndpoints.cs` — see above
