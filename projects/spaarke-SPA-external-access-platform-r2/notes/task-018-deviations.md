# Task 018 — Deletion notes & deviations

> Delete inert `ExternalCallerAuthorizationFilter` + remove transitional `/api/v1/collab` group.
> FULL rigor · opus/xhigh · prescriptive. Executed 2026-08-07.

## What was deleted (all proven zero-caller first)
- `Api/Filters/ExternalCallerAuthorizationFilter.cs` — **zero call sites** for `AddExternalCallerAuthorizationFilter()` (fully inert; its CIAM logic lives in `CiamContactPrincipalStrategy` inside `CallerPrincipalResolver`).
- `Api/Filters/WorkforceCallerAuthorizationFilter.cs` — only consumers were the removed `/api/v1/collab` endpoints.
- `Api/ExternalAccess/WorkforcePrincipalContextEndpoint.cs` (`GET /api/v1/collab/me`).
- `Api/ExternalAccess/WorkforceCollaborationDownloadEndpoint.cs` (`GET /api/v1/collab/projects/.../content`).
- `Api/ExternalAccess/Dtos/WorkforcePrincipalContextResponse.cs` (DTO used only by the collab /me handler).
- `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/WorkforceCollaborationDownloadEndpointTests.cs` (tested the deleted endpoint).
- `ExternalAccessEndpoints.cs` — removed the `MapWorkforceCollaborationEndpoints` call + method + `/api/v1/collab` group mapping; class doc updated from three to two route groups.

## Zero-caller evidence (Step 2)
Repo-wide grep of ALL client surfaces (external-spa, PCF, solutions) + tests for `/api/v1/collab` returned **only server-side definitions** — no client or test invokes the collab surface. The replacement path (`/api/v1/external`, principal-agnostic, `CallerPrincipalResolver` + `ExternalCollaboration` dual-scheme) has been live since task 015 / teams-app-r1 task 025.

## Conflict-check (Step 1)
**SOFT warn** — `smart-todo-decoupling-r3` has unmerged changes in `Api/ExternalAccess/` but on **disjoint files** (`ExternalProjectDtos.cs`, `ExternalProjectDataEndpoints.cs`, `ExternalTodoDtoTests.cs`) — none overlap the deletion targets. teams-app-r1's FR-22 external-access surface is already merged to master. Consequence: **deliberately did NOT edit `ExternalProjectDataEndpoints.cs`** (see residue below) to avoid manufacturing a merge conflict with that active worktree.

## KEPT (not in task 018's deletion scope — verified still-consumed or reserved for P2)
- `WorkforcePrincipalResolver` + `IWorkforcePrincipalResolver` — **actively consumed by `CallerPrincipalResolver`** (the R2 principal-agnostic KEEP path), plus `ContactStandingGrantReader`, `ExternalCallerContext`, `TenantEnvironmentRouter`. DI registration in `ExternalAccessModule.cs:81` kept. Its unit tests kept (filter tests surgically removed — see below).
- `WorkforcePrincipal` / `WorkforceDenyReason` / `WorkforcePrincipalResolution` — model types used by the resolver.
- `AccessibleRecordSetAuthorizationFilter` — **now unattached** (its only call site was the removed collab download endpoint). **Kept intentionally** — it is NOT in task 018's deletion list and is the P2 task 022/030 accessible-record-set enforcement building block. Its comments/log still describe the P2 composition (accurate as forward intent). **Follow-up for P2 (022/030): re-wire it onto the principal-agnostic surface (or delete if superseded).**

## Test edits
- `WorkforcePrincipalResolverTests.cs` — **surgical**: removed the 3 `WorkforceCallerAuthorizationFilter` endpoint-filter tests + the `BuildFilterContext`/`NextSpy` helpers + now-unused usings (`Microsoft.AspNetCore.Http`, `Api.Filters`); the resolver tests (the KEEP subject) all retained. 170 ExternalAccess unit tests pass.
- `ExternalAccessIntegrationTests.cs` + e2e `access-level-enforcement.spec.ts` — comment/`@see` refresh: the deleted type name → `CallerPrincipalAuthorizationFilter` (the current `/api/v1/external` filter). The e2e `@see` had pointed at the deleted file.

## Known residue (documented, not fixed — with rationale)
Zero **CODE** references to the deleted types remain (build proves it). Remaining references are all in **comments/doc-strings** in kept files, of two kinds:
1. **Intentional lineage history** — e.g. "the CIAM strategy reproduces the *old* `ExternalCallerAuthorizationFilter` byte-for-byte", "replaces the CIAM-only `AddExternalCallerAuthorizationFilter`", "same deny mapping as `WorkforceCallerAuthorizationFilter`". Accurate and valuable; kept.
2. **Pre-existing stale current-state comments** naming the deleted filter as if live, in `ExternalProjectDataEndpoints.cs`, `ExternalUserContextEndpoint.cs`, `ExternalCallerContext.cs`, `ExternalDataService.cs`. These **predate task 018** (drifted at the 015 principal-agnostic switch). Left as-is: `ExternalProjectDataEndpoints.cs` is actively modified by `smart-todo-decoupling-r3` (editing it would create a merge conflict); the others are out of task 018's deletion scope. Low-value cosmetic drift; safe to sweep in a later ExternalAccess-touching task.

## Verification
- `dotnet build src/server/api/Sprk.Bff.Api/` — **0 errors** (23 pre-existing warnings, none from this task).
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/` — **10155 passed, 101 skipped, 2 failed**. The 2 failures (`DataverseEntitySchemaTests.AllUpdateDocumentRequestProperties_HaveFieldMappings` + `ExpectedFieldMappings_MatchesUpdateDocumentRequestPropertyCount`) are **PRE-EXISTING and unrelated to task 018** — proven by stashing all task-018 changes and re-running: they fail identically on the clean pre-018 base. Root cause: a merged project added properties to `UpdateDocumentRequest` (Documents/email domain) without updating that test's `ExpectedFieldMappings` dictionary; inherited via the 137-commit master merge. **See ISS-018-1 below.** Task 018 introduces **zero** new failures.
- `/api/v1/external` (170 ExternalAccess unit tests) + `/api/v1/external-access` intact — no route regression.
- Publish (Release, compressed): **48.38 MB** (+0.09 vs 48.29 baseline; negligible restore noise; well under 60 MB ceiling). No HIGH/CRITICAL CVE (zero packages added).

## Escalation / issue to file
- **ISS-018-1** — `DataverseEntitySchemaTests` (2 tests) red on master-merged base: `UpdateDocumentRequest` gained properties without corresponding `ExpectedFieldMappings` entries. Documents/schema domain (from the master merge, likely email-communication-intelligence-r2 / a compose/office change). Fix requires the correct `sprk_` field name + type per new property (Documents-domain knowledge) — out of task 018's ExternalAccess scope. Recommend filing via `/defer` against the Documents/schema owner. Task 018 is green in its own domain and adds no regression.
