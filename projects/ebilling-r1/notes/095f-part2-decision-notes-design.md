# 095f part 2: a note on every invoice decision, and a note on every Correction (EB-D-236, EB-D-237): design for review

> **Status:** DESIGN DRAFT, 2026-11-15. No code has been written.
> **Base:** `work/ebilling-r1` at f9f74de82e.
> **Reviewers:**
> - ontology-r1, before the build: the parts marked **ontology** in §8, which touch the Decision Record writer or the action catalog;
> - word-add-in-r1 and spa-r4, for the shared client files in 2e;
> - the owner, for §10.
>
> **Decisions:** EB-D-236 and EB-D-237 (`notes/decisions-log.md:245-246`); D-75 (`:80`); EB-D-225 (part 1).
> **Path legend:** `BFF/` = `src/server/api/Sprk.Bff.Api/`; `Fin/` = `BFF/Services/Finance/`; `Api/` = `BFF/Api/Finance/`; `Writer` = `BFF/Services/Signals/DecisionRecordWriter.cs`; `Catalog` = `BFF/Services/Signals/Actions/DecisionActionCatalog.cs`; `Note` = `Fin/InvoiceCorrectionNote.cs`.

## 1. Rules common to every part

### 1.1 One validation, part 1's

Every new note field is checked by `Note.Check` (`Note:51-67`), exactly as the from-email route does in part 1 (`Api/InvoiceSubmissionFromEmailHttp.cs:265-286`):

- trim;
- empty or whitespace-only means **no note**;
- longer than `Note.MaxLength` (2000, `Note:31`) is refused;
- any `char.IsControl` other than `\n`, `\r` and `\t` is refused;
- the text is never truncated or stripped.

The check runs before any read or write.

The decision routes answer refusals with coded 400s, not field errors. Examples: `ebilling.reason_too_long` (`Fin/InvoiceRejectReturnService.cs:71`) and `ebilling.line.reason_too_long` (`Fin/InvoiceLineDecisionService.cs:92`). So the decision routes get two shared codes:

- **`ebilling.note_too_long`**;
- **`ebilling.note_invalid_characters`**.

The submission route in 2e keeps part 1's field-error shape instead, because it is a body-shape check on the same contract as from-email.

### 1.2 Where a note is stored

- **Decision Record.** The note goes in the record's `Values["note"]`, inside `sprk_steps`. That is the slot the catalog already declares for approve (`Catalog:417`) and assign-firm (`Catalog:451`).
  - `Values` is in the billing hash (`Writer:1419-1437`), so a note is part of what was decided: the same decision id with a different note is refused as `ReviewInvalid` (`Writer:1349-1357`) and never written twice.
  - The key is **added only when a note is given**. A decision without a note hashes exactly as today, so retries of pre-change decisions still match (test N2, §9).
- **No writer change for Values.** `ValidateBilling` checks only *required* parameters (`Writer:1580-1591`) and never refuses an undeclared value key. A `note` value therefore needs no writer change. Declaring `note` as an optional catalog parameter on more entries only documents it (§8).
- **The invoice-level note** goes to `sprk_invoice.sprk_correctionnote` (`Note:28`), overwritten by the latest invoice-level decision (EB-D-236). The text written is the decision's note when one is given. Whether a decision with a required reason and no note writes its reason instead, or leaves the column unchanged, is §10 Q1. History stays in the Decision Records.
  - **The column must land in the same conditional write as the decision's own invoice change.** A refused or concurrent decision then writes no note, and a note never lands without its decision.
- **Line notes** stay on the line decision's own record (D-75). They never touch the invoice column.

### 1.3 The column's ownership changes

`Note:19` says no decision service writes `sprk_correctionnote` and "nothing else owns that column". Today the only writer is the submission persister (`Fin/Submission/InvoiceSubmissionPersister.cs:1687-1690`). This design adds four writers:

- the lifecycle transition (2a);
- the adjustment path (2b);
- the override release (2c);
- the approval, through 2a.

The remark at `Note:19` is rewritten. A new ArchTest, `InvoiceCorrectionNoteWritersGuardTests`, lists the allowed writers (as `tests/Spaarke.ArchTests/InvoiceReviewDetailWritersGuardTests.cs` does for `sprk_reviewdetail`). It ships with must-fire and must-not-fire controls, so a fifth writer fails the build.

