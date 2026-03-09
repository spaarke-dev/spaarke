# Matter Report Card — KPI Catalog

> **Version**: 0.1 (Draft)  
> **Date**: February 11, 2026  
> **Purpose**: Comprehensive catalog of candidate KPIs across the three scorecard areas, with input source flexibility analysis  
> **Status**: Design Reference — for solution design discussion

---

## Reading This Document

Each KPI is tagged with:

| Tag | Meaning |
|-----|---------|
| **QN** | Quantitative — numeric, formula-driven, can be computed from data |
| **QL** | Qualitative — judgment-based, requires human assessment |
| **QN/QL** | Hybrid — can be either depending on available input source |

**Input Source Columns:**

| Column | Description |
|--------|-------------|
| **System-Calculated** | Automatically derived from Spaarke module data (invoicing, matter management, documents) |
| **Integration-Synced** | Pulled from external systems (e-billing, ERP, matter management) via API |
| **Assessment: In-House** | Collected from in-house counsel via periodic progress assessment |
| **Assessment: Outside Counsel** | Collected from outside counsel/law firm via periodic progress assessment |
| **AI-Derived** | Computed by AI analysis of documents, correspondence, billing patterns |

A ● in a column means that input source *can* feed this KPI. Multiple ● means multiple sources can contribute, with the scoring engine resolving based on confidence weighting.

---

## Area 1: Outside Counsel Guideline (OCG) Compliance

**Purpose**: Measures how well outside counsel adheres to the client's billing, staffing, communication, and operational expectations as defined in the Outside Counsel Guidelines.

**Composite Score**: Weighted average of individual KPIs below → 0–100 scale per matter.

### 1.1 Billing & Invoice Compliance

| # | KPI | Type | Description | System-Calc | Integration | In-House Assess | OC Assess | AI-Derived |
|---|-----|------|-------------|:-----------:|:-----------:|:---------------:|:---------:|:----------:|
| 1.1.1 | **Invoice Line-Item Compliance Rate** | QN | % of invoice line items conforming to OCG billing rules (no block billing, proper task codes, rate caps, etc.) | ● | ● | | | ● |
| 1.1.2 | **Billing Timeliness** | QN/QL | Average days from work performed to invoice submitted; or qualitative assessment if invoice dates unavailable | ● | ● | ● | ● | |
| 1.1.3 | **UTBMS/Task Code Accuracy** | QN/QL | % of line items with correct, granular UTBMS task and activity codes vs. generic/catch-all codes | ● | ● | | | ● |
| 1.1.4 | **Rate Compliance** | QN | % of billed rates at or below agreed/approved rates per timekeeper level | ● | ● | ● | | |
| 1.1.5 | **Invoice Rejection/Bounce-Back Rate** | QN | % of invoices or line items rejected, reduced, or sent back for revision | ● | ● | ● | ● | |
| 1.1.6 | **Narrative Description Quality** | QL | Adequacy and specificity of time entry descriptions (vs. vague entries like "research" or "conference") | | | ● | | ● |
| 1.1.7 | **Duplicate/Excessive Billing Detection** | QN | Instances of duplicate charges, excessive time for routine tasks, or charges for non-billable activities | ● | ● | | | ● |

### 1.2 Staffing & Resourcing Compliance

| # | KPI | Type | Description | System-Calc | Integration | In-House Assess | OC Assess | AI-Derived |
|---|-----|------|-------------|:-----------:|:-----------:|:---------------:|:---------:|:----------:|
| 1.2.1 | **Staffing Ratio Adherence** | QN/QL | Ratio of partner/associate/paralegal hours vs. expected mix for matter type and complexity | ● | ● | ● | ● | |
| 1.2.2 | **Staffing Stability** | QN/QL | Degree of continuity in the team assigned (turnover of key personnel, unexpected substitutions) | ● | ● | ● | ● | |
| 1.2.3 | **Appropriate Delegation** | QL | Work performed at the right level of seniority — not over-staffed with partners on routine tasks or under-staffed with juniors on complex work | | | ● | ● | ● |
| 1.2.4 | **Pre-Approval Compliance** | QN/QL | Whether required staffing approvals (e.g., adding new timekeepers, using contract attorneys) were obtained before work began | ● | ● | ● | ● | |
| 1.2.5 | **Diversity Staffing** | QN/QL | Representation of diverse attorneys in meaningful roles on the matter (not just token inclusion) | | ● | ● | ● | |

