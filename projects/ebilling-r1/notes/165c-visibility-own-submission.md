# 165c — visibility counts only a submission's own invoice

Task 165c (uac-r2 ruling 2026-11-06, class (c)). Rigor FULL. Branch `task/ebilling-165c` from `work/ebilling-r1` at `2fac72b8a5`.

## 1. What changed

One rule, one place: `InvoiceBlockedHold.IsOwnSubmission(invoiceId, submissionId)` (new, pure) — the submission's immutable id derives the
invoice's (`InvoiceSubmissionPersister.InvoiceIdFor`). 165a's `OwnSubmissionOf` now calls it. Every own-submission selection uses it:

| Site | Before | After |
|---|---|---|
| `InvoiceBlockedHold.OwnSubmissionOf` / `ReadOwnSubmissionAsync` (165a) | inline predicate | `IsOwnSubmission` |
| `ExternalInvoiceVisibility.DecideSubmissionsAsync` (reader 1) | the link alone | `IsOwnSubmission` |
| `ExternalInvoiceVisibility.ReadSubmissionSourcesAsync` (reader 2) | the link alone | `IsOwnSubmission` |
| `InvoiceSubmissionService.ReadOwnSubmissionAsync` (EB-D-179 intake, 097e prefill) | its own query + `SingleOrDefault(InvoiceIdFor(s.Id) == invoiceId)` | `InvoiceBlockedHold.ReadOwnSubmissionAsync(_entities, invoiceId, CorrectionSubmissionColumns, ct)` (same query: entity, link condition, columns, no TopCount) |
| `InvoiceAssignFirmService.ReadStateAsync` | two inline `InvoiceIdFor(s.Id) ==/!= invoiceId` filters | the same split over `IsOwnSubmission` |

Set firm keeps its split, not `OwnSubmissionOf`: it needs the other linked rows too (`State.OtherSubmissions`, the ontology F1 open-holds
read), and it refuses when the own count is not exactly one. The intake moved from `SingleOrDefault` to `FirstOrDefault`; the two differ
only when two returned rows share one primary key, which Dataverse cannot return. Existing tests of both sites are unmodified.

