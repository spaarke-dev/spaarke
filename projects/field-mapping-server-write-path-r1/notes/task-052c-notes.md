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

- Selection: ONE scan of `[createdFrom, createdTo)` for the one table, ordered by id, at most `MaxRowsPerTable` rows. More rows →
  `truncated`, Success=false, "narrow the window"; the first page is still repaired (fill-only, so a narrowed re-run writes
  nothing twice).
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

- **K1 — Truncated windows are not continued.** An on-demand run keeps no cursor (it must not touch the scheduled state); a
  truncated window repairs its first page and the administrator narrows the window. Rows are ordered by id, so the repaired
  page is not the earliest by `createdon`.
- **K2 — No retry set.** A row an on-demand run cannot finish is counted (`needsRetry`) and not kept; running the window again is
  the retry. Rows the scheduled pass is retrying stay in its set either way.
- **K3 — The admin 202 test's run reads nothing** (that host's WhoAmI names no user): what a started run does is pinned in
  `RecordRepairPassJobTests` over the real job; the 202 test proves the route hands the job the typed run (`mode: on_demand`,
  table) through the real host.
