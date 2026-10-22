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

- **Current invoice's vendor org.** /review takes it from the app-only read of the current invoice, which the route's own filter has already authorized as the caller. The worklist reads it as the caller. The result is the same.
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

rework: fix-round-1 task 119
