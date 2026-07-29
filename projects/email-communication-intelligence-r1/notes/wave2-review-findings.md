# Wave 2 review findings (010 + 020) — Step 9.5 record

> Recorded 2026-07-29 by the orchestrator during autonomous execution.

## 010 — report-card regarding entry
- Added `("sprk_reportcard","sprk_regardingreportcard")` to BOTH `Engine/RegardingFieldMap.cs` AND the send-time `CommunicationService.RegardingLookupMap` (`sprk_reportcards` entity set).
- **Pre-existing dual-map (ADR-024 tension, NOT introduced by this project):** two regarding maps exist — `RegardingFieldMap` (Engine/, rung write path) and `RegardingLookupMap` (send-time caller-supplied path). They must be kept in sync. Candidate for a future consolidation task; flagged, not fixed (out of scope for 010's single-entry addition).
- Broke the seam forcing-function `AllElevenAdr024RegardingFamilies_StaysInSyncWith_RegardingFieldMapAll` (11→12 families). **Fixed** by adding the report-card row to the hardcoded family table + renaming `Eleven`→`Twelve` (the test author anticipated the 12th family; the Theory is data-driven so the 12th family is auto-covered).

## 020 — identifier reverse-lookup rung — ACCEPTED
Code-review verdict: no Critical issues. All FR-01/C-1/NFR-04/NFR-08 correctness guardrails satisfied + tested (16/16). Reuses one mechanism (ADR-024/045).

### Warning (non-blocking) — NFR-08 worst-case query count → FAST-FOLLOW
`IdentifierReverseLookupRung.EvaluateAsync` issues up to `MaxDistinctTokens` (25) × 7 core types = ~175 Dataverse `RetrieveMultiple` calls per message, deduped by `(numberField, value)`. Correct + bounded + best-effort + async (does not block capture), and the NFR-08 contract (cache roster, report `QueryCount`, zero queries when no tokens) IS met — so accepted. **Optimization for a fast-follow**: add a batched `QueryRecordsByNumberFieldsAsync(entity, field, IReadOnlyList<string> values)` (OR/`In` filter) so cost drops from tokens×types to ≤7 queries/message regardless of token count. Not blocking; the correctness requirements are unaffected.

## Pre-existing branch test debt (NOT from this project's code) — for /test-diet at wrap-up
5 Communication tests FAIL on clean HEAD (`5acd5c00c`), verified by stashing ALL work-tree + untracked changes and re-running — they predate every r1 code task (sender-identity / DTO-shape projection, unrelated to `RegardingFieldMap` or the rungs):
1. `CommunicationByRegardingReadTests.ReadByRegardingAsync_ReturnsThreadsAndMessages_InR1DtoShape`
2. `CommunicationFilteredQueryTests.QueryCommunicationsAsync_ProjectsEnrichedSenderIdentity_UniformWithOtherReadPaths`
3. `CommunicationThreadReadServiceTests.ReadThreadAsync_RowExcludedByAccessLayer_ContributesNoSenderIdentityToOutput`
4. `CommunicationThreadReadServiceTests.ReadThreadAsync_MultipleVisibleRows_EachDtoCarriesItsOwnSenderIdentity`
5. `CommunicationThreadReadServiceTests.ReadThreadAsync_OutgoingMessageWithSender_ProjectsDirectionAndSenderIdentity`
These are branch debt (likely from the master merge `cdd283656`). **Do NOT attribute to r1 tasks.** Flag for the wrap-up `/test-diet` + a possible regression-repair; the r1 waves must keep them from regressing further but are not obligated to fix pre-existing branch breakage. Current suite: 705 passed / 8 skipped / 5 failed (all 5 pre-existing).