### 1.4 The column now holds text from two authors

Before this change, `sprk_correctionnote` held the firm's own "why corrected" text, written at submission. After it, an internal reviewer's note overwrites it. Two consequences go to the owner (§10 Q2):

- a reviewer's later note replaces the firm's correction note on the invoice. The firm's note survives only in the submission record and the Correction's history, not on the invoice;
- any reader that shows the column to the firm would then show internal text. A grep of `BFF/` finds no reader of `sprk_correctionnote` outside the submission path, but the column's audience must be confirmed before 2a ships.

## 2. Part 2a: the lifecycle extension, plus the reject and return note

### Current behaviour

- **Routes:** `POST /api/finance/invoices/{id}/reject` and `/return` (`Api/InvoiceRejectReturnEndpoints.cs:37`, `:56`). The body is `InvoiceDecisionRequest(string? Reason)` (`:132`).
- **Decider:** the caller's systemuserid (`:89`).
- **Validation.** The service validates the reason:
  - required: `ebilling.reason_required` (`Fin/InvoiceRejectReturnService.cs:69`);
  - at most 2000 characters (`:71`, `:182`);
  - checked in `DecideAsync` (`:277-296`).
- **Order:**
  1. locks (`:298-324`);
  2. preflight (`:489`);
  3. portion claim (`:505-512`);
  4. `TransitionAsync` (`:523`);
  5. record (`RecordAsync` `:678`);
  6. closes (`:688-691`);
  7. link (`:694-711`).
- **Failure after the transition:** 503 `ebilling.invoice.decision_incomplete` with `failedStep` (`:129`, `:989-998`; mapped at `Api/InvoiceRejectReturnEndpoints.cs:118-126`). A repeat completes it.
- **The Decision Record** carries `Reason` and `Values["reason"]` (`BuildDecision` `:928-962`, `:954`).
- **The lifecycle.**
  - `IInvoiceLifecycleService.TransitionAsync(invoiceId, target, ct)` (`Fin/InvoiceLifecycleService.cs:247`; impl `:471`) writes only `statuscode`/`statecode`, plus a blanked review detail (`:520-530`), in one update conditioned on the row version (`:534`).
  - It has no way to set any other column.
  - Its callers: approval (`Fin/InvoiceApprovalService.cs:719`), reject/return (`:523`), the blocked return (`Fin/SubmissionOverrideService.cs:1123`), and the AP export (`Fin/ApExportService.cs:972`, `Api/ApExportEndpoints.cs:387`).

### Change

1. **Lifecycle (eBilling).** One new interface member:

   ```text
   Task<InvoiceTransitionResult> TransitionAsync(Guid invoiceId, InvoiceLifecycleStatus target, string? decisionNote, CancellationToken ct = default);
   ```

   - The existing three-argument member forwards with `decisionNote: null`. Its behaviour, and every existing caller, is unchanged.
   - When `decisionNote` is non-null, the implementation adds `sprk_correctionnote` to the same `fields` dictionary (`:520-525`), so the note lands in the one conditional update (`:534`), or not at all.
   - The parameter is a note, not an open "extra columns" map. That keeps the lifecycle from becoming a general invoice writer.
   - The test doubles that implement the interface gain the member: `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/ApprovalWorld.cs`, `tests/integration/auth/Finance/ApExportRoutesHost.cs`, `tests/integration/data-mutation/Ebilling/ApExportWorld.cs`, and the services' own tests.
2. **Reject and return.**
   - The body becomes `InvoiceDecisionRequest(string? Reason, string? Note)`.
   - `DecideAsync` checks the note (§1.1) after the reason and before the locks.
   - `TransitionAsync` at `:523` passes the column text (§1.2).
   - `BuildDecision` adds `Values["note"]` when a note is given.
   - The reason rules are unchanged.
3. **Repeat after 503.** The completing repeat must send the same note. The InvoiceReview wizard keeps its form state across the retry (`src/solutions/InvoiceReview/src/components/decision/InvoiceDecisionWizard.tsx:158`).
   - If the record was already written, a repeat with an edited note is refused by the hash (§1.2).
   - If it was not yet written, the record takes the repeat's note while the column keeps the first one (K-1, §11).

