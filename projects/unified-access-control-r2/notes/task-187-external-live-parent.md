# Task 187: external child-row reads check live parents (D-123, #1857)

> Branch `uac/187-external-live-parent` from master `6da771b96`. Owner round 99, "Live-parent check (precise)", and fix
> round 1 (owner round 100: D1 to D4; review findings R1 to R5), and fix round 2 (F1: upload via an invoice; K1: walk cost).
> This note holds the surface table, the §11 answers, the query cost, the mutation evidence, the publish size and the known
> limits.

## The rule as built

An external child-row read selects the row's parent lookups too (`ExternalMultiRootRule.LiveParentSelect`: the row
table's lineage lookups in `ParentLineage.ChildFiling`, minus the root lookups). It resolves their LIVE roots in one
bounded app-only batch per page (`ExternalMultiRootRule.ReadLiveParentsAsync` → `ExternalDataService.GetLiveParentRootsAsync`
→ `ParentLineageWalk.TopsOfManyAsync`). It then decides with `ExternalMultiRootRule.ChildRowReadable` over
`ChildRowRoots.Of(table, row, stamps, live[, authorizedParent])`, which is {stamped roots} ∪ {live roots of every parent
the row names}. `Of` derives the parents from the row itself (R3), so a caller cannot hand it an empty list. The existing
secure-flag read (`GetNonSecureRootsAsync`, effective flags) runs once over that union.

- **No over-hiding.** A parent whose top is an ordinary root, or a parentless record, adds nothing secure. A row naming
  no parent is decided on its stamps alone, as before.
- **Fail closed.** These all withhold the row: a parent value that is not a GUID; a parent the walk did not resolve
  (missing, a read that throws, deeper than `MaxDepth` = 6, past `MaxLiveParentReads` = 20,000); and no filing reader. A
  list reports `Truncated` when a fault or an incomplete read withheld rows.
- **Writes.** The to-do PATCH and the to-do, event and document delete-own answer 403 when the row is behind a wall and
  503 when a read could not decide (R4, D3). Nothing is written or deleted in either case.
- **Logs.** Counts only (`[EXT-LIVEPARENT] …`). No ids, names or values.

## Surface table

Line numbers are at the branch head after fix round 1, except the rows marked "fix round 2".

