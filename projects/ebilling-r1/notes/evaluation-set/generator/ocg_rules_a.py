"""Expected-rule lists for E-OCG-01..06 (AI-drafted from the public documents, paraphrased, pending legal-ops review).

Each rule: type (one of the 11 FR-24 types), params (values exactly as the document states them), consequence
(what the document says happens; None when it says nothing specific), clause reference (section as printed, 1-based
pdf page) and a paraphrase (own words, no quotation). Evaluation data only.
"""


def R(t, p, c, sec, pg, para):
    return {"rule_type": t, "params": p, "consequence": c, "clause_ref": {"section": sec, "page": pg},
            "paraphrase": para, "expressible": True}


def U(sec, pg, para, reason):
    return {"clause_ref": {"section": sec, "page": pg}, "paraphrase": para, "reason": reason}


OCG_A = {
    "E-OCG-01": {  # County of Berks (PA), 2016
        "issuer_type": "county", "pages": 5,
        "rules": [
            R("TimeIncrement", {"increment_hours": 0.1, "group_tasks_of_6_minutes_or_less": True}, None, "Billing Practices", 3,
              "Time is recorded daily in tenths of an hour; several tasks totalling six minutes or less are grouped as one 0.1 charge."),
            R("AiObservation", {"observations": ["block_billing"]}, None, "Billing Practices", 3,
              "Each activity needs its own time entry; block billing is not acceptable."),
            R("AiObservation", {"observations": ["vague_description"]}, None, "Billing Practices", 3,
              "Descriptions must state the nature, purpose or subject of the work and the specific task."),
            R("ApprovedTimekeeper", {"max_attorneys_per_event": 1}, None, "Staffing", 1,
              "Only one attorney attends trial, court appearances, meetings, depositions, interviews and inspections unless approved."),
            R("ApprovedTimekeeper", {"approved_titles": ["attorney", "paralegal"]}, "Disallow", "Staffing", 2,
              "Only attorney or paralegal work is paid; secretarial, clerical and administrative work is not paid at any rate."),
            R("NonBillableActivity", {"keywords": ["training lawyers", "opening or closing files", "conflicts check", "preparing invoices",
                                                    "bates stamping", "routine file review", "interoffice conference",
                                                    "billing inquiry", "auditor request"]}, "Disallow", "Disallowed Fees/Expenses", 4,
              "Listed activities are disallowed, including file opening and closing, conflict checks, invoice preparation, Bates stamping, routine file review, interoffice conferences and responding to billing or auditor requests."),
            R("ExpenseRule", {"non_reimbursable": ["markup", "local telephone", "cell phone", "meals", "parking, tolls and mileage",
                                                    "fax", "in-house delivery", "office supplies", "routine postage",
                                                    "computer legal research"]}, "Disallow", "Disallowed Fees/Expenses", 4,
              "Mark-up, local and cell phone, meals, parking/tolls/mileage, fax, in-house delivery, office supplies, routine postage and computer research are not reimbursed."),
            R("ExpenseRule", {"caps": {"photocopy_per_page": 0.10}, "page_count_required": True}, None, "Allowable Fees/Expenses", 3,
              "Photocopies for external use are reimbursed at ten cents a page with the page count stated."),
            R("ExpenseRule", {"preapproval_required": ["expert fees", "outside vendor fees", "other travel (hotel, rental car)"],
                              "preapproval_over": 1000}, None, "Allowable Fees/Expenses", 4,
              "Expert, vendor and other travel expenses need prior approval, as does any other expense over one thousand dollars."),
            R("CoreRecordWindow", {"budget_cap": True, "budget_tolerance_pct": 10}, "Disallow", "Budget", 3,
              "A deviation above ten percent of an approved budget needs prior approval or the excess is rejected for payment."),
            R("RequiredData", {"fields": ["firm_name_address_phone_tax_id", "invoice_date", "matter_name", "billing_period",
                                           "entry_date", "timekeeper_name_or_initials", "entry_rate", "entry_total",
                                           "expenses_itemized_separately", "timekeeper_status_hours_amount_summary",
                                           "total_expenses", "cumulative_total_billed_on_matter"]}, None, "Invoice Details", 4,
              "Invoices must carry firm identity, dates, matter, per-entry date/timekeeper/rate/total, separately listed expenses and a timekeeper summary with cumulative billing."),
            R("RequiredData", {"fields": ["distinct_invoice_number_per_matter"]}, None, "Billing Practices", 3,
              "Each legal matter is billed separately with its own invoice number."),
        ],
        "unmapped": [
            U("Billing Practices", 3, "Invoices are submitted monthly.", "needs-new-type"),
            U("Staffing", 2, "Outside counsel should use model documents and bill only the cost of tailoring them.", "judgement"),
            U("Staffing", 1, "Assigned personnel may not be shifted without approval.", "judgement"),
        ]},
    "E-OCG-02": {  # Delaware DOJ, Jan 2025
        "issuer_type": "state", "pages": 5,
        "rules": [
            R("ApprovedRate", {"rates_fixed_for_matter": True, "change_notice_days": 60}, "Reject", "III. General Billing practices", 2,
              "Rates stay fixed for the matter; a change needs written approval 60 days before it takes effect."),
            R("InvoiceAge", {"max_days_after_period_end": 30, "no_carry_over_billing": True}, None, "III. General Billing practices", 2,
              "Invoices are rendered monthly within 30 days after month end, for the whole month, with no carry-over billing across months."),
            R("RequiredData", {"fields": ["matter_name", "invoice_number", "date_of_each_service", "type_of_activity", "subject_matter",
                                           "persons_involved", "hourly_rate_per_timekeeper", "expense_details_by_category"]},
              "Reject", "III. General Billing practices", 2,
              "Invoices reference matter and invoice number and list per-timekeeper time with date, activity, subject and persons involved; insufficient narrative is rejected."),
            R("TimeIncrement", {"increment_hours": 0.1, "de_minimis_combined_max_hours_per_day": 0.5}, None,
              "III.A / III.F", 2, "Entries are in tenths of an hour, no less than 0.1; tasks totalling a de minimis amount may be combined up to 0.5 hours a day."),
            R("AiObservation", {"observations": ["block_billing", "vague_description"]}, "Reject", "III.A.1-2", 2,
              "Block billing and entries such as bare 'Research' or 'Telephone calls' are unacceptable."),
            R("ApprovedTimekeeper", {"max_attorneys_per_event": 2, "paralegals_at_events": False, "summer_interns_allowed": False,
                                      "preapproval_overrides": True}, None, "III.J-L", 3,
              "No more than two attorneys at meetings, depositions, mediations, negotiations or hearings, no paralegals at them, and no summer intern or clerk time without pre-approval."),
            R("DailyHoursCap", {"max_hours_per_timekeeper_per_day": 10, "review_only": True}, "Flag", "III.M", 3,
              "More than ten hours a day by one person is closely reviewed."),
            R("NonBillableActivity", {"keywords": ["invoice preparation", "invoice review", "invoice correction", "secretarial time",
                                                    "overtime for staff"]}, "Disallow", "III.R, III.W", 3,
              "Time spent preparing, reviewing or correcting invoices is not acceptable; secretarial time and staff overtime are not permitted."),
            R("AiObservation", {"observations": ["internal_conference_share", "duplicative_work", "general_research", "excess_file_review"],
                                "internal_conference_share_over_pct": 10}, "Flag", "III.N-T", 3,
              "Internal conferences above 10 percent of monthly billing, general-knowledge research, duplicative work and excess file review are closely reviewed."),
            R("ExpenseRule", {"non_reimbursable": ["computer/e-mail/word processing", "online research", "fax and telephone",
                                                    "conference room and rent", "office supplies", "library use",
                                                    "clerks, proofreaders, support salaries", "in-state transportation", "meals", "markup"]},
              "Disallow", "IV.a, IV.e", 4, "Overhead items, online research, fax/phone, in-state transportation, meals and any markup are not reimbursed."),
            R("ExpenseRule", {"caps": {"photocopy_per_page": 0.10}, "itemization_required": ["units", "price_per_unit", "total_cost"],
                              "no_miscellaneous_or_grouped": True}, None, "IV.a, IV.d", 4,
              "Photocopies at most ten cents a page; every disbursement is itemised by units, unit price and total, and grouped or miscellaneous lines may be refused."),
            R("ExpenseRule", {"preapproval_required": ["travel outside Delaware", "expedited or multiple transcript copies",
                                                        "experts, consultants and contract attorneys"]}, None, "IV.b, IV.e.viii, V", 5,
              "Out-of-state travel, expedited or multiple transcripts and outside experts or contract attorneys need prior approval."),
        ],
        "unmapped": [
            U("III.U-V", 4, "Attorney time for paralegal or clerical tasks, and senior rates for work a junior could do, are closely reviewed.", "judgement"),
            U("III.B", 2, "The State does not negotiate prompt-pay discounts and pays no late fees.", "not-billing"),
        ]},
    "E-OCG-05": {  # NMSU
        "issuer_type": "university", "pages": 4,
        "rules": [
            R("TimeIncrement", {"increment_hours": 0.1}, None, "E. Fees, Costs and Billing", 3, "Hourly work is billed in tenths of an hour."),
            R("ApprovedRate", {"rates_fixed_to_negotiated": True, "max_rate_increases_per_year": 1, "rate_not_above_lowest_client_rate": True},
              "Reduce", "E. Fees, Costs and Billing", 3,
              "Billed rates may not exceed negotiated rates without pre-approval; one rate increase a year; unapproved increases are paid at negotiated rates."),
            R("NonBillableActivity", {"travel_rate_pct": 100, "no_time_on_other_matters": True}, None, "E. Fees, Costs and Billing", 3,
              "Approved travel time is paid at the base rate, excluding time spent on other matters."),
            R("ExpenseRule", {"caps": {"photocopy_color_per_page": 0.15, "photocopy_bw_per_page": 0.0085, "meals": "NMSU per diem"},
                              "no_markup": True, "mileage": "state rate", "air": "coach/economy"}, "Reduce", "E. Costs 1-3", 3,
              "Colour copies at most fifteen cents and black-and-white 0.85 cent a page; mileage at the state rate; coach air; meals at the per diem; no markup."),
            R("ExpenseRule", {"preapproval_required": ["non-routine costs", "travel"]}, None, "E. Costs", 3,
              "Costs other than routine ones need a budget line or prior written approval."),
            R("InvoiceAge", {"max_days_after_period_end": 30}, None, "E. Billing Information", 3,
              "Bills are due no later than thirty days after the end of the month of service."),
            R("RequiredData", {"fields": ["task_date", "person_name", "task_description", "time_in_tenths", "billing_rate_per_person",
                                           "itemized_costs_without_markup"]}, None, "E. Billing Information", 3,
              "Hourly invoices carry date, person, task description, time in tenths, rate and itemised costs."),
            R("CoreRecordWindow", {"budget_cap": True, "written_approval_to_exceed": True}, "Disallow", "C.4 Budget", 2,
              "Fees may not exceed the agreed target budget without written approval."),
            R("ApprovedTimekeeper", {"max_attorneys_per_task": 1, "no_duplicate_staffing": True, "changes_need_approval": True},
              "Disallow", "D. Staffing", 2, "Duplicate staffing and more than one attorney per task, deposition, hearing or meeting are not paid without approval; team changes need approval."),
            R("NonBillableActivity", {"keywords": ["preparation of budgets", "preparation of invoices", "routine diary review",
                                                    "billing inquiry response", "weekly staff meeting", "clerical work", "secretarial",
                                                    "word processing", "proofreading", "filing", "training", "marketing",
                                                    "relationship-building", "basic research", "internal firm event"]},
              "Disallow", "E. Non-Billable Time and Items", 4, "Budgets, invoices, staff meetings, clerical and secretarial work, training, marketing and basic research are not paid."),
            R("ExpenseRule", {"non_reimbursable": ["computerized legal research", "cellular telephone", "local telephone or facsimile",
                                                    "meals (except client, witness or travel)", "local travel", "miscellaneous or other expenses",
                                                    "credit card processing fees", "overhead items"]}, "Disallow",
              "E. Non-Billable Time and Items", 4, "Online research, phone and fax, most meals, local travel, miscellaneous categories and overhead are not paid."),
        ],
        "unmapped": [
            U("D. Staffing", 2, "Attorney conferencing within the office is generally not compensable unless a significant legal event requires it at the client's request.", "judgement"),
            U("D. Staffing", 2, "Education cost of a replacement team member is borne by the firm.", "judgement"),
            U("A. Engagement", 1, "No compensation for work outside the agreed scope or without express authority.", "needs-new-type"),
        ]},
}
