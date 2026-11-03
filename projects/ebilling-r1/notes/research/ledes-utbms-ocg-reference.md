# LEDES / UTBMS / OCG / Rate Card / PDF Invoice Reference (eBilling R1)

Research date: 2026-10-14. Primary sources are the LEDES Oversight Committee (LOC) sites ledes.org and utbms.com, spec spreadsheets downloaded from them, and published outside counsel guidelines (OCGs). Statements marked **(inference)** are design judgement, not sourced facts. Statements marked **(vendor)** come from vendor marketing or help pages and are not neutral.

Source key (used throughout):

- [L-HOME] https://ledes.org/
- [L-98B] https://ledes.org/ledes-98b-format/
- [L-98BI] https://ledes.org/ledes-98bi-format/
- [L-2000] https://ledes.org/ledes-2000-format/
- [L-X20] https://ledes.org/ledes-xml-ebilling-ver-2-0-format/
- [L-X21] https://ledes.org/ledes-xml-ebilling-ver-2-1-format/
- [L-X22] https://ledes.org/ledes-xml-ebilling-ver-2-2/
- [L-XSPEC] https://ledes.org/wp-content/uploads/2026/04/LEDES_XML_E-Billing_2-2-2-Update-4-2026.xlsx (XML 2.2.2 spec workbook; I downloaded and read all five sheets)
- [L-98BI-SPEC] https://ledes.org/wp-content/uploads/2020/02/LEDES98BI-V5-Format-Rev-1-2020.xlsx (98BI V5 field spec workbook; read in full)
- [L-TK] https://ledes.org/loc-revised-timekeeper-classification-codes-august-2014/ and workbook https://ledes.org/wp-content/uploads/2023/06/LEDES-Revised-Timekeeper-Classifications-Reference-Document-6-2023-Upd.xlsx
- [L-API] https://ledes.org/ledes-api/ and https://ledes.org/ledes-api-v1-2020/
- [U-HOME] https://utbms.com

---

## 1. LEDES formats

### 1.1 Status summary

| Format | Fields / structure | Current version and status | Source |
|---|---|---|---|
| LEDES 1998B | ASCII, pipe-delimited, 24 fields, one flat row per line item | "Not intended that the LEDES 1998B format will be modified in the future"; spec doc revised Aug 2014 (Rev 7-2014) to reference timekeeper classifications. "Most widely used" US format | [L-98B]; same claim at https://www.thomsonreuters.com/en-us/help/legal-tracker/company/file-specifications/ledes-file-formats |
| LEDES 1998BI | ASCII, pipe-delimited, 52 fields (flat) | **V5 (Rev 1-2020)**, ratified Jan 2020, 52 fields. Not expected to change further; global needs are routed to the XML formats. History: beta 2-2005 (33 fields), Ver 1 3-2006 (50), update 9-2007 (50), **Ver 2 4-2008 (52)**, Ver 5 1-2020 (52). Proposed by UK group LITIG in 2004. Thomson Reuters Legal Tracker lists it as "1998BI (v5)" and says "51 fields" (the LOC page says 52) | [L-98BI]; https://www.thomsonreuters.com/en-us/help/legal-tracker/company/file-specifications/ledes-file-formats |
| LEDES 2000 | Not described | "No longer supported by the LEDES Oversight Committee" | [L-2000] |
| LEDES XML 2.0 | XML/XSD, 15 segments | 2.0 (1-2006, 153 elements), 2.0.1 (4-2008), 2.0.2 (7-2014), 2.0.3 (1-2020), **2.0.4 (3-2023, 172 elements)**, 2.0.4 update (6-2023). "Sufficient for many ebilling situations." 2006 version replaced LEDES 2000; added itemized complex taxes, alternative fee arrangements, credits and debits, multiple vendor tax IDs | [L-X20] |
| LEDES XML 2.1 | XML/XSD, 16 segments | 2.1 (4-2008, 191 elements) ... 2.1.3 (1-2020, 192), 2.1.4 (3-2023, 205; new rate schema), **2.1.5 (4-2026, 205, current)**; 2.1.5 fixes a math error that applied Client Funds twice in the invoice total | [L-X21] |
| LEDES XML 2.2 | XML/XSD, 18 segments | 2.2 (1-2020, 205 elements; added tiered taxes, e.g. Japanese withholding tax), 2.2.1 (3-2023, 216; home-currency timekeeper rate schema), 2.2.1 update (6-2023), **2.2.2 (4-2026, 216, current)**; same Client Funds math fix | [L-X22] |
| LEDES XML Budgeting; XML Timekeeper Attributes | Separate XML formats | Listed as "Other Data Exchange Formats" | [L-HOME]; timekeeper attributes spec at https://ledes.org/ledes-xml-timekeeper-attributes-format/ |
| LEDES Standardized Error Codes V2 | Standard list of validation error codes | Ratified Sept 2024; more codes expected later for "less common validation errors" | https://ledes.org/2024/09/ledes-standardized-error-codes-v2-ratified/ ; index https://ledes.org/ledes-error-codes/ |
| LEDES API v1 | System-to-system interface between law firm billing apps and corporate matter-management / e-billing apps | Ratified 2020-02-03; Phase 2 development announced to begin 2020-06-01; API-testing vendor survey posted 2022-02-22; nothing newer listed. Capabilities: check invoice status, submit invoices, accruals and other legal data. The page does not state JSON/REST, endpoints or auth | [L-API] |
| Newer / JSON | **No JSON invoice standard found.** The LOC home page lists no standards ratified in 2025 or 2026 other than the XML 2.1.5 / 2.2.2 updates (April 2026) and the Error Codes V2 (2024); "Proposed Standards Requiring Public Comment" currently reads "No Proposed Standards" | [L-HOME] |

Version-date caveat: the LEDES 2.2 page body still calls 2.2.1 the newest while its version table lists 2.2.2 (April 2026); the spec workbook itself is dated "2.2.2, 2/2026" on one sheet and "4/2026" on another. Treat 2.2.2 as current. Source: [L-X22], [L-XSPEC].

Which format to use: the LOC says to follow the client's instruction first; the field-level difference table between XML versions is an image/PDF at https://ledes.org/xml-format-differences/ and https://ledes.org/wp-content/uploads/2025/03/Differences-Between-XML-Formats.pdf. In the 2.2.1 vs 2.1.4 vs 2.0.4 comparison, the 2.2.1-only items are tax tiers, `taxing_jurisdiction_country/locality`, `tax_on_tax`, `withholding_tax`, tax-invoice / bill-to / payable-by party fields, `tax_point_date`, `credit_note`, regulatory statement, `po_number`, `account_type`, and work-location fields on fee/expense. Source: the Differences PDF above.

### 1.2 Tax support by format (vendor summary, consistent with spec workbooks)

| Format | Tax capability | Source |
|---|---|---|
| 1998B | No tax fields | https://brightflag.com/resources/what-are-ledes-files-an-introduction/ (vendor); no tax fields in the 24-field list below |
| 1998BI | One tax per line (rate, total, type) | same Brightflag page; field list in 1.4 |
| XML 2.0 | Multiple taxes per line item, complex tax scenarios | same Brightflag page; [L-X20] |
| XML 2.2 | Only format with tiered taxes | same Brightflag page; [L-X22] |

### 1.3 LEDES 1998B field list (24 fields)

Source: Mitratech spec page (vendor transcription of the LOC spec) https://success.mitratech.com/Collaborati/Frequently_Asked_Questions/LEDES_Files/faq_ledes1998b_specs . The authoritative LOC spreadsheet is the legacy `.xls` at https://ledes.org/wp-content/uploads/2014/08/Ledes-1998B-Format-Rev-7-2014.xls (I could not parse `.xls` here; lengths below are the Mitratech transcription and should be re-verified against it). The first 24 fields of 98BI V5 are structurally the same list (see 1.4), and that workbook is the one I read directly. Differences between the two transcriptions are flagged.

