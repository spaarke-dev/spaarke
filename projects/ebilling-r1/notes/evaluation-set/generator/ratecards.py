"""Structured rates for E-RC-01..05 (read from the public documents under local/rate-cards/).

Each card: currency, rates (key, amount, unit, effective_from, effective_to, scope), rules (FR-24 types) and unmapped
provisions. Amounts are copied as the documents state them; no timekeeper names are recorded (E-RC-05 is keyed by role,
year of first admission and department only).
"""
from ocg_rules_a import R, U


def rate(key, amount, scope, frm=None, to=None, unit="hour"):
    return {"key": key, "amount": amount, "unit": unit, "effective_from": frm, "effective_to": to, "scope": scope}


UK_FROM = "2025-04-01"
RC = {
    "E-RC-01": {  # UK Attorney General's panel counsel rates (GLD), OGL v3.0
        "currency": "GBP", "issuer_type": "national-government",
        "rates": [
            rate("London A panel", 150, "London panel", UK_FROM), rate("London B panel", 125, "London panel", UK_FROM),
            rate("London C panel, over 5 years call", 100, "London panel", UK_FROM),
            rate("London C panel, under 5 years call", 75, "London panel", UK_FROM),
            rate("Regional A panel", 137.5, "Regional panel", UK_FROM), rate("Regional B panel", 112.5, "Regional panel", UK_FROM),
            rate("Regional C panel", 75, "Regional panel", UK_FROM),
            rate("PIL A panel", 150, "PIL panel", UK_FROM), rate("PIL B panel", 125, "PIL panel", UK_FROM),
            rate("PIL C panel, over 5 years call", 100, "PIL panel", UK_FROM),
            rate("PIL C panel, under 5 years call", 75, "PIL panel", UK_FROM),
            rate("Junior junior, second six", 31.25, "off panel", UK_FROM), rate("Junior junior, third six", 56.25, "off panel", UK_FROM),
            rate("Junior junior, tenant", 56.25, "off panel", UK_FROM),
            rate("KC, fewer than 10 years", 225, "King's Counsel", UK_FROM),
            rate("KC, 10 years or more, previous rate under 200", 245, "King's Counsel", UK_FROM),
            rate("KC, 10 years or more, previous rate 200 to 269", 270, "King's Counsel", UK_FROM),
            rate("Immigration judicial review permission hearing, fixed fee", 437.5, "fixed fee (plus VAT)", UK_FROM, unit="fixed"),
        ],
        "rules": [
            R("ApprovedRate", {"rates": {"London A": 150, "London B": 125, "Regional A": 137.5, "KC under 10 years": 225}, "currency": "GBP"},
              None, "Rates", 1, "Hourly rates by panel and seniority apply to all work from 1 April 2025."),
            R("NonBillableActivity", {"travel_rate_pct": 50, "non_working_travel_only": True, "no_travel_within": "M25 for London counsel"}, "Reduce", "Travel", 1,
              "Non-working travel time is paid at half the hourly rate and nothing is paid for travel inside the normal area of practice."),
            R("RequiredData", {"fields": ["case_reference", "work_description_by_date", "hourly_rate", "time_spent", "travel_time_separate"]}, None, "Fee notes", 1,
              "A fee note shows the case reference, dated activity entries, the rate, the time and any non-working travel time."),
            R("ExpenseRule", {"meals": "no subsistence rate", "first_class_travel": "special circumstances only", "differentiate_categories": True}, "Disallow", "Travel", 1,
              "No daily subsistence is paid and travel, accommodation and legal fees are shown separately."),
        ],
        "unmapped": [U("Rates", 1, "Rates are reviewed every two years from April 2027.", "not-billing")]},
    "E-RC-02": {  # Louisiana Office of State Procurement memo 2024-02-22
        "currency": "USD", "issuer_type": "state",
        "rates": [
            rate("Attorney, 10 or more years", 350, "attorney", "2024-02-22"), rate("Attorney, 5 to 10 years", 275, "attorney", "2024-02-22"),
            rate("Attorney, 3 to 5 years", 225, "attorney", "2024-02-22"), rate("Attorney, under 3 years", 175, "attorney", "2024-02-22"),
            rate("Paralegal", 80, "paralegal", "2024-02-22"),
        ],
        "rules": [
            R("ApprovedRate", {"rates": {"attorney_10y_plus": 350, "attorney_5_to_10y": 275, "attorney_3_to_5y": 225, "attorney_under_3y": 175,
                                          "paralegal": 80}, "kind": "maximum"}, "Reduce", "Schedule", 1,
              "Maximum hourly fees by years of practice and a paralegal maximum for state legal services contracts."),
        ],
        "unmapped": [U("Memo", 1, "Contracts run for one year and may be extended twice.", "not-billing")]},
    "E-RC-03": {  # CJA rate chart, S.D. Ohio, as of 2026-01-01
        "currency": "USD", "issuer_type": "court",
        "rates": [
            rate("CJA non-capital, from 2019-02-15", 148, "non-capital", "2019-02-15", "2019-12-31"),
            rate("CJA non-capital, 2020", 152, "non-capital", "2020-01-01", "2020-12-31"),
            rate("CJA non-capital, 2021", 155, "non-capital", "2021-01-01", "2021-12-31"),
            rate("CJA non-capital, 2022", 158, "non-capital", "2022-01-01", "2022-12-31"),
            rate("CJA non-capital, 2023", 164, "non-capital", "2023-01-01", "2023-12-31"),
            rate("CJA non-capital, 2024", 172, "non-capital", "2024-01-01", "2024-12-31"),
            rate("CJA non-capital, 2025", 175, "non-capital", "2025-01-01", "2025-12-31"),
            rate("CJA non-capital, 2026", 177, "non-capital", "2026-01-01", None),
            rate("CJA capital, from 2019-02-15", 190, "capital", "2019-02-15", "2019-12-31"),
            rate("CJA capital, 2020", 195, "capital", "2020-01-01", "2020-12-31"),
            rate("CJA capital, 2021", 197, "capital", "2021-01-01", "2021-12-31"),
            rate("CJA capital, 2022", 202, "capital", "2022-01-01", "2022-12-31"),
            rate("CJA capital, 2023", 210, "capital", "2023-01-01", "2023-12-31"),
            rate("CJA capital, 2024", 220, "capital", "2024-01-01", "2024-12-31"),
            rate("CJA capital, 2025", 223, "capital", "2025-01-01", "2025-12-31"),
            rate("CJA capital, 2026", 226, "capital", "2026-01-01", None),
            rate("Associate, non-panel attorney", 80, "associate", "2026-01-01"), rate("Associate, panel attorney", 90, "associate", "2026-01-01"),
            rate("Mentee, 60 percent of attorney rate (non-capital 2026)", 106.2, "mentee", "2026-01-01"),
        ],
        "rules": [
            R("ApprovedRate", {"rates": {"non_capital_2026": 177, "capital_2026": 226, "associate_non_panel": 80, "associate_panel": 90},
                               "mentee_pct_of_attorney_rate": 60}, "Reduce", "Hourly rates", 1,
              "Appointed counsel are paid the stated hourly rate for the date of the work; associates and mentees have their own rates."),
            R("CoreRecordWindow", {"budget_cap": {"felony_and_non_capital_habeas": 13800, "misdemeanor": 3900, "appeals": 9800, "other": 3000}, "year": 2026},
              "Flag", "Compensation maximums", 1, "Statutory compensation maximums per case type cap the total that can be claimed without a waiver."),
        ],
        "unmapped": [U("Footnotes", 1, "Pre-indictment appointments merge with the criminal charge once indicted.", "not-billing")]},
    "E-RC-04": {  # Florida Administrative Code 2-37.030
        "currency": "USD", "issuer_type": "state",
        "rates": [
            rate("Specialised attorney services (admiralty, IP, media, bond and securities)", 250, "attorney, specialised", None),
            rate("All other attorney services", 200, "attorney", None),
            rate("Paralegal, legal assistant, law clerk, research assistant", 40, "support", None),
        ],
        "rules": [
            R("ApprovedRate", {"rates": {"specialised_attorney": 250, "other_attorney": 200, "paralegal_and_assistants": 40}, "kind": "maximum"}, "Reduce", "(1)-(3)", 1,
              "Maximum hourly rates for specialised attorneys, other attorneys and paralegals or research assistants."),
            R("TimeIncrement", {"interval_minutes": [6, 10], "actual_time_only": True}, None, "(4)", 1,
              "A billable hour is actual time measured in intervals of six to ten minutes."),
            R("ExpenseRule", {"overhead_included_in_rate": ["local calls", "routine postage", "copying", "local travel", "library materials", "courier",
                                                            "word processing", "clerical"], "third_party_at_cost_no_surcharge": True,
                              "travel": "section 112.061 Florida Statutes"}, "Disallow", "(4)", 1,
              "Routine overhead is part of the rate; non-routine third-party costs are reimbursed at cost with no surcharge."),
            R("ApprovedRate", {"contingency_max_pct": {"through_trial": 35, "through_appeal": 40, "collections": 30}}, "Reduce", "(5)(a)", 1,
              "Contingency fees may not exceed 35 percent through trial, 40 through appeal and 30 for collections."),
        ],
        "unmapped": [U("(6)", 1, "A schedule of partner and associate rates is provided at the start of the relationship.", "not-billing")]},
    "E-RC-05": {  # Young Conaway Exhibit B (Appendix B guidelines), second interim period 2026-03-01..2026-05-31
        "currency": "USD", "issuer_type": "law-firm",
        "rates": [
            rate("Partner, admitted 1990, Business Planning", 1650, "partner", "2026-03-01", "2026-05-31"),
            rate("Partner, admitted 1999, Bankruptcy", 1405, "partner", "2026-03-01", "2026-05-31"),
            rate("Partner, admitted 2001, Bankruptcy", 1345, "partner", "2026-03-01", "2026-05-31"),
            rate("Partner, admitted 2012, Business Planning", 1080, "partner", "2026-03-01", "2026-05-31"),
            rate("Associate A, admitted 2022, Bankruptcy", 780, "associate", "2026-03-01", "2026-05-31"),
            rate("Associate B, admitted 2022, Bankruptcy", 715, "associate", "2026-03-01", "2026-05-31"),
            rate("Associate C, admitted 2024, Bankruptcy", 600, "associate", "2026-03-01", "2026-05-31"),
            rate("Associate D, admitted 2024, Bankruptcy", 600, "associate", "2026-03-01", "2026-05-31"),
            rate("Associate E, admitted 2025, Bankruptcy", 500, "associate", "2026-03-01", "2026-05-31"),
            rate("Associate F, admitted 2025, Bankruptcy", 585, "associate", "2026-03-01", "2026-05-31"),
            rate("Associate G, admitted 2025, Bankruptcy", 600, "associate", "2026-03-01", "2026-05-31"),
            rate("Paralegal A, Bankruptcy", 410, "paralegal", "2026-03-01", "2026-05-31"),
            rate("Paralegal B, Bankruptcy", 395, "paralegal", "2026-03-01", "2026-05-31"),
            rate("Paralegal C, Bankruptcy", 400, "paralegal", "2026-03-01", "2026-05-31"),
            rate("Paralegal D, Bankruptcy", 385, "paralegal", "2026-03-01", "2026-05-31"),
            rate("Paralegal E", 270, "paralegal", "2026-03-01", "2026-05-31"),
        ],
        "rules": [
            R("ApprovedRate", {"per_timekeeper": "see rates (keyed by role and admission year; no names recorded)", "no_rate_increases_in_period": True},
              "Flag", "Exhibit B", 5, "Each timekeeper bills at a single stated hourly rate for the application period and no rate increase was applied."),
        ],
        "unmapped": [U("Exhibit B", 5, "Fees and hours per timekeeper are printed alongside the rate but are not rate terms.", "not-billing")]},
}
