# Task 187: external child-row reads check live parents (D-123, #1857)

> Branch `uac/187-external-live-parent` from master `6da771b96`. Owner round 99, "Live-parent check (precise)". This note
> holds the surface table, the §11 answers, the query cost, the mutation evidence, the publish size, and the open questions.

## The rule as built

An external child-row read selects the row's parent lookups too (`ExternalMultiRootRule.LiveParentSelect`: the row
table's lineage lookups in `ParentLineage.ChildFiling`, minus the root lookups). It resolves their LIVE roots in one
bounded app-only batch per page (`ExternalMultiRootRule.ReadLiveParentsAsync` → `ExternalDataService.GetLiveParentRootsAsync`
→ `ParentLineageWalk.TopsOfManyAsync`). It then decides with `ExternalMultiRootRule.ChildRowReadable` over
`ChildRowRoots.Of(stamps, parents, live)`, which is {stamped roots} ∪ {live roots of every parent}. The existing secure-flag read
(`GetNonSecureRootsAsync`, effective flags) runs once over that union.

- **No over-hiding.** A parent whose top is an ordinary root, or a parentless record, adds nothing secure. A row naming
  no parent is decided on its stamps alone, as before. The tests that list rows pass on master as well (12 of 38).
- **Fail closed.** These all withhold the row: a parent value that is not a GUID; a parent the walk did not resolve
  (missing, a read that throws, deeper than `MaxDepth` = 6, past `MaxLiveParentReads` = 500); and no filing reader. A list
  reports `Truncated` when a fault or an incomplete read withheld rows. A write (to-do PATCH, to-do/event delete-own)
  returns 403 when the row is withheld and 503 on delete-own when a read failed. The PATCH stays 403 on a failed read,
  because its route has no 503 contract.
- **Logs.** Counts only (`[EXT-LIVEPARENT] …`). No ids, names or values.

## Surface table

Line numbers are at the branch head.

| Surface | Where | Status |
|---|---|---|
| To-do list (project, matter and work assignment routes) | `ExternalDataService.cs:1582` `GetTodosAsync` (live read :1598); select `BuildTodoListUrl` :1632 | **In scope, fixed** (AC1, AC2, AC3, AC7) |
| Events list (project, matter and work assignment routes) | `ExternalDataService.cs:1675` `GetEventsAsync` → `ReadableEventsAsync` :1711; select `BuildEventsQuery` | **In scope, fixed** |
| Document list (project, matter, work assignment, service request and invoice routes) | `ExternalDataService.cs:444` `GetDocumentsAsync` (live read :454); select `BuildDocumentListUrl` :626 | **In scope, fixed.** Related event, communication, to-do, invoice, agreement, parent and canonical document |
| Document download and versions (every parent route) | `ExternalProjectDataEndpoints.cs:1014` `IsDocumentReadableUnderAsync` (callers :624, :1450); row read `ExternalDataService.cs:895` | **In scope, fixed.** Same refusal as "not under the parent"; no SPE pointer read |
| Documents module `/fetch` (the grid) | `ExternalModuleDataEndpoints.cs:300` (live read) and :309 `ScopeRows`; `ExternalModuleRegistry.cs` `LiveParentAttributes` / `LiveParents` / `RuleInput` | **In scope, fixed** (AC6) |
| Messages (project, matter, work assignment and service-request threads) | `ExternalMessageEndpoints.cs:410` `SecureRootsFilter`; row select `CommunicationThreadReadService.cs:516`, `RootsOf` :470 | **In scope, fixed** (AC5) |
| Notification feed | `ExternalNotificationFeedEndpoints.cs:332` (the same `SecureRootsFilter`, with the live reader) | **In scope, fixed** (AC5) |
| To-do PATCH | `ExternalProjectDataEndpoints.cs:1702` `CheckLiveParentsAsync` after the Write check | **In scope, fixed** (AC4). 403 with no PATCH sent |
| To-do and event delete-own | `ExternalProjectDataEndpoints.cs:1817` (step 3 of `AuthorizeDeleteOwnAsync`) | **In scope, fixed** (AC4). 403 or 503 with no DELETE sent |
| Invoice message thread | `ExternalMessageEndpoints.cs:396` (`RootsReadableFor` exemption) | **Not changed: owner decision** (2026-10-11 option A, "inherit"; doc comment :386). **Open question Q1** |
| Invoices module `/fetch` | `ExternalAccessModule.cs:434` (no `RequireReadOnSecureRoots`) | **Not changed: option A** (any parent). **Open question Q2** |
| Invoice-route documents: the route's own invoice | `ExternalDataService.cs:558` `DocumentRootLinks.ForRoute` | **Option A kept.** The route invoice is not walked again; the document's other links are. **Open question Q3** |
| Document delete-own | `ExternalProjectDataEndpoints.cs:1874` | **Not changed**: outside the POML's AC4, and in tension with option A. **Open question Q4** |
| Message-root record check | `ExternalMessageEndpoints.cs:577` `ScopeRows` on the root's module | **SAFE.** The record is a root (project, matter or work assignment), an invoice (option A) or a service request (requester scope). It is not a stamped child row, and those modules have no multi-root rule |
| Notification feed: service-request rows | `ExternalNotificationFeedEndpoints.cs:313` `ScopeRows` | **SAFE.** Requester-scoped records (`sprk_requestedby`); the module has no multi-root rule |
| `/data/record/{entity}/{id}` | `ExternalModuleDataEndpoints.cs:371` | **SAFE.** `IsRecordAccessible` admits only an id that is itself an accessible root id, so a document id never passes |
| Contacts, organizations, `/me`, entitlements, assigned access | `ExternalProjectDataEndpoints.cs:425`, :433, and others | **SAFE.** Not child rows of a root |
| Create to-do, event or document; upload; post a message; mark seen | create and post routes | **SAFE.** They read no existing child row (they create under a root the caller already holds) |
| Shared collection-page reader and `CollectionPrefer` | `ExternalDataService.cs:3147` `GetCollectionPageAsync` | **Not touched** (SPA-r3 task 064 owns them). `git diff 6da771b96` shows no line in either |
| `WorkforcePrincipalResolver.ResolveAsync` | | **Not touched** |

