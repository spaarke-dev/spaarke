# Billing-guideline compliance: test plan with synthetic guidelines and invoices

> **Date**: 2026-11-03 · **Asked by**: owner · **Status**: proposal. The owner decides where it runs (§1).
> Inputs: ebilling's evaluation set, `C:\code_files\spaarke-wt-ebilling-r1\projects\ebilling-r1\notes\evaluation-set\`
> (`manifest.md`, `ground-truth/`, `generator/`, `local/`). Facts below were read 2026-11-03 from that folder, from
> ebilling's `TASK-INDEX.md` and from dev (`spaarkedev1`).

## 0. What exists today

| Piece | State |
|---|---|
| Billing rule types (11 + AddedFee, Discount), handlers, `InvoiceComplianceEvaluator`, rejection rule | Built and merged (ebilling 070–073, 115) |
| Rule drafting Action `BILLING-RULE-DRAFT@v1`, rule-set endpoints, Rule Review page, rate-card and timekeeper import | Built and merged (ebilling 075–078) |
| **Set B**: 150 synthetic invoice PDFs (`local/synthetic/B-*.pdf`) | Rendered. Built to test **extraction**: no rule set, no expected findings, 20 non-USD. B-0001 and B-0002 were submitted on dev today. |
| **Set F**: 300 seeded-violation and 300 clean invoices | Ground truth only (`ground-truth/compliance-invoices/F-*.json`). **PDFs not rendered** (`gen_eval_set.py render` does it on demand). 875 expected findings across the 11 original types; none for AddedFee or Discount. |
| **6 synthetic rule sets** RS-01..RS-06 (county, university, state agency, quarter-hour panel, insurer, public defender) | JSON rule lists only, 8–11 rules each, each `modelled_on` real guidelines in Set E. **No guideline document a person could read or upload.** |
| **Set E**: 16 real public-sector OCGs and 5 rate cards | Local PDFs, plus expected-rule lists drafted by AI and **not yet reviewed** by a legal-operations reviewer |
| Reference evaluator | `generator/rules.py: evaluate(inv, rs)` gives the expected findings for any invoice under any rule set |
| **Dev content** | **No `sprk_ruleset` rows.** The only billing policy is `BILL-SYSTEM-SUBMISSION`. Today's submissions were checked against no guidelines. |
| **Targets** | ebilling 161 and 162 measure extraction, coding and misread rejections (NFR-04..06). **No requirement measures compliance detection** (findings against expected findings). Success criterion 3 ("admin uploads an OCG and publishes a rule set where every rule cites its clause") is planned for UAT with ≥ 3 real OCGs. |

So the code is in place; what's missing is the guideline documents, the matching reference data on dev, and a measured detection target.

## 1. Where it should run

**Recommendation: ebilling owns it, and ontology reviews the Signal and Decision Record side.**

- Everything being tested sits in ebilling: the rule sets, the evaluator, the drafting Action, the evaluation set and its generator, and the 161/162 harnesses. The missing target is a gap in ebilling's FR-44 and 162.
- Ontology R1 runs critical path only (D-106/D-127). Its share is narrow: each finding becomes exactly one correct Signal, and decisions on findings write correct Decision Records. That fits as acceptance checks in the plan (§3, phase 6), reviewed by this session.
- If the owner prefers this session to run it, it runs as a separate workstream and stays out of 090 (wrap-up).

## 2. Synthetic billing guidelines (can be written here or in ebilling)

Write **six guideline documents, GL-01..GL-06, one per rule set**, so that every Set F invoice already has a guideline that matches its expected findings.

- **Content:** each rule in RS-0n becomes a numbered clause in plain language (for example "§4.2 Time is recorded in increments of one-tenth of an hour"). Parameters and consequences match the JSON exactly. Each document also gets:
  - **3–6 provisions that are not billing rules**: staffing approval, conflicts, reporting cadence, confidentiality, budget-update duty. These test the drafter's "not converted" list.
  - **Realistic wording drawn from the research below.** Public-sector style follows the Set E documents the rule set is `modelled_on`. Insurer and corporate style follow published panel-counsel and corporate guidelines.
- **Answer key:** each guideline carries a key in the Set E format (`rules[]` with `clause_ref`, plus `unmapped_provisions[]`). Drafting proposals can then be scored the same way as Set E.
- **Rate schedules:** add one rate schedule per guideline (title → rate, plus per-timekeeper exceptions). This supports ApprovedRate and ApprovedTimekeeper, and the rate-card import (078) loads it.
- **Two optional extra documents, GL-07 (corporate) and GL-08 (insurer panel counsel).** Set E is all public sector, while Spaarke customers are corporate legal departments. These would also use AddedFee and Discount, which no set covers yet. They need new invoices from the generator (§3 phase 2).
- **Licence:** synthetic, so committable. Paraphrase only; copy no clause from a real guideline beyond a few words.

What published guidelines commonly contain (research 2026-11-03; vendor templates are examples, not standards):

