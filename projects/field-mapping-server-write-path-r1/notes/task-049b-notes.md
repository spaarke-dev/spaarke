# Task 049b — only the maker's subject inside the request (notes)

> Branch `task/fm-049b`, from `origin/master` (contains 049). Ruling: decisions.md, "049b: only the maker is granted
> inside the request (UAC-r2)". Finding: task-021-live.md, "Re-measure after 049" (inline pass, 5 subjects, 11 writes,
> about 6.6 s of PostCreate).

## Design

- `AssignedAccessRequest` gains an optional `OnlySubject`. When it is set, `AssignedAccessMaterializer.MaterializeAsync`
  evaluates that one subject (every registry field that names it) and **skips the ended-assignment loop entirely**, so a
  one-subject pass never revokes anything. It logs `{n} of {N} assigned subject(s) evaluated (one subject only; …)`, never
  the full-pass line. `RunAfterWriteAsync` / `AfterWriteAsync` take an optional `onlySubject`, and set
  `RevokeOnChange = false` for it (belt and braces: the revoke branches sit only in the skipped loop).
- `RecordCreationService.GrantAssignedAccessAsync`, maker case (Assigned To = the maker's own linked contact):
  1. inline: the materializer with `onlySubject = Contact(maker's contact)`;
  2. then the normal full pass is queued (`QueueAssignedAccessGrantAsync` → `AssignedAccessCreateGrantJobHandler`).
- Non-maker case: unchanged (queue; full pass inline if the queue fails).
- No new service, queue, job type or owner: the seam is a filter on the existing materializer (CLAUDE.md §11:
  extension of the one invariant owner).

## The conditions

1. **Same path.** The remainder is the existing create-grant job: the full materializer pass, under the per-record lease,
   with the settle loop and L1 revoke-on-change (G-8). Shape chosen: maker inline, then the full pass queued (not an
   "N−1 subjects" job), so there is no second scan order and no new payload field.
   **Idempotency, verified:** after the maker's pass the ledger holds a `Shared` row for the maker. The full pass reaches
   `ContinueOursAsync` (Shared branch): it reads the share mask, finds it, and `EnsureRowsAsync` skips a ledger write that
   already says the same. Zero writes for the maker (asserted in the materializer test and in both Office tests).
   **Ordering:** the maker's inline pass finishes before the enqueue, so the job never runs concurrently with it.
2. **Enqueue failure (maker case): the rest is NOT run inline.** Choice: log a warning naming the reconciliation job
   (`[ASSIGNED-ACCESS] … only the maker's own assigned subject was evaluated in the request; the create-time pass for the
   other assigned subjects was not queued, so the assigned-access reconciliation job completes it`), and the
   enqueue's own catch says "the assigned-access reconciliation job completes it" instead of "it runs inline". The
   backstop is `AssignedAccessReconciliationJob` (every 5 minutes, recently changed roots first; task-049-notes.md Part
   B item 4). Nothing logs or returns "all granted": the materializer's line says "1 of N", and the create's response
   never carried the grant outcome. Rationale: the ruling's text ("logged and the reconciler completes it"), and the full
   inline fallback is the 6.6 s this task removes. The non-maker case keeps its inline fallback (049 condition 4).
3. **Maker grant fails.** Unchanged: `RunAfterWriteAsync` never throws, logs, and the create stands. The full pass is
   still queued afterwards, so the queued job (then the reconciler) retries the maker as well.
4. **Tests** (below).

## Tests

| Test | File | Before (master 049) | After |
|---|---|---|---|
| `OfficeQuickCreate_ForItsMaker_GrantsOnlyTheMakerInline_AndQueuesTheRest` | AssignedAccessWriterTriggerTests | FAIL | pass |
| `OfficeQuickCreate_ForItsMaker_NeverGrantsAnExternalSubjectInline` | AssignedAccessWriterTriggerTests | FAIL | pass |
| `OfficeQuickCreate_ForItsMaker_WhenTheQueueFails_LogsIt_AndNeverReportsEverySubjectGranted` | AssignedAccessWriterTriggerTests | FAIL | pass |
| `OfficeQuickCreate_ForItsMaker_AFaultInTheMakersGrant_NeverFailsTheCreate` (condition 3) | AssignedAccessWriterTriggerTests | FAIL (nothing queued) | pass |
| `OfficeQuickCreate_ForItsMaker_SharesTheRecordWithThem_BeforeItReturns` ×2 (updated: one job is now queued) | AssignedAccessWriterTriggerTests | FAIL | pass |
| `CreateAsync_AnIsolatedCreate_ForItsMaker_RunsItsPassInline_OnceSecuringCompleted` (updated: one job is now queued) | RecordCreationServiceSecureRootSeamTests | FAIL | pass |
| `OfficeQuickCreate_ForAnotherAssignee_QueuesThePass_AndGrantsNothingInline` ×2 (control, unchanged) | AssignedAccessWriterTriggerTests | pass | pass |
| `AOneSubjectPass_GrantsThatSubjectOnly_AndTheFullPassAfterIt_WritesNothingMoreForIt` | AssignedAccessMaterializerTests | does not compile (new seam) | pass |
| `AOneSubjectPass_NeverSettlesAnEndedAssignment_TheFullPassStillRevokesIt` | AssignedAccessMaterializerTests | does not compile (new seam) | pass |

**Fail-first evidence.** The new and updated tests were run with master's `RecordCreationService.cs` copied back (and
the additive materializer seam kept, so the suite compiles): 16 run, **7 failed, 9 passed**. The 7 are the four new
Office tests, the two updated maker theories and the updated seam test, each failing for the intended reason:
the attorney's share, the external contact's grant or the organization's grant was written in the request, or nothing
was queued. The 9 passes are the controls (non-maker ×2, lookup fault, the two pre-existing materializer-fault tests,
two isolated-create seam controls) and the two materializer tests, which pass against master's service because they
exercise only the new seam; against master as a whole they do not compile. The service was restored afterwards.