### API contract

| Route | Request | New errors |
|---|---|---|
| reject, return | `+ note?: string` | 400 `ebilling.note_too_long`, 400 `ebilling.note_invalid_characters` |

### Tests

- Lifecycle:
  - the note lands in the same update as the status;
  - a concurrent change writes neither the status nor the note;
  - the three-argument call writes no note column (must-not-fire).
- Reject and return:
  - the note is in `Values` and in the column;
  - with no note, `Values` has no `note` key and the existing hash is byte-identical;
  - 2000 characters is accepted; 2001 and `\u0007` are refused before any lock;
  - `\n` and `\t` are accepted.

**Ownership:** eBilling. `Values["note"]` on `reject-invoice` and `return-to-firm` needs no writer change. Declaring the parameter is the optional catalog edit in §8, which is **ontology** (light).

## 3. Part 2b: the adjustment note

### Current behaviour

- **Routes:**
  - `POST /api/finance/invoices/{id}/adjustments` (`Api/InvoiceAdjustmentEndpoints.cs:52`), with body `AddAdjustmentRequest(Guid AdjustmentId, string? Method, decimal? Value, string? Reason, Guid? RuleId)` (`:154`);
  - `DELETE .../adjustments/{lineId}`.
- **Validation:** the reason is required, trimmed (`Fin/InvoiceAdjustmentService.cs:454`) and capped by `InvoiceLineDecisionService.ReasonMaxLength` (`:177`), with codes `ebilling.adjustment.reason_required` and `ebilling.adjustment.reason_too_long` (`:68-69`).
- **Storage:** the reason goes to the adjustment row's `sprk_billingevent.sprk_adjustmentreason`. **No Decision Record is written** (remark `Api/InvoiceAdjustmentEndpoints.cs:166`).
- **Totals** are recalculated through `Fin/InvoiceTotalsService.cs:153`. The invoice status does not change.

### The gap

EB-D-236 lets an adjustment carry a note, with history in the Decision Records. Adjustments have no Decision Record, so a note written only to the invoice column is lost at the next decision.

### Options (§10 Q3)

**(A) Recommended: give adjustments a Decision Record.**

- Two new billing codes, `add-adjustment` and `remove-adjustment`.
- The subject is the invoice. Codes outside the four line codes already map to the invoice (`Writer:1263-1267`), so **the writer literal is not edited**.
- The decision id:
  - for an add, the client's `AdjustmentId`, a natural idempotency key that is already in the body;
  - for a remove, an id derived from `remove-{lineId}`.
- `Values`: `method`, `value`, `ruleId` and, when given, the note.
- The record is written after the adjustment and totals land, with the reject/return 503-then-repeat pattern.
- The exact-set test (`tests/unit/Sprk.Bff.Api.Tests/Services/Signals/DecisionRecordWriterTests.Billing.cs:191`) is the designed place to decide a new code's table, and it gains the two codes.

**(B) eBilling only.** The note goes on the adjustment row (a new `sprk_billingevent.sprk_adjustmentnote` column, which is a schema add) and on the invoice column. There is no Decision Record, so the history lives on the adjustment rows.

### Change, either option

- The body gains `Note`, checked per §1.1.
- The invoice column is written in the same update as the totals. The totals write is the invoice update in this path (`Fin/InvoiceTotalsService.cs:153`), and it gains the optional note column. That is the 2b writer in §1.3.

### API contract

| Route | Request | New errors |
|---|---|---|
| add adjustment | `+ note?: string` | 400 `ebilling.note_too_long`, 400 `ebilling.note_invalid_characters` |
| remove adjustment | optional JSON body `{ note?: string }` | same |

### Tests

- The note is on the record (A) or the row (B), and on the column.
- No note means no key.
- (A) A repeated `AdjustmentId` files one record. A remove followed by an add of a new adjustment files two.
- The validation set of 2a.

**Ownership:** (A) is **ontology**: two catalog codes and the exact-set test. (B) is eBilling only, plus a schema change.

## 4. Part 2c: the override note and the blocked-return note

### Current behaviour

