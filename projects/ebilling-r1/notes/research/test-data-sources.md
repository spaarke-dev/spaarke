# eBilling test and evaluation data sources

Researched 2026-10-14 (web search and fetch). Nothing was downloaded as a dataset. A few single PDFs were opened to confirm what they contain.

**Scope.** Test and evaluation material for the Spaarke legal eBilling module. The module extracts header and line items from law-firm invoices (mostly PDF), assigns UTBMS task/activity/expense codes, turns Outside Counsel Guidelines (OCGs) and rate cards into rules, and checks invoices against those rules.

## How to read this

- **Verification column.** `Fetched` means I opened the URL and saw the content. `Search-only` means a search result listed it but I could not open it: the host blocked the request, returned a 403, or the PDF could not be read. `Search-only` rows need a manual check before anyone relies on them.
- **Usefulness** is scored 1 to 5 for the eBilling use case, not for general document AI.
- **Licence** is what the source states. Where it states nothing, I say so. I am not giving legal advice. For anything marked "public record, no licence", confirm with counsel before redistributing copies outside internal evaluation.
- **PII** distinguishes personal data (private individuals) from professional names (timekeepers named in court filings).

### Headline findings

1. No public corpus of real law-firm invoices with UTBMS-coded lines and ground truth exists. I found none, and no public benchmark for UTBMS code prediction or block-billing detection. The closest published work, "Better Bill GPT" (arXiv 2504.02881), does not state where its data came from or release it. Vendor patents describe the training data as proprietary.
2. The best real material is **Chapter 11 fee applications and monthly fee statements**. Filed documents are public, carry timekeeper, date, narrative, hours, rate and task-code detail, and the text layer of most is machine-readable. Caveat: they use the U.S. Trustee project categories (B110, B130, and so on) and, in some firms, the C- and L-series, not the A-series activity codes. They are fee applications, not invoices (no invoice number, remit-to or tax block), so they test line-item and coding accuracy, not header extraction.
3. **Header and layout extraction** needs synthetic invoices rendered as PDFs with known ground truth, plus a small set of real or partner-supplied invoices.
4. **OCG and rate-rule drafting** is well served. Many public bodies publish OCGs with explicit rules (see Category 3).
5. UTBMS code sets are free to download from utbms.com. The terms permit download but do not clearly grant redistribution.

---

## 1. Real legal invoices, time entries and fee records

### 1a. Chapter 11 fee applications (verified examples)

All are PACER-derived filings hosted by the claims agent. The claims agents' sites are free to view. None states a licence. These are court records, in effect public domain as to access, but I found no explicit reuse grant.

