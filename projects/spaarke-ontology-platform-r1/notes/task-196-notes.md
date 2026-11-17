# Task 196 notes - gate Decision Record job: a refused lock is a Failure

Branch `task/ontology-196-gate-lock` (worktree `C:\wt196`, from origin/master `3df5ee9e3f`). Commit `31c6bc1656`. Rigor FULL, sonnet @ high.

## What changed
- `src/server/api/Sprk.Bff.Api/Services/Signals/GateDecisionRecordJob.cs`: the refused-lock branch of `GateDecisionRecordJobHandler.ProcessAsync` returns `JobOutcome.Failure(..., "ProcessingLockRefused", ...)` instead of Success. Warning on every refusal (job id, communication id, authorize, attempt, delivery count; `HandlerFailed` event). On `job.IsFinalDelivery` an extra Error line ("will be dead-lettered") plus one `OntologyWriterTelemetry.RecordFailure(GateRecordFailed)`. The branch sits before the try/finally, so it never releases the lock and never calls the writer. Class remarks and the two `EventId` doc comments updated. New `internal const string LockRefusedError` (the Failure's error text; not a refusal reason, no new telemetry instrument).
- `tests/unit/Sprk.Bff.Api.Tests/Services/Signals/GateDecisionRecordJobTests.cs`: Rig extended (optional cache and TimeProvider, `Job(..., deliveryCount)`), `ReleaseFaultingCache` helper; the old refused-lock test replaced; 3 new tests (1 theory with 4 rows).
- Not touched: IdempotencyService, JobContract, ServiceBusJobProcessor.

## Tests and measurements
- `GateDecisionRecordJobTests` (filtered): 32 passed, 0 failed (26 before; 1 replaced, 3 added incl. a 4-row theory).
- Seeding proof: with the old handler (plus the one const so the test file compiles) 7 of 32 fail (own-leftover-lock, takeover boundary, concurrent-duplicate, 4 final/non-final rows); 25 pass. Restored; 32/32 pass.
- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors. `dotnet test tests/Spaarke.ArchTests` (built first): 2639 passed, 0 failed. `dotnet format --verify-no-changes` on both changed files: exit 0.
- Publish size (BFF hygiene §10): `dotnet publish -c Release -r linux-x64 --self-contained false src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`, fresh short-path worktrees (`C:\wt196m` = origin/master `3df5ee9e3f`, `C:\wt196b` = branch `31c6bc1656`), `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, PDBs included. Master 42,399,565 B (40.44 MB, 191 files); branch 42,399,718 B (40.44 MB, 191 files); delta +153 B. Worktrees removed with `Remove-WorktreeSafe.ps1`.
- No package change, so no CVE re-check needed.

## Step 9.5 (inline self-review; no sub-agents per the brief)
- No F-class finding. adr-check: ADR-004 (outcome contract Success/Failure/Poisoned unchanged in shape), D-123 (ids and gate values only in the new log lines), ADR-038 (tests cover each acceptance criterion; no numeric target).
- Class grep (Step 9.5 rule 6) for `TryAcquireProcessingLockAsync` followed by Success on a refusal: not fixed here (D-106), listed for the operator below.

## Known limits
- A concurrent duplicate that is refused is redelivered immediately by the processor (abandon) up to the delivery cap (5) and can be dead-lettered while its sibling is still writing; a replay of the dead-lettered message then finds the record ("already recorded"). Visible and replayable, not silent.
- Final-delivery refusals log twice (Warning, then Error) by a literal reading of the constraint; metered once.

## Other issues seen (not fixed)
- `Services/Signals/AssignTaskEmailJob.cs` ~75-80 has the same shape: a refused per-job-owner lock returns Success ("another job holds the email"), so the same own-leftover-lock redelivery would drop the system email (the processed marker is only set after a send). Same class: `Services/Ai/Jobs/AppOnlyDocumentAnalysisJobHandler.cs` ~110-117 (comment says "return success to prevent retry"). Other `TryAcquireProcessingLockAsync` call sites from the grep (not opened): EmailAnalysisJobHandler, ProfileSummaryJobHandler, AttachmentClassificationJobHandler, IncomingMessagingJobHandler, RagIndexingJobHandler, InsightsIngestJobHandler, and the claim-style callers in CoreAncestorStampReconciliationJob, ExternalAccessReconciliationJob, GrantExpiryReminderJob, RecordRepairPassJob, TodoAssigneeOwnerReconciliationJob.
- Guard idea (not built): an ArchTest/source-scan that flags a handler returning `JobOutcome.Success` inside an `if (!await ...TryAcquireProcessingLockAsync(` block.

rework: none task 196
