# Task 149 — gate Decision Records: one per decision, written by an ADR-004 queue job (D-139, D-144)

> Branch `task/ontology-149-gate-dr-queue` (worktree `C:\wts-149`), cut from `origin/docs/ontology-platform-design` at `492d00bd1`.
> POML: `tasks/149-gate-decision-record-queue-idempotent.poml`. Builds on task 042 (the gate entry point) and 142 (the writer contract).
> uac-r2 must review the D-144 ownership choice (section "D-144 as amended") before merge.

## What was built

| Piece | Where | What it does |
|---|---|---|
| Enqueue | `RuleGatedAssessedConsumer.EnqueueDecisionRecordAsync` (`CommunicationRuleGate.cs`) | ONE call site, after `_gate.EvaluateAsync` and before `if (decision.Authorize)`, so authorize and deny both queue. It does not wait for the record; the RI action runs next. The rule-priority read moved out of the gate's path (the job does it). |
| Enqueuer | `GateDecisionRecordEnqueuer` (`Services/Signals/GateDecisionRecordJob.cs`) | Builds the `JobContract` (`IdempotencyKey = GateDecisionKey.Of(communication, outcome)`, `CorrelationId` = the run id) and submits it with a 5 s timeout. Never throws: a failure or timeout is logged with ids (`GateDecisionRecordEvents.EnqueueFailed` Error / `EnqueueTimedOut` Warning) and counted on `ontology.writer.failures{reason=decision_gate_enqueue_failed}`. |
| Payload | `GateDecisionRecordPayload` | Ids, the gate's reason code and numbers, the matched rule id, the privilege flag, the direct-filed-under id and the decision time. No subject, sender or body (D-123); a test serialises it and asserts so. |
| Job | `GateDecisionRecordJobHandler : IJobHandler` (scoped, `SignalsModule`) | Lock under the decision key, build the request (reads the matched rule's priority), call `DecisionRecordWriter.WriteGateDecisionAsync`. Outcomes: written / already recorded / skipped by design (D-144) / another copy in flight = `Success`; unreadable payload or a refusal a retry cannot change = `Poisoned`; a fault or a retryable refusal = `Failure` (the processor abandons, redelivers, dead-letters after the last delivery). |
| Idempotency | `GateDecisionKey` and `FindGateRecordAsync` (`DecisionRecordWriter.cs`) | The key is a SHA-256 of `gate-dr-{communication}-{authorized|denied}`, rendered as a GUID, and stored in `sprk_steps.reviewId` (the column every record already carries its review id in: NO schema change, so the POML's first escalation trigger did not fire). The writer looks it up before its one create (Equal on `sprk_regardingcommunication`, Like on `sprk_steps`, `sprk_confirmedby` null so a human review cannot masquerade as the gate record, through the shared reader so any team's record is visible). A found record is returned as `AlreadyRecorded`. |
| D-144 (as amended) | `ResolveOwnerCoreAsync` / `ReadCommunicationOwnerAsync` | See below. |
| uac-r2 K3 | `SignalReadService.cs` | The no-Read refusal logs at Warning. One commit, one assertion in `SignalReadRouteTests`. |

## Placement (bff-extensions)

- **Where**: the BFF, as a queue consumer on the existing main-queue seam (ADR-004 `IJobHandler`, ADR-052: queue work whose host is the BFF because it
  reuses the ontology writer identity, the ownership resolver and the Dataverse client, all already in-process). No new `BackgroundService`, no timer,
  no new queue, no new package.
- **CLAUDE.md §11**: *Existing* - no job type writes Decision Records; `SignalEvaluationTrigger` / `SignalEvaluationTriggerHandler` is the nearest
  enqueuer and handler, for a different payload (it is bound to Signal evaluation kinds). *Extension* - `WriteGateDecisionAsync` (the writer's one
  create site) is reused and only gains the lookup; the enqueuer and handler are new because nothing carries a gate decision. *Cost of doing nothing* -
  each email spends about 6-9 Dataverse requests inline on the shared app identity before the RI action (throttling), and every enrichment retry
  writes a duplicate record.
- **New registrations (2)**: `GateDecisionRecordEnqueuer` (singleton, concrete) and `GateDecisionRecordJobHandler` (scoped `IJobHandler`; scoped because
  `IIdempotencyService` is). No new interface (ADR-010).
- **Facade (AI)**: not an AI path; no `Services/Ai` dependency added.
- **Package/CVE**: no package change. `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api`: no vulnerable packages.

## Publish-size delta (bff-hygiene item 4)

`dotnet publish -c Release -r linux-x64 --self-contained false -o deploy/api-publish/`, then `Compress-Archive -CompressionLevel Optimal` (PDBs included),
all three from short-path worktrees, measured in this session with one script.

| Build | Commit | Zip | Files | Raw |
|---|---|---|---|---|
| fresh `origin/master` (`C:\p149m`) | `f292ad119` | 37.1023 MB | 192 | 123.9175 MB |
| task base (`C:\p149base`) | `492d00bd1` | 37.1027 MB | 192 | 123.9171 MB |
| task head (`C:\p149h`) | `176ef2c29` (review-fix head; the fixes add one query condition, tests and notes) | 37.1052 MB | 192 | 123.9369 MB |

Delta vs fresh master: +0.0029 MB zip (37.1052 vs 37.1023), +0.0194 MB raw, +0 files. Far below the +5 MB / 55 MB / 60 MB thresholds.

## D-144 as amended (owner ruling on uac-r2 F1, PR #1907 comment 6198338714; `notes/decisions.md`)

**Amended.** The first version of D-144 owned an unfiled communication's gate record by the communication's owning business unit's default team. uac-r2
(F1) showed that exposes the communication's subject (through the regarding lookup's display name), `sprk_privilegeflagged` and the fact snapshot to
every Basic colleague in that unit, contrary to D-142. The owner ruled:

- An unfiled communication (no matter or project, so the resolver keeps its creator) is no longer refused. Its gate record is owned by the
  communication's **own owning user**, as read from the row (`ownerid`, through the shared sysadmin reader) on the resolver's `Unchanged` path. It is not
  owned by the unit's default team, and nobody else is given a share.
- **Secure-if-any still comes first.** The writer asks `RecordOwnershipResolver` once more as a plain parent, but only for that check (uac-r2's
  predicate, not a copy of it). A secure answer (the communication is owned in the Secure Record BU) is used as returned: the NAMED Secure team. An
  ordinary team answer is discarded, never applied. `NoDefaultOwnerTeam` on that ask means "ordinary, and the unit has no default team": the team is not
  needed any more, so it no longer skips.
- **A missing or unreadable owner refuses and counts as a skip** (`gate_dr_skipped_no_unit_team`, Warning `DecisionRecordEvents.GateDecisionSkippedNoUnitTeam`
  50313, `ontology.writer.skips`, never `ontology.writer.failures`; the job completes). Missing = no row, no `ownerid`, an empty id, or an owner that is
  not a `systemuser` (a team here would contradict the first ask). The communication's business unit unreadable (`ParentUnresolved` on the secure check)
  is the same skip. It never falls through to any team. A fault in the owner read is a failure (`OwnerResolutionFailed`), not a skip. The reason and
  event names are kept (stable for the alert rules already on dev); only their text changed.
- Scoped on purpose: only `ParentUnresolved` / `NoDefaultOwnerTeam` on a communication that stands alone. A FILED communication keeps its prior
  behaviour; a core record whose BU cannot be read stays a fail-closed `OwnerRefused` failure, not a skip.
- One create site: the writer still creates through the one private `CreateAsync`. The owner is applied in `FiledAndOwnedAsync` (`ownerid` = the
  principal), so the D-34 ArchTest stays green.
- **Re-file.** A later secure filing of the communication still moves a user-owned gate record: the secure-child walk reaches the record through
  `sprk_regardingcommunication` (`SecureChildLineage`). Proved with the real `SecureChildReconciler` and restamper over the share world
  (`OntologyRestamp_AUserOwnedGateDecisionRecord_FollowsItsCommunicationIntoIsolation_...`, both routes), not only at resolver level.
- **Accepted exposure (owner, no code).** Spaarke Console User and Spaarke Ontology Administrator hold Deep read on `sprk_decisionrecord` but only Basic
  read on `sprk_communication`, so holders of those roles in the owner's business unit or a parent unit can read the record, including the subject and
  `sprk_privilegeflagged`. Plain Basic colleagues cannot. No role changes.
- **To verify live at deploy (not provable here):** creating a record with `ownerid` set to another user needs the writer identity to hold Assign on
  `sprk_decisionrecord`. The unit tests prove the payload; the live seam test (`DecisionRecordWriterSeamTests`) is where the create is proved.
- The task 042 reason `gate_dr_skipped_unfiled` and its event are replaced by the new ones (they were never shipped to a dashboard; only comments name the old one).
- Size: writer change plus log/comment text in the job; no new type, package or DI registration, so the publish-size delta above stands.

## Acceptance criteria, closed