| Source | URL | What it contains | Size | Format | Licence/terms | PII | Use | Verification |
|---|---|---|---|---|---|---|---|---|
| K&L Gates LLP first interim fee application, In re Akoustis Technologies (Bankr. D. Del. 24-12796, Doc 518, filed 2025-04-30), Stretto | https://cases.stretto.com/public/x368/13098/PLEADINGS/1309804302580000000256.pdf | Summary by project category with hours and fees (B110, B120, B130, B160, B185, B190, B195, B210 to B410, C100 to C400, L110 and up), timekeeper table, expense summary, itemised entries | about 35 pages, 3.3 MB, about 1,200 lines of text | PDF with text layer. The PDF metadata title says "FTX - LRC 2nd Interim Fee Application", which is a template leftover; the content is K&L Gates | None stated. Public court record | Professional names and billing rates only. Narratives may name debtor employees. Redacted under Fed. R. Bankr. P. 9037 | 5 | Fetched |
| Foley Hoag LLP second monthly application, In re Pear Therapeutics (Bankr. D. Del. 23-10429, Doc 314, filed 2023-07-14), Stretto | https://cases.stretto.com/public/X237/12136/PLEADINGS/1213607142380000000188.pdf | Cover sheet, project-category summary (B-codes), expense summary with expense categories, itemised time. $578,155.50 fees for May 2023. Includes the B195 non-working travel category | 20 pages in the filing, about 3,700 text lines (the PDF itself is about 60 pages; 1 MB) | PDF with text layer | None stated | As above | 5 | Fetched |
| Randi S. Ellis (Future Claimants' Representative) second monthly fee statement, In re Red River Talc (Bankr. S.D. Tex. 24-90505, Doc 1042, filed 2025-02-05), Epiq | https://document.epiq11.com/document/getdocumentsbydocket/?docketId=1140755&projectCode=RRI&docketNumber=1042 | Short statement (4 pages), category summary and professional hours. Good for a single-timekeeper, high-rate case | 4 pages | PDF | None stated | As above | 3 | Fetched |
| Young Conaway supplement to second interim fee request, In re Lugano Diamonds & Jewelry (Bankr. D. Del. 25-12055, Doc 681, filed 2026-07-15), Omni | https://casedocs.omniagentsolutions.com/cmsvol2/pub_47571/6868038c-8c01-43d4-8650-ef1785e91dd6_681.pdf | The UST Appendix B schedules A to E: timekeeper rate table, comparison with budget, rate changes. No itemised narratives | 11 pages | PDF | None stated | As above | 3 (good for rate-card and budget checks, not for line coding) | Fetched |
| Covington & Burling final fee application, Montreal Maine & Atlantic Railway (Bankr. D. Me.), hosted by monitor Richter | https://www.richter.ca/wp-content/uploads/Insolvency-Cases/en/M/Montreal-Maine-and-Atlantic-Canada-Co/Chapter-11---Montreal-Maine-and-Atlantic-Railway-Ltd/Motions/429-DE1931-Covington-final-fee-appl-20160120.pdf | Final application, 2016. Scanned CCITT page images, no text layer | unknown | Scanned PDF | None stated | As above | 3 as an OCR-stress case; I could not read its content | Fetched (container only; content unread) |
| Other filings the search surfaced (not opened): Pillsbury monthly fee statements (Veritas); Polsinelli monthly fee statement (Stretto); Epiq committee application, 2026 | https://www.veritaglobal.net/ample/document/2590817260331000000000010 and https://www.veritaglobal.net/ample/document/2590817260810000000000001; https://cases.stretto.com/public/x243/12204/PLEADINGS/1220407182480000000283.pdf; https://document.epiq11.com/document/getdocumentsbydocket/?docketId=1253089&projectCode=SHQ&docketNumber=468 | Reported by search as B110/B130/B160 time detail with timekeeper, date, narrative, hours, amount | unknown | PDF | None stated | As above | 4 | Search-only (Veritas host failed certificate verification) |

**How to find more.** Use the claims agents' public case sites (Kroll `restructuring.ra.kroll.com`, Stretto `cases.stretto.com`, Omni `omniagentsolutions.com`, Epiq `dm.epiq11.com`, Veritas `veritaglobal.net`). Search each docket for "Monthly Fee Statement", "Interim Fee Application" or "Fee Examiner". Fee examiner reports (for example in very large cases) also quote line items flagged as block-billed or vague. That gives labelled bad-line examples for rule checks.

**Reference forms.** The U.S. Trustee Appendix B guidelines and exhibit forms (justice.gov/ust) describe the standard exhibit set: Exhibit B timekeeper summary, Exhibit C budget and staffing plan. Southern District of Florida form LF-89 shows the same layout. Useful for building a layout taxonomy. Search-only for the Exhibit C PDF (it could not be read when fetched).

### 1b. Class-action and fee-petition billing records

| Source | URL | What it contains | Licence/terms | PII | Use | Verification |
|---|---|---|---|---|---|---|
| Payment Card Interchange settlement fee declarations (e.g. Bernay declaration, Dkt 7471-4) | https://www.PaymentCardSettlement.com/Content/Documents/New Docs/Dkt No. 7471-4_Exhibit 3_Declaration of Alexandra S. Bernay.pdf | Firm-by-firm lodestar charts, hours by timekeeper; some declarations have detailed entries | None stated; court record | Professional names | 2 (lodestar summaries, rarely line-level) | Search-only (HTTP 403 on fetch) |
| Qvar antitrust settlement fee declarations (2026) | https://qvarantitrustsettlement.com/media/q2zonqqz/2026-06-05-ecf-210-5-exhibit-e-declaration-of-christopher-m-burke-on-behalf-of-burke-llp-in.pdf | Hours and rates by timekeeper | None stated | Professional names | 2 | Search-only |
| CourtListener / RECAP (Free Law Project) | https://www.courtlistener.com/help/api/bulk-data/ | Archive of PACER documents and dockets, including bankruptcy. Bulk data carries a Public Domain Mark | Public Domain Mark on the bulk-data page. Coverage of any given fee application is not guaranteed | Court records | 3 as a discovery tool for fee-petition exhibits | Fetched (page shows the mark; coverage of fee exhibits unconfirmed) |

Fee petitions in civil-rights and fee-shifting cases (attorney declarations with attached billing records) are the other main real source of narratives with dates and hours. Find them by searching PACER or RECAP for "billing records" or "contemporaneous time records". Most are not UTBMS-coded.

### 1c. Public-records releases by government bodies

Legal bills are public records in some states and partly withheld in others. Ohio treats narratives as privileged, Texas lets districts redact narrow privileged portions, and California treats invoices as presumptively public (per search results summarising First Amendment Coalition and attorney general rulings).

| Source | URL | What it contains | Licence/terms | PII | Use | Verification |
|---|---|---|---|---|---|---|
| MuckRock request: Kent School District legal invoices and payments, 2020 to 2024 | https://www.muckrock.com/foi/kent-67/kent-school-district-disclosure-of-all-legal-invoices-and-payments-to-attorneys-andor-law-firms-2020-2024-162639/ | A public-records request. Whether invoices were released and in what form is unknown | MuckRock users post received documents; no blanket licence | Likely redacted narratives | 3 (a lead for real law-firm invoice layouts) | Search-only (HTTP 403) |
| Bricker Graydon, "Are legal bills public records?" and First Amendment Coalition note | https://brickergraydon.com/insights/publications/Are-legal-bills-public-records and https://firstamendmentcoalition.org/asked-and-answered/are-school-districts-attorney-fees-public-records | Legal analysis of which parts are disclosable. Not data | n/a | n/a | Useful to know which fields to expect redacted | Search-only |

**Recommendation.** File public-records requests for outside-counsel invoices from a few agencies, asking for the native PDFs and any LEDES files. Targets: a state attorney general's office, a transit authority, a county, a school board and a public university. Ask for 12 months of invoices for one law firm. This is the only route to realistic non-bankruptcy PDF invoices. Expect redacted narratives (blackouts are themselves a useful test case).

### 1d. LEDES sample files

| Source | URL | What it contains | Size | Format | Licence/terms | PII | Use | Verification |
|---|---|---|---|---|---|---|---|---|
| LEDES 1998B format spec and sample file, Rev 7-2014 (ledes.org) | https://ledes.org/ledes-98b-format/ ; spec https://ledes.org/wp-content/uploads/2014/08/Ledes-1998B-Format-Rev-7-2014.xls ; sample https://ledes.org/wp-content/uploads/2017/11/Ledes98B-Sample-File-Rev-7-2014.txt | 24-field pipe-delimited spec and a sample invoice | small | XLS and TXT | No download terms stated on the page; the site terms of use say "You may download materials displayed on this website"; LEDES Oversight Committee keeps IP rights; "LEDES" service mark rules | Synthetic | 5 for the output schema; 2 for realism | Fetched |
| LEDES 98BI, LEDES 2000, XML 2.x, XML budgeting and timekeeper-attribute formats | https://ledes.org/ledes-98bi-format/ , https://ledes.org/ledes-2000-format/ , https://ledes.org/ledes-xml-budgeting-format/ and others listed on https://ledes.org | Specs for international (tax, 51 or 52 fields), XML and budgeting formats | small | XLS, XML schemas | As above | none | 4 | Fetched (index page); sub-pages not opened individually except 98B |
| Onit Counsel Exchange example LEDES files | https://onit.screenstepslive.com/a/2068367-example-ledes-files-for-counsel-exchange | Fixed-fee invoice, a 142 KB 98B file, a 98BI sample, an XML sample | 4 files | TXT, XML | **Proprietary to Onit. No sharing or reproduction.** Do not use | Synthetic | 1 (excluded) | Fetched |
| Thomson Reuters Legal Tracker LEDES help | https://www.thomsonreuters.com/en-us/help/legal-tracker/company/file-specifications/ledes-file-formats/ledes-file-formats | Field notes and validation behaviour for 1998B, 98BI, XML 2.1 and 2.2 | n/a | Web page | Vendor documentation; read only | none | 3 (reference for validation rules) | Fetched |
| Brightflag accepted LEDES files and sample links | https://vendorhelp.brightflag.com/hc/en-us/articles/20537313125277-Which-LEDES-Files-Are-Accepted-by-Brightflag | Links to "LEDES98BI V2 Sample File 1-2020" and "Ledes1998B Sample File Rev 7-2014" | small | TXT | Vendor help; check before reuse | Synthetic | 2 | Search-only |

LEDES files are the best source of structured **ground truth**: render PDFs from LEDES-shaped data, so the expected extraction output is the LEDES record.

---

## 2. Datasets and benchmarks for invoice and document extraction

No source below is legal-specific. Treat them as generic document-AI baselines and as layout-variety fodder, not as a measure of legal-billing accuracy.

| Source | URL | What it contains | Size | Format | Licence/terms | PII | Use | Verification |
|---|---|---|---|---|---|---|---|---|
| DocILE (Rossum) | https://docile.rossum.ai/ ; code https://github.com/rossumai/docile | Business documents (invoices, order forms) annotated for key information localisation/extraction (KILE) and line item recognition (LIR), 55 field classes | 6,680 annotated, 100k synthetic, about 932k unlabelled | PDF, pre-computed OCR (DocTR), JSON annotations | Code is MIT. Data needs an access-request form with a secret token and is for research. Competition rules forbid external document datasets. Commercial use not granted | Real business documents; no anonymisation statement on the page. Links to a legal notice on research data processing | 4 for LIR method and metrics; 2 for legal content | Fetched |
| RVL-CDIP | https://huggingface.co/datasets/aharley/rvl_cdip | 400,000 grayscale page images in 16 classes including invoice (label 11) | 38.8 GB | PNG images, classification only | "other". Derived from IIT-CDIP Legacy Tobacco Documents Library; the card points to that library's copyright page | Real tobacco-litigation documents; the card says "More Information Needed" on personal data | 2 (only for invoice-versus-other routing) | Fetched |
| ConfBench (amazon) | https://huggingface.co/datasets/amazon/ConfBench | 75 FCC invoices (broadcast advertising, public records), each with up to 18 scan-noise variants and ground-truth JSON with line items | 1,346 samples, 32.7 GB | PDF, JSON | **CC BY-NC 4.0** (non-commercial) | Business names and amounts; no PII statement | 3 for noise-robustness and line-item scoring, with the licence limiting use | Fetched |
| CORD v2 (naver-clova-ix) | https://huggingface.co/datasets/naver-clova-ix/cord-v2 | Receipt images with parsed JSON: menu items, subtotal, tax, total | 1,000 rows (800/100/100), 2.3 GB | Parquet | CC BY 4.0 | Receipts; incidental personal data possible | 2 | Fetched |
| SROIE (ICDAR 2019) via Voxel51 | https://huggingface.co/datasets/Voxel51/scanned_receipts | Scanned receipts with company, address, date, total | about 712 samples, 421 MB | JPEG plus JSON | CC BY 4.0 on the mirror; original competition licence not stated in sources I saw. Original at rrc.cvc.uab.es | Card says personal data not intentionally included but may appear | 1 | Fetched |
| FUNSD | https://guillaumejaume.github.io/FUNSD/ | 199 noisy scanned forms with entities and relations | 199 forms | images, JSON | Licence page not read; "cite the paper" | Old forms, tobacco-era | 1 | Fetched |
| Kleister Charity | https://github.com/applicaai/kleister-charity | UK charity reports with address, income, spending; long multi-page PDFs with OCR from four engines | 1,729 train, 440 dev, 609 test; about 12 GB | PDF, OCR text | No licence file in the repo. Source data is the UK Charity Commission | Charity names and addresses | 2 for long-document extraction practice | Fetched |
| VRDU (Google) | https://github.com/google-research-datasets/vrdu | Registration forms and ad-buy forms (public DOJ and FCC filings), with multi-template few-shot splits | not stated | PDF, OCR, JSON | No licence stated; archive is read-only. Not invoices | Public filings; names | 2 for template-generalisation methodology | Fetched |
| katanaml-org/invoices-donut-data-v1 | https://huggingface.co/datasets/katanaml-org/invoices-donut-data-v1 | 500 invoice images with ground-truth JSON: header fields and line items | 501 rows, 198 MB | image plus JSON | MIT per card; cites a Mendeley Data collection "Samples of electronic invoices". Provenance unclear | Names, addresses, tax IDs, IBANs that look fabricated; verify | 3 for quick header and line-item baselines | Fetched |
| mychen76/invoices-and-receipts_ocr_v1 | https://huggingface.co/datasets/mychen76/invoices-and-receipts_ocr_v1 | Invoices and receipts with OCR text | 1k to 10k rows | Parquet | No licence found in the search output. Check the card | unknown | 2 | Search-only |
| Shubhal829/high-quality-invoice-images-for-ocr | https://huggingface.co/datasets/Shubhal829/high-quality-invoice-images-for-ocr | Synthetic invoice images with tabular layouts, seller/client, line items, tax | not stated | images | No licence found; source reported as Kaggle | Synthetic | 2 | Search-only |
| LegalBench | https://huggingface.co/datasets/nguha/legalbench ; https://hazyresearch.stanford.edu/legalbench/ | 162 legal-reasoning tasks from 40 contributors. **No billing, invoice or time-entry task** | 162 tasks | HF dataset | CC BY 4.0 overall; per-task licences differ | Mostly public text | 1 (not applicable; useful only as a general legal-language sanity check) | Fetched |
| Better Bill GPT (arXiv 2504.02881) | https://arxiv.org/abs/2504.02881 | Paper comparing LLMs with human legal-invoice reviewers (reported up to 92% LLM accuracy against a 72% reviewer ceiling on approval decisions) | invoice count not stated | paper | Data and code not stated as released | n/a | 3 as a methodology reference for scoring review decisions; no data | Fetched (abstract) |
| Patents US11049204 (block-billing detection), US20210383442 (narrative categorisation) | https://patents.google.com/patent/US11049204 ; https://patents.justia.com/patent/20210383442 | Descriptions of methods; training data proprietary | n/a | text | Patents. Read for the technique; **do not implement a claimed method without a freedom-to-operate check** | n/a | 2 | Search-only |

**Gap.** I found no public benchmark for UTBMS task/activity code prediction from narratives, none for block-billing detection, and none for OCG-to-rule translation. Spaarke will have to build and label its own sets. That is also a possible differentiator.

---

## 3. Published OCGs and rate cards

I opened the PDFs I could and grepped them for the rule keywords. Entries marked "rules seen" carry text I read.

### 3a. Outside Counsel Guidelines

| Issuer | URL | Rules seen (increments / block billing / travel / expenses / other) | Format | Licence/terms | Use | Verification |
|---|---|---|---|---|---|---|
| County of Berks, PA (Outside Counsel Guidelines, 2 May 2016) | https://berkspa.gov/getmedia/5f301be1-b62d-43fe-b439-6a3c869d321b/Outside_Counsel_Guidelines.pdf | One attorney at trial, court and meetings; no pay for clerical/secretarial work; no billing for training; use model documents; insurer guidelines take precedence. Contract between counsel and the county | PDF, text layer, about 5 pages | Government publication; no licence stated | 4 | Fetched; billing sections beyond staffing not fully read |
| New Mexico State University | https://legal.nmsu.edu/wp-content/uploads/sites/28/2021/11/NMSU-Outside-Counsel-Guidelines.pdf | Tenths of an hour; travel time at base rate for approved travel; routine copies capped at $0.15 colour; mileage at state rate; air coach fare; staffing with the minimum number of lawyers; alternative fee arrangements | PDF, about 2,200 words | Public university; no licence stated | 5 (explicit numbers and caps, good for rule extraction) | Fetched |
| City of Kansas City, KS / Wyandotte County, Exhibit B outside counsel guide (bid N33053) | https://purchasing.wycokck.org/eprocurement/bids/N33053/Exhibit%20B%20-%20Outside%20counsel%20guide.pdf | One-tenth hour increments, entries recorded daily; tasks of six minutes or less grouped; "block billing is not acceptable"; no minimum charge; budget required; bill per matter | PDF, about 3,300 words | Procurement document, public | 5 | Fetched |
| University of California standard core legal OCGs (UCOP) | https://ucop.edu/uc-legal/_files/standard-core-legal-outside-counsel-guidelines.pdf | Task- or activity-based itemisation; rates approved within ranges, no unilateral increases; each task separately identified when several are done in one day; narrative/block billing wording; travel costs identify traveller and reason; legal assistant time shown separately; late-invoice rules per search results (120 days) | PDF | Public university; no licence stated | 5 | Fetched; extracted via the saved PDF, wording partly read |
| Nevada DIDS (public defender) attorney fee billing guidelines, 4 Mar 2025 | https://dids.nv.gov/uploadedFiles/didsnvgov/content/Resources/Forms/DIDS%20Billing%20Guidelines%20-%20Attorney%20Fees%2003042025.pdf | 0.1-hour increments, off-increment time rounded down; travel claims need dates, routes and mileage; submitted through LegalServer | PDF, about 960 words | Public | 4 | Fetched |
| Zurich Australia, Financial Lines Litigation Management Guidelines for Defence Counsel | https://zurich.com.au/content/dam/au-documents/claims/financial-lines/financial-lines-litigation-management-guidelines.pdf | One-tenth hour increments recorded daily; actual time in tenths; block billing and vague entries named as improper; travel needs prior consultation; revising standard forms only for actual time | PDF, about 3,400 words | Corporate publication, no licence | 5 (insurer, explicit prohibited-entry list) | Fetched |
| Texas Attorney General rules on outside counsel contracts, 1 TAC ch. 57 (via UT System) | https://www.utsystem.edu/sites/default/files/offices/general-counsel/OutsideCounsel/OAGRulesreOutsideCounsel.pdf | Invoice must be itemised; invoice summary with hours per legal professional, fee and expense subtotals and running total across invoices under the contract; section 57.6 sets invoice content | PDF, 36 KB | Texas administrative rule, public | 4 | Fetched |
| Delaware Department of Justice outside counsel billing (bid JUD-DOJ25001) | https://bidcondocs.delaware.gov/JUD/JUD-DOJ25001-OUTSIDE_COUNSEL-billing.pdf | Per search: invoices in increments no less than 0.1 hour; block billing unacceptable | PDF | Public bid document | 4 | Search-only (host rejected the request) |
| University of Missouri System OCGs | https://www.umsystem.edu/sites/default/files/media/gc/outside-counsel-guidelines.pdf | Per search: most-favoured-rate expectation, expert approval, invoice review | PDF | Public university | 4 | Search-only (redirect/HTML) |
| Houston Community College, Guidelines for Outside Counsel (RFQ 162001) | https://northeast.hccs.edu/about-hcc/procurement/rfqs/162001_HCC-Guidelines-for-Outside-Counsel.pdf | Per search: tenth-hour increments; travel not billed unless legal services are provided; lowest fare | PDF | Public procurement | 4 | Search-only (connection failed) |
| Implenia legal billing guidelines | https://implenia.com/fileadmin/implenia.com/campaign-landing-pages/Billing_Guidelines/Implenia_Legal_Billing_Guidelines.pdf | Per search: budget before each engagement, notice at 75% of budget; invoices may be adjusted or rejected | PDF | Corporate | 3 | Search-only (blocked) |
| Advantage Solutions billing manual (2015) and other LexisNexis-hosted corporate guidelines | https://www.lexisnexis.com/ln-media/attorney-implementation/m/mediagallery/121307/download.aspx ; https://lexisnexis.com/ln-media/attorney-implementation/m/mediagallery/120898/download.aspx | Per search: billing rates, staffing, disbursements, invoice structure; termination for repeated non-compliance | PDF | Corporate samples hosted by a vendor; terms unknown | 3 | Search-only |
| Liberty Mutual litigation guidelines | not located by URL in my results | Insurer panel guidelines effective for matters after 31 Dec 2019 | PDF | Corporate | 3 | Search-only; URL not captured |

**Collections.** I found no curated public OCG collection. The nearest are procurement portals (state and county bid documents) and the vendor white papers from Onit, which describe good-practice guideline content and are gated. Use the table above as the seed set and add 10 to 20 more by searching procurement portals for "outside counsel guidelines" and "legal billing guidelines".

### 3b. Rate cards and panel rates

| Source | URL | What it contains | Format | Licence/terms | Use | Verification |
|---|---|---|---|---|---|---|
| Attorney General's civil panel counsel: practical information (UK GLD) | https://www.gov.uk/guidance/attorney-generals-panel-counsel-practical-information | Hourly rates from 1 April 2025: London A £150, B £125, C £100 (over 5 years' call) or £75; Regional A £137.50, B £112.50, C £75; KC £225, £245, £270; junior juniors £31.25 and £56.25; one fixed fee £437.50 + VAT. Billing rules: monthly fee notes, GLD case reference, PDF one invoice per file, non-working travel at half rate, rolling and duplicate fee notes rejected, claims ideally within 3 months | HTML | Open Government Licence v3.0 | 5 (clean tiered rate card plus explicit rules, and licence allows reuse) | Fetched |
| Texas Gov't Code 2254.106 | https://texas.public.law/statutes/tex._gov't_code_section_2254.106 | $1,000 per hour ceiling for contingent-fee contracts of political subdivisions | HTML | Public statute | 2 | Search-only |
| Texas OAG outside counsel rules | see 3a | Invoice summary requirements; no rate card | PDF | Public | 3 | Fetched |
| Fee-application rate tables | see 1a (Exhibit B timekeeper tables) | Real firm rates by timekeeper title with annual increases | PDF | Public record | 4 (real rate-increase patterns for rate-card checks) | Fetched |

