# Task 017-d3 — chat `dataverse.create_record` + budget inquiry on RecordCreationService

Branch `task/fm-017-d3`, from `origin/task/fm-017` (e6c20bd51c). A separate file so it does not conflict with 017's notes; the
main session folds it in.

## Outcome: PARTIAL. Only the ebilling-r1 item landed; both re-points are STOPPED

| Item | State |
|---|---|
| ebilling-r1: chat refuses `sprk_externalidentifier` | **Done** (below) |
| Budget inquiry re-point | **Stopped**: a test suite under a forbidden path pins the old create (escalation B) |
| Chat create re-point | **Stopped**: an existing refusal code would change (escalation A); some design is still open (C) |
| Owner decision 2c (drop `sprk_matternumber` / `sprk_projectnumber` from the description) | **Held** with the chat re-point, as decision 2c says (it lands in the same change) |
| `ChatCreatedChildStampTests.cs` | Not committed (it tests the re-points) |

## Done: `sprk_externalidentifier` refused from chat (ebilling-r1, 2026-10-30)

- `DataverseCreateRecordHandler.ServerWrittenInvoiceFamilyTables` now also holds `ExternalIdentifierTable`
  (`"sprk_externalidentifier"`).
  - The mechanism is the same: refused in `ValidateChat` (before the suspend) and in `ExecuteChatAsync`, before any
    Dataverse call, with `VALIDATION_FAILED`.
- `ServerWrittenInvoiceFamilyCreateBlockedMessage` gives the external identifier its own middle sentence ("written … when
  an invoice is resolved to its matter"). The words for the three existing tables are byte-identical.
- Tests: `ChatServerWrittenTableBlockTests` adds a `sprk_externalidentifier` row to both theories (9 tests, was 7).
- Must-fire: with the table taken out of the set, the 2 new rows fail and the other 7 pass. Restored, 9/9 pass.
- `RecordTableRules` is untouched. Chat cannot create the table, whatever the registry accepts.

## Escalation B — budget inquiry: the existing suite pins the old create, under a path I may not edit

`tests/unit/Sprk.Bff.Api.Tests/Services/Signals/BudgetInquiryExecutorTests.cs` has about 20 tests.

- It builds the executor as `new BudgetInquiryExecutor(user, ownership, appOnly, sender, new HttpContextAccessor(), logger)`,
  with no HTTP context, and asserts the app-only create through `rig.AppOnly` (Sent, ids, the binds, the firm lookup,
  SendFailed with the id, cancellation after the row exists).
- Every way to reach the internal, scoped `RecordCreationService` breaks those tests:
  - **a constructor parameter:** CS0051, because the executor and `BudgetInquiryActionExecutor` are public. It would
    also force an edit to that test's constructor call.
  - **`HttpContext.RequestServices`,** as `DecisionRouteCores.CreateChildAsync` does: there is no context, so nothing is
    created, and the Sent tests fail.

  Keeping them green would need a fallback create, which NFR-01 forbids.
- Also, dropping the now-unused `appOnly` parameter fails CS9113 (unread primary-constructor parameter, as an error). The
  same test constructs the executor with it.
- **Needed from the coordinator:** permission to re-point `BudgetInquiryExecutorTests`'s rig. Either:
  - give it an `HttpContext` whose `RequestServices` holds `RouteRecordCreation.Over(...)`, and drop `appOnly`; or
  - move it to a KEEP path (test-diet).

  No assertion needs to weaken.
- **Implementation, ready to re-apply once allowed** (written and built, then reverted):
  - `ExecuteAsync` resolves `RecordCreationService` from `http.HttpContext.RequestServices`. With no context it throws,
    and the catch returns `CreateFailed`, with nothing created and nothing sent.
  - It creates with `RecordCreationPayload.Mapped(BuildItem(...))`, `RecordCreationPrincipal.ForCaller(user, CallerObjectId())`
    and `CorrelationId = TraceIdentifier`.
  - Refusal map, `internal static BudgetInquiryResult RefusalResult(refusal, correlationId)`:
    - Kind `Unavailable` (`check_failed`, `owner_unavailable`, `stamp_unavailable`) → `CreateFailed`. The old core threw
      on a fault, and the throw became `CreateFailed`.
    - Every other kind (`stamp_underivable`, which is `Invalid`, included) → `Refused`, as the old core's refusals were.
    - `Error` = `RecordCreationProblemMap.ToProblemDetails(refusal, Table, correlationId).Detail`: the uniform not-found,
      and fixed words for a fault or an upstream failure.
  - Before vs after, outcome by case (`BudgetInquiryActionExecutor.ToOutcome` is unchanged, so `Refused` → `InvalidState`
    and `CreateFailed` → `WriteFailed`, as before):

    | Case | Before | After |
    |---|---|---|
    | caller lacks Create, Append or AppendTo | Refused, the core's words | Refused, the map's words (uniform not-found for AppendTo) |
    | secure matter | Refused (`SecureFilingRefused`) | Refused (`OwnerUnresolved` / `NoOwnerSource`) |
    | owner, resolver or check fault | CreateFailed, "could not be created." | CreateFailed, the map's 503 words |
    | the I-1 stamp cannot be derived | n/a | Refused (`stamp_underivable`) or CreateFailed (`stamp_unavailable`) |
    | service unresolvable or throws | n/a | CreateFailed; nothing created or sent |
  - `PreflightAsync` stays on its own WhoAmI and `OwnedChildWrite.OwnershipOf`. It is a dry run, and the service has no
    preflight mode. 2d-5 must keep `OwnershipOf`, or give the service a preflight.
  - The tests were drafted for `ChatCreatedChildStampTests.cs`, as a partial of `SecureChildOwnershipAiToolTests`:
    - Sent with the team owner, the I-1 bind and the creator person;
    - no service: CreateFailed, nothing created;
    - no AppendTo: Refused in the uniform not-found;
    - a resolver fault: CreateFailed in the 503 words;
    - a secure matter: Refused;
    - a theory over every refusal kind;
    - the two step-10 codes.

## Escalation A — chat: an existing refusal code would change (POML trigger 1)

An isolated root (a work assignment or project under a secure matter) whose completion does not finish:

- **Today:** a tool ERROR with `secured.ReasonCode` (`OwnedCreateResult`). This covers `RowStranded` (could not be shared
  and could not be removed) and `IsComplete: false` (shared, but securing was not finished).
- **Through the service:** a SUCCESS. The service turns both into `outcome.Warnings` text (`RecordCreationService.AfterCreateAsync`
  ~:1650-1665). The outcome carries no reason code (only `Isolated`), and the adapter must not parse the text.
- So the chat result changes from `Success = false`, code `sdap.*` to `Success = true` with a warning.
- Office was already 201 with a warning, so this hits chat only.
- Options:
  - (a) Accept it: the record exists and is told as a warning, matching Office and the route.
  - (b) A service change: expose the completion's reason code on `RecordCreationOutcome`, and have the chat adapter
    render today's error.
- **Recommendation: (a).** Then the coordinator records the contract change.

## C — chat design, settled or open (for the run that does the re-point)

- **"For" person (coordinator correction, 2026-10-30):** keep `WithForPersonAsync`, which is the handler's ONE WhoAmI plus
  the linked contact.
  - Pass the remapped item as `RecordCreationPayload.Mapped(forMapped)` with `ServerSetLookupColumns = {forColumn}`. That is
    today's server-set shape: the for-column costs no AppendTo.
  - Record it as a deviation from "no longer resolve WhoAmI themselves". The service writes no "for" column: step 9 leaves
    it to the adapters, and `ChatCallerIsTheForPerson` only describes it.
  - Per table (`RecordTableRules.ForPerson`), the value stays today's:
    - matter, project, work assignment: `sprk_assignedtointernal` → the caller's linked contact;
    - todo, event: `sprk_assignedto` → the linked contact;
    - communication: `sprk_sentby` → the caller's systemuser;
    - every other table: none.
  - Still to check in the re-point run: that the service treats the work assignment's copied `sprk_assignedto` (copied from
    a server-set source) as server-set. `OwnedChildWrite.CreateAsync` ~:378-389 does this.
    `SecureChildOwnershipAiToolTests.WorkAssignmentAssignedToDefault`'s
    `..._TheCopyOfTheServerSetForPerson_CostsNoAppendTo_...` pins it.