| Provision | Typical form | Rule type |
|---|---|---|
| Time increment | 0.1 hour (six minutes); a minority use 0.25 | TimeIncrement |
| Block billing | Not accepted; each task carries its own time | AiObservation `block_billing` |
| Vague entries ("review file", "attention to matter") | Not paid | AiObservation `vague_description` |
| Clerical or administrative work (file organisation, copying, scheduling, conflict checks, invoice preparation) | Not billable | NonBillableActivity |
| Travel time | Not billed, or billed at 50% of rate | NonBillableActivity `travel_rate_pct` |
| Rates | Fixed for the engagement; increases need written notice (60 days is common) and approval; billed at the prior rate otherwise | ApprovedRate (`max_annual_increase_pct`) |
| Staffing | Approved timekeepers only; one attorney per deposition, hearing or meeting unless approved | ApprovedTimekeeper |
| Expenses | No overhead (in-house copying, phone, research subscriptions); copies capped per page; receipts over a threshold; no markup | ExpenseRule |
| Invoice timing | Monthly, within 30–60 days of month end; older invoices reduced or rejected | InvoiceAge |
| Format | LEDES with UTBMS task and activity codes; PO number and matter number | RequiredData |
| Pre-approval (research beyond a few hours, motions, experts) | Common in insurer guidelines | Mostly judgement, so **unmapped**. Good "not converted" material. |
| Daily hours | A cap per timekeeper per day | DailyHoursCap |
| Duplicates | The same timekeeper, date, hours and narrative | Duplicate |

## 3. Plan

| Phase | What | Output | Effort |
|---|---|---|---|
| 0 | **Owner decisions**: home (§1); targets, for example recall ≥ 90% and precision ≥ 90% per rule type on seeded invoices, ≤ 2% of clean invoices with any finding, consequence correct on every finding; subset size and AI budget. | D-entries | — |
| 1 | **Guidelines**: write GL-01..06 (and GL-07..08 if wanted) as DOCX/PDF, with answer keys and rate schedules; validate each key against its RS JSON with a script. | `evaluation-set/guidelines/` | 1 session |
| 2 | **Focused invoice set ("Set G")**: Set F gives every invoice its own firm and matter (401 firms), which would mean seeding hundreds of matters. Regenerate with a small, fixed pool instead: one firm per guideline, two matters each, about 15 seeded + 5 clean invoices per guideline (about 120 in total). Expected findings come from `evaluate()`; render the PDFs and run `gen_eval_set.py check`. AddedFee and Discount injection is added only with GL-07/08. | generator change, `ground-truth/focused/` | 1 session |
| 3 | **Dev reference data** (zz- prefix, removed afterwards): firms, timekeepers, rate cards (through the 078 import), matters with open and close dates and budgets, approver roles. **Plan only until the owner approves**; nothing here runs ebilling's seed scripts. | seed manifest + script | ½ session |
| 4 | **Drafting test**: upload each guideline on the Rule Review page, run `BILLING-RULE-DRAFT@v1`, score the proposals against the key (rule recall, exact parameters, consequence, clause cited, "not converted" list). Correct the proposals by hand, then publish each rule set scoped to its matters. This is success criterion 3 on synthetic documents, ahead of the real-OCG UAT. | drafting score table | owner + 1 session |
| 5 | **Submission run**: submit Set G through the internal route (044) in a batch, plus a few through Outlook as UAT. Wait for processing. | run ids | ½ session + AI cost |
| 6 | **Score and check**: harness compares stored findings and Signals with the expected findings. Report precision and recall per rule type, false positives on clean invoices, consequence (Reject/Reduce/Disallow/Flag), and line vs invoice scope. Separate extraction misses (162's area) from rule misses. Ontology checks, reviewed here: one open Signal per finding (dedupe key), the right subject (line or invoice), a sentence that asserts only what the predicate read (§0.3), Signal ownership from the matter, a line decision closing its Signals with a Decision Record, and Superseded when a corrected invoice replaces one. | `notes/compliance-eval-results.md` | 1 session |
| 7 | **Tune** the rule parameters, drafter prompt version and AiObservation thresholds, then re-run; record misses that stay with an owner decision. **Clean up** the zz- data. | results v2 | as needed |

Do phases 1 and 2 first: they need no dev access or approvals and unblock everything else.

## 4. Risks

- **AI cost**: every invoice runs extraction and coding; AiObservation adds model calls. Estimate before phase 5 (161's escalation rule applies).
- **Extraction noise hides rule results**: a misread hour fails a TimeIncrement check for the wrong reason. Score rules on the extracted values and on the ground-truth values, so the two kinds of miss stay separate.
- **Set E keys are unreviewed**: any recall figure that uses real OCGs is provisional until a legal-operations reviewer signs them off.
- **Access**: firm users see their invoices and visible rules. Seeding external contacts for firms is uac-r2's area; leave them out unless portal UAT needs them.

## 5. Sources (research 2026-11-03)

- The Hartford, panel counsel billing guidelines: https://www.thehartford.com/claims-panel-counsel/billing-guidelines
- Implenia Legal Billing Guidelines: https://implenia.com/fileadmin/implenia.com/campaign-landing-pages/Billing_Guidelines/Implenia_Legal_Billing_Guidelines.pdf
- Corporate OCGs published for LexisNexis CounselLink implementations, for example https://www.lexisnexis.com/ln-media/attorney-implementation/m/mediagallery/120904/download.aspx
- Legal.io, legal billing requirements template: https://www.legal.io/blog/5419358/Template-Legal-Billing-Requirements
- Wolters Kluwer ELM, best practices for legal bill review: https://info.wkelmsolutions.com/rs/874-VIE-290/images/eBook-Best-Practices-for-Legal-Bill-Review.pdf
- LeanLaw, travel-time billing: https://www.leanlaw.co/blog/how-to-structure-billing-for-travel-time-door-to-door-vs-travel-only-for-law-firms/
- LeanLaw, LEDES/UTBMS codes in insurance defense: https://www.leanlaw.co/blog/ledes-utbms-codes-insurance-defense-guide/
- Vaquill, OCG template (vendor; examples only): https://www.vaquill.ai/blog/outside-counsel-guidelines-template-12-rules
- Insurer guideline ethics opinions (pre-approval rules), for example Oregon 2005-166: https://ezel.ai/ethics-opinions/osb/2005-166-insurance-defense-guidelines