I found no state attorney general publishing a per-timekeeper cap schedule in these results. Many state AG rates sit in individual contracts obtained by request.

---

## 4. UTBMS code sets

| Source | URL | What it contains | Format | Licence/terms | Use | Verification |
|---|---|---|---|---|---|---|
| utbms.com code-set pages | https://utbms.com | Pages for ABA Litigation, DRI Revised Litigation, Jackson EW-UTBMS (England and Wales), Canadian Litigation, ABA and LOC Project, ABA Counseling, ABA Bankruptcy, LOC Patent (2009) and IP Patent Prosecution (2023), LOC Trademark, ABA and LOC Activity, ABA and LOC Expense, Criminal Law, eDiscovery, GRC, M&A, Real Estate, Workers' Compensation, KM, Universal UTBMS Definitions, Non-English translations | HTML tables and linked files | "Copyright (c) LEDES Oversight Committee 1998-2025." The terms of use say "You may download materials displayed on this website" and reserve IP rights. They do not explicitly grant redistribution. Get written confirmation from the LEDES Oversight Committee before shipping the code lists inside a product | 5 | Fetched |
| ledes.org LOC revised codes | https://ledes.org/loc-revised-activity-codes-2013/ ; https://ledes.org/loc-revised-expense-codes-2013/ ; https://ledes.org/loc-revised-timekeeper-classification-codes-august-2014/ | Revised activity (2013), expense (2013) and timekeeper classification (2014) code lists | HTML | As above | 5 | Fetched (links listed on the index; pages not opened individually) |
| Jackson EW-UTBMS J-Code documentation v5.21 | https://utbms.com/wp-content/uploads/2023/07/Jackson-Review-UTBMS-LitCode-Revision-EW-UTBMS-J-Code-Documentation-and-Guidelines-v5.21.pdf | Spec for the England and Wales civil litigation set | PDF | As above | 3 (non-US market) | Search-only |
| Thomson Reuters Legal Tracker, task and activity codes | https://www.thomsonreuters.com/en-us/help/legal-tracker/law-firm/task-and-activity-codes | Vendor listing of codes | HTML | Vendor documentation | 3 for cross-checking | Search-only |
| Wikipedia, Uniform Task-Based Management System | https://en.wikipedia.org/wiki/Uniform_Task-Based_Management_System | Overview and history; UTBMS was developed by the ABA, the Association of Corporate Counsel and PricewaterhouseCoopers and is now maintained by the LEDES Oversight Committee | HTML | CC BY-SA | 2 | Search-only |

