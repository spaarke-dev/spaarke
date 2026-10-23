# Task 119: read-time Duplicate withholding on /review (D-176, #2118)

> Completed 2026-10-21 on `task/ebill-119-review-withhold` (base `b26aaece6e` on `work/ebilling-r1`). Rigor FULL, Opus 5.5 @ high.

## What changed

`GET /api/finance/invoices/{invoiceId}/review` (`Api/Finance/InvoiceLineReviewEndpoints.cs` GetReview) builds `InvoiceReviewReadService`'s read model from the submission's stored `sprk_findingsjson`. Before this task, every stored Duplicate finding came back with its earlier-invoice detail to any reader of the current invoice. That detail was `duplicateOf.trackingNumber`, `duplicateOf.invoiceId`, the duplicated line's values, the suggestion, and the plan sentence naming the earlier number.

Now (`Services/Finance/InvoiceReviewReadService.cs`):

- **Grouping.** `DuplicateReferences` keys every (policyCode, subject, line) group that holds a Duplicate. A finding is a Duplicate when its `valuesRead` names `duplicateOf.invoiceId` or `duplicateOf.trackingNumber`, matched case-insensitively, as the Signal read does. Only those groups are keyed (note 10).
- **One reference per prior (note 11).**
  - When `duplicateOf.invoiceId` parses to a non-empty GUID, the reference is `Reference(id, null, null)` and any number is ignored.
  - Otherwise the reference is `Reference(null, current invoice's sprk_vendororg, number)`.
  - A Duplicate that names neither adds `Reference(null, null, null)`. That reference identifies nothing, so `ShownAsync` does not show its group.
- **One call per response.** `DuplicateGroupsWithheldAsync` calls `EarlierInvoiceReadability.ShownAsync` once per response, with the caller's OBO `IDataverseUserClient`. The client is injected; there is never an app-only read of the earlier invoice (ADR-028).
- **Fault handling (note 12).** Any exception other than the request's cancellation withholds every Duplicate group and logs a Warning that names only the exception type. Cancellation propagates.
- **Withheld shape (note 4).** `FindingsFor` gives the whole group `detailWithheld = true`, no `valuesRead` and no `suggestedAdjustment`. The group's plan sentence becomes `EarlierInvoiceReadability.WithheldSentence`. This holds even when the Duplicate is not the group's primary reason; the sentence only ever appears on the primary reason.
- **Pass-through.** Write-time-withheld findings (no `duplicateOf.*`) and non-Duplicate groups come back as stored.
- **K2 (note 5).** `BillingRuleCatalogue.WithheldDuplicateTemplate` is now defined as `EarlierInvoiceReadability.WithheldSentence`, a compile-time reference to the constant ontology owns. The write-time text and the read-time text cannot drift.
- **Unchanged.** No new endpoint, DI registration or package. `FinanceModule`'s existing registration gained a comment; `EarlierInvoiceReadability` is registered unconditionally in `SignalsModule`.

## Notes from 157 (POML note 7)

- **157's point (a) does not apply here.** Point (a) is that the D-123 read sweep strips reason texts. That sweep runs on the Signal read route over `sprk_signal` evidence. It never touches `sprk_findingsjson`, so it changes nothing on /review. This task applies the withheld shape itself.
- **157's point (b) does apply.** A prior with no invoice link is withheld for every caller until it is linked. Examples are a submission row with no `sprk_invoice`, or an older finding whose only reference is a number that resolves to a submission with no invoice. No caller can read such a prior, so its Duplicate group is withheld on /review exactly as on the worklist.

## Agreement with 157's rule

The escalation trigger did not fire. Scope, id-first references, the number fallback within the current invoice's vendor org, "every reference readable", fail-closed handling and the withheld sentence match `SignalReadService.DuplicatesShownAsync`. Both call the same `ShownAsync`.

The two surfaces differ in three places. None of them is a disagreement on the rule:

- **Current invoice's vendor org.** /review takes it from the app-only read of the current invoice, which the route's own filter has already authorized as the caller. The worklist reads it as the caller. The result is the same. *(Superseded: /review now takes the submission's `sprk_vendororg` (below), and since UAC review F4, option b (2026-10-23), the worklist does too, from the current invoice's submissions read as the caller.)*
- **When an exception escapes `ShownAsync`.** /review withholds and returns 200, which note 12 requires. The worklist lets the exception propagate. Nothing is disclosed on either surface; only availability differs. This is for the main session to relay to ontology-r1 as an FYI.
- **Granularity of "numbers ignored".** /review applies it per finding (per prior); the Signal read applies it per Signal. A (policy, subject) group holds at most one Duplicate finding: `DuplicateHandler` emits one per line per policy and one invoice-level finding per policy. So the two give the same result in practice. Where they could ever differ, /review is the stricter.

