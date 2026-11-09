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
- The push authorizes a document source as it does any other: Read (and the narrowing for an external caller) decides it, and
  every other outcome is the push's existing uniform 404.
- Which children a push from a document updates (review round 2, F1): the target's lookup to `sprk_document`. When the target
  has several, the push uses the one that files the target row under the document. That is the first in the target table's
  ownership primary order (`RecordOwnershipContext.InPrimaryOrder`), with citations and Secure-only links left out, and it must
  be a column the table's `SecureChildLineage` order declares. For `sprk_analysis` (three lookups: `sprk_documentid`,
  `sprk_outputfileid`, `sprk_regardingdocument`) that is `sprk_documentid`, the analysed document. An analysis that names the
  document only as its output file or as its regarding record is not updated. A target that defines no such column
  (`sprk_billingexport`) is still the existing 409 `field_mapping_parent_lookup_ambiguous`. Every non-document source keeps
  today's behaviour, its 409 included.
- The analysis create's 201 `warnings` member carries every warning of the create (`RecordCreationOutcome.Warnings`), not only
  mapping warnings. This is intended: it is the same member `ChildRecordEndpoints` returns (review round 2, K3).
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
| `FieldMappingEndpoints.DocumentOwnershipParentColumn` (review round 2) | `RecordOwnershipContext.InPrimaryOrder`, `RecordOwnershipResolver.IsCitation` / `IsSecureOnlyParent`, `SecureChildLineage.LookupOrder` | It composes those; changing `ResolveParentLookup` for every source would change other sources' 409 (NFR-07) | A Manual Refresh from a document to an analysis is always the 409 against the real schema |

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

## Verification on the merged tip (30b2e5da34, work branch with 048)

verify-suites: solution build 0 warnings, 0 errors; ArchTests 1940/1940; filtered suites 6836 passed, 0 failed, 7 skipped.
Second filter (Analysis, Event, mapping source, push, chat update/delete, writer-only): 432/432. CVE: no vulnerable packages.

## Review (2026-11-05)

- code-review and adr-check (implementer): no fix-now finding.
- Opus adversarial pass 1: no fix-now defect in the design. It re-derived the recorded decision (written only by
  `AuthorizeCreateAsync`, after every check), the same-document binding, the uniform Unavailable on every non-allowed case,
  field security on the caller read, and NFR-07.