### 1.3 Communication & Operational Compliance

| # | KPI | Type | Description | System-Calc | Integration | In-House Assess | OC Assess | AI-Derived |
|---|-----|------|-------------|:-----------:|:-----------:|:---------------:|:---------:|:----------:|
| 1.3.1 | **Status Reporting Timeliness** | QN/QL | Whether required status reports/updates are delivered on the agreed schedule | ● | | ● | ● | |
| 1.3.2 | **Status Report Quality** | QL | Completeness, usefulness, and actionability of status updates provided | | | ● | | ● |
| 1.3.3 | **Responsiveness** | QL | How quickly outside counsel responds to inquiries, requests, and escalations | | | ● | ● | ● |
| 1.3.4 | **Proactive Communication** | QL | Whether outside counsel proactively alerts the client to risks, changes, opportunities — or only responds when asked | | | ● | | ● |
| 1.3.5 | **Document Management Compliance** | QN/QL | Adherence to document naming, filing, and sharing protocols specified in OCG | ● | | ● | ● | |
| 1.3.6 | **Data Security & Confidentiality** | QL | Compliance with information security requirements, proper handling of privileged materials, appropriate use of technology | | | ● | ● | |
| 1.3.7 | **Guideline Acknowledgment** | QN | Whether outside counsel formally acknowledged receipt and understanding of current OCG version | ● | | ● | ● | |

### 1.4 OCG Compliance — Reciprocal (Client Self-Assessment)

*These KPIs measure the client's own compliance with their obligations, providing the bilateral/360° dimension.*

| # | KPI | Type | Description | System-Calc | Integration | In-House Assess | OC Assess | AI-Derived |
|---|-----|------|-------------|:-----------:|:-----------:|:---------------:|:---------:|:----------:|
| 1.4.1 | **Guideline Clarity** | QL | How clear, reasonable, and unambiguous are the OCGs from outside counsel's perspective? | | | | ● | |
| 1.4.2 | **Client Responsiveness** | QL | How timely is the client in providing decisions, approvals, and information needed by outside counsel? | | | ● | ● | |
| 1.4.3 | **Invoice Review Timeliness** | QN/QL | How quickly does the client review and approve/reject invoices after submission? | ● | ● | | ● | |
| 1.4.4 | **Scope Change Communication** | QL | How well does the client communicate scope changes that affect budget, staffing, or timeline? | | | ● | ● | |

---

## Area 2: Budget Compliance

**Purpose**: Measures financial discipline — how well the matter is tracking to its approved budget, the quality of financial forecasting, and the efficiency of spend relative to matter complexity and phase progression.

**Composite Score**: Weighted average of individual KPIs below → 0–100 scale per matter.

### 2.1 Budget Adherence

| # | KPI | Type | Description | System-Calc | Integration | In-House Assess | OC Assess | AI-Derived |
|---|-----|------|-------------|:-----------:|:-----------:|:---------------:|:---------:|:----------:|
| 2.1.1 | **Overall Budget Variance** | QN | Total actual spend vs. total approved budget, expressed as % variance | ● | ● | ● | ● | |
| 2.1.2 | **Phase-Level Budget Variance** | QN | Budget variance broken down by UTBMS litigation phase or project phase (e.g., discovery at 180% while overall at 95%) | ● | ● | | ● | |
| 2.1.3 | **Budget Amendment Frequency** | QN | Number of budget revisions/amendments requested during the matter lifecycle | ● | ● | ● | ● | |
| 2.1.4 | **Budget Amendment Justification** | QL | Quality and reasonableness of justifications provided when budget increases are requested | | | ● | | ● |
| 2.1.5 | **Alternative Fee Arrangement Adherence** | QN/QL | For matters with AFAs (flat fee, cap, collar, success fee): compliance with the agreed fee structure and triggers | ● | ● | ● | ● | |

### 2.2 Financial Forecasting Quality

| # | KPI | Type | Description | System-Calc | Integration | In-House Assess | OC Assess | AI-Derived |
|---|-----|------|-------------|:-----------:|:-----------:|:---------------:|:---------:|:----------:|
| 2.2.1 | **Forecast Accuracy** | QN | Variance between outside counsel's cost forecasts/estimates and actual spend at matter close | ● | ● | | | |
| 2.2.2 | **Forecast Update Timeliness** | QN/QL | Whether forecasts are updated at required intervals or when material changes occur | ● | | ● | ● | |
| 2.2.3 | **Accrual Accuracy** | QN | Variance between reported monthly/quarterly accruals and actual invoiced amounts for the period | ● | ● | ● | ● | |
| 2.2.4 | **Forecast Trend Reliability** | QN | Over the life of the matter, is the forecast converging toward actual or oscillating? Measures directional consistency of estimate updates | ● | ● | | | ● |
| 2.2.5 | **Early Warning Effectiveness** | QL | Did outside counsel proactively flag budget risks before overruns materialized, or did surprises arrive with invoices? | | | ● | ● | ● |