## Tests (`tests/unit/Sprk.Bff.Api.Tests/Services/Finance/InvoiceReviewReadServiceTests.cs`, "119:" cases)

The caller is an evaluating double, mirroring 157's: the invoice read returns the In-listed ids the caller can read, and the submission read evaluates the org and number In lists. The cases are the POML's closed set as updated by notes (6) to (13):

- A readable earlier invoice, found by id and by the tracking-number fallback, comes back unchanged. The check reads as the caller: one caller `sprk_invoices` read, one app-only `sprk_invoice` read, and the submission read filters on the firm.
- An unreadable earlier invoice, by id and by number, gets the withheld shape. Its message equals `BillingRuleCatalogue.WithheldDuplicateTemplate`. Other groups are untouched.
- The whole group is scrubbed and its sentence replaced when the Duplicate is not the primary reason.
- The fallback withholds and the review still returns when the submission is not found, when there is more than one, on a 500, and when the read throws.
- A write-time-withheld finding stays withheld and the caller is asked nothing.
- `DuplicateOfEarlierLine` is shown when the invoice's own submission is readable and withheld when it is not.
- There is one check per response: four Duplicates (id and number forms, line-level and invoice-level) produce one caller invoice read and one submission read.
- An exception escaping the check (a throwing fake) returns a review with every Duplicate group withheld. The test also asserts the read model's own Warning.
- **Beyond the closed set:** cancellation during the check propagates. The contract (note 12) says so explicitly, so the test pins it.

**Seeding proofs.**

- With the check disabled (`DuplicateGroupsWithheldAsync` returns no groups), 13 of the 119 cases fail, including the unreadable-invoice test.
- With the wrap narrowed to an exception type that is never thrown, the throwing-fake case fails.
- Both edits were restored.

## Verification

- `dotnet build src/server/api/Sprk.Bff.Api/` succeeds with no warnings in the changed files.
- Unit tests filtered to Finance, Signals and InvoiceReview: 6131 passed, 5 skipped, 0 failed.
- ArchTests (full): 1349 passed.
- **Publish size** (CLAUDE.md §10 and NFR-01): measured against the branch base `b26aaece6e`, not master, as the brief asked.
  - Command: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`, framework-dependent linux-x64 (set in the csproj), Release.
  - Archive: `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, PDBs included.
  - Both trees were exported with `git archive` to `C:\p119\{base,head}`.
  - Result: base 40,145,404 B, head 40,149,017 B (38.29 MB each). Delta +3,613 B.
- No new HIGH CVE (`dotnet list package --vulnerable --include-transitive`: none). No packages changed.

## Review

- **code-review (Step 9.5).** No Critical or Warning findings. One Suggestion was fixed: a needless `System.Guid` qualifier.
  - `InvoiceReviewReadService` grows from 887 to about 1030 lines and from 4 to 6 constructor parameters. It is still cohesive: the D-176 check filters the findings this read model returns.
- **adr-check.** No violations. ADRs considered:
  - 001, 007, 008: no new endpoint or Graph use.
  - 002: read path only.
  - 010: concrete injection, no new interface or registration.
  - 013: no AI.
  - 028: the earlier-invoice read runs as the caller, with no elevation.
  - 029: publish delta reported.
  - 038: the contract test on the KEEP path was modified, not deleted.
  - 044: GUIDs are parsed with `Guid.TryParse`.
  - 047: no new Signal.