**Reader 1** (`DecideSubmissionsAsync`: the external submission routes and the assistant tools' submission rows). After the firm withhold
(step 0, unchanged), a submission whose invoice link is a GUID that is not its own invoice is withheld with the new reason
`InvoiceLinkNotOwn` (`invoice_link_not_own`) and is not passed to the invoice decision. A link that is not a GUID is decided as before.
Placing it after step 0 keeps the firm withhold's reason for a row both rules withhold (`InvoiceSubmissionRerunTests.Job`, unmodified).

**Reader 2** (`ReadSubmissionSourcesAsync`, inside the invoice predicate). A submission row whose link is not its own invoice adds nothing to
that invoice: no source document, and no hide from its channel or empty link. The invoice's own submission and its Stored Files still decide
it. Justification for "nothing" rather than "no source only": the row is not the invoice's submission, so neither its document nor its channel
describes the invoice. Beyond the contract (one line): a row whose own id cannot be read hides the invoice (`source_link_unreadable`), since it
cannot be shown to be the invoice's own (fail closed; Dataverse always returns the key).

**Event.** Both readers log `ebilling.visibility.invoice_link_not_own` (Warning) with the submission id and the invoice id only
(`ExternalInvoiceVisibility.InvoiceLinkNotOwnEvent`).

**Escalation check (POML trigger).** No legitimate path writes a link that is not the submission's own invoice: the only server writer of
`sprk_invoicesubmission.sprk_invoice` is `InvoiceSubmissionPersister.BuildSubmissionUpdate`, called from `PersistCoreAsync` with
`invoiceId = InvoiceIdFor(submissionId)`; `SubmissionCommitGate` only clears it. Corrections record `sprk_resubmissionof`, not the link. No
client, script or `@odata.bind` writes it.

No schema, field-security, endpoint, DI registration or package change.

## 2. Tests

New, `ExternalInvoiceVisibilityTests.OwnSubmission.cs` (19 cases):

- Reader 1 must fire: `Reader1_ALinkThatIsNotItsOwnInvoice_IsWithheld_AndTheInvoiceIsNotAsked` (contact, flagged systemuser; the invoice's
  Stored Files are not read), `Reader1_TheAssistantTools_HideARowLinkedToAnInvoiceThatIsNotItsOwn` (control: the own row is visible).
- Reader 1 must not fire: `Reader1_AnAgreeingLink_OrNoLink_IsDecidedAsBefore` (agreeing link, unlinked Spa, unlinked Internal; no event).
- Reader 2 must fire: `Reader2_ARowLinkedToAnInvoiceThatIsNotItsOwn_AddsNothingToIt` (a Restricted document, an empty Internal link, an
  unknown channel; the document is not read).
- Reader 2 must not fire: `Reader2_TheOwnSubmissionsDocument_StillDecidesTheInvoice` (source_hidden, no event).
- `Reader2_ARowWhoseIdCannotBeRead_HidesTheInvoice`; `TheEvent_CarriesOnlyItsCodeAndTheIds` (keys exactly EventCode, SubmissionId, InvoiceId).

Fixture: `InvoiceWorld.NewInvoice()` returns an invoice id derived from a new submission id; the first submission seeded on it without an
explicit id is that own submission (later ones only point at it). Existing tests that seed a submission on an invoice now use derived ids, as
the persister writes them. `ConfirmRoute_ASubmissionWhoseDocumentLinkIsEmpty_HidesTheInvoice` seeds the empty-link row first (the own row).
One expectation changed: `UnlinkedOrRepointed_StaysWithheld_ByItsOwnInvoicesMarker` ("repointed") stays withheld after the commit, with
`invoice_link_not_own`. `NewService` takes an optional logger.

Fixtures outside the visibility tests, same reason (their ids were not derived, a shape the persister never writes):
`InvoiceSubmissionPersisterTests.ProjectForTheFirm` seeds the committed submission under its own id;
`InvoiceSubmissionEndpointsContractTests` derives `HiddenInvoice` and the 097f `Old`/`New`/`Second`/`Other` invoice ids from their submission
ids (`SubmissionRow` takes an optional id), and the 098 / EB-D-169 cases derive the invoice from the submission. No expectation changed there.

Seeds (each applied, the visibility tests run, then restored byte-identical and rebuilt):

| Seed | Fails |
|---|---|
| S1: the own-invoice check dropped from reader 1 | 5 (reader 1 must fire x2, assistant tools, event, repointed) |
| S2: the own-invoice check dropped from reader 2 | 6 (reader 2 must fire, 3 shapes x 2 callers) |

Gates: see section 4.

## 3. Dev count

Re-run read-only on spaarkedev1 (2026-11-07, Dataverse MCP): 6 linked submissions, 0 whose link is not their own invoice. Nothing for uac-r2.

## 4. Gates and publish size

Filtered unit suite (`ExternalAccess|ExternalInvoiceVisibility|Finance|Submission|InvoiceAssignFirm`): 11979 passed, 0 failed, 4 skipped.
ArchTests: 2122 passed, 0 failed. BFF build: 0 warnings, 0 errors. Before the fixture updates the filtered suite had 13 failures, all
fixtures whose invoice ids were not derived (section 2).

Publish size: measured after the commit (section 5).

Step 9.5 (code-review, adr-check) not run here: the lead runs two adversarial passes after this task.

Not changed (out of scope, listed for the lead): other inline copies of the same comparison, each already fail closed:
`SubmissionOverrideService.cs` (:621, :760, :1661), `SubmissionEvaluationOutcome.cs:253`, `InvoiceSubmissionService.cs:846`,
`SubmitInvoiceFromEmailOperation.cs:404`. A guard that refuses a new inline `InvoiceIdFor(...) ==` outside `InvoiceBlockedHold` would be an
ArchTest source scan; not built (not asked by the POML).

rework: none task 165c
