# Task 050 — OBO Version-History Endpoint Pair (list + open-prior-version, read-only)

🔒 RIGOR LEVEL: FULL — OBO auth-path + new endpoint + `.cs` change (root §8). code-review/adr-check run by orchestrator at Step 9.5.

## What shipped

| Surface | Route / Member | File |
|---|---|---|
| NEW endpoint (list) | `GET /api/obo/drives/{driveId}/items/{itemId}/versions` | `src/server/api/Sprk.Bff.Api/Api/DocumentVersionEndpoints.cs` |
| NEW endpoint (open prior, read-only) | `GET /api/obo/drives/{driveId}/items/{itemId}/versions/{versionId}/content` | same file |
| NEW OBO list primitive | `ListFileVersionsAsUserAsync` → `IReadOnlyList<VersionInfoDto>?` (id/label, lastModified, size; newest first; 404→null; 403→UnauthorizedAccessException) | `ISpeFileOperations.cs`, `DriveItemOperations.cs`, `SpeFileStore.cs` |
| Mapping | `app.MapDocumentVersionEndpoints()` UNCONDITIONAL, next to `MapOBOEndpoints()` | `Infrastructure/DI/EndpointMappingExtensions.cs` |
| Seam tests | 6 tests: list metadata + OBO-context assert, open v3-after-v4 exact bytes, item-404, no-restore-route, negative authorization (403 never bytes), unauthenticated 401 | `tests/integration/seam/Compose/SpeVersionHistoryOboSeamTests.cs` |

- Reuses `DownloadFileVersionAsUserAsync:842` unchanged for the open half (task 002 inventory row 4). New list primitive mirrors `GetCurrentVersionIdAsUserAsync`'s Graph call shape (inventory §3), returning the full mapped list. Reuses the pre-existing (previously unused) `Models.VersionInfoDto` projection — no new DTO type.
- Registration symmetry (§F.1): endpoint maps unconditionally; `ISpeFileOperations` was already registered unconditionally in `DocumentsModule` — **no new DI registration, no flag**.
- NO restore/branch/write path. NO app-only elevation anywhere in the new path (seam tests verify app-only facade methods are never invoked). Admin `ContainerItemEndpoints.cs:48` untouched.

## §11 Placement Justification (for PR)

**Existing**: closest neighbors are the admin version list `GET /api/spe/containers/{id}/items/{itemId}/versions` (`ContainerItemEndpoints.cs:48` — app-only, config-scoped) and the unexposed Compose-internal OBO primitive `DriveItemOperations.DownloadFileVersionAsUserAsync:842`. **Extension**: the admin endpoint cannot be widened — it authenticates as the SPE-admin app, not the calling user, so it cannot enforce the per-document user boundary ADR-028 requires; instead the new pair lands on the existing OBO SPE/Documents surface (`/api/obo/drives/...`) and reuses the existing OBO byte primitive verbatim — no new Graph-calling download code, no new service, no new subsystem, no new package; the only net-new Graph call is the small list primitive mirroring an existing call shape. **Cost of doing nothing**: FR-07 / Success Criterion 4 fails — render-on-save's "version history is the safety net" is unreachable from the product (a user cannot list or open v3 after v4 exists). Placement is in-BFF per `.claude/constraints/bff-extensions.md` decision criteria: it is a thin broker over the user's OBO Graph token on an existing endpoint surface — exactly the BFF's job.

## Verification

- `dotnet build src/server/api/Sprk.Bff.Api/` — **0 errors**.
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~Version|FullyQualifiedName~Compose"` — **1068/1068 passed, 0 failed** (includes the 6 new seam tests; full Compose suite zero regressions).
- Publish size (NFR-01, compressed zip of `dotnet publish -c Release` output): **48.34 MB incl. PDBs / 47.46 MB excl. PDBs** — delta vs task-003-era baseline (~49.63 MB incl. PDBs): **−1.29 MB**; well under the 60 MB ceiling and the +5 MB single-task escalation threshold.
- `dotnet list package --vulnerable --include-transitive` — **no vulnerable packages** (no new HIGH CVE).

## Deviations from POML

1. **403 mapping**: sibling OBO endpoints map `UnauthorizedAccessException` → 401; this pair maps it → **403 Problem** to meet the task's explicit "403/404, never the bytes" acceptance criterion (the exception models Graph's 403 under the user's own token — a *forbidden*, not *unauthenticated*, condition). Unauthenticated callers still get 401 via `RequireAuthorization()`.
2. **Publish output location**: published to the session scratchpad instead of `deploy/api-publish/` to avoid polluting the worktree; measurement methodology otherwise per §10.
3. **`/conflict-check`**: not run in this isolated sub-agent context (skill/orchestration is main-session-owned); the task's `parallel-reason` already flags the shared `EndpointMappingExtensions.cs` touch — orchestrator should run `/conflict-check` before the PR.
4. **TASK-INDEX.md / current-task.md**: not updated (main-session-owned per dispatch hard rules).
