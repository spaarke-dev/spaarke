# Task 059: OfficeService's optional constructor params, and the search extraction

> **Date**: 2026-10-01 · **Rigor**: FULL (sonnet / high, run on Opus) · **Mode**: directional

## 1. What changed

| Change | Detail |
|---|---|
| **Optional ctor params made REQUIRED** | `emailUploadCapture`, `genericEntityService`, `callerSystemUserResolver`. `dataverseClient` and `impersonatedQuery` moved to the new service, required there |
| **Dead null branches deleted** | Search stub fallback (`_dataverseClient is null` → hard-coded fixtures) and its `GenerateStubResults` generator; the seam half of the search forcing function; matter types `null` → empty list; record-type lookup `null` → `null`; quick-create and To Do `_genericEntityService is null` → `null`; email capture `is not null &&`; job creator `_callerSystemUserResolver is not null` wrapper |
| **`OfficeSearchService` extracted** | `Services/Office/OfficeSearchService.cs`: entity search, matter types, and the To Do writer's `sprk_recordtype_ref` lookup. Concrete, scoped, unconditional in `OfficeModule`. `OfficeService`'s `IOfficeService` search members delegate to it |
| **ADR-010 misreading corrected** | The dispatcher comment said "ADR-010 — no new registration". ADR-010 asks for concrete registrations inside feature modules; it forbids nothing of the kind. Comment rewritten |
| **`IOfficeService`** | **No member or signature changed.** One doc line on `CreateTodoAsync`'s return was corrected: it named "no generic-create dep injected", which can no longer happen |

## 2. Premise check (primary criterion)

`grep "new OfficeService("` over every `*.cs` in the repository → **0 hits**. The "bare test constructions" that every
optional param cited do not exist; the test hosts resolve `OfficeService` from the real container.

Every one of the five is registered **unconditionally** in production:

| Dependency | Registered in | Composed by `Program.cs` |
|---|---|---|
| `EmailUploadCaptureService` | `CommunicationModule.cs:146` | `AddCommunicationModule`, unconditional |
| `DataverseWebApiClient` | `SpeAdminModule.cs:71` | `AddSpeAdminModule`, unconditional |
| `IGenericEntityService` | `GraphModule.cs:97` | `AddGraphModule`, unconditional |
| `IImpersonatedCommunicationQuery` | `CommunicationModule.cs:279` | as above |
| `ICallerSystemUserResolver` | `AnalysisServicesModule.cs:974` + `TryAdd` in `CommunicationModule.cs:280` | as above |

Every Office test host that `RemoveAll`s one of them adds a double back
(`OfficeEntitySearchAuthorizationContractTests:281-291`, `OfficeMatterTypeLookupContractTests:90-91`,
`OfficeQuickCreateContractTests:695-702`), so no test relied on the null branches.

## 3. Constructor count, reconciled with task 068

| | Params | Of which optional |
|---|---|---|
| Before (`803eb7ede`; this file is identical to master) | **21** | 8: `emailUploadCapture`, `dataverseClient`, `genericEntityService`, `scopeFactory`, `documentProfileAi`, `appLifetime`, `impersonatedQuery`, `callerSystemUserResolver` |
| **After 059** | **20** | 3: `scopeFactory`, `documentProfileAi`, `appLifetime` |
| After 068 (projected) | 17 | 0 |

The POML counted **six** optional params. Tasks 062 (`impersonatedQuery`) and 067 (`callerSystemUserResolver`) added two
after it was written.

**The dispatcher's three are deliberately left to 068.** They exist only to hand-build `OfficeProfileDispatcher`, which
068 deletes, moving the profile onto the job queue. Making them required here would change three params that 068 removes
anyway. That also settles the POML's `deps: 058,068` against the owner's 2026-09-30 order (058 → 080 → 059 → 060 →
068): no parameter is changed by both tasks, so neither task double-counts the other.

**Why the count drops by only one.** The extraction removes `dataverseClient` and `impersonatedQuery` from
`OfficeService` and adds `OfficeSearchService`. It drops by one, not by zero, only because the To Do writer's
`sprk_recordtype_ref` lookup moved too (§4). That lookup was the last thing keeping the app-only read client in
`OfficeService`.