| Surface | Where | Status |
|---|---|---|
| To-do list (project, matter and work assignment routes) | `ExternalDataService.cs:1573` `GetTodosAsync`; select `BuildTodoListUrl` :1623 | **Fixed** (AC1, AC2, AC3, AC7) |
| Events list (project, matter and work assignment routes) | `ExternalDataService.cs:1666` `GetEventsAsync` → `ReadableEventsAsync` :1702; select `BuildEventsQuery` | **Fixed** |
| Document list (project, matter, work assignment, service request and invoice routes) | `ExternalDataService.cs:444` `GetDocumentsAsync`; select `BuildDocumentListUrl` :618 | **Fixed.** Related event, communication, to-do, invoice, agreement, parent and canonical document |
| Document download and versions (every parent route) | `ExternalProjectDataEndpoints.cs:1014` `IsDocumentReadableUnderAsync`; row read `ExternalDataService.cs:887` | **Fixed.** Same refusal as "not under the parent"; no SPE pointer read |
| Invoice-route documents: the route's own invoice | `ExternalDataService.cs:453` (`DocumentRootLinks.ForRoute` removed) | **Fixed, D4.** The route invoice is walked like every parent: an invoice whose live roots include a secure root the caller cannot read withholds its documents from the list and refuses download and versions. Ordinary invoice: option A, unchanged |
| Documents module `/fetch` (the grid) | `ExternalModuleDataEndpoints.cs:299` (live read), :309 `ScopeRows`; `ExternalModuleRegistry.cs` `LiveParentAttributes` / `LiveParents` / `RuleInput` | **Fixed** (AC6) |
| Invoices module `/fetch` | `ExternalAccessModule.cs:437` (`RequireReadOnSecureRoots = true`) | **Fixed, D2.** Projects matter, project and agreement; withholds an invoice whose live roots include a secure root the caller cannot read. An ordinary second parent still lists (option A) |
| Messages (project, matter, work assignment and service-request threads) | `ExternalMessageEndpoints.cs:424` `SecureRootsFilter`; row select `CommunicationThreadReadService.cs:516`, `RootsOf` :470 | **Fixed** (AC5) |
| Invoice message thread: the messages | `ExternalMessageEndpoints.cs:403` `RootsReadableFor` | **Fixed, D1.** No longer exempt: each message's stamps plus the live roots of its OTHER parents; the route invoice is the gate's decision (`authorizedParent`) |
| Invoice message thread: the gate (read and post) | `ExternalMessageEndpoints.cs:556` `EvaluateRecordGateAsync` (:633), `PostMessageAsync` :132 | **Fixed, D4.** For a rule module the gate reads `RootRuleAttributes`, the live parents and the flags: an invoice behind a secure wall is 403 on read and post |
| Notification feed | `ExternalNotificationFeedEndpoints.cs:332` (the same `SecureRootsFilter`, with the live reader) | **Fixed** (AC5) |
| To-do PATCH | `ExternalProjectDataEndpoints.cs:1702` `CheckLiveParentsAsync` after the Write check | **Fixed** (AC4). 403 behind the wall; **503 on a failed read (R4)**, `sdap.access.todo_write_unavailable`; no PATCH sent |
| To-do, event and document delete-own | `ExternalProjectDataEndpoints.cs:1836` (step 3 of `AuthorizeDeleteOwnAsync`) | **Fixed** (AC4; **document added by D3**). 403 or 503; no DELETE sent, no SPE file deleted |
| Service-request message gate | `ExternalMessageEndpoints.cs:556` `ScopeRows` on the requester-scoped module | **SAFE.** Requester scope; the module has no multi-root rule |
| Notification feed: service-request rows | `ExternalNotificationFeedEndpoints.cs:313` `ScopeRows` | **SAFE.** Requester-scoped records (`sprk_requestedby`) |
| `/data/record/{entity}/{id}` | `ExternalModuleDataEndpoints.cs:414` | **SAFE.** `IsRecordAccessible` admits only an id that is itself an accessible root id, so a document or invoice id never passes |
| Contacts, organizations, `/me`, entitlements, assigned access | `ExternalProjectDataEndpoints.cs` | **SAFE.** Not child rows of a root |
| Document upload via an invoice (`POST /invoices/{id}/documents`) | fix round 2: `ExternalProjectDataEndpoints.cs:935` `RightsForInvoiceAsync` → `ExternalMessageEndpoints.cs:518` `GetRecordRightsDecisionAsync` → `EvaluateRecordGateAsync` :591; invoice read `ExternalDataService.cs:1177` `GetInvoiceLookupsAsync` | **In scope, fixed (F1).** It was a hole: rights were the union of the invoice's parents with no secure-root check, so Create on ordinary matter M wrote a file into secure project X's container. Now, when the union carries Create, the invoice is put to the invoice-thread gate (rule attributes, live parents, flags, `ScopeRows`). Behind a secure wall: Create is dropped, so the upload is a 403 before SPE or Dataverse is touched, and the list's `CanCreate` is false. A gate read that could not decide: the `invoice_unreadable` 503. Read is unchanged (the D4 walk keeps its shapes). The upload handler body is not edited |
| Create to-do, event or document on a root; upload to a root, service request; mark seen | create routes | **SAFE.** They read no existing child row; a root's own rights are the principal's |
| Shared collection-page reader and `CollectionPrefer` | `ExternalDataService.cs:3151` `GetCollectionPageAsync` | **Not touched** (SPA-r3 task 064 owns them) |
| `WorkforcePrincipalResolver.ResolveAsync` | | **Not touched** |

**D4 is derived**, not a separate owner ruling: it is D2 (an invoice behind a secure root's wall is withheld from the
invoices list) applied with round 100's family rule to the invoice's own routes. If the list withholds the invoice, its
thread and its documents cannot be served through it.

