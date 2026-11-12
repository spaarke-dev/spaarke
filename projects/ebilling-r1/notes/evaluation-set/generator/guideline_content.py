"""Synthetic billing guidelines GL-01..GL-06: one per Set F rule set (RS-01..RS-06), written for the billing-guideline compliance test.

Each guideline states every rule of its rule set, with the same parameters and consequence, as a numbered clause, plus provisions that are
not billing rules (the drafter's "not converted" list). build_guidelines.py renders the documents and answer keys from this data and
checks them against the rule sets, so the prose and the keys cannot drift apart.

The issuing organisations are fictional. The department profiles come from public benchmarks (researcher report, 2026-11-03; sources in
guidelines/README.md): "S" marks a figure taken from a source, "E" an estimate derived from one.

A clause is C(number, text, rules=[Set F rule ids it states] | unmapped=reason, para=short paraphrase). Unmapped reasons follow Set E:
  not-billing     not about invoices (conduct, confidentiality, reporting, payment terms)
  judgement       about billing, but deciding it needs facts the invoice does not carry (an approval with no threshold, a receipt, the
                  firm's other rates)
  needs-new-type  checkable from invoice data, but no rule type states it; includes an approval above a stated threshold (Set E
                  E-OCG-14: vendor costs over $2,000)
A clause with neither is context (scope, definitions) and is not scored.
"""

# Internal conferences are exactly UTBMS activity A105, so the guideline names the code. Clerical work is named by task only: no code
# identifies it (A110 also covers legitimate document work, and Set F codes invoice preparation A111), so naming A110 would make every
# A110 line non-compliant by the text, which Set F does not (review of 2026-11-03).
A105 = "UTBMS activity code A105, Communicate (within the firm)"

CLERICAL = ("scheduled conference calls, calendar entries, recording docketed deadlines, conflict checks, invoice preparation, "
            "preparing photocopied exhibits, and documents organized and indexed for the file")


def C(n, text, rules=None, unmapped=None, para=None):
    return {"n": n, "text": text, "rules": rules or [], "unmapped": unmapped, "para": para}


def S(n, title, *clauses):
    return {"n": n, "title": title, "clauses": list(clauses)}


OUTCOMES = ("When an entry or invoice does not comply with these guidelines, the outcome is the one the clause states: the amount is "
            "reduced to what the clause allows; the charge is disallowed and not paid; the item is referred for review, which may lead "
            "to a question to the firm or an adjustment; or the whole invoice is returned unpaid for correction and resubmission.")