## §11 answers (new surface)

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `ParentLineageWalk.TopsOfManyAsync` (and the private `TopsFromRead`) | `ParentLineageWalk.TopsAsync` climbs ONE start | Extended in the same class. `TopsAsync` per row would cost one query per row per level, which breaks the "one batched read per parent table" contract | A page of N rows would cost N×levels queries, or the read would stay stamp-only (#1857 leak) |
| `LiveParentLookup`, `LiveParentRootReader`, `LiveParentRootsRead`, `LiveParentRoots` (in `ExternalMultiRootRule.cs`) | `NonSecureRootReader` / `NonSecureRootsRead` (flag read) | Mirrors that delegate-and-answer shape beside it. The flag read answers "is root R secure", not "which roots is parent P under" | Without them the rule has no input for a parent's live roots |
| `ChildRowRoots` + `ExternalMultiRootRule.ChildRowReadable` | `SecureRootsReadable(links)` | `ChildRowReadable` delegates to it. The type's only constructor is `Of(stamps, parents, live)`, so the union cannot be skipped by accident; the guard checks it | A child-row read could keep passing stamps only, and the guard would have nothing to enforce |
| `ExternalMultiRootRule.LiveParentLookupsOf` / `LiveParentSelect` / `LiveParentsOf` / `ReadLiveParentsAsync` | `Stamps`, `ReadFlagsAsync` | Added to the same static rule class | Each surface would re-derive the parent columns and drift from the one lineage map |
| `ExternalDataService.GetLiveParentRootsAsync`, `ReadLiveParentRootsAsync` (virtual seam) | `GetNonSecureRootsAsync` (same class, same app-only pattern) | Added beside it. The walk needs the existing `IGenericEntityService` filing reader | Endpoints and the message filter would have no reader to pass |
| `ExternalDataService.CheckLiveParentsAsync`, `LiveParentDecision`, `BuildLiveParentCheckUrl` | `GetOwnershipAsync` (delete-own) / the PATCH Write check | A single-row read that delete-own and PATCH share. Neither route read the row's parents before | The to-do PATCH and the to-do/event delete-own would act on a carrier row the list withholds (AC4) |
| `ExternalDataService` constructor parameter `CoreAncestorResolver? columns` | The resolver's 6-hour cached metadata probe (stamp path) | Reused. It is already registered unconditionally (`AnalysisServicesModule` → `AddCoreAncestorResolver`, TryAdd singleton), so there is **no new DI registration** | The walk would select columns a schema does not have, so every read would fault and withhold |
| `ExternalModuleDescriptor.LiveParentAttributes` / `LiveParents` / `RuleInput`; `ScopeRows(..., liveParents)` | `RootRuleAttributes` / `RootLinks` | Extended: `RootRuleAttributes` concatenates them, so the fetch projects them server-side and strips them | The documents grid would keep the stamp-only decision |
| `MessageRootLookups.Parents` (optional record member) | `MessageRootLookups` | Extended in place | Messages and notifications would keep the stamp-only decision |
| Optional parameters on `GetMessagesCoreAsync`, `RootsReadableFor`, `SecureRootsFilter` and `ExecuteScopedFetchCoreAsync` | | Optional at the end, so existing test callers compile. Production passes the reader; no reader resolves nothing, which is fail closed | |
| ArchTest `ExternalChildRowLiveParentGuardTests` | | New guard (enforcement ladder) | A stamps-only call returns unnoticed |

No new endpoint, package, Dataverse column or file surface.

## Query cost

The cost per page (list or single row) is the existing secure-flag read, plus the live-parent walk.

- **The walk.** It reads level by level. Each level costs one `id IN (…)` query per distinct parent table, in chunks of 200 ids
  (`IdsPerQuery`), and selects only that table's lineage lookups. Roots (project, matter, work assignment) end the climb
  without a read; their secure state comes from the one flag read over the union. Records are deduplicated across rows and
  levels. Bounds: ≤ 6 levels and ≤ 500 records read in total.
- **The column probe.** One per parent table per walk, through the cached metadata probe. These are cache hits after the
  first.
- **One read per parent table: when it holds.** It holds exactly when each parent table appears at one level and has
  ≤ 200 ids, which is the usual case: a to-do under a communication or an invoice that is filed to roots. It is exceeded only
  by a same-table chain (an event under an event, or a document under its parent or canonical document) or a climb through
  different tables (communication → invoice reads `sprk_communication`, then `sprk_invoice`, still one per table). The
  owner's "the walk is bounded" covers this.
- **Worst case.** Queries ≤ Σ over levels (≤ 6) and tables of ⌈n/200⌉, with Σ n ≤ 500. The lineage map has 25 child
  tables, so the hard ceiling is 6 × 25 + 2 = 152 queries. That needs a page whose parents reach every child table at every
  level, which no real filing shape has. A to-do page's first level is at most 8 queries (agreement, analysis, budget,
  communication, document, event, invoice, report card). A document page's first level is at most 6.
