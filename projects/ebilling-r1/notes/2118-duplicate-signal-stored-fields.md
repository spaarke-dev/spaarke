# #2118 / D-176: what a stored billing Duplicate Signal holds about the earlier invoice

> For ontology-r1's read-time check in `SignalReadService` (owner chose option (b), hide on read; ontology D-176, 2026-10-21).
> Paths are under `src/server/api/Sprk.Bff.Api/Services/Signals/` on `work/ebilling-r1` unless stated.

## 1. Stored columns (`SignalWriter.cs:1789-1794`)

| Column | Holds |
|---|---|
| `sprk_sentence` | the rendered sentence. No template is stored. |
| `sprk_factsnapshot` | JSON of the fact values |
| `sprk_evidencerefs` | JSON array of `SignalEvidenceRef` |
| `sprk_name` | the headline |

### Invoice-level duplicate, prior visible at write time (`Billing/Handlers/DuplicateHandler.cs:106-116`)

- **Subject:** `sprk_invoice`.
- **Facts:**
  - `invoice.firmInvoiceNumber` belongs to the current invoice.
  - **`duplicateOf.trackingNumber` belongs to the earlier invoice.**
- **Sentence:** taken from the first of these that exists:
  1. the reason template;
  2. the policy version's `sprk_messagetemplate`;
  3. `InvoiceDuplicateTemplate` (`Billing/BillingFindingSignalMapper.cs:129`): "Invoice {{invoice.firmInvoiceNumber}} duplicates {{duplicateOf.trackingNumber}}."

  **It can contain the earlier tracking number.**
- **Evidence:**
  - `billing.reasonset`, holding the fingerprint.
  - `billing.reason`: `Text` is the rendered reason sentence, which **can contain the earlier tracking number**. `Source` is `sprk_policyversion:{id}`.
  - `billing.field` `invoice.firmInvoiceNumber`, for the current invoice.
  - **`billing.reference` with `Ref` = `duplicateOf.trackingNumber`.** Its `Text` is the earlier tracking number.

### Line-level duplicate, prior visible (`DuplicateHandler.cs:178-211`)

- **Subject:** `sprk_billingevent`, the current line.
- **Reason codes:** `DuplicateOfPriorSubmissionLine` (another submission) or `DuplicateOfEarlierLine` (an earlier line in the same invoice).
- **Facts:**
  - `invoice.firmInvoiceNumber`.
  - `line.date` and `line.timekeeperName`. These are the current line's values. When they are match fields, they are equal to the prior line's values.
  - `duplicateOf.trackingNumber`. This is the earlier invoice's number for `DuplicateOfPriorSubmissionLine`. For `DuplicateOfEarlierLine` it is the current invoice's own number (`:169`).
- **Evidence:**
  - `billing.field` for the current line's match fields: timekeeper, date, and hours / narrative / amount when they are match fields.
  - `billing.field` `invoice.firmInvoiceNumber`.
  - `billing.reference` `duplicateOf.trackingNumber`.
  - `billing.reference` `duplicateOf.lineIndex`, only for `DuplicateOfEarlierLine`. It is a line index within the same invoice, not a record id.
- **Sentence:** the policy version's `sprk_messagetemplate`, then the type's default wording (`BillingRuleDraftTranslator.cs:140`): "Invoice {{invoice.firmInvoiceNumber}}, the line dated {{line.date}} for {{line.timekeeperName}}, duplicates {{duplicateOf.trackingNumber}}."

### Already withheld at write time (prior across the secure wall, unresolved, or with no tracking number)

- **Withheld when:**
  - `MayShowDetail` is false (`DuplicateHandler.cs:75-77`, `:91-101`, `:163-166`). It requires the prior's secure roots to be a subset of the invoice's roots, and fails closed.
  - In a merged group, if any member is withheld, the whole group is withheld (`BillingFindingSignalMapper.cs:324`).
- **What is stored instead:**
  - `sprk_factsnapshot` = `{}` (`BillingFindingSignalMapper.cs:334`, `:483`).
  - `sprk_sentence` = "This matches an earlier submission." (`Billing/BillingRuleCatalogue.cs:188`).
  - In the evidence, every value `Text` is null (`BillingFindingSignalMapper.cs:375`), and every `billing.reason` text is the withheld template (`:440-442`).
  - **No `duplicateOf.*` entry, no tracking number and no earlier-invoice id.**