GUIDELINES = [
    {
        "id": "GL-01",
        "rule_set_id": "RS-01",
        "title": "Alder County Office of the County Attorney: Outside Counsel Billing Guidelines",
        "issuer": "Alder County Office of the County Attorney",
        "issuer_type": "county",
        "effective": "2026-01-01",
        "version": "3.1",
        "profile": {
            "persona": "P0: county government law office (small public-sector department)",
            "organisation": "County government serving about 650,000 residents (E)",
            "in_house_lawyers": "9 (E)",
            "outside_counsel_spend": "about $1.8M a year (E; the ACC 2025 median for organisations under $1B revenue is $364K, S)",
            "litigation_share": "about 60% of outside spend (E): tort and civil-rights defence, employment, land use",
            "firms": "small and mid-size local firms; rates are set contract by contract, so the guideline caps no hourly rate",
            "style": "short, older public-sector guideline focused on overhead, expenses and invoice timing (modelled on Set E county guidelines)",
        },
        "intro": ("These guidelines apply to every law firm the County retains. They are part of each engagement contract; where the "
                  "contract and these guidelines differ, the contract controls."),
        "sections": [
            S("1", "Engagement and conduct",
              C("1.1", "Before accepting a matter, the firm confirms in writing that it has no conflict with the County, its elected "
                       "officials or its departments, and discloses any relationship that could appear to be one.",
                unmapped="not-billing", para="Firms confirm and disclose conflicts before accepting a matter."),
              C("1.2", "Within 30 days of assignment the firm sends the assigned Assistant County Attorney an initial case assessment "
                       "with a litigation plan and an estimate of fees through trial.",
                unmapped="needs-new-type", para="An initial case assessment and fee estimate is due within 30 days of assignment."),
              C("1.3", "The firm does not speak to the media about a County matter without the County Attorney's prior approval.",
                unmapped="not-billing", para="No media contact without approval."),
              C("1.4", "Settlement offers are reported to the County promptly. Only the Board of Commissioners, or an official it "
                       "delegates, may authorise a settlement.",
                unmapped="not-billing", para="Only the Board or its delegate authorises settlement.")),
            S("2", "Recording time",
              C("2.1", "Time is recorded contemporaneously in increments of one-tenth of an hour (0.1 hour). An entry recorded in any "
                       "other increment is reduced to the next lower tenth of an hour.",
                rules=["RS-01-R01"], para="Time is billed in 0.1-hour increments; other increments are reduced."),
              C("2.2", "Each task is a separate entry with its own time and a description specific enough to show what was done and "
                       "why. Block billing, meaning several tasks under one time charge, and vague descriptions such as \"attention to "
                       "file\" or \"review documents\" are not acceptable. Such entries are referred to the assigned Assistant County "
                       "Attorney for review.",
                rules=["RS-01-R02"], para="Block-billed and vague entries are referred for review."),
              C("2.3", f"Clerical and administrative work is overhead and is not billable, whoever performs it. It includes {CLERICAL}. "
                       "Entries for this work are disallowed and not paid.",
                rules=["RS-01-R03"], para="Clerical and administrative work is not billable.")),
            S("3", "Expenses",
              C("3.1", "Expenses are billed at actual cost, without markup.",
                unmapped="judgement", para="Expenses at actual cost, no markup (cost is not visible on the invoice)."),
              C("3.2", "Word processing (UTBMS expense code E103), facsimile charges (E104) and online legal research such as Westlaw "
                       "or Lexis (E106) are overhead. They are not reimbursed and are disallowed.",
                rules=["RS-01-R04"], para="E103, E104 and E106 are not reimbursable."),
              C("3.3", "In-house photocopying (E101) is reimbursed at no more than $0.15 per page. A charge above that rate is "
                       "reduced to $0.15 per page.",
                rules=["RS-01-R05"], para="Copying is capped at $0.15 per page."),
              C("3.4", "The firm keeps a receipt for any single expense over $50 and produces it on request.",
                unmapped="judgement", para="Receipts for expenses over $50 are kept and produced on request.")),
            S("4", "Invoices",
              C("4.1", "Invoices are submitted monthly.",
                unmapped="needs-new-type", para="Invoices are submitted monthly."),
              C("4.2", "The County must receive each invoice within 45 days after the end of the billing period it covers. A later "
                       "invoice is referred for review.",
                rules=["RS-01-R06"], para="Invoices received more than 45 days after the period end are referred for review."),
              C("4.3", "Each invoice shows the County purchase order (PO) number issued for the matter. An invoice without a PO number "
                       "is returned unpaid.",
                rules=["RS-01-R08"], para="Invoices without the PO number are returned unpaid."),
              C("4.4", "An entry that repeats another entry for the same timekeeper, date, hours and description is a duplicate and is "
                       "disallowed.",
                rules=["RS-01-R07"], para="Duplicate entries (same timekeeper, date, hours, description) are disallowed.")),
            S("5", "Review and payment",
              C("5.1", OUTCOMES),
              C("5.2", "The County pays approved invoices within 45 days of approval.",
                unmapped="not-billing", para="Payment within 45 days of approval."),
              C("5.3", "The County may audit the records supporting any invoice for three years after final payment.",
                unmapped="not-billing", para="Audit rights for three years after final payment.")),
        ],
    },
    {
        "id": "GL-02",
        "rule_set_id": "RS-02",
        "title": "Lakemont State University System Office of General Counsel: Outside Counsel Guidelines",
        "issuer": "Lakemont State University System, Office of General Counsel",
        "issuer_type": "university",
        "effective": "2025-07-01",
        "version": "2025-26",
        "profile": {
            "persona": "P6a: public research university system (higher education)",
            "organisation": "Public university system with an academic medical centre; operating budget about $3.2B (E)",
            "in_house_lawyers": "14 (E; NACUA puts a large research university at 10-15 in-house attorneys, S)",
            "outside_counsel_spend": "about $4M a year (E; no published higher-education median)",
            "litigation_share": "about 40% (E): employment, student and Title IX, construction, research contracts, medical malpractice",
            "firms": "regional and national firms at discounted public-sector rates (Penn State asks for at least 10% off standard rates, S)",
            "style": "university guideline with an approved staffing roster, a rate schedule, a daily-hours cap, LEDES codes, matter "
                     "dates and budget, and a 90-day limit on old charges (Penn State, University of California, Georgia Tech)",
        },
        "intro": ("The Office of General Counsel (OGC) alone retains outside counsel for the System and its campuses. These guidelines "
                  "apply to every engagement and are incorporated in each engagement letter."),
        "sections": [
            S("1", "Engagement",
              C("1.1", "No campus, department or employee may retain outside counsel for the System; only the OGC may.",
                unmapped="not-billing", para="Only the OGC retains outside counsel."),
              C("1.2", "The firm discloses any conflict before the engagement and any that arises during it.",
                unmapped="not-billing", para="Conflicts disclosed before and during the engagement."),
              C("1.3", "Student education records and patient health information are confidential under federal law. The firm notifies "
                       "the OGC within 24 hours of any incident that may expose System data.",
                unmapped="not-billing", para="Confidentiality of student and patient data; incident notice within 24 hours.")),
            S("2", "Staffing",
              C("2.1", "With the engagement letter the firm submits a staffing plan naming each lawyer and paraprofessional who will "
                       "work on the matter and their classification. The plan, once the OGC approves it, is the firm's approved "
                       "roster; adding a timekeeper needs the OGC's written approval first. An invoice that includes time by anyone "
                       "not on the approved roster is returned unpaid.",
                rules=["RS-02-R01"], para="Only timekeepers on the approved roster may bill; otherwise the invoice is returned."),
              C("2.2", "The OGC expects lean staffing and may ask the firm to explain the staffing of any task.",
                unmapped="judgement", para="Lean staffing; the OGC may question the staffing of any task.")),
            S("3", "Rates",
              C("3.1", "Rates offered to the System reflect the firm's most favourable terms for public-sector clients.",
                unmapped="judgement", para="Rates reflect the firm's best public-sector terms (the firm's other rates are not on the "
                                           "invoice)."),
              C("3.2", "Each timekeeper's hourly rate is the rate in the firm's approved rate schedule, which may not exceed the "
                       "maximum for the timekeeper's classification in Appendix A. Rates are fixed for the fiscal year (July to June) "
                       "and change only when the OGC issues a revised schedule. Time billed at a higher rate is reduced to the "
                       "approved rate.",
                rules=["RS-02-R02"], para="Rates may not exceed the approved schedule (Appendix A maximums); excess is reduced.")),
            S("4", "Recording time",
              C("4.1", "Time is recorded in increments of one-tenth of an hour (0.1 hour). An entry in any other increment is reduced "
                       "to the next lower tenth.",
                rules=["RS-02-R03"], para="0.1-hour increments; other increments are reduced."),
              C("4.2", "No timekeeper may bill more than 10 hours in a single day. Time above 10 hours in a day is "
                       "reduced to 10 hours.",
                rules=["RS-02-R04"], para="At most 10 hours per timekeeper per day; excess is reduced.")),
            S("5", "Invoices",
              C("5.1", "Invoices are submitted in LEDES format. Every time entry carries a UTBMS litigation task code (L100 to L500 "
                       "series) and an activity code (A100 series). An invoice with a time entry that lacks its codes is returned "
                       "unpaid.",
                rules=["RS-02-R05"], para="Every time entry needs UTBMS task and activity codes; otherwise the invoice is returned."),
              C("5.2", "Each invoice covers one matter.",
                unmapped="needs-new-type", para="One matter per invoice."),
              C("5.3", "The OGC must receive each invoice within 30 days after the end of the billing period. A later invoice is "
                       "referred for review. Any charge for work performed more than 90 days before the OGC receives the invoice is "
                       "also referred for review.",
                rules=["RS-02-R08"], para="Invoices more than 30 days after the period end, and charges more than 90 days old, are "
                                           "referred for review."),
              C("5.4", "Time and expenses must fall between the date the OGC opens the matter and the date it closes it; an entry "
                       "dated outside those dates is referred for review. An invoice whose total exceeds the approved budget for the "
                       "matter is referred for review.",
                rules=["RS-02-R06"], para="Entries outside the matter dates, and invoices over the matter budget, are referred for "
                                           "review."),
              C("5.5", "An entry that repeats another entry for the same timekeeper, date, hours and description is a duplicate and is "
                       "disallowed.",
                rules=["RS-02-R07"], para="Duplicate entries are disallowed."),
              C("5.6", "The firm sends a budget within 30 days of the engagement and an update whenever it expects fees to exceed the "
                       "budget by more than 10%.",
                unmapped="needs-new-type", para="Budget within 30 days; update when fees will exceed it by more than 10%.")),
            S("6", "Review and payment",
              C("6.1", OUTCOMES),
              C("6.2", "The System pays approved invoices within 45 days. The OGC reviews each firm's performance annually.",
                unmapped="not-billing", para="Payment within 45 days; annual performance review.")),
        ],
        "rates_appendix": True,
    },
    {
        "id": "GL-03",
        "rule_set_id": "RS-03",
        "title": "Brennick Industrial Group, Inc. Law Department: Outside Counsel Billing Requirements",
        "issuer": "Brennick Industrial Group, Inc., Law Department",
        "issuer_type": "corporate-mid",
        "effective": "2026-03-01",
        "version": "4",
        "profile": {
            "persona": "P3: mid-size industrial manufacturer (corporate, mid-market)",
            "organisation": "Manufacturer and distributor of industrial components; revenue about $2.5B (E)",
            "in_house_lawyers": "10 (E; ACC 2025 median for $1-5B revenue is 12, S)",
            "outside_counsel_spend": "about $4M a year (E; ACC 2025 band median $3.3M, S)",
            "litigation_share": "about 36% (S-derived from ACC 2025 litigation spend for $1-5B): product liability, commercial "
                                "contract disputes, employment, environmental",
            "firms": "50-200-lawyer regional firms in secondary cities; the $520 partner maximum matches 2022 Atlanta and Baltimore "
                     "litigation partner medians of $525-585 (S)",
            "style": "procurement-led corporate requirements: a purchase order on every invoice, a strict 8-hour day, meal and copy "
                     "limits, no internal conferences",
        },
        "intro": ("These requirements apply to all legal services bought by Brennick Industrial Group and its subsidiaries (the "
                  "Company), including product liability, commercial, employment and environmental matters. Outside counsel buys are "
                  "managed by the Law Department with Procurement."),
        "sections": [
            S("1", "Working with the Law Department",
              C("1.1", "The responsible in-house lawyer directs strategy. The firm sends that lawyer a short status report each month "
                       "on every open matter.",
                unmapped="not-billing", para="Monthly status report to the responsible in-house lawyer."),
              C("1.2", "The firm follows the Company's Supplier Code of Conduct and returns the Company's information security "
                       "questionnaire within 10 business days of the request.",
                unmapped="not-billing", para="Supplier Code of Conduct; security questionnaire within 10 business days."),
              C("1.3", "The firm reports annually on the diversity of the teams staffing Company matters.",
                unmapped="not-billing", para="Annual diversity report.")),
            S("2", "Rates",
              C("2.1", "Hourly rates may not exceed the maximum for the timekeeper's classification in Schedule 1. Time billed at a "
                       "higher rate is reduced to the Schedule 1 maximum.",
                rules=["RS-03-R01"], para="Rates are capped by classification (Schedule 1); excess is reduced."),
              C("2.2", "Rates may change at most once a year, on 90 days' written notice, and by no more than 3%.",
                unmapped="needs-new-type", para="One rate change a year, 90 days' notice, at most 3%.")),
            S("3", "Recording time",
              C("3.1", "Time is recorded in increments of one-tenth of an hour (0.1 hour). An entry in any other increment is reduced "
                       "to the next lower tenth.",
                rules=["RS-03-R02"], para="0.1-hour increments; other increments are reduced."),
              C("3.2", "No timekeeper may bill more than 8 hours in one day. Time above 8 hours in a day is reduced "
                       "to 8 hours.",
                rules=["RS-03-R03"], para="At most 8 hours per timekeeper per day; excess is reduced."),
              C("3.3", "Each task is recorded separately with its own time. Block billing, several tasks under one time charge, is not "
                       "accepted; block-billed entries are referred to the Law Department for review.",
                rules=["RS-03-R07"], para="Block-billed entries are referred for review."),
              C("3.4", f"The following are not billable and are disallowed: clerical and administrative work, including {CLERICAL}; "
                       f"and time spent in internal conferences between lawyers or staff of the firm ({A105}).",
                rules=["RS-03-R04"], para="Clerical work and internal firm conferences are not billable."),
              C("3.5", "Legal research of more than 5 hours on a single issue needs the responsible in-house lawyer's approval in "
                       "advance.",
                unmapped="needs-new-type", para="Research over 5 hours on one issue needs advance approval.")),
            S("4", "Expenses",
              C("4.1", "Word processing (UTBMS expense code E103) and online legal research (E106) are part of the firm's overhead and "
                       "are disallowed.",
                rules=["RS-03-R05"], para="E103 and E106 are not reimbursable."),
              C("4.2", "Photocopying (E101) is reimbursed at no more than $0.10 per page, and meals (E111) at no more than $60.00 per "
                       "meal. Amounts above those limits are reduced to the limit.",
                rules=["RS-03-R06"], para="Copying capped at $0.10 per page and meals at $60.00 per meal."),
              C("4.3", "Travel needs advance approval. Air travel is reimbursed at economy fares only.",
                unmapped="judgement", para="Travel needs advance approval; economy fares only.")),
            S("5", "Invoices and payment",
              C("5.1", "Procurement issues a purchase order (PO) for each matter. Every invoice shows that PO number; an invoice "
                       "without it is returned unpaid.",
                rules=["RS-03-R08"], para="Invoices without the PO number are returned unpaid."),
              C("5.2", "Invoices are submitted monthly through the Company's e-billing system.",
                unmapped="needs-new-type", para="Monthly invoices through the e-billing system."),
              C("5.3", OUTCOMES),
              C("5.4", "The Company pays approved invoices 60 days after approval.",
                unmapped="not-billing", para="Payment 60 days after approval.")),
        ],
        "rates_appendix": True,
        "rates_appendix_name": "Schedule 1",
    },
    {
        "id": "GL-04",
        "rule_set_id": "RS-04",
        "title": "Harborline Financial Corporation: Preferred Counsel Panel Billing Guidelines",
        "issuer": "Harborline Financial Corporation, Legal Department",
        "issuer_type": "corporate-large",
        "effective": "2025-01-01",
        "version": "Panel term 2025-2026",
        "profile": {
            "persona": "P1: large financial-services company (corporate, large department)",
            "organisation": "Regional bank holding company; revenue about $12B (E)",
            "in_house_lawyers": "about 90 (E: ACC 2025 finance ratio of 7.7 lawyers per $1B revenue, S, times $12B)",
            "outside_counsel_spend": "about $35M a year (E; ACC 2025 median for $5-20B revenue is $13.8M and for $20B+ is $44M, S)",
            "litigation_share": "about 30% (E): commercial lending disputes, consumer finance class actions, regulatory "
                                "investigations, employment",
            "firms": "Am Law 51-200 and large regional firms in major metros; the $745 partner maximum sits below the 2024 national "
                     "mean of $803 (S), a discounted panel rate",
            "style": "panel guideline with an approved timekeeper list per matter, quarter-hour billing kept from the legacy panel "
                     "agreement (all eight public guidelines the researcher read require 0.1 hour), budgets and matter dates",
        },
        "intro": ("These guidelines apply to every firm on Harborline's Preferred Counsel Panel and form part of the Panel Agreement. "
                  "Panel firms handle Harborline's litigation, regulatory and employment matters."),
        "sections": [
            S("1", "Panel membership and conduct",
              C("1.1", "Panel firms meet Harborline's vendor risk requirements and sign the annual information security attestation.",
                unmapped="not-billing", para="Vendor risk requirements and annual security attestation."),
              C("1.2", "The firm tells the Legal Department within 24 hours if a regulator contacts it about a Harborline matter.",
                unmapped="not-billing", para="Regulator contact reported within 24 hours."),
              C("1.3", "Panel firms commit to staff Harborline matters with diverse teams and report on them each year.",
                unmapped="not-billing", para="Diversity commitment and annual report.")),
            S("2", "Staffing",
              C("2.1", "For each matter the Legal Department approves an Approved Timekeeper List. Only timekeepers on that list may "
                       "bill the matter. An invoice that includes time by any other timekeeper is returned unpaid.",
                rules=["RS-04-R01"], para="Only timekeepers on the approved list may bill; otherwise the invoice is returned."),
              C("2.2", "Time spent bringing a new team member up to speed is not charged without the Legal Department's agreement.",
                unmapped="judgement", para="No charge for onboarding new team members without agreement.")),
            S("3", "Rates",
              C("3.1", "Hourly rates may not exceed the maximum for the timekeeper's classification in Appendix A. Time billed at a "
                       "higher rate is reduced to the Appendix A maximum.",
                rules=["RS-04-R02"], para="Rates are capped by classification (Appendix A); excess is reduced."),
              C("3.2", "Rates are fixed for the panel term; no increase takes effect during it.",
                unmapped="needs-new-type", para="No rate increases during the panel term.")),
            S("4", "Recording time",
              C("4.1", "As provided in the Panel Agreement, time is recorded in quarter-hour increments (0.25 hour). An entry in any "
                       "other increment is reduced to the next lower quarter hour.",
                rules=["RS-04-R03"], para="Quarter-hour (0.25) increments; other increments are reduced."),
              C("4.2", f"Clerical and administrative work is not billable. It includes {CLERICAL}. Such entries are "
                       "disallowed.",
                rules=["RS-04-R08"], para="Clerical and administrative work is not billable.")),
            S("5", "Expenses",
              C("5.1", "Facsimile charges (UTBMS expense code E104) and online legal research (E106) are not reimbursed. Photocopying "
                       "(E101) charged at more than $0.15 per page is not reimbursed. These charges are disallowed.",
                rules=["RS-04-R07"], para="E104 and E106 are not reimbursable; copying above $0.15 per page is disallowed."),
              C("5.2", "Experts, consultants and other vendors costing more than $10,000 need the Legal Department's approval before "
                       "they are engaged.",
                unmapped="needs-new-type", para="Vendors over $10,000 need advance approval.")),
            S("6", "Invoices and budgets",
              C("6.1", "Harborline must receive each invoice within 60 days after the end of the billing period. A later invoice is "
                       "referred for review.",
                rules=["RS-04-R05"], para="Invoices received more than 60 days after the period end are referred for review."),
              C("6.2", "Work must fall within the matter's open and close dates as recorded by the Legal Department; time or expenses "
                       "dated outside them are referred for review. An invoice whose total exceeds the approved matter budget is "
                       "referred for review.",
                rules=["RS-04-R04"], para="Entries outside the matter dates, and invoices over the matter budget, are referred for "
                                           "review."),
              C("6.3", "An entry that repeats another entry for the same timekeeper, date, hours and description is a duplicate and is "
                       "disallowed.",
                rules=["RS-04-R06"], para="Duplicate entries are disallowed."),
              C("6.4", "The firm submits an accrual of unbilled fees for each open matter by the fifth business day after quarter end.",
                unmapped="not-billing", para="Quarterly accruals by the fifth business day."),
              C("6.5", OUTCOMES)),
        ],
        "rates_appendix": True,
    },
    {
        "id": "GL-05",
        "rule_set_id": "RS-05",
        "title": "Kestrel Mutual Insurance Company: Litigation and Coverage Counsel Billing Guidelines",
        "issuer": "Kestrel Mutual Insurance Company, Claims Legal",
        "issuer_type": "insurer",
        "effective": "2026-01-01",
        "version": "2026",
        "profile": {
            "persona": "P5: property and casualty insurer",
            "organisation": "Property and casualty insurer; about $6B in premium (E)",
            "in_house_lawyers": "about 25 in the law department (E), plus claims professionals who manage panel counsel",
            "outside_counsel_spend": "about $10M a year excluding claim defence (E); defence fees are paid from claims expense, which "
                                     "ACC's outside-spend figures exclude",
            "litigation_share": "about 40% of law-department spend (E): coverage litigation and complex commercial defence",
            "firms": "50-200-lawyer regional firms on a coverage and complex-litigation tier; the $585 partner maximum is above "
                     "routine defence-panel rates ($230-250, S), as that tier would be",
            "style": "insurer guideline (CNA and Allianz 2025 shape): early report and budget, research pre-approval, codes and the "
                     "assignment PO on every invoice, old charges not paid",
        },
        "intro": ("These guidelines apply to counsel Kestrel Mutual (the Company) assigns to defend its insureds or to represent the "
                  "Company in coverage and other litigation. Nothing in them limits counsel's independent professional judgement or "
                  "duty to the insured."),
        "sections": [
            S("1", "Reporting",
              C("1.1", "Within 45 days of assignment counsel sends the claims professional an initial report with an evaluation and a "
                       "budget through trial.",
                unmapped="needs-new-type", para="Initial report and budget within 45 days of assignment."),
              C("1.2", "Counsel sends a pre-trial report at least 60 days before trial.",
                unmapped="not-billing", para="Pre-trial report 60 days before trial."),
              C("1.3", "Legal research of more than 3 hours on one issue needs the claims professional's approval in advance.",
                unmapped="needs-new-type", para="Research over 3 hours needs advance approval.")),
            S("2", "Rates",
              C("2.1", "Hourly rates may not exceed the maximum for the timekeeper's classification in Appendix A. Time billed at a "
                       "higher rate is reduced to the Appendix A maximum.",
                rules=["RS-05-R01"], para="Rates are capped by classification (Appendix A); excess is reduced.")),
            S("3", "Recording time",
              C("3.1", "Time is recorded in increments of one-tenth of an hour (0.1 hour), rounded down. An entry in any other "
                       "increment is reduced to the next lower tenth.",
                rules=["RS-05-R02"], para="0.1-hour increments; other increments are reduced."),
              C("3.2", "Each task is recorded separately with a description that shows what was done. Block-billed entries and vague "
                       "descriptions (for example \"work on file\" or \"trial preparation\" with no detail) are reduced.",
                rules=["RS-05-R03"], para="Block-billed and vague entries are reduced."),
              C("3.3", f"Clerical and administrative work is not billable, including {CLERICAL}. Internal conferences "
                       f"between counsel's own lawyers or staff are not billable ({A105}). Such entries are disallowed.",
                rules=["RS-05-R04"], para="Clerical work and internal conferences are not billable."),
              C("3.4", "Time of more than 12 hours by one timekeeper in one day is referred to the claims professional for review.",
                rules=["RS-05-R06"], para="More than 12 hours per timekeeper per day is referred for review.")),
            S("4", "Expenses",
              C("4.1", "Expenses are billed at actual cost. Counsel keeps receipts for any item over $25 and produces them on request.",
                unmapped="judgement", para="Expenses at cost; receipts kept for items over $25."),
              C("4.2", "Experts and investigators are retained only with the claims professional's written approval.",
                unmapped="judgement", para="Experts and investigators need written approval.")),
            S("5", "Invoices",
              C("5.1", "Invoices are submitted in LEDES format. Every time entry carries a UTBMS task code and activity code, and every "
                       "invoice shows the purchase order (PO) number on the assignment letter. An invoice that lacks a code on any "
                       "time entry, or lacks the PO number, is returned unpaid.",
                rules=["RS-05-R07"], para="Codes on every time entry and the assignment PO number are required; otherwise the "
                                           "invoice is returned."),
              C("5.2", "Charges for work performed more than 120 days before the Company receives the invoice are disallowed.",
                rules=["RS-05-R08"], para="Charges more than 120 days old when the invoice is received are disallowed."),
              C("5.3", "An entry that repeats another entry for the same timekeeper, date, hours and description is a duplicate and is "
                       "disallowed.",
                rules=["RS-05-R05"], para="Duplicate entries are disallowed."),
              C("5.4", "Invoices under $1,000 are held and submitted quarterly unless they are the final invoice on the matter.",
                unmapped="needs-new-type", para="Invoices under $1,000 are held for quarterly submission unless final."),
              C("5.5", OUTCOMES),
              C("5.6", "Counsel may appeal an adjustment within 14 days of receiving it.",
                unmapped="not-billing", para="Appeals within 14 days.")),
        ],
        "rates_appendix": True,
    },
    {
        "id": "GL-06",
        "rule_set_id": "RS-06",
        "title": "State of Westmark Office of the Attorney General: Special Counsel Billing Requirements",
        "issuer": "State of Westmark, Office of the Attorney General",
        "issuer_type": "state",
        "effective": "2025-07-01",
        "version": "FY2026",
        "profile": {
            "persona": "P6b: state attorney general retaining special counsel (large public-sector department)",
            "organisation": "State attorney general's office representing state agencies; mid-size state (E)",
            "in_house_lawyers": "about 140 (E; Florida reports 450 in-house lawyers, S, secondary)",
            "outside_counsel_spend": "about $12M a year on special counsel (E; Florida spent about $250M, S, secondary)",
            "litigation_share": "about 70% (E): tort and civil-rights defence of state agencies, employment, construction claims",
            "firms": "firms under 200 lawyers in the state capital and regional cities; the $455 partner maximum sits inside the "
                     "$325-755 partner range in West Virginia University's outside-counsel contracts (S)",
            "style": "statutory special-counsel appointment with a named personnel list, many expense limits, monthly invoices due "
                     "within 30 days and matter budgets (Delaware DOJ, Wyandotte County, Ohio AG shape)",
        },
        "intro": ("The Attorney General appoints special counsel under state law to represent state agencies. These requirements are "
                  "part of every appointment; where an appointment letter sets a different term, the letter controls."),
        "sections": [
            S("1", "Appointment",
              C("1.1", "Special counsel may not represent any party adverse to the State or a state agency while the appointment "
                       "lasts.",
                unmapped="not-billing", para="No representation adverse to the State."),
              C("1.2", "Invoices may be public records. Descriptions must show the work done without disclosing privileged strategy.",
                unmapped="judgement", para="Invoices may be public; descriptions must not disclose privileged strategy.")),
            S("2", "Personnel",
              C("2.1", "The appointment letter lists the attorneys and staff approved to work on the matter. Only those personnel may "
                       "bill it. An invoice that includes time by anyone else is returned unpaid.",
                rules=["RS-06-R01"], para="Only approved personnel may bill; otherwise the invoice is returned.")),
            S("3", "Rates",
              C("3.1", "Hourly rates may not exceed the maximum for the timekeeper's classification in Appendix A. Time billed at a "
                       "higher rate is reduced to the Appendix A maximum.",
                rules=["RS-06-R02"], para="Rates are capped by classification (Appendix A); excess is reduced."),
              C("3.2", "Rates charged to the State are no higher than the lowest rates the firm charges any other public client.",
                unmapped="judgement", para="Rates no higher than the firm's lowest public-client rates (not visible on the "
                                           "invoice).")),
            S("4", "Recording time",
              C("4.1", "Time is recorded in increments of one-tenth of an hour (0.1 hour). An entry in any other increment is reduced "
                       "to the next lower tenth.",
                rules=["RS-06-R08"], para="0.1-hour increments; other increments are reduced."),
              C("4.2", "No attorney or staff member may bill more than 10 hours in one day. Time above 10 hours in a day is reduced to "
                       "10 hours.",
                rules=["RS-06-R07"], para="At most 10 hours per timekeeper per day; excess is reduced."),
              C("4.3", "Each entry describes the specific task performed. Vague descriptions such as \"attention to matter\" or "
                       "\"review file\" are referred for review.",
                rules=["RS-06-R11"], para="Vague descriptions are referred for review.")),
            S("5", "Expenses",
              C("5.1", "Word processing (UTBMS expense code E103), facsimile (E104) and online legal research (E106) are overhead and "
                       "are disallowed.",
                rules=["RS-06-R03"], para="E103, E104 and E106 are not reimbursable."),
              C("5.2", "Photocopying (E101) is reimbursed at no more than $0.15 per page, and delivery and messenger services (E107) at "
                       "no more than $40.00 per delivery. Amounts above those limits are reduced to the limit.",
                rules=["RS-06-R04"], para="Copying capped at $0.15 per page and deliveries at $40.00 each."),
              C("5.3", "Travel outside the State needs the Attorney General's approval in advance.",
                unmapped="judgement", para="Out-of-state travel needs advance approval.")),
            S("6", "Invoices",
              C("6.1", "Invoices are submitted monthly, and the Office must receive each within 30 days after the end of the month "
                       "billed. A later invoice is referred for review.",
                rules=["RS-06-R06"], para="Invoices received more than 30 days after the period end are referred for review."),
              C("6.2", "Every time entry carries a UTBMS litigation task code and an activity code. An invoice with a time entry that "
                       "lacks its codes is returned unpaid.",
                rules=["RS-06-R10"], para="Codes required on every time entry; otherwise the invoice is returned."),
              C("6.3", "Work must fall within the matter's open and close dates; entries dated outside them are referred for review. "
                       "An invoice whose total exceeds the budget approved for the matter is referred for review.",
                rules=["RS-06-R05"], para="Entries outside the matter dates, and invoices over the matter budget, are referred for "
                                           "review."),
              C("6.4", "An entry that repeats another entry for the same timekeeper, date, hours and description is a duplicate and is "
                       "disallowed.",
                rules=["RS-06-R09"], para="Duplicate entries are disallowed."),
              C("6.5", "The final invoice is submitted within 60 days after the matter closes.",
                unmapped="needs-new-type", para="Final invoice within 60 days of closing."),
              C("6.6", OUTCOMES),
              C("6.7", "No interest or late fee accrues on any amount the State owes.",
                unmapped="not-billing", para="No interest or late fees.")),
        ],
        "rates_appendix": True,
    },
]

RATE_ORDER = ["Partner", "Of Counsel", "Senior Associate", "Associate", "Paralegal", "Legal Assistant"]