### 2.3 Spend Efficiency

| # | KPI | Type | Description | System-Calc | Integration | In-House Assess | OC Assess | AI-Derived |
|---|-----|------|-------------|:-----------:|:-----------:|:---------------:|:---------:|:----------:|
| 2.3.1 | **Budget Consumption Rate (Burn Rate)** | QN | Pace of budget consumption relative to matter phase progression or expected timeline (e.g., 70% budget consumed at 40% matter completion = red flag) | ● | ● | ● | ● | ● |
| 2.3.2 | **Cost per Phase vs. Benchmark** | QN | Actual cost of each phase compared to historical benchmarks for similar matter types and complexity levels | ● | ● | | | ● |
| 2.3.3 | **Blended Rate Efficiency** | QN | Effective blended hourly rate vs. the expected blended rate given the staffing mix and approved rates | ● | ● | | | |
| 2.3.4 | **Cost Proportionality** | QN/QL | Whether the total spend is proportional to the matter's complexity, risk level, and value at stake | ● | | ● | ● | ● |
| 2.3.5 | **Expense & Disbursement Reasonableness** | QN/QL | Non-fee costs (travel, experts, filing fees, technology) as a % of total spend; flagging outliers against benchmarks or expectations | ● | ● | ● | | ● |

### 2.4 Budget Compliance — Reciprocal (Client Self-Assessment)

