# Deferred issues — spaarkeai-mcp-bff-api-r2

> Defects found during task execution that are out of the task's scope. Each needs a GitHub issue filed by the operator
> (the task-006 sub-agent was not authorised to publish issues) and a pointer back here.

## DEF-006-01 — Event create copies a regarding record's name to a caller without Read on it (security, pre-existing)

- **Found by:** task 006 adversarial review, tenant-isolation pass (classed F1, pre-existing; not introduced by the extraction).
- **Where:** `src/server/api/Sprk.Bff.Api/Services/Events/EventsCapability.cs` — `CreateCoreAsync` (regarding block) and
  `ResolveRegardingFieldsAsync`; policy `src/server/shared/Spaarke.Core/Auth/OperationAccessPolicy.cs:286`
  (`["event.attach_regarding"] = AccessRights.AppendTo`).
- **Defect:** the create authorizes AppendTo on the regarding record only, then reads that record's primary name and number
  app-only and stores them on the new event (`sprk_regardingrecordname`, `sprk_regardingrecordnumber`). AppendTo does not
  imply Read, so a caller whose role grants Append To widely but Read narrowly can learn a record's name and number.
- **Scenario:** B (Append To at Organization depth, Read on matters at User depth) `POST /api/v1/events` with
  `regardingRecordType=1` and the id of A's matter → the create succeeds → `GET /api/v1/events/{newId}` (B's own event)
  returns A's matter name. Same through `IEventsCapability.CreateAsync` once an MCP tool calls it.
- **Why not fixed in task 006:** the task is a byte-preserving extraction (frozen-bytes baselines); either fix changes what
  REST returns or who is allowed, which is an authorization behaviour change needing an owner decision (root CLAUDE.md §6).
- **Fix options:** (a) read the name/number only after a caller Read check (probe or `IDataverseUserClient`); without Read,
  keep the request's own name and no number — deny bytes unchanged; (b) make `event.attach_regarding` require
  `Read | AppendTo` — same 403 rights answer, but changes who may attach.
- **Must land before** the events create tool is exposed to Copilot (task 025 / 036).
- **GitHub issue:** not yet filed (operator).

## DEF-006-02 — `EventStatusDeprecationTests.NoSourceFile_ReadsOrWrites_TheDeprecatedColumn` fails on the base (pre-existing)

- **Found by:** task 006 broad test run (`Sprk.Bff.Api.Tests`, 11005 passed / 1 failed).
- **Where:** `scripts/demo-data/DemoDataLib.ps1` and `scripts/demo-data/reference-names.json` reference the deprecated event
  status column the guard forbids; task 006 touched no file under `scripts/`.
- **Fix:** move the demo-data scripts to the replacement column, or add them to the guard's documented exceptions with a reason.
- **GitHub issue:** not yet filed (operator).
