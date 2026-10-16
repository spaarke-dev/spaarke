# eBilling research: accruals, purchase orders, AP coding and export

Date: 2026-10-14. Scope: R1 has no PO, no accruals, USD only, AP export file. The design must let accruals and POs arrive as fast followers without restructuring.

Evidence labels used below:
- **[Doc]** vendor product documentation (describes real behaviour, still vendor-authored).
- **[Mkt]** vendor marketing or white paper; treat claims and statistics as unverified.
- **[Gen]** general accounting knowledge not confirmed by a fetched source; verify with finance/auditors.
- **[Gap]** searched, not found.

---

## 1. Legal accruals

### 1.1 What they are (and are not)

- An accrual here is an **expense accrual for services received but not yet invoiced**: outside counsel has done work, nothing is in the ledger, so finance books an estimate in the period the work was performed. A SimpleLegal white paper calls this the "known unknown" and says finance relies on legal, and legal on the firms, for the estimate. [Mkt] https://info.simplelegal.com/hubfs/Content/Accruals/Guide%20to%20Accruals%20Management.pdf
- Brightflag's vendor help describes the same thing: an accrual is an estimate for work done but not yet billed, recorded in the month the work was performed. [Doc] https://vendorhelp.brightflag.com/en/articles/788391-understanding-accrual-accounting-and-accruals (summary via search result; page itself not fetched)
- Typical journal: debit legal fees expense, credit accrued expenses (liability), reversed on day 1 of the next period so the later invoice does not double count. [Gen/secondary] https://aaronhall.com/accrual-of-legal-fees-not-yet-billed-by-counsel/ ; auto-reversal on the first of next month is described in accounting-software guidance, e.g. https://www.cleverence.com/articles/sage-documentation/accrued-revenues-sage-50-resources-4827/
- **ASC 450 is a different thing.** ASC 450 governs loss contingencies (e.g. the expected outcome of a lawsuit), accrued only when a loss is probable and reasonably estimable. The eBilling accrual is an operating-expense cut-off accrual. No fetched source states this distinction; it is [Gen]. Confirm with the customer's controller. Design consequence: the accrual module must not carry litigation reserve semantics (no probability, no range, no disclosure fields).
- Four accrual components, per SimpleLegal [Mkt]: (1) firm unbilled estimates, (2) pending invoices (billed, not yet approved), (3) rejected invoices that will be resubmitted, (4) "missing information" (likely incurred, no evidence; detect by spend drop-off). Legal Tracker uses essentially the same arithmetic (see 1.3).

### 1.2 Who supplies the numbers, cadence, deadlines

