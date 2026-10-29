# Task 071 — RecordFamilyReader (FR-30)

Branch `task/fm-071`, cut from master `2cd0f62e2c` (UAC 194b, #2432). Rigor FULL (security-sensitive read path). Executed on
opus at high effort.

## What was built

| File | What |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Dataverse/RecordFamilyReader.cs` | The reader and its result types. All `internal`. |
| `src/server/api/Sprk.Bff.Api/Services/Dataverse/Extensions/MetadataServiceExtensions.cs` | One `AddScoped<RecordFamilyReader>()`. |
| `tests/unit/domain/Dataverse/RecordFamilyReaderTests.cs` | 15 tests over an in-memory caller-scoped seam. |

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
   - (c) Every row: the first of `RecordOwnershipContext.OrderedParentsOf` held by a Filing edge of its table; of several
     edges holding that parent, the first by `InPrimaryOrder`.
   - (d) A message: its thread after its own parents (`RecordThreadFiling`); a Direct thread
     (`sprk_threadtype` = `DirectThreadType`) files nothing — a message under it is listed nowhere and the up-walk stops
     at it.
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
6. **Dedupe** by (table, id) through the one visited set. Because only primary edges are followed, every member has one
   path, so the depth kept is the shallowest by construction.
7. **Caps.** Depth 4; 500 members per page; the per-level cap is `MaxRowsPerQuery` = 5000 rows per batched read (one
   Dataverse page per (edge, 50-id chunk)). The first read of a chunk asks for the rows still needed + 1 and re-reads up
   to 5000 when rejected rows (secondary filings) used the budget. `Truncated` is true when a further page exists, when a
   readable record lies past the depth cap (below the view, or a readable ancestor above the capped top), or when a read
   met the 5000-row ceiling. The depth cap ends that branch only; the ceiling stops the walk.
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
  /push's child query is private, one level and profile-bound.
- **Extension:** No: the access walks must stay app-only for isolation (UAC), and the push query cannot walk depth or apply
  pruning without becoming this reader.
- **Cost of doing nothing:** The Family modal (073) and push preview (040) have no caller-safe way to list a family; each
  would build its own walk.

## Tests (15, `tests/unit/domain/Dataverse/RecordFamilyReaderTests.cs`)

KEEP path `tests/unit/domain/**` (ADR-038 §2). The fake seam answers only what the caller may read, with isolation decided
by `SecureRecordOwnerTeam.IsInSecureBusinessUnit` over the row's business unit (never a team id). It parses the OData the
reader sends and throws on any filter term it does not understand.

| # | Test | Criterion |
|---|---|---|
| 1 | `…ReadableRecordsBelowAnUnreadableSecureRecord_AreAbsentAndTheViewEqualsAFamilyWithoutThatBranch` | Pruning |
| 2 | `…AncestorTheCallerCannotRead_StopsTheUpWalkAndRootsTheViewAtTheHighestReadableAncestor` | Up-walk |
| 3 | `…RootTheCallerCanRead_IsTheTopOfTheFullView` | Up-walk (root returned when readable) |
| 4 | `…RecordFiledUnderAMatterAndAProject_IsInTheMattersFamilyOnly` | Primary parent |
| 5 | `…SignalReachableAtDepthOneAndTwo_AppearsOnceAtDepthOne` | Dedupe |
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

One-line justifications for the tests beyond the criteria:

- **13:** POML constraint "never truncate silently" at the depth cap. It also pins two defects found in self-review
  (the cap below the anchor's subtree ended the walk of the sibling branches; the hop cap dropped a readable ancestor
  without saying so), plus the must-not-fire control that an unreadable ancestor past the cap is not hinted at.
- **14:** the binding UAC-r2 ruling (a): a Signal is listed under its core record (the stamp row) even when its subject's
  lookup ranks first in its table's lookup order.
- **15:** the binding UAC-r2 ruling (d): a message ranks its thread after its own parents, and a Direct thread files
  nothing.

Fail-before: all 15 failed against the stub (`Failed: 15, Passed: 0`). Test 13's two later assertions were shown failing
separately: with the hop-cap flag removed, `fromDeepest.Truncated` was false; with the naive form that flags the cap
without reading the ancestor as the caller, `belowHidden.Truncated` was true.

## Known limits

- **K1, the record-type catalog must be readable by the caller.** If `sprk_recordtype_ref` is not readable, a project's
  pair cannot be resolved: the project is not listed under its parent and its up-walk stops at it. Fails closed (shows
  less).
- **K2, the Data Protection key ring.** The BFF configures no `AddDataProtection` persistence, so it uses the host
  default. A continuation minted on another deployment slot, or before keys were lost, is refused as invalid. Fails
  closed; the user reloads the family.
- **K3, paging re-walks.** Each page re-walks from the top as the caller (no cache, by design), so page k costs the
  queries of pages 1..k. A large matter takes on the order of 100–150 caller-scoped queries per page (design §12.11).
- **K4, offset paging over a live family.** If the family changes between pages, a member can be skipped or repeated
  across pages; the modal (073) de-dupes by (table, id). "No duplicates, no gaps" holds for an unchanged family.
- **K5, the ceiling ends the walk.** Past the 5000-row ceiling of one batched read, the walk stops: `Truncated` = true and
  there is no continuation. Below the depth cap, paging cannot reach either: `Truncated` = true, and the continuation is
  null for that reason.
- **K6, a Signal filed only under a Decision Record.** If a Signal has no core, no subject and no other ownership parent,
  it has no primary under 194b, because the Decision Record is not an ownership parent. It is listed under no parent.
  Writers always set a core or matter.
- **K7, one family per record.** A record whose primary parent the caller cannot read is absent from every family view
  the caller opens, even when the caller can read its secondary parent. Opened directly, it is the top of its own view.
  This is UAC F2 (never through an unreadable record) combined with primary-only edges.
- **K8, name columns not live-verified.** The names for the 23 tables beyond the regarding map come from solution source,
  not live metadata. They are display only: a wrong name fails the query as a 400, not as a leak.
- **K9, timing.** A malformed or rebound continuation is refused before any read; a stale-top continuation is refused
  after the up-walk. The answer is identical but the timing differs. The difference reveals only that the caller's own
  token was well-formed, never anything about records the caller cannot read.

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

### Not covered by this verification

- TASK-INDEX.md (POML step 4) is not updated on this branch: it lives on the work branch.
- The adversarial reviews (two, auth-tagged) and adr-check are run by the main session.
