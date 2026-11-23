# 095b - Notify the submitter when an invoice submission ends

Task: `tasks/095b-submission-completion-notification.poml`. Code: branch `worktree-agent-a00d7e2496d172b6c`, commit `7d5604d65d`. Owner scope and channel confirmed 2026-11-03: the in-app bell (Dataverse `appnotification`) through the existing `NotificationService`. No email, no Teams.

## 1. What shipped

| File | Change |
|---|---|
| `Services/Finance/Submission/SubmissionOutcomeNotifier.cs` (new) | `SubmissionOutcomeNotifier.NotifyAsync(submissionId, outcome, invoiceId)`, `SubmissionOutcomeKind`, `SubmissionOutcomeNotifierEvents` (EventIds 50396 `BellNotCreated` Warning, 50397 `NoRecipient` Information, 50398 `BellCreated` Information) |
| `Services/Jobs/Handlers/InvoiceSubmissionProcessingJobHandler.cs` | one new ctor parameter and 6 call sites (section 3) |
| `Infrastructure/DI/FinanceModule.cs` | `services.AddScoped<SubmissionOutcomeNotifier>()` beside the sink registration (unconditional) |
| tests | `SubmissionBellWorld.cs` (Dataverse double over the REAL notification writer), `SubmissionOutcomeNotifierTests.cs`, 9 handler tests and 3 flow tests (24 tests carry `095b` in the display name) |

The notification text: title `Invoice {firm invoice number} accepted | could not be processed | refused | held for review`; body `Tracking number {n}. {one fixed outcome sentence}`. No amount, no finding, no rule text, no person name. Priority Informational for Accepted, Warning otherwise. Link: the invoice record (`/main.aspx?etn=sprk_invoice&id=...`) for an Accepted outcome only; every other outcome (Refused by the rule, Blocked and held, Failed) links the submission record (`/main.aspx?etn=sprk_invoicesubmission&id=...`), as the POML says "no invoice link" (review fix F4, section 7; before it a rule refusal linked the invoice it had created). Recipient: `sprk_createdbyperson` of the submission; none means nothing is created and `NoRecipient` is logged at Information.

## 2. Component justification (CLAUDE.md section 11)

