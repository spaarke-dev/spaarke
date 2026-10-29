# Task 071 — RecordFamilyReader (FR-30)

Branch `task/fm-071`, cut from master `2cd0f62e2c` (UAC 194b, #2432). Rigor FULL (security-sensitive read path). Executed on
opus at high effort.

## What was built

| File | What |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Dataverse/RecordFamilyReader.cs` | The reader and its result types. All `internal`. |
| `src/server/api/Sprk.Bff.Api/Services/Dataverse/Extensions/MetadataServiceExtensions.cs` | One `AddScoped<RecordFamilyReader>()`. |
| `tests/unit/domain/Dataverse/RecordFamilyReaderTests.cs` | 27 tests over an in-memory caller-scoped seam (fix round 1: 15 → 27). |
| `src/server/shared/Spaarke.Dataverse/DataverseForbiddenException.cs` | Fix round 1: a 403 from the multi-row read, with Dataverse's error code and message. |
| `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs` | Fix round 1: `RetrieveMultipleRowsAsync` throws it on a 403 (every other status as before). |
| `tests/integration/auth/UnifiedAccessControl/DataverseWebApiServiceImpersonationTests.cs` | Fix round 1: 2 tests of that 403. |
| `tests/Spaarke.ArchTests/AccessTableReaderScanTests.cs` | Fix round 1: `sprk_policyversion` listed in `NotAccessTables`. |

Surface: `ReadAsync(caller, kind, table, id, direction, continuation, ct)` → `RecordFamilyRead(Outcome, Anchor, Top,
Members, CountsByTable, Truncated, Continuation)`. A member is `(Table, Id, Name, RecordNumber, Status, ModifiedOn, Kind,
EdgeKind, Parent, Depth)`. No lookup names, no registry rows, no access metadata (§12.12).

## Decisions

1. **The caller-scoped seam is the existing `IImpersonatedCommunicationQuery`** (MSCRMCallerID impersonation of the
   caller's systemuserid; the /push pattern). It is reused, not a new seam (§11). The reader holds no app-only client;
   every member query, every display-column read, the up-walk's parent reads and the record-type catalog read go through
   it. It reads one page (no `@odata.nextLink`), so every query carries `$top` ≤ 5000.
2. **The caller is a parameter** (`RecordFamilyCaller(ObjectId, SystemUserId)`). The endpoint (072) resolves the
   systemuserid from the caller's token the way /push does. The oid binds the continuation; the systemuserid is
   impersonated.
3. **Primary parent: UAC 194b's order, through its own primitives, per the UAC-r2 rulings.**
   - (b) A project: its regarding pair (id + `sprk_recordtype_ref` type, the catalog read as the caller once per read).
     A work assignment: `SecureRootInheritance.TypedLookupsOf` order (matter, project, invoice, event).
   - (a) A Signal or Decision Record: its core record first (`sprk_corerecordtype` + `sprk_corerecordid`, both halves
     and a real id, as `ForExistingRowAsync` requires), mapped to the Filing edge on the row that holds that id — the
     Stamp edge first, else the first by `RecordOwnershipContext.InPrimaryOrder`. Then its subject
     (`RecordOwnershipContext.SubjectColumnOf`). Then (c).
   - (c) Every row (fix round 1): the Filing columns it holds, ranked by `RecordOwnershipContext.InPrimaryOrder(table, …,
     subject)`; the first wins. The columns are those whose parent `OrderedParentsOf` keeps (an ownership parent, not a
     citation, not Secure-only) plus a message's thread column, kept although the thread is not an ownership parent. The
     thread column ranks at its place in `SecureChildLineage`'s `LookupOrder`, as in `RecordThreadFiling.QuestionWithThreadFiling`
     (after the named regarding lookups, before an unnamed one such as `sprk_regardingservicerequest`), and counts only when
     the thread files something (`RecordThreadFiling.ParentsOf` non-empty, from the thread row read as the caller). A Direct
     thread (`sprk_threadtype` = `DirectThreadType`), or a thread regarding nothing, files nothing, so the next column decides,
     as in 194b. The separate (d) fallback step is gone. A thread the caller cannot read counts as one that files (194b reads an
     unknown type as a record thread): its messages are reached through it or not at all.
   - **Not called: `RecordOwnershipContext.ForExistingRowAsync`.** Its `ExistingRowReads` are app-only
     (`IGenericEntityService`, `CoreAncestorResolver`), and with `CoreAncestors` null a cored row with a subject loses
     core-first. So ruling (a)'s own instruction ("the core comes first; map the core to its stamp row") is applied over
     194b's primitives. No ordering of columns is defined here: every column order comes from `OrderedParentsOf`,
     `InPrimaryOrder` or `TypedLookupsOf`. Flagged to the operator (report: "shape does not fit").
4. **Walk order.** Up (Full only) to the first ancestor the caller cannot read, a detail row, a Direct thread, a loop, or
   4 hops; then the page sequence is the anchor's chain (top first), the anchor's own subtree (BFS), then the rest (BFS
   from the top, walking through the chain without re-listing it and skipping the anchor). Each level: Filing edges in
   registry order, parent ids sorted and chunked by 50, rows `$orderby={table}id asc`. Deterministic for an unchanged
   family.
5. **Depth** is the distance from the top (top = 0), never negative (FR-31). The result names it `Top`, not `root`: it is
   the highest ancestor the caller can read within the hop cap, and nothing says whether a record exists above it. 072
   maps it to the wire's `root`.
6. **Dedupe** by (table, id) through the one visited set. Proof that a second, deeper path needs a cycle: `PrimaryOfAsync` is a
   function of the row, so each record has at most one primary edge, and the walk follows primary edges only (down: a child
   is accepted only under its primary parent; up: only the primary). The primary edges form a functional graph, in which a
   record has one path from the top unless a cycle of primary edges passes through it. Only bad data makes such a cycle (the
   anchor chain is the primary path itself). So "the same record at depth 1 and at depth 2" cannot be built from valid rows. The
   strongest reachable case is tested instead: two documents that are each other's parent document. The top is reached again
   two levels down, and it is kept once at depth 0 (BFS, first emission wins).
7. **Caps.** Depth 4; 500 members per page. Each request of a batched read (one (edge, 50-id chunk)) asks for the rows still
   needed + 1, at most `MaxRowsPerQuery` = 5000 (one Dataverse page). A full answer is followed by the next request, keyed by
   id: rows are ordered `{table}id asc`, and the next request adds `and {table}id gt {last id}`, until a request returns fewer
   rows than its `$top` (fix round 1). No row is out of reach, so there is no row ceiling and no "ceiling hit". `Truncated` is
   true when a further page exists, or when a readable record lies past the depth cap (below the view, or a readable ancestor
   above the capped top). The depth cap ends that branch only.
8. **Continuation.** ASP.NET Data Protection (`IDataProtectionProvider`, purpose
   `…RecordFamilyReader.Continuation.v1`), Base64Url. It carries version, oid, anchor table + id, kind, direction, top
   table + id and the offset. A resumed read checks the binding, re-walks AS THE CALLER and is refused unless the walk
   derives the same top, so a carried top that became unreadable is never traversed and the token cannot steer the
   walk. Malformed, tampered, re-bound and stale-top tokens are one answer: `ContinuationInvalid`, no records.
9. **Display columns.** Name: `RegardingRecordType.GetPrimaryNameField` (the one live-verified map), else a private
   residual map for the 23 family tables it does not list, from the solution source `Entities/*/Entity.xml`
   (`GetPrimaryNameField` is not widened: `RegardingNameFieldsTests` pins its key set to the addressable regarding
   targets). Record number: `RecordFamilyEdges.RecordNumberColumnOf`. Status: the `statuscode` formatted value.
   Modified on: `modifiedon`. All read as the caller, so a field-secured value comes back null.
10. **Anchor not found** (no such row, not readable, or not a family table) is one outcome with no records. The reader does
    not run the equalised RPA probe; the endpoint (072) owns the uniform 404.
11. **The pair in the child direction.** The pair id is text: the batched query matches `contains(sprk_regardingrecordid,
    '<first 8 hex digits>')` per parent, then each row's pair is parsed exactly and its type checked through the catalog.
    False positives are caller-readable rows that are dropped.

## Placement Justification (CLAUDE.md §10)

- **Where.** `Sprk.Bff.Api/Services/Dataverse/`, beside `RecordFamilyEdges` (its edge registry) and
  `RecordOwnershipResolver` (194b's order). The BFF is the single backend; both consumers (072 endpoint, 040 push preview)
  are in it, and the caller-scoped Dataverse seam is already registered here.
- **Not `Spaarke.Dataverse` / `Spaarke.Core`:** it depends on BFF-internal types (`IImpersonatedCommunicationQuery`,
  `RecordFamilyEdges`, `RecordOwnershipContext`, `SecureRootInheritance`, `RecordThreadFiling`).
- **Not AI:** no `PublicContracts` facade applies. **No background work** (ADR-052 does not apply).
- **DI:** one scoped registration in `AddDataverseMetadataServices` (called unconditionally from `Program.cs`, already
  the home of the Dataverse write-path services). Scoped because one read's state lives in the call. Dependencies are
  unconditional (the seam in `CommunicationModule`; Data Protection from `AddAuthentication`), so no flag (ADR-032).
- **No package added.** Publish-size delta and CVE: see Verification.

## Component justification (CLAUDE.md §11, from the POML `<justification>`)

- **Existing:** UAC's access walks (SecureRootInheritance, SecureChildReconciler) are app-only and enforce invariants;
  /push's child query is private, one level and profile-bound; `ParentLineage` / `ParentLineageWalk` and
  `CoreAncestorResolver`'s upward reads walk lineage app-only (B8).
- **Extension:** No: the access walks must stay app-only for isolation (UAC), and the push query cannot walk depth or apply
  pruning without becoming this reader.
- **Cost of doing nothing:** The Family modal (073) and push preview (040) have no caller-safe way to list a family; each
  would build its own walk.

**`DataverseForbiddenException` (fix round 1).** Existing: the multi-row read threw a bare `HttpRequestException` from
`EnsureSuccessStatusCode` and dropped the body; `DataverseWebApiService.TryReadErrorAsync` reads the body for the
principal-access calls only. Extension: it extends `HttpRequestException` and keeps its status 403 and its message, so every
existing caller reads it as before. The Dataverse text, which names the principal, is kept out of `Message`. Cost of doing
nothing: a caller without Read on one child table cannot open any family. Seen on dev: Test User 1 and `sprk_policies` /
`sprk_policyversions`.

## Tests (27, `tests/unit/domain/Dataverse/RecordFamilyReaderTests.cs`)

KEEP path `tests/unit/domain/**` (ADR-038 §2). The fake seam answers only what the caller may read, with isolation decided
by `SecureRecordOwnerTeam.IsInSecureBusinessUnit` over the row's business unit (never a team id). It parses the OData the
reader sends and throws on any filter term it does not understand. Fix round 1: it knows every family table (the Filing
edges' tables and the cores, by entity set, plus the record-type catalog) and THROWS on any other entity set, rather than
answering empty. It can answer a table with Dataverse's missing-privilege 403, or with any other fault.

| # | Test | Criterion |
|---|---|---|
| 1 | `…ReadableRecordsBelowAnUnreadableSecureRecord_AreAbsentAndTheViewEqualsAFamilyWithoutThatBranch` | Pruning |
| 2 | `…AncestorTheCallerCannotRead_StopsTheUpWalkAndRootsTheViewAtTheHighestReadableAncestor` | Up-walk |
| 3 | `…RootTheCallerCanRead_IsTheTopOfTheFullView` | Up-walk (root returned when readable) |
| 4 | `…RecordFiledUnderAMatterAndAProject_IsInTheMattersFamilyOnly` | Primary parent |
| 5 | `…SignalFiledUnderAMatterAndItsDecisionRecord_IsListedOnceUnderItsPrimary` (renamed in fix round 1: it is found through two edges to the matter, never at depth 2) | Dedupe |
| 6 | `…FamilyOverTheCap_KeepsTheAnchorsChainAndSubtreeAndLoadMoreReturnsTheRestOnce` | Caps |
| 7 | `…ContinuationReplayedUnderAnotherBindingOrTampered_IsRefusedExactlyLikeAnInvalidOne` | Continuation |
| 8 | `…ContinuationWhoseCarriedTopBecameUnreadable_DoesNotTraverseIt` | Continuation |
| 9 | `…KindOtherThanFiling_IsRejectedBeforeAnyDataverseCall` | Kind |
| 10 | `…BadDataLoop_TerminatesWithEachRecordOnce` | Visited set |
| 11 | `…FieldSecuredDisplayColumn_ComesBackNull` | Display columns |
| 12 | `…EveryDataverseRead_IsMadeAsTheCallerThroughTheCallerScopedSeam` | No app-only read |
| 13 | `…FamilyDeeperThanTheDepthCap_SaysItIsTruncatedAndStillListsTheOtherBranches` | Beyond the list |
| 14 | `…SignalWhoseSubjectRanksFirstInLookupOrder_IsListedUnderItsCoreRecord` | Beyond the list |
| 15 | `…MessagesInThreads_RankTheThreadAfterTheirOwnParentsAndADirectThreadFilesNothing` | Beyond the list |
| 16 | `…RecordReachableAgainDeeperThroughPrimaryEdges_IsKeptOnceAtItsShallowestDepth` | Dedupe (fix 4b) |
| 17 | `…ChildTableTheCallerHoldsNoReadPrivilegeOn_ReturnsTheFamilyWithoutThatTable` | Fix 1 |
| 18 | `…AnchorOrAncestorInATableTheCallerHoldsNoReadPrivilegeOn_IsNotReadable` | Fix 1 |
| 19–25 | `…AnyOtherRefusalOrFault_StillThrows` (7 cases: the impersonation privilege, alone and beside the table's name; another table's Read privilege; another error code; no error body; a bare 403; a 500) | Fix 1 must-not-fire |
| 26 | `…EdgeWithMoreRowsThanOneRequestReturns_LoadMoreReturnsEveryMemberExactlyOnce` | Fix 2 |
| 27 | `…PrimaryParent_IsTheOneUac194bsOrderingYieldsForTheSameRows` | Fix 3 parity |

Test 1 (fix 4a) now also holds a readable agreement whose primary is the hidden document and which also carries
`sprk_regardingmatter` to the matter: absent, and the view still equals the family without the branch.

**Parity (test 27)** calls 194b's own pure members on the same values. A Signal with a core and a subject:
`SignalWriter.OwnershipContextOf(...).TargetRecordId`. A message in a record thread with a service request, and one in a
Direct thread with the same service request: `RecordThreadFiling.QuestionWithThreadFiling(row, RecordThreadFiling.ParentsOf(thread)).TargetRecordId`.
A work assignment under an invoice and a matter: the first held column of `SecureRootInheritance.TypedLookupsOf`. A project:
its one filing column is its pair (`SecureChildLineage.RootLookupOrder`), asserted; the pair's TYPE is resolved through the
`sprk_recordtype_ref` catalog, a read, so that step is not compared through a pure member. The reader's answer for a message
listed under a thread is the thread's own parent (the record 194b files it under). Note: 194b's
`RecordOwnershipContext.OrderedParentsOf` has no `LookupOrder` for `sprk_workassignment` (a root) and would order its columns
by name; the work assignment's rule is `TypedLookupsOf` (UAC-r2 ruling (b)), which is what is compared.

One-line justifications for the tests beyond the criteria:

- **13:** POML constraint "never truncate silently" at the depth cap. It also pins two defects found in self-review
  (the cap below the anchor's subtree ended the walk of the sibling branches; the hop cap dropped a readable ancestor
  without saying so), plus the must-not-fire control that an unreadable ancestor past the cap is not hinted at.
- **14:** the binding UAC-r2 ruling (a): a Signal is listed under its core record (the stamp row) even when its subject's
  lookup ranks first in its table's lookup order.
- **15:** the binding UAC-r2 ruling (d): a message ranks its thread after its own parents, and a Direct thread files
  nothing.

Fail-before (task build): all 15 failed against the stub (`Failed: 15, Passed: 0`). Test 13's two later assertions were shown failing
separately: with the hop-cap flag removed, `fromDeepest.Truncated` was false; with the naive form that flags the cap
without reading the ancestor as the caller, `belowHidden.Truncated` was true.

## Known limits

- **K1, the record-type catalog must be readable by the caller.** If `sprk_recordtype_ref` is not readable, a project's
  pair cannot be resolved: the project is not listed under its parent and its up-walk stops at it. Fails closed (shows
  less). (A6, fix round 1: a table the caller holds no Read privilege on, the catalog included, now yields no rows; it no
  longer throws. A row-level refusal was always no rows.)
- **K2, the Data Protection key ring.** The BFF configures no `AddDataProtection` persistence, so it uses the host
  default. A continuation minted on another deployment slot, or before keys were lost, is refused as invalid. Fails
  closed; the user reloads the family.
- **K3, paging re-walks.** Each page re-walks from the top as the caller (no cache, by design), so page k costs the
  queries of pages 1..k. A large matter takes on the order of 100–150 caller-scoped queries per page (design §12.11).
- **K4, offset paging over a live family.** If the family changes between pages, a member can be skipped or repeated
  across pages; the modal (073) de-dupes by (table, id). "No duplicates, no gaps" holds for an unchanged family.
- **K5, the depth cap ends a branch; nothing else does.** Fix round 1 removed the 5000-row ceiling: a batched read is paged
  by id until a short page, so "load more" reaches every member of a large family. Below the depth cap, paging cannot reach:
  `Truncated` = true, and the continuation is null for that reason.
- **K6, a Signal filed only under a Decision Record.** If a Signal has no core, no subject and no other ownership parent,
  it has no primary under 194b, because the Decision Record is not an ownership parent. It is listed under no parent.
  Writers always set a core or matter.
- **K7, one family per record.** A record whose primary parent the caller cannot read is absent from every family view
  the caller opens, even when the caller can read its secondary parent. Opened directly, it is the top of its own view.
  This is UAC F2 (never through an unreadable record) combined with primary-only edges.
- **K8, name columns not live-verified.** The names for the 23 tables beyond the regarding map come from solution source,
  not live metadata. They are display only: a wrong name fails the query as a 400, not as a leak.
- **K10, the depth cap counts from the TOP of the walk (owner decision, 2026-10-29).** The cap of 4 is measured from the top
  (as built), not from the anchor. An anchor at depth 3 or 4 shows little or none of its own subtree, and the continuation
  cannot reach it. This is the owner's decision and an exception to UAC C4's "the anchor's own subtree". Test 13 stays.
- **A4, rows that are not members cost reads.** A readable row a batched read returns but does not list (its primary is
  elsewhere) still counts toward the rows the request asks for, so a page can take more requests to fill. Since fix round
  1 it never ends the walk: the next request is keyed past it.
- **A5, field security moves filing, never leaks.** A field-secured filing lookup comes back null to the caller, so the row
  ranks its next column; a field-secured `sprk_threadtype` reads as "not set", a record thread (194b's reading). Either
  moves where the row or the thread files for that caller; the caller sees only rows Dataverse returned to them.
- **A7, continuations never expire.** A token stays valid while the key ring does. Harmless: a resume re-walks as the
  caller, binds to their oid, and is refused unless the walk derives the same top.
- **B4, core and subject edge cases.** A Signal or Decision Record whose core is frozen or not yet stamped
  (`sprk_corerecordid` held by no lookup on the row) is listed under its subject. A malformed half core pair (one half, or a
  non-id) falls to the subject. A Decision Record with no core and no subject falls to (c).
- **B7, one offset continuation.** The continuation is one offset into the whole walk's order, not one per level. FR-30
  does not require per-level continuation.
- **B9, a thread outside the walk is read on demand.** When a message is reached through a column ranked after its thread
  (an unnamed one, such as `sprk_regardingservicerequest`) and the thread was not read yet, the thread row is read as the
  caller, once per thread per read, to decide whether it files anything.
- **K9, timing.** A malformed or rebound continuation is refused before any read; a stale-top continuation is refused
  after the up-walk. The answer is identical but the timing differs. The difference reveals only that the caller's own
  token was well-formed, never anything about records the caller cannot read.
- **K11 (review K6), no query budget.** Nothing bounds a read's queries beyond the caller's cancellation token: a huge set is
  scanned to its end (in pages of `MaxRowsPerQuery` after the first request), and it always terminates.
- **K12 (review K3), thread reads are not batched.** A thread row is read one at a time, and only when the message's thread
  column decides its primary parent (B9).

## Verification

Run on branch `task/fm-071` after the verification fix below (head `ffd7886c92`, on master `2cd0f62e2c`), 2026-10-29.

| # | Check | Result |
|---|---|---|
| 1 | `dotnet build src/server/api/Sprk.Bff.Api/` | 0 errors, 0 warnings |
| 2 | New tests: `Sprk.Bff.Api.Tests --filter FullyQualifiedName~RecordFamilyReader` (the project that globs `tests/unit/domain/**`) | 15 passed, 0 failed |
| 3 | `RecordFamilyEdges` + `RecordOwnership` filter | 912 passed, 0 failed |
| 3 | The project's filtered runner set (`scripts/verify-suites.sh` filter, plus `RecordFamily`), `Sprk.Bff.Api.Tests` only, never unfiltered | 5825 passed, 7 skipped, 0 failed (5832 total, 17 m 45 s) |
| 3 | DI-registration tests for `AddDataverseMetadataServices` | None exist. Resolution checked by reading: the reader needs `IImpersonatedCommunicationQuery` (singleton, `CommunicationModule`, unconditional) and `IDataProtectionProvider` (`AddAuthentication`); a scoped service over a singleton is not captive |
| 4 | `dotnet build tests/Spaarke.ArchTests/` then `dotnet test` | First run: 1665 total, **1 failed** (see below). After the fix: **1665 passed, 0 failed**, build 0 warnings 0 errors |
| 5 | Publish size, `publish-delta.ps1 -MasterSha 2cd0f62e2c -BranchSha ffd7886c92`, Release publish, zipped with PowerShell `Compress-Archive -CompressionLevel Optimal` | master 39,789,532 B (192 files); branch 39,810,681 B (192 files); delta **+21,149 B (about +20.7 KB)**; branch total 37.97 MB. File counts match. Well under the +5 MB, 55 MB and 60 MB thresholds. The script removed its own helper worktrees |
| 6 | `dotnet list ... package --vulnerable --include-transitive` (run on the branch, and again inside the publish script) | No vulnerable packages: no HIGH, no CRITICAL. No package was added |

### Verification found one defect in the built code (fixed, commit `ffd7886c92`)

ArchTests first failed on `DecisionRecordContextImmutabilityTests` (D-34): `RecordFamilyReader.cs:191` held
`["sprk_policyversion"] = "sprk_name"` in the display-name map. That guard scans source text for
`["sprk_policyversion"] =` and reads it as a write of the Decision Record context column. It is a table-name key, not a
write, and not a leak. A first attempt to hold the name in a constant called `PolicyVersionTable` tripped a second guard
(`AccessTableReaderScanTests`, #2253 K3, which reads any `const string …Table` as a named access table). The shipped fix is a
constant `PolicyVersionKey` (no scanned suffix) used as the map key. No behaviour change; the 15 tests are unchanged and pass.
This failure was caused by the new code (it does not occur on master, which has no such entry), so it is not pre-existing.

### Fix round 1 (two adversarial reviews), 2026-10-29

| Fix | Change | Test: fail before → pass after |
|---|---|---|
| 1 | A missing table Read privilege is no rows (child edges, anchor, up-walk, catalog); `DataverseForbiddenException` carries the 403's code and message | Tests 17, 18 failed against the pre-fix reader (the 403 propagated); the seam test `RetrieveMultipleImpersonatedAsync_Forbidden_ThrowsWithDataversesErrorCodeAndMessage` failed against the pre-fix `DataverseWebApiService`. Must-not-fire tests 19–25 pass both before and after (controls) |
| 2 | Batched reads paged by id; `CeilingHit` removed | Test 26 failed before (`Truncated` true on the last page, no continuation past 5000 rows) |
| 3 | A message's thread ranked at its 194b place | Test 27 failed before (message primary: the service request instead of the thread's matter) |
| 4a | Test 1 holds a row whose primary is the hidden document | With a mutant that lists a child under the queried edge's parent instead of its primary, the OLD test 1 passed and the new one fails |
| 4b | Test 16 (loop: kept once at the shallowest depth) | With a mutant `Emit` that does not dedupe, test 16 fails |
| 5 | `PolicyVersionKey` → `PolicyVersionTable`; `sprk_policyversion` in `NotAccessTables` | Without the entry, `AccessTableReaderScanTests` failed naming `RecordFamilyReader.cs:172`; with it, it passes. D-34 (`DecisionRecordContextImmutabilityTests`) passes |

Verification after fix round 1:

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 errors, 0 warnings |
| `--filter FullyQualifiedName~RecordFamilyReader` | 27 passed, 0 failed |
| `--filter FullyQualifiedName~RecordFamilyEdges\|FullyQualifiedName~RecordOwnership` | 912 passed, 0 failed |
| `--filter FullyQualifiedName~DataverseWebApiService\|FullyQualifiedName~Impersonat` | 120 passed, 0 failed |
| `tests/Spaarke.ArchTests`, `--no-incremental` build then full run | 1665 passed, 0 failed |
| Unfiltered `Sprk.Bff.Api.Tests` | Not run (per the brief) |
| Publish-size delta | Not re-measured in this round: one small source file and no package were added |

### Fix round 2 (review findings F2, F3, K1, K2, K4), 2026-10-29

Live check 2026-10-29 (main session, dev): `sprk_documents` `$orderby=sprk_documentid asc`, then `sprk_documentid gt <10th id>`, returned exactly rows 11–60 in the same order, so the key paging's `gt` agrees with `$orderby`.

| Fix | Change | Test: fail before → pass after |
|---|---|---|
| F2 | After its first request an edge is paged at `MaxRowsPerQuery`, not `needed()+1` (`RecordFamilyReader.ForEachChildAsync`) | `ReadAsync_EdgeWithManyRowsFiledUnderAnotherParent_CostsAFewRequestsNotOnePerSmallPage`: 5000 secondary-filed agreements. With the old size: 10 requests (fails the bound of 3); now 2 |
| F3 | The vacuous `sprk_policies == 2` assertion is removed from the privilege test; a new test uses 51 documents with a file version each (two 50-id chunks in one read) and refuses `sprk_fileversion` | `ReadAsync_RefusedTableQueriedInSeveralChunksOfOneRead_IsAskedOnceNotPerChunk` with the `_noReadPrivilege.Add` removed: asks twice, fails (expected 1). With the cache: 1. Control in the same test: unrefused, 2 calls |
| K1 | A 403 whose body cannot be read or decoded still throws `DataverseForbiddenException` (status 403, no code or message). The impersonated GET now sends `HttpCompletionOption.ResponseHeadersRead` and disposes the response, because the default buffered the body inside `SendAsync`, where a stream fault or `InvalidOperationException` replaced the 403 before `TryReadErrorAsync` ran. `TryReadErrorAsync` catches the same set as `DataverseWebApiClient.CreateFailureAsync` (`JsonException`, `HttpRequestException`, `IOException`, `InvalidOperationException`, `NotSupportedException`). Its other caller (`ReadPrincipalAccessOrUnknownAsync`) treats `(null, null)` as UNKNOWN and logs it, so the widening is safe there | `RetrieveMultipleImpersonatedAsync_ForbiddenWithAnUnreadableBody_StillThrowsTheForbidden` (3 cases: IO, invalid-operation, http-request): all 3 failed with the old code (each fault escaped as itself), all pass now. A real unsupported-charset header does not fault on .NET 10 (it falls back), so a faulting body stands in for it |
| K2 | `sprk_policyversion` reason in `AccessTableReaderScanTests.NotAccessTables` reworded | Text only; the scan passes |
| K4 | A refused read of `sprk_recordtype_ref` logs at Warning; every other table stays Information (ids and table names only) | No new test (per the brief) |

Verification after fix round 2:

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 errors, 0 warnings |
| `Sprk.Bff.Api.Tests --filter "FullyQualifiedName~RecordFamilyReader\|FullyQualifiedName~DataverseWebApiServiceImpersonation\|FullyQualifiedName~DataverseWebApiService"` | 49 passed, 0 failed |
| `tests/Spaarke.ArchTests`, `--no-incremental` build then full run | 1665 passed, 0 failed |
| Unfiltered `Sprk.Bff.Api.Tests` | Not run (per the brief) |

### Not covered by this verification

- TASK-INDEX.md (POML step 4) is not updated on this branch: it lives on the work branch.
- The adversarial reviews (two, auth-tagged) and adr-check are run by the main session.
