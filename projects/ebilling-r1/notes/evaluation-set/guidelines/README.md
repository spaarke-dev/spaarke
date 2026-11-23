# Synthetic billing guidelines for the compliance test

> Written 2026-11-03 by the spaarke-ontology-platform-r1 session for ebilling-r1 (owner decision "option C": ontology writes the
> guidelines, keys and reference data; ebilling seeds dev, publishes, submits and scores). Plan:
> `projects/spaarke-ontology-platform-r1/notes/billing-guidelines-compliance-test-plan.md` on `docs/ontology-platform-design`.

These files test compliance detection end to end on invoices that already exist. Each guideline GL-0n is the document a law department would
upload for rule set RS-0n. It states every RS-0n rule, with the same parameters and consequence, as a numbered clause, and adds provisions
that are not billing rules. **No Set F invoice was regenerated and no rule set was changed.**

## Files

| Path | What | Committed |
|---|---|---|
| `GL-01.md` … `GL-06.md` | The guideline text | yes |
| `keys/GL-0n.json` | Answer key: each rule with its clause, the Set F parameters and consequence, the production rule bodies a correct drafter produces, and the provisions it must leave unconverted with a reason | yes |
| `gap-report.md` | What production R1 cannot state, with finding counts, and the subset scoring baseline | yes |
| `subset.json` | The 120 test invoices: 15 seeded and 5 clean per rule set | yes |
| `reference-data/RS-0n.json` | Synthetic firms, matters and one rate-card workbook per firm (078 format) for every subset invoice | yes |
| `production-key/RS-0n.json` | Per invoice: the Set F findings and what production should raise instead, with a status per finding, plus the production rule bodies and consequences | yes |
| `local/guidelines/GL-0n.docx` and `.pdf` | The guideline rendered for upload (the drafter accepts both) | no (git-ignored) |

Generators, run from the `evaluation-set` folder in this order:

```
python -I generator/build_guideline_data.py           # subset.json, reference-data/
python -I generator/production_key.py                 # production-key/
python -I generator/build_guidelines.py --render      # GL-0n.md, keys/, gap-report.md; --render writes local/guidelines/ (needs python-docx, reportlab)
```

All three are deterministic. `build_guidelines.py` fails when a rule-set rule has no clause, a clause omits a parameter value or its
consequence, a provision has no reason, or the rate schedule disagrees with the rule set. The text lives in
`generator/guideline_content.py`, so the documents and the keys cannot drift apart. `production_key.py` models the production handlers
in Python (`Services/Signals/Billing/**`, 2026-11-03); the consequence rules come from `BillingRuleCatalogue.AllowedConsequences`.

## The six departments

The owner asked for guidelines representative of different law departments (size, spend, matter mix) that still match the existing
invoices. The rule sets fix the rules, rates and consequences; each guideline is written for a department whose profile fits them. The
organisations are fictional. Figures marked S come from a source below; E marks an estimate derived from one.

| Guideline | Rule set | Department | Size | In-house lawyers | Outside counsel spend | Rate maximum (partner) |
|---|---|---|---|---|---|---|
| GL-01 | RS-01 | Alder County Office of the County Attorney | County of ~650,000 (E) | 9 (E) | ~$1.8M (E) | none: rates are set per contract |
| GL-02 | RS-02 | Lakemont State University System, Office of General Counsel | ~$3.2B budget (E) | 14 (E; 10-15 is typical, S) | ~$4M (E) | $650 |
| GL-03 | RS-03 | Brennick Industrial Group, Law Department (manufacturer) | ~$2.5B revenue (E) | 10 (E; band median 12, S) | ~$4M (E; band median $3.3M, S) | $520 |
| GL-04 | RS-04 | Harborline Financial Corporation (bank holding company) | ~$12B revenue (E) | ~90 (E; 7.7 per $1B, S) | ~$35M (E) | $745 |
| GL-05 | RS-05 | Kestrel Mutual Insurance Company (P&C insurer) | ~$6B premium (E) | ~25 (E) | ~$10M excl. claim defence (E) | $585 |
| GL-06 | RS-06 | State of Westmark, Office of the Attorney General | Mid-size state (E) | ~140 (E) | ~$12M special counsel (E) | $455 |

That gives three public-sector departments (county, university, state) and three corporate ones (mid-market manufacturer, large bank,
insurer), with outside spend from about $1.8M to $35M. Each key's `profile` block holds the full profile: matter mix, firm tier and the
public guidelines its style follows.

