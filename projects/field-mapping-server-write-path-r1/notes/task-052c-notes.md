# Task 052c — The repair pass on demand: one table, one window

Slice c of task 052 (FR-25, NFR-01). POML: `tasks/052c-repair-on-demand-trigger.poml`. Rigor: FULL (auth-tagged: two
adversarial passes are the main session's). Nothing here enables the pass on any environment: the route refuses every request
while `RecordRepair:Pass:GoLive` is unset.

## Decision 1 — route shape

`POST /api/admin/jobs/record-repair-pass/runs`, body `{ "table": "...", "createdFrom": "...", "createdTo": "..." }`, mapped on the
existing `/api/admin/jobs` group (`RequireAuthorization("SystemAdmin")`), in `JobsEndpoints.cs`. Typed for this job only.

- The generic `POST /{jobId}/trigger` keeps no body and still passes `parameters: null`: a body forwarded to every job would hand
  admin-chosen parameters to jobs that never validated them. Both routes share one dispatch mapping (`DispatchAsync`: 202 with
  the run id and a `Location` of `.../runs/{runId}`, 404, 409 busy, 503 lease store down, 499 caller gone); the generic route's
  behaviour is unchanged.
- Everything is checked before the lease is taken, in this order: the job is registered (404); go-live is set (409
  `record_repair_run.not_configured`) and parses (409 `record_repair_run.go_live_invalid`); the body is a JSON object (400
  `body_invalid`); then `RecordRepairOnDemandRun.TryCreate` against the job's own `Tables` list (400 with the codes below).
- The bounds are strings in the body, so a bound without an offset can be refused (a typed `DateTimeOffset` would read it as
  local time). Accepted: `yyyy-MM-ddTHH:mm:ss[.fffffff]` followed by `Z` or `±hh:mm`.
- **Window bound: 31 days** (`RecordRepairOnDemandRun.MaxWindow`). One run reads at most the pass's row cap
  (`RecordRepair:Pass:MaxRowsPerTable`, default 200), so a longer window would mostly report truncated; 31 days covers a
  calendar month (the unit an administrator usually states) and keeps each scan's `createdon` range bounded. A wider span is
  several runs.
- Status codes: 400 for a request the administrator can correct; 409 for "the pass is not configured" (the request is well
  formed, the server state refuses it — the same status family as the lease's 409, told apart by `reasonCode`).

Reason codes (`RecordRepairRunReason`, carried as `reasonCode` on a ProblemDetails, no exception text, no row data):
`body_invalid`, `table_not_repaired` (unregistered, or excluded: task, account and every other table the pass excludes),
`timestamp_invalid`, `timestamp_offset_missing`, `window_empty` (`createdFrom >= createdTo`), `window_too_long`,
`window_in_future` (`createdTo` after now), `before_go_live` (the pass is forward-only; refused, never clamped),
`not_configured`, `go_live_invalid`; all prefixed `record_repair_run.`.

## Decision 2 — how on-demand mode is reached only from the admin route

- The route builds a `RecordRepairOnDemandRun` (private constructor; the only factory is `TryCreate`, which applies every rule)
  and hands it to `ScheduledJobHost.TriggerNowAsync` as the value of one parameter key
  (`RecordRepairOnDemandRun.ParameterKey = "recordRepairPass.onDemandRun"`).
- `RecordRepairPassJob.OnDemandRunOf(context)` returns the run only when `context.Trigger == ManualAdmin` AND that key holds an
  instance of the type. Otherwise the run is the scheduled pass.
  - A scheduled tick never is on demand, whatever its parameters carry.
  - A persisted `ConfigJson` reaches a run as one string under `configJson` (the host never spreads it into keys), so it can
    never carry an instance; a key holding text (or any other type) is ignored, with a warning that the run is the scheduled pass.
- The job does not trust the route: before it reads anything it asks `RecordRepairOnDemandRun.Check` again with its own go-live,
  its own clock and its own table list. A window that starts before go-live (for example a go-live changed between the request
  and the run) writes nothing, reads nothing, and fails. Go-live unset fails an on-demand run (a scheduled run reports
  `not_configured` as success, unchanged).
- No change to `ScheduledJobHost` and no new store (the escalation trigger did not fire).

## What an on-demand run does

Same job, same host lease, same per-table claim key (`record-repair-pass:{table}`, owner = the run id), same product-principal
set (asked first; undecidable → nothing read), same row rule (`RecordRepairPassContext.RowOutsideThePass` in the job, then again
in Repair over the row it read), same Repair mode, same contact-table gate (the customer team must resolve). Differences:

- Selection (revised in review round 1): the table's `[createdFrom, min(createdTo, run start - settle time))`, paged in id order
  (pages of `MaxRowsPerTable`, continuing after the last id read, starting after `afterId` when given) until exhausted or
  `DefaultOnDemandRowBudget` rows were read. More rows than the budget → `truncated`, Success=false, `lastIdCovered`; everything
  read was repaired (fill-only, so a continued or repeated run writes nothing twice).
- State: the scheduled pass's `record-repair-pass:{table}:state` (watermark, sweep window and cursor, retry set) is neither read
  nor written. A row that needs asking again is counted `needsRetry` (Success=false; run the window again), not kept. A row in
  the scheduled retry set stays there: if the on-demand run repaired it, the scheduled pass's next ask is a no-op and drops it.
- Observability: the heartbeat and the result JSON gain `mode` (`scheduled` / `on_demand`), `table`, `createdFrom`, `createdTo`
  and `needsRetry` (the scheduled pass's result gains `mode: "scheduled"` and nulls; additive). The run record shows trigger
  `ManualAdmin`, visible on `GET /api/admin/jobs/record-repair-pass/status` and `/history`.

## Ledger

The route is in `RouteAuthorizationGuardTests.Ledger.cs`'s `AdminOnlyRoutes` group for `Api/Admin/JobsEndpoints.cs`
(`SystemAdminPolicy`); the group's reason names the auth test. `EveryAdminOnlyRouteCarriesItsGroupsMechanism` proves the route
carries the group's `RequireAuthorization("SystemAdmin")`; `TheSetOfAdminOnlyRoutesIsPinned` pins it. `RecordRepairReachabilityTests`
lists `RecordRepairPassJob::RunOnDemandTableAsync` as Repair's second production caller.

## Placement Justification (§10, ADR-052)

In the BFF, on the existing in-process `Spaarke.Scheduling` host and the existing `/api/admin/jobs` SystemAdmin group. The work is
BFF domain code (Repair mode, the ownership resolver) over BFF-owned tables with the BFF identity, at administrator-initiated
volume (one table, at most the row cap per run). No new scheduler, job, store, policy, scheme, package, DI registration or
background service: one route in an existing endpoints file, one mode of an existing job. Publish-size delta and the CVE check
are the main session's.

## Component Justification (CLAUDE.md §11)

- **Route `POST /api/admin/jobs/record-repair-pass/runs`** (+ `RecordRepairRunRequest` body DTO). (1) Existing:
  `POST /{jobId}/trigger` runs a job by id with no parameters. (2) Extension: the generic trigger is not given a free-form body,
  because every other job would then receive admin-chosen parameters it never validated; one typed route for this job is the
  narrowest extension. (3) Cost of doing nothing: rows the watermark has passed (the invoice family skipped before its guard,
  K1; rows abandoned after 12 attempts, K6; owners held back by the Assign-cascade allow-list, K9) are never repaired.
- **`RecordRepairOnDemandRun`** (+ `RecordRepairRunRefusal`, `RecordRepairRunReason`). (1) Existing: none for a window; go-live
  parsing was inline in the job (now `RecordRepairPassJob.ReadGoLive`, shared). (2) Extension: it is the one value that
  switches the existing job into its on-demand mode, so the route and the job ask the same rules. (3) Cost of doing nothing:
  the rules would live twice (route and job) and the job could be switched by an untyped key.
- **On-demand mode of `RecordRepairPassJob`** (`OnDemandRunOf`, `RunOnDemandTableAsync`, the `needsRetry` count, `mode` in the
  heartbeat/result). (1) Existing: the scheduled pass. (2) Extension: same job, claim, rules and Repair; only selection and state
  differ. (3) Cost of doing nothing: as for the route.

## Evidence

**Must-fire, each rule switched off then restored with `git checkout`** (production code mutated, test project rebuilt, run
over `RecordRepairPassJobTests` + `Auth.Admin.RecordRepairPassTrigger*`, 88 tests):

- Batch A (`RecordRepairPassJob.cs`) — the job's own window check off; the `ManualAdmin` check in `OnDemandRunOf` off; the
  on-demand branch writing the table's state; the claim treated as taken; truncation not counted; go-live unset reported as
  success for an on-demand run. **8 failed, exactly the expected ones:** `AnOnDemandRunWhoseWindowStartsBeforeGoLive_*`,
  `ARunThatIsNotAnAdministratorsOnDemandRun_IsTheScheduledPass("a scheduled tick ...")`, `OnlyAManualRunCarryingTheTypedValue_*`,
  `AnOnDemandRun_LeavesTheScheduledPasssWatermarkSweepAndRetrySetUnchanged`, `ARowAnOnDemandRunCannotFinish_IsReported_NotKept`,
  `WhileAnotherRunnerHoldsTheTable_*`, `AWindowWithMoreRowsThanTheCap_*`, `AnOnDemandRun_WithGoLiveUnset_*`.
- Batch B (`RecordRepairOnDemandRun.cs`, `JobsEndpoints.cs`) — `Check` answers null; `TryCreate` takes any table; a bound
  without an offset accepted; go-live unset read as set at the route; `.AllowAnonymous()` on the route; the busy-lease 409 not
  mapped. **15 failed, exactly the expected ones:** 401, 403, the lease 409, the not-configured 409, the window and table cases of
  the refusal theory (4 table, 2 empty, too long, future, before go-live, offset missing), and the job's own forward-only test
  (`Check` is shared). The three cases not mutated (`timestamp_invalid` ×2, `body_invalid`) and the admin 202 stayed green.
- Restored: 88/88 pass.

Gates (on 39d8474f62, after the mutations were restored):

- `dotnet build Spaarke.sln`: 0 warnings, 0 errors.
- `tests/Spaarke.ArchTests`: 2639/2639 (route guard, AdminOnlyRoutes pin, Repair reachability, owner-write census unchanged).
- Sprk.Bff.Api.Tests filtered (Repair, RecordOwnership, Scheduled, Job, Admin, RecordCreation, Contact, AssignCascade, Census,
  Issue975; `!~Api.Office.`): 9061 passed, 5 skipped (pre-existing), 0 failed. The new tests alone: 17 job + 18 route = 35.
- `dotnet format whitespace --verify-no-changes` on every touched .cs: clean.

## Deviations from the POML

- Test paths: the auth test is `tests/integration/auth/Admin/RecordRepairPassTriggerAuthTests.cs` (the auth KEEP path's
  `{Module}` subfolder, beside `RecordMatchingAdminPolicyTests`), not `tests/integration/auth/` directly. The on-demand job tests
  are a new partial file `RecordRepairPassJobOnDemandTests.cs` of the same `RecordRepairPassJobTests` class (the original file
  became `partial` and its harness gained `ScanReportsCreatedOn`), to keep the scheduled and on-demand cases readable apart.
- "Route ledger entry with a ProvenByTest": `ProvenByTest` is a credit on a SWEEP finding entry (`SweepFinding`) only; this route
  has no sweep finding. A new admin-only route is declared through `AdminOnlyRoutes` (pinned, mechanism-checked), which is what
  was done; the group's reason names the auth test that proves 401/403/202.
- Go-live unset / unparseable at the route answers 409 (not 400): the request is well formed and the server state refuses it.

## Known limits

- **K1 — A truncated window is continued by the administrator, not by the job (superseded in review round 1).** An on-demand run
  keeps no cursor of its own (it must not touch the scheduled state). It pages the window in id order up to the row budget
  (`DefaultOnDemandRowBudget` = 500), then reports `truncated`, Success=false and `lastIdCovered`; the administrator runs the
  window again with `afterId` = that id. Rows are ordered by id, so a run's rows are not the earliest by `createdon`.
- **K2 — No retry set.** A row an on-demand run cannot finish is counted (`needsRetry`) and not kept; running the window again is
  the retry. Rows the scheduled pass is retrying stay in its set either way.
- **K3 — The admin 202 test's run reads nothing** (that host's WhoAmI names no user): what a started run does is pinned in
  `RecordRepairPassJobTests` over the real job; the 202 test proves the route hands the job the typed run (`mode: on_demand`,
  table) through the real host.
- **K4 — The job's disable switch and `RunScheduledJobs=false` do not stop the route.** The route starts a manual run through
  the host's `TriggerNowAsync`, which does not consult either. The off switch for on-demand runs is unsetting
  `RecordRepair:Pass:GoLive`: the route answers 409 `record_repair_run.not_configured` and the job refuses an on-demand run with
  go-live unset.
- **K5 — A window that ends at or after "now minus two minutes" is reported as not clean.** The scan stops at the scheduled
  pass's settle point; the run says which rows were left (`settleClamped`, `scanUntil`) and Success=false, and the administrator
  runs the tail later. A window ending at "now" therefore never reports success.

## Review and fix round 1

| # | Finding | Class | Outcome |
|---|---|---|---|
| 1 | An on-demand run read one page and had no cursor, so rows past the cap inside a one-second window were unreachable | F4 | Fixed. The run pages (id order, `after` = last id read, the scheduled pass's order) until the window is exhausted or `DefaultOnDemandRowBudget` rows were read; on the budget it names `lastIdCovered` in the result JSON, the heartbeat and the message; an optional body field `afterId` (GUID) continues a run, validated before the lease (`record_repair_run.after_id_invalid`, 400; the empty GUID is refused). The scheduled pass's state is untouched. |
| 2 | A started run left no record of who started it | F3 | Fixed. After the host accepts the run the route logs once, structured: `oid` (via `CallerResolution.ResolveObjectId`), run id, table, window, `afterId`. No token or row data; a refused or lease-held request logs no started line. |
| 3 | No test of the route's `go_live_invalid` 409 | F4 | Fixed. A host with an unparseable go-live answers 409 `record_repair_run.go_live_invalid`, starts no run and does not echo the setting. |
| 4 | A client abort while the body is read was an unhandled `OperationCanceledException` | F3 | Fixed. `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)` returns 499 (as the generic trigger does) and logs the cancel at information level; tested with a body that stalls and a client that leaves. |
| 5 | The on-demand window could end at now while the scheduled pass stops at now minus the settle time | F4 | Fixed. The job clamps the scan's upper bound to min(`createdTo`, run start minus `SettleTime`); a clamped run is reported not clean (K5); a window wholly inside the settle time reads nothing and takes no claim. The route still accepts `createdTo` up to now. |
| 6 | Boundary and path test gaps in the request rules | F4 | Fixed. `RecordRepairOnDemandRunValidationTests`: `createdTo` = now accepted (+1 s refused); a window of exactly 31 days accepted (+1 s refused); `createdFrom` = go-live accepted (-1 s refused); upper-case and padded table names accepted and normalised to the pass's spelling; `+05:30` offset normalised to UTC; fractional seconds kept; `timestamp_invalid` (6 shapes) and `timestamp_offset_missing`; `afterId` absent / well formed / malformed / empty. Route: `body_invalid` for `null`, `[]` and a bare string as well as text that is not JSON. |
| 7 | Notes | F4 | This section, K1 revised, K4 and K5. |

Not done: the optional assertion of the core-ancestor stamp in the on-demand repair test. The pass harness
(`RepairWorld`) records the conditional write's mapping and owner fields but not the stamp, so the stamp stays pinned where it is
today (`RecordRepairModeTests`); exposing it would change the harness for one assertion.

**Row budget value: 500.** One Repair per row is a read and at most one conditional write; at a pessimistic second per row 500
rows stay inside the table claim (`ClaimDuration`, 15 minutes). It is a named constant (`DefaultOnDemandRowBudget`) with an
`init` property on the job for tests.

**Must-fire evidence (production rule switched off, test project rebuilt, `RecordRepairPassJob*` + `RecordRepairPassTrigger*` +
`RecordRepairOnDemandRun*`, 132 tests; each file restored with `git checkout`).** Mutations that would not compile were redone
without a constant condition (J1, J2, R1).

| Rule switched off | Tests that failed (all expected) |
|---|---|
| Paging stops after the first page | `AWindowWithMoreRowsThanOnePage_*`, `AWindowWithMoreRowsThanTheBudget_*`, `AWindowWithMoreRowsThanTheBudgetOnOnePage_*`, `ARunContinuedWithTheLastIdCovered_*` (4) |
| Budget never stops the run | the two budget tests (2) |
| `afterId` ignored by the job | `ARunContinuedWithTheLastIdCovered_*` (1) |
| `afterId` dropped when the run is built | `AWellFormedAfterId_IsKept`, `ARunContinuedWithTheLastIdCovered_*`, the started-run log test (3) |
| Empty GUID accepted as `afterId` | `AMalformedOrEmptyAfterId_IsRefused(empty)`, the route's `after_id_invalid` case (2) |
| Scan not clamped to the settle point | `AWindowEndingAtNow_*`, `AWindowEntirelyInsideTheSettleTime_*` (2) |
| `lastIdCovered` not set | `AWindowWithMoreRowsThanTheBudget_StopsAtTheBudget_*` (1) |
| 499 catch not matching | `AClientThatAbortsWhileItsBodyIsRead_*` (1) |
| Started-run log not written | `AStartedRun_IsLoggedOnce_*` (1) |
| Caller `oid` not logged | `AStartedRun_IsLoggedOnce_*` (1) |
| Unparseable go-live ignored by the route | `WithAnUnparseableGoLive_*` plus the route's other cases (23) |
| Window bound `>=` instead of `>` | the exactly-31-days case (1) |
| `createdTo` = now refused | the `createdTo == now` cases and both settle tests (4) |
| `createdFrom` = go-live refused | the `createdFrom == go-live` case and every on-demand job test (16) |
| Table matched case-sensitively | upper-case and padded cases (2) |
| Table name not trimmed | the padded case (1) |
| Offset bound not normalised to UTC | `AnOffsetBound_IsNormalisedToUtc` (1) |

Gates after the round: solution build 0 warnings; ArchTests 2639/2639; `dotnet format whitespace --verify-no-changes` clean on
every touched .cs; the filtered Sprk.Bff.Api.Tests run (Repair, RecordOwnership, Scheduled, Job, Admin, RecordCreation, Contact, AssignCascade, Census, Issue975; `!~Api.Office.`) 9105 passed, 5 skipped (pre-existing), 0 failed.