**Structure:** there is no separate header record. The first line is the literal text `LEDES1998B[]`, the second line is the column header row, and each following line is one line item; invoice-level fields repeat on every row. Invoice-level fields (invoice date, number, total, billing dates, description) are only honored from the first row per invoice number (98BI workbook Notes column for fields 1, 5, 6, 7, 8). The `LEDES1998B[]` first-line convention is from the same Mitratech page's sample references and the general format; I did not see it quoted in the extracted tables, so verify against the LOC sample file linked on [L-98B].

| # | Field | Required in practice | Type / max | Notes |
|---|---|---|---|---|
| 1 | INVOICE_DATE | Yes | Date 8, YYYYMMDD | Null is an error |
| 2 | INVOICE_NUMBER | Yes | Char 20 | Unique per firm (98BI allows 50) |
| 3 | CLIENT_ID | Yes | Char 20 | Law firm's client code |
| 4 | LAW_FIRM_MATTER_ID | Optional per Mitratech; Required in 98BI V5 | Char 20 | Firm's matter code |
| 5 | INVOICE_TOTAL | Yes | Currency 14.2 (98BI: 12.4) | Sum of all LINE_ITEM_TOTAL |
| 6 | BILLING_START_DATE | Optional per Mitratech; Required in 98BI V5 | Date 8 | |
| 7 | BILLING_END_DATE | Optional per Mitratech; Required in 98BI V5 | Date 8 | |
| 8 | INVOICE_DESCRIPTION | Most clients require | Char 2,000 (98BI: 15 KB) | |
| 9 | LINE_ITEM_NUMBER | Yes | Char 20 | Unique per invoice; typically the firm's time-entry key |
| 10 | EXP/FEE/INV_ADJ_TYPE | Yes | Char 2 | `E` expense, `F` fee, `IE` invoice-level adjustment on expenses, `IF` invoice-level adjustment on fees (98BI workbook: "Line item adjustment types T and IT are eliminated") |
| 11 | LINE_ITEM_NUMBER_OF_UNITS | Yes (except invoice-level adjustment) | Numeric 14.2 | Hours for fees, item count for expenses |
| 12 | LINE_ITEM_ADJUSTMENT_AMOUNT | Optional | Currency 14.2 | Positive = premium, negative = discount |
| 13 | LINE_ITEM_TOTAL | Yes | Currency 14.2 | = (unit cost x units) + adjustment (98BI adds `+ LINE_ITEM_TAX_TOTAL`) |
| 14 | LINE_ITEM_DATE | Yes | Date 8 | Date incurred; usually inside billing period |
| 15 | LINE_ITEM_TASK_CODE | Required for fee lines | Char 20 | UTBMS-style; must be client-authorized |
| 16 | LINE_ITEM_EXPENSE_CODE | Required for expense lines | Char 20 | |
| 17 | LINE_ITEM_ACTIVITY_CODE | Client-dependent | Char 20 | 98BI notes: "Task codes may be used without activity codes but activity codes should not be used without task codes" |
| 18 | TIMEKEEPER_ID | Required for fee lines | Char 8 (98BI: 20) | Must match client-authorized timekeeper |
| 19 | LINE_ITEM_DESCRIPTION | Most clients require | Char 2,000 (98BI: 15 KB) | May be null on coded expense lines (98BI workbook) |
| 20 | LAW_FIRM_ID | Per Mitratech "not used" (taken from firm profile); Required in 98BI V5 (federal tax ID or non-US tax ID) | Char 20 (98BI: 50) | Thomson Reuters says enter "None" if no federal ID: https://www.thomsonreuters.com/en-us/help/legal-tracker/company/file-specifications/ledes-file-formats |
| 21 | LINE_ITEM_UNIT_COST | Yes (except invoice-level adjustments) | Currency 14.5 (98BI: 10.4) | **Standard, undiscounted** rate; discounts go in field 12 |
| 22 | TIMEKEEPER_NAME | Informational | Char 30 | "Last, First" |
| 23 | TIMEKEEPER_CLASSIFICATION | Optional in Mitratech; Required for task items in 98BI V5 | Char 10 | Must match codes in the LOC classification list (see section 2) |
| 24 | CLIENT_MATTER_ID | Yes unless client assigns no matter IDs | Char 20 | Client-assigned matter |

Representation summary for 98B:

- **Fees vs expenses:** the `F`/`E` type in field 10; fee lines carry task (+activity) code, timekeeper and hours; expense lines carry an expense code and item count. Source: Mitratech spec page above.
- **Taxes:** none in 98B. Source: https://brightflag.com/resources/what-are-ledes-files-an-introduction/ (vendor) and the field list above.
- **Adjustments:** per-line via field 12; invoice-level via `IF`/`IE` rows whose unit cost is ignored. Source: Mitratech spec page.
- **Timekeeper info:** only ID, name, classification repeated on each fee row; no rate-vs-standard-rate field apart from unit cost. Source: same.
- **Mandatory in practice:** Legal Tracker says task code "can be blank unless the client requires it for trademark or patent matters"; expense lines need a UTBMS expense code including invoice-level expense adjustments. Source: https://www.thomsonreuters.com/en-us/help/legal-tracker/company/file-specifications/ledes-file-formats . Brightflag notes LEDES itself does not force UTBMS codes. Source: https://brightflag.com/resources/what-are-ledes-files-an-introduction/ (vendor).

### 1.4 LEDES 1998BI V5 additional fields (25 to 52)

Source for all rows: [L-98BI-SPEC]. Fields 1-24 as in 98B (with the Required/length differences noted above).

| # | Field | Required | Type | Note |
|---|---|---|---|---|
| 25 | PO_NUMBER | Optional | Char 100 | Client PO |
| 26 | CLIENT_TAX_ID | Required | Char 20 | VAT number in UK |
| 27 | MATTER_NAME | Required | Char 255 | Information only |
| 28 | INVOICE_TAX_TOTAL | Optional | Currency 12.4 | Sum of line taxes; null means tax-exempt invoice |
| 29 | INVOICE_NET_TOTAL | Required | Currency 12.4 | Sum of line totals minus tax total |
| 30 | INVOICE_CURRENCY | Required | Char 3 | ISO 4217 |
| 31 | TIMEKEEPER_LAST_NAME | Required if name filled | Char 30 | |
| 32 | TIMEKEEPER_FIRST_NAME | Required if name filled | Char 30 | |
| 33 | ACCOUNT_TYPE | Required | Char 1 | `O` own account, `T` third party |
| 34-40 | LAW_FIRM_NAME, _ADDRESS_1, _ADDRESS_2, _CITY, _STATEorREGION, _POSTCODE, _COUNTRY | Optional | Char 60/60/60/40/40/20/3 | Display fields |
| 41-47 | CLIENT_NAME, _ADDRESS_1, _ADDRESS_2, _CITY, _STATEorREGION, _POSTCODE, _COUNTRY | Optional | Same | Display fields |
| 48 | LINE_ITEM_TAX_RATE | Optional | Decimal 0.10 | 0 to 1 |
| 49 | LINE_ITEM_TAX_TOTAL | Required | Currency 10.4 | = ((unit cost x units) + adjustment) x tax rate |
| 50 | LINE_ITEM_TAX_TYPE | Optional | Char 20 | VAT, GST, PST, Other; informational |
| 51 | INVOICE_REPORTED_TAX_TOTAL | Optional | Currency 12.4 | Tax in national currency as reported to authorities; informational |
| 52 | INVOICE_TAX_CURRENCY | Optional | Char 3 | Currency of field 51 |

Legal Tracker adds that for 1998BI, client name/address fields become required when the client enforces EU VAT rules or the matter has a Billed-To Entity. Source: https://www.thomsonreuters.com/en-us/help/legal-tracker/company/file-specifications/ledes-file-formats .

### 1.5 LEDES XML 2.2.x structure (header vs line items)