- **Routes:** `POST /api/finance/submissions/{id}/override` (`Api/SubmissionOverrideEndpoints.cs:54`) and `/return` (`:67`). The body is `BlockedDecisionRequest(string? Reason)` (`Fin/SubmissionOverrideService.cs:134`).
- **Authorization:**
  - override: privilege check `:690-696`;
  - return: decider check `:698-703`.
- **Reason:** required, at most 2000 characters (`:292`, `:706-715`), with codes from `BlockedSubmissionErrors` (`:51-54`).
- **Codes:** `override-submission` (`:309`) and `return-to-firm` (`:310`).
- **The Decision Record** is built at `:1546-1558`, carrying `Reason` and `Values`.
- **Override** releases through one transaction of two conditional updates (submission and invoice). The invoice update writes only `sprk_reviewdetail` (`BuildReleaseTransaction` `:1240-1262`).
- **The blocked return** moves the invoice through `TransitionAsync` (`:1121-1123`).

### Change

- **Body:** `BlockedDecisionRequest(string? Reason, string? Note)`, with the note checked after the reason (§1.1).
- **Override:**
  - `BuildReleaseTransaction` gains an optional `decisionNote` parameter. When set, it adds `sprk_correctionnote` to the invoice `Entity` in the same transaction (`:1250-1254`). This is the 2c writer in §1.3. The remark at `:1236-1238` ("It writes no other invoice column") is updated.
  - `Values["note"]` is added when a note is given.
- **Blocked return:** the 2a overload of `TransitionAsync`, plus `Values["note"]`.

### API contract

| Route | Request | New errors |
|---|---|---|
| override, blocked return | `+ note?: string` | 400 `ebilling.note_too_long`, 400 `ebilling.note_invalid_characters` |

### Tests

- The release transaction carries the note on the invoice update and still writes no status.
- A version fault applies neither update.
- No note means no column and no key.
- The validation set.
- The existing `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/SubmissionOverrideServiceTests.cs` stays green.

**Ownership:** eBilling. The optional catalog parameter on `override-submission` is **ontology** (light, §8).

## 5. Part 2d: line-decision notes (one Decision Record per line decision, D-75)

### 5.1 Accept, reduce and disallow

**Current behaviour**

- **Routes** (`Api/InvoiceLineReviewEndpoints.cs`):
  - single line (`:60`), body `SingleLineDecisionRequest(Guid DecisionId, string? Action, string? Reason, decimal? ApprovedAmount)` (`:313`);
  - multi line (`:78`), body `MultiLineDecisionRequest(Guid DecisionId, string? Action, string? Reason, IReadOnlyList<MultiLineDecisionLine>? Lines)` (`:318`), one action and one reason for every line;
  - accept-clean (`:95`), body `AcceptCleanRequest(Guid DecisionId)` (`:321`).
- **The service:**
  - action enum `Fin/InvoiceLineDecisionService.cs:23`, command `:47`;
  - reason checks `:507-514`, with `ReasonMaxLength = 2000` (`:191`);
  - one record per line, through the `RecordAsync` loop (`:794`) and `BuildDecision` (`:905-945`).
- **Accept-clean, and the approval's bulk accept** (`Fin/InvoiceApprovalService.cs:647-652` → `AcceptCleanHeldAsync` `Fin/InvoiceLineDecisionService.cs:352`, Bulk = true), write one `accept-line` record per line plus "Accepted in bulk" correction rows (`:201`).

**Change**

- `SingleLineDecisionRequest` and `MultiLineDecisionRequest` gain `Note`. A multi-line call has one note, which goes on each line's record, as its reason does today.
- `LineDecisionCommand` (`:47`) gains `Note`, checked per §1.1 next to `:507-514`.
- `BuildDecision` adds `Values["note"]` when a note is given.
- **Accepted in bulk carries no note.** `AcceptCleanRequest` is unchanged. The approval's bulk accept passes none, even when the approval itself has a note (5.3).
- Line notes never touch the invoice column (§1.2).

**API contract**

| Route | Request | New errors |
|---|---|---|
| single line, multi line | `+ note?: string` | 400 `ebilling.note_too_long`, 400 `ebilling.note_invalid_characters` |
| accept-clean | unchanged | none |

**Tests**

