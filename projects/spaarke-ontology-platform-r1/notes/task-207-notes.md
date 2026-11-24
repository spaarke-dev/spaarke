# Task 207 notes: Assign Task, Administrative-mode assignee gets its own "no email" warning (#3063, DI-171)

Branch `work/ontology-207-admin-account-warning`, cut from origin/master `6d167e0646`. Rigor FULL, sonnet @ high.

## Finding (from #3063 and uac-r2's read-only diagnosis, 2026-11-24)

The assignee's systemuser is an unlicensed administrator in Administrative access mode (`accessmode = 1`). Dataverse's
impersonated `RetrievePrincipalAccess` answers 403 for such a user (filtered privileges); the probe maps that to
`AccessRights.None`, so `AssignTaskRecipientGate.DecideInternalAsync` refused at "the user cannot read the task" with
`NoAccess`, and the assigner read "the assignee does not have access to this record". The refusal is right (such a user
cannot use the app); the sentence misled.

## Change

- `AssignTaskRefusal.AdministrativeAccount` (new), `AssignTaskRecipient.AdministrativeAccountWarning =
  "assignee_email_not_sent_admin_account"`, mapped in `AssignTaskRecipient.Warning`.
- `DecideInternalAsync`, only at the existing "the user cannot read the task" refusal (rights answered, no Read): when the
  one eligible user's `AccessMode == 1` the refusal is `AdministrativeAccount`, otherwise `NoAccess` as before. The log
  reason names the account mode.
- `AssignTaskNotifier`: no edit. Its job-path switch has `None`, `Unverified` and `default => NotAllowed`, so the new
  refusal falls into NotAllowed exactly as NoAccess does. The request path reads `recipient.Warning`.
- `decisionDisplay.ts` `WARNING_TEXT`: "The task was assigned, but no email was sent: the assignee's user account is set up
  for administration only and can't use the app."
- Constant `AdministrativeAccessMode = 1` is private to the gate (same value as `FlaggedUserPlacementJob`'s private const,
  which is uac-r2's file; not edited).

Unchanged (per the POML and uac-r2's guidance): no early refusal on access mode; an Administrative user whose rights answer
includes Read is emailed as before; an unanswered rights read stays `Unverified` for every access mode;
`InternalShareEndpoints.ClassifyEligibility`, `CallerRecordAccessProbe` and `DataverseWebApiService` untouched; who is
emailed does not change for any input; no other refusal branch changes code.

## Consumers of the warning codes (Grep `assignee_email_not_sent`, `AssignTaskRefusal.`, `Warning =>`)

- BFF: `AssignTaskRecipientGate.cs` (constants, `Warning` switch), `AssignTaskNotifier.cs` (`EmailNotSentWarning`; the request
  path returns `recipient.Warning` in its warning list; the job-path refusal switch). Neither holds text.
- Client: `decisionDisplay.ts` `WARNING_TEXT` / `describeWarning`, used by `ConfirmStep.tsx`. This is the only text table for
  these codes in this repo.
- Out-of-repo consumers: none found in this repo. The Copilot/MCP widget (mcp-r1) is the place a second table could live;
  nothing in `src/` references the codes, so if it keeps its own text for `assignee_email_not_sent_*` it is outside this repo
  or reads the generic fallback. **For the main session to pass on to mcp-r1:** a new code `assignee_email_not_sent_admin_account`
  now exists; an unknown code should fall back to generic wording (the shared `describeWarning` does).
- Older Console clients that do not know the code read the generic sentence ("The decision was recorded, but one follow-up
  step needs a look."), so no version gate.

## Section 11 (component justification)

- Existing: the `NoAccess` refusal and its warning, at the same branch.
- Extension: a reason refinement of that refusal (one enum value, one constant, one branch); nothing else can carry a
  different sentence without a new code (owner chose "new warning code" on 2026-11-24).
- Cost of doing nothing: the assigner is told the assignee lacks record access when the account cannot use the app at all,
  which sends them looking for a security-role problem that does not exist.

Placement: in the BFF, existing gate (`Services/Signals/AssignTaskRecipientGate.cs`); no new service, interface, DI
registration, endpoint or package.

## Tests (ADR-038)

BFF, `tests/integration/data-mutation/Signals/AssignTaskNotifierTests.cs` (compiles into `tests/unit/Sprk.Bff.Api.Tests`);
harness `LinkUser` gained an optional `accessMode`:

- Must fire: Administrative + rights without Read: gate returns `Refused(AdministrativeAccount)`, commit warning
  `assignee_email_not_sent_admin_account`, no email, no bell.
- Job path: the same assignee is refused (Completed, nothing sent).
- Must not fire: Administrative + Read is emailed; Read-Write (accessmode 0) + no Read keeps `NoAccessWarning`;
  Administrative + unanswered rights keeps `UnverifiedWarning`.

Client, `DecisionWizard.assignTaskNotify.test.tsx`: the new sentence, no internal code in it, generic fallback for an unknown
code; the existing sentence tests are unchanged and pass.

Failing first: with the enum value, constant and mapping present but the branch absent (the tests reference the new symbols,
so they cannot compile on bare master), `AssignTaskNotifierTests` ran 80 tests: 1 failed (the new-warning test: gate answered
NoAccess), 79 passed (the four controls). Client: with master's `decisionDisplay.ts` copied back, the task 207 test fails
(received the generic sentence); restored after.

## Defects found outside this task's scope

None.
