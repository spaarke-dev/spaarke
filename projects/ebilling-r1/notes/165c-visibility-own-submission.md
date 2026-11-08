# 165c — visibility counts only a submission's own invoice

Task 165c (uac-r2 ruling 2026-11-06, class (c)). Rigor FULL. Branch `task/ebilling-165c` from `work/ebilling-r1` at `2fac72b8a5`.

## 1. What changed

One rule, one place: `InvoiceBlockedHold.IsOwnSubmission(invoiceId, submissionId)` (new, pure) — the submission's immutable id derives the
invoice's (`InvoiceSubmissionPersister.InvoiceIdFor`). 165a's `OwnSubmissionOf` now calls it. Every site below uses it; that no other source
spells the rule is enforced as far as section 7 says (a scan of the source, which does not follow a derived id into a parameter):

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

**Event.** Both readers log `ebilling.visibility.invoice_link_not_own` (reader 1 at Warning; reader 2 at Debug for a pair reader 1 logged in the same call, at Information otherwise, since pass 2) with the submission id and the invoice id only
(`ExternalInvoiceVisibility.InvoiceLinkNotOwnEvent`).

**Escalation check (POML trigger).** No legitimate path writes a link that is not the submission's own invoice: the only server writer of
`sprk_invoicesubmission.sprk_invoice` is `InvoiceSubmissionPersister.BuildSubmissionUpdate`, called from `PersistCoreAsync` with
`invoiceId = InvoiceIdFor(submissionId)`; `SubmissionCommitGate` only clears it. Corrections record `sprk_resubmissionof`, not the link. No
other code writes it; the form shows it read-only (pass 1 fix round, K-3).

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
the persister writes them. `ConfirmRoute_ASubmissionWhoseDocumentLinkIsEmpty_HidesTheInvoice` became
`ConfirmRoute_AnotherSubmissionWhoseDocumentLinkIsEmpty_LeavesTheInvoiceVisible` in the pass 1 fix round (section 6).
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

Publish size: section 5.

Step 9.5 (code-review, adr-check) not run here: the lead runs two adversarial passes after this task.

The other inline copies of the same comparison and a guard against new ones: done in the pass 1 fix round (section 6, K-4).

## 5. Publish size

Fresh short-path worktrees, `dotnet publish -c Release src/server/api/Sprk.Bff.Api/` (framework-dependent, linux-x64), zipped with
`Compress-Archive -CompressionLevel Optimal`, PDBs included (4 each), 191 files each.

| Build | Zip | Uncompressed |
|---|---|---|
| master `23567b2f93` | 41,168,234 B (39.26 MiB) | 136,858,433 B |
| this branch `86dbc05da4` | 41,451,523 B (39.53 MiB) | 137,737,217 B |
| Delta | +283,289 B (+0.27 MiB) | +878,784 B |

The delta includes all of `work/ebilling-r1` not yet on master; 165c adds no package, endpoint or registration. Below every threshold
(+5 MB, 55 MB, 60 MB).

## 6. Pass 1 fix round

