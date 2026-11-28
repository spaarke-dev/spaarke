# ISS-010: where the remaining table-metadata reads come from (2026-11-28)

Read-only analysis on master `f0f1e810bd` (Part C and D1 merged). No code change yet. Sources: the call profile of `SecureChildOwnershipAiToolTests.Iss010_FourActionDecision_IsRecorded_…` (run with `ISS010_PROFILE_OUT`), and a code-mapper pass over `RecordCreationService`, `CallerWriteAuthorization`, `DataverseWriteItemMapper` and `CallerMetadataReadMemo`.

## Numbers

- **Rig, four-action commit:** 103 calls in total. Of these, 25 are `EntityDefinitions` reads over 21 distinct paths.
- **Live** (Part C check, 2026-11-27, before D1): 28 `EntityDefinitions` reads, 5 of them on `sprk_workassignment`.
- **Repeats in the rig:** only 4 reads (each x2), and all are paths the request memo does not see:
  - `EntitySetName` of `sprk_matter`, `contact` and `sprk_recordtype_ref`;
  - `sprk_workassignment` `ManyToOneRelationships`.
- **Raw-`user` call sites** in `RecordCreationService.cs` (not wrapped with `CallerMetadataReadMemo.For`): `:824`, `:1336` (the work-assignment re-map via `DataverseWriteItemMapper.MapAsync`), `:1879`, `:1931` and `:1961`.

## What the options are worth

1. **Wrap the raw-`user` sites with the request memo.** This removes at most the 4 repeats, about 0.35 s live. These are small edits in `RecordCreationService.cs`, which field-mapping-r1 also edits (their task 067), so coordinate with them first. The memo keeps only successful `EntityDefinitions(` GETs, so access answers are unaffected.
2. **Fewer distinct shapes.** The 21 distinct paths are mostly per-column `$filter` sets (`IsSecured`, `TargetAttributes`, `DateTimeBehavior`) and several `$select` shapes on the same table. Reading each table's column metadata once per request and filtering in memory would cut more, but it changes `CallerWriteAuthorization` (an access-gate file; uac-r2 reviews).
3. **A metadata cache across requests** (per tenant, with a TTL). This would remove nearly all 25 reads after warm-up, about 2 s. It is a design decision under ADR-009 (caching) and the caller-token argument in `CallerMetadataReadMemo.RequestScope`: a schema answer read under one caller's token would be served to another. It needs the owner and uac-r2.

## Recommendation

Measure D1 live first. Part C gave 15.4 s and D1 removes about 11 calls (about 1 s), which should bring the commit under the 15 s target. If it does, ISS-010 can close with option 1 as an optional small follow-up. If it does not, the next lever is the 66 WCF (app-only SDK) calls, not metadata. Profile those before choosing between options 2 and 3.
