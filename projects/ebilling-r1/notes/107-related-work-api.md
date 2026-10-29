# Task 107 - Related work product API (notes)

> Run 2026-10-16. Rigor: the POML says STANDARD; the tree overrides UP to FULL (tags `bff` / `api` / `auth`, modifies `.cs`), so code-review and adr-check ran. Model sonnet @ high. Spec FR-34, NFR-02, NFR-11. Wave W4-1.

## 1. What was built

`GET /api/finance/invoices/{invoiceId}/lines/{lineId}/related-work` (`Api/Finance/InvoiceRelatedWorkEndpoints.cs`) over `InvoiceRelatedWorkService` (`Services/Finance/InvoiceRelatedWorkService.cs`, concrete, scoped), plus `NullSemanticSearchService` (ADR-032 P3 peer of `ISemanticSearchService`, see section 5).

- Filter chain, copied from 106: group `RequireAuthorization()` -> `AddFinanceAuthorizationFilter("finance.read", sprk_invoices, route invoiceId)` (Read as the caller, fail closed) -> `RequireEbillingEnabled()`.
- The line must belong to the invoice (`sprk_billingevent` read with both `sprk_billingeventid` and `sprk_invoice`); otherwise the uniform 404.
- Response: `{ invoiceId, lineId, status, reason, code, message, windowDays, truncated, items[] }`. `status` = `available` | `empty` | `unavailable`. `items[]` = `{ documentId, name, createdOn, score }`, at most 10, ordered by the search score (ties by id).
- 200 unless authorization fails; the only 4xx after authorization is the uniform 404.

## 2. How the answer is built

