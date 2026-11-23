# ISS-037: C16 ArchTest, "other content" refusal made typed

## Defect
ArchTest `082c-2 C16: no ex.Message and no resolver-record ToString() on a reader surface`
(`tests/Spaarke.ArchTests/ProcedureReaderRedactionGuardTests.cs`) failed on `work/ebilling-r1` at
`InvoiceAdjustmentService::IsOtherContent: reads an exception's Message`. Task 095f-2b decided "other content" by comparing
`ex.Message` with `DecisionRecordWriter.BillingOtherContentRefusal`. `InvoiceLineAddService.IsOtherContent` had the same comparison
(not a guarded reader surface, so not flagged, but the same message coupling).

## Fix
Carrier (b): a read-only `bool IsOtherContent` on `DecisionRecordRefusedException` (optional ctor arg `isOtherContent`, default false).
`DecisionRecordWriter.AlreadyRecordedOrRefuse` sets it at the one throw site that picks the other-content message, only when every
same-code row has a readable hash (the unreadable-hash message and the other-code message stay false). Both services now test
`Reason == ReviewInvalid && ex.IsOtherContent`. Chosen over a new `DecisionRecordRefusalReason` member because the reason string is
a persisted/mapped contract shared by other callers, and a new member would change how they treat ReviewInvalid. Message text and
refusal behaviour are unchanged. The marker is also true for the Procedure entry's other-content refusal (same helper); no caller
reads it there.

## Tests
- `DecisionRecordWriterTests.Billing.cs`: readable other content -> `IsOtherContent` true; unreadable-hash and another-billing-action
  refusals (both ReviewInvalid) -> false.
- `DecisionRecordWriterTests.RecordProcedure.cs`: other-content refusal -> true.
- Existing InvoiceAdjustment / InvoiceLineAdd other-content tests pass unchanged.
- Fail-before: the C16 ArchTest fails on the base (service files reverted), passes with the fix.