| Source of number | Evidence |
|---|---|
| Firm submits unbilled estimate per matter, in the eBilling portal (or uploads from its time/billing system) | Legal Tracker [Doc] https://www.thomsonreuters.com/en-us/help/legal-tracker/company/accruals-and-unbilled-amounts/understanding-accruals ; LawVu [Doc] https://help.lawvu.com/en/articles/9650470-accruals ; Brightflag [Doc] https://help.brightflag.com/hc/en-us/articles/34015885826845-Accruals-Overview-Timeline |
| In-house user enters/edits on behalf of the firm (before or after the window) | Legal Tracker (users with financial-data edit authority), LawVu (client can add or change any time), Brightflag (client admin can enter on vendor's behalf; "not generally recommended" as ongoing practice) [Doc] https://help.brightflag.com/en/articles/753174-vendor-missed-the-accrual-deadline-what-happens-next |
| System-derived when no entry required | Legal Tracker can derive the unbilled amount from the matter budget in months where firm entry is not required [Doc] (understanding-accruals page above). Run-rate derivation is described as a manual fallback in SimpleLegal's white paper (use last months' charges as a starting point) [Mkt]. |
| WIP-based | PERSUIT (which acquired Apperio in 2025) markets WIP/accrual visibility [Mkt]; only a search-result summary was obtained (https://www.apperio.com/blog/why-legal-accruals-break-and-how-get-them-right now redirects to persuit.com). Treat as unverified. |

Cadence and deadlines:
- Legal Tracker: company sets frequency (firms submit monthly, quarterly or annually); example is the 20th day of the last month of the quarter with a short input window; firms may submit before or after period end (after is recommended so they enter one amount). [Doc] understanding-accruals page.
- Brightflag: deadline configured per client; reminders 5 days before and on the deadline day; **a missed period closes and cannot be reopened by support**; next period opens automatically. [Doc] https://vendorhelp.brightflag.com/hc/en-us/articles/20458093105693-Is-There-an-Accrual-Submission-Deadline ; https://help.brightflag.com/en/articles/753174-vendor-missed-the-accrual-deadline-what-happens-next
- LawVu: org admin enables monthly request, picks recipients (all providers or only those with active/on-hold matters), sets window (commonly about the 20th to 25th); notices on open day and last day. [Doc] https://help.lawvu.com/en/articles/9650470-accruals
- SimpleLegal: accrual windows typically 3-5 days before month end; toggled per matter group, matter or vendor; "Other" category for missing/unopened matters; optional split requiring prior-period and current-period estimates. [Mkt/slides] https://info.simplelegal.com/accruals-success-kit (search summary)
- Firm response rates are poor: SimpleLegal's 2017 study claims 48% response and a 26% delta between estimates and actuals. [Mkt, vendor study] (Guide PDF above.)

### 1.3 Approval, lock, true-up, reversal (Legal Tracker is the best-documented)

- Review options (company chooses): matter owners review/approve (edits notify the firm); sysadmin review during a review period with no formal approval; or no review. Statuses: Pending, Locked, Approved. [Doc] https://www.thomsonreuters.com/en-us/help/legal-tracker/company/accruals-and-unbilled-amounts/working-with-accruals ; understanding-accruals page.
- **Lock:** at the end of input/review period the amounts are locked and kept as a permanent record; after review only coordinators can post an "Amended Amount" (must be enabled), repeatable until the next window opens. [Doc] working-with-accruals page.
- **Missed deadline policy** is a company setting: enter $0 for the firm, or warn that pending invoices may be reduced or rejected. [Doc] understanding-accruals page.
- **Formula:** Accrual Period Ending Balance = firm unbilled amounts + pending invoices (received, not yet sent to AP; disputed invoices included; rejected invoices zeroed, so firms must include them in unbilled). Only current-fiscal-year services count. [Doc] understanding-accruals page.
- **Each export is a full balance, not a delta:** "Accrual is not a net adjustment to the prior accrual." The system locks the original balance for audit/SOX; if an AP extract is sent after lock but before month end, finance is told to use "Remaining Accrual Balance" to avoid double counting. [Doc] https://www.thomsonreuters.com/en-us/help/legal-tracker/company/accruals-and-unbilled-amounts/sending-accruals-to-finance-department
- **Invoices arriving after lock** show in the Accrual Report against a Remaining Accrual Balance; negative remaining means invoices exceeded the accrual. [Doc] understanding-accruals page.
- **Reversal:** none of the Legal Tracker, Brightflag or LawVu pages fetched describe a reversal feature. The design pattern is: the tool sends a period-end full balance, and the ERP journal is configured to auto-reverse on day 1 (see 1.1). The eBilling system's job is to avoid double counting, not to post the reversal. [Inference from Doc + Gen]
- Memo entries (offsetting +/- amounts across service periods) shift spend between fiscal years; overages can be offset by underages on other matters sharing a cost centre. [Doc] understanding-accruals page.
- Reporting for finance: Brightflag "Accruals V2" report has accrual amount and period per matter plus allocation fields such as GL code or entity code [Doc] https://help.brightflag.com/hc/en-us/articles/10240651186205 (search summary). Legal Tracker accrual export is one row per accrual, or split into several rows when the matter has multiple accounting-code allocations. [Doc] sending-accruals page. LawVu sends an Excel file to finance. [Doc] LawVu page.
- LawVu grid shows a matter only if it has an amount or a comment (zero with no comment is hidden). [Doc]

Not found: TeamConnect accrual documentation (Mitratech's blog is generic: https://mitratech.com/resource-hub/blog/best-way-track-accruals/) [Gap]; SimpleLegal/Onit auto-calculation rules and thresholds requiring explanation [Gap]; a documented "explanation required over X%" rule in any vendor [Gap]. Threshold rules below are therefore a design suggestion, not market fact.

### 1.4 Typical data model (synthesised across the vendors above)

- **AccrualPeriod**: fiscal period/year, firm input open/close, company review close, lock timestamp, status (Open, InReview, Locked), missed-deadline policy.
- **AccrualRequest**: one per (period, vendor), notification log, key contact.
- **AccrualEntry** (the firm response): per (period, vendor, matter): amount, currency, comment, source (Firm, InHouse, SystemDerived), status (Pending, Submitted, Approved, Locked, Amended), amended amount + who/why.
- **Computed at lock** (stored, immutable): unbilled amount, pending-invoice amount, ending balance, plus allocation (cost centre/GL) so the export can split rows.
- **Later**: remaining balance as invoices land; journal export batch with reversal date.

Suggested rules (design, not vendor-proven): auto-derive from budget/run-rate for non-responders; flag when estimate deviates from trailing average beyond a configurable percentage; require comment above threshold; lock after close with audited amend path; never recompute a locked balance.

---

## 2. Purchase orders in legal spend

### 2.1 How POs are used

- Legal bills often do not fit a classic PO process (sensitive matters, unknown scope at inception, AP lacks service context). Practitioner opinion in a peer forum, not authoritative. [Gen/opinion] https://www.gartner.com/peer-community/post/how-best-explain-why-legal-bills-not-subject-to-po-process-context-moving-to-ebilling-system
- Where POs are used, a **time-bound blanket PO with a spend cap** is the common corporate pattern (generic supplier policy, not legal-specific), sometimes with a release number appended to the PO number. [Gen] https://kongsberg.com/globalassets/kongsberg-maritime/documents/rrcm-supplier-doc/how-to-invoice-guide-2019-v3b.pdf ; https://support.oracle.com/knowledge/Middleware/1335736_1.html
- The three granularities you named (blanket per firm per year, per matter, from engagement letter) were not documented as such by any fetched source [Gap]. Onit's model supports all three via precedence (below), which is the useful evidence.

### 2.2 How eBilling vendors model them

- **Onit** [Doc] https://onit.screenstepslive.com/s/documentation/a/1972452-purchase-orders :
  - PO fields: PO number (required, read-only after create), Amount, Description, Open flag, Created date, and read-only Pending (linked invoices awaiting review), Used (approved), Balance After Pending = Amount - Used - Pending.
  - Linkage precedence, most specific wins: invoice > vendor-on-matter > matter > vendor default. One PO per invoice. Closed POs cannot be billed against. POs held in the system default currency regardless of vendor currency. Tax can optionally be excluded from balance.
- **Brightflag** [Doc] https://vendorhelp.brightflag.com/en/articles/788094-how-to-find-the-purchase-order-po-number-for-a-ledes-invoice : PO is listed on the matter's Purchase Orders tab; the firm must put it in the LEDES `PO_NUMBER` field and it must match exactly or the invoice will not submit. If the matter needs a PO and none is listed, the client must add it.
- **LawVu** accrual grid exposes cost centres and purchase orders as columns. [Doc] help.lawvu.com page above.
- Coupa/Ariba/Oracle/NetSuite integrations from the eBilling vendors: nothing specific found [Gap]. Generic behaviour: in Coupa suppliers create the invoice from the PO and the amount defaults to PO remaining balance; adding lines can break auto-match; duplicate invoice numbers are checked per supplier [Doc, customer supplier guides] https://www.intuit.com/oidam/intuit/ic/en_us/content/supplier-programs-coupa-portal-supplier-guide.pdf ; https://edge.sitecorecloud.io/issworldsera337-issworld-issworldpro3305-2144/media/files/au-files-2024/coupa-updates/csp-po-invoice-and-tips--tricks.pdf . The common legal pattern is that the eBilling tool does validation and approval, then ships an AP file/API call to the ERP (see 3), rather than routing the firm through Coupa/Ariba. [Inference]

### 2.3 Matching

- SAP Ariba lets the customer choose 2-way (invoice vs PO) or 3-way (plus receipt). For services the approved service entry sheet (SES) acts as the receipt. [Doc, training/vendor blogs] https://learning.sap.com/courses/receiving-purchase-orders-in-sap-ariba-buying-and-invoicing/differentiating-service-receiving-from-standard-processes ; https://www.doxis.com/en/blog/3-way-matching-in-sap
- Legal services: there is no goods receipt; the in-house matter owner's invoice approval in the eBilling tool is functionally the receipt/acceptance. So the eBilling approval can stand in for the SES; AP sees effectively a 2-way match (invoice vs PO) with a pre-approved invoice. [Inference; not stated by a source]
- PO balance check as a billing rule: Onit's pending/used/balance fields make "invoice exceeds PO balance" computable, and its docs note other billing rules can apply to PO-linked invoices. [Doc] Onit page. Whether to block or warn is a policy choice.

---

## 3. GL, cost centre and other coding; AP export and feedback

### 3.1 What AP needs

Fields seen in vendor documentation:

- **Legal Tracker accounting codes** [Doc] https://www.thomsonreuters.com/en-us/help/legal-tracker/company/financial-setup/invoicing-setup-accounting-codes : admin-defined code types (e.g. Cost Center, GL Account), up to 10 allocation rows (simple) or 100 (enhanced), optional dropdown menus to prevent invalid codes; "required" means that if an allocation is entered, all required types must be filled. Codes appear on the matter profile and invoice, so coding is set on the matter and carried to invoices. The page does not say invoice vs line level, and no default-code feature is mentioned. Allocation is percentage/amount rows, which is how a matter splits across cost centres.
- **Brightflag AP batch export** [Doc] https://help.brightflag.com/en/articles/753252-configuring-your-ap-batch-export-file : formats CSV, XLSX, XML or custom; fields include Vendor Name, Entity Name, Entity Tax ID, Entity Address, Invoice Number, Invoice Date, Invoice Due Date, Invoice Totals, Currency Code, Invoice Status, Approved Date, Upload Date, Review Stage, Reviewer Email, Department, Matter Number, Matter Name, Matter Category, Matter Status, Matter Lead, User Email, Billing Start/End Date, Allocations Information; columns, order and names are customisable. Routing: multiple AP routes by entity/geography with priority order and a catch-all. Delivery: email, SFTP batch, or API. https://help.brightflag.com/en/articles/753246-accounts-payable-ap-integrations-in-brightflag
- **Legal Tracker AP extract**: field list not publicly retrievable (the file-specification pages are gated/sparse); feature sheet says fields are configurable. Memo-entry invoices are excluded from the AP extract. [Gap] (search-result summaries of https://www.thomsonreuters.com/en-us/help/legal-tracker/company/file-specifications.html)
- **SimpleLegal/Onit, CounselLink AP file layouts**: not found [Gap].
- Generic AP-needs checklist (design inference, matches the above plus standard ERP invoice interfaces): vendor/supplier ID, invoice number (unique per vendor), invoice date, due date or payment terms, gross/net/tax, currency, legal entity/company code, GL account, cost centre, optional business unit, project/WBS, tax code, remit-to, PO number and PO line when applicable, description, attachment (PDF) reference.

### 3.2 Invoice vs line level

- Evidence points to **matter-level (header-like) allocation rows** in the eBilling tools (Legal Tracker allocation rows on matter/invoice; Brightflag "allocations information" per invoice), with coding derived from the matter rather than typed per line. Line-level coding is rare in the documented tools. Multi-matter invoices are handled in LEDES by line-level matter IDs (see 4). [Inference from Doc]
- LEDES 1998B has `LAW_FIRM_MATTER_ID` and `CLIENT_MATTER_ID` at line level, `LINE_ITEM_NUMBER` unique per invoice, and `EXP/FEE/INV_ADJ_TYPE` codes. [Doc] https://success.mitratech.com/Collaborati/Frequently_Asked_Questions/LEDES_Files/faq_ledes1998b_specs ; https://www.cosmolex.com (search summary). So a line already belongs to a matter in the source format.

### 3.3 Matter-to-cost-centre mapping

Legal Tracker stores accounting codes on the matter profile (above); Brightflag exports a matter's Department and Allocations; LawVu shows cost centres and POs on the matter-level accrual grid. In all, the matter is the carrier of coding defaults; invoice or line may override.

### 3.4 Payment-status feedback ("reverse feed")

- **Legal Tracker AP reverse feed** [Doc] https://www.thomsonreuters.com/en-us/help/legal-tracker/company/file-specifications/ap-reverse-feed-file-specifications.html : columns INVOICE_ID (optional integer, looked up first), INVOICE_NUMBER (string 31, required unless ID), INVOICE_DATE, OFFICE_VENDOR_ID (30, needed when invoice numbers collide), FIRM_NAME (100), PAYMENT_DATE, PAYMENT_EXCHANGE_RATE, PAYMENT_NUMBER (check/wire no., 100), PAYMENT_STATUS (50), PAYMENT_COMMENTS (250). Unknown columns ignored, any order. Upload only allowed after full approval and inclusion in an AP batch; re-uploading rewrites the data. https://www.thomsonreuters.com/en-us/help/legal-tracker/company/invoices/upload-payment-info
- **Brightflag AP Reverse Feed** [Doc]: payment status, payment date, payment reference number via SFTP or API; "Paid" shows only if payment status is fed back; Brightflag does not process payments. https://help.brightflag.com/en/articles/753246-accounts-payable-ap-integrations-in-brightflag
- Lesson: key by (vendor, invoice number) with our own surrogate ID echoed in the export, and tolerate re-sends (idempotent overwrite).

---

## 4. Design implications for R1 data model

Principle: R1 populates a subset of columns; every later feature is a new table or nullable column pointing at stable keys. Nothing here needs a table split later.

### 4.1 Entities (R1 builds the ones marked R1)

| Entity | R1 | Notes |
|---|---|---|
| Invoice (header) | R1 | vendor (firm), vendor invoice number, invoice date, due date, payment terms, currency (default USD, stored), gross/net/tax totals, status, `legal entity`, `purchase order` (nullable lookup), `export batch` (nullable lookup), source file ref. Unique constraint: (vendor, invoice number). |
| InvoiceLine | R1 | belongs to **one matter** (required lookup), line type (fee/expense/adjustment), timekeeper, date, units, rate, amount, UTBMS codes, tax, `purchase order line` (nullable), plus nullable per-line coding override. Multi-matter invoices work because matter lives on the line, matching LEDES line-level matter IDs. |
| Matter | exists | holds default coding (see below) and, later, default PO. |
| Coding (CodingAllocation) | R1 (simple) | child of Invoice *or* Line: legal entity/company code, GL account, cost centre, business unit, project/WBS, tax code, percent or amount. Start with a single resolved allocation per line derived from matter defaults; the table shape allows splits (Legal Tracker allows many rows) without change. |
| MatterCodingDefault | R1 | matter -> default entity, GL, cost centre, etc. Resolution order: line override > invoice override > matter default > vendor default > entity default. Export always writes the resolved value and records its source. |
| Vendor/supplier | R1 | AP supplier ID (per legal entity), remit-to, tax ID, default payment terms; office-level vendor ID as in Legal Tracker reverse feed. |
| ExportBatch + ExportBatchItem | R1 | batch id, format (CSV/JSON/XML), created, file hash, item = invoice id + exported snapshot values. Immutable once generated. Re-export = new batch. |
| PaymentStatus (feedback) | R1 shell, import later | invoice FK, status, payment date, payment reference, exchange rate, comments, source file, received at. Append-only rows; "current" = latest. Matches Legal Tracker/Brightflag fields. |
| PurchaseOrder | later | number, vendor, legal entity, currency, amount, valid from/to, open flag, scope (vendor / matter / vendor-on-matter), Pending/Used/Balance as computed values. Invoice and line already carry nullable PO lookups, so adding POs adds only the table and a billing rule. |
| AccrualPeriod / AccrualRequest / AccrualEntry / AccrualSnapshot / JournalExportBatch | later | per 1.4. Entries key to (period, vendor, matter) and reuse the same Coding resolution and ExportBatch pattern (batch type = Accrual journal, with reversal date). |

### 4.2 Choices to make now

1. **Matter on the line, not the header.** Required for multi-matter invoices and consistent with LEDES. The header is a container; header totals are the sum of lines plus any header-level adjustments.
2. **Coding resolved at line level, stored per line at export time, defaulted from matter.** Invoice-level coding becomes the special case where all lines resolve to the same values. This avoids a restructure if a customer's AP wants per-line GL. Cost: one resolved-coding row per line (small).
3. **Currency fields everywhere from day one** (currency code on invoice, line, and future PO/accrual, plus an exchange-rate field used only by payment feedback). R1 hard-validates USD. Onit holds POs in the system default currency; note this when multi-currency arrives. [Doc]
4. **Snapshot at export.** Export items copy the resolved values so later edits to matter defaults never change a past file. Needed for audit and for accrual "never recompute a locked balance" symmetry. [Inference from Legal Tracker lock behaviour]
5. **Our own invoice ID in every outbound file** and accepted back in the reverse feed, with (vendor, invoice number) fallback. [Doc pattern]
6. **Status model**: keep eBilling approval status separate from AP/payment status (Brightflag only shows "Paid" when fed back). Do not overload one state field.
7. **PO number on the invoice**: keep a captured string (as extracted/submitted) separate from the validated lookup, so R1 can store what the firm quoted and the later PO rule validates it. Brightflag requires exact match. [Doc]
8. **Export formats**: model the AP file as a mapping layer (field list, order, names, format) rather than hard-coded columns; Brightflag customises columns per customer, Legal Tracker's AP fields are configurable. [Doc]
9. **Memo/zero-pay invoices** need a flag to exclude from AP export (Legal Tracker excludes memo entries). [Doc]
10. **Do not conflate ASC 450 reserves with accruals**; no litigation reserve fields. [Gen]

### 4.3 Open questions for the customer / finance

- Is coding required at invoice or line level in their AP system, and is GL per expense type (fees vs expenses) or per matter?
- Do they require POs at all for legal, and if so granularity and whether to block or warn on balance breach?
- Accrual cadence, who owns the firm-response policy, and who posts reversals in the ERP.
- Which ERP and file contract (Coupa, Ariba, Oracle, NetSuite, SAP): none of the vendors' public docs revealed these mappings, so they must come from the customer's AP team. [Gap]

## Source reliability summary

Strongest: Thomson Reuters Legal Tracker help pages, Brightflag help centre, LawVu help, Onit documentation (all vendor-authored product documentation). Weakest: SimpleLegal statistics (vendor study), PERSUIT/Apperio (not retrieved), Gartner peer post (opinion), Ariba matching sources (training material and blogs). The ASC 450 distinction, ERP reversal conventions and the 2-way-with-approval-as-receipt interpretation are not source-backed.
