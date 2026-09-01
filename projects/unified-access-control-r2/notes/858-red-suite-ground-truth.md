# #858 — red-suite ground truth (measured, not inferred)

> Captured 2026-09-01 from a full `dotnet test tests/unit/Sprk.Bff.Api.Tests/` run at commit
> `841c24117`, BEFORE any fixture repair. This file exists so that any later claim of "fixed"
> can be checked against a measured baseline instead of a remembered one.

**Measured totals:** `Failed: 20` · Passed: 11,726 · Skipped: 57 · Total: 11,803.
No compile errors. The trailing `Build FAILED.` line is `dotnet test`'s normal propagation of a
non-zero test result, not a build break.

## The 20, grouped by class

| # | Class | Test |
|---|---|---|
| 1 | `Api.Ai.ComposeCreateOnSaveEndpointContractTests` | `CreateOnSave_WithEmptySessionId_Returns200AndPersistsDocumentWithoutRebind` |
| 2 | ″ | `UploadThenCreateOnSave_TransientDraft_PersistsNewDocumentAndSpeItemAndRebindsSession` |
| 3 | ″ | `CreateOnSave_WhenContainerIdMissing_Returns400` ← **premise deliberately dead (see below)** |
| 4 | ″ | `CreateOnSave_WhenSourceRecordReadFails_StillCreatesTheDocumentUnassociated` |
| 5 | ″ | `CreateOnSave_WithSourceDocumentRecordId_InheritsSourceRecordLinks` |
| 6 | ″ | `CreateOnSave_WhenSpeCreateSucceeds_Returns200CarryingPersistedOutcome` |
| 7 | ″ | `CreateOnSave_WhenSpeCreateReturnsNull_Returns200CarryingStorageFailedOutcome` |
| 8 | `Seam.Compose.ComposePdfRefreshBaselineSeamTests` | `PdfSourced_SecondSaveAfterRefresh_ResolvesTheCreatedDocxAndKeepsTheFirstSavesWork` |
| 9 | ″ | `PdfSourced_WhenTheDerivedDocumentWasDeleted_ProjectsThePdfAfreshInsteadOfFailing` |
| 10 | ″ | `SessionThatServedAPdfThenServesADocx_DoesNotStampTheDocxAuthored` |
| 11 | ″ | `PdfSourcedCreateOnSave_StampsTheRecordAuthored_SoTheFrA08SuppressionCanReachIt` |
| 12 | `Seam.Compose.ComposeTransientKeyDedupSeamTests` | `SaveNew_ForkNew_SkipsTransientKeyDedup_ForksNewRecord_ThroughTheWire` |
| 13 | ″ | `EightRepeatedCreateOnSaveSameTransientKey_ProduceExactlyOneRecord_ThroughTheWire` |
| 14 | ″ | `SaveVersion_RepeatedCreateOnSaveSameTransientKey_ReplacesInPlace_OneRecord_ThroughTheWire` |
| 15 | `Seam.Compose.ComposeOriginRoutingSeamTests` | `Save_BornInEditor_CreateOnSave_ContentModel_ResolvesAuthored_PersistsAuthoredMarker_ThroughTheWire` |
| 16 | ″ | `Save_ImportedTransient_CreateOnSave_OperationLogPath_StaysTracked_PersistsImportedMarker_ThroughTheWire` |
| 17 | `Regression.Compose.Def14_ComposeSaveLockedDocumentTests` | `CreateOnSaveDocument_WhenSpeCreatePreconditionFailed_Returns412WithActionableCopy_NotOpaque500` |
| 18 | ″ | `CreateOnSaveDocument_WhenSpeCreateItemLocked_Returns423WithActionableCopy_NotOpaque500` |
| 19 | `Seam.Ai.ComposeFidelitySeamTests` | `BornInEditorCreateOnSave_RendersNumberingGoldenFile_RoundTrips_AndSurvivesSubsequentTrackedChangeEdit` |
| 20 | `Seam.Compose.ComposePdfIntakeRoundTripSeamTests` | `PdfNda_OpensEditsAndSavesAsNewDocx_WithHonestLossinessDataAndInheritedLinks` |

## What the inventory proves, and what it does not

**Proves — the blast radius is exactly the create-on-save-through-the-wire set.**
Every one of the 20 drives create-on-save through `WebApplicationFactory`. Nothing outside that
set broke: no auth test, no upload test, no non-Compose test, and the 387-test Compose *unit*
suite is fully green. A change that had broken something structural would not respect that
boundary this precisely. This is real evidence for the single-shared-cause hypothesis.

**Does NOT prove — that each of the 20 fails for the same proximate reason.**
Shared blast radius is not shared root cause. `ResolveCreateOnSaveContainerAsync` has several
distinct exits (foreign session → host context ignored; unsupported host type → 409; probe denial
→ 403; unresolvable container → `null` → container-step failure). Different fixtures could be
landing on different exits and all present as "create-on-save is broken." The partition must be
read off actual assertion text per test, not asserted from this table.

## One test's premise is dead by design, not broken

`CreateOnSave_WhenContainerIdMissing_Returns400` asserts the `containerId is required for
create-on-save` 400 guard. #858 **deliberately deleted** that guard, because the field it guarded
(`SaveComposeDocumentRequest.ContainerId`) is exactly the client-named-container defect the whole
project exists to remove. There is no longer any such thing as a missing `containerId`, so the
test cannot be repaired — a repaired version would assert the defect is still reachable.

It must be **rewritten to the new contract** and must NOT be deleted or `[Skip]`ped: the intent
worth preserving is *"a create-on-save that cannot obtain a container fails honestly and writes
no bytes."* That intent is still live and still needs a test — it just now resolves through
`BuildContainerFailedResult` rather than a 400.

## Standing caution carried over from compose-r8

compose-r8 warned on #858 that this region (their cluster 2a) sits at **76.8% branch coverage**,
and that a seeded-mutation pass over its neighbours found **eleven** documented guarantees with no
test at all — two of which could destroy a user's document. Their words: *"a green suite there is
weaker evidence than it looks."*

Consequence for this work: **turning these 20 green is necessary but not sufficient evidence.**
The new #858 exits (403 denial, 409 unsupported host, foreign session, unresolvable container)
need their own behaviour tests, because no pre-existing test covers paths that did not previously
exist.