Source for this subsection: [L-XSPEC] (sheets "Content Segments", "Segment Hierarchy", "Detail Format Specifications", "Math Statement"). 216 data elements in 18 segments.

Hierarchy (parent/child, multiplicity):

| Segment | Level | Purpose |
|---|---|---|
| @FIRM | file, 1+ per file; must start the file | Firm name, address, contacts, remit address |
| @TAX | file, 0+ | Lookup of each tax type used (rate, `tax_tiers`, `withholding_tax`, `tax_on_tax`, `increase_decrease`, jurisdiction) |
| @CLIENT | 1+ per @FIRM | Client name, address, contact |
| @INVOICE | 1+ per @CLIENT | Invoice header: `inv_id`, `inv_date`, `inv_due_date`, `inv_currency`, `inv_start_date`, `inv_end_date`, `inv_desc`, tax-invoice/bill-to/payable-by parties, `credit_note`, `rejected_inv_replacement` (+ reference), `inv_payment_terms`, `inv_total_tax`, `inv_total_net_due`, `exchange_rate` |
| @TAX_TIER_SUMMARY / @TAX_TIER_DETAIL | 0+ per invoice | Tiered tax roll-up and tiers (2.2 only) |
| @REGULATORY_STATEMENT | 0+ per invoice | Statutory text required in firm's country |
| @MATTER | 1+ per @INVOICE | `cl_matter_id`, `lf_matter_id`, `matter_name`, `po_number`, `account_type`, `matter_billing_type` (`""`, `CT`, `FF`, `GW`, `MPF`, `TM`), `matter_final_bill`, totals of fees/expenses, matter-level discount/credit totals, tax on fees/exp, net fees/exp, `matter_funds_applied`, `matter_total_due` |
| @TAX_SUMMARY | 0+ per matter | Tax by `tx_id` |
| @MATTER_DISC_CRED / @TAX_MATTER_DISC_CRED | 0+ per matter | Matter-level discounts, credits and AFA items |
| @TKSUM | 0+ per matter; one per biller/rate | `tk_id`, `tk_lname`, `tk_fname`, `tk_level` (LOC classification), `tk_rate`, `tk_rate_other_iso`, `tk_other_iso`, `tk_exchange_rate`, `tk_exchange_rate_date` |
| @FEE | 0+ per matter | `charge_date`, `tk_id`, `tk_level`, `charge_desc`, `task_code`, `task_activity`, `charge_type` (`U` unit price / `F` fixed), `units`, `rate`, `base_amount` (original WIP), `item_disc_cred_amount`, `total_amount` |
| @FEE_ITEM_DISC_CRED | 0+ per fee | `disc_cred` in {`FIA` fee item adjustment, `FID` fee item discount, `FIDHR` discount hourly rate, `FIFF` fixed fee, `Other`}, `disc_cred_type` Percent/Flat, `increase_decrease`, `discount_percent`, `disc_cred_amount` |
| @TAX_ITEM_FEE | 0+ per fee | Links fee to `tx_id` |
| @EXPENSE (+ @EXPENSE_ITEM_DISC_CRED, @TAX_ITEM_EXPENSE) | 0+ per matter | `charge_date`, optional `tk_id`/`tk_level`, `charge_desc`, `task_code`, `expense_code`, `charge_type`, `units`, `rate`, `base_amount`, `item_disc_cred_amount`, `total_amount`, `payee_name` (set means third-party disbursement) |
| @EXTEND_HEADER / @EXTEND_DATA | on most segments | Client/firm-specific extensions |

Practical points from the same workbook:

- Required vs optional: on @FEE, task and activity codes are **Optional** (one code per line); `charge_date`, `tk_id`, `tk_level`, `charge_desc`, `units`, `rate`, `base_amount`, `total_amount` are required. On @EXPENSE most fields incl. `charge_date`, `expense_code` are optional while description, units, rate and amounts are required.
- Each @FEE rate "is the same value that appears in TKSUM.tk_rate"; a timekeeper billing at two rates in a period appears as two @TKSUM rows.
- Files should be UTF-8, period decimal separator (workbook notes).
- XML 2.x "Math Statement" sheet fixes the order of operations: item discounts apply before matter-level splits; tiered taxes cannot be used in split invoices or multi-matter invoices; expenses at a different split must be billed on a separate invoice.
- Fee `base_amount` is the original WIP value, which makes "write-down / discount applied" explicit (unlike 98B, where only the undiscounted unit cost and a signed adjustment appear).

### 1.6 What is commonly mandatory in practice (summary)

All of the following are cited above; this is a consolidation.

| Item | Typical practice | Source |
|---|---|---|
| Format | Follow the client; 98B is the default US format; XML for tax/AFA/multi-currency | [L-98B], https://ledes.org/xml-format-differences/ |
| UTBMS codes | Client-specified; invoices without valid codes can be rejected by client rules | https://info.simplelegal.com/hubfs/Content/Billing%20Guidelines/Legal%20Billing%20Guideline%20Best%20Practices.pdf ; Legal Tracker page above |
| Timekeeper ID must pre-exist and be client-authorized | Yes | Mitratech 98B spec page |
| Matter IDs must match the client's matter | Yes (TC Energy OCG 6.6: invoices with the wrong matter number are rejected) | https://www.tcenergy.com/siteassets/pdfs/vendors/legal/tce-billing-and-staffing-guidelines.pdf |
| Line math reconciles | Required by spec formulas | [L-98BI-SPEC], [L-XSPEC] |

---

## 2. Timekeeper classification codes and rate-related fields

Source for the whole table: [L-TK] workbook "LOC Revised Timekeeper Classification Codes - Ratified August 2014", revised 6/25/2023 (RSRCHR changed to law-firm = yes). The workbook applies to all LEDES standards (page text at the [L-TK] URL). Columns in the workbook: Client / LF (law firm) / eD (eDiscovery) / Temp-contract provider / Other legal service provider / Fee-support-only. I show the code, text, and the mapping from former codes where the workbook gives one. Law-firm-usable flag shown as "LF" where the workbook marks it so.

| Code | Classification | Notes (legacy mapping from workbook) |
|---|---|---|
| PARTNR | Partner | 98B `PT`, 98BI `PT` |
| OFCOUN | Of Counsel | `OC` |
| ASSOC | Associate | 98B `AS`, 98BI `AS` |
| TRANEE | Associate Trainee | Qualifies as Associate after practical experience (UK trainee solicitor) |
| STFATT | Staff Attorney | Permanent, no partnership track |
| SECNDE | Secondee | Firm attorney seconded to the client |
| LGLINT | Legal Intern | 98BI `SI`; not supported in 98B, 2000, XML 2.0/2.1 |
| PARALG | Paralegal | Licensed or certified; 98BI `PL`; 98B "not supported" |
| LGLAST | Legal Assistant | Unlicensed; 98B `LA`; 98BI not supported |
| CLKSEC | Clerk or Secretary | 98B `OT`, 98BI `SE`, `NP` |
| NBOTHR | Other | Non-billing system user; 98B `OT`, 98BI `OT`; XML 2.1 not supported |
| LIBRRN | Librarian | |
| RSRCHR | Research Specialist/Researcher | Common in Europe and Asia |
| ANALST | Analyst | |
| HLNLPF | High-Level Non-Lawyer Professional | Accountant, actuary, CPA, compliance professional |
| LGPRMG | Legal Project Manager | |
| IPAGNT | IP Agent | Internal resource at client or firm |
| CSTCNS | Costs Counsel | UK costs lawyer |
| TEMPAT | Contract Attorney | Outsourced attorney timekeeper |
| TEMPOS | Contract Other Staff | |
| DSCATT, DSCPMG, DSCANJ, DSCANS, DOCCDR, DOCRAT, DOCRNA, FORANL, EVDEVL, MDRCRV, SOLDSG, TRAINR, TRNDEV, TRANSL | eDiscovery / specialist roles (discovery attorney, discovery project manager, junior and senior discovery analyst, document coder, document reviewer attorney / non-attorney, forensic analyst, evidentiary developer, medical records reviewer, solution designer, trainer, training developer, translator) | Mostly for legal-service-providers and temp staff |
| CONSLT, EXPERT, INVSTG, IPSVPV, ARBITR, MEDITR, CTRPTR, VIDOGR, SPAGNT, TRNFAG | External providers (consultant, expert, investigator, IP service provider, arbitrator, mediator, court reporter, videographer, service of process agent, transfer agent) | "Other legal service provider" column |
| ADJSTR, AUDITR, RSPROF, STFCNS, TPADMN | Client-side roles (adjuster, auditor, responsible professional, staff counsel, third-party administrator) | "Client" column |
| ADJSMT | Adjustment | Invoice adjustment row for 98B/98BI/2000 when specified by the client |
| FLTFEE | Flat/Fixed Fee | Flat-fee arrangement row for 98B/98BI/2000 |