- **Accessibility:** `DataverseCreateRecordHandler` is public, and `RecordCreationService` is internal (CS0051).
  - Make the handler `internal`. Tool discovery uses `assembly.GetTypes()`, and the tests and ArchTests have
    `InternalsVisibleTo`.
  - Every test that constructs the handler then needs the extra constructor argument (about 10 files). Several are under
    `tests/unit/Sprk.Bff.Api.Tests/**`; check before starting.
- **`columnsSet`:** `RecordCreationOutcome.WrittenColumns` holds the body's keys: bind keys, the owner, the creator. The old
  value was `item.Columns` (logical names), and tests pin `columnsSet` (e.g. "sprk_assignedto"). Report `forMapped.Columns`
  plus the copied defaults the outcome wrote, translated back from the bind keys through the item's lookups.
- **Refusal map, by kind, to today's chat codes** (text from the shared map):

  | Kind | Code |
  |---|---|
  | `PrincipalUnresolved`, `Upstream` | `ClientFailure.ErrorCode` (today's `MapClientError` code) |
  | `NotFoundOrDenied`, `BlockedColumn`, Forbidden `record_creation.denied` | `AccessDenied` |
  | Forbidden, any other code (plan codes) | `refusal.Code` |
  | Invalid `invalid_payload` / `table_unsupported` / `stamp_underivable` | `VALIDATION_FAILED` |
  | Invalid or Undecided with `PreWrite.Code` | `refusal.Code` (invoice guard) |
  | OwnerUnresolved with `OwnerDecision.IsSecureOwner` (secure filing refused) | `VALIDATION_FAILED` |
  | OwnerUnresolved, any other | `refusal.Code` |
  | Undecided `no_decision` | `INTERNAL_ERROR` |
  | Undecided, any other | `refusal.Code` (plan or provisioning) |
  | Unavailable (`stamp_unavailable` included) | `INTERNAL_ERROR` |
  | Conflict | `refusal.Code` |

  Check the `BlockedColumn` row against `CallerWriteAuthorization`: today a server-owned column is refused as `Denied`,
  which is 403 `child_record.denied` on the route.
- **New behaviour to state in the PR:**
  - an unregistered Owned-path table (e.g. `phonecall`) becomes `VALIDATION_FAILED` (`table_unsupported`), where today it
    is created;
  - `sprk_matternumber` / `sprk_projectnumber` are refused as BlockedColumn (decision 2c).
- **Owner parity (coordinator item 4):** pin chat's owner unchanged against today for one stamped case (e.g. a todo under an
  event under a matter). Step 10 changes only the create body.

## Out-of-scope findings

- `BudgetInquiryExecutor.PreflightAsync` (~:160-190) keeps its own WhoAmI and an `OwnedChildWrite.OwnershipOf` mirror of the
  create core's secure rule; its remark says a parity test pins it. Once the create is on the service, that mirror must
  follow the service's step 7.
- `RecordTableRules.cs:443`: `ChatCallerIsTheForPerson`'s rule text names `DataverseCreateRecordHandler.WithForPersonAsync`.
  It stays true under the decision above.