- **Measured** (tests over the projecting fake; `FilingReadLog` counts queries):
  - three to-dos under three communications: **1** filing query (`sprk_communication`), **1** flag read and **0** root reads;
  - a communication → invoice chain: **2** queries;
  - a page with no parent lookups: **0** filing queries.

## Tests

| Run | Result |
|---|---|
| New tests, branch: `ExternalLiveParentReadTests` (23), `ExternalLiveParentDocumentTests` (15), `ExternalLiveParentRuleTests` (17) | **55 / 55 pass** |
| Targeted unit filter `External\|CommunicationThreadRead\|ParentLineage\|SecureChild` (it includes the integration tests globbed into the project, e.g. `ExternalTodoScopeTests` 61 and `ExternalTodoWritePath` 18) | **3,710 pass, 0 fail** |
| Full `tests/Spaarke.ArchTests` | **994 pass, 0 fail** (15 of them are the new guard) |
| BFF build | 0 errors, 0 warnings in changed files |

The 25 failures of the first run were all expected and are fixed. Five were `$select` pins that now include the parent
lookups. Twenty were doubles that did not override the new `CheckLiveParentsAsync` seam and so returned 500.

## Mutation and fail-on-master evidence

| Perturbation | Red |
|---|---|
| **Master source** (`6da771b96`, a separate worktree) with the two new HTTP-level test files and the fixture's test-only additions copied in | **26 of 38 fail**. They cover every carrier, stale, fail-closed, PATCH and delete-own case and the cost test. The 12 that pass are the listed or applied controls (no over-hiding). The rule-level file cannot compile on master (new API), which is expected |
| **M1:** drop the live-parent input (`ChildRowRoots.Of` ignores `parents`) | **35 of 55 new tests fail**. The same 20 positive controls and pins stay green |
| **M2:** drop the parent lookups from the list `$select` (`LiveParentSelectSuffix` returns empty) | **19 fail**: every list case, plus three select pins (`ProjectListUrl…`, `BuildEventsQuery…`, `TheListAndSingleRowReads_SelectEveryLiveParentLookup`) |
| **The guard on master source** | **Fails** and names the five stamps-only sites: `ExternalMessageEndpoints.cs:412`, `ExternalDataService.cs:497`, `:1515`, `:1627`, `ExternalModuleRegistry.cs:288` |