**Notes.**
- The structure is Litigation `L110` to `L5xx` task codes (phase = first digit), activity codes `A101` to `A112`-ish (plan, research, draft, review, and so on), and expense codes `E101` and up (E101 copying). Check the exact lists on the pages above before encoding them.
- Bankruptcy fee applications use the ABA Bankruptcy "B" codes, which are on the same site. They use B-codes at the task level and typically no A-code. The corpus in 1a therefore tests task coding well but not activity coding.
- Patent and trademark sets (LOC 2009, 2023 prosecution) matter if the customers do IP work.

---

## 5. Synthetic data generation

### 5a. Tools and approaches

| Tool | URL | What it does | Licence | Fit | Verification |
|---|---|---|---|---|---|
| docsynth | https://pypi.org/project/docsynth/ | Builds synthetic documents from templates and data and computes a golden dataset of true field values. First pack is invoices (flat, tiered, metered, subscription billing; many business types, jurisdictions and languages; clean, scanned and handwritten captures). Deterministic, no LLM. PDF via Playwright and Chromium. Alpha (0.1.0) | MIT | Good for generic invoice variety. Not legal-specific: you would add a law-firm pack (timekeepers, rates, narratives, UTBMS) | Fetched |
| SEED (AWS Labs) | https://awslabs.github.io/synthetically_engineered_evaluation_data/ | From a schema or plain-English brief, generates a PDF plus paired ground-truth JSON. Batch runs, multi-document packets, optional scan ageing via Augraphy. Uses Amazon Bedrock | Not stated on the page; check the repo | Strong on narrative realism. Needs Bedrock, so it creates a cloud dependency for test-data generation | Fetched |
| Augraphy | https://github.com/sparkfish/augraphy | Degrades clean document images to look printed, faxed, scanned, folded, photocopied. Paired clean and noisy output | MIT | Use as the final scan-noise pass on any generated PDFs | Fetched |
| Layout-preserving content replacement (arXiv 2508.03754) | https://arxiv.org/abs/2508.03754 | Takes a real invoice, swaps field values using an LLM, inpaints, and outputs a new image with aligned JSON | Paper; code release not stated | Good idea for turning a handful of real or partner invoices into many variants; needs an in-house implementation | Fetched |
| FakeInvoiceGen | https://pypi.org/project/fakeinvoicegen/ | Python package combining Faker and InvoiceGenerator to make fake invoices | not checked | Quick starter; limited layout control | Search-only |
| Faker, ReportLab/WeasyPrint/HTML-to-PDF via Playwright | n/a | Build your own: HTML/CSS templates per "firm", data from a seeded generator | permissive | Most control over legal-specific features | Not a source; standard practice |