## 4. The extraction: why it was done, and its boundary

The POML made the extraction optional ("skip it if the class is coherent without it"). It was done for two reasons:

1. **A distinct reason to change.** Search changes with the picker (ranking, impersonation, and now task 084's per-row
   filing affordance). The save, job and To Do writers change with the write-path invariants. Without the extraction,
   084 would add `CallerRecordAccessProbe` to `OfficeService`'s constructor; now it lands in `OfficeSearchService`.
2. **A clean boundary.** `OfficeService` now issues **no Dataverse read of its own**: reads go through
   `OfficeSearchService`, writes through persistence and `IGenericEntityService`.

**One deviation from the POML's member list.** `ResolveRegardingRecordTypeIdAsync` (the To Do writer's lookup) moved as
well. It shares `GetJsonString` and the app-only client with the matter-type list, and it is the same kind of read: one
row from a small reference table. Leaving it would have kept `DataverseWebApiClient` in `OfficeService` purely for that
lookup. It moved verbatim.

**Moved verbatim:** `SearchEntitiesAsync`, `QuerySearchEntityAsync`, `EntitySearchMeta`, `_searchMeta`, `MapSearchRow`,
`GetJsonString`, `GetEntityTypesToSearch`, `GetLogicalName`, `GetMatterTypesAsync`, `MapMatterTypeRow`,
`ResolveRegardingRecordTypeIdAsync`. `GenerateStubResults` was **deleted**, not moved: its only caller was the dead stub
branch, and that branch's own comment said "task 059 owns its removal".

## 5. Behaviour preservation

**Production behaviour is unchanged.** Every deleted branch required a dependency that DI always supplies.

**Two observable differences, both in logging only:**
- **Log category.** Search, matter-type and record-type-lookup log lines now carry the category
  `Sprk.Bff.Api.Services.Office.OfficeSearchService` instead of `…OfficeService`. Their message text is unchanged.
  An Application Insights query filtering these lines by the old category must be updated.
- **One log line reworded.** The search forcing function's error log no longer reports "seam present", because the seam
  can no longer be absent. It now reads "…reached OfficeSearchService without a caller systemuserid". The exception
  message is unchanged.

**Tests.** The POML says the search tests must pass *unmodified*, and also that `MapSearchRow`, `MapMatterTypeRow` and
`EntitySearchMeta` move. Both cannot hold literally: two unit tests name those members through their class. The
resolution was to change **the qualifier only** (`OfficeService.` → `OfficeSearchService.`) and leave every assertion
byte-identical:

| Test file | Lines changed | Assertion changes |
|---|---|---|
| `tests/unit/Sprk.Bff.Api.Tests/Services/Office/OfficeEntitySearchMappingTests.cs` | 7 | 0 |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Office/OfficeMatterTypeMappingTests.cs` | 8 | 0 |

Every HTTP-level test passed with **no edit at all**: `OfficeEntitySearchAuthorizationContractTests` (8),
`OfficeMatterTypeLookupContractTests`, `OfficeTodoRegardingContractTests` (which exercises the moved record-type
lookup), `OfficeQuickCreate*`, `OfficeTodoSourceAuthorizationContractTests`, `CommunicationsEndpointsContractTests`,
the save/version/collision suites and `Issue1038`/`Issue975`. Targeted run: **223 passed / 0 failed / 7 skipped**. All 7
skips predate this task (`OfficeEndpointsContractTests`).

## 6. Gates

_Filled in at Step 9.5._

## 7. Observation, not acted on

Several BFF services each resolve `sprk_recordtype_ref` by logical name with their own query and cache:
- `TodoRegardingBuilder`
- `ExternalDataService`
- `IncomingAssociationResolver`
- `CommunicationEnrichmentService`
- `CommunicationProposalApplyService`
- `DataverseServiceClientImpl`
- this one

Consolidating them is a CLAUDE.md §11 question for another day. A behaviour-preserving move is not the place for it.