1. **Existing.** `NotificationService` (reused unchanged; `GrantAccessNotifier` and `AssignTaskNotifier` are the precedents) and `IEbillingNotificationSink` (049's seam, a no-op until task 132).
2. **Extension.** The sink cannot carry this. The after-steps call it BEFORE they write the terminal status, so it would announce a status that is not committed yet; it never reports Failed; and it is task 132's seam (firm, SPA feed, approvers), whose registration 132 replaces. `NotificationService` needs no change; the new class is only the composition (who, text, link) and the "never fails the submission" shell.
3. **Cost of doing nothing.** A submitter who closes the Outlook pane (or the SPA tab) never learns the submission was accepted, refused or failed, and a failed one is never resubmitted.

No new interface (ADR-010): the class is concrete and tests run the real class over a Dataverse double.

## 3. Placement (CLAUDE.md section 10, ADR-004 / ADR-052)

In the BFF, inside the existing `InvoiceSubmissionProcessingJobHandler` (ADR-004 `IJobHandler`), after the final status is committed. No new `BackgroundService`, timer, queue, endpoint, package or Dataverse column. The ADR-052 host decision is unchanged: the work is a few Dataverse calls at the end of an existing queue-driven job, so the BFF is the host. Publish-size delta: not measured here (the main session measures it; one new 192-line class, no package).

### Final-status points hooked

| Final status | Where it is written | Hook |
|---|---|---|
| Accepted, and Rejected by the rule or Blocked (internal, held) | the after-steps' terminal write (row-version conditional), reported by `MapAfterStepsAsync` `Completed` | notify when `!result.AlreadyTerminal` (Accepted -> Accepted, linking the invoice; Rejected -> Refused and Blocked -> held, both linking the submission, F4) |
| Rejected, document not read (no invoice) | `RejectUnreadableAsync`, the handler's own write | after the write |
| Failed, no invoice | `FailAsync` (permanent failures, flag off, snapshot faults) and `TransientAsync` on the final delivery | after `MarkFailedAsync` |
| Failed, commit undone | the commit gate's write, reported as `Undone` by the persister (`case Undone`) and by the after-steps (`MapAfterStepsAsync` `Undone`) | before `UndoneAsync`, in both |

Not final, so not hooked: a linked submission left Processing (`LeaveLinkedSubmissionAsync`, `Refused`), a busy lock, a transient failure before the final delivery. Not hooked on purpose: `SubmissionOverrideService` (Override and Return are decisions by a person who is not the submitter; Return already goes to the firm through the sink).

## 4. Exactly once

The notifier itself holds no state. The handler calls it only from the delivery whose own write moved the submission into its final status:

- **Accepted / Rejected through the after-steps:** the terminal write is conditional on the row version (`BulkUpdateIfRowVersionsMatchAsync`), so one delivery wins. A delivery that finds the submission already final gets `Completed` with `AlreadyTerminal = true` and the handler does not notify.
- **Failed and the handler's own Rejected:** the run holds the per-submission processing lock, and step 0 re-reads the row and returns before any write when the status is already Accepted, Rejected or Failed.
- **A redelivered job, a replay of a dead-lettered message, and a dead-letter after Failed** all reach step 0 with a final status and exit as a no-op Success, so they never reach a call. The commit gate's held-undo compensation (step 0, status already Failed) does not notify either; the delivery that undid the commit did.
- **A replay of the same email** does not create a second submission job here (095's intake answers with the existing submission); if a new submission is created it is a new submission with its own outcome.

Proved by `RedeliveryAfterTheFinalStatus_NeverNotifies`, `CompletedAlreadyTerminal_TellsNobody`, `GateUndo_TellsOnce_TheCompensationDoesNot` and, end to end over the real after-steps, `Accepted_TellsTheSubmitterOnce_ARedeliveryAddsNone`, `ResumeThatWritesTheTerminal_TellsOnce`, `FailedAfterTheLastDelivery_TellsOnce_TheReplayAddsNone`.

Choice: the status transition is the claim, not a cache marker. `IIdempotencyService` is check-then-set and fails open (jobs.md), so a marker would be weaker than the transition. The price is at-most-once in one window (K1 below).

## 5. Outcome safety

`NotifyAsync` never throws and is bounded by a 15 second timeout (`DefaultTimeout`, as `GrantAccessNotifier.DefaultSendTimeout`); it ignores the job's token because the status is already written. Any fault (read, write, timeout, cancellation) is a Warning with `SubmissionOutcomeNotifierEvents.BellNotCreated` (50396) and the exception type only (ADR-015). The status, the `JobOutcome` and the retries do not depend on it: `NotificationFault_NeverChangesTheOutcome` proves an Accepted job still Completes and a Failed one still Poisons with its status written. Seeded proof: with the `catch` rethrowing, 4 tests fail (the notifier's two fault tests and both rows of the handler theory); restored, all 24 pass. The bound itself is proved by `NotifyAsync_WhenTheCallNeverAnswers_ReturnsWithinTheBound_AndLogsTheWarning` (review fix F3b, section 7): a read or a create that waits on its token, a 200 ms bound, a 20 s guard; it asserts `NotifyAsync` returns within the guard and logs the `BellNotCreated` Warning.

## 6. Tests (task scope: the acceptance cases; extras justified)

Notifier: Accepted links the invoice; Failed and Refused link the submission with the sentence; held-for-review (links the submission, F4); the time bound (a read that never answers, a create that never answers, F3b); no person (Information, nothing created); writer throws (Warning, EventId 50396, swallowed; seeded); read throws. Handler (strict fakes): Accepted, rule refusal (links the submission), Blocked (held, links the submission), unreadable, Failed (and no person), final-delivery Failed vs earlier delivery, redelivery after each final status, AlreadyTerminal, gate undo vs compensation, left Processing, notification fault (2 rows). Flow (real components): accepted + redelivery + replay; crash then resume; Failed after the last delivery + dead-letter replay.

Extras beyond the listed cases, one line each: `Compose_NamesOnlyTheNumbersAndTheSentence` pins the privacy constraint (a closed set the task states); the held-for-review test covers the one text variant the code has; gate-undo and left-Processing cases pin the hook points that are NOT calls.

## 7. Review log

- code-review (own, Step 9.5): no F-class finding. Cohesion: the handler is 909 lines and 13 ctor parameters but one reason to change (job-framework shell); the notifier is 192 lines, 3 parameters, one catch. No interface, no log-rethrow, no restating comments of note.
- adr-check (own): compliant with ADR-001, 002, 004, 007, 010 (concrete, no interface), 013, 015 (ids only), 017 (status persisted before the notification), 018 (unconditional, flag read by the handler), 052. One Warning: **ADR-047** (notification spine) is Proposed; this uses the Dataverse appnotification channel the owner confirmed and the two existing notifiers use. Path C for now (no violation while the ADR is Proposed); when 047 is Accepted the single `NotifyAsync` body is the one place to move to the spine.
- Independent adversarial review and the publish-size measurement: skipped here by instruction; the main session runs both.
- Class search (task-execute 9.5 rule 6): the pattern "a status written with no tell" has no other instance in this handler (all Failed / Rejected writes are the table above). `SubmissionOverrideService` is the one other writer of a final submission status (listed above, not changed).
- **Adversarial review round (fix-now findings, both fixed; the 095a notes section 9 carries the rest of the round):**
  - **F3b - the time bound was untested.** `NotifyAsync` gives every read and the create `bound.Token` (a 15 second `CancellationTokenSource`), but no test proved a call that never answers is given up. Added `NotifyAsync_WhenTheCallNeverAnswers_ReturnsWithinTheBound_AndLogsTheWarning` (a Theory: the read hangs, the create hangs) over `SubmissionBellWorld.Build(timeout)` with a 200 ms bound: the double waits on the token it is given (`ReadWaitsOnItsToken`, `CreateWaitsOnItsToken` in `SubmissionBellWorld.cs`), the test asserts `NotifyAsync` returns within a 20 s guard, that no bell is created, and that the `BellNotCreated` Warning (EventId 50396) is logged. Seed-proof: with `CancellationToken.None` in place of `bound.Token` in both calls, both rows failed (each at the 20 s guard, not a hang); restored, both pass.
  - **F4 - a rule refusal (and a Blocked outcome) linked the invoice.** The POML says "no invoice link" for them; `MapAfterStepsAsync` passed `result.InvoiceId` for every Completed outcome, so a refused invoice's bell opened the invoice the refusal had created. Now `InvoiceSubmissionProcessingJobHandler.MapAfterStepsAsync` passes the invoice id only when the written status is Accepted; Refused and held-for-review link the submission (`/main.aspx?etn=sprk_invoicesubmission&id=...`). Pinned tests updated and added: `CompletedRejected_TellsTheSubmitterOnce_LinkingToTheSubmission` (asserts the submission link, with a control that the after-steps result does carry the invoice id), `CompletedBlocked_IsHeldForReview_LinkingToTheSubmission`, and the notifier's `Blocked_IsHeldForReview_AndLinksToTheSubmission`. **The owner may prefer the invoice link for a rule refusal** (the invoice exists and shows the findings); if so, revert is one line (pass `result.InvoiceId` for every Completed outcome) and the three tests' link assertions.

## 8. Known limits and open items

- **K1 (at-most-once window):** a process crash between the final status write and the bell leaves that submission without a notification (the redelivery finds it final). Chosen over a possible duplicate; the portal and the pane still show the status.
- **K2:** `MarkFailedAsync` is unconditional (pre-existing). If a processing lock expired under a slow run and two deliveries both wrote Failed, both would notify. Needs a lock-expiry race; the lock is 20 minutes.
- **K3:** the notifier waits up to 15 seconds inside the held lock on a slow Dataverse; acceptable against the 20 minute lock.
- **K4:** the link opens the submission form for a no-invoice outcome; whether the submitter's role can read that row is not checked (a link they cannot open is harmless).
- **Coordination with task 132 (not a defect today):** 132 will report Accepted / Rejected to the firm, the SPA feed and approvers through the sink. If 132 also tells the internal submitter, they would receive two notices; 132 should treat the submitter's bell as done by 095b.
- The `Blocked` internal submission is told as "held for review" (title and sentence differ from "refused"); the task named only the three.
- Not changed, owner decision if wanted: Override / Return outcomes do not notify the original submitter.

rework: none task 095b
