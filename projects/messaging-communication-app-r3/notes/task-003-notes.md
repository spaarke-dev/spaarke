# Task 003 — FR-16 `GET /api/communications/threads` + `ListThreadsAsync`

> **Status**: Implemented + verified 2026-07-20. Rigor FULL · opus · xhigh.
> **Spec**: FR-16 + Success Criterion 5 (list ALL of a user's threads, incl. first-class record-less).
> **For**: Phase 4 Surface 2 (workspace left pane task 012/031; standalone code page task 032).

---

## Endpoint contract

`GET /api/communications/threads`

- **Auth**: group `.RequireAuthorization()` + `.AddEndpointFilter<CommunicationAuthorizationFilter>()` (ADR-028; caller resolved server-side from `HttpContext.User`, never client-supplied).
- **Route disambiguation**: a bare `GET /threads` (literal segment, no route param) is DISTINCT from `GET /threads/{threadId:guid}/messages`, `GET /threads/{threadId:guid}/unread-count`, and `POST /threads/direct`. No collision — verified by a clean app build (routing would throw `AmbiguousMatchException` at startup otherwise) and by inspection.

### Query params (all optional)

| Param | Type | Meaning |
|---|---|---|
| `search` | string | Name filter — `contains(sprk_name, '<value>')`. Two-stage escape: single-quote doubling (OData string-literal breakout defense) THEN `Uri.EscapeDataString` (transport-safe — the seam concatenates the value raw into the URL, so `&`/`#`/`+`/`%`/space would otherwise break/inject the query string). |
| `top` | int | Page size. Normalized: `null`/≤0 → 50 (default); capped at 200 (`MaxThreadListPage`, mirrors `MaxThreads`). |
| `pageToken` | string | Opaque **composite** keyset cursor from a previous page's `nextPageToken`. Malformed/incomplete → 400. |

### Response — `ThreadListResult` (200)

```jsonc
{
  "threads": [
    { "threadId": "<guid>", "name": "Acme v Widgets", "threadType": 100000000, "createdOn": "2026-07-19T12:00:00Z" },
    { "threadId": "<guid>", "name": "Alice ↔ Bob", "threadType": 100000001, "createdOn": "2026-07-18T09:00:00Z" }
  ],
  "count": 2,
  "nextPageToken": "MjAyNi0wNy0xOFQwOTowMDowMC4wMDBa",  // opaque; null when no further page
  "hasMore": true
}
```

- `ThreadListItem` = `{ ThreadId, Name (nullable), ThreadType (nullable int; Record-Anchored=100000000, Direct 1:1=100000001), CreatedOn (nullable) }` — minimal for the left-pane list; opening a thread fetches messages via the existing `GET /threads/{threadId}/messages`.
- Errors: `400` (malformed `pageToken`), `403` (unresolved caller — fail closed, no app-only fallback).

### Paging / search / order semantics

- **Order**: `createdon desc, sprk_communicationthreadid desc` — a deterministic TOTAL order, so pages are stable + non-overlapping + lossless.
- **Paging model**: **COMPOSITE keyset cursor on `(createdon, sprk_communicationthreadid)`**, NOT offset. Rationale: the Dataverse Web API has no `$skip`, and `IImpersonatedCommunicationQuery` returns only rows (it drops the `@odata.nextLink` skiptoken cookie).
  - `nextPageToken` = base64 of `"{createdon}|{threadId:D}"` for the last kept row.
  - Next-page predicate (decoded from the token): `(createdon lt V or (createdon eq V and sprk_communicationthreadid lt Vid))`.
  - Detection: the service over-fetches `pageSize + 1` rows; if it gets more than `pageSize`, `hasMore = true`, it trims to `pageSize`, and mints the cursor from the boundary row.
  - **Why composite (C1 fix, Step-9.5 Critical)**: `createdon` is second-granular in Dataverse — bulk/seed/rapid creation routinely ties. A `createdon`-only strict-`lt` cursor would permanently SKIP tied rows past the page cut (a user silently loses visibility of their own threads → breaks FR-16 "list ALL"). The `sprk_communicationthreadid` tiebreaker in both the `$orderby` and the predicate makes tied-createdon pages lossless. (Regression-guarded by the ≥3-tied-rows straddling-boundary seam test.)

---

## `ListThreadsAsync` approach (NFR-01 correctness)

`ListThreadsAsync(ClaimsPrincipal? caller, string? search, int? top, string? pageToken, CancellationToken)` on `CommunicationThreadReadService`.

- **Query shape**: IMPERSONATED (`MSCRMCallerID` = caller `systemuserid`) via `IImpersonatedCommunicationQuery.QueryAsync("sprk_communicationthreads", …)`. `$select=sprk_communicationthreadid,sprk_name,sprk_threadtype,createdon`, `$orderby=createdon desc,sprk_communicationthreadid desc`, `$top=pageSize+1`, optional `contains(sprk_name,…)` + composite `(createdon lt V or (createdon eq V and sprk_communicationthreadid lt Vid))` cursor AND-composed.
- **Record-less inclusion**: the query is **NOT scoped to any `sprk_regarding{type}` lookup** (unlike `ReadByRegardingAsync`, whose step-1 filters `_{regardingField}_value eq …`). A Direct/record-less thread carries no regarding anchor, so dropping the regarding filter is exactly what lets it appear. Nothing post-filters by regarding.
- **No membership-union**: the impersonated set IS the answer. Dataverse row-level security (ownership, role depth, BU, teams, sharing, hierarchy) is the ONLY visibility gate — a thread the caller can't see is simply absent from the returned rows. The method takes NO dependency on `IThreadPrivateGrantProvider` / `IThreadMembershipDerivationService` / `IThreadExplicitParticipantReader` / `IMembershipResolverService`. The retired-2026-07-16 union is not resurrected. (The message-level `ICommunicationAccessFilter` is not used here — internal-only/privilege are message-row concerns; thread-list visibility is 100% Dataverse's decision, which is why the list == the impersonated thread set exactly.)
- **Fail-closed**: reuses `ResolveCallerOrThrowAsync` — an unresolved caller → `SdapProblemException` 403, and NO impersonated query is issued (no app-only fallback that would widen access).
- **DI**: no change — `CommunicationThreadReadService` is already registered `AddScoped` in `CommunicationModule.cs`; no new dependency introduced.

