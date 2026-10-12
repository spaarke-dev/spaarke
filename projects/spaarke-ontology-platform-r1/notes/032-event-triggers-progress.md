# Task 032 - the three event triggers: progress and decisions

Lane `task/ontology-032-event-triggers` (worktree `C:\wts-032`), from `origin/docs/ontology-platform-design` @ `c6845f474`. Rigor FULL. The POML status, TASK-INDEX and `current-task.md` are the main session's.

## What was built

| File | What |
|---|---|
| `Services/Signals/SignalEvaluationTriggerHandler.cs` (new) | `SignalEvaluationTriggerHandler` (ADR-004 `IJobHandler`, job type `SignalEvaluationTrigger`); `SignalEvaluationTrigger` (the one enqueue, never throws, logs EnqueueFailed); `SignalTriggerKind` (Classification, BudgetRevision, AssociationConfirmed); EventIds 50340-50342 (kept in this file so a parallel lane cannot collide in `OntologyWriterEvents`) |
| `Services/Signals/SignalReevaluationJob.cs` (shared with 033/034) | `EvaluateSubjectAsync(entity, id)`: the same `EvaluatePolicyAsync` the nightly pass uses, with an optional `SubjectScope` (evaluator subject filter; existing Signals read by the `{code}\|{type}\|{id}\|` prefix so no other subject is closed; policies about another table skipped by the rule body's `subject`). Decide lane only |
| `Services/Communication/CommunicationRuleGate.cs` | `RuleGatedAssessedConsumer` enqueues the Classification trigger when the communication has a matter AND a `sprk_triagecategory` (optional ctor param) |
| `Infrastructure/DI/SignalsModule.cs` (shared) | registers the trigger and the handler |
| tests | `SignalEvaluationTriggerHandlerTests.cs` (partial of `SignalReevaluationJobTests`, reuses its harness; one-word `partial` edit), |

## Decisions and deviations

1. **The BudgetRevision trigger is enqueued by task 043's commit route, with no delay, AFTER it closes the Signal** (review fix F1; this task's first version scheduled it 30 s out from `ReviseBudgetExecutor`, a timing hope rather than an order guarantee, and has been removed together with the scheduled-enqueue overload). **043 MUST call `SignalEvaluationTrigger.EnqueueAsync(SignalTriggerKind.BudgetRevision, "sprk_matter", matterId, revisionId)` after the Signal is closed Acted** (and also when the amount write failed and the Signal stays open, so it clears as ConditionCleared). Until 043 lands, no production path raises the BudgetRevision trigger: a revision recorded through a decision is closed by the commit route itself, and hand-made revisions are F-65. The handler already runs the kind.
2. **Optional constructor parameter** for the trigger on `RuleGatedAssessedConsumer`, so existing constructions in tests stay valid; production DI always supplies it.
3. **Classification fires only when the communication has a matter and a triage category** (one extra column read already in the same Retrieve). An unfiled classified email is picked up by the association-confirmed trigger (D-48) or nightly.
4. **Retry policy.** A pass with a thrown read/write (`PolicyFailures`) or a writer refusal (`Refused`) returns Failure (abandon, redeliver, dead-letter after the last delivery by `ServiceBusJobProcessor`). Invalid policies / anomalies are already logged by the shared code and complete the job (a retry cannot fix them). K-class: deterministic D-38 refusals are retried to the dead-letter queue (loud, not silent).
5. **Tenant/matter-scoped policies** are skipped with the nightly Warning (F-62); they do not fail the trigger.
6. **Enqueue timeout (review K3).** `EnqueueAsync` links a 5 s timeout (`EnqueueTimeout`) so a degraded Service Bus cannot stall a request; on timeout it logs a Warning (`EnqueueTimedOut`, 50343) and returns false, the nightly pass covers it. A caller cancellation is not treated as a timeout.
   **Known limit (review K2, not done):** writer failures inside a trigger pass are metered by the shared writer on every delivery attempt, so one failing message can be counted up to 5 times before it is dead-lettered. Metering only on the final delivery would need the delivery count threaded into the shared writer path; left as is (the logs and the dead-letter queue are exact).
7. **No new metric.** Failures are logged at Error with their own EventIds (App Insights `traces` / `exceptions`); the dead-letter queue is the second signal.
8. **Association-confirmed (D-48): enqueue exposed, call site NOT wired** - see F-66 (#1659). **Hand-made budget revisions** have no event - F-65 (#1658). Both are the POML's escalation triggers in part: reported to the owner, no polling substitute built.

## uac-r2 files relied on (re-read on origin/docs/ontology-platform-design, 2026-10-11)

`SecureChildLineage.cs`, `config/secure-record-owner-role.json`, `RecordOwnershipResolver`, `CoreAncestorResolver`, `ExternalCallerContext`, `RouteAuthorizationGuardTests.Ledger.cs`: none edited; ownership goes through `SignalWriter` (task 039) unchanged. No new route (the ledger is untouched). Open PRs checked: #1609 (046, `DecisionRouteCores.cs`), #1653 (033) and #1654 (034) overlap `SignalReevaluationJob.cs`, `SignalsModule.cs` and `SignalReevaluationJobTests.cs` (line-level merge only).

## Checks

Publish size (Release, framework-dependent linux-x64, Compress-Archive Optimal over the publish folder, fresh detached worktrees at C:\wts-032m / C:\wts-032p, both 192 files): base 36.670 MB, branch 36.677 MB, delta +0.007 MB (re-measured after the review fix and the base merge). `dotnet list package --vulnerable --include-transitive`: none.
Mutation control: removing the subject-prefix scope fails `Trigger_DoesNotCloseAnotherSubjectsOpenSignal`.