- One note goes onto N records in a multi-line call.
- No note means no key.
- **Must-not-fire:**
  - accept-clean records carry no `note`;
  - the approval's bulk records carry no `note` when the approval has one;
  - no line decision writes `sprk_correctionnote`.
- The validation set.

**Ownership:** eBilling. Declaring `note` on `accept-line`, `reduce-line` and `disallow-line` (`Catalog:391-414`) is the optional **ontology** catalog edit (§8).

### 5.2 Correct and confirm: there is no Decision Record today

**Current behaviour**

- **Routes:** `POST .../lines/{lineId}/correct` (`Api/InvoiceCorrectionEndpoints.cs:43`) and the header route (`:66`). The body is `CorrectionRequest(string? Field, string? Value, bool Confirm = false)` (`Fin/InvoiceCorrectionService.cs:187`).
- `CorrectAsync` (`:363`) writes only `sprk_invoicecorrection` rows (`:1420-1458`). Each row is labelled Corrected or Confirmed (`:1430`, D-64) and has an id derived from its scope, field, version and final text (`:1423`).
- There is no Decision Record, no decision id and no reason.

**The conflict**

EB-D-236 says correct and confirm notes are "stored with that decision's Decision Record (D-75: one per line decision)". No such record exists today.

**Options (§10 Q4)**

**(A) Make correct and confirm Decision Records.**

- New codes `correct-line` and `confirm-line` (subject: the line), plus `correct-header` and `confirm-header` (subject: the invoice).
- The two header codes fall into the existing invoice default.
- The two line codes **must be added to the literal at `Writer:1266`**. Otherwise they silently map to the invoice and are then refused at `Writer:1563`.
- That is an edit to an existing writer line, not an addition, so it needs a §6.5 **path-B amendment** to D-100's C7 ("additive only"). One alternative ontology-r1 may prefer is to replace the literal with a declared per-code subject on `DecisionActionDefinition`, which is a larger, one-time change.
- **Cost:** one record per field correction. A header pass can correct many fields, so a Correction-heavy review files many records.

**(B) Note on the correction row.**

- A new `sprk_invoicecorrection.sprk_note` column (a schema add), written in the same create.
- The note is part of the row's derived id input only if the owner wants an edited note to be a new correction. The draft says it is not.
- There is no Decision Record. That falls short of EB-D-236's wording, but adds no writer change.

**(C) Defer correct and confirm notes** to a later release, with 5.1 shipped now.

**Recommendation:** (B) for R1, with (A) recorded as the target if the owner wants corrections to appear in the decision history. The decision belongs to the owner, with ontology-r1 consulted for (A).

**Ownership:** (A) is **ontology**, a path-B amendment. (B) and (C) are eBilling only.

### 5.3 Approve (the invoice-level decision in the same review)

**Current behaviour**

- **Route:** `POST /api/finance/invoices/{id}/approve` (`Api/InvoiceApprovalEndpoints.cs:56`). The handler takes no body (`:186-194`), and the client sends `{}` (`src/solutions/InvoiceReview/src/services/decisionApi.ts:438`).
- **The record** is built at `Fin/InvoiceApprovalService.cs:1550-1560`. Its only value is `approvedAmount` (`:1557`). The catalog's optional `note` parameter (`Catalog:417`) is never sent.

**Change**

- A new optional body, `InvoiceApprovalRequest(string? Note)`; a missing body still means no note.
- The note is checked (§1.1) before the locks.
- The approval's transition (`:719`) uses the 2a overload.
- `Values["note"]` is added when a note is given.

**Contract:** `approve` gains `+ note?: string` and the two note errors.

**Tests:**

- note in, record and column;
- no body means unchanged behaviour (a byte-identical hash);
- the bulk-accept must-not-fire check in 5.1.

**Ownership:** eBilling. The catalog parameter already exists.

This part is built in the 2a task because it reuses the 2a overload.

## 6. Part 2e: the from-document Correction note

### Current behaviour (server)