**Rule-set titles.** RS-03 ("State-agency style") is written as a manufacturer's guideline and RS-04 ("Quarter-hour panel") as a bank's.
The RS titles name the public guideline style each rule set was modelled on (`modelled_on`); the rules suit a corporate department just as
well. RS-06 ("Public-defender style") is written as a state attorney general's special-counsel requirements, because its $455 partner
maximum is far above appointed-defence rates. RS JSON titles are unchanged.

**Rates.** The rule-set maximums sit below the 2024 national means (partner $803, associate $597, paralegal $258, S) and fit discounted or
regional firms: $520 matches 2022 Atlanta and Baltimore litigation partner medians ($525-585, S); $455 sits in West Virginia University's
contract range ($325-755, S); $585 for an insurer is a coverage tier, above routine defence-panel rates ($230-250, S). The guidelines list a
Senior Associate tier, as public contracts do; LEDES has no such classification, so the rate-card workbooks roster senior associates as
Associate with a per-timekeeper approved rate at the Senior Associate maximum.

**Quarter-hour billing (GL-04).** Every public guideline the research read requires 0.1 hour; GL-04 presents 0.25 as a term kept from a
legacy panel agreement.

## Synthetic reference data

`reference-data/RS-0n.json` holds what ebilling seeds on dev before submitting (zz- prefix, removed afterwards):

- **Firms**: the firm on each subset invoice, its letterhead address and a billing e-mail on the reserved `.example` domain. `key` is
  unique per rule set (`rs-0n-...`). Six firm names appear in two rule sets with different rate caps and rosters (for example
  "Ravenscroft & Ambrose LLP" in RS-02 and RS-06), and "Kingsley and Forsythe LLP" bills two RS-01 invoices. Seed one firm record per
  `key`; if firm names must be unique on dev, suffix the rule set. One firm shared across rule sets would merge rosters and rate cards and
  approve one invoice's timekeepers for another.
- **Rate-card workbook per firm** (078 sheets `Timekeepers` and `Rates`, exact column names): the roster is the invoice's approved
  timekeepers where the guideline has an approved-timekeeper rule, otherwise every timekeeper on the invoice; each rostered timekeeper has a
  per-timekeeper approved rate (the guideline maximum for their title, or their billed rate where the guideline sets no rate); the rack rate
  is the higher of the billed rate and the approved rate plus at least 12%, rounded up to $5 (so a 10% discount off rack never falls below
  the approved rate); classification rows carry the guideline maximums. A timekeeper who bills but is not on the roster has no approved
  rate: production raises a "no approved rate" ApprovedRate finding beside the ApprovedTimekeeper finding, and the production key expects
  it. Roster and rates run
  from 2024-01-01 with no end date, so they never decide a date rule.
- **Matters**: one per invoice, with name, firm reference, assigned firm, matter type Litigation, open and close dates, budget, PO number and
  received date from the invoice's context. Billing status is Final where the guideline has a matter-dates rule, because the after-close
  check applies only to Final matters.

Team sizes follow the research: guidelines commonly allow one partner and one associate per matter without approval, with a paralegal
(Allianz's standard model). The Set F rosters (6-9 timekeepers per firm) are larger than that; they are kept as they are so that no invoice
changes.

## Scoring

Two keys, both agreed with ebilling:

- **Guideline key** (`keys/GL-0n.json`): scores the drafter. A proposal matches a key rule when its type and production body match
  `production.rules`, its consequence matches `production.consequence` (production cannot apply Reduce or Disallow as written;
  `gap-report.md` section 3), and it cites the key's clause. AiObservation `minConfidence` (0.7) is a test setting no guideline states;
  leave it out of the body match. Provisions in `unmapped_provisions` should appear on the drafter's "not converted" list. A rule whose
  `production.status` is `none` should also be on that list, with the gap named. Target: rule recall at least 80%.
- **Production key** (`production-key/RS-0n.json`): scores detection. Score findings against `production_findings`, not against Set F's
  expected findings, wherever the two differ (status `moved`, `not_expressible`, `missed`, `production_only`). `gap-report.md` §3 is the
  baseline. Targets: recall and precision at least 90% per mechanical rule type, at least 75% for AiObservation, consequence correct on
  every finding.
- **Clean invoices**: at most 2% may carry any finding. With 30 clean invoices that means none, and the production key predicts none.
  The clean invoices were chosen for their legitimate A110 entries ("Update document review protocol...", "Prepare production set...").
  The guidelines name clerical work by task, not by code, because no UTBMS code identifies it: A110 also covers that legitimate work. A
  drafter or admin who turns the clerical clause into an "A110 is not billable" rule produces false alarms on these clean invoices; across
  all of Set F that rule would flag about one clean invoice in five under GL-01, GL-03, GL-04 and GL-05 (43 of 200). Internal conferences
  are exactly A105, so that half of the GL-03 and GL-05 clause converts.

## Limits

- Department profiles are estimates built from benchmark medians; no source publishes one department's figures. Higher-education and state
  spend have no published median.
- Wolters Kluwer 2024-2025 rate tables by firm size are paywalled; the rate anchors are the 2022 sample, 2024 summaries and secondary
  reports.
- All Set F invoices are USD litigation invoices, so the guidelines' non-litigation scope (contracts, regulatory) is never exercised.
- Almost every subset invoice has its own firm, so production's cross-invoice duplicate check (a firm's earlier submissions) is barely
  exercised (one RS-01 firm bills two invoices).
