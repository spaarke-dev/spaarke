# Task 058: delete the fabricated-data Office routes

> **Date**: 2026-09-30 · **Rigor**: FULL (opus / xhigh) · **Issue**: [#229](https://github.com/spaarke-dev/spaarke/issues/229) (Office part)

## 1. What this closed, and what it did NOT

**This task removed a LATENT hazard, not a live disclosure.**
- The four routes read nothing.
- They synthesized "documents" from the GUID the caller sent, so there was no data to leak and no access to grant.
- The hazard was that a future developer could implement one of them with a real read and no per-document filter.

**The LIVE authorization gaps the 2026-09-21 review found are closed by OTHER tasks, not this one:**

| Task | Gap |
|---|---|
| 062 | F1: `/office/search/entities` untrimmed enumeration |
| 063 | F2: send-to-index tenant binding |
| 064 | F3: To Do source records |
| 065 | F4: saves with no target |
| 066 | F9: communications routes |
| 067 | F5: job ownership fail-open |

Do not cite this deletion as having fixed any of them.

## 2. Reproduce-first evidence (live, before deleting)

`spaarke-bff-dev` (master `2682e8225`). `POST /api/office/share/links` with a user token
(`az account get-access-token --scope api://1e40baad-…/user_impersonation`), 2026-09-30 23:5x UTC:

| Request `documentIds` | HTTP | Response (verbatim, trimmed) |
|---|---|---|
| `31af48f7-6445-44c4-bdbe-c6b8d49e7bfc`: **random, no such document** | **200** | `{"links":[{"documentId":"31af48f7-…","url":"https://spaarke.app/doc/31af48f7-…","displayName":"Document 31af48f7","fileName":"document-31af48f7.docx","contentType":"application/vnd.openxmlformats-officedocument.wordprocessingml.document","size":245678,"iconUrl":"/icons/word.svg"}],"invitations":null,"errors":null,"allSucceeded":true,…}` |
| `77bcfa39-dabc-f111-aaaf-3833c5e9614d`: **real**, `Invoice-10044725.pdf`, 697,393 bytes | **200** | `{"links":[{"documentId":"77bcfa39-…","url":"https://spaarke.app/doc/77bcfa39-…","displayName":"Document 77bcfa39","fileName":"document-77bcfa39.docx",…,"size":245678,"iconUrl":"/icons/word.svg"}],…}` |

The two responses are **structurally identical**. The real PDF is reported as a `.docx` of 245,678 bytes, so the route
never looks at the document. That is why deletion, not authorization, is correct.

**Deviation from the criterion:** it asked for "a real document the caller cannot access". The only token available was
an administrator's, which can access everything. A real document receiving the same fabricated response proves the
same point: the route ignores the document entirely. No probe code was written, so there is nothing to remove.

## 3. Callers

- **In the repo: none.**
  - The pane's share-link path is `/api/documents/{id}/share-link` (`FileAccessEndpoints`), a different route.
  - `useShareFlow.ts` was the only client reference, and it referenced only itself. Its `apiClient.searchDocuments` /
    `generateShareLinks` calls appear only in its own doc comment; `ApiClient.ts` has neither method.
- **Outside the repo: no evidence of any caller, but this is INCONCLUSIVE.**
  - Application Insights `spe-insights-dev-67e2xz` records only my two probes.
  - That component holds just 85 requests in total, all since 22:46 UTC today, so it has no 30-day history.
  - The escalation trigger fires only if a caller is FOUND; none was. Any external caller would have been receiving
    fabricated data.

## 4. Deletion inventory

**BFF**

| File | Removed |
|---|---|
| `Services/Office/OfficeService.cs` (−775 lines) | `SearchDocumentsAsync`, `GenerateStubDocumentResults`, `CreateShareLinksAsync`, `SimulateSharePermissionCheckAsync`, `GetDocumentMetadataForLinkAsync`, `GenerateShareLinkUrl`, `GetDocumentIconUrl`, `ProcessShareInvitationsAsync`, the private `ShareLinkDocumentMetadata` record, `GetRecentDocumentsAsync`, `GenerateStubRecentAssociations`, `GenerateStubRecentDocuments`, `GenerateStubFavorites`, `GetAttachmentsAsync`, `PackageAttachmentAsync`. **Kept: `GenerateStubResults`** (it belongs to `SearchEntitiesAsync`) |
| `Services/Office/IOfficeService.cs` | `SearchDocumentsAsync`, `CreateShareLinksAsync`, `GetRecentDocumentsAsync`, `GetAttachmentsAsync` |
| `Api/Office/OfficeEndpoints.cs` (−623 lines) | `GET /search/documents` (mapping + handler), `MapShareEndpoints` + the `/share` group (`/links`, `/attach` + 2 handlers), `MapRecentEndpoints` (`/recent` + handler) |
| Models (7 files deleted) | `DocumentSearchRequest`, `DocumentSearchResponse` (+`DocumentSearchResult`), `ShareLinksRequest` (+`ShareLinkRole`), `ShareLinksResponse` (+`DocumentLink`, `ShareInvitation`, `InvitationStatus`, `ShareLinkError`), `ShareAttachRequest` (+`AttachmentDeliveryMode`), `ShareAttachResponse` (+`AttachmentPackage`, `AttachmentError`), `RecentDocumentsResponse` (+`RecentAssociation`, `RecentDocument`, `FavoriteEntity`, the Office `EntityReference`). Each was verified by grep to be referenced only by deleted code |
| **`Models/Office/AssociationType.cs` (new)** | 🔴 `AssociationType` was **defined inside `RecentDocumentsResponse.cs`** and is live: the job queue, the ownership resolver and the upload worker use it. It was **moved verbatim** (byte-identical, checked) before the file was deleted: `Account` stays removed and **ordinal 3 stays BURNED** |
| `Api/Filters/OfficeRateLimitFilter.cs`, `Configuration/OfficeRateLimitOptions.cs` | `OfficeRateLimitCategory.Share` / `.Recent`, their switch arms, `ShareRequestsPerMinute` / `RecentRequestsPerMinute` (`[Range]`-validated). No repo config and no deployed `spaarke-bff-dev` app setting set either key |

**Tests, each named**

| Test file | Change |
|---|---|
| `tests/integration/contract/Api/Office/OfficeEndpointsContractTests.cs` | Removed the Document Search (2, already Skip), Share Links (2), Share Attach (2) and Recent (2) regions. Their KEEP-path "replacement" is the routes' removal (ADR-038) |
| `tests/integration/contract/Api/Office/OfficeEndpointAuthorizationContractTests.cs` | `Get_OfficeRecent_WhenUnauthenticated_Returns401` → **`Get_OfficeMatterTypes_WhenUnauthenticated_Returns401`**, repointed at `GET /api/office/search/matter-types` (same `OfficeAuthFilter`), so the authentication-gate coverage is kept, not dropped |
| `tests/unit/Sprk.Bff.Api.Tests/Filters/Office/RateLimitFilterTests.cs` | The `Share`/`Recent` cases and option values |
| `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs` | **The four Pending waivers** (#1023 ×2, #1024 ×2), deleted in the same change so `NoWaiverIsStale` stays green. The census note was updated |
| `tests/e2e/specs/outlook-addins/share-flow.spec.ts` | **Deleted.** It tested a share UI the pane does not have, against the deleted routes (live-host-manual since task 074; ran nowhere) |
| `tests/e2e/pages/addins/OutlookTaskPanePage.ts` (+`index.ts`) | The three route mocks (`mockSearchApi`, `mockShareLinksApi`, `mockShareAttachApi`) and their three response types/exports |
| `tests/e2e/specs/outlook-addins/save-flow.spec.ts`, `tests/e2e/specs/word-addins/save-flow.spec.ts`, `tests/e2e/pages/addins/WordTaskPanePage.ts` | Both `mockRecentApi` helpers and their **18 call lines**. These were **inert**: they intercepted a route the pane never requests |
| `tests/load/office-endpoints.k6.js` | The four test functions, their metrics, thresholds and summary fields. The mixed-load test's 30% recent/share slice was folded into save (now 40% search, 60% save) |
| `OfficeEntitySearchMappingTests.cs` | Named in the criterion; **no change needed**. It references none of the deleted members (grep) |

**Client**: `src/client/office-addins/shared/taskpane/hooks/useShareFlow.ts` deleted.

**Deliberately NOT removed (for task 075):** the share-only UI helpers left in `OutlookTaskPanePage`
(`navigateToShareMode`, `searchDocuments`, `selectDocument`, `copyLink`, `shareAsAttachment`, …). Only the deleted
share spec used them. They reference no route, so the criteria do not require them; they are now dead code, which is
075's subject.

## 5. Verification

- **Zero references** to the four routes in tracked `src/` and `tests/` files:
  `git ls-files src tests | grep -v node_modules | xargs grep -nE "office/share|office/recent|/share/links|/share/attach|search/documents"` → none.
- **Builds:** the BFF, `Sprk.Bff.Api.Tests` and `Spaarke.ArchTests` all build clean.
- **office-addins:** `tsc` **68**, equal to the pinned baseline, all test/mock files (0 production); lint 0.
- **e2e:** the edited files parse (TypeScript `transpileModule`, 0 diagnostics). k6 passes `node --check`.
- Full suite, ArchTests and publish size: see §6.

## 6. Gates

| Gate | Result |
|---|---|
| Publish size (§10), fresh master vs branch, `Compress-Archive` Optimal incl. PDBs, short-path worktrees | master `b8fc4dc3e` **45.49 MB** → branch `8d56d47eb` **45.46 MB** = **−0.028 MB** (−29,093 bytes). **212 files on both sides**, so the measurement is valid. A reduction, as expected |
| CVE | No package or csproj changes. `dotnet list package --vulnerable --include-transitive`: no vulnerable packages |
| Code review | **0 critical, 0 warnings.** No orphaned fields: every remaining `OfficeService` field is still used, and the constructor and DI are unchanged. The `AssociationType` move is byte-identical, and the repointed 401 test keeps its coverage. **1 suggestion, deferred to task 075:** the share-only UI helpers left in `OutlookTaskPanePage` are dead |
| ADR check | **0 violations.** ADR-001/008/010: removals only. ADR-038: the deleted tests are contract tests for deleted routes (the task's constraint: "their same-PR replacement is the route's removal"); no deletion falls under the replacement-required paths (auth, regression, data-mutation, tenant), and the one auth-flavoured contract test was **repointed, not deleted** |
| Docs | Three docs listed the routes as live and were fixed: `office-outlook-teams-integration-architecture.md`, `sdap-overview.md` and `office-addins-admin-guide.md` (endpoint list, box diagram, rate-limit table and the health-check `curl`) |
| Full BFF suite / ArchTests | see §7 |

## 7. Suite reconciliation

| | Before (080's run) | After 058 | Delta | Expected from this task |
|---|---|---|---|---|
| `Sprk.Bff.Api.Tests` passed | 13,047 (+1 timing flake that fails on load, so 13,048 effective) | **13,040** | **−8** | −8: share-links ×2, share-attach ×2, recent ×2 (contract), plus 2 `RateLimitFilterTests` theory rows |
| skipped | 56 | **54** | **−2** | −2: the two document-search contract tests, which were already `Skip` |
| failed | 1 (`SseStreamingIntegrationTests` timing flake) | **0** | | |
| `Spaarke.ArchTests` | — | **337/337** | | The four waiver deletions keep `NoWaiverIsStale` green. The criterion's "191/191" is stale; the suite has grown |

The delta matches the removals exactly. The repointed 401 test is counted in both runs. (Duration 12 m 11 s.)

Coordination with task 074: `share-flow.spec.ts` (29 tests) is marked DELETED in `notes/074-e2e.md` and
`notes/parity-checklist.md` §10. The live-host manual pass is now 80 tests.