**After.** Filter `AssignedAccess | RecordCreation | CallerChecksConcurrency | SecureFromCreation | OfficeQuickCreate |
SecureRootCreateRoute`: 1205 tests, **1202 passed, 3 skipped, 0 failed** (run twice). One earlier run of the same
filter reported 1 failure that did not reproduce in the next two runs (its name was not captured): treated as a flake.
`dotnet build src/server/api/Sprk.Bff.Api/`: 0 errors, 0 warnings. `tests/Spaarke.ArchTests` (full): 1973 passed, 0 failed.

## For UAC-r2

- **Condition 2 trade-off.** The reconciliation job runs with no grantor, so on the (rare) enqueue-failure path an
  external contact granted by the backstop gets no invite (the same known limit task 049 recorded for a dead-lettered
  job). Keeping the full inline fallback for the maker case would avoid that at the cost of the 6.6 s on that path only.
  Implemented: the reconciler, per the ruling's text. Flip is one line in `GrantAssignedAccessAsync` if UAC-r2 prefers it.
- The queued job covers the maker too (idempotent, zero writes for it). A maker-only inline grant that failed is retried
  by that job first, then by the reconciler.

## Fix round 1 (review K2, K4)

- **K2 - a one-subject pass acts only on a share.** In `DecideSubjectAsync`, right after `ResolveTargetAsync`, a request
  with `OnlySubject` set and a target that is not `TargetKind.Share` records the subject with `AssignedAccessAction.None`
  (state `Skipped`) and returns: no ledger write, no contact/organization grant, no invite. The queued full pass handles
  that subject. This makes "an external subject is never granted inline" a property of the materializer, not only of
  the caller. Test `AOneSubjectPass_ActsOnlyOnAShare_ANonShareSubjectIsLeftToTheFullPass` (unlinked contact with an email,
  internal grantor present so an invite would be sent): one-subject pass asserts no grant row, no write, no invite, no
  ledger row; the control full pass for the same setup grants and writes the Granted ledger row. Before the change the
  test failed (`Expected ... "none" ... but "granted"`); after it passes.
- **K4 - doc.** `AssignedAccessStatus.Evaluated` and `AssignedAccessOutcome.Complete` now say that for a one-subject pass
  they cover that subject only. Doc only.