---

## Seam tests (DoD — `tests/integration/seam/Communication/CommunicationListThreadsSeamTests.cs`, 10 tests, all green)

Boundary mocks only (`IImpersonatedCommunicationQuery`, `ICallerSystemUserResolver`); real `CommunicationThreadReadService` + real `CommunicationAccessFilter`; no `Mock<HttpMessageHandler>`, no DI-registration/ctor-null tests. The paging mock (`EmulateDataverse`) faithfully applies the composite `(createdon, id)` predicate + `createdon desc, id desc` ordering + `$top`, so paging tests are genuine round-trips.

| Test | Proves |
|---|---|
| `…_RecordLessDirectThread_AppearsForCallerWhoCanSeeIt` | (a) a Direct/record-less thread is listed; query carries **no** `_sprk_regarding` scoping. |
| `…_ReturnsExactlyTheImpersonatedThreadSet_NoPostRegardingScoping` | (b) access parity — list == impersonated set 1:1; exactly ONE thread-set query (`VerifyNoOtherCalls`). |
| `…_ThreadCallerCannotSee_IsAbsentAndNoUnionComputed` | (c) NEGATIVE — a thread absent from the impersonated set never surfaces; no second/union query. |
| `…_MoreThanPageSize_DistinctCreatedOn_PagesAreStableAndNonOverlapping` | (d) keyset paging (distinct createdon): page1+page2 stable, ordered, disjoint. |
| `…_ThreadsSharingOneCreatedOn_StraddlingBoundary_NoRowDroppedOrDuplicated` | **(C1)** ≥3 threads sharing ONE createdon straddling the page cut are ALL reached exactly once (composite cursor is lossless — no silent thread loss). |
| `…_SearchWithApostrophe_DoublesQuoteThenPercentEncodesLiteral` | (e) `contains(sprk_name,'O%27%27Brien')` — quote doubled THEN percent-encoded. |
| `…_SearchWithSpaceAndAmpersand_IsPercentEncoded_NoQueryInjection` | **(W1)** `north & south` → `'north%20%26%20south'`; no user `&` splits the query; single real `$top=6` (no injected `$top`). |
| `…_UnresolvedCaller_ThrowsForbiddenAndIssuesNoImpersonatedQuery` | fail-closed 403 + zero impersonated queries. |
| `…_MalformedPageToken_Returns400NotAFullDump` | graceful 400 on a bad cursor (never an unfiltered dump). |
| `NoMembershipUnionRegression_ListThreadsAsync_TakesNoMembershipOrGrantSeamParameter` | (f) structural no-union guard on the NEW method signature + ctor still exactly 4 collaborators. |

