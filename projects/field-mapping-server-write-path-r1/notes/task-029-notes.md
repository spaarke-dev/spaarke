# Task 029 notes — Document Upload and Summarize Files on the server write path

## Scope (owner decision, 2026-11-25: option B)

The POML covered the Document Upload wizard. The Summarize Files wizard was a separate create path (`POST /api/v1/documents {name, containerId}`, with the container read client-side from the user's business unit). The escalation trigger fired, and the owner chose to move Summarize onto the BFF child-records route as well.

## What changed

- **Document Upload**:
  - The `sprk_document` create sends no `sprk_searchindexname` and no `sprk_AI_Search_Index` bind.
  - The `rag/index-file` body has no `searchIndexName`; the server routes by content type (FR-40, task 041).
  - The client search-index resolver is deleted.
- **Summarize Files**: creates `POST /api/v1/child-records/sprk_document {sprk_documentname}`. The client no longer reads a business-unit container.

## Deviation: Upload always names its mapping source

POML step 3 said to pass `mappingSource` only when the filing parent differs from the intended source. The wizard passes the filing parent every time, because the server infers a source only for the tables in `ChildRecordEndpoints.InferMappingSourceTables` (`sprk_matter`, `sprk_project`). `sprk_document` is not in that set, so without the query a document would map nothing.

- The server comment above that set now says that Document Upload and the Event wizard name their source.
- A later task that adds `sprk_document` to the set, or removes the query parameter, must keep one or the other.
- **Server effect** of the source the wizard names (`RecordCreationService.Mapping.cs`):
  - a table with no profile maps nothing;
  - an unreadable source gives a warning, never a refusal;
  - only a stripped secure-source value that is a filing lookup is refused (existing behaviour).
- The server comment edit is comment-only. It adds no endpoint, service, registration or package, so no publish-size measurement is needed (`.claude/rules/bff-hygiene.md` item 4 applies to additions).

## Review (Step 9.5): one adversarial pass, Opus

| # | Class | Finding | Outcome |
|---|---|---|---|
| 1 | F1, introduced | The upload decorator returned only the id, so the route's mapping warnings were dropped | Fixed: warnings are carried through to the per-file result and shown |
| 2 | F1, pre-existing (since UAC-r2 task 166) | `withBffChildCreates` had no `deleteRecord`, so a failed file attach left a fileless row | Fixed: the decorator deletes through `DELETE /api/v1/documents/{id}` |
| 3 | F1, pre-existing | Summarize creates documents with no file, and the analysis then reads the "No file content" placeholder | Filed as DI-042 / #3156 (the upload pipeline is outside FR-16's scope) |
| 4 | F2, introduced | The always-name-the-source decision was not recorded, and the server comment misled | Fixed: this note plus the comment |
| K1 | Known limit | NFR-05 timing of ≤10 parallel creates on the child-records route | Observe in the live check |
| K2 | Known limit | The old route set `statuscode=1`; the new route takes the table default | Check in the live check |
| K3 | Known limit | A legacy matter with an empty secure flag counts as flagged, so its mapped values are stripped with a warning | Existing server behaviour |
