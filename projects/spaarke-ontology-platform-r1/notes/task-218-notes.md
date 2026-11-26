# Task 218 notes — manual job trigger keeps its logs (#3125, DI-175)

## Findings (step 1)
- `ScheduledJobHost.TriggerNowAsync` started the run with `Task.Run(...)` inside the `POST /api/admin/jobs/{jobId}/trigger`
  request. `Task.Run` captures the ExecutionContext, so `Activity.Current` (the request's server activity) flowed into the
  run and every log line carried its trace id. When the rate-limited sampler drops the request's trace, the run's logs go with it.
- The scheduled path (`DispatchAndAdvance` -> `Task.Run(RunJobAsync)`) starts from the BackgroundService, with no request
  activity, so it was never affected.
- `Spaarke.Scheduling` has no `ActivitySource` and starts no Activity per run, so none was added to the manual path.

## Change
- `src/server/shared/Spaarke.Scheduling/ScheduledJobHost.cs`: save `Activity.Current`, set it to null, start the `Task.Run`,
  restore it in `finally` (device copied from `ExternalAccessAudit.Write`). Sampler, OpenTelemetry setup, trigger API unchanged.
- Tests in `tests/unit/Spaarke.Scheduling.Tests/ScheduledJobHostTests.cs`:
  - `TriggerNowAsync_WithAmbientRequestActivity_RunsHandlerWithNoInheritedActivity_3125` fails on origin/master
    (handler saw the request activity), passes after; also asserts the caller's `Activity.Current` is the same object after the
    call, and the response, run id and run record are unchanged.
  - `TriggerNowAsync_WithNoAmbientActivity_StillRunsAndLeavesActivityNull_3125` is the must-not-fire control (passes both sides).

## Other users of `TriggerNowAsync`
- `Api/Admin/JobsEndpoints.cs` (`POST /api/admin/jobs/{jobId}/trigger`).
- `Api/SpeAdmin/DashboardEndpoints.cs` (SPE dashboard refresh, two call sites).
Both benefit the same way; neither changes. Last edits to `ScheduledJobHost.cs`: format-only (2026-11-10), scheduling fix #1581 (2026-10-10).

## Known limits
- The single "Manual admin trigger dispatched" line is logged inside the request, so it stays on the request's trace and can still
  be dropped with it. It is one line emitted before the run starts; the run's own lines (heartbeat, refusals, failures) are
  the ones that matter and are now independent. Correlation: RunId / CorrelationId in each message.
- Service Bus-triggered handlers: not checked (out of scope, issue says unverified).
- The BFF-wide sampler is untouched (separate, larger decision).
- Not verifiable here: that a dev manual trigger's heartbeat reaches App Insights (main session, after the next dev BFF build).

rework: none task 218