- **Route:** `POST .../from-document` (`Api/InvoiceSubmissionEndpoints.cs:83`), handler `:670`.
- **Body:** `SubmitInvoiceFromDocumentRequest` (`:1110-1142`), with `Mode` (`:1128`), `InvoiceId` (`:1131`) and `CorrectedInvoiceId` (`:1142`).
- **Validation:** the Validate switch (`:1189-1201`) mirrors from-email's mode rules but has **no note**.
- **Correction prepare:** `:684-695`.
- **Request built:** `:714-739`, without a note.
- **No idempotency filter** on the route.
- **Service:** `SubmitExistingDocumentAsync` (`Fin/Submission/InvoiceSubmissionService.cs:526`), Correction at `:631`.
- **The job.** It is built at `:1505` without a note. `BuildJob` already takes `correctionNote` (`:2232-2244`, payload `:2247`), and the job handler parses it (`BFF/Services/Jobs/Handlers/InvoiceSubmissionProcessingJobHandler.cs:357`, `:909-919`).
- **The persister:**
  - writes the note when the submission is a resubmission (`Fin/Submission/InvoiceSubmissionPersister.cs:1687-1690`);
  - warns for every Correction without one (`:605-609`). Today that warning fires on every from-document Correction.

### 6.1 Part 2e-1 (server, eBilling)

1. `SubmitInvoiceFromDocumentRequest` gains `public string? CorrectionNote { get; init; }`.
2. The Validate switch adds part 1's rules, word for word (`Api/InvoiceSubmissionFromEmailHttp.cs:265-286`):
   - a note on `New` gives field error `correctionNote: "correctionNote is not sent for mode New."`;
   - on `Correction`, TooLong and ControlCharacter give the same field-error messages.
3. In the handler, after Validate and **before** `PrepareCorrectionAsync` (`:684`), a Correction with a missing or blank note gives **400 `ebilling.submission.correction_note_required`**, reusing the constant `Fin/Submission/FromEmailSubmissionContracts.cs:49` and the detail text of `Fin/Submission/FromEmailSubmissionService.cs:232-233`. Nothing has been read or written at that point.
4. The note is carried to `BuildJob(..., correctionNote:)` at `:1505`. The persister then writes it unchanged, and its warning fires only for legacy data, as EB-D-237 expects.

**API contract:** from-document gains `+ correctionNote?: string`, with:

- 400 `ebilling.submission.correction_note_required` (Correction without a note);
- 400 field errors on `correctionNote` (a note on New, too long, control characters).

**Tests:**

- New plus a note gives 400, and New without one still behaves as before;
- Correction without a note gives the coded 400 with zero reads;
- 2000 characters is accepted; 2001 and `\u0007` give field errors;
- the job payload carries the note;
- the persister writes it;
- the warning is not logged for a from-document Correction with a note (must-not-fire).

**Breaking-change note.** A from-document Correction without a note is accepted today and refused after 2e-1. That is the owner's decision (EB-D-237). The only client of the route sends `New` only (§6.2), so no deployed caller breaks.

### 6.2 What the clients actually do (a mismatch to resolve before 2e-2)

- **The from-document route's only client** is `src/client/shared/Spaarke.Communication.Components/src/logic/invoice/emailInvoiceSubmission.ts`:
  - body type `:61-69`;
  - builder `:76`;
  - path `:115`;
  - POST `:230-233`.

  It is word-add-in-r1's task-118 body. Its comment says the email UI only submits a NEW invoice (`:74`).
- **The Create Invoice wizard does not use from-document.**
  - `src/solutions/CreateInvoiceWizard/src/main.tsx` hosts `src/client/shared/Spaarke.UI.Components/src/components/CreateInvoiceWizard/CreateInvoiceWizard.tsx`.
  - That component wraps the shared `src/client/shared/Spaarke.UI.Components/src/components/InvoiceSubmission/InvoiceSubmissionWizard.tsx` on the **internal multipart** route `POST /api/finance/{matters|projects|workassignments}/{id}/invoice-submissions` (`internalInvoiceSubmission.ts:8`, `:411`).
  - It has **no Correction path** at all.
- **`InvoiceSubmissionWizard`** has no note prop (`:134-190`). It is also used by the external SPA: `src/client/external-spa/src/components/record/actions/SubmitInvoiceAction.tsx:231` and `src/client/external-spa/src/pages/InvoiceSubmitPage.tsx:328`. A change to it therefore touches spa-r4's surface as well as ours.

