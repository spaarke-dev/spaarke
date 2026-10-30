# Task 017-d3: chat `dataverse.create_record` and budget inquiry on RecordCreationService

Branch `task/fm-017-d3`, from `origin/task/fm-017` (e6c20bd51c). This is a separate file so it does not conflict with 017's
notes; the main session folds it in.

## Outcome: done

| Item | State |
|---|---|
| ebilling-r1: chat refuses `sprk_externalidentifier` | Done (commit `139c7ea90b`) |
| Chat create, Owned path, through `RecordCreationService` as the caller | Done |
| Budget inquiry `ExecuteAsync` through `RecordCreationService` as the caller | Done (coordinator decision B: the existing test rig was re-pointed, with no assertion weakened) |
| Owner decision 2c: drop `sprk_matternumber` from both description copies | Done. `sprk_projectnumber` was never listed. The handler and `infra/dataverse/sprk_analysistool-dataverse-create-record-row.json` are byte-equal, and `CatalogToolDescriptionParityContractTests` passes |
| `ChatCreatedChildStampTests.cs`, including owner parity | Done (20 tests) |
| Additive service change (coordinator decision A) | Done: `RecordCreationOutcome.IsolatedCompletion` |

## The additive service change (decision A)

The new property is `RecordCreationOutcome.IsolatedCompletion` (`SecureRootInheritResult?`).

- `AfterCreateAsync` takes a callback that receives the completion's result.
- `CreateAsync` sets the property on both outcomes:
  - the created outcome;
  - the "removed again" refusal.
- The property is null when no completion ran.
- Nothing else in the service changed. Office and the route ignore the property.

The chat adapter uses the property to keep today's tool ERROR with `secured.ReasonCode`, for both `RowStranded` and
`IsComplete: false`. It is never a success with a warning.

- Tested by `ChatCreate_AnIsolatedRootWhoseSecuringDidNotFinish_KeepsTodaysErrorAndItsReasonCode` (adapter).
- Through the service, tested by the existing `SecureRootInheritanceWriterTests` and `SecureRootCreateRouteTests` chat cases.
- Must-fire: with the property left unset, 5 of those chat cases fail.

## Deviation: the chat "for" person (coordinator correction)

The handler keeps `WithForPersonAsync`: ONE WhoAmI, plus the caller's linked contact.

- It passes the remapped item as `RecordCreationPayload.Mapped(forMapped)` with `ServerSetLookupColumns = {forColumn}`, so the
  "for" column costs no AppendTo.
- This deviates from "adapters no longer resolve WhoAmI". Step 9 of the service leaves the "for" person to the adapters.

Result per table (unchanged from today):

| Table | "For" column | Value |
|---|---|---|
| matter, project, work assignment | `sprk_assignedtointernal` | the caller's linked contact |
| todo, event | `sprk_assignedto` | the linked contact |
| communication | `sprk_sentby` | the caller's systemuser |
| every other table | none | — |

Verified: the service treats the work assignment's copied `sprk_assignedto` (copied from the server-set "for" person) as
server-set. `WorkAssignmentAssignedToDefault`'s `…TheCopyOfTheServerSetForPerson_CostsNoAppendTo…` passes through the service.

## Chat: existing refusals, before and after

The code comes from `RefusalErrorCode`. The text comes from `RefusalText`, which uses the shared map
(`RecordCreationProblemMap`) except where noted.

| Case | Before (old core) | After (service) |
|---|---|---|
| Caller client failure (OBO, 401, 429, 5xx) | `MapClientError` code | same code (`PrincipalUnresolved` / `Upstream` with `ClientFailure.ErrorCode`). The text is now the map's fixed words, not Dataverse's |
| Parent missing, or no AppendTo on it | `AccessDenied` | `AccessDenied` (`NotFoundOrDenied`), with the uniform not-found text |
| Caller lacks Create or Append | `AccessDenied` | `AccessDenied` (`Forbidden` / `record_creation.denied`) |
| Server-owned column in the item | `AccessDenied` (`CallerWriteAuthorization` `Denied`) | `AccessDenied` (`BlockedColumn`). **Verified row 1:** `CallerWriteAuthorization.cs:54-59` gives `Denied` |
| Invalid payload | `VALIDATION_FAILED` | `VALIDATION_FAILED` |
| Invoice-family guard (two core records) | its own code, "The record was NOT created: … (code)." | same code and shape (`PreWrite`) |
| Secure filing refused (`SecureFilingRefused`) | `VALIDATION_FAILED`, the core's sentence | `VALIDATION_FAILED`, the same sentence (`OwnerUnresolved` with `IsSecureOwner`; `refusal.Detail`) |
| Owner not decided | `RefusalCode`, "its owner could not be decided — …" | same code and shape |
| Isolated-root plan refusal (e.g. `sdap.provision.creator_no_access`) | `RefusalCode` | same code (`Forbidden` / `Undecided` with its own code) |
| Isolated row removed again (share failed) | `ReasonCode`, "…so the new record was removed again. Nothing was created." | same text and code |
| Isolated row stranded / unfinished | ERROR with `ReasonCode` | same (decision A) |
| Fault (resolver, check, create throws) | `INTERNAL_ERROR` | `INTERNAL_ERROR` (`Unavailable`), no fallback create |
| NEW: I-1 stamp cannot be derived | — | `VALIDATION_FAILED` (`stamp_underivable`) |
| NEW: I-1 stamp read fault | — | `INTERNAL_ERROR` (`stamp_unavailable`) |
| NEW: unregistered Owned-path table (e.g. `phonecall`) | created by the old core | `VALIDATION_FAILED` (`table_unsupported`), no create |
| NEW (2c): `sprk_matternumber` in the item | written | `AccessDenied` (`BlockedColumn`) |