Every mutation was restored with `git checkout -- <file>` from the committed version. The tree was clean before the final run.

## Guard

`tests/Spaarke.ArchTests/ExternalChildRowLiveParentGuardTests.cs` scans `src/server/**`, with line comments stripped. Its rules:

1. `ExternalMultiRootRule.SecureRootsReadable` is used (called or passed as a method group) only in `ExternalMultiRootRule.cs`.
2. No `ChildRowRoots.Of(...)` passes a literal "no parents": `null`, `default`, `[]`, `Array.Empty<…>()`, `Enumerable.Empty<…>()` or `new LiveParentLookup[0]`.

An argument list that the guard cannot split into three is reported, never skipped. The guard also fails if it finds no
rule file, or fewer than five `Of` calls, so it cannot pass vacuously.

Controls:
- **Must-not-fire:** the sanctioned shape; the rule file itself; comments and `SecureRootsReadableBy`.
- **Must-fire:** nine stamps-only spellings; an unreadable call; the splitter's handling of nesting and string literals.

There is no ratchet baseline, because the branch has no existing violations. A `$select` that drops a lookup is outside a
source scan. The select pins and the projecting fake cover it, as M2 shows.

## Publish size (§10)

Both sides were published fresh with `dotnet publish -c Release -o` (as in `Deploy-BffApi.ps1:228`) from short-path
worktrees (`C:\wt187m` at `6da771b96` and `C:\wt187` at the branch head). Each was zipped with PowerShell
`Compress-Archive -CompressionLevel Optimal`, PDBs included.

| Side | Bytes | MB | Files |
|---|---|---|---|
| Master `6da771b96` | 38,412,243 | 36.63 | 192 |
| Branch | 38,424,743 | 36.64 | 192 |
| **Delta** | **+12,500** | **+0.01** | 0 |

No `.csproj`, props or lock-file change, so no new package and no CVE delta.

## ADR notes

- **ADR-034** ("no recursion, no grandparent walk") governs `CoreAncestorResolver`'s stamp WRITE. This is a READ-side
  check. It reuses `ParentLineageWalk`, which already climbs for tasks 174 and 175, and the owner ordered the bounded walk
  (round 99). No conflict.
- **ADR-002:** no plugin.
- **ADR-038 section 4:** the new seams are `virtual`, and the tests drive the real walk over a fake reader.
- **Auth:** the external plane stays app-only. This change only narrows what the caller sees.

## Open questions (owner; none changed by this task)

- **Q1. Invoice threads.** The thread stays exempt from the rule (option A, 2026-10-11). Under round 99, a message on an
  invoice thread can name a carrier parent (an event or analysis under a secure X). Should the live-parent half apply to
  invoice threads, so that each message's OTHER parents are checked and the invoice itself is not re-walked (as Q3 does for
  documents)?
- **Q2. Invoices module `/fetch`.** It still uses any-parent with no multi-root rule (option A). An invoice filed to an
  agreement under secure X is listed to a reader of the invoice's other parent. Keep option A, or apply the live-parent
  half to the invoice's non-route parents?
- **Q3. Invoice-route documents.** The route's own invoice is not walked (that preserves option A). The document's other
  related links are checked. Confirm.
- **Q4. Document delete-own.** It has no live-parent check (POML AC4 names to-dos and events). Add the same check
  (Readable → continue; Unavailable → 503; otherwise 403)?
