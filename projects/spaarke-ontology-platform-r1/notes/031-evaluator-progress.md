# Task 031 — the re-evaluating scheduled job: progress and decisions

Lane: `task/ontology-031-evaluator` (worktree `C:\wts-031`), branched from `origin/docs/ontology-platform-design` @ `459fc505d`.
Rigor FULL (POML), effort xhigh. This file is the lane's record (the project `current-task.md` belongs to the main
session and is not edited by this lane). The POML and TASK-INDEX are NOT marked complete here: the main session does both
after independent review.

## What was built

| File | What |
|---|---|
| `Services/Signals/SignalWriter.cs` | D-13 key `{code}\|{type}\|{id}\|{episode}` (`BuildDedupeKey(..., episode)`, `TryParseDedupeKey` reading from the right, prefixes); `SignalWriteRequest.Episode` (validated ≥ 1) and `SubjectRecordNumber` (R13); evidence-line fields on `SignalEvidenceRef` (`text`, `source`, `asOf`, `clause`); public status/resolution option values; `DeriveCoreRecordAsync` (read-only, the same derivation the write uses); `CloseAsync` (system resolutions only, optimistic-concurrency update, never a Decision Record); `ReassignNoCoreOwnerAsync` (037 hand-off, D-35) |
| `Services/Signals/SignalEvaluator.cs` | The side-effect-free core `EvaluateAsync(policy, subjectId?)`: gate → widened FetchXML → paging → per-item today (D-25) → core record → facts (§0.3) → witness lines (D-23/FR-48). Reads only |
| `Services/Signals/SignalReevaluationJob.cs` | The ONE `IScheduledJob` (`signal-reevaluation`, `30 2 * * *` UTC) plus the pure `SignalEpisodePlanner` (episode rules) |
| `Services/Signals/SignalEvaluatorHealthCheck.cs` | Diagnostic staleness check on `/healthz/catalog` (catalog tag), never liveness |
| `Services/Signals/OntologyWriterDataverseClient.cs` | `UpdateIfRowVersionMatchesAsync` (`ConcurrencyBehavior.IfRowVersionMatches`) |
| `Services/Signals/OntologyWriterEvents.cs` | EventIds 50306 heartbeat, 50307 policy/item failed, 50308 no-core reassigned |
| `Telemetry/OntologyWriterTelemetry.cs` | `ontology.evaluator.runs` (outcome), `ontology.evaluator.signals` (action), gauge `ontology.evaluator.last_evaluated` (D-61 run-level); reasons `signal_close_failed`, `signal_reassign_failed`, `dedupe_key_unparseable`, `write_failed` |
| `Services/Signals/PredicateCompiler.cs` | Two internal helpers only: `TryParseRelativeDays`, `FormatInstantForFetch` (the compiler's own grammar and format, no second parser) |
| `Infrastructure/DI/SignalsModule.cs` | Registers the evaluator, the job (`AddScheduledJob`) and the health check |
| `tests/Spaarke.ArchTests/RecordOwnerAssignmentCensusTests.cs` | One census entry for `ReassignNoCoreOwnerAsync` (uac-r2-owned file: **needs uac-r2 review**, flagged in the PR) |

## Decisions and deviations from the POML

1. **Registration in `SignalsModule`, not `RegistrationModule.cs`.** The POML's `RegistrationModule.cs` predates the Signals
   module; every Signals service registers in `Infrastructure/DI/SignalsModule.cs` (tasks 023–040).
2. **Reading enabled policies directly** (`sprk_enabled = true`, `statecode = 0`: the same filter as `PolicyScopeResolver`)
   rather than through `PolicyScopeResolver.ResolveAsync`, which matches one tenant/matter request. The nightly pass needs
   every enabled policy and its lane, severity and headline in one read. **A tenant- or matter-scoped policy is NOT evaluated
   nightly**: it is counted (`scopedNotEvaluated`) and logged at Warning (50307) every run. Dev has none. Defer row F-60.
3. **Episode rules** (`SignalEpisodePlanner`, pure):
   - Suppression grain: (policy, `sprk_corerecordtype`, `sprk_corerecordid`) for every core type; (policy, item) with no
     core record. Suppression blocks a FIRST episode too (FR-17 "no new Signal for that pairing surfaces").
   - Latest Open/Acknowledged: same episode. Core re-derived and different → close the old one as `ConditionCleared`, raise
     n+1 under the new core (037 hand-off (2)). No-core item reassigned → reassign the Signal, logged once (037 hand-off
     (1)). Due date changed → reconcile through the writer. Otherwise **no write at all** (D-61).
   - Latest Resolved: never closed again. Superseded / PolicyRetired → n+1 (FR-16, D-32); Do lane → n+1 only when the
     subject's date differs from the resolved Signal's `sprk_duedate`; Decide lane → n+1 unless Dismissed less than the
     quiet window ago. **Decide-lane Acted / ConditionCleared re-raise on the next pass when the predicate still holds**
     (FR-03 literal: only a dismissal has a waiting period). A Dismissed Signal with no `sprk_resolvedon` is held, not
     re-raised.
   - Not a hit → every open Signal closes `ConditionCleared`. A hit whose core record the writer refused is still a hit:
     neither written nor closed, counted refused (D-38).
4. **Closures** use the writer's dedicated identity with an optimistic-concurrency update: a Signal a person resolved
   after the read is not closed (`changedSinceRead`). Superseded and PolicyRetired closures stay with task 033.
5. **Run outcome.** `failed` when anything was refused, a policy was invalid or unreadable, or a key was unparseable. A
   policy with a non-episode-scoped key is not written at all that run (a duplicate episode 1 would otherwise be raised).
   Only a successful run stores the last-success time (distributed cache, shared across instances).
6. **Per-item today (D-25).** Each relative bound on a Date Only `when` field is widened by 2 days in the query and
   re-judged per row against that item's today (assignee → owner → UTC) through `DataverseRecipientDays` (task 098); a row
   whose verdict is the same for every possible today needs no time-zone read. Path B keeps the single anchor.
7. **Witness lines (D-23/FR-48).** One per `exists` clause: the matching row with the latest clause date; `kind` is the
   table without `sprk_`, `ref` `table:id`, `tier` `Fact`, `text`/`source` from a per-table map (communication: subject /
   sender; budget revision: name), `asOf` the clause's date field, `clause` `all[N]`. **No context lines are written**:
   an Existence rule reads nothing it does not test. Witness values never enter `sprk_sentence`.
8. **Fact snapshot.** `when` fields: the subject's value, normalized (option → int, lookup → id, money → amount). `exists`
   fields: the clause's pinned literal. A template token on an option-set field therefore renders the option VALUE (e.g.
   `100000001`), not its label; label rendering is a sentence-quality follow-up for the template authors (no R1 template
   uses such a token today).
