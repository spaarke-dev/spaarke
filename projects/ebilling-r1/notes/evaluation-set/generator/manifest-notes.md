## 7. Dated notes, deviations and known limits

Entries are dated; add new ones at the end. Dates are 2026-10-15 unless shown.

### 7.1 Label methods and tiers

- **Set A (8 fee applications).** Seven reconcile exactly against the totals the filing states (A-01, A-02, A-03, A-05, A-06, A-07 and A-08; the summary-level A-01 and A-08 reconcile at timekeeper-summary level only). A-04 is `partial`: the filing is a scan with an OCR layer and one 0.5-hour entry prints as 0.00. K&L Gates (A-01) and WongPartnership (A-08) are summary-level because the entry detail is not in the filing text. Fee-line amounts in A-05 and A-07 are derived (hours x the timekeeper's rate) because those filings print hours only; they sum to the stated fees. Each label records a `reconciliation` block so a reader can see how far the parse is trusted.
- **Set C (12 real invoices).** `extracted+reviewed`. Header, totals and timekeeper summaries were read from each invoice's own summary pages and then checked by arithmetic closure (timekeeper amounts equal fees; fees + adjustments + expenses + unpaid prior balance equal the amount due), and the summary page images for C-03, C-04 and C-05 were read by eye. Closure caught two OCR defects (a dropped 2,000.00 discount line on C-03's summary page; missing Current Fees and Billable Hours figures on C-04), fixed from the page image. Line entries: C-01 to C-05 (Goss Samford, scans with OCR, reconciled exactly), C-11 and C-12 (Dinsmore & Shohl, native text, reconciled to total hours; no rates or line amounts are printed). C-06 to C-10 (Stoll Keenon Ogden) are invoice-level only because the column order in their text layer is interleaved.
- **Set E (16 OCGs, 5 rate cards).** `ai-drafted-pending-review`: the expected-rule lists and rate tables are paraphrases drafted from the local documents and **have not been signed off by a legal-operations reviewer**. Treat expected-rule recall figures from them as provisional until that review happens. 166 rules map to the eleven FR-24 types and 36 provisions are recorded as `unmapped_provisions` (judgement, not billing, or needing a new type).
- **Sets B and F.** `generator`: ground truth is generator output; rendering is a pure function of the committed ground truth. `intended_codes` is an independent answer key for B.

### 7.2 Real-invoice sourcing (set C) and its limits

- All twelve are law-firm invoices that utilities filed with the Kentucky Public Service Commission in rate-case dockets (three filings, three firms, 2018-2026). They are public records with no stated licence, included for internal evaluation only, stored local-only and not redistributed. The source URLs carry the filers' business e-mail addresses as part of the PSC path.
- Skew to be aware of: one jurisdiction, regulated-utility rate-case work, three firm layouts. The set exercises scan-with-OCR, native-text and interleaved-column layouts but not litigation invoices, LEDES exports or large-firm templates. Adding invoices from other dockets or from customers (with consent) would widen it.
- Other sources examined and set aside: Texas PUC rate-case-expense exhibits (heavily degraded scans, mixed with courier and consultant invoices), Illinois community-college board packets (payment summaries, not invoices), municipal contract packets (terms, not invoices).
- Real PDFs are cut from the downloaded filings by `generator/cut_real_invoices.py` using the page ranges in `generator/real_sources.py`; re-download the filings from the listed URLs to rebuild `local/`.

### 7.3 Known anomalies in source filings (not label defects)

- A-02: the filing's own timekeeper summary prints 14,256.00 for an associate at 740.00 an hour for 19.4 hours (should be 14,356.00); its total row and the line amounts agree with the corrected value, so that summary-table total is excluded from the checks.
- A-04: the cover sheet states interim fees of 8,766.50 while the attached invoice totals 12,766.50 before a 457.50 reduction; the invoice figures are used.
- C-05: fees include a 50.00 flat charge (1.50 flat-charge hours) that is not part of the 70.70 billable hours; the line is flagged `flat_charge`.
- C-10: the expense total (21,019.40) is mostly duplicating charges; only the total is labelled.

### 7.4 Rendering, freshness and checks

- Set F PDFs are rendered on demand (`gen_eval_set.py render`); only set B PDFs were rendered for the spike. Bounding boxes are not part of the ground truth: scoring is on fields and line references, not coordinates.
- 2026-10-15 (task 004 finding, fixed): B-0001's PDF was stale relative to its ground-truth JSON (task 004 found it; B-0004 agreed). All 150 local B PDFs were re-rendered and `gen_eval_set.py render` now writes `local/synthetic/render-manifest.json` (ground-truth sha256, page count, scanned flag per PDF). `gen_eval_set.py check` fails when a PDF is missing from the manifest, was rendered from different ground truth, or whose text disagrees with the JSON (invoice number, matter, PO, totals, invoice date, every line amount, fee-line hours where the template prints hours, first 28 narrative characters; page count). Scan-noise PDFs are checked on the clean render before the noise is applied. Task 005 should run `check` before scoring.
- Environment: `python -m venv .venv` then `pip install -r generator/requirements.txt` (reportlab for rendering, pypdfium2 for reading PDFs). Run the scripts that read downloaded filings with `-I`.
- Gates: `gen_eval_set.py verify` (counts, licence fields, reference evaluator over every F file), `gen_eval_set.py check` (PDF versus ground truth), `build_manifest.py --check` (FR-02 targets and row requirements).

### 7.5 Spike subset (task 005)

The spike holds the B items listed in section 3 (including B-0150, the longest invoice, and bundled PDFs), two fee applications (A-02 line-level with a discounted rate, A-07 a long final application) and three real invoices (C-03 scan with OCR noise, C-10 interleaved columns, C-11 clean native text).