**Recommended approach.** Write a small in-house generator. Seed a data model with LEDES-shaped records (header, timekeepers, lines with UTBMS codes), render through 15 to 25 HTML/CSS firm templates to PDF, then run an Augraphy pass on about 30% of output. The LEDES record is the ground truth. Use an LLM only to draft narratives (with a human-reviewed bank of narrative styles), and seed it with narratives from the public fee applications in 1a to reflect real phrasing. Generate the code label from the narrative's intended task, not from the narrative text, so coding accuracy has an independent answer key.

### 5b. Variation to include

| Axis | Variants |
|---|---|
| Layout | Table with one row per time entry; grouped by timekeeper; grouped by task code; grouped by date; expenses in a separate section or interleaved; multi-column; landscape; summary page with detail pages after |
| Pages | 1 to 40 pages; running headers and footers; page totals that must not be double-counted; "continued" rows |
| Header | Invoice number formats; invoice date versus period; client and matter ids; PO number; remit-to; firm tax id; currency and language (including 98BI/VAT style) |
| Line items | Hours in decimal and in minutes; rate on every line versus a timekeeper legend; flat-fee and capped lines; write-offs and "no charge" lines; negative adjustments |
| Narratives | Clear; vague ("attention to file", "review documents"); block-billed (several tasks with one time); multiple matters in one invoice; internal abbreviations; names of individuals; all-caps; non-English fragments |
| Coding | Codes present, absent, wrong or partly wrong; task only; task plus activity; T-code mismatch with narrative; LEDES-style "L110 A102" pairs and the bankruptcy B-series |
| Expenses | UTBMS expense codes E101 and up; mileage and per-diem; travel with receipts; markup on disbursements; third-party vendors; foreign currency |
| Money | Discounts by percentage and by fixed amount; volume discount at the invoice level; taxes (sales tax, VAT, GST, withholding); rounding differences between line totals and the invoice total; deliberate arithmetic errors |
| Rule violations to seed | Exceeds daily cap; non-0.1 increments; clerical work billed at attorney rate; duplicate entries across invoices; rate above the cap or rate-card; unapproved timekeeper; travel at full rate; non-reimbursable expense; late submission; budget overrun |
| Image quality | Clean digital PDF, scan at 150 to 300 dpi, skew, fax artefacts, stamps, handwriting notes, redaction boxes |