9. **ADR-052 placement**: BFF `IScheduledJob` on the shared Spaarke.Scheduling host (Redis lease, admin jobs endpoint). No
   timer `BackgroundService`. **ADR-039 path A** (the BFF stays the single backend; no new process).

## R13 decision (`sprk_regardingrecordnumber`, `sprk_privilegeflagged`)

- `sprk_regardingrecordnumber`: **supplied now** (030's option (a)). The evaluator reads the subject's number from the field
  its `sprk_recordtype_ref` row names (`sprk_regardingrecordnumberfield`, ADR-024's fifth resolver field) and passes it as
  `SignalWriteRequest.SubjectRecordNumber`. Tables whose catalog row has no number field (To Do) or no catalog row
  (communication) get none.
- `sprk_privilegeflagged` (spec ADR-015 row, "copied forward; nothing branches on it"): **nothing to copy.** No verified
  subject table (matter, communication, To Do, event, work assignment) has the column (live metadata, 2026-10-10), so no
  task can copy it today; the Signal column stays empty. Owner: whichever task first adds a privilege flag to a subject
  table. Defer row F-62 so it is not lost.

## uac-r2 files relied on (re-read 2026-10-10, origin/master and origin/docs/ontology-platform-design)

| File | origin/master | project branch |
|---|---|---|
| `Services/Access/SecureChildLineage.cs` | `0615f9781` (signal + decision record registered) | `b2fc3fb78` (adds 044's budget revision only) |
| `config/secure-record-owner-role.json` | `0615f9781` | `b2fc3fb78` (adds 044's budget revision only) |
| `Services/Dataverse/RecordOwnershipResolver.cs` (I-6) | `d254d7166` | `d254d7166` |
| `Services/Dataverse/CoreAncestorResolver.cs` (FR-26, D-34) | `d7fdcafc3` | `d7fdcafc3` |
| `Infrastructure/ExternalAccess/ExternalCallerContext.cs` (ADR-003) | `d254d7166` | `d254d7166` |
| `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` | `d5e1dffe7` | `48742f4d2` |

No change since task 039 invalidates the plan: the writer's ownership path (resolver with the core record as target and the
subject as parent) is unchanged; 031 adds no route (ledger untouched) and calls the resolver only through `SignalWriter`.
Open PRs checked 2026-10-10: #1609 (046) and #1612 (uac-r2 pair AppendTo) touch none of 031's files. The only uac-r2 file
edited is the owner-write census (one entry, flagged for their review).

## Live schema read for this task (spaarkedev1, read-only, 2026-10-10)

- `sprk_signal.sprk_resolutiontype`: Acted 100000000, Dismissed 100000001, Condition Cleared 100000002, Superseded 100000003,
  Policy Retired 100000004. `sprk_signalstatus`: Open 100000000, Acknowledged 100000001, Resolved 100000002.
- `sprk_signal`: `IsOptimisticConcurrencyEnabled = true`; `sprk_regardingrecordnumber` String; `sprk_suppresseduntil`,
  `sprk_resolvedon` DateTime; `sprk_privilegeflagged` Boolean.
- No verified subject table (matter, communication, To Do, event, work assignment) has `sprk_privilegeflagged`.
- `sprk_recordtype_ref` catalog (`sprk_recorddisplaynamefield` / `sprk_regardingrecordnumberfield`): matter
  `sprk_mattername`/`sprk_matternumber`, project `sprk_projectname`/`sprk_projectnumber`, event `sprk_eventname`/
  `sprk_eventnumber`, To Do `sprk_name`/none, service request `sprk_name`/`sprk_servicerequestnumber`; `sprk_communication`
  has NO catalog row.
- `sprk_assignedto` (contact) exists on To Do, event and work assignment.
- One policy in dev: `POL-COMMIT-BUDGET` (Decide, disabled), current version v2; 0 Signals. No Dataverse write was made by
  this lane.

## Known limits (tests and acceptance criteria)

- **Fake fifth core type (D-36 criterion).** As in task 037, the core taxonomy is a static list in uac-r2's
  `CoreAncestorResolver`, so a fifth type cannot be added there without editing their file. This task's own code has no
  core-type branch; the planner test runs matter, project, work assignment, service request and a fake fifth core type
  (`sprk_fakecore` with its own catalog id) through the same grouping and suppression code unchanged.
- The job's tests run the real job, evaluator, writer, validator and `CoreAncestorResolver` over in-memory Dataverse seams;
  the `sprk_signal` columns and option values are pinned to the live read above and exercised live by `SignalWriterSeamTests`.
  No live run of the job was made (no deploy in this lane).
