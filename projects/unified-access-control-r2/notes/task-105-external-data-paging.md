# Task 105 — ISS-002 (#963): the external app's lists are complete or say they are cut short

> Branch `fix/uac-r2-105-external-paging` (worktree `C:\wt105`, from `origin/master` 39ba73dbc). Rigor FULL.
> Scope as amended by owner round 59 item 5: paging to a cap, an optional `truncated` field, and a small SPA banner.

## 1. Premises checked before coding

| Premise | Result | Evidence |
|---|---|---|
| `GetCollectionAsync` reads one page and never follows `@odata.nextLink` | TRUE | `ExternalDataService.cs:1176-1202` on master |
| `$top=200` at four call sites | TRUE; the register's line numbers had drifted | master `:234` documents, `:605` to-dos (`BuildTodoListUrl`), `:682` events (`BuildEventsQuery`), `:1166` grant rows |
| With `$top`, Dataverse returns no `@odata.nextLink` | TRUE | Microsoft Learn, "Use OData to query data", "Limit the number of rows": "Don't use `$top` when you request pages of data." Live, spaarkedev1, 2026-10-08: `sprk_documents?$top=2` gave 2 rows and no nextLink; `Prefer: odata.maxpagesize=2` without `$top` gave 2 rows and a nextLink |
| `$top` combined with `maxpagesize` is safe | FALSE, and it fails silently | Live: `$top=3` with `maxpagesize=2` gave 2 rows and **no** nextLink. Learn, "Page results": "Don't use `$top` with the `Prefer: odata.maxpagesize` request header." The reader now refuses a URL that carries `$top` |
| A nextLink must be used unchanged, with the same `Prefer` value | TRUE | Learn, "Page results" |
| GET URL limit | 32,768 characters | Learn, "URL length limitations" |
| "Authorization-adjacent" (the POML header) | FALSE, as round 59 corrected | every reader of `GetCollectionAsync` is a display list or a payload lookup; none makes an access decision |
| Escalation trigger 2: a caller treats a page-1 empty result as authoritative for access | NOT FIRED | readers: project list, document/to-do/event lists, the participant id read, contact/account details, the record-type lookup. All are for display or a payload |
| Escalation trigger 1: surfacing needs more than an additive field | NOT FIRED | `truncated` is additive and omitted when false. The only consumer is `src/client/external-spa`, which is also the Teams app's bundle, and it ships in this PR |

Live data on dev, 2026-10-08: no project has more than 200 children today. The most is 6 documents, 72 events on the busiest event root, and 4 grant rows. The 551 "documents" belong to the null-project group. The defect cannot be reproduced live without seeding rows (see §6).

## 2. What changed

