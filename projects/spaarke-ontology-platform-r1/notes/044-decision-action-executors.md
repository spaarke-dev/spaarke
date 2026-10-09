# Task 044 - decision action executors (2026-10-09)

Branch `task/ontology-044-decision-executors`, based on `origin/docs/ontology-platform-design` (the 036 catalog lives only there),
master merged in. PR base is the project branch. Rigor FULL (POML), sonnet/high.

## 1. Action -> core -> caller-rights check (POML step 1)

| Action | Executor | Write path (existing core) | Caller-rights check before any write |
|---|---|---|---|
| revise-budget | `ReviseBudgetExecutor` | writer `OntologyWriterDataverseClient.CreateAsync(sprk_budgetrevision)`; amount via `IDataverseUserClient.PatchAsync(sprk_budgets)` as the user (D-55) | probe Write on `sprk_budgets(id)`; budget read as the caller must be on the Signal's matter |
| approve-variance | `ApproveVarianceExecutor` | none (no constructor parameters) | n/a (D-19) |
| mark-complete | `MarkCompleteExecutor` | event: `EventEndpoints.CompleteEventAsync`; To Do: `ChildRecordEndpoints.UpdateAsync` | probe Write on the item |
| reschedule | `RescheduleExecutor` | event: new `EventDueAssigneeWrite` (route `PATCH /api/v1/events/{id}/due-assignee`); To Do: child-records update (`sprk_duedate`) | probe Write on the item |
| reassign | `ReassignExecutor` | event: same narrow write (assignee contact, statuscode 659490007, reassigned-by); To Do: child-records update (`sprk_assignedto@odata.bind`) | probe Write on the item |
| send-reminder | `SendReminderExecutor` | `CommunicationService.SendAsync` | probe AppendTo on the core record; recipient resolved as the caller; law firm -> primary contact with an email, else refuse (#30) |
| extend-response-date | `ExtendResponseDateExecutor` | caller PATCH `sprk_responseduedate` | probe Write on the work assignment |
| record-the-response | `RecordTheResponseExecutor` | caller PATCH `sprk_respondedon` + `sprk_responseoutcome` (D-54/D-58) | probe Write on the work assignment |
| add-todo / create-event | `AddTodoExecutor` / `CreateEventExecutor` | `ChildRecordEndpoints.CreateAsync` (Create + AppendTo checked as the caller inside the core, before the app-only create) | the core's own G5 check |
| send-email | `SendEmailExecutor` | `CommunicationService.SendAsync` | as send-reminder |
| send-budget-inquiry / assign-work | not here | tasks 070 / 046 | - |

`DecisionRouteCores` adapts the shipped handlers (virtual methods, called with the commit route's `HttpContext`). No existing core's
behaviour was changed (second escalation trigger did not fire); `EventEndpoints.ResolveActingUserContactAsync` went private -> internal.
Executor shape: `IDecisionActionExecutor { Code; ExecuteAsync(DecisionActionRequest) -> DecisionActionOutcome }`, registry
`DecisionActionExecutors` (refuses unknown/duplicate codes; `MissingCodes()` is empty when the catalog is covered). Outcome =
Done / Refused (nothing written) / Failed (maybe partial; lists the ids that landed).

## 2. uac-r2 re-read (origin/master c8a87d818)

SecureChildLineage.cs, config/secure-record-owner-role.json, RecordOwnershipResolver.cs, ExternalCallerContext.cs @ d254d7166;
RecordRouteAccessAuthorizationFilter.cs, CallerRecordAccessProbe.cs @ c5f71487f; RouteAuthorizationGuardTests.Ledger.cs @ c7e3d719b;
CoreAncestorResolver.cs, ChildRecordEndpoints.cs @ d7fdcafc3; ADR-034. Nothing invalidates the plan. Reused, none rebuilt: the probe,
`RecordRouteAccessAuthorizationFilter`, the child-records cores, `CoreAncestorResolver.CoreRecordEntities`. Open uac-r2 PRs touching our
files: none (only #1501, task 136, edits ChildRecordEndpoints.cs, which this task does not edit).
uac-r2-owned test files edited (text/classification only, need their review): `RouteAuthorizationGuardTests.Ledger.cs` (EventEndpoints
description + a NotAuthorizationForm for the shape filter) and `RecordOwnerAssignmentCensusTests.cs` (`EventDueAssigneeWrite.cs` in
RunAsUserWritesThatFileNothing).

## 3. Deviations and blockers

- **Base branch**: project branch, not master (036 is not on master). Publish delta measured vs the same branch without this commit.
- **BLOCKER for the live revise-budget proof (acceptance 1/8)**: the writer role `Spaarke Ontology Service` holds Create on
  `sprk_budgetrevision` and Read on `sprk_budget` (queried live), but NOT `prvAppendsprk_BudgetRevision` nor `prvAppendTosprk_Budget`.
  Creating a revision that carries the `sprk_budget` lookup needs both, so the writer's create will fail on dev until the owner
  approves those two grants (role edits need owner approval; not done here). Unit tests prove the logic with a faked writer.
- **Live low-privilege proof (ADR-038 constraint) not run**: needs a low-privilege user token and the grants above; no Dataverse
  writes were made by this task.
- Event reschedule/reassign writes no `sprk_eventlog` row (the removed PUT did not either on this path); complete keeps its own log.
- create-event writes the date to `sprk_duedate` and attendees into `sprk_description` (no attendee column).
- `sprk_budgetrevision` is not in `SecureChildLineage`: a revision on a Secure matter is not a secure child (see issues list).