## §11 answers (new surface)

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `ParentLineageWalk.TopsOfManyAsync` (and the private `TopsFromRead`) | `ParentLineageWalk.TopsAsync` climbs ONE start | Extended in the same class. `TopsAsync` per row would cost one query per row per level | A page of N rows would cost N×levels queries, or the read would stay stamp-only (#1857 leak) |
| `LiveParentLookup`, `LiveParentRootReader`, `LiveParentRootsRead`, `LiveParentRoots` (in `ExternalMultiRootRule.cs`) | `NonSecureRootReader` / `NonSecureRootsRead` (flag read) | Mirrors that delegate-and-answer shape beside it | Without them the rule has no input for a parent's live roots |
| `ChildRowRoots` + `ExternalMultiRootRule.ChildRowReadable` | `SecureRootsReadable(links)` | `ChildRowReadable` delegates to it. `Of(table, row, stamps, live)` derives the parents from the row (R3), so the union cannot be skipped | A child-row read could keep passing stamps only |
| `ExternalMultiRootRule.LiveParentLookupsOf` / `LiveParentSelect` / `LiveParentsOf` / `Except` / `ReadLiveParentsAsync` | `Stamps`, `ReadFlagsAsync` | Added to the same static rule class | Each surface would re-derive the parent columns and drift from the one lineage map |
| `ExternalDataService.GetLiveParentRootsAsync`, `ReadLiveParentRootsAsync` (virtual seam) | `GetNonSecureRootsAsync` | Added beside it, over the existing `IGenericEntityService` filing reader | Endpoints and the message filter would have no reader to pass |
| `ExternalDataService.CheckLiveParentsAsync`, `LiveParentDecision`, `BuildLiveParentCheckUrl` | `GetOwnershipAsync` (delete-own) / the PATCH Write check | A single-row read that delete-own and PATCH share; covers to-dos, events and (D3) documents | The PATCH and delete-own would act on a row the list withholds (AC4) |
| `ExternalProjectDataEndpoints.TodoWriteUnavailable` + `TodoWriteUnavailableReasonCode` (R4) | `DeleteOwnUnavailable` | Same shape, own wording and code: the delete-own text says "nothing was deleted" | A transient read failure tells the PATCH caller "no access" |
| `ExternalDataService` constructor parameter `CoreAncestorResolver? columns` | The resolver's 6-hour cached metadata probe | Reused; already registered unconditionally, so **no new DI registration** | The walk would select columns a schema does not have |
| `ExternalModuleDescriptor.LiveParentAttributes` / `LiveParents` / `RuleInput`; `ScopeRows(..., liveParents)` | `RootRuleAttributes` / `RootLinks` | Extended: `RootRuleAttributes` concatenates them | The documents and invoices grids would keep the stamp-only decision |
| `MessageRootLookups.Row` (optional record member) | `MessageRootLookups` | Extended in place: the raw row, from which `Of` derives the parents | Messages and notifications would keep the stamp-only decision |
| Optional parameters on `GetMessagesCoreAsync`, `PostMessageCoreAsync`, `IsRootRecordReadableAsync`, `GetRecordRightsAsync`, `RootsReadableFor` (`id`), `SecureRootsFilter` (`authorizedParent`) and `ExecuteScopedFetchCoreAsync`; `ExternalDataService` on `PostMessageAsync` | | Optional at the end. Production passes the readers; no reader resolves nothing, which is fail closed | |
| ArchTest `ExternalChildRowLiveParentGuardTests` | | New guard (enforcement ladder) | A stamps-only call returns unnoticed |
| Fix round 2: `ExternalMessageEndpoints.GetRecordRightsDecisionAsync` (and the private `RightsFromGate`); `EvaluateRecordGateAsync` returns `Undecided` | `GetRecordRightsAsync` | The same gate evaluation; `GetRecordRightsAsync` keeps its body and answer. Only the undecided bit is new | The upload could not tell "behind the wall" (403) from "could not read" (503) |
| Fix round 2: `ExternalDataService.GetInvoiceLookupsAsync` (virtual seam) | `GetInvoiceParentsAsync` (matter and project only, a typed pair) | Not extended: the gate needs the rule's attribute set (the agreement too) in its record shape, and three `GetInvoiceParentsAsync` test doubles depend on the pair | The gate cannot be run for the document routes, which have no `RecordService` |
| Fix round 2: `ExternalDataService.Logger` (internal) | the service's own logger | Exposed, not a new logger | The gate's fault logs from the document routes would go nowhere |
| Fix round 2 (K1): `ParentLineageWalk.QueriesSent` / `RecordsAsked`; `ExternalDataService.WalkQueriesWorthReporting` + `LogWalkCost` | `FilingReadLog` (test-only) | Counters on the one walk | The walk's cost per request is invisible in production |

No new endpoint, package, Dataverse column or file surface.

## Query cost

The cost per page (list or single row) is the existing secure-flag read, plus the live-parent walk.

- **The walk.** It reads level by level. Each level costs one `id IN (…)` query per distinct parent table, in chunks of 200
  ids (`IdsPerQuery`), and selects only that table's lineage lookups. Roots end the climb without a read. Records are
  deduplicated across rows and levels.
- **The bound (R1).** `MaxLiveParentReads` = 4 × `MaxCollectionRows` = **20,000** distinct records per request, ≤ 6
  levels. When a level does not fit, the part that fits is read and only the chains through an unread record are
  undetermined (withheld, `Truncated`); the rest of the page lists. The old bound of 500, applied to whole levels, emptied a
  normal page of 251 to-dos each under its own communication and thread (502 records).
- **Cost at the bound.** 20,000 / 200 = 100 filing queries, plus at most one rounding query per (level, table): the hard
  ceiling is 100 + 6 × 25 = **250 queries**. A full 5,000-row page whose rows each sit under their own communication and
  thread reads 10,000 records: 25 + 25 = **50 queries**. The usual page (rows sharing a few parents) stays at one query per
  parent table.
- **The column probe.** One per parent table per walk, through the cached metadata probe. Cache hits after the first.
- **Single-row gates.** The document delete-own check reads one row plus its walk. The invoice thread gate reads the
  invoice (matter, project, agreement), walks the agreement (one query per level) and reads the flags of its unreadable
  roots. An invoice with no agreement and readable roots costs no extra query (pinned:
  `AnInvoiceThread_DoesNotWalkTheRouteInvoiceAgain_ItIsTheGatesDecision_D1`).
- **Measured** (`FilingReadLog` counts queries): three to-dos under three communications, 1 filing query; a communication →
  invoice chain, 2; a page with no parent lookups, 0.
- **Logged** (fix round 2, K1). Each request's walk logs `[EXT-LIVEPARENT] Walk cost: {queries} filing queries for {records}
  records ({parents} parents)` at Debug, and the same line at Information once it passes 50 queries
  (`WalkQueriesWorthReporting`; a full 5,000-row page of rows under their own communication and thread is exactly 50).
  Counts only.
- **The invoice upload gate** (fix round 2, F1) adds, only for a caller whose union carries Create: one invoice read (matter,
  project, agreement), the agreement's walk when there is one, and the flag read of the roots the caller cannot read. A
  ViewOnly caller causes no gate read (pinned: `InvoiceRights_WithoutCreate_CauseNoGateRead_F1`).

## Tests

| Run | Result |
|---|---|
| Targeted unit filter `External\|CommunicationThreadRead\|ParentLineage\|SecureChild` (includes the integration tests globbed into the project) | Fix round 1: 3,743 pass. **Fix round 2: 3,756 pass, 0 fail** (13 new) |
| Full `tests/Spaarke.ArchTests` | **999 pass, 0 fail** (20 are the guard) |
| BFF build | 0 errors, 0 warnings in changed files |

## Mutation and fail-before evidence

Round 0 (the original change):

| Perturbation | Red |
|---|---|
| Master source with the new HTTP-level tests copied in | 26 of 38 fail (every carrier, stale, fail-closed, PATCH and delete-own case); the 12 listed controls pass |
| M1: drop the live-parent input | 35 of 55 new tests fail |
| M2: drop the parent lookups from the list `$select` | 19 fail |
| The guard on master source | Fails and names the five stamps-only sites |

Fix round 1 (each test failed before its fix and passes after):

| Item | Perturbation | Red |
|---|---|---|
| R1 | Restore the whole-level break | 1 of 59 (the partial-level test) |
| R1 | Bound back to 500 | 2 of 59 (the 251- and 3,000-row pages) |
| R2 | `LiveParentsOf(CommunicationTable, row)` replaced by null at the thread-read parse | 4 of 4 carrier cases (message and feed); the ordinary control passes |
| R2 | `LiveParentSelect` dropped from the thread `$select` | the same 4 |
| R3 | An empty row at the message call site | the guard fails (rule 3) |
| R3 | A `(stamps, parents, live)` overload added to `ChildRowRoots` | the reflection fact fails |
| R4 | Before the fix | the PATCH failed-read test sees 403, not 503 |
| R5 | `sprk_regardingmatter` dropped from the walk's select | 14 of 60 with the projecting double, 0 of 60 with the old double |
| D3 | Before the fix | route 403 and 503 cases both deleted the document (204); 5 service and pin tests failed |
| D3 | Documents decided over the to-do regarding stamps | the twin-link test |
| D2 + D4 gate | `RequireReadOnSecureRoots` back to false on invoices | 8 fail (fetch, gate, agreement, post, select pin) |
| D4 gate | No flag read in the gate | 2 fail (ordinary-root cases over-hidden: read and post) |
| D4 gate | No live read in the gate | 1 fails (the X reader is refused) |
| D1 | Invoice exemption restored | 1 fails (the event-of-secure-X message lists) |
| D1 | The route invoice walked per message | 6 fail, including the no-walk pin |
| D4 documents | The `ForRoute` code (`fbdcf83d1`) with the new tests | 3 fail (list withheld, content and versions refused) |

Fix round 2 (13 new tests: 7 on the in-memory upload fixture, 3 over the real reads, 3 on the walk-cost log):

| Item | Perturbation | Red |
|---|---|---|
| F1 | Before the fix (`RightsForInvoiceAsync` returns the union, no gate) | 6 fail: secure project 403 (in-memory and real reads), agreement under secure X 403, the three 503s. The controls pass: ordinary second parent 201, secure X the caller reads 201, list `CanCreate` with ordinary X, no gate read without Create |
| F1 | Undecided never reported | 3 fail (the three 503s become 403) |
| F1 | The gate given no live-parent reader | 1 fails (the agreement case: 503, not 403, and its 201 control) |
| F1 | The gate given no flag reader | 4 fail (ordinary X over-hidden: 201 and `CanCreate`; the flag-fault 503s become 403) |
| F1 | Undecided ignores a failed flag read | 2 fail (the flag-fault 503s) |
| K1 | No walk-cost log | 3 of 3 fail |
| K1 | Threshold `>=` instead of `>` | 1 fails (exactly 50 queries stays at Debug) |

The two ArchTest route-authorization rules failed on the first fix-round-2 shape (`GetRecordRightsAsync` delegating to a new
method put the gate two hops from the message POST's declared seam). `GetRecordRightsAsync` was restructured to call
`EvaluateRecordGateAsync` itself again; the ledger was not changed.

Every mutation was restored from a saved copy and rebuilt (never `--no-build` after a restore).

## Guard

`tests/Spaarke.ArchTests/ExternalChildRowLiveParentGuardTests.cs` scans `src/server/**`, with line comments stripped. Its
rules:

1. `ExternalMultiRootRule.SecureRootsReadable` is used (called or passed as a method group) only in `ExternalMultiRootRule.cs`.
2. (Type level, by reflection.) No `ChildRowRoots.Of` overload accepts a `LiveParentLookup` or a list of them; each takes a
   `string` table and an `IReadOnlyDictionary` row first; the struct has no public constructor. Enforced by the compiler
   too: the three old bypass spellings (`parents: null`, a cast null, a local empty array) no longer compile, so they are
   not must-fire cases any more.
3. No `ChildRowRoots.Of(...)` passes a literal EMPTY ROW: `null`, `default`, `default(T)`, a cast of either, `new()`,
   `new Dictionary<…>()` / `{ }`, `[]` or `….Empty`.

An argument list the guard cannot split into four or five is reported, never skipped (the old three-argument shape is a
must-fire case). The guard also fails if it finds no rule file, or fewer than five `Of` calls.

Controls: must-not-fire (the sanctioned shape; the rule file; comments, `SecureRootsReadableBy` and the four real call
shapes); must-fire (three stamp-decision spellings and nine empty-row spellings; an unreadable call; the old shape); the
splitter's handling of nesting, generics and string literals.

## Publish size (§10)

Measured at round 0 from fresh `dotnet publish -c Release -o` of master `6da771b96` and the branch:

| Side | Bytes | MB | Files |
|---|---|---|---|
| Master `6da771b96` | 38,412,243 | 36.63 | 192 |
| Branch (round 0) | 38,424,743 | 36.64 | 192 |
| **Delta** | **+12,500** | **+0.01** | 0 |

Fix rounds 1 and 2 add a few hundred lines of C# to existing files and no package, so the delta stays in the same range;
it was not re-measured. No `.csproj`, props or lock-file change, so no CVE delta.

## ADR notes

- **ADR-034** ("no recursion, no grandparent walk") governs `CoreAncestorResolver`'s stamp WRITE. This is a READ-side
  check reusing `ParentLineageWalk`, and the owner ordered the bounded walk (round 99). No conflict.
- **ADR-002:** no plugin. **ADR-038 section 4:** the seams are `virtual`; the tests drive the real walk over a fake reader.
- **Auth:** the external plane stays app-only. This change only narrows what the caller sees.

## External SPA (R4)

`SmartTodo.handleToggleStatus` catches any `updateTodo` error, reverts the optimistic update and shows the error's
message, so the PATCH 503 needs no SPA change. `delete-own.ts` already maps the delete-own 503.

## Known limits (owner round 100: recorded, not fixed)

- **Unprobed `$select` columns.** The list and single-row selects name the lineage lookups without a metadata probe; every
  environment is built from SpaarkeMaster, so the columns exist. The walk itself does probe.
- **`sprk_currentversionid` is excluded from the walk.** No writer of that lookup was found.
- **The canonical-document parent** (`sprk_canonicaldocument`) is walked like any parent; this is an owner FYI.
- **No `Truncated` signal on message threads.** A message withheld because a read could not decide is simply absent.
- **The invoice thread gate answers 403 on a read that could not decide** (fail closed). The thread routes have no 503
  shape.
- **Invoice-thread stamps stay in the decision (D1).** A message is decided over its stamps and its other parents, like
  every thread. A STALE stamp naming a secure root the invoice no longer has would withhold that message (the safe
  direction); the opposite drift is caught by the gate, which reads the invoice live. Consistent with every other thread
  (fix round 2: kept as a K).

## Known limits (fix round 2: recorded, not fixed)

- **Invoices `/fetch` hides on a read fault without a `Truncated` signal**, as the documents module's `/fetch` does. The
  module grid has no such field; an invoice withheld because a read could not decide is simply absent.
- **The refused invoice upload answers the "insufficient rights" 403** (`sdap.access.deny.insufficient_rights`, "Your access
  level does not permit uploading documents to this invoice"), not the "no access to this invoice" 403. F1 drops Create
  and keeps Read so that the read routes keep D4's owner-tested shapes (list 200 with the documents withheld, download
  "not part of the requested invoice"), and so that the list's `CanCreate` is honest. Both are 403 and nothing is written.
  Returning `None` from the shared `RightsForDocumentParentAsync` would have changed those read shapes; a write-only `None`
  would need the upload handler body to pass a flag, which this round keeps out of (SPA-r3 task 063 edits that body).
- **A caller with Create on an invoice gets the 503 on every invoice document route when the gate read fails**, not only on
  upload (the gate runs inside the shared rights read). A ViewOnly caller does not run the gate, so its list and download
  keep the walk's fail-closed behaviour. Both directions refuse; neither writes.
- **K1(b): the `dataverse-query` rate-limit policy is NOT applied to the external to-do, event and document list routes.**
  The policy partitions per caller (`CallerResolution.ResolveOpaqueCallerKey`, falling back to the IP), sliding window of 50
  permits a minute (4 segments, queue 5), shared by every route with that policy, which already includes the external
  messages and the notification feed. Normal SPA use (code-mapped, `src/client/external-spa`): a matter or work-assignment
  record modal issues 4 calls in that bucket (events, to-dos, documents, messages) plus the feed's 60-second poll; but the
  outside-counsel dashboard (`OutsideCounselDashboard.tsx:775`, `:867`) fans out one events and one documents call per
  project, 2N in one burst: a partner on 25 or more projects uses up the 50 permits on a single dashboard load, and from 28
  projects the calls past the queue of 5 are rejected with 429 (opening 13 record modals in a minute does the same). Applying
  the policy would throttle normal use and starve the bell and the message thread that share the bucket. The walk-cost log (K1a) is the signal to revisit with a dedicated, higher policy.