**Verified row 2:** the copied work-assignment `sprk_assignedto` (see the deviation section).

**`columnsSet`:** this stays logical names. `WrittenColumnsOf` returns the item's columns plus each copied default's target
column, translated back from its bind key. It never reports the owner, the creator or the stamp binds.

## Budget inquiry: existing refusals, before and after

`BudgetInquiryActionExecutor.ToOutcome` is unchanged: `Refused` → `InvalidState`, and `CreateFailed` → `WriteFailed`.

| Case | Before | After |
|---|---|---|
| Caller lacks Create, Append or AppendTo | Refused, the core's words | Refused, the map's words (the uniform not-found for AppendTo) |
| Secure matter | Refused (`SecureFilingRefused`) | Refused (`OwnerUnresolved` / `NoOwnerSource`) |
| Owner, resolver or check fault | CreateFailed, "could not be created." | CreateFailed, the map's 503 words |
| NEW: I-1 stamp | — | Refused (`stamp_underivable`) or CreateFailed (`stamp_unavailable`) |
| NEW: service unresolvable or throws | — | CreateFailed; nothing is created or sent |

Rule: the `Unavailable` kind gives CreateFailed; every other kind gives Refused.

Text: `sprk_servicerequest` has no noun of its own in `RecordCreationService.NounOf`, which reads
`ChildRecordEndpoints.Noun`. The map therefore says "record", as in "A record this record is filed under was not found. The
record was not saved." `ChildRecordEndpoints.cs` is do-not-touch, so this is a finding for 2d-5 (below).

## Tests

`tests/integration/data-mutation/RecordOwnership/ChatCreatedChildStampTests.cs` is a partial of
`SecureChildOwnershipAiToolTests` with 20 tests:

- chat event under an event: the I-1 bind and I-16 Restricted are in the create payload. It uses the REAL resolver with the
  share world's column probe.
- owner parity: today's `OwnedChildWrite.CreateAsync` and the chat create give the same owner (`ChildTeam`) and the same
  creator. Today's core writes no stamp; the chat create writes it.
- the secure matter's sharee mirror on a chat to-do;
- `phonecall`: `VALIDATION_FAILED`, with no create call and no fallback;
- no AppendTo: `AccessDenied`, uniform not-found, no create;
- stamp read fault: `INTERNAL_ERROR`, no create;
- the app-only create throws: `INTERNAL_ERROR`, one attempt, no fallback;
- `IsolatedCompletion` stranded, unfinished, removed and complete;
- `columnsSet`;
- the refusal map over every kind, plus an every-kind-has-a-code check;
- the description (2c);
- budget:
  - Sent, with the team owner, the I-1 bind and the email;
  - no service: CreateFailed, nothing created or sent;
  - no AppendTo: Refused;
  - a resolver fault: CreateFailed;
  - a secure matter: Refused;
  - every refusal kind;
  - the two step-10 codes.

**Re-pointed (decision B; constructor or symbol moves only, no assertion weakened):**

- `BudgetInquiryExecutorTests` rig;
- `DecisionActionExecutorsTests`;
- `DataverseCreateRecordHandlerTests`;
- `DataverseToolNameFreezeTests`;
- `BusinessSliceDeterminismContractTests`;
- `P2LoopInjectionEvalSuiteTests`;
- `AssignedAccessWriterTriggerTests`;
- `SecureRootInheritanceWriterTests`;
- `ChatServerWrittenTableBlockTests`;
- `SecureChildOwnershipAiToolTests.CreateRecordHandler`.

`RouteRecordCreation.Default(...)` was added for these sites.

## Out-of-scope findings, for 2d-5

- `BudgetInquiryExecutor.PreflightAsync` (~:160-190) keeps its own WhoAmI and an `OwnedChildWrite.OwnershipOf` mirror of the
  old core's secure rule. The service has no preflight mode, so 2d-5 must keep `OwnershipOf` or give the service a preflight.
- `RecordTableRules.cs:443`: `ChatCallerIsTheForPerson`'s rule text names `DataverseCreateRecordHandler.WithForPersonAsync`.
  It stays true under the deviation above.
- `RecordCreationService.NounOf` / `ChildRecordEndpoints.Noun` has no noun for `sprk_servicerequest`, so the budget
  inquiry's refusal text says "record" twice.