So EB-D-237's "the wizard asks for the note on its Correction path" has no Correction path to attach to.

### 6.3 Part 2e-2 (client), options (§10 Q5)

**(A) A Correction path in the Create Invoice wizard.**

- **Server:** the internal multipart route gains `mode`, `invoiceId` and `correctionNote`, with the same rules as 2e-1. This is a new mode on an existing route, M size.
- **Client:**
  - `InvoiceSubmissionWizard.tsx` gains optional props, e.g. `correction?: { invoiceId: string; noteRequired: true }`, which render a required "Why is this invoice corrected?" box with a `maxLength` of 2000 and the control-character check client-side;
  - `CreateInvoiceWizard.tsx` and `internalInvoiceSubmission.ts` send them.
- The props are additive and optional, so the external SPA's two call sites compile and behave unchanged.

**(B) The email path.** If word-add-in-r1 adds Correction to its from-document client, then `emailInvoiceSubmission.ts` changes:

- the body type `:61-69` gains optional `mode`, `invoiceId` and `correctionNote`;
- the builder `:76` sends them;
- the comment `:74` is updated.

That is word-add-in-r1's file and its decision. 2e-1 is the server half it needs.

**(C) Server only for R1:** 2e-1 ships, and the wizard's Correction path is a later scope.

**Coordination before any client change:**

- word-add-in-r1 owns `emailInvoiceSubmission.ts` and is asked first, as EB-D-237 requires.
- spa-r4 is told about any `InvoiceSubmissionWizard.tsx` prop change before it merges, because the external SPA renders the same component.

No message is sent from this design. The session sends them.

**Ownership:** eBilling, word-add-in-r1 and spa-r4. **No ontology code.**

## 7. Client work for the decision notes (InvoiceReview)

- `src/solutions/InvoiceReview/src/services/decisionApi.ts`: send `note` on:
  - approve (`:438`, today `{}`);
  - reject (`:440`);
  - return (`:442`);
  - add adjustment (`:445`);
  - remove adjustment (`:453`);
  - override (`:456`);
  - blocked return (`:458`).
- `src/solutions/InvoiceReview/src/services/invoiceReviewApi.ts:608-654`: send `note` from `decideLine` and `decideLines` (and from `correctLine` and `correctHeader` only under 5.2 (A) or (B)). Never from `acceptClean`.
- UI: one optional "Note" box in `components/decision/InvoiceDecisionWizard.tsx` (`:158`), `BlockedBanner.tsx` (`:117`) and the line-decision steps (`steps.tsx:212-218`), with a `maxLength` of 2000.

## 8. Which parts touch ontology-owned code

| Part | eBilling only | Ontology-owned change | Review |
|---|---|---|---|
| 2a (+ approve) | lifecycle overload, services, routes | none required. Optional: declare `note` on `reject-invoice` and `return-to-firm` (`Catalog:423-436`) | ontology-r1 light (Values content) |
| 2b | the note field, the column | (A): add `add-adjustment` and `remove-adjustment` to `DeclaredBilling`, plus the exact-set test | ontology-r1 under (A) |
| 2c | release transaction, routes | optional `note` on `override-submission` (`Catalog:438`) | ontology-r1 light |
| 2d 5.1 | command, routes | optional `note` on `accept-line`, `reduce-line` and `disallow-line` (`Catalog:391-414`) | ontology-r1 light |
| 2d 5.2 | (B) or (C) | (A): edit the literal at `Writer:1266` (path-B amendment of C7) plus four codes | ontology-r1 full under (A) |
| 2e | server route, persister path | none | word-add-in-r1, spa-r4 |

**Optional catalog parameters.** Declaring `note` as an optional parameter adds lines to existing entries' parameter lists. The writer never reads optional parameters (`Writer:1580-1591`), so behaviour is unchanged. `tests/unit/domain/Signals/DecisionActionCatalogTests.cs` may pin parameter lists and would then be updated. It is listed so ontology-r1 can decline it; the notes work without it (§10 Q6).

## 9. Tests common to every part