---

## Recommended starter evaluation set

Target sizes are first-pass, chosen to give usable confidence intervals on the main metrics without a large labelling cost. Revisit after the first error analysis.

| Set | What to collect first | Target count | Ground truth source | Measures |
|---|---|---|---|---|
| A. Real time-entry sets | Fee applications from section 1a. Take 6 to 8 applications from 5 or more different firms, across 3 claims agents. Add scanned examples for OCR stress | 6 to 8 documents, about 600 to 1,000 labelled time entries | Hand-label header (firm, period, totals) and every line (timekeeper, date, hours, rate, amount, narrative, task code as filed) for a sample of 100 to 150 lines per document. The filed B/C/L codes are free ground truth for task coding | Line extraction F1, total reconciliation, task-code accuracy |
| B. Header and layout extraction | Synthetic law-firm invoices (5b layouts) | 150 to 200 PDFs, 20 to 25 templates; 30% with scan noise | Generator output | Header field accuracy, line-item F1, table-structure errors |
| C. Real invoice layouts | 10 to 20 real law-firm invoices from partner customers (with consent) or public-records requests (1c) | 10 to 20 invoices | Hand-labelled; redact PII first | Reality check on set B. Expect lower scores; record the gap |
| D. Coding set | 1,500 to 2,000 narratives: 500 to 800 from set A with filed codes, the rest generated and then reviewed by a lawyer or legal-ops reviewer. Include 200 block-billed and 200 vague entries | 1,500 to 2,000 lines | Two reviewers for a 300-line subset to measure agreement; resolve disagreements | Top-1 and top-3 task, activity and expense accuracy; per-code recall; confusion between near codes |
| E. OCG and rate-rule set | The OCGs in 3a that I read (NMSU, Wyandotte County, UC, Zurich, Berks, Nevada DIDS) plus UK AG panel rates. Add 10 more | 15 OCGs, 5 rate cards | A lawyer-written rule list per document (the expected rules in structured form) | Rule recall, precision, wrong parameter values (for example 0.15 colour copies), hallucinated rules |
| F. Compliance checking | Invoices (from B and A) with seeded violations against rules from E | 300 invoices, about 800 seeded violations, plus about 300 clean invoices as negatives | Seeded by the generator | Violation detection precision and recall; false flag rate |
| G. Regression/public baselines (optional) | A 50-document slice of DocILE (if access is granted) or katanaml invoices-donut-data-v1 (MIT) | 50 to 100 | Provided | Sanity check against published numbers. Do not mix into headline results |