- The codes clauses (GL-02 5.1, GL-05 5.1, GL-06 6.2) require a task code and an activity code, as production does; Set F checks the task
  code only. No subset line has a task code without an activity code, so the difference cannot show.
- The daily-hours caps read per day; Set F and production both judge them per invoice.
- A few clean narratives could read as clerical to a person or an AI ("Review court docket and calendar of pending deadlines");
  production's rules do not read narratives for non-billable work, so they do not affect the production key.

## Sources (researched 2026-11-03)

- ACC 2025 Law Department Management Benchmarking Report: https://www.acc.com/sites/default/files/2025-06/ACC_2025_Law_Department_Management_Benchmarking_Report.pdf
- CLOC 2025 State of the Industry: https://cloc.org/wp-content/uploads/2025/02/2025-CLOC-2025-SOTI-Report.pdf
- Wolters Kluwer Real Rate Report 2022 sample: https://assets.contenthub.wolterskluwer.com/api/public/content/9e3b15fb670e48bcbcff935adabd3eb3
- Wolters Kluwer 2024 rates (summary): https://www.legal.io/blog/5568541/Senior-Associates-Lead-Billing-Rate-Increases
- Legal Dive on 2022 city rates: https://legaldive.com/news/153-cost-difference-between-nyc-phoenix-lawyers-Wolters-Kluwer-ELM-Solutions/634079
- Thomson Reuters, State of the US Legal Market 2026: https://www.thomsonreuters.com/en-us/posts/legal/state-of-the-us-legal-market-2026/
- South Carolina Insurance Reserve Fund defence rates: https://irf.sc.gov/legal_defense
- CNA Defense Counsel Billing Guidelines 2025: https://cdn.sitetheory.io/nest001/site/1231/452076/2025%20CNA%20Defense%20Counsel%20Billing%20Guidelines.3.2025.pdf
- Allianz Resolution Management Guidelines, September 2025: https://cdn.sitetheory.io/nest001/site/1231/461304/Allianz%20Resolution%20Management%20Guidelines%20September%202025.pdf
- Penn State Outside Counsel Guidelines 2025: https://ogc.psu.edu/assets/uploads/documents/2025-Penn-State-Outside-Counsel-Guidelines.pdf
- University of Missouri System outside counsel guidelines: https://www.umsystem.edu/sites/default/files/media/gc/outside-counsel-guidelines.pdf
- Delaware DOJ outside counsel billing policy: https://bidcondocs.delaware.gov/JUD/JUD-DOJ25001-OUTSIDE_COUNSEL-billing.pdf
- Wyandotte County outside counsel guide: https://purchasing.wycokck.org/eprocurement/bids/N33053/Exhibit%20B%20-%20Outside%20counsel%20guide.pdf
- West Virginia University outside counsel contracts (2024 disclosure): https://ethics.wv.gov/SiteCollectionDocuments/Disclosure%20of%20Lobbying%20Contracts/2024/2024%20West%20Virginia%20University.pdf
- NACUA, higher education law at a glance: https://www.nacua.org/docs/default-source/about-section/higheredlawataglance.pdf?sfvrsn=6
- Affirm legal services requirements: https://affirm.com/terms/lsr
- Federal Reserve Bank of Boston engagement letter (MSLP): https://www.bostonfed.org/-/media/Documents/special-lending-facilities/mslp/legal/MSLP-Legal_Services-Engagement-Letter-GT.pdf

The guidelines paraphrase common provisions; none copies a clause from these documents.