| # | KPI | Type | Description | System-Calc | Integration | In-House Assess | OC Assess | AI-Derived |
|---|-----|------|-------------|:-----------:|:-----------:|:---------------:|:---------:|:----------:|
| 2.4.1 | **Initial Budget Realism** | QL | Was the approved budget realistic for the matter scope and complexity, or was it set artificially low? | | | ● | ● | |
| 2.4.2 | **Scope Creep Accountability** | QL | How much of any budget variance is attributable to client-driven scope changes vs. firm execution? | | | ● | ● | |
| 2.4.3 | **Payment Timeliness** | QN/QL | How quickly does the client pay approved invoices? (Impacts firm's willingness to invest in the relationship) | ● | ● | | ● | |
| 2.4.4 | **Budget Review Engagement** | QL | Does the client actively engage in budget reviews and forecasting conversations, or leave it entirely to outside counsel? | | | ● | ● | |

---

## Area 3: Outcome Success

**Purpose**: Measures the ultimate effectiveness of the legal work — did the matter achieve its objectives relative to what was expected? This area has the highest dependence on the Matter Performance Profile established at matter inception.

**Composite Score**: Weighted average of individual KPIs below → 0–100 scale per matter. Note: many outcome KPIs can only be fully scored at matter close; the system uses leading indicators during the matter lifecycle and converts to final scores upon resolution.

### 3.1 Result vs. Expectation

| # | KPI | Type | Description | System-Calc | Integration | In-House Assess | OC Assess | AI-Derived |
|---|-----|------|-------------|:-----------:|:-----------:|:---------------:|:---------:|:----------:|
| 3.1.1 | **Outcome vs. Target** | QN/QL | Primary result compared to the success criteria defined in the performance profile. For litigation: settlement/judgment vs. initial exposure range. For transactions: deal terms achieved vs. target terms. For advisory: objectives met vs. stated goals. | ● | | ● | ● | ● |
| 3.1.2 | **Exposure Reduction** | QN | For litigation/disputes: % reduction from initial assessed exposure to final resolution amount (includes settlements, judgments, avoided costs) | ● | | ● | ● | |
| 3.1.3 | **Resolution Favorability** | QL | Subjective assessment of whether the resolution was favorable, neutral, or unfavorable relative to the realistic range of possible outcomes | | | ● | ● | |
| 3.1.4 | **Business Objective Achievement** | QL | Did the legal work achieve the underlying business objective? (e.g., deal closed, risk eliminated, license preserved, IP protected) — distinct from legal outcome per se | | | ● | | |
| 3.1.5 | **Avoided/Mitigated Risk Value** | QN/QL | Estimated value of risks avoided or mitigated through counsel's work that didn't result in formal claims or losses | | | ● | ● | ● |

### 3.2 Efficiency of Resolution

| # | KPI | Type | Description | System-Calc | Integration | In-House Assess | OC Assess | AI-Derived |
|---|-----|------|-------------|:-----------:|:-----------:|:---------------:|:---------:|:----------:|
| 3.2.1 | **Cycle Time vs. Expected Duration** | QN | Actual matter duration vs. expected duration from performance profile, adjusted for complexity | ● | ● | ● | ● | |
| 3.2.2 | **Total Cost of Resolution (TCR)** | QN | All-in cost (internal + external + settlements + expenses) to achieve the outcome; compared to expected TCR from profile | ● | ● | ● | ● | |
| 3.2.3 | **Cost-Outcome Ratio** | QN | TCR relative to the value at stake or the value preserved/recovered — the "value for money" indicator | ● | | ● | ● | ● |
| 3.2.4 | **Milestone Achievement Rate** | QN/QL | % of intermediate milestones (filing deadlines, discovery completion, settlement conferences) met on or ahead of schedule | ● | | ● | ● | |
| 3.2.5 | **Escalation Frequency** | QN/QL | Number of times the matter required escalation to senior leadership, emergency intervention, or crisis management | ● | | ● | ● | |
| 3.2.6 | **Resolution Path Efficiency** | QL | Was the matter resolved through an efficient path (early settlement, motion practice, ADR) or did it take an unnecessarily long/expensive route? | | | ● | ● | ● |

### 3.3 Strategic & Qualitative Value

| # | KPI | Type | Description | System-Calc | Integration | In-House Assess | OC Assess | AI-Derived |
|---|-----|------|-------------|:-----------:|:-----------:|:---------------:|:---------:|:----------:|
| 3.3.1 | **Strategic Counsel Quality** | QL | Did outside counsel provide strategic advice that went beyond the immediate matter — identifying broader risks, opportunities, or business implications? | | | ● | | |
| 3.3.2 | **Proactive Risk Identification** | QL | Did counsel identify and flag risks the client hadn't considered, enabling early mitigation? | | | ● | ● | ● |
| 3.3.3 | **Innovation & Creative Problem-Solving** | QL | Did counsel bring creative legal strategies, technology solutions, or process improvements to the matter? | | | ● | ● | |
| 3.3.4 | **Knowledge Transfer & Institutional Learning** | QL | Did the firm contribute to the client's institutional knowledge — training, templates, playbooks, precedent analysis that benefits future matters? | | | ● | ● | |
| 3.3.5 | **Precedent/Portfolio Impact** | QL | Does the matter outcome positively impact the company's position in related or future matters? (e.g., favorable ruling that strengthens position in similar pending cases) | | | ● | ● | ● |
| 3.3.6 | **Stakeholder Satisfaction** | QL | Overall satisfaction of the internal business stakeholders (not just legal) with how the matter was handled and its outcome | | | ● | | |
| 3.3.7 | **Relationship Strength** | QL | Net effect on the client-firm working relationship — stronger, unchanged, or strained? | | | ● | ● | |

### 3.4 Outcome Success — Leading Indicators (Scored During Active Matters)

*These are assessed on a rolling basis while the matter is active, providing a "projected outcome" component to the report card before final resolution.*

| # | KPI | Type | Description | System-Calc | Integration | In-House Assess | OC Assess | AI-Derived |
|---|-----|------|-------------|:-----------:|:-----------:|:---------------:|:---------:|:----------:|
| 3.4.1 | **Strategy Confidence** | QL | In-house counsel's current confidence level that the legal strategy will achieve the desired outcome | | | ● | ● | |
| 3.4.2 | **Exposure Trajectory** | QN/QL | Is the assessed exposure trending favorably (decreasing for defense, increasing for plaintiff) relative to the initial assessment? | ● | | ● | ● | ● |
| 3.4.3 | **Milestone Progress** | QN | % of planned milestones completed on schedule at the current point in the matter lifecycle | ● | | ● | ● | |
| 3.4.4 | **Risk Register Health** | QL | Are identified risks being managed/mitigated, or are new unplanned risks emerging? | | | ● | ● | ● |
| 3.4.5 | **Opposing Party Dynamics** | QL | Assessment of how negotiations, litigation posture, or regulatory interactions are trending | | | ● | ● | ● |

---

## Summary Analysis: Input Source Coverage

### KPIs by Primary Measurement Type

| Category | Total KPIs | Quantitative (QN) | Qualitative (QL) | Hybrid (QN/QL) |
|----------|:----------:|:------------------:|:-----------------:|:---------------:|
| **Area 1: OCG Compliance** | 22 | 6 | 8 | 8 |
| **Area 2: Budget Compliance** | 18 | 10 | 4 | 4 |
| **Area 3: Outcome Success** | 19 | 5 | 11 | 3 |
| **TOTAL** | **59** | **21** (36%) | **23** (39%) | **15** (25%) |

### KPIs by Available Input Sources

| Input Source | Area 1 | Area 2 | Area 3 | Total | % of All KPIs |
|-------------|:------:|:------:|:------:|:-----:|:-------------:|
| **System-Calculated** | 11 | 13 | 9 | 33 | 56% |
| **Integration-Synced** | 8 | 12 | 3 | 23 | 39% |
| **Assessment: In-House** | 17 | 15 | 18 | 50 | 85% |
| **Assessment: Outside Counsel** | 16 | 14 | 16 | 46 | 78% |
| **AI-Derived** | 5 | 5 | 9 | 19 | 32% |

### Key Insight: Assessment Coverage

The assessment channel (in-house + outside counsel combined) can feed **85% of all KPIs**. This validates the architectural decision that the system must work even when no system-calculated or integration data is available. A customer with zero module adoption beyond matter management can still produce meaningful report cards entirely through practitioner assessments.

Conversely, system-calculated data can feed **56% of KPIs**, and even a fully integrated customer will still need practitioner input for the remaining 44% — particularly in Area 3 (Outcome Success) where 11 of 19 KPIs are purely qualitative.

### Recommended MVP KPI Selection

For the initial MVP, select 3–5 KPIs per area that balance measurability with impact. Suggested core set:

**Area 1: OCG Compliance (pick 3–4)**
- 1.1.1 Invoice Line-Item Compliance Rate (QN — highest automation potential)
- 1.1.2 Billing Timeliness (QN/QL — works across all input modes)
- 1.2.1 Staffing Ratio Adherence (QN/QL — high decision-making value)
- 1.3.3 Responsiveness (QL — universally understood, low assessment burden)

**Area 2: Budget Compliance (pick 3–4)**
- 2.1.1 Overall Budget Variance (QN — the table-stakes metric everyone expects)
- 2.2.1 Forecast Accuracy (QN — differentiator; measures firm's predictive quality)
- 2.3.1 Budget Consumption Rate (QN — the leading indicator that enables proactive action)
- 2.2.5 Early Warning Effectiveness (QL — captures the "no surprises" expectation)

**Area 3: Outcome Success (pick 3–4)**
- 3.1.1 Outcome vs. Target (QN/QL — the definitive "did we succeed?" metric)
- 3.2.2 Total Cost of Resolution (QN — the value-for-money view)
- 3.2.1 Cycle Time vs. Expected Duration (QN — straightforward, comparable)
- 3.3.1 Strategic Counsel Quality (QL — captures the "beyond the engagement" value)

This gives **13 core KPIs** for the MVP — enough for meaningful scores without overwhelming practitioners during assessments or making the performance profile setup too burdensome.

---

## Appendix: Assessment Question Design Guidance

For KPIs sourced via practitioner assessments, the question format should minimize cognitive load. Recommended formats:

| Format | Use When | Example |
|--------|----------|---------|
| **5-point scale** | Rating quality or satisfaction | "How would you rate billing timeliness? (1=Very Poor ... 5=Excellent)" |
| **Range selection** | Estimating a numeric value | "Average time from work to invoice: <30 days / 30-60 days / 60-90 days / 90+ days" |
| **Yes/No with follow-up** | Binary compliance questions | "Were budget forecasts updated at required intervals? Yes / No → If No, describe" |
| **Traffic light** | Quick status/trajectory | "How is this matter trending? 🟢 On Track / 🟡 Needs Attention / 🔴 At Risk" |
| **Comparative** | Benchmarking against experience | "Compared to similar matters you've managed, this firm's communication is: Better / About the Same / Worse" |

Each question maps directly to one or more KPIs, and the assessment engine converts responses to the 0–100 normalized scale used by the scoring engine.

---

*This document is a design reference. KPI selection, weights, and assessment questions will be refined during solution design and validated with pilot customers.*
