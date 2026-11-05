# Task 043 notes - a document as a field-mapping source

Branch `task/fm-043`. Owner rule (2026-11-04): if the user has access to the document, and their permissions allow them to
create or update the analysis, then the document is a valid mapping source for them. Rigor FULL. Spec FR-03, FR-04, FR-19,
NFR-01, NFR-07; UAC-r2 task 188 (the external document narrowing); ADR-010, ADR-038.

## What changed (production)

| File | Change |
|---|---|
| `Services/Dataverse/FieldMapping/MappingSourceReadProbe.cs` | `TryResolveSourceEntitySet` (the association map first, then `sprk_document` on `FinanceAuthorizationFilter.DocumentEntitySet`), `IsDocumentSource`, `CheckCallerReadOfSourceAsync` (the source readers' Read decision over that set). `ReadAsync` asks the caller's Read on a document, then the route's narrowing answer (`MappingSourceReader.DocumentShown`), before the document is read app-only. |
| `Services/Dataverse/FieldMapping/MappingSourceCallerMask.cs` | Resolves the source's entity set through `TryResolveSourceEntitySet`, so a document's columns are read as the caller and field security masks them as for any table. |
| `Api/FieldMappings/FieldMappingEndpoints.cs` | `AuthorizePushSourceAsync` resolves through `TryResolveSourceEntitySet`; a document source also goes to `InternalPlaneDocumentNarrowing.ForCallerAsync` after Read is allowed. |
| `Api/Filters/AnalysisAuthorizationFilter.cs` | The /create filter's "let through" records the body document (`RecordDocumentAttachAuthorized`), alongside 031's playbook record. |
| `Api/Ai/AnalysisEndpoints.cs` | The create's mapping gets the filter's document decision (`MappingDocumentDecisionOf`); the 201 body carries the create's warnings. |
| `Api/Ai/CreateAnalysisRequest.cs` | `CreateAnalysisResponse.Warnings` (optional, omitted when null). |
| `Api/Events/EventEndpoints.cs`, `Services/RecordCreation/RecordCreationService.cs` | Pass-through of the optional `documentShown` answer (`CallerMappingOf`, `RecordCreationMapping.ForCaller`). |

## Behaviour

- A caller who can read the document (an internal user, or an external caller the narrowing shows it to) gets the Document to
  Analysis profile's values on the analysis create, fill-only, with no warnings member in the body.
- When the caller cannot read the document, or the narrowing does not show it, or either check faults, nothing is copied. The
  analysis is still created, and the body carries the one `SourceUnavailableWarning`. The text is the same in every case, and
  the document is not read app-only.
- The push treats a document source as it treats any other: Read (and the narrowing for an external caller) decides it, and
  every other outcome is the push's existing uniform 404.
- Every refusal on every route is unchanged (NFR-07). The external-hidden and narrowing-fault cases on the analysis create are
  still the filter's existing 403, because `AttachDocumentCheck` decides them before the handler runs.

## Design choices

1. **`CheckCallerReadOfSourceAsync` beside `CheckCallerReadAsync`.** The quick-create filter calls `CheckCallerReadAsync`, so a
   document branch inside it would change that filter's 403 for a document parent (an NFR-07 escalation trigger). The two
   share their second half (`AskReadAsync`).
2. **The narrowing decision is reused, not asked twice (POML step 3).** The /create filter already runs Read + AppendTo and then
   the narrowing on the body document. It records the id in `HttpContext.Items` under a private key (the 031 playbook-record
   pattern), and the handler hands `documentId == recorded` to the mapping. The decision travels through the existing
   `RecordCreationRequest.Mapping` shape, so the escalation trigger at POML line 87 does not fire.
3. **Other create routes pass no document decision.** A document source there still maps nothing, as before this task. Only the
   analysis create has a Document to Analysis profile today.
4. **Warnings are strings.** `RecordCreationOutcome.Warnings` is `IReadOnlyList<string>`, and `ChildRecordEndpoints` returns
   it as `warnings: string[]` only when it is non-empty. The analysis body follows that shape, so `CreateAnalysisResponse` stays
   public (AC6's "RecordCreationWarning list" is that string list).
5. **The association lockstep map is not touched.** `EntityAccessFilter.TryResolveEntitySet` still has no document entry, so
   upload, record-route and Compose authorization are unchanged (`AssociationTypeLockstepTests` green, unchanged).

## Component justification (CLAUDE.md §11)

| Surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `MappingSourceReadProbe.TryResolveSourceEntitySet` (+ `IsDocumentSource`) | `EntityAccessFilter.TryResolveEntitySet` (the association map); `FinanceAuthorizationFilter.DocumentEntitySet` | Extending the map would add a document to upload, record-route and Compose authorization (lockstep with the association types); a wrapper keeps the change to the three mapping-source readers | The three readers answer `EntityTypeNotAuthorizable` for `sprk_document`, so a Document to Analysis profile never maps and a push from a document is always 404 |
| `MappingSourceReadProbe.CheckCallerReadOfSourceAsync` | `CheckCallerReadAsync` | Changing it would change the quick-create filter's refusal for a document parent (NFR-07) | The create-time read cannot ask Read on a document |
| `AnalysisAuthorizationFilter.RecordDocumentAttachAuthorized` / `AuthorizedDocumentId` | `RecordPlaybookUseAuthorized` / `AuthorizedPlaybookId` (031) | Same pattern, separate key: a different decision | The handler would ask the narrowing a second time (POML step 3) or map with no narrowing answer |
| `AnalysisEndpoints.MappingDocumentDecisionOf` | none (`grep MappingDocumentDecision` - no matches before this task) | A private lambda inline would hide it from the seam tests | Same as the row above |
| `MappingSourceReader.DocumentShown`, the `documentShown` parameters | none | Optional parameters on the existing factories; no new type | The route's decision cannot reach the service's mapping step |
| `CreateAnalysisResponse.Warnings` | `ChildRecordEndpoints`' `{ id, warnings }` | Additive optional member | The caller is not told that nothing was copied (AC6) |

No new endpoint, DI registration, interface, package or background work (ADR-010).

## Tests (KEEP paths only)

| File | Added |
|---|---|
| `tests/integration/auth/FieldMapping/MappingSourceReadProbeTests.cs` | The document read order (probe on `sprk_documents`, narrowing, read); not shown / narrowing fault / no decision -> Unavailable with no read; a Default-only profile; cannot read -> Unavailable before the narrowing; a missing and a denied document give identical results after the same work; probe fault; a non-document source never asks the decision; `TryResolveSourceEntitySet` agrees with the map on every supported type and adds only the document (AC7); the quick-create `CheckCallerReadAsync` still answers `EntityTypeNotAuthorizable` for a document (NFR-07). |
| `tests/integration/auth/FieldMapping/MappingSourceCallerMaskTests.cs` | AC4: a document is read as `sprk_documents({id})` and a secured column is not copied. |
| `tests/integration/seam/RecordCreation/RecordCreationServiceMappingSeamTests.TypedRoutes.cs` | AC1 (the 031 test flipped: the profile maps, fill-only, as the caller, no warnings); AC2 (cannot read / probe fault: byte-identical 201 body with the one warning); AC3 (not shown / narrowing fault / no decision: nothing mapped, one warning; shown: AC1's outcome). |
| `tests/integration/contract/Api/Ai/AnalysisEndpointsAuthorizationContractTests.cs` | Through the real filter and service: internal and external-shown map with no warnings member; no Read on the document -> 201 with the warning; external-hidden and narrowing fault -> the existing 403 with nothing written. |
| `tests/integration/contract/Api/FieldMappings/FieldMappingPushDocumentSourceTests.cs` (new) | AC5: internal and external-shown push from a document (200, 2 updated); no Read / external-hidden / narrowing fault -> the unknown-source 404 body, nothing read or written. |
| `tests/integration/contract/Api/FieldMappings/FieldMappingPushAuthorizationContractTests.cs` | Host hooks the new file needs (`SeamProbe.RightsOn`, `PushHost.StartAsync(configure)`); no assertion changed. |
| `tests/Spaarke.ArchTests/RouteAuthorizedLookupsCallSiteTests.cs` | `OnlyTheCreateFilterRecordsTheDocumentDecision`: the document record is written only by `AuthorizeCreateAsync`, as 031 pins the playbook record. Beyond AC1-AC7 because the reuse in design choice 2 is only safe while nothing else can write that record. |

AC2 "document does not exist": held at the probe level (missing vs denied, identical results after the same work). Through the
route, a missing document is the filter's existing 403 before the handler runs, so the seam theory covers cannot-read and
probe-fault only.

### Failing-first evidence

| Revert | Failed / total | Failing tests |
|---|---|---|
| A: no document branch in `TryResolveSourceEntitySet` | 19 / 328 | the probe, mask, seam AC1/AC2/AC3, contract AC1/AC3 and push AC5 positive tests |
| Seeding proof (POML): narrowing call removed from `ReadAsync` | 8 / 328 | probe document-allowed, Default-only, 3 probe not-shown, 3 seam AC3 |
| B+D+E: push narrowing off, filter record removed, body warnings null | 7 / 328 + 1 / 6 ArchTests | contract AC1/AC3 (x2) and AC2/AC6, seam AC2 (x2), push external-hidden / external-narrowing-fault, the ArchTest |

Each revert was restored from a backup copy, and every suite passed after the restore.

## Verification

See the commit message and the final report for counts, publish delta and CVE (`dotnet list package --vulnerable
--include-transitive`: no vulnerable packages).

## Coordination

- 049 (task/fm-049): `RecordCreationService.cs` changed only at `RecordCreationMapping.ForCaller` (end of file), not step 5 or
  the post-create grant.
- 048 (unmerged): touches `FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync`; this task changes `AuthorizePushSourceAsync`,
  a different region. Expect a clean merge.

## Release note

A Document to Analysis field-mapping profile now fills the new analysis from its document when the user can read that
document. When they cannot, the analysis is still created and the response says no fields were copied. The Manual Refresh push
works from a document on the same terms.

## Rework

None after review: code-review and adr-check found no fix-now finding.