- **What remains:**
  - the subject;
  - the reason code;
  - the policy;
  - the current invoice's `invoice.firmInvoiceNumber` field line, with its `Text` stripped;
  - the current line's field names and confidences.

**For the read check, withhold these on a visible-at-write Duplicate:**
- the `duplicateOf.trackingNumber` fact and evidence;
- every `billing.reason` text;
- `sprk_sentence`;
- the line-level `line.date` and `line.timekeeperName` values. They equal the prior line's on the matched fields.

The withheld output should look like the write-time withheld shape above. For `DuplicateOfEarlierLine` the "earlier invoice" is the invoice itself, so no check is needed.

## 2. Identifying the earlier invoice

- The stored Signal carries **only the earlier submission's tracking number**. No fact, evidence ref or lookup stores an invoice or submission id. `BillingPriorSubmission.SubmissionId` (`Billing/BillingFactSet.cs:278`) is used for filtering only and is not stored.
- The tracking number's format is `{firm invoice number}.{n}` (`Billing/BillingFactSet.cs:279`).
- **Resolving it:**
  - Use `sprk_invoicesubmission.sprk_trackingnumber`. It is unique per firm under the alternate key (`sprk_vendororg`, `sprk_trackingnumber`) (`docs/data-model/schema-additions-alternate-keys.md:47`).
  - Then the submission's invoice. `sprk_invoice.sprk_trackingnumber` (`Services/Finance/InvoiceReviewService.cs:261`) also holds the number, but it is not a key, and pre-eBilling invoices have none.
  - Scope by firm: the prior is always the same firm's, because the loader filters `sprk_vendororg` = the invoice's firm (`Billing/BillingFactLoader.cs:497-498`). So resolve with (the current invoice's `sprk_vendororg`, the tracking number).
  - **Superseded (UAC review F4, option b, agreed with ontology, 2026-10-23):** the firm is the current invoice's SUBMISSION's `sprk_vendororg` (the `sprk_invoicesubmission` rows whose `sprk_invoice` is the current invoice, read as the caller), not the invoice's own, which a writer of the invoice can change. No readable submission, or submissions that disagree on the firm: withhold. `SignalReadService.DuplicatesShownAsync`; `/review` and `/blocked` already used the submission's firm.
  - An unresolvable number, or more than one match, must fail closed: withhold.
- If a stored id would be simpler for you, ebilling can add an evidence `billing.reference` `duplicateOf.invoiceId` for new Signals. Existing Signals would still need the tracking-number path.
  - **Correction (uac-r2 re-check F4(c), 2026-10-23):** number-only Duplicates are not only old Signals. `DuplicateHandler` still writes them: every same-invoice repeat (`DuplicateOfEarlierLine`, `DuplicateHandler.cs:185`) carries only the current invoice's own tracking number, and a prior with no invoice carries no `duplicateOf.invoiceId`.

## 3. Other billing types quoting records outside the invoice's family

These were checked on `work/ebilling-r1` and on the 115 branch.

- **Only Duplicate quotes another invoice or submission.**
- These quote only the invoice itself, its lines, its own core record, or the rule: AiObservation, ApprovedTimekeeper, CoreRecordWindow, DailyHoursCap, ExpenseRule, InvoiceAge, NonBillableActivity, RequiredData, TimeIncrement, and AddedFee (115).
- **ApprovedRate, and Discount (115), quote a rate-card row:**
  - ApprovedRate quotes `approvedRate`, `approvedRate.limit`, the basis, the scope and the status.
  - Discount quotes `rackRate` and `discountedRate`. Both are template facts, so they are rendered into the sentence.
  - Both carry `rateCard.id` / `rateCard.rateId` in `billing.reference` evidence (`Billing/Handlers/ApprovedRateHandler.cs:88-93`, `:124`, `:132-136`; 115 branch `DiscountHandler.cs:53-65`).
  - The row is the card the resolver chose for this line's firm and record. It is never another invoice. It is the firm's own agreed or rack rate, and after 085 it is keyed to the Procedure Version that applies to the invoice.
  - **Question for ontology:** does a rate-card row count as "outside the subject family" for D-176? We read it as no: it is the configuration that judged this invoice, under the same firm. If you count it as outside, the same read check would apply to ApprovedRate and Discount.