| Criterion | Proof |
|---|---|
| Authorize and deny each enqueue one job; the RI action does not await the record | `RuleGatedAssessedConsumerDecisionRecordTests` (both outcomes; order asserted); `RiActionsViaSeamSeamTests` events: `decision-job-enqueue` first, the record last, after the RI action |
| Same job twice / two jobs for the same decision: one record | `GateDecisionRecordJobTests`, `DecisionRecordWriterTests.GateIdempotency`, seam retry test (two jobs, two job ids, one key, one record) |
| Different outcome, second record | writer and job tests |
| Enqueue failure: gate and RI action unchanged, logged and metered | consumer test; enqueuer tests (exception, timeout) with `GateEnqueueFailed` counted |
| Payload has no subject or sender | payload serialisation test |
| Unfiled: one record owned by the communication's own owner, never the BU default team; Secure BU still gets the named team (D-144 as amended) | writer + job tests with the real ownership directory |
| Missing or unreadable owner (no row, no owner, a team, no BU): nothing written, skips counter, never failures, never a team (D-144 as amended) | writer tests with both metrics listened to |
| Empty-worklist refusal at Warning | `SignalReadRouteTests.CallerWithNoSignalRead_GetsAnEmptyList_NotAnError` |
| Single-create-site ArchTests stay green | `DecisionRecordContextImmutabilityTests`, `RecordOwnerAssignmentCensusTests`, `SecureFamilyConsistencyTests`: 44/44 |

Test scope: the stated contract and the cases above. Seeding proof (mutation) on the idempotency check only: with `FindGateRecordAsync`'s result
ignored, 10 of 172 tests in the writer, job, consumer and seam classes fail (writer 4, job 5, seam 1); restored, 467/467 in the wider filter. Separately, with the Warning
turned back to Information the K3 assertion fails (1/1).

## Tests run (filtered; never the unfiltered `Sprk.Bff.Api.Tests`)

| Suite | Result |
|---|---|
| `Sprk.Bff.Api.Tests` filtered to DecisionRecordWriter, GateDecisionRecordJob, RuleGatedAssessedConsumer, RiActionsViaSeam, SignalEvaluationTriggerHandler, SignalWriter, DecisionCommit | 467 passed, 0 failed |
| `Sprk.Bff.Api.IntegrationTests` filtered to `SignalReadRouteTests` | 38 passed, 0 failed |
| `Spaarke.ArchTests` filtered to the three create-site / census / secure-family classes | 44 passed, 0 failed |
| BFF build | 0 errors, 0 warnings |

## Review record

One independent adversarial pass (CLAUDE.md §8.5; the task is not `auth`/`security`/`tenant-isolation`). Fixed in the review round (head `176ef2c29`):

- **F1 - review-id spoofing.** `FindGateRecordAsync` matched any record under the communication whose `sprk_steps.reviewId` equalled the gate key, so a
  human review commit carrying a client-chosen review id equal to that key would have stopped the gate record from ever being written. The lookup now
  also requires `sprk_confirmedby` null (a gate record never sets it; a review record always does). Test: a review-shaped seeded row (confirmer set,
  same key) is not "already recorded" and the gate record is written. Seeding proof: with the condition removed, that case and the lookup-shape
  assertion fail (2 of 120 in `DecisionRecordWriterTests`); restored, 315/315 in the filtered unit run.
- **F3 - publish size.** The head row and delta above were still placeholders; measured and filled in.
- **F4 - guard coverage.** `DecisionRecordWriterOnlyCreatesAndReads` scanned `_writerClient` only; the writer's `_entities` (the shared sysadmin
  `IGenericEntityService`, which can create and update) is now held to `RetrieveMultipleAsync`, with a must-fire control (`_entities.CreateAsync`) and
  a must-not-fire control (`_entities.RetrieveMultipleAsync`). Also proved against the real writer: a seeded `_entities.UpdateAsync` fails the test.

Known limits K4-K9 from the same pass are accepted as known-limit class (no confirmed defect on a real path) and are not changed in this round; K5 is
recorded in detail because it is observable.

- **K5 - an outcome that flips back writes two records, not three, by design.** deny -> authorize -> deny leaves two records (one denied, one
  authorized): the key is the (communication, outcome) pair (D-139), so the second deny finds the first deny's record and writes nothing. The
  record trail shows each outcome once, not each change of outcome.


## Known limits (K) and what was seen but not fixed

- **K2 - ADR-004 atomic receive-side idempotency (#984).** ADR-004 asks for an atomic mark (SET NX or a conditional upsert on a natural key).
  `IIdempotencyService` is check-then-set and fails open, and `sprk_decisionrecord` has no alternate key on the decision key (adding one is a schema
  change the owner decides). Two jobs for the same decision that run at the same instant can both pass the lock and both find no record. Mitigations in
  place: the lock (narrows the window), the durable lookup before the create (the real guard, sequential retries, redeliveries and a timed-out create
  that landed are all caught), the deterministic key. Closed by #984 or by an alternate key. Recorded here as a documented project exception (CLAUDE.md
  §6.5 path A) for the PR to cite.