| # | Test |
|---|---|
| N1 | Every new note field shares the §1.1 table: null, empty, whitespace, 1, 2000 and 2001 characters, `\n`, `\t`, `\u0007` and `\u001b`. Each refusal happens before any read (zero reader calls). |
| N2 | With no note, the billing hash of every touched decision is byte-identical to the pre-change hash, captured as a golden value per code. |
| N3 | Every route without a note behaves exactly as before (the existing tests are untouched and green). |
| N4 | `InvoiceCorrectionNoteWritersGuardTests` (ArchTest): the allowed writers are the persister, the lifecycle, the release transaction and the totals path. A planted extra writer fails (must-fire), and the allowed set passes (must-not-fire). |
| N5 | No log line contains note text: the services log ids and lengths only. |

## 10. Open questions

**For the owner**

1. **Column text when there is no note.** When a decision has a required reason (reject, return, override, adjustment) and no note, does `sprk_correctionnote` take the reason ("overwrite with the latest reason") or stay unchanged? The draft takes the note only.
2. **Two authors, one column** (§1.4). Should reviewer notes overwrite the firm's correction note on the invoice? Alternatives:
   - a separate internal column, e.g. `sprk_decisionnote`;
   - confirm that `sprk_correctionnote` is internal-only in every surface before 2a ships.
3. **Adjustments (2b):** (A) a Decision Record per adjustment, or (B) a note on the adjustment row?
4. **Correct and confirm (5.2):** (A) Decision Records (a path-B amendment), (B) a note on the correction row, or (C) defer?
5. **Create Invoice wizard (6.3):** (A) build a Correction path, (B) leave Correction to the email path, or (C) server only for R1?

**For ontology-r1**

6. Should `note` be declared as an optional parameter on `reject-invoice`, `return-to-firm`, `override-submission` and the three line codes, or should it stay an undeclared value?
7. Under 5.2 (A): an edit to the literal at `Writer:1266`, or a declared per-code subject on `DecisionActionDefinition`?
8. Is a note inside the billing hash (so a changed note under the same decision id is refused) the behaviour you want, rather than leaving it out of the hash like the facts?

**For word-add-in-r1 and spa-r4**

9. word-add-in-r1: will the Outlook from-document flow offer Correction (6.3 (B))? If not, 2e-1 has no client until 6.3 (A).
10. spa-r4: is an additive optional `correction` prop on `InvoiceSubmissionWizard` acceptable for the external SPA's two call sites?

## 11. Known limits

| # | Limit |
|---|---|
| K-1 | A reject or return that fails at "record" and is repeated with an edited note records the edited note, while the invoice column keeps the first. |
| K-2 | A multi-line decision has one note for all its lines, as it has one reason. |
| K-3 | A decision without a note leaves the column unchanged (pending Q1), so the column may show an older decision's note. |

## 12. Build order and proposed tasks (no POMLs yet)

| Order | Id | Scope | Size | Review |
|---|---|---|---|---|
| 1 | 095f-2a | Lifecycle overload; reject, return and approve notes; the note ArchTest (N4); the shared codes | M | code-review + adr-check; ontology-r1 light |
| 2 | 095f-2c | Override and blocked-return notes | S | code-review; ontology-r1 light |
| 3 | 095f-2d1 | Accept, reduce and disallow notes; accept-clean must-not-fire | M | code-review; ontology-r1 light |
| 4 | 095f-2e1 | from-document `correctionNote` plus the 400 | S | code-review; word-add-in-r1 told before merge |
| 5 | 095f-2b | Adjustment note, after Q3 | S (B) / M (A) | ontology-r1 under (A) |
| 6 | 095f-2ui | InvoiceReview note boxes and `decisionApi`/`invoiceReviewApi` | M | code-review |
| 7 | 095f-2d2 | Correct and confirm notes, after Q4 | M; L under (A) | ontology-r1 full under (A) |
| 8 | 095f-2e2 | Create Invoice wizard Correction path, after Q5 | M–L | word-add-in-r1 first, then spa-r4 |

- 2a goes first because 2c's blocked return and the approval reuse its overload.
- 2e-1 has no dependencies and can run in parallel with 2a.
- 2b, 2d2 and 2e2 wait on the owner's answers.
- Deploy beyond dev still needs `Deploy-EbillingInvoiceCorrectionNoteColumn.ps1` first (095f part 1).
