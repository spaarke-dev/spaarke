# Fix — task-021 inbound integration-test drift (2026-08-06)

## Problem (surfaced during R-1, pre-existing on the branch)

4 `CommunicationIntegrationTests.InboundPipeline_*` tests were FAILING on the branch, independently of R-1
(verified by stashing R-1 and running against the committed R-2/R-3 state):

- `InboundPipeline_WebhookNotification_CreatesIncomingRecord`
- `InboundPipeline_MultiMessageThread_StoresFullThreadNotUniqueBody`
- `InboundPipeline_IncomingRecord_HasNoRegardingFields`
- `InboundPipeline_DuplicateWebhook_DoesNotCreateDuplicate`

**Root cause:** task 021 (race-proof dedup) re-routed the inbound create in `IncomingCommunicationProcessor`
from `IGenericEntityService.CreateAsync` to `ICommunicationDataverseService.CreateCommunicationRaceProofAsync`
(returns `(Guid Id, bool WasDuplicate)`), but these integration tests still stubbed/captured the OLD
`CreateAsync` seam → `capturedEntity` was null / `createCallCount` was 0. Task 021 shipped without updating them.

## Fix (test-only)

Re-pointed the 4 tests' create stubs from `CreateAsync` to `CreateCommunicationRaceProofAsync` on the same
`Mock<IDataverseService>` composite (which exposes both seams). The **Entity built by the processor is unchanged**
— task 021 changed only the CALL — so every field assertion (`sprk_body`, `sprk_regarding*`, `sprk_associationcount`,
etc.) is preserved verbatim. Each stub now returns `(Guid, false)` (not-a-duplicate) and captures the first
(`Entity`) argument.

## Verification
Full Communication suite: **962 passed / 0 failed** (was 958 / 4). Inbound subset 4/4. No production code changed.
