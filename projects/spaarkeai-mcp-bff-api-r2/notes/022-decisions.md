# Task 022 — search tools (spaarke_search_documents, spaarke_find_records): decisions and notes

Date: 2026-11-25. Branch: agent worktree (wave 5). Built on task 027 (the shared search access methods). Two read-only tools on
`/mcp/legal-operations`, plus the #3212 fix on the document search REST route.

## What was added

| Piece | Where |
|---|---|
| `spaarke_search_documents` | `src/server/api/Sprk.Bff.Api/Api/Mcp/LegalOperations/Search/SearchDocumentsTool.cs` |
| `spaarke_find_records` | `.../Search/FindRecordsTool.cs` |
| Shared argument, fault, text and size helpers | `.../Search/SearchToolSupport.cs` |
| `McpText.SingleLine(string?)` (shared helper; see D-022-text) | `src/server/api/Sprk.Bff.Api/Api/Mcp/McpText.cs` |
| #3212 fix + two stable codes | `Api/Ai/SemanticSearchEndpoints.cs`, `Models/Ai/SemanticSearch/SearchErrorCodes.cs` |
| Ledger rows (two) | `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Mcp.Search.cs` |
| Tool-set pins (both) | `tests/integration/contract/Api/Mcp/McpHostContractTests.cs`, `tests/integration/auth/Mcp/McpCallerPathTests.cs` (Test5 dictionary) |
| Tests | `tests/integration/contract/Api/Mcp/Search/`, `tests/integration/auth/Mcp/Search/`, `tests/integration/regression/Issue3212_SemanticSearchFailureDetailTests.cs` |

Both tools call ONLY 027's public methods (`SemanticSearchAuthorizationFilter.SearchDocumentsAsCallerAsync`,
`RecordSearchAuthorizationFilter.SearchRecordsAsCallerAsync`). No access check is copied; `SearchAndAuthorizeResultsAsync`,
`DecideCount` and `SearchAndAuthorizeRowsAsync` are not called. The REST routes run the same implementation, so for the same
caller and query the hit ids and their order are the REST routes' (proved against the real routes in
`SearchToolsContractTests`, scope=all, scope=entity and records).

## Decisions

### D-022-counts — no total, no count, no "N more" (#1533)

