# Task 023 - document tools (spaarke_list_documents, spaarke_get_document): notes and decisions

Decisions go in this file, not in the shared `decisions.md` (wave rule: parallel tasks do not edit shared notes).

## Decisions
- D1 List authorization is parent-level (Read on the matter, once), not per document. The REST list route
  (`GET /api/v1/documents?containerId=`) is keyed by container, has no caller in the repository and cannot serve the attachment picker
  (task 011's D4 asks by `regardingEntityType` + `regardingId`). The tool asks the two questions that route asks, of the matter it is
  given: Read on the matter as the caller (`RecordRouteAccessAuthorizationFilter.DecideRouteRecordAsync`, the one fixed-entity-set
  decision the route filters and the events capability use), then the external document narrowing once over the rows
  (`InternalPlaneDocumentNarrowing.ForCallerAsync`). Per-document Read per row was rejected: 50 sequential caller-rights reads pass the
  NFR-01 read budget (p95 3 s). Per-document Read is asked when a document is opened (`spaarke_get_document`) and, for 040, when it is
  attached to a send.
- D2 `spaarke_get_document` makes the routes' own per-document decision through `InternalPlaneDocumentNarrowing.AuthorizeDocumentsAsync`
  (Read as the caller, then the external narrowing). It is the one document-resource decision outside `DocumentAuthorizationFilter`,
  and the ArchTest `EveryDocumentResourceGrant_GoesThroughTheNarrowingHelper` keeps every other site on it. No check is copied.
- D3 Extraction (POML step 3): the link steps of `GET /api/documents/{id}/preview-url` and `/open-links` moved unchanged into
  `FileAccessEndpoints.MintPreviewUrlAsync` and `ResolveOpenLinksAsync` (internal static; logs and `SdapProblemException`s identical).
  The routes call them. The frozen-bytes test `FileAccessLinkRoutesFrozenBytesTests` (bodies captured BEFORE the extraction) passes
  before and after. The open-links route's last step (a just-in-time writer role on a secure container, `editAccess.PrepareAsync`)
  stays in the route: the tool is read-only and does not do it.
- D4 Uniform not-found: forbidden, narrowing-hidden, missing document and missing or unreadable matter all return
  `McpToolError("sdap.access.deny.record_unavailable", "The requested record was not found.", 404)` (the REST uniform not-found,
  `ProblemDetailsHelper.RecordUnavailableReasonCode`), naming no id. Escalation trigger checked and NOT fired: the REST routes answer
  403 for both a forbidden and an unknown document id (Dataverse grants no rights on a row that does not exist), so a uniform result
  contradicts no documented contract.
- D5 `linksError`: when the links cannot be made (no file attached, pointer container refused, no preview, Graph fault) the tool still
  returns the metadata and `linksError { reasonCode, message }` (the REST problem's code and title, or `mcp.document_links_unavailable`
  for a non-REST fault), never a whole-call failure. `links` and `linksError` are never both set.
- D6 `hasMore` on the list is true when the service returned its row limit (50): `GetDocumentsByLookupAsync` has TopCount 50 and no
  ordering, so a matter with more documents is silently cut by the source (see defer D-023-1). `ListDocumentsTool.ServiceRowLimit` must
  stay equal to that TopCount.
- D7 For task 040's same-matter check: use the document's own `MatterId` from `IDocumentDataverseService.GetDocumentAsync`
  (`DocumentEntity.MatterId`), not membership in this tool's list (the list is capped at 50 by the source).
- D8 Result size (NFR-02): a name is clipped to 400 JSON bytes (`DocumentToolSupport.NameBytes`; Dataverse names reach 850 characters
  and the default encoder writes a non-ASCII character as up to 6 bytes). 50 rows of 850-character non-ASCII names stay under 64 KiB
  (test `List_AtTheServiceLimit_With850CharacterNonAsciiNames_...`).
- D9 Shared-file edits (kept minimal): two tool-set expectation tests list the served tools exactly and now include the two new
  tools: `tests/integration/contract/Api/Mcp/McpHostContractTests.cs` (the names list; the redundant `HaveCount(5)` removed, the
  list assertion already checks the count) and `tests/integration/auth/Mcp/McpCallerPathTests.cs` (the static-schema tools' argument
  pin). Sibling wave tasks (022, 024, 026) add their tools to the same two lists, so the merge into the work branch needs a union of
  those lines.

## Test scope (ADR-038; the stated contract plus named edge cases)
Contract (over the MCP route): tools/list metadata; list = source ids newest first in the D4 shape; no pointer/summary/description;
50 rows under 64 KiB; empty matter; extra argument; wrong type / bad id; get = source metadata + the REST links; no file ->
`linksError`; forbidden; forbidden vs unknown byte-identical; missing / extra / non-GUID id. Auth (handlers direct): forbidden vs missing
document, narrowing-hidden, narrowing fault (fail closed), token only to the authorization service, no caller (null context and
unauthenticated, nothing read); forbidden vs missing matter, list hides narrowed documents, list on a narrowing fault, no caller.
One-line justifications beyond the criteria: the extra shared fakes file `DocumentToolsWorld.cs` (both test files need the same module-
boundary world; Moq at IDocumentDataverseService / IAuthorizationService boundaries only, no HttpMessageHandler or IServiceClient mock);
the 400-byte name clip (the NFR-02 case needs an input that would otherwise pass 64 KiB); the narrowing-fault cases (fail-closed is the
security property the helper owns, one case each per tool).

## Verification
- Frozen-bytes REST tests (`FileAccessLinkRoutesFrozenBytesTests`, 6): pass before and after the extraction.
- New tests: 22 (13 contract, 9 auth). Mutation proof: with the get tool's denial changed to a distinct 403, 2 of them fail.
- ArchTests: 2848 passed (the two ledger rows included).

rework: none task 023

## Verifier-finding fixes (round 2)
- D-023-preview: preview link withheld from model-visible output (fail-closed); a widget that needs preview gets it through an app-only
  path in a later task; owner may reverse. `spaarke_get_document` no longer calls `FileAccessEndpoints.MintPreviewUrlAsync`, and neither
  its text nor `structuredContent.links` carries a preview or embed URL (links = `desktopUrl`, `webUrl` only). Reason: the preview URL is
  minted app-only, outlives the request, works without a Dataverse check and is model-visible (NFR-07, ADR-015). The REST routes are
  unchanged (`FileAccessLinkRoutesFrozenBytesTests`). Supersedes the preview parts of D3 and D5 for the tool.
- D-023-fault: an unexpected fault in the authorization or Dataverse reads of either tool is an `isError` `document.unavailable` (503) with
  fixed text and the correlation id (the model-visible text carries no exception text; the log carries the exception type only). The
  catch is `when (!cancellationToken.IsCancellationRequested)`: an `HttpClient` timeout (`TaskCanceledException`) is mapped, the caller's
  own cancellation propagates. An open-links fault (SDAP problem or other) still returns the metadata with `linksError`.
- D-023-empty-text: the list says "No documents are filed under this matter." only when the source returned no rows; when rows came back
  and none survived the narrowing (or it failed closed), or the list may be cut, it says "No documents you can see are filed under this
  matter."
- D-023-names: names withheld until the shared mapping is fixed. A deviation from the POML's model-visible "regarding matter name" field.
  Filling DocumentEntity.MatterName/ProjectName changes POST /api/ai/search (associatedOnly parentEntityName), GET
  /api/ai/visualization/related hub label and the index parent stamp, so it is filed as a separate issue for an owner/uac-r2 decision
  (defer-issues.md D-023-3). Until then `regarding` (list) and `regardingMatter` / `regardingProject` (get) carry `entityType` and `id`
  only, and the get text has no "Filed under ..." line. `DocumentToolsWorld` no longer seeds a name, so the double gives what production
  gives; `BothTools_NeverCarryAMatterOrProjectName_EvenWhenTheSourceRowHoldsOne` seeds names on purpose to prove none is passed through.

## Fix re-check (main session, 2026-11-25; scoped sonnet pass over the fix diff 7c48cb304b)

- No F-class findings. Existence oracle: none (both 503 sites follow an allowed authorization decision).
- K3 fixed by the main session: the empty-list text is now one neutral line, "No documents to show under this matter.", for an empty matter, a fully narrowed list and a narrowing that failed closed. Separate lines would tell an external caller that hidden documents exist. This supersedes D-023-empty-text. Still open: `hasMore` is computed before narrowing, so a fully hidden page of 50 shows count 0 with hasMore true. That is pre-existing and belongs with the 50-row cap issue, #3206.
- T1 fixed: a test for an HttpClient timeout (TaskCanceledException) in the links step, which keeps the metadata and sets linksError.
- K1 (recorded): a production `AuthorizationService.AuthorizeAsync` fault is caught inside that service and answered as a deny, so the get tool's authorization-fault 503 is reachable only with a throwing double. In production a rights-read fault answers the uniform not-found, which fails closed. The 503 covers the row read.
- K2 (recorded, kept): the fault log carries the exception type only, by the D-023-fault choice (NFR-05). A programming error inside the read step therefore shows as a retryable 503 with no stack in the tool log; `GetDocumentAsync` logs its own exception before rethrowing.

## Second-review fix (025 verifier, 2026-11-25)

- `DocumentToolSupport.LineOf` now writes the document name and the file type (the extension of a user-chosen file name) through `McpText.SingleLine`, the shared one-line rule (`Api/Mcp/McpText.cs`, moved out of `ListPlaybooksTool`). A name holding a line break could start a row of its own in the text of `spaarke_list_documents` and pass for another document. Structured content keeps the name as it is. Test: `List_ANameWithALineBreak_IsOneRowOfTheText_AndTheStructuredNameIsUnchanged`.