| Finding | Change | Test |
|---|---|---|
| F3-1: the empty-link shape of the reader 2 must-fire case was masked (the own row's `SpaUpload` also wrote an InvoicePdf, so the invoice stayed visible with the check dropped; only the log assertion failed) | `Reader2_ARowLinkedToAnInvoiceThatIsNotItsOwn_AddsNothingToIt` seeds the own row as Spa with no document and no Stored File, so the visibility assertion proves every shape. `ConfirmRoute_ASubmissionWhoseDocumentLinkIsEmpty_HidesTheInvoice` duplicated `Internal_AnEmptyDocumentLink_AndNoStoredFile_IsHidden`; it is now `ConfirmRoute_AnotherSubmissionWhoseDocumentLinkIsEmpty_LeavesTheInvoiceVisible` (the own row with a readable document, plus another row with an empty link and no Stored File: visible) | seed S2 below |
| K-1: reader 2 logged a Warning per row on every read, and a second one when reader 1 had already withheld the row in the same call | `DecideSubmissionsAsync` records the pairs it logs and passes them to the invoice decision (a `DecideAsync` overload; the three-argument one passes none). Reader 2 logs a pair reader 1 already logged at Debug, any other at Warning. One Warning per (submission, invoice) pair per call; the event content is unchanged (`TheEvent_CarriesOnlyItsCodeAndTheIds`, unmodified) | `TheEvent_IsOneWarningPerPairPerCall` (new; fails with reader 2 always at Warning), `TheEvent_OnAnInvoiceDecisionAlone_IsAWarning` (new control) |
| K-3: the link columns were editable on the submission form | `Deploy-EbillingFormsViews.ps1`: the `links` section lists `sprk_invoice`, `sprk_document` and `sprk_resubmissionof` as `ReadOnly`, like `sprk_vendororg`. No test pinned that section; the script was not run (an operator applies it) | ArchTest `InvoiceSubmissionFormLinksReadOnlyTests` (new: the pin, one must-not-fire and two must-fire parser controls; the pin fails on the previous script) |
| K-4: six inline copies of the own-submission comparison | `SubmissionOverrideService.cs` (:621, :760, :1661), `InvoiceSubmissionService.cs:846`, `SubmissionEvaluationOutcome.cs:253` and `SubmitInvoiceFromEmailOperation.cs:404` call `InvoiceBlockedHold.IsOwnSubmission`. Behaviour is the same except that `Guid.Empty` on either side now gives false (stricter). Their tests pass unmodified | ArchTest `OwnSubmissionRuleSingleSourceTests` (new): outside `InvoiceBlockedHold.cs` and `InvoiceSubmissionPersister.cs`, no `src/server/**` source uses a direct `InvoiceIdFor(...)` call as an operand of `==` or `!=`; must-fire and must-not-fire controls; the baseline is empty. On the previous sources it reports exactly the six sites. Pass 1 stated this as "no source compares the derived id"; it did not cover a comparison through a local or `Equals` (widened in section 7) |

Seeds (each applied, the tests run, then the file restored byte-identical, checked by hash, and rebuilt):

| Seed | Fails |
|---|---|
| S2 again: the own-invoice check dropped from reader 2 | 10: reader 2 must fire, all 6 (3 shapes x 2 callers), each on the visibility assertion; the repurposed confirm-route case x2; the two new event tests |
| Reader 2 always at Warning | 1: `TheEvent_IsOneWarningPerPairPerCall` |
| The six sites as before this round | `OwnSubmissionRuleSingleSourceTests` main case (the six sites listed) |
| The forms script as before this round | `InvoiceSubmissionFormLinksReadOnlyTests` pin |

Gates: filtered unit suite (`ExternalAccess|ExternalInvoiceVisibility|Finance|Submission|InvoiceAssignFirm|SubmissionOverride|SubmitInvoiceFromEmail`) 11981 passed, 0 failed, 4 skipped (+2 new); ArchTests 2130 passed, 0 failed (+8 new); BFF build 0 warnings, 0 errors. No package, endpoint or registration added.

## 7. Pass 2 follow-ups

| Finding | Change | Test |
|---|---|---|
| K-1: the guard missed a comparison through a local (`var invoiceId = InvoiceIdFor(s); ... link != invoiceId`) or through `Equals`, so three live sites passed it | `InvoiceSubmissionRerun.Signals.cs` (:126 and the findings check at :168), `InvoiceSubmissionRerun.cs` (:330) and `InvoiceSubmissionAfterSteps.cs` (:331) call `InvoiceBlockedHold.IsOwnSubmission`. The locals stay (they are still passed on). Behaviour is the same except that `Guid.Empty` on either side now gives false, and the :331 check also requires the invoice to be the submission's own (on the first-run path it already was; on the resume path the invoice id is read from the link itself, so an unlinked-to-own submission is now refused there too). The :168 check (the stored findings name the invoice) was the one remaining true hit of the extended guard; it is the same rule spelled on the findings' invoice, so it moved too. Their tests pass unmodified | `OwnSubmissionRuleSingleSourceTests` extended |
| The guard's claim (`InvoiceBlockedHold.IsOwnSubmission` remarks, section 1 and section 6) was wider than what it checked | The remarks and these sections now say what is enforced: a direct `InvoiceIdFor(...)` operand of `==`, `!=` or `Equals` (either side, either spelling), and a local assigned from `InvoiceIdFor(...)` that is an operand of the same within its block (not against `Guid.Empty`, `default` or `null`). Not enforced, stated in the test remarks: a derived id passed to a method that compares it, a comparison through a field or property or a second local, and a comparison on a parameter. The :331 site and the `InvoiceSubmissionRerun.cs` site compare a parameter, so reverting them is not caught | as above |
| K-3: four `ExternalInvoiceVisibilityTests.RerunPending.cs` cases seeded `Guid.NewGuid()` invoices with `SpaUpload`, which is no submission's own invoice | Seeded with `_world.NewInvoice()` (a derived id), so the rows are the invoice's own. The assertions are unchanged and pass; none changed meaning (each is about the firm hold, which the own-submission rule does not touch) | the same four tests |
| K-2: reader 2 logged a Warning for a row it met alone | Information (still the code and the two ids only). Reader 1 stays at Warning; a pair reader 1 already logged stays at Debug | `TheEvent_OnAnInvoiceDecisionAlone_IsInformation` (renamed from `_IsAWarning`; asserts Information 1, Warning 0, Debug 0) |

Controls added: must fire - a local compared with `!=` and `==` (either side), `Equals` on a local (three spellings), `Equals` on the call itself (four spellings and a nested one);
must not fire - a local only written, passed, interpolated, copied or compared with `Guid.Empty`/`default`/`null`; the same name as a parameter in another method of the same
file or as a different local in another file; a name inside a string.

Seed: the :126 comparison of `InvoiceSubmissionRerun.Signals.cs` put back as it was; `NoServerSource_ComparesADerivedInvoiceId_OutsideTheRule` failed naming `InvoiceSubmissionRerun.Signals.cs:126`; restored.

rework: none task 165c