(The pre-existing `CommunicationWorkspaceReadSeamTests.NoMembershipUnionRegression_…` ctor guard also still covers the class as a whole — unchanged, still 4-param.)

---

## Verification results

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | **0 errors** (19 pre-existing warnings). |
| `dotnet test …--filter Services.Communication\|Seam.Communication` | **586 passed, 0 failed, 5 pre-existing skips**; new list-threads class **10/10 pass** (incl. C1 tie + W1 encoding). |
| BFF publish (Release, compressed zip incl PDBs) | **47.08 MB** (PDBs ≈2 MB). Ceiling 60 MB; baseline ~46 MB. **Delta ≈0** (no new package). |
| `dotnet list package --vulnerable --include-transitive` | **0 NEW HIGH**. Only pre-existing HIGH `System.Security.Cryptography.Xml 8.0.3` (GHSA-cvvh-rhrc-wg4q et al) — not introduced by this task. |
| `git diff --name-only` | 3 source (`CommunicationEndpoints.cs`, `CommunicationThreadReadModels.cs`, `CommunicationThreadReadService.cs`) + 1 seam test (new) + notes + `current-task.md`. |

---

## Placement Justification (cite `.claude/constraints/bff-extensions.md`)

- **Placement decision**: the new endpoint + `ListThreadsAsync` method live inside `Services/Communication/` behind the ADR-045 communication boundary (the same home as the R1/R2 read surfaces they mirror). Correct placement per `bff-extensions.md` decision criteria: it is a read over `sprk_communicationthread` on the access-scoped impersonation path — server-only, latency-coupled to the left-pane load, not a candidate for extraction.
- **Component Justification (root §11)**: (1) *Existing* — closest neighbor `ReadByRegardingAsync` is structurally record-scoped (typed `sprk_regarding{type}` lookup) so it cannot return record-less threads. (2) *Extension* — reuses the impersonation seam (`IImpersonatedCommunicationQuery`) + the fail-closed caller resolver + the same DTO/parse/OData helpers; only the regarding filter is dropped and search/paging added. No new service/interface/package/DI registration. (3) *Cost-of-doing-nothing* — the FR-16 workspace left pane + FR-14 standalone page have nothing to call; a user's Direct conversations are invisible to every existing (record-anchored) read.
- **No new**: package, AI dependency, background worker, DI registration, or membership seam. Publish delta ≈0. No new HIGH CVE.
- **ADR compliance**: ADR-045 (in-boundary), ADR-028 (auth filter + server-resolved caller, no app-only fallback), ADR-024 (no second regarding mechanism — it simply omits regarding scoping), ADR-038 (seam tests as DoD), ADR-019 (400 ProblemDetails on malformed input). Access model = impersonation + Dataverse RLS only, per `messaging-communication-app-r1/notes/access-model-decision.md` (retired union stays retired).
- **Coordination**: run `/conflict-check` before the PR — shared `Services/Communication/**` (r1/r2/email-r4).