**BFF: `Infrastructure/ExternalAccess/ExternalDataService.cs` (the existing service; no new component, DI registration or package).**
- `GetCollectionAsync` follows `@odata.nextLink` up to `MaxCollectionPages` (25) × `CollectionPageSize` (200) = `MaxCollectionRows` (5,000, the same ceiling as the evaluator's NFR-03 cap). Every page sends `Prefer: odata.maxpagesize=200`. It returns `CollectionRead<TRow>(Rows, Outcome)`, where the outcome is `Complete`, `CapReached`, `LaterPageFailed` or `FirstPageFailed`. `Truncated` is `CapReached` or `LaterPageFailed`.
  - Hitting the cap logs a warning: `collection_truncated`, the url, the cap and the row count.
  - A failure on a later page logs an error naming the page, the row count and the url.
  - A nextLink outside the Dataverse Web API base is not followed (the bearer token never leaves). The read is reported as truncated.
  - A first-page failure keeps its old behaviour: logged and returned empty (out of scope per the POML).
  - A plain loop, not the `PrivilegeGroupResolver` iterator (ISS-001): each page's rows are added once.
- The four `$top=200` sites drop `$top` and add a primary-key tiebreaker to `$orderby`, so pages cannot overlap (Learn, "Page results", deterministic ordering).
- **Id-list reads are chunked** (`GetByIdsAsync`, `IdFilterChunkSize` = 100) for contacts, accounts and projects. Paging the grant rows to 5,000 would otherwise push the contact-detail read's single `contactid eq … or …` filter past the 32 KB URL limit at about 450 participants. The 400 would come back as an empty contact list, a regression this task would have introduced. If some chunks fail and others succeed, the result is truncated. If every chunk fails, it is `FirstPageFailed`, as before. A multi-chunk result is re-sorted by name.
- `GetProjectsAsync`, `GetDocumentsAsync`, `GetTodosAsync`, `GetEventsAsync`, `GetContactsAsync` and `GetOrganizationsAsync` return `ExternalCollectionResponse<T>` carrying `Truncated`. Organizations inherit the contact list's flag.
- **Found in passing and fixed:** `ResolveRecordTypeRefAsync` filtered `sprk_recordtype_refs` on `sprk_recordentitylogicalname`, which does not exist. The live answer is 400 `0x80060888`; the column is `sprk_recordlogicalname` (live metadata 2026-10-08). So every external to-do create logged "sprk_recordtype_ref not found" and never wrote `sprk_RegardingRecordType@odata.bind`, writing three of ADR-024's four resolver fields instead of four. Its `$top=1` is also dropped (the reader refuses `$top`).

**BFF: `Api/ExternalAccess/Dtos/ExternalProjectDtos.cs`.** `ExternalCollectionResponse<T>` gains `truncated` (`JsonIgnore WhenWritingDefault`), so a complete list's JSON is unchanged.
**BFF: `Api/ExternalAccess/ExternalProjectDataEndpoints.cs`.** The six list handlers return the service's response.

**SPA: `src/client/external-spa`.**
- `web-api-client.ts`: `getCollection` and the six list functions return `ListResult<T> { items, truncated }`. The inert `$top: 100/200` defaults are removed (`getCollection` never sent options; the BFF ignores them), and the `$top` option is documented as not applied.
- `components/TruncatedListNotice.tsx` (new): one Fluent v9 warning `MessageBar` with one wording, "This list is incomplete: only N {noun} could be shown. Some {noun} are not listed here."
- `DocumentLibrary`, `SmartTodo`, `EventsCalendar` and `ContactsOrganizations` (contacts and organisations) render the notice. The dashboard's "My Documents" count becomes a lower bound ("N+", "more than N") when any project's list was cut short.

## 3. Placement justification (root CLAUDE.md §10) and component justification (§11)

- **Placement:** the change lives in the existing `ExternalDataService`, the external data plane's only Dataverse reader, so no other location applies. It adds no endpoint, DI registration, package or background work.
- **`CollectionRead<TRow>` / `CollectionReadOutcome` / constants (internal, in the existing file).** *Existing:* `SpeContainerMembershipService.PermissionReadResult` (Graph SDK types, a different client) and `AccessibleRecordSetService.MembershipPageWalk` (resolver continuation tokens), neither over this service's raw `HttpClient`. *Extension:* neither can be reused across clients. This one modifies the existing private reader. *Cost of doing nothing:* without an outcome, a later-page failure is indistinguishable from a complete list, which is the defect.
- **`GetByIdsAsync` (private).** *Existing:* three inline OR-filter builders in the same file, now replaced by this one. *Cost of doing nothing:* the contact list returns empty past about 450 participants, once the grant read is no longer capped at 200.
- **`ExternalCollectionResponse.Truncated`.** *Existing:* the envelope itself, extended rather than replaced. *Cost of doing nothing:* the client cannot tell a cut list from a complete one (NFR-03).
- **`TruncatedListNotice.tsx` / `ListResult<T>`.** *Existing:* each view's own error `MessageBar` (error state, not truncation). *Extension:* a shared component keeps five call sites on one wording instead of five copies. *Cost of doing nothing:* the banner the owner asked for (round 59) does not exist.

## 4. Tests

- `tests/integration/regression/Issue963_ExternalDataPagingTests.cs` (regression KEEP path), 28 tests. A loopback fake Dataverse (WireMock) pages like the live service: it honours `maxpagesize`, emits a `$skiptoken` nextLink, sends no nextLink under `$top`, and returns the smaller of `$top` and `maxpagesize`. The real service runs over a real `HttpClient`. The tests:
  - each of the four reads at 250 children returns all 250, unique, not truncated, in 2 requests, with page 2 being the nextLink;
  - each read beyond the cap returns 5,000, truncated, in exactly 25 requests, with a warning naming the url and "Returning 5000 rows";
  - exactly the cap is complete;
  - page 2 failing returns truncated with page 1's 200 rows and an error log naming page 2;
  - fewer than one page means one request and not truncated;
  - every request carries `Prefer: odata.maxpagesize=200` and no `$top`;
  - a first-page failure stays empty and not truncated (pinned);
  - a nextLink to another host is not followed (that host records zero requests) and the list is truncated;
  - contact details come in 3 chunks with a short URL, re-sorted;
  - a failed contact chunk means truncated;
  - organizations from a truncated contact list are truncated;
  - the project list is chunked;
  - an external to-do create binds the record-type ref found through the live column (the fake answers 400 for any other column, as Dataverse does: G-13).
- `tests/integration/contract/Api/ExternalAccess/ExternalAccessContractTests.cs`: `ListRoute_CarriesTruncatedOnlyWhenTheReadWasCutShort` across 5 list routes. The field is absent for a complete list and `true` for a cut one, through the real host.
- Stubs updated for the new return type: `ExternalAccessContractTests`, `ExternalTodoScopeTests`, `EventRoutesLiveTests`.
- SPA: `tests/TruncatedLists.test.tsx`, 10 tests. The envelope maps `truncated`. Each of the 4 views shows the notice for a cut list (2 notices on the contacts tab) and none for a complete one. The existing mocks are moved to `ListResult`.

**Perturbations** (each committed first, then reverted):

| Perturbation | Result |
|---|---|
| Not following nextLink | 10 tests fail |
| Ignoring the cap | 4 fail |
| Dropping the flag on a later-page failure | 5 fail |
| Removing the off-host guard | 1 fails (after the test was strengthened; the first version passed, because the unreachable host failed anyway) |
| Not flagging a failed chunk | 1 fails |
| Restoring `sprk_recordentitylogicalname` | 1 fails |
| SPA: dropping `truncated` in `getCollection` | 5 fail |
| SPA: removing the EventsCalendar notice | 1 fails |

## 5. Known limits (K-class, no fix)

- **K2.** A first-page failure is still "empty, not truncated", as the POML scopes it. A Dataverse outage shows an empty list, as it did before this task. This is the same honesty class as the defect, and it is left for the owner to schedule.
- **K4.** `GetCollectionPageAsync` still swallows cancellation like the old reader. A client that disconnects during page 2 logs one `collection_truncated` error.
- **K4.** The dashboard reads every project's full document list, up to 5,000 each (before: 200), to show 10 and a count. The live maximum today is 6 per project.
- The project list's `truncated` is in the response but not bannered: the dashboard's rows come from `/me`, so a cut detail read shows an id in place of a name, not a missing row.

## 6. Live gate (after merge; needs the owner's OK for the seed writes)

Deploy the BFF and the external SPA. Then on a test project, seed 250 `sprk_document` rows: `GET /api/v1/external/projects/{id}/documents` returns 250 and no `truncated`, and the SPA shows no notice. The cap path (5,001 rows) is a unit-level proof; seeding it live is not proposed. No app setting, role or schema change.