- **K3 - rule priority is read at job time.** A rule edited between the decision and the job shows its later priority in the snapshot; the rule id is the
  decision's.
- **K3 - no inline fallback and no outbox.** A failed enqueue writes no record for that run (D-139); the next enrichment run of the same communication
  queues it again, and the key makes that safe.
- **K3 - the 5 s enqueue timeout can delay the gate** in the worst case (same pattern as `SignalEvaluationTrigger`).
- **K2 - skip classification edge.** A communication filed only under a non-core parent whose business unit is unreadable is counted as a skip
  (`NoDefaultOwnerTeam` / `ParentUnresolved` after the plain-parent ask), not a failure. It writes nothing either way.
- **Pre-existing, not touched (D-106).** `dotnet format whitespace --verify-no-changes` reports four lines in
  `tests/unit/domain/Signals/SignalWriterTests.cs` (1553-1555, 1589), none in lines this task changed (the task adds one ctor argument at 1584).
- **Note for operators.** The repo's pre-commit hook (lint-staged) takes a transient `git stash` backup while it runs; it was not run by this task's
  commands and left the shared stash list untouched.

## Review pass 2 (CLEAN, no F-class finding) - known limits recorded and hardening

- **K1 - gate records written inline by task 042 before this deploy carry a random review id.** The first re-gate of such a communication with the same
  outcome therefore finds no record under the deterministic key and writes one more record. It happens once per communication. On spaarkedev1 the only such
  rows are zz-146 test data, which is being deleted; no other environment runs ontology code.
- **K2 - hardened (this round).** `DecisionRecordContextImmutabilityTests` now treats every `\b_entities\b` token in the comment-stripped writer as a
  violation unless it is the field declaration, the constructor assignment, or followed by `.RetrieveMultipleAsync(` (whitespace and newlines allowed). That
  closes `?.` calls, local aliases, method groups, helper hand-offs and split calls. Must-fire controls: `?.`, alias, split `_entities\n.CreateAsync(`, method
  group, helper argument; must-not-fire: one-line and multi-line `RetrieveMultipleAsync`, the declaration and the constructor.
- **K3 - the seam fake in `RiActionsViaSeamSeamTests.cs` does not evaluate the `sprk_confirmedby` Null condition** of the gate-record lookup. The unit-level
  harness (`DecisionRecordWriterTests`, the review-shaped seeded row) covers it.
- **K4 - covered (this round).** `Gate_AnUnfiledCommunicationOwnedInTheSecureRecordBusinessUnit_GoesToTheSecureTeam_NeverTheBusinessUnitDefaultTeam_D144`
  runs the real `RecordOwnershipResolver`: an unfiled communication held in the Secure Record BU is owned by the NAMED Secure team (a secure child), never
  the BU's default team. Seeding proof: with the writer's plain-parent ask replaced by the Secure default team, the case fails (expected the named team).
- **K5 - the non-core-parent unreadable / too-deep case is counted as a skip, not a failure.** Already listed above as "skip classification edge".

## Fix round on uac-r2 F1 (D-144 amended): seeding proof

With the writer's ordinary-team answer put back as the owner (the first version of D-144), 8 of 186 filtered unit tests fail: the owner assertions, the
Basic-colleague case, the discard case and the missing-owner skips. Restored: 186/186. The re-file test is about the reconciler, not the writer, so the
seeding does not apply to it; it passes against the unchanged reconciler because the walk already reaches the record through `sprk_regardingcommunication`.

## uac-r2 re-check of the D-144 fix round (F3, K1, K2)

- **F3.** The second (secure-if-any) ask is now exercised: with the first ask `Unchanged`, a second-ask refusal of `SecureOwnerTeamUnresolved`,
  `SecureParentNotIsolated` or `SecureBusinessUnitAmbiguous` refuses (`OwnerRefused`), and a fault in it gives `OwnerResolutionFailed`. Nothing is
  created and the communication's owner row is never read. Seeding proof: routing any second-ask refusal except `ParentUnresolved` to the user owner
  fails the three refusal cases (the fault case holds either way, as the ask itself throws).
- **K1.** The Basic-colleague test also asserts on the shared entity service: only reads, no share or access row.
- **K2.** A DISABLED owning user is a counted skip, not a failure: `gate_dr_skipped_owner_disabled` (Warning, event 50314, `ontology.writer.skips`), the
  job completes, and it never falls to a team. `isdisabled` is read in the same request as the owner (a left outer link from the communication to
  `systemuser`), so there is no extra read. Seeding proof: with the check disabled, the writer test and the job test fail.
- Census wording changed as uac-r2 asked (the Routed entry now names the two second-ask answers that lead to the owner).