The entity-scope total (`SemanticSearchMetadata.TotalResults`) counts documents the caller may not read (#1533), so neither tool
exposes `TotalResults`, a count, or anything derived from one. `hasMore` is true when the page came back full
(`response.Results.Count >= limit`). The hit rows are the whole answer. Test: the stub index reports a total of 4242 and the
tests assert 4242 never appears in `content` or `structuredContent`, and that `hasMore` follows page fullness. Revisit when #1533
is fixed.

### D-022-bounds — limit and offset are refused, not clamped

`query` is required, trimmed and at most 1000 characters (`RecordSearchRequest.MaxQueryLength`). `limit` must be a whole number
1..50 (default 20) and `offset` a whole number 0..1000 (default 0), exactly the REST handlers' bounds. Out of range or not a
whole number: a 400 tool error with the REST code (`INVALID_LIMIT` / `INVALID_OFFSET`), never a silent clamp. This is the REST
behaviour, so parity holds. The argument set is closed: an unknown name is `mcp.invalid_argument`.

### D-022-delegates — the index delegates live in `SearchToolSupport`

The MCP-tool ArchTest forbids `Services/Ai/**` type tokens in a tool class (ADR-013). The two index delegates need
`ISemanticSearchService` / `IRecordSearchService`, so `SearchToolSupport.DocumentIndex(HttpContext?)` and `RecordIndex(...)` resolve
them lazily from `http.RequestServices` and return the `Func<...>` the shared methods take. The tool classes name no AI type.
The caller check runs first, so nothing is resolved without a signed-in caller.

### D-022-faults — fixed text, stable code, never `ex.Message`

The shared-method call and the delegate sit in `try/catch (Exception) when (!ct.IsCancellationRequested)`. A failure (a
`SdapProblemException` included) becomes isError, 500, code `SEMANTIC_SEARCH_FAILED` / `RECORD_SEARCH_FAILED`, text "The search
could not be completed right now. Try again shortly.". The log line carries the exception TYPE and the correlation id only,
never the query, a token or the message. A caller cancellation propagates. Typed refusals map to the REST reasonCode with fixed
text (401 sign-in; 403 uniform "You can't search that record. It may not exist, or you may not have access to it." so an
unreadable and a missing record read the same, FR-15; 400 invalid scope; anything unexpected fails closed to 500).

### D-022-3212 — REST fix for the search 500 detail (the only REST change)

`SemanticSearchEndpoints` `Search` and `Count` answered a 500 whose `detail` was the exception's message (#3212, found in 027).
Now: `Search Failed` / `Count Failed` with a fixed detail and `code` = `SEMANTIC_SEARCH_FAILED` / `SEMANTIC_SEARCH_COUNT_FAILED`
(mirroring `RecordSearchEndpoints` / `RECORD_SEARCH_FAILED`); the exception is still logged. Regression test
`Issue3212_SemanticSearchFailureDetailTests` (2 tests) FAILED before the fix (detail == the exception text) and PASSES after:
proved by restoring the HEAD version of `SemanticSearchEndpoints.cs`, running, then restoring the fix (copy-away, no stash).

### D-022-text — model-visible output (ADR-015) and `McpText.SingleLine`

Rows are slim: documents `id, name, fileType, documentType, parentType, parentName, snippet, openLink`; records `id, name, type,
snippet, referenceNumbers (<= 3, <= 40 chars), openLink`. Not exposed: score, organizations, people, keywords, descriptions,
SPE pointers, totals. Snippets are the first highlight (else the summary), markup stripped, whitespace collapsed, <= 200
characters. Every user-authored value written into a text line goes through the shared `McpText.SingleLine` (control characters
and Unicode line/paragraph separators become spaces; null becomes ""), so a title containing "\n- " cannot forge a second row.
`McpText.cs` was not on this branch, so it was created with exactly the coordinator's signature and behaviour; the file is meant
to be identical to the other branch's at merge. No private TextSafe copy. Forged-row tests: one document and one record whose
title contains "\n- " yield one row each; `McpText` unit cases cover each control and separator class.

Open links: the index leaves `FileUrl`/`RecordUrl` null, and no REST URL exists for these, so the link is
`{Dataverse environment URL}/main.aspx?pagetype=entityrecord&etn=<table>&id=<guid>` (table `[A-Za-z0-9_]` only, id a GUID, else
no link), the same environment URL the other tools use (`DataverseOptions.EnvironmentUrl`).

### D-022-size — under 64 KiB at 50 hits (NFR-02)

`Fit` keeps rows while the JSON bytes plus the text lines stay under a 56 KiB row budget (the envelope is far below the 8 KiB
left) and sets `truncated` when it drops rows. Tests: 50 maximum-size hits stay under 64 KiB; 50 realistic hits are all returned.

## Placement and component justification (CLAUDE.md §10, §11)

1. **Existing** — overlaps the REST search routes and the 027 shared methods; the tools are thin adapters over them (no check is
   copied) in the same folder family as `Api/Mcp/LegalOperations/Documents/` and `.../Playbooks/`.
2. **Extension** — extending an existing tool does not fit (different questions: search vs list/get); extending the REST routes
   would add a second contract. `McpText.SingleLine` is the coordinator's shared helper (D-022-text).
3. **Cost of doing nothing** — Copilot and the Decision wizard cannot search documents or records as the signed-in caller
   (spec FR-02), and a model-visible 500 would leak exception text (#3212).

Placement: BFF, inside the existing `/mcp/legal-operations` host (ADR-001/004: no new host, no background work). No new package,
no new DI registration beyond the two `McpToolDefinition` classes the module scans.

## Verification

- BFF build: 0 warnings, 0 errors.
- New tests: `SearchToolsContractTests` (23 test methods; parity for scope=all, scope=entity and records; slim shape; no counts;
  `hasMore`; size; forged rows; bounds; faults incl. `SdapProblemException`, timeout and caller cancellation; no query in logs),
  `SearchToolsMustNotLeakTests` (6 methods; trimmed document, unreadable parent, uniform refusal with no index call, unreadable
  record, no-caller x2, missing token / tenant), `Issue3212_SemanticSearchFailureDetailTests` (2).
- Pins: `McpHostContractTests` tool-list pin and `McpCallerPathTests` Test5 static-schema dictionary hold both tools in strict
  real-host order.
- Focused run (`SearchTools|Issue3212|McpHostContractTests|McpCallerPathTests`): 90 passed, 0 failed.

- Related suites (`Mcp|SemanticSearch|RecordSearch|SearchAuthorization|Issue3212`): 701 passed, 1 skipped (pre-existing), 0 failed.
- ArchTests (`tests/Spaarke.ArchTests`, whole project, includes the MCP tool rule, the ledger, ADR-013 and 027's phase-caller guard):
  2853 passed, 0 failed.
- Format: `dotnet format whitespace --folder --verify-no-changes` on every changed file: clean (the commit hook also formatted them).
- **CVE.** `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api`: no vulnerable packages. No package was added.
- **Publish size** (ADR-029): `dotnet publish -c Release src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -o deploy/api-publish`,
  framework-dependent, Release; zipped with `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, PDBs included;
  each from a fresh short-path worktree (`C:\wt022m` master `9a910c573b`, `C:\wt022p` the base `bb1abdafa2`, `C:\wt022y` this
  commit `b62586fe4a`), measured the same day, then removed with `Remove-WorktreeSafe.ps1`.

  | Build | Compressed | Folder | Files |
  |---|---|---|---|
  | master `9a910c573b` | 43,607,060 bytes (41.59 MB) | 144,513,071 | 194 |
  | base `bb1abdafa2` (before this task) | 43,210,850 bytes (41.21 MB) | 143,515,707 | 186 |
  | `b62586fe4a` (this task) | 43,224,423 bytes (41.22 MB) | 143,561,287 | 186 |

  Delta of this task: +13,573 bytes compressed (+0.013 MB), +45,580 bytes in the folder, 0 files. Total 41.22 MB: below the 5 MB
  per-task justification line and the 55 MB review line. (The branch is behind master by 8 files; that gap is other projects' work.)
- **Conformance host** (`tests/integration/Mcp.Conformance.Host`, `run-conformance.sh`): built (widget included) and run with both tools registered
  in the real BFF: "Baseline check passed: all failures are expected", exit 0.

## Inline code-review and adr-check

ADR-001/004 (one Minimal API host, no background work), ADR-008 (the shared methods own the access decision), ADR-013 (no
`Services/Ai/**` type in a tool class; the delegates are in the support class), ADR-015 (slim rows, no counts, nothing about the
caller in logs), ADR-019 (fixed text plus stable code; #3212 fixed), ADR-028 A10 (no caller means `UserContextRequired`, no
app-only fallback; the caller check precedes any resolution), ADR-029 (placement, publish delta above), ADR-038 (every test on a
KEEP path: contract, auth, regression; no mocked `HttpMessageHandler`; substitution only at module boundaries). Findings: none open.

## Process note

One slip: a Bash command carried a `python` call (the wave rule forbids it). It hung, was stopped with TaskStop and confirmed gone;
no other python process of this session exists. No output of it was used.

rework: none task 022