**Order of work.**
1. Download UTBMS code lists and the LEDES 98B spec and sample (Category 4 and 1d). Confirm redistribution terms with the LEDES Oversight Committee in parallel.
2. Pull the five or six OCG PDFs already verified and write the expected-rule lists for them. This is cheap and unlocks rule-drafting accuracy early.
3. Select 6 to 8 fee applications from 1a and label sets A and the first half of D.
4. Build the synthetic generator and produce sets B and F.
5. Start the public-records requests and partner-invoice requests (set C) at once, since turnaround is weeks.

## Handling and risk notes

- **Storage.** Keep real filings and any partner invoices outside the git repository (a private bucket or SharePoint Embedded container). Commit only the labels, the generator and manifests of source URLs.
- **PII.** Fee applications name timekeepers (professionals) and can mention debtor employees or claimants in narratives. Court filings are redacted under Rule 9037, but narratives can still carry personal data. Run a PII scan before using any of it in prompts sent to third-party models, and apply the same data-handling rules as for customer data.
- **Licence conflicts.** ConfBench is non-commercial. DocILE data is research-only with an access form. Onit samples are proprietary. Use these only for internal research comparisons, or leave them out.
- **Copyright of OCGs.** OCGs from public bodies are public documents, but none states a licence. Using them internally for evaluation is low risk. Do not republish them in the product's help or marketing without checking.
- **Not verified.** Search-only rows in every table. In particular, the Kent School District request, the settlement-site fee declarations, Delaware DOJ, University of Missouri, Houston Community College, Implenia, Liberty Mutual and the Veritas-hosted Pillsbury filings.