Rate-related design notes (all [L-TK] page text):

- A classification was added only "if it provided insight into the timekeeper's billing rate" or aided analysis (e.g. Paralegal vs Legal Assistant, because a paralegal is expected to rate higher).
- Clients choose which classifications they reimburse; if a client does not accept a classification it must state those services cannot be billed under any other classification.
- Junior/Senior distinctions are left to the vendor's definitions; contract/temporary timekeepers are split only into attorney/non-attorney.
- The workbook documents which vendor types may use each classification; clients "should not manipulate this."
- **Legacy code mapping matters for ingest:** many billing tools still emit the two-letter codes (`PT`, `AS`, `OC`, `LA`, `OT`, `PL`; vendor docs also list `SI`, `SE`, `NP`) per https://support.leanlaw.co/leanlaw-ledes-codes-reference (vendor help page found via search) and the workbook mapping column. **(inference)** Normalize both vocabularies into one classification lookup.

Rate-related fields by format:

| Format | Rate fields | Source |
|---|---|---|
| 1998B / 98BI | `LINE_ITEM_UNIT_COST` (standard undiscounted rate), `LINE_ITEM_ADJUSTMENT_AMOUNT`, `TIMEKEEPER_CLASSIFICATION`, `TIMEKEEPER_ID` | Mitratech page, [L-98BI-SPEC] |
| XML 2.x | `@TKSUM.tk_rate` (+ `tk_rate_other_iso`, `tk_other_iso`, `tk_exchange_rate`, `tk_exchange_rate_date` in 2.0.4/2.1.4/2.2.1+ for home-currency rates), `@FEE.rate`, `@FEE.base_amount`, `@FEE_ITEM_DISC_CRED` (`FID` percent discount, `FIDHR` discount-to-hourly-rate flat, `FIA`, `FIFF`), `@MATTER.matter_billing_type` (`CT`,`FF`,`GW`,`MPF`,`TM`) | [L-XSPEC]; versions at [L-X20], [L-X21], [L-X22] |

---

## 3. UTBMS code sets

### 3.1 Who maintains, versioning

- The LEDES Oversight Committee hosts and develops the UTBMS code sets at utbms.com ("A Global Resource on UTBMS"). Original ABA sets (1997) are reproduced by the LOC "with the permission of the ABA"; the ABA has "temporarily removed" its own UTBMS pages. Sources: https://utbms.com/aba-litigation-codes/ , https://utbms.com/aba-expense-codes/ , https://www.simplelegal.com/?p=5435 (search-result summary: universal activity codes maintained by the LOC).
- The ABA Litigation Section page cited by utbms.com: https://www.americanbar.org/groups/litigation/resources/uniform-task-based-management-system/ (not re-verified; link taken from utbms.com).
- Versioning is by set, with no single release train. Dated items: LOC Patent Codes 2009, LOC Trademark Codes 2009, ABA Workers' Compensation 2010, eDiscovery codes ratified 2011-07-13, LOC Revised Activity and Expense Codes 2013, **LOC IP Patent Prosecution Codes 2023** (released 2023-04-26; update noted 2024-06-10), 1997 originals for litigation, counseling, bankruptcy, project, activity and expense. Sources: [U-HOME], https://utbms.com/ip-patent-prosecution-codes/ , https://utbms.com/aba-workers-compensation-codes/ , https://utbms.com/loc-ediscovery-codes/ .

### 3.2 Code sets available (23 listed at utbms.com)

Source for table: [U-HOME] (code set index) and the linked pages.

| Code set | Body | Link |
|---|---|---|
| ABA Litigation | ABA | https://utbms.com/aba-litigation-codes/ |
| ABA Counseling | ABA | https://utbms.com/aba-counseling-codes/ |
| ABA Project | ABA | https://utbms.com/aba-project-codes/ |
| ABA Bankruptcy | ABA | https://utbms.com/aba-bankrupty-codes/ (sic) |
| ABA Criminal Law | ABA | https://utbms.com/aba-criminal-law-code-set/ |
| ABA Real Estate | ABA | https://utbms.com/aba-real-estate-utbms-code-sets/ |
| ABA Workers' Compensation (2010; 5 phases, 27 tasks) | ABA | https://utbms.com/aba-workers-compensation-codes/ |
| ABA Activity (original 1997, A101-A111) | ABA | https://utbms.com/aba-activity-codes/ |
| ABA Expense (original 1997, E101-E124) | ABA | https://utbms.com/aba-expense-codes/ |
| ABA/LOC Mergers & Acquisitions | Joint ABA-LOC | https://utbms.com/abaloc-mergers-acquisitions-utbms-codes/ |
| LOC Revised Activity (2013) | LOC | https://utbms.com/loc-revised-activity-codes/ |
| LOC Revised Expense (2013) | LOC | https://utbms.com/loc-revised-expense-codes/ |
| LOC Revised Project | LOC | https://utbms.com/loc-revised-project-codes-2/ |
| LOC Patent (2009) | LOC | https://utbms.com/loc-patent-codes/ |
| LOC IP Patent Prosecution (2023) | LOC | https://utbms.com/ip-patent-prosecution-codes/ |
| LOC Trademark (2009) | LOC | https://utbms.com/loc-trademark-codes/ |
| LOC eDiscovery | LOC | https://utbms.com/loc-ediscovery-codes/ |
| LOC GRC | LOC | https://utbms.com/loc-grc-utbms-code-set/ |
| LOC Universal UTBMS Definitions | LOC | https://utbms.com/loc-universal-utbms-definitions-2/ |
| Canadian Litigation | Canadian Bar Association | https://utbms.com/canadian-litigation-code-set/ |
| DRI Revised Litigation | DRI | https://utbms.com/dri-revised-litigation-codes/ |
| Jackson EW-UTBMS Litigation "J-Code-Set" | Judiciaries of England and Wales | https://utbms.com/jackson-ew-utbms/ |
| Yerra Global KM Group KM Code Set | Yerra Global KM Expert Group | https://utbms.com/yerra-global-km-group-km-code-set/ |