- **Adversarial reviewer (pass 1 of 2; the POML is tagged `auth`, so pass 2 is the main session's).** No F-class findings.
  - Fixed: the K3 that the throwing-fake test reached the wrap only through 157's current NullReferenceException. The test now asserts the read model's own Warning.
  - Known limits:
    - K4: the exception-handling difference from the worklist described above.
    - K3: no test for a value name in another case, or for a number-only Duplicate on an invoice with no vendor org. The handler always writes the exact names; a null org yields an unusable reference, which is withheld.
    - K2: keying on the policy code could also withhold a non-Duplicate policy that shares a code. That only ever withholds more.
    - K2: 157's point (b), above.
  - Found in passing, outside scope (ontology-owned): the doc comment at `EarlierInvoiceReadability.cs:34` still says `WithheldDuplicateTemplate` is "on `work/ebilling-r1`, not on this branch". After this task the template is defined from that constant, so the comment is stale. It is reported, not edited.

## Round 2: blocked view (F1 of the second adversarial pass)

Commits on `task/ebill-119-review-withhold` after `f28b1f1228`: `373f8d4cd4` (refactor), `f7aa46f0a1` (fix and tests), then this docs commit.

**The defect.** `GET /api/finance/submissions/{submissionId}/blocked` (`Api/Finance/SubmissionOverrideEndpoints.cs`, `GetBlockedAsync` → `SubmissionOverrideService.GetAsync`) returned every stored finding's `valuesRead` as stored. That included `duplicateOf.trackingNumber`, `duplicateOf.invoiceId`, `duplicateOf.lineIndex` and the duplicated line's values, with no D-176 check. The POML note's claim that /review was the only other ebilling path returning Duplicate detail was wrong.

**The refactor (`373f8d4cd4`).** The reference building, the (policy, subject, line) group key and the fail-closed wrap moved out of `InvoiceReviewReadService` into one internal static helper, `Services/Finance/DuplicateDetailGate.cs`:

- `WithheldGroupsAsync<TFinding>` (`:52`): builds the references, makes the one `EarlierInvoiceReadability.ShownAsync` call per response with the caller's `IDataverseUserClient`, and returns the withheld groups. Any exception other than the request's cancellation withholds every Duplicate group and logs `"{Surface}: the earlier-invoice check faulted ({ExceptionType}); ..."` at Warning on the calling service's logger. The message names no earlier-invoice detail.
- `References<TFinding>` (`:101`): the rule of note 11, unchanged.
- `FindingGroup(PolicyCode, Subject, Line)`, `DuplicateOfInvoiceIdValue`, `DuplicateOfTrackingNumberValue`.

`InvoiceReviewReadService.DuplicateGroupsWithheldAsync` is now a one-line call with surface `"Invoice review"`. Its 30 tests passed unchanged after the refactor (the Warning text they assert is identical).

**The fix (`f7aa46f0a1`).** `SubmissionOverrideService.GetAsync` parses the findings as before (`ReadFindings`), then calls `DuplicateDetailGate.WithheldGroupsAsync` with surface `"Blocked submission"`:

- **As the caller.** Both constructors take `IDataverseUserClient` (OBO) and `EarlierInvoiceReadability`. `FinanceModule`'s existing registration gained a comment; there is no new registration.
- **Vendor org.** A number-only prior is resolved within the submission's own `sprk_vendororg` (`TrackingNumberAllocator.FirmColumn`, added to `SubmissionColumns`, so there is no extra read). This is the column `BillingFactLoader.LoadPriorsAsync` filters priors on, so it is the firm the Duplicate's resolution used.
- **Withheld shape.** Every finding of a withheld group returns `DetailWithheld = true` and an empty `ValuesRead`. The view carries no sentence or reason text, so there is nothing to replace with `WithheldSentence`.
- **Unchanged.** Step 0 (`RequireInternalFinanceCaller`) and the route's `FinanceAuthorizationFilter` (Read on the submission, as the caller) still run first. The override privilege check, `CanOverride` and `CanReturn` are untouched.
- **DTO contract.** No change. `BlockedFindingView` already had `DetailWithheld`, and only its value and `ValuesRead` change for a withheld group. The doc comments of `BlockedFindingView` and `BlockedValueRead` now name D-176.

**Reader inventory.** Every BFF reader of `sprk_findingsjson` or of Duplicate `valuesRead` was checked by grepping `SubmissionFindingsJson`, `sprk_findingsjson`, `valuesRead`/`ValuesRead` and `duplicateOf` over `Services/Finance`, `Services/Finance/Submission`, `Services/Signals/Billing`, `Services/Signals` and `Api/Finance`:

| Reader | Returned by a route? | Finding |
|---|---|---|
| `InvoiceReviewReadService` (`GET .../invoices/{id}/review`) | Yes | Gated (round 1, now through the helper) |
| `SubmissionOverrideService.GetAsync` (`GET .../submissions/{id}/blocked`) | Yes | **Gated (this round)** |
| `SubmissionOverrideService` decide path (`POST .../override`, `.../return`) | Response is `BlockedDecisionResponse` (ids, outcome, record id) | No findings returned. Its Decision Record copies plan sentences: see K1 |
| `InvoiceSubmissionService.GetAsync` and list (`GET .../invoice-submissions/{id}`) | Yes | `ViewColumns` exclude `sprk_findingsjson`; no findings returned |
| `InvoiceSubmissionAfterSteps` (job) | No | Writes the reject Decision Record (K1) and the stored sets |
| `AdminAttentionText` (job) | No | Reads only `signalPlanRefusals` and `signalWriteRefusals` into `sprk_adminattention`; no values |
| `StoredSignalSetCheck`, `InvoiceSubmissionPersister` | No | Write side and set bookkeeping |
| `SignalReadService` (worklist) | Yes | Reads `sprk_signal` evidence, not `sprk_findingsjson`; ontology 157 gates it |
| `BillingFindingSignalMapper`, `SubmissionRejectionRule`, handlers | No | Evaluation time, in memory |

No other route was found, so the helper has exactly two callers.

**Tests.**

- `SubmissionOverrideServiceTests`, "119 r2" cases (14). The World gained the same evaluating caller double as `InvoiceReviewReadServiceTests`. Cases:
  - readable by id and by number: unchanged, read as the caller, the number resolved within the submission's firm;
  - unreadable by id and by number: withheld;
  - the fallback not found, more than one submission, a 500, and a throw: withheld, and the view still returns;
  - write-time withheld: stays withheld and the caller is asked nothing;
  - no Duplicate: every finding as stored and the caller asked nothing;
  - mixed readability;
  - one check per response for four Duplicates: one caller invoice read and one submission read;
  - throwing fake: everything withheld, one Warning naming the exception type, and no log line carrying the earlier number or id;
  - real cancellation propagates.
- **K3 (both views).**
  - A mixed-readability case on each view: two Duplicate groups, one readable and one not. Only the unreadable group is withheld, and the readable one is returned whole.
  - Every unreadable case on both views asserts that `JsonSerializer.Serialize(dto)` contains neither the earlier tracking number nor the earlier invoice id. On /review those cases are: unreadable, not-primary, every fallback failure, and the throwing fake.
  - The /review `DuplicateOfEarlierLine` unreadable case is exempt, because its "earlier invoice" is the current one.
- **Seeding.** With the blocked view's check disabled (`Findings = findings`), 8 of the 14 r2 cases fail: unreadable ×2, fallback ×4, the throwing fake, and mixed. The 6 that still pass are the readable, write-time-withheld, no-Duplicate, one-check and cancellation cases, which do not depend on withholding. The edit was restored.

**Verification.**

- `dotnet build src/server/api/Sprk.Bff.Api/` succeeds with 0 warnings.
- `tests/unit/Sprk.Bff.Api.Tests`, `tests/Spaarke.ArchTests` and `tests/integration/Sprk.Bff.Api.IntegrationTests` all build.
- Unit tests filtered to Finance, Signals, InvoiceReview and SubmissionOverride: 6146 passed, 5 skipped, 0 failed. The route contract tests compile into this project and are included.
- ArchTests (full): 1349 passed.

**K1 (native surfaces; the main session is filing the issue).** D-176 is enforced on BFF routes only. Two places outside the BFF still carry earlier-invoice detail:

- **The form.** The `sprk_invoicesubmission` main form binds `sprk_findingsjson` (`src/dataverse/solutions/SpaarkeMaster/Entities/sprk_invoicesubmission/FormXml/main/{c412f942-875e-45c4-9ea1-cae75a81f1b9}.xml:190`). Any user with Read on a submission sees its stored findings, Duplicate detail included.
- **Decision Records.** Records copy the Signal plan sentences, which can name the earlier tracking number, into `sprk_factsnapshot`. That happens in the reject-invoice record (`InvoiceSubmissionAfterSteps.cs:1429`) and also in the override and return records (`SubmissionOverrideService.cs:1056`, `["reasons"][].sentence`, found during this round).

Recorded only; this round makes no change to either.

rework: fix-round-1 task 119

**Round 3: K4 plan key (adversarial review, low).** `InvoiceReviewReadService.GetAsync` selected a line's findings by its line index (`sprk_linesequence - 1`) but looked its Signal plan sentence up by the row id. If a line row's sequence was edited so it no longer matched its index, a row could show the findings of index N (not withheld) with the plan sentence of its own index M (whose Duplicate group is withheld), which names the unreadable prior. The line loop now passes `InvoiceSubmissionPersister.LineIdFor(submission.Dto.SubmissionId, sequence - 1)` as the plan subject (the row id when there is no submission, where `parsed` is null and no findings are returned), so plan and findings share one key. Untampered data gives the same key as before: plan subjects are written as `LineIdFor(submissionId, index)` (`InvoiceSubmissionAfterSteps.cs:1282`, `InvoiceSubmissionPersister.cs:220`) and line rows are created with that id (`:745`). The invoice-level call is unchanged. The test fixture's `LineId(sequence)` now follows the persister's rule (`LineIdFor(SubmissionId, sequence - 1)`), because its old made-up ids no longer matched the plan key. New test: two line rows with swapped `sprk_linesequence`, index 0 a Duplicate of an unreadable prior, index 1 a readable Duplicate of the same policy and reason code; it asserts no line carries the prior's tracking number or the plan sentence naming it, and that the withheld line shows `EarlierInvoiceReadability.WithheldSentence`. Seed: with the service change reverted the test fails (the line at sequence 2 returned the sentence naming F-090.1); restored, it passes. Filtered unit suite (Finance, Signals, InvoiceReview, SubmissionOverride): 6147 passed, 5 skipped, 0 failed.


## Round 4: vendor-org parity (uac-r2)

The review resolved a legacy number-only Duplicate prior within the CURRENT invoice's `sprk_vendororg`, while the blocked view (`SubmissionOverrideService.GetAsync`) uses the SUBMISSION's `sprk_vendororg` (the firm the priors were read from). A user with Write on the invoice could change its vendor org to firm B, after which the number resolved to firm B's invoice and, if the caller could read it, the gate showed firm A's prior's tracking number. `InvoiceReviewReadService` now adds `TrackingNumberAllocator.FirmColumn` to its existing submission read (`SubmissionColumns`, no new query), carries it on the private `SubmissionRead` record as `VendorOrg`, and passes `submission?.VendorOrg` to `DuplicateGroupsWithheldAsync`. `ReviewSubmissionDto` is unchanged. Tests: the `Entities` double now returns only the requested columns of the submission row (a missing column fails the test), `SubmissionRow` sets `sprk_vendororg` equal to the invoice's org so existing fixtures are unchanged, and a new theory moves the invoice to another org: readable only in the invoice's (wrong) org is withheld with no leak, readable in the submission's org is shown.

## Round 5: F4-1 unusable reference (ontology-r1)

Test-only. Neither route had a test for a Duplicate whose stored values give no usable reference to the earlier invoice. `InvoiceReviewReadServiceTests.UnusableReference_IsWithheld_AndAsksTheCallerNothing` (`/review`) and `SubmissionOverrideServiceTests.Get_D176_UnusableReference_IsWithheld_AndAsksTheCallerNothing` (`/blocked`) each cover three shapes: `duplicateOf.invoiceId` = "not-a-guid" with no tracking number; both `duplicateOf.*` names present with blank values; and a tracking number with the submission's `sprk_vendororg` removed (the number cannot be resolved). Each asserts the group is withheld (the whole-DTO serialization names neither "not-a-guid" nor the planted tracking number), the other groups are untouched, and `CallerPaths` is empty, so the caller-readability check makes no read. The brief's case (b), "no `duplicateOf.*` values at all", is NOT withheld, by design: `DuplicateDetailGate.References` keys only a finding that names `duplicateOf.invoiceId` or `duplicateOf.trackingNumber`, as `SignalReadService` does ("No other row is a Duplicate"), and a finding with no such name quotes no earlier invoice (a write-time scrub also sets `detailWithheld`). It is returned as stored, so the test uses blank values instead; if the reviewer wants a name-less Duplicate-policy finding withheld too, that is a production change to the gate, not made here. Seed: in `WithheldGroupsAsync`, groups whose references are all unresolvable (no id, and no number or no org) were dropped from the keyed set, so they read as shown; all six new cases failed, and the full filtered suite passes without it.