| Finding | Class | Outcome |
|---|---|---|
| The 201 `warnings` member is not shown by the Analysis wizard (`analysisService.ts`) | F2 (tracking) | Acceptance criterion added to task 028 (a34f2c2c03) |
| A route with no document decision still probed the caller's rights on a document source | K1 | Fixed in round 1: `ReadAsync` returns Unavailable before the probe when the source is a document and the route passed no decision (depends only on route and table). Tests: `ReadAsync_ADocumentOnARouteWithNoDocumentDecision_IsTheOneUnavailable_WithNothingAsked`; the seam theory's no-decision case asserts no probe. Failing first: 2 of 193 with the fix reverted; 1041/1041 with it |
| `POST /child-records/sprk_analysis` passes no document decision, so a Document to Analysis profile maps nothing there | K2 | Known limit; task 028 keeps the wizard on `/api/ai/analysis/create` (criterion added) |
| The caller read of a document source fetches the whole row (no `$select`, by #2191) | K1 | Known limit; watch create latency under task 049 |
| Other review items | - | Sent to UAC-r2 by message |

## Known limits

- Only the analysis create passes a document decision; on every other create route a document source maps nothing, with the
  one warning.
- AC2 "document does not exist" is held at the probe level; through the route the filter answers its existing 403 first.

## Release note

A Document to Analysis field-mapping profile now fills the new analysis from its document when the user can read that
document. When they cannot, the analysis is still created and the response says no fields were copied. Manual Refresh on a
document pushes the profile's values to the analyses of that document (those whose analysed document it is) when the user can
read the document. Analyses that name it only as their output file or regarding record are not changed.

## Review round 2 (2026-11-05)

Second independent review. Each finding was checked against the code first.

| Finding | Class | Outcome |
|---|---|---|
| F1: a push from a document to an analysis always got the 409 `field_mapping_parent_lookup_ambiguous`. `sprk_analysis` has three lookups to `sprk_document` (`sprk_Document.xml`); the AC5 double listed one | F1 | Fixed. `ResolveParentLookup` takes the target table; for a document source with several matches, `DocumentOwnershipParentColumn` picks the target's ownership primary (above, Behaviour). No refusal's status, code or wording changed (NFR-07): the 409 still counts every lookup found |
| F3: no test of the same-document binding in `AnalysisEndpoints.MappingDocumentDecisionOf` | F3 | Fixed: nothing recorded gives null; X recorded shows X and not Y; through the route, a filter-allowed document that is not the mapping source gives the one warning, with nothing read app-only |
| K3: the analysis 201 `warnings` carries every create warning | K3 | Kept as intended (matches `ChildRecordEndpoints`); stated under Behaviour |
| K4: `IsDocumentShownAsync`'s null branch is unreachable after round 1 | K4 | Kept as defence in depth, with a one-line comment |

### F1 rule and its reach

The rule is composed from existing definitions only: `RecordOwnershipResolver.IsCitation`, `RecordOwnershipResolver.IsSecureOnlyParent`,
`RecordOwnershipContext.InPrimaryOrder` and `SecureChildLineage.Children[target].LookupOrder`. It is fixed by the target table, never
by the request. Every target in the schema with several lookups to `sprk_document`, and what the push now does
(`DocumentOwnershipParentColumn_IsFixedByTheTargetTable` pins all four):

| Target | Lookups to `sprk_document` | Before | Now |
|---|---|---|---|
| `sprk_analysis` | `sprk_documentid`, `sprk_outputfileid`, `sprk_regardingdocument` | 409 | `sprk_documentid` |
| `sprk_document` | `sprk_parentdocument`, `sprk_canonicaldocument` (Secure-only) | 409 | 409 (unchanged; see the follow-up below) |
| `sprk_invoicesubmission` | `sprk_document`, `sprk_reportdocument` | 409 | 409 (unchanged; see the follow-up below) |
| `sprk_billingexport` | `sprk_csvdocument`, `sprk_jsondocument` | 409 | 409 (not in `SecureChildLineage`) |

The `sprk_document` and `sprk_invoicesubmission` rows keep the 409: the rule is limited to the analysis (follow-up below).

### Tests

| File | Added or changed |
|---|---|
| `FieldMappingPushDocumentSourceTests.cs` | The double now returns all three analysis lookups (and a matter lookup), with a different analysis under each. AC5 positive: the one child query is `_sprk_documentid_value`, and only the two analysed rows are written. New: a document to `sprk_billingexport` push is the existing 409 with the same wording and nothing read or written; the rule's answer for each of the four targets |
| `FieldMappingPushAuthorizationContractTests.cs` | Must-not-fire: a matter source with two lookups on `sprk_analysis` (where the order would name `sprk_regardingmatter`) is still the 409, with the same wording |
| `RecordCreationServiceMappingSeamTests.TypedRoutes.cs` | `MappingDocumentDecisionOf`: null with nothing recorded; X shown, Y not. Route: filter allowed another document, so 201 with the one warning, nothing mapped, nothing read app-only |

### Failing-first evidence

| Change or mutation | Failed / total | Failing tests |
|---|---|---|
| Production F1 change not yet made (tests only) | 2 / 67 (push filter) | AC5 positive, internal and external |
| The document gate removed (every source disambiguated) | 2 / 67 | the new must-not-fire, and the existing ambiguous-409 theory |
| The declared-order requirement removed | 1 / 67 | the `sprk_billingexport` 409 |
| `MappingDocumentDecisionOf` always answers true | 2 / 172 (seam filter) | the helper test and the route test |
| `MappingDocumentDecisionOf` non-null when nothing is recorded | 1 / 172 | the helper test |

The push counts are before the four-target pin was added. Each mutation was restored from a backup copy, and both suites passed after.

### Verification (round 2, branch tip before commit)

verify-suites: solution build 0 warnings, 0 errors; ArchTests 1940/1940; filtered suites 6844 passed, 0 failed, 7 skipped.
Second filter (AnalysisEndpoints, FieldMappingPush, MappingSource, RecordCreationServiceMappingSeam, EventEndpoints, ChildRecord):
939/939.

### Round 2 follow-up (main session)

The round-2 rule also resolved two other targets that had always answered the 409: a document's own lookups
(`sprk_parentdocument`) and an invoice submission's (`sprk_document`). Neither is in the owner's rule (Document to Analysis),
so the rule is now limited to `sprk_analysis` (`FieldMappingEndpoints.DocumentPushTarget`); every other target keeps the
existing 409, unchanged (NFR-07). The theory `DocumentOwnershipParentColumn_IsFixedByTheTargetTable` pins all four targets.

Scoped re-check of round 2: no F. K4 hardened: the rule answers only `sprk_documentid` for the analysis
(`DocumentPushParentColumn`); if the metadata ever lacks it, the push keeps the 409 instead of choosing another document
lookup. UAC-r2: round 2 (`de96647095..495639ce81`) cleared from the access side, no F (2026-11-05).

## Publish size and CVE (final)

Publish delta against master `0f53e053bf` at `495639ce81`: **+1,485 B**, 39.21 MB total. `dotnet list package --vulnerable
--include-transitive`: no vulnerable packages.