Structure rule used by all sets: a **phase** code (x100) groups **task** codes (x1nn); the litigation set is "5 phases and 29 more granular task codes" (https://utbms.com/aba-litigation-codes/). Activity and expense codes form universal, separate dimensions. Sources: [U-HOME]; Task/Activity relation in [L-98BI-SPEC] note on field 17.

### 3.3 ABA Litigation code set (L-codes), complete

Source: ABA UTBMS Litigation Codes 1997 workbook linked from https://utbms.com/aba-litigation-codes/ (https://utbms.com/wp-content/uploads/2023/11/ABA-UTBMS-Litigation-Codes-1997.xlsx), downloaded and read. The set is "intended for use in all adversarial matters including litigation, binding arbitrations, and regulatory/administrative proceedings." Phase rows are L100, L200, L300, L400, L500.

| Code | Name | Level |
|---|---|---|
| L100 | Case Assessment, Development and Administration | Phase |
| L110 | Fact Investigation/Development | Task |
| L120 | Analysis/Strategy | Task |
| L130 | Experts/Consultants | Task |
| L140 | Document/File Management | Task |
| L150 | Budgeting | Task |
| L160 | Settlement/Non-Binding ADR | Task |
| L190 | Other Case Assessment, Development and Administration | Task |
| L200 | Pre-Trial Pleadings and Motions | Phase |
| L210 | Pleadings | Task |
| L220 | Preliminary Injunctions/Provisional Remedies | Task |
| L230 | Court Mandated Conferences | Task |
| L240 | Dispositive Motions | Task |
| L250 | Other Written Motions and Submissions | Task |
| L260 | Class Action Certification and Notice | Task |
| L300 | Discovery | Phase |
| L310 | Written Discovery | Task |
| L320 | Document Production | Task |
| L330 | Depositions | Task |
| L340 | Expert Discovery | Task |
| L350 | Discovery Motions | Task |
| L390 | Other Discovery | Task |
| L400 | Trial Preparation and Trial | Phase |
| L410 | Fact Witnesses | Task |
| L420 | Expert Witnesses | Task |
| L430 | Written Motions and Submissions | Task |
| L440 | Other Trial Preparation and Support | Task |
| L450 | Trial and Hearing Attendance | Task |
| L460 | Post-Trial Motions and Submissions | Task |
| L470 | Enforcement | Task |
| L500 | Appeal | Phase |
| L510 | Appellate Motions and Submissions | Task |
| L520 | Appellate Briefs | Task |
| L530 | Oral Argument | Task |

Notable definitional rules in the workbook that matter for validation: "Most legal research will be under the primary task for which the research is conducted" (L120 note); L140 is "narrowly defined" and vendor database-building work "should be an Expense"; once trial begins courtroom time presumptively goes to L450; L230 vs L160 vs L450 pre-trial conference distinctions.

### 3.4 Activity codes (A-codes)

Two generations exist and clients use either; the code string alone does not tell you which. **(inference)** Store a `code_set_version` on the matter or client rule set.

**Original ABA activity codes (1997), 11 codes.** Source: https://utbms.com/wp-content/uploads/2023/11/ABA-UTBMS-Activity-Codes-1997.xlsx (linked from https://utbms.com/aba-activity-codes/).

| Code | Name |
|---|---|
| A101 | Plan and prepare for |
| A102 | Research |
| A103 | Draft/revise |
| A104 | Review/analyze |
| A105 | Communicate (in firm) |
| A106 | Communicate (with client) |
| A107 | Communicate (other outside counsel) |
| A108 | Communicate (other external) |
| A109 | Appear for/attend |
| A110 | Manage data/files |
| A111 | Other |

**LOC Revised Activity Codes (2013), current LOC list.** Source: PDF https://ledes.org/wp-content/uploads/2016/04/2013-LOC-Revised-UTBMS-Activity-Codes.pdf (linked from https://ledes.org/loc-revised-activity-codes-2013/; the PDF is permission-protected for editing but readable, I extracted text with pdftotext; page 2 had a code/name misalignment in extraction, so A126-A128 and the A111 row are reconstructed from the page-2 text order).

| Code | Name (2013 revision) | Usage note (abridged from PDF) |
|---|---|---|
| A101 | Plan and prepare for | Includes budgeting and case assessment if the client allows |
| A102 | Research | Legal or factual research |
| A103 | Draft/Revise | |
| A104 | Review/Analyze | |
| A105 | Communicate (within legal team) | Internal communications within firm or legal team |
| A106 | Communicate (with client) | |
| A107 | Communicate (opponents/other outside counsel) | |
| A108 | Communicate (other external) | Parties not covered by A113/A114 |
| A109 | Appear For/Attend | Scheduled event related to the matter |
| A110 | Manage Data/Files/Documentation | Matter closing and post-closing only; excludes discovery/eDiscovery tasks and pre-closing clerical work |
| A111 | Other | |
| A112 | Billable Travel Time | When no other billable services are performed; includes waiting time |
| A113 | Communicate (witnesses) | |
| A114 | Communicate (experts) | |
| A115 | Medical Record and Medical Bill Management | In-house only |
| A116 | Training | Hourly training services, typically eDiscovery |
| A117 | Special Handling Copying/Scanning/Imaging (Internal) | Manual handling |
| A118 | Collection-Forensic | eDiscovery |
| A119 | Culling & Filtering | eDiscovery |
| A120 | Processing | eDiscovery (OCR, ingestion, conversion) |
| A121 | Review and Analysis | eDiscovery |
| A122 | Quality Assurance and Control | eDiscovery |
| A123 | Search Creation and Execution | eDiscovery |
| A124 | Privilege Review Culling and Log Creation | eDiscovery |
| A125 | Document Production Creation and Preparation | eDiscovery |
| A126 | Evidence/Exhibit Creation and Preparation | eDiscovery |
| A127 | Project Management | eDiscovery project management (excludes legal project management) |
| A128 | Collection Closing Activities | eDiscovery |

Inconsistency in the LOC source: the 2013 Expense PDF says local travel "excludes billable travel time (A115)" while out-of-town travel says (A112); the activity PDF defines A112 as Billable Travel Time and A115 as Medical Record management, so the local-travel reference is evidently a typo. Sources: both PDFs above.

### 3.5 Expense codes (E-codes and X-codes)

**Original ABA expense codes (1997), E101-E124.** Source: https://utbms.com/wp-content/uploads/2023/11/ABA-UTBMS-Expense-Codes-1997.xlsx (linked from https://utbms.com/aba-expense-codes/).

| Code | Name | Code | Name |
|---|---|---|---|
| E101 | Copying | E113 | Subpoena fees |
| E102 | Outside printing | E114 | Witness fees |
| E103 | Word processing | E115 | Deposition transcripts |
| E104 | Facsimile | E116 | Trial transcripts |
| E105 | Telephone | E117 | Trial exhibits |
| E106 | Online research | E118 | Litigation support vendors |
| E107 | Delivery services/messengers | E119 | Experts |
| E108 | Postage | E120 | Private investigators |
| E109 | Local travel | E121 | Arbitrators/mediators |
| E110 | Out-of-town travel | E122 | Local counsel |
| E111 | Meals | E123 | Other professionals |
| E112 | Court fees | E124 | Other |

**Important:** the LOC's 2013 revision renumbered expenses to an **X-series** (X101...), not E-codes. Source: https://ledes.org/wp-content/uploads/2015/07/2013-LOC-Revised-UTBMS-Expense-Codes.pdf (linked from https://ledes.org/loc-revised-expense-codes-2013/). Key entries (the full set is about 70 codes; read from the PDF):

| Range | Content |
|---|---|
| X101-X105 | Copies B&W / color (internal), copy service (external), special handling, word processing |
| X106-X110 | Facsimile, telephone local / long distance / mobile, conference/video/webinar |
| X111-X116 | Online legal research, delivery/messengers, postage, local travel, out-of-town travel, meals |
| X117-X124 | Court and agency fees, eviction, foreclosure, title insurance, immigration, late fees, publication costs, publications/books |
| X125-X143 | ATE premiums, witness fees, deposition and trial transcripts, trial exhibits, medical records costs/analysis/service fees, private investigators, arbitrators/mediators, local counsel, appraisers, experts/consultants, litigation support vendors, translation, special-purpose office rental, moving/storage, settlement costs, bank fees |
| X200-X207 | IP: drawings, patent and trademark records, searching/monitoring, prosecution application / post-issuance / opposition official fees, IP annuity payments, IP holdbacks |
| X300-X308 | eDiscovery expenses (forensic and third-party collection, culling, Bates stamping, review, privilege, production, exhibit prep, electronic media, technical) |
| X400-X405 | Technical: software licence/user access, subscription, transaction fee, hardware, hosting, data storage |
| X900-X998, X999 | Reserved for individualization by vendors or clients; catch-all; Other |

Usage notes in the PDF that drive rules: expense codes carry exclusions (e.g. X113 postage excludes overnight delivery X112; X111 online research can be a client-disallowed overhead); X107/X108 VOIP should be one line, not per-call; X114/X115 "if client requires a more granular breakdown, law firm should submit separate itemized expense line items." Same PDF.

Sources note: sources such as SimpleLegal's guide describe activity codes "A101-A106" and expense codes "E101-E124" as the generic set (https://www.simplelegal.com/?p=5435), consistent with the 1997 originals. One site, fluentcase.com, gives different meanings for several codes and should not be used (conflicts with the LOC lists above): https://fluentcase.com/knowledge-base/billing/billing-codes .

### 3.6 Other sets: structure and pointers

| Set | Top-level structure | Source |
|---|---|---|
| Counseling (C) | 4 phases only: C100 Fact Gathering, C200 Researching Law, C300 Analysis and Advice, C400 Third Party Communication | https://utbms.com/wp-content/uploads/2023/11/ABA-UTBMS-Counseling-Codes-1997.xlsx |
| Project (P) | P100 Project Administration; P200 Fact Gathering/Due Diligence (P210 Corporate Review, P220 Tax, P230 Environmental, P240 Real and Personal Property, P250 Employee/Labor, P260 Intellectual Property, P270 Regulatory Reviews, P280 Other); P300 Structure/Strategy/Analysis; P400 Initial Document Preparation/Filing; P500 Negotiation/Revision/Responses; P600 Completion/Closing; P700 Post-Completion/Post-Closing; P800 Maintenance and Renewal. Intended for "non-litigation matters of a significant size" | https://utbms.com/wp-content/uploads/2023/11/ABA-UTBMS-Project-Codes-1997.xlsx ; LOC revised version PDFs at https://utbms.com/loc-revised-project-codes-2/ |
| Bankruptcy (B) | B100 Administration (B110-B195, includes B195 Non-Working Travel), B200 Operations (B210-B260), B300 Claims and Plans (B310, B320), B400 Bankruptcy-Related Advice (B410, B420) | https://utbms.com/wp-content/uploads/2023/11/ABA-UTBMS-Bankruptcy-Codes-1997.xlsx |
| Patent / Trademark | LOC sets (2009) as PDFs; patent prosecution replaced by the 2023 IP Patent Prosecution workbook | https://utbms.com/wp-content/uploads/2023/07/UTBMS-Patent-Billing-Codes.pdf ; https://utbms.com/wp-content/uploads/2023/08/UTBMS-Trademark-Billing-Codes.pdf ; https://utbms.com/wp-content/uploads/2023/04/LOC-IP-Patent-Prosecution-Codes-4-2023.xlsx |
| Workers' compensation | 5 phases, 27 tasks (2010); no hosted file, ABA link only | https://utbms.com/aba-workers-compensation-codes/ |
| eDiscovery | PDF ratified 2011-07-13 | https://utbms.com/wp-content/uploads/2023/07/eDiscovery-UTBMS-Code-Ratified-7-13-2011.pdf |
| Criminal Law, Real Estate, M&A, GRC | Pages exist; file links not extracted | pages listed in 3.2 |

I did not find a separate "insurance" UTBMS set beyond workers' compensation in the utbms.com index. Source for absence: [U-HOME].

---

## 4. Outside Counsel Guideline (OCG) rule categories enforced by e-billing systems

Sources: three public OCGs read from the PDFs/pages — TC Energy (TCE) https://www.tcenergy.com/siteassets/pdfs/vendors/legal/tce-billing-and-staffing-guidelines.pdf ; Georgia Tech (GT) https://generalcounsel.gatech.edu/sites/default/files/2024-01/GT%20OGC%20Outside%20Counsel%20Guidelines%20(Final%20February%202023).pdf ; Yahoo https://legal.yahoo.com/xw/en/yahoo/terms/vendor/outsidecounsel/index.html ; plus the SimpleLegal best-practices guide https://info.simplelegal.com/hubfs/Content/Billing%20Guidelines/Legal%20Billing%20Guideline%20Best%20Practices.pdf and vendor feature descriptions at https://brightflag.com/legal-e-billing/ and https://www.layer3labs.io/guides/outside-counsel-billing-guidelines (vendor). Cook County's guideline (https://fpdcc.com/downloads/policies/FPCC-Outside-Counsel-Guidelines-111623.pdf) is cited only through a search-result summary because the PDF would not download.

"Check type" column: **D** = deterministic rule, **H** = heuristic (text pattern/threshold, tunable false positives), **AI** = judgement that is better suited to a language model plus human review. The classification is **(inference)** unless a vendor source says otherwise.

| # | Category | How an OCG usually expresses it (with real examples) | How it is checked | Check type |
|---|---|---|---|---|
| 1 | Block billing | "Block billing is not acceptable... each task requires a separate time entry" (TCE 6.7); "will not be accepted, and the Institute will not pay for any time recorded in a block fashion" (GT C.3); "Block billing is not permitted" (Yahoo). Sanction often a 50% cut or rejection (Layer3, vendor) | Heuristic: entries over a threshold (a practitioner tip: >2 h) containing conjunctions, semicolons, multiple verbs (https://www.counselaudit.com/guidelines/block-billing-prohibition.html search summary); AI to confirm | H then AI |
| 2 | Vague / insufficient descriptions | "Vague, general and generic activity descriptions are not appropriate" (Cook County, via search summary); calls and meetings "must specifically describe the parties involved and the subject matter" (TCE 6.8); disbursements "miscellaneous" or grouped may be refused (GT 4.i). A sample rule rejects descriptions under a minimum word count, e.g. six words (https://www.alternativeinsights.co.uk/wp-content/uploads/2020/04/BusyLamp_Getting_Started_Billing_Guidelines.pdf search summary, example only) | Word count + stop-phrase list is deterministic; "is this specific enough" is judgement | D (length) + AI |
| 3 | Minimum time increment | Tenths: "increments of tenths of an hour (0.1 billing hour = 6 minutes)" (TCE 6.7; Yahoo; GT C.3 "nearest one-tenth"); quarter-hour in a SimpleLegal sample text | Units modulo increment; also prohibits "unit billing or minimum charges" (TCE 6.4d) | D |
| 4 | Maximum daily hours per timekeeper | "Will not pay for more than ten (10) hours of time by a single timekeeper in a single day unless otherwise approved in advance" (GT VI.B). Eight hours cited as common in practitioner material (search summary of https://lawrepository.ualr.edu/cgi/viewcontent.cgi?article=1973&context=lawreview, not verified) | Sum units by timekeeper and date across the invoice (and across invoices) | D |
| 5 | Staffing limits / unapproved timekeepers | "Lead inside counsel must approve all timekeepers before they may bill" (Yahoo); no more than one attorney at trial/hearing/meeting/deposition (GT VI.B); avoid multiple representatives at meetings/calls (TCE 4.4); no ramp-up cost for staffing changes (TCE 4.3, GT, Yahoo); guidelines may bar first-year associates, summer associates, law clerks (SimpleLegal / search summary) | Timekeeper authorized list, classification allow-list, attendee-count per event/day; "duplicative attendance" is judgement | D (lists) + AI (duplication) |
| 6 | Rate above rate card / unapproved increase | "Billings at a rate that exceeds the approved rate... will be reduced to the approved rate" and increases need prior written approval, effective from the date of approval (GT VI.B); rates fixed two years or life of matter, later increases capped at CPI (Yahoo); "Rate increases are not allowed unless approved in writing prior to time being billed" (SimpleLegal sample) | Compare line rate to approved rate for (timekeeper, matter, date). Systems "auto adjust to the approved rate, auto adjust to zero, or reject the invoice" (SimpleLegal p.22) | D |
| 7 | Administrative / clerical tasks | Overhead "regardless of the personnel performing the task": scheduling, data entry, filing, docketing, conflict checks, invoice preparation (GT App. A); also no charge for conflicts, billing statements, invoice inquiries (TCE 6.4a) | Keyword/phrase matching plus classification of staff (CLKSEC, LGLAST) is heuristic; borderline cases need AI | H + AI |
| 8 | Internal conferences | Not paid unless approved (GT VI.B); team meetings the lead inside counsel isn't invited to are not payable (Yahoo); "scrutinize and reduce" (TCE 6.3a) | Entries coded to A105 (Communicate within team) or text patterns "internal conference/meeting"; multiple timekeepers same day/same narrative | H |
| 9 | Travel time | Not paid unless work done on the client's matter during travel (Yahoo; GT; TCE 6.4b/7.5); must be a separate time entry (GT); billable-travel activity code A112 exists (section 3.4); bankruptcy B195 "Non-Working Travel" | A112/B195 code or text match; hours cap; reduced-rate rule | D (code) + H (text) |
| 10 | Non-reimbursable expenses | Overhead list: word processing, rent, conference rooms, supplies, library/online research, proofreaders, support staff, photocopying time (TCE 7.3); overhead such as rent, support staff, photocopying (Yahoo); online research fees not reimbursable (GT 3, Legal Research) | Expense code deny-list per client (e.g. X105, X111, X101-X102 in LOC 2013 numbering); text match for uncoded | D (coded) + H |
| 11 | Expense caps and pre-approval | Written pre-approval for any expense over $1,000 (Yahoo); copies no more than $0.10 per page (TCE 7.4); meals $100/day (Yahoo), $75/person/day, hotel $300/night (SimpleLegal sample text); coach air, mid-size cars (Yahoo, TCE 7.5, SimpleLegal); no markup (GT, Yahoo) | Per-expense-code amount and unit-price thresholds; per-day sums; approval record lookup | D |
| 12 | Late submission / stale charges | Invoices monthly within 2 weeks of month end (TCE 6.5); within 30 days of period end and no payment for services over 90 days old (GT C.2); within 60 days of month end and no payment for work over six months old (Yahoo); SimpleLegal: systems enforce max invoice age and max line-item age | Invoice date vs period end; line date vs submission date | D |
| 13 | Missing / invalid UTBMS codes | LEDES with UTBMS task, activity and expense codes required "at Yahoo's discretion"; invoices without valid codes rejected (search summary of Layer3); SimpleLegal: "If the line item is missing a task code, or the charges... exceed the task code cap, the invoice can be flagged, auto-adjusted, or rejected" | Code lookup in the client's allowed set; code-vs-phase compatibility; activity without task is invalid per [L-98BI-SPEC] | D |
| 14 | Duplicate entries | "Duplicate entries" listed as automated catch (Brightflag, vendor); "Duplicated efforts" in SimpleLegal sample non-billable list | Same timekeeper+date+units+description hash within and across invoices; "duplicated effort" between timekeepers is judgement | D (exact) + AI (semantic) |
| 15 | Billing outside matter dates / budget | Charges must be "consistent with budgets approved in advance" and budget adjustments need a written request (Yahoo); time over the Approved Billing Limit annually and over life of matter needs prior written approval (TCE 6.1); invoices must reference the correct matter number or be rejected (TCE 6.6); matter closed in e-billing system cannot be invoiced further (TCE 6.5) | Matter open/close dates, budget remaining by phase/timekeeper/year; accrual reporting | D |
| 16 | Excess time for task / task code cap | "Time billed may be reduced... if excessive time was spent" at the client's sole discretion (GT); task-code caps and patent fixed-schedule pricing (SimpleLegal p.22) | Per-task-code cap = deterministic; "excessive" without a cap is judgement | D (cap) + AI |
| 17 | Legal research on basic principles | "Time should not be charged for [basic] research" (GT App. A.3); TCE 6.3b | Narrative classification | AI |
| 18 | AI-use disclosure | Some clients require disclosure of AI use and pass-through of time saved (Layer3, vendor) | Metadata/attestation field | D |

Where systems apply sanctions: Legal Tracker/SimpleLegal-class systems let each rule action be flag, auto-adjust (to approved rate or to zero) or reject (SimpleLegal p.22); clean, low-value invoices can be auto-approved; flagged items go to human review (Layer3, vendor). Brightflag says AI flags guideline violations, rate discrepancies and duplicates before a human opens the invoice (vendor). **(inference)** The engine should store, per rule, an action (`flag` | `adjust` | `reject`), a reason code, a client-defined parameter set, and the guideline clause reference, because rejection reasons are fed back to the firm.

---

## 5. Rate cards

### 5.1 Common structures (with sources)

| Dimension | What exists in practice | Source |
|---|---|---|
| By individual timekeeper | Core structure in rate review in Legal Tracker; LawVu: "Each timekeeper has only one active rate" per template | https://www.thomsonreuters.com/en-us/help/legal-tracker/company/timekeeper-rates/timekeeper-rate-review-overview ; https://help.lawvu.com/en/articles/12636832-timekeeper-rates-for-billing-guidelines |
| By classification / role | Rates keyed to classification rather than names avoids a card that "references 200 individual timekeepers and becomes unmanageable"; Brightflag requires Role, Location and Practice Area dropdowns plus Years of Experience (post-qualification) on submission | https://swiftwaterco.com/insights/ebilling-panel-configuration/ (search-result summary) ; https://vendorhelp.brightflag.com/en/articles/787885-how-to-submit-a-timekeeper-matter-rate |
| By matter | Brightflag matter-specific rates: one row per timekeeper per `Client Matter ID` | same Brightflag page |
| By practice area / location | Practice area and location are required fields in Brightflag template; Legal Tracker compares to peers in the same metro area, classification and matter type | same; Legal Tracker rate review page above |
| Effective dates | Each rate row (Brightflag) or rate sheet (Legal Tracker) carries an effective date; LawVu has effective and end dates and flags a discrepancy if they do not cover the billing period | pages above |
| Rack rate vs proposed rate | Brightflag distinguishes `Rack Rate` (standard published, undiscounted) from `Proposed Rate` (sent for approval); LEDES `LINE_ITEM_UNIT_COST` is the undiscounted rate and discounts are separate | Brightflag page; Mitratech 98B spec; [L-XSPEC] (`FID`, `FIDHR`) |
| Discounts | Not in the LawVu template (source systems may hold them); in LEDES XML represented as item-level or matter-level discount/credit segments; "Missing volume discounts" is a flagged category at Brightflag (vendor) | LawVu page; [L-XSPEC]; https://brightflag.com/legal-e-billing/ |
| Alternative fee arrangements | LEDES 98B: `IF`/`IE` rows and the `FLTFEE` and `ADJSMT` classifications for flat fee / adjustment rows (when specified by client); XML: `matter_billing_type` codes `CT`, `FF`, `GW`, `MPF`, `TM`, `charge_type` `F` fixed charge, `FIFF` fixed fee item; TCE names fixed fees, incentive billing, capped fees, phasedbilling, caps and collars (6.2); GT lists fixed fee, reduced hourly with incentive bonus, value billing, blended rates | [L-TK]; [L-XSPEC]; TCE OCG; GT OCG |
| Currency | XML 2.0.4/2.1.4/2.2.1+ allow submitting rates in the timekeeper's home currency so firms need approval only in home currency | [L-X20], [L-X21], [L-X22] |

**(inference)** A workable model: `RateCard (client, scope: firm|matter, effective_from, effective_to, currency)` with `RateCardLine (classification or timekeeper, practice_area?, location?, rack_rate, approved_rate, discount_pct?, years_of_experience_band?)` and an explicit lookup precedence (matter + timekeeper > matter + classification > firm + timekeeper > firm + classification). Not sourced; confirm with Spaarke's customers.

### 5.2 How rate-increase requests are handled

| Practice | Detail | Source |
|---|---|---|
| Prior written approval | Increases not submitted unless approved in writing beforehand; effective only for work on or after the approval date | GT OCG VI.B; SimpleLegal sample text |
| Rate freeze | Fixed for two years or life of matter, whichever longer; afterwards capped at the percent increase in US CPI, at client discretion | Yahoo OCG |
| Annual window | Practitioner advice: a single annual review window; anything outside billed at the prior rate (vendor advice, not a published rule) | https://www.vaquill.ai/blog/outside-counsel-guidelines-template-12-rules (vendor, search summary) |
| Formal workflow | Firm submits a rate sheet with effective date; the system compares to the timekeeper's history and to peers; reviewer accepts, edits (effective date or rate) or rejects with a comment shared with the firm. Statuses: Pending, Accepted, Modified, Rejected, Closed. Rate sheets uploaded by the company are auto-approved. If the reviewer changes the effective date, the firm is not notified | Legal Tracker pages above and https://www.thomsonreuters.com/en-us/help/legal-tracker/law-firm/timekeeper-rates/timekeeper-rate-sheets-overview |
| Rate first appearing on an invoice | Configurable: "rate reviewer only" (invoice held out of pending until rate decided) or "rate reviewer or invoice approver" | Legal Tracker rate review page |
| Bulk submit with reason | Brightflag template includes optional `Reason for Request` (promotion, annual raise); client must approve before the rate can appear on invoices | Brightflag page above |
| Warn on higher-than-previous | eBillingHub keeps a library of timekeeper rates and shows a warning if an invoice rate is higher than previously billed | https://legal.ebillinghub.com/site_ver2/help/Timekeeper_Rates.htm (search-result summary) |
| Invoice-time correction | LawVu adjusts the invoice by the difference when billed rate mismatches the approved rate; an optional "prior rate" field supports change reporting | LawVu page |

---

## 6. PDF invoices from law firms (no LEDES)

### 6.1 How common

- Brightflag reports 48% of invoices for US-headquartered customers and 87% for non-US-headquartered customers arrive as PDFs (vendor data). Source: https://brightflag.com/resources/what-are-ledes-files-an-introduction/ .
- Reasons given: small/boutique and non-US firms cannot produce LEDES; where taxes apply to legal services firms typically need to submit PDFs to comply with tax regulations (98B has no tax fields); finance teams want PDFs for audit; LEDES does not force UTBMS codes. Same source (vendor).
- Brightflag says most legal e-billing systems cannot read PDFs and recommends platforms that extract structured data from PDFs, noting AI classification maps line items to task/activity codes because attorneys interpret UTBMS codes inconsistently (vendor claims). Sources: same page and https://brightflag.com/legal-e-billing/ .

### 6.2 What needs to be extracted

Matter number, billing period, and every time and expense entry: timekeeper, hours, rate and UTBMS task, activity and expense codes (from a vendor search summary of parser requirements: https://www.docsumo.com/blog/invoice-data-extraction ); this maps to the 98B/XML field lists in section 1.

### 6.3 Layout variation and extraction difficulty

Legal-specific literature is thin; the following is from general invoice-extraction research, applied to legal by **(inference)**.

| Difficulty | Source (general invoices) | Legal-specific implication (inference) |
|---|---|---|
| Layouts "vary radically across vendors and regions" | https://arxiv.org/pdf/2507.07029 | Firms use their own billing system templates (one per firm); time-detail may be a separate attachment from the cover bill |
| Multi-page and nested tables, tables that change width, no gridlines | https://arxiv.org/pdf/2507.07029 ; https://www.llamaindex.ai/insights/best-ocr-for-invoices | Time detail tables run for dozens of pages; narrative descriptions wrap onto multiple lines and can be mistaken for new rows |
| Template-based extraction needs one template per vendor | https://www.klippa.com/en/blog/information/invoice-data-extraction/ | Not viable across a panel of many firms |
| Accuracy drops on low-quality scans | https://arxiv.org/pdf/2511.05547 and the Klippa/LlamaIndex pages | Scanned or emailed-photo invoices from small firms |
| Line-item extraction is often skipped or manually validated | https://image-ppubs.uspto.gov/dirsearch-public/print/downloadPdf/8526739 (patent text, search summary) | Skipping lines defeats OCG line-level rules |
| Merged cells, irregular line items need post-processing to normalize across suppliers | https://www.llamaindex.ai/insights/best-ocr-for-invoices | Need normalization of timekeeper names, classification, rate, date formats |
| LLMs as reviewers: one study (Onit-affiliated, 50 mostly synthetic invoices) found up to 92% approval-decision accuracy versus a 72% ceiling for experienced lawyers; measures approval decisions, not field extraction | https://arxiv.org/pdf/2504.02881 | Supports AI for narrative rules but not as evidence for extraction accuracy |

Features likely to be hard for legal invoices **(inference, not sourced)**: partially-discounted or written-down lines where the hours x rate does not equal the amount shown; separate "adjustments", "courtesy discounts" and retainer applications at the end; timekeeper listed by initials or short name in the detail but full name in a summary table; invoices in a different currency and with tax lines; fixed-fee or milestone invoices with no hourly lines; expense lines without codes; time-and-expense summary tables that must reconcile to the detail.

### 6.4 How vendors handle it

| Approach | Source |
|---|---|
| AI extraction of all invoice formats so "100% of legal spend" can be analyzed regardless of how it arrived; AI flags guideline violations, rate discrepancies, duplicate entries before a human opens the invoice (vendor) | https://brightflag.com/legal-e-billing/ |
| AI classification of line items to standardized task/activity codes when UTBMS codes are absent (vendor claim) | https://brightflag.com/resources/what-are-ledes-files-an-introduction/ |
| Confidence-threshold routing: high-confidence fields pass, uncertain fields go to a person; track straight-through rate, manual-review rate and false-auto-approval rate | search summary of https://arxiv.org/html/2510.15727v1 and https://github.com/EDDY05ee/FieldExtract-AI (general invoices) |
| Require LEDES instead (Yahoo and most OCGs), or accept PDFs only as a supporting copy | Yahoo OCG; Brightflag page (FAQ says requiring LEDES "narrows your panel") |
| I found **no** documentation of CounselLink, Legal Tracker, Onit or Wolters Kluwer ELM PDF intake in these searches; do not assume capabilities | search results above |

---

## 7. Gaps and verification still needed

1. The LEDES 1998B authoritative spec is a legacy `.xls` (https://ledes.org/wp-content/uploads/2014/08/Ledes-1998B-Format-Rev-7-2014.xls) that I could not parse here; the 24-field table is from Mitratech's transcription cross-checked against the 98BI V5 workbook. Re-verify lengths and the `LEDES1998B[]` header line against the LOC sample file linked on [L-98B].
2. The XML 2.0.x and 2.1.x field-level workbooks were not read (only 2.2.2). Download https://ledes.org/wp-content/uploads/2026/04/LEDES_XML_E-Billing_2-1-5-4-2026-3.xlsx and the 2.0.4 workbook if Spaarke needs to ingest them.
3. LOC 2013 expense PDF: I did not transcribe every X-code with its name; the table above groups them. Pull the full list from the PDF when seeding reference data.
4. Cook County OCG, UBS billing guidelines (https://www.ubs.com/global/en/our-firm/suppliers/contracting-standards/contracting-standards-archive/) and the Wolters Kluwer / ACC pieces appeared in search but were not read in full.
5. Legal Tracker, Brightflag and eBillingHub rate-workflow statements are from help-center pages and could change; the LawVu page documents one vendor's template limits, not an industry norm.
6. No source found for LEDES JSON invoice standards or for the LEDES API data format, endpoints or auth model; read the "LEDES Software API v1.0" PDF linked from https://ledes.org/ledes-api-v1-2020/ before designing an API-based intake.