1. Line (app-only read, authorized by the invoice): `sprk_costtype`, `sprk_description` (the billed narrative), `sprk_eventdate`, `sprk_resolvedtimekeeper`, and the line's OWN core lookup (`sprk_matter` / `sprk_project` / `sprk_regardingworkassignment`, D-75). Exactly one core lookup must be set.
2. Timekeeper's linked contact: `sprk_timekeeper.sprk_contact` (task 017, label "Linked contact").
3. Window: `EbillingSettings.RelatedWorkWindowDays` (task 025, default 7, clamped to 0-90), applied to the document's `createdon` as whole UTC days, both ends inclusive (`createdon >= date - N days` and `createdon < date + N + 1 days`).
4. Candidates: one `sprk_document` query: active (`statecode = 0`), the core lookup of the line (`sprk_matter` / `sprk_project` / `sprk_workassignment`) = the line's core id, `sprk_createdbycontact` = the linked contact, in the window; most recent first, **at most 200 rows** (`MaxCandidateChecks`, the check budget). The core record and the author are re-checked on the returned rows, so a row of another core record or author can never become a candidate whatever the source returns.
5. Caller Read per candidate, **newest first, in pages of 50, until more than 50 are readable** (`MaxCandidates` + 1) or the 200-check budget is spent: `AuthorizationService.GetCallerRecordAccessAsync(caller, "sprk_documents", id, token)` + `OperationAccessPolicy.HasRequiredRights(rights, "read")` - the question `AddDocumentAuthorizationFilter("read")` asks, and the identical access path 106 uses for sibling invoices (8 at a time, repeats absorbed by `CachedAccessDataSource`), now one shared loop (`CallerReadCheck`). The first 50 readable go on; `truncated` is true only when MORE than 50 readable exist. Budget spent with fewer than 51 readable: the readable found so far, flagged as a complete answer would be (non-revealing).
6. Search: `ISemanticSearchService.SearchAsync` with `Scope = documentIds`, the readable ids, the narrative as the only text (cut to the contract's 1000 characters), `IncludeHighlights = false`, the caller's tenant (`tid` claim, as the semantic search route reads it). The result is intersected with the readable set, de-duplicated by document (best chunk score), ordered by score, top 10. The search limit counts chunks, so when fewer documents than `min(10, readable)` are ranked the readable ones not yet ranked are searched again, at most twice (`MaxExtraSearches`); a round that ranks nothing new ends the loop.

## 3. Reason and error codes (stable; the UI keys on them)

Empty (`status = empty`, 200, empty list): `not-a-fee-line`, `no-timekeeper`, `no-linked-contact`, `none-in-window` (the POML's three plus the expense line), and, with a one-line reason each, `no-core-record` (none or two core lookups on the line: nowhere unambiguous to look), `no-line-date` (no window can be placed), `no-narrative` (nothing to rank by), `none-indexed` (readable candidates exist but the files index returned none of them, e.g. not indexed yet).

Unavailable (`status = unavailable`, 200, no items): `related-work.access-check-failed` (no caller id/token, a rights lookup that threw, or one that Faulted without granting Read), `related-work.search-unavailable` (search threw, search switched off, or no tenant on the token), `related-work.timeout` (10 s deadline), `related-work.unavailable` (any other source failed). The cause is logged with ids only.

**No existence oracle:** when every candidate is unreadable the answer is exactly the one for an empty window (`none-in-window`, `truncated = false`); the truncation flag is withheld in that case and in `access-check-failed` so neither reveals that unreadable documents exist.

## 4. Deviations and decisions (each one line)

1. **Date column = `createdon`** (system, always set, UTC), not `sprk_createddatetime`: the latter is null on the dev documents inspected. Recorded as the POML asks.
2. **containerId not used** (POML note): the documentIds scope built from the core-record query is narrower and already excludes other records.
3. **Candidate cap 50 readable, check budget 200** (the search contract allows 100): each check is one rights lookup (~360 ms cold, 8 at a time); 50 readable bounds what is searched, 200 checks bound the cold path. `truncated` is a statement about readable documents only. The most recent readable are kept.
4. **Search limit 50 chunks, top 10 documents returned** (`MaxResults`).
5. **Access path = `GetCallerRecordAccessAsync`, not `IRetrievalAccessTrim`** (the batched AI-retrieval trim): the brief and 106 fix-round-2 name this path for any non-invoice record shown, and it reports `Faulted` so a faulted lookup is distinguishable from a refusal. Trade-off: one lookup per candidate rather than one batched read; bounded by the cap.
6. **Time zone:** the window is whole UTC days around the line's `sprk_eventdate` date part; a document created late local evening can fall on the adjacent UTC day. With a +-7 day default this moves an edge document by at most one day.

## 5. BFF hygiene (root section 10) and section 11

- Placement: BFF, B4 live request (synchronous read of Dataverse plus one search call; no queue, schedule or background work).
- Section 11 (service + endpoint): existing = `ISemanticSearchService` / `SemanticSearchAuthorizationFilter` (tenant-isolated search, drops unauthorized documents) and `FinanceAuthorizationFilter`; no route finds documents by core record, author and date window. Extension: reuses all of them; the new service only builds the candidate set (author and window are not index fields) and calls the existing search. Cost of doing nothing: reviewers cannot check a fee narrative against the work product it describes, FR-34 fails and the Related work pane (111) has no data.
- Section 11 (`NullSemanticSearchService`): existing = the `AddNullObjectsForCompoundOff` P3 peers; extension: none possible - `ISemanticSearchService` had no peer and is registered only inside the DocumentIntelligence + Analysis gate, while `InvoiceRelatedWorkService` (FinanceModule, unconditional per the flag rule) injects it; cost of doing nothing: the BFF is not constructible with AI off (`GateCombinationConstructibilityTests` ratchet fails; `ValidateOnBuild` throws). P3, not an optional parameter, because ADR-032 allows only P1/P2/P3; the consumer turns the `FeatureDisabledException` into `search-unavailable`, never into a silent empty list.
- ADR-010: no new interface. `ISemanticSearchService` now has two implementations, so it leaves the 1:1 set: the count is **164** against the ceiling **165** (measured by setting the ceiling to 0 and reading the message, then restored); inferred delta -1 versus 165. Ceiling not lowered (not this task's call).
- Ledger: `RouteAuthorizationGuardTests.Ledger.cs` gains `GovernedFile("Api/Finance/InvoiceRelatedWorkEndpoints.cs", RouteLevelGate, ...)` and `ExpectedEndpointFileCount` 130 -> **131**. Shared append point: open PRs #2039, #2038 and #2035 also edit this file; expect a trivial count conflict at merge (renumber, do not drop either entry).
- Publish size and CVE: see section 8.
- Hot-path conflict check: PR #1507 (ontology 131b) also touches `AnalysisServicesModule.cs` (a different region; soft warn only).

## 6. Escalation triggers - checked, neither fired

- 017 has the linked-contact column: `sprk_timekeeper.sprk_contact` (label "Linked contact"). **But dev has 0 `sprk_timekeeper` rows**, so the whole path cannot be exercised live yet (read-only MCP, 2026-10-16).
- Documents carry `sprk_createdbycontact` in dev (column present; 2 of the documents carry it, both written by the external SPA on 2026-10-13). It is stamped **only by the BFF for documents created from the external SPA** (`docs/data-model/sprk_createdbycontact.md`), so work product a firm user authored in an internal channel carries `sprk_createdbyperson` instead and will never match. That is how the spec defines the author ("via linked external contact"); it is a known limit, not a defect.
- `ISemanticSearchService` searches in `documentIds` scope without a contract change (`SearchFilterBuilder.BuildDocumentIdsScopeFilter`, max 100 ids). Not fired.

## 7. Quality gates

- code-review (self-run over the three source files): no F-class finding. Reviewed for: narrative or document text in logs or the response (none; the logging test asserts it); fail-closed on every access outcome (mutation-proved); the deadline token reaching the rights checks and the search (a deadline cancel is a `.timeout`, a caller cancel propagates); existence oracle through the empty reason and `truncated` (closed, tested); array writes in the parallel check are by index (safe). Cohesion: one responsibility (assemble one pane), single file.
- adr-check: ADR-001 Minimal API, own endpoints file; ADR-008 per-resource filter on the route; ADR-010 no new interface; ADR-013 search through `ISemanticSearchService` (the ArchTest on AI-internal types passes); ADR-015 logs carry ids and counts only; ADR-018 flag chained after authorization; ADR-019 ProblemDetails for 4xx only; ADR-032 P3 peer for the conditional dependency. No violation, no ADR tension.
- Mutation proof (each turned tests red, then restored): dropping the core-record condition and row re-check (3 tests), every candidate readable (6), a faulted or throwing lookup treated as a plain refusal (4), searching all candidates instead of the readable ones (2), window upper bound off by one (3), line not tied to the invoice (2), dropping the author condition and re-check (1), removing the authorization filter (6), removing the flag filter (1).
- Class grep (fix the class): the caller-Read loop (parallel gate, `GetCallerRecordAccessAsync`, Readable / Refused / Failed) now exists twice - here and in `InvoiceContextService.LoadReadableSiblingsAsync` / `CheckReadAsync` (task 106). Not merged (changing 106's file is a scope change); see the task report for the deferred item.

## 8. Publish size and vulnerabilities

Release, `-r linux-x64 --self-contained false`, `Compress-Archive -CompressionLevel Optimal` over the publish folder (PDBs as `dotnet publish` emits them), each built from a clean short-path tree (`C:\w107a` this task, `C:\w107b` its parent commit, `C:\w107c` the fresh master tip), so no incremental state is shared:

| Build | Zip | MiB |
|---|---|---|
| This task (working tree on `bbb10b78c`) | 39,208,938 bytes | **37.394** |
| Parent commit `bbb10b78c` (branch tip before this task) | 39,192,762 bytes | 37.379 |
| Fresh `origin/master` tip `77cd69a03` | 38,981,742 bytes | 37.178 |

**Delta of this task: +0.015 MB** (+16,176 bytes) against its parent commit, which isolates this task's contribution. Delta of the whole branch against the fresh master tip: +0.216 MB (the accumulated eBilling work, informational). Thresholds: +5 MB per task, 55 MB total, 60 MB hard stop - all clear. No package added; `dotnet list package --vulnerable --include-transitive`: no vulnerable packages.

## 9. Tests

- `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/InvoiceRelatedWorkServiceTests.cs` - 56 test cases (50 + 6 in fix-round-1): ranking by score and the top-N cap; the search request (narrative only, documentIds scope, readable ids, tenant, no highlights, long narrative cut); a document of another core record / project / another author / inactive is never a candidate, checked or searched; the window edges (both inclusive, to the tick) and settings-driven / clamped windows; the candidate cap and `truncated` (set only by readable documents); a run of restricted new documents does not hide readable older ones; an exhausted check budget answers like an empty window; long documents taking every chunk are re-searched, capped at two extra searches, and a round that finds nothing new stops; a line of another invoice or unknown -> null; unreadable candidate omitted and never searched, Write-only is not Read, all-unreadable answers exactly like an empty window with no truncation flag, rights lookup throwing / faulted -> unavailable with nothing searched, degraded read-probe counts as readable, no identity or token -> unavailable; every empty reason; results outside the readable set dropped; search failing / switched off / no tenant -> unavailable; Dataverse failing -> unavailable; deadline -> timeout; cancellation propagates; logs and response carry no narrative or document name.
- `tests/integration/contract/Api/Finance/InvoiceRelatedWorkEndpointsAuthorizationContractTests.cs` - 11 tests: 401, 403 (no rights, Write-only, other invoice / query-id decoy, rights check faulting), 503 `ebilling.disabled` (authorized) and 403 (unauthorized, flag off), 200 body (another core record's and an unreadable document absent by id and name, caller token reached the document rights check, search got only the readable id and the narrative), expense line 200 empty with reason, document rights fault 200 unavailable, uniform 404 for a line of another invoice.
- Results: focused `InvoiceRelatedWork` 61 pass (50 + 11); filtered Finance / DiGraph / Startup / EndpointGrouping unit run 994 pass, 2 skipped (live tests); `tests/Spaarke.ArchTests` 1056 pass.

## 10. Fix-round-1 (adversarial review: three F1 defects, the same leak in 106, shared helper)

Defects (all confirmed, all fixed):

1. **`truncated` leaked unreadable documents.** It was computed over all candidate rows before the Read check, so "51 candidates, 1 readable" answered `truncated = true`. It now rests on readable documents only (`scan.Readable.Count > MaxCandidates`). Tests: `ManyUnreadableDocumentsAndOneReadable_BeyondTheCap_IsNotTruncated`, `ARunOfRestrictedNewDocuments_DoesNotHideTheReadableOlderOnes`.
2. **The cap was applied before the Read filter.** The query took the 51 newest and only then checked Read, so 50 restricted new documents hid 10 readable older ones. Candidates are now checked newest first in pages until more than 50 are readable or a 200-check budget is spent. Budget spent: the same answer a complete one gives (readable so far, or the empty-window answer when none). Test: `AnExhaustedCheckBudget_AnswersLikeAnEmptyWindow_NeverRevealingWhatWasNotReached`.
3. **Long documents crowded out results.** The search limit counts chunks and de-duplicates by document afterwards, so three long briefs could take all 50 chunks and return 3 documents. Unranked readable ids are now searched again (at most twice). Tests: `LongDocumentsTakingEveryChunk_DoNotCrowdOutTheRest_TheUnrankedAreSearchedAgain`, `TheExtraSearches_AreCapped`, `ReadableDocumentsTheIndexDoesNotHold_AreNotSearchedForAgainAndAgain`.

106, same class: see `106-context-panel-api.md` section 7c.

**Shared helper (item 5).** `Services/Finance/CallerReadCheck.cs` (internal static class + `CallerReadOutcome`, no interface, no DI registration): `CheckAsync` (one record: `GetCallerRecordAccessAsync` + `HasRequiredRights`, Readable / Refused / Failed, fault without the right = Failed, degraded probe with the right = Readable, cancellation propagates) and `ScanAsync` (walk ordered rows in pages of 50, gate of 8, stop at N readable or M checks, any Failed fails the scan). Parameters: entity set, operation key, rows and id selector, log context. Both `InvoiceContextService` (siblings and the core record) and `InvoiceRelatedWorkService` use it; 107 keeps its identity / token check before querying, as 106 does. Section 11: existing = the two private copies in 106 and 107 (identical loops); extension = the helper replaces both, nothing new in the BFF surface; cost of doing nothing = the next fix to one fail-closed loop would again miss the other (this review found exactly that). ADR-010: not an interface, ceiling unchanged (164 of 165).

**Known limits (K-class), recorded:**

- **K2 side channels (derived; the reviewer's own list was not in the brief and should be added if it differs).** (a) Latency and the `timeout` code vary with the number of documents / sibling invoices the caller cannot read, because each is still checked (bounded by the 200 / 1000 budgets and the deadline). (b) 106: the 1000-check ceiling (`MaxSiblingAccessChecks`) is reached only by a record with more than 1000 sibling invoices of which fewer than 200 are readable among the newest; the aggregate sections then say `too-large`, which reveals that the record has more than 1000 invoices. Raised from 200 so that it needs a record no real firm has. (c) 107: when 200 restricted documents precede readable older ones the answer omits the older readable ones, silently; the same price of not revealing them.
- **K1 latency (to measure live in task 112).** The search service makes per-result metadata reads (as the review put it; not re-verified here), so a documentIds search over up to 50 readable documents plus up to two re-searches may cost more than the deadline on a cold path. Dev has no `sprk_timekeeper` rows, so this cannot be measured now.

Results: focused `InvoiceContext` + `InvoiceRelatedWork` 130 pass (unit and contract projects compile together); `Finance` filter 972 pass, 2 skipped; ArchTests 1056 pass; build 0 warnings, 0 errors.

Mutation proofs (each killed): `truncated` from all candidates (2 tests fail); cap applied before the Read check (3); no extra searches (3); extra-search id list never shrinks (2); 106 `AllExamined` from all rows (1); helper treats a faulted lookup as Refused (3); helper ignores a Failed batch (6).

## Reviewer's K2 side channels (added by the main session, 2026-10-16)

From the 107 adversarial review, alongside the derived list in section 10:
- `related-work.access-check-failed` is reachable only when at least one candidate exists: a Dataverse 429/5xx/timeout on an unreadable document's rights lookup answers `unavailable` instead of `none-in-window`. The caller cannot trigger the fault.
- An all-unreadable window costs up to the check budget of cold rights lookups (8 at a time), so its latency differs from an empty window.
- The no-token branch answers differently depending on whether candidates exist; it is unreachable because `FinanceAuthorizationFilter` refuses a request without a token (403) before the handler runs.
